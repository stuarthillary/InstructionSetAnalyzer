using System.CommandLine;
using System.CommandLine.Invocation;
using Iced.Intel;

namespace InstructionSetAnalyzer;

internal sealed class CliApplication
{
    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly IHostFeatureChecker _hostChecker;

    public CliApplication(TextWriter output, TextWriter error, IHostFeatureChecker hostChecker)
    {
        _output = output;
        _error = error;
        _hostChecker = hostChecker;
    }

    public int Run(string[] args) =>
        BuildRootCommand().Parse(args).Invoke(new InvocationConfiguration { Output = _output, Error = _error });

    private RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Analyze x86 ELF binaries and check Linux host CPU compatibility.");

        var pathArgument = new Argument<string>("path")
        {
            Description = "An ELF file or a directory to scan recursively."
        };
        var checkHostOption = new Option<bool>("--check-host")
        {
            Description = "Check whether this Linux host supports every detected feature."
        };
        var analyzeCommand = new Command("analyze", "Find CPU features used by an ELF file or directory.");
        analyzeCommand.Arguments.Add(pathArgument);
        analyzeCommand.Options.Add(checkHostOption);
        analyzeCommand.SetAction(parseResult =>
            Analyze(parseResult.GetValue(pathArgument)!, parseResult.GetValue(checkHostOption)));

        var featuresArgument = new Argument<string[]>("features")
        {
            Description = "Required Iced CPUID feature names. Values can be separated by spaces or commas.",
            Arity = ArgumentArity.OneOrMore
        };
        var checkHostCommand = new Command("check-host", "Check specified CPU features against this Linux host.");
        checkHostCommand.Arguments.Add(featuresArgument);
        checkHostCommand.SetAction(parseResult => CheckHostNames(parseResult.GetValue(featuresArgument)!));

        rootCommand.Subcommands.Add(analyzeCommand);
        rootCommand.Subcommands.Add(checkHostCommand);
        return rootCommand;
    }

    internal int Analyze(string inputPath, bool checkHost)
    {
        var path = Path.GetFullPath(inputPath);
        var analyzer = new ElfInstructionSetAnalyzer();

        try
        {
            IReadOnlySet<CpuidFeature> features;
            if (File.Exists(path))
            {
                var result = analyzer.Analyze(path);
                _output.WriteLine(
                    $"Binary {path} ({result.Architecture}, {result.ExecutableSectionCount} executable sections)");
                features = result.Features;
            }
            else if (Directory.Exists(path))
            {
                var result = AnalyzeDirectory(path, analyzer);
                _output.WriteLine($"Directory {path}");
                _output.WriteLine($"Analyzed ELF binaries: {result.AnalyzedCount}");
                features = result.Features;

                if (result.AnalyzedCount == 0)
                {
                    _error.WriteLine("No supported x86 or x86-64 ELF binaries were found.");
                    return 1;
                }
            }
            else
            {
                _error.WriteLine($"Path does not exist: {path}");
                return 1;
            }

            PrintFeatures(features);
            return checkHost ? CheckHostFeatures(features) : 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private DirectoryAnalysisResult AnalyzeDirectory(string path, ElfInstructionSetAnalyzer analyzer)
    {
        var features = new HashSet<CpuidFeature>();
        var analyzedCount = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in Directory.EnumerateFiles(path, "*", options))
        {
            if (!ElfInstructionSetAnalyzer.IsElf(file))
            {
                continue;
            }

            try
            {
                _output.WriteLine($"Analyzing {file}");
                var result = analyzer.Analyze(file);
                features.UnionWith(result.Features);
                analyzedCount++;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                _error.WriteLine($"Warning: could not analyze {file}: {exception.Message}");
            }
        }

        _output.WriteLine();
        return new DirectoryAnalysisResult(analyzedCount, features);
    }

    private int CheckHostNames(IEnumerable<string> values)
    {
        try
        {
            return CheckHostFeatures(FeatureParser.Parse(values));
        }
        catch (ArgumentException exception)
        {
            _error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private int CheckHostFeatures(IEnumerable<CpuidFeature> features)
    {
        try
        {
            var report = _hostChecker.Check(features);
            HostReportWriter.Write(report, _output);
            return report.IsCompatible ? 0 : 2;
        }
        catch (Exception exception) when (
            exception is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            _error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private void PrintFeatures(IEnumerable<CpuidFeature> features)
    {
        _output.WriteLine("CPU features used:");
        foreach (var feature in features.OrderBy(feature => feature.ToString(), StringComparer.Ordinal))
        {
            _output.WriteLine($"  {feature}");
        }
    }
}
