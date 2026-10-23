using System.CommandLine;
using Iced.Intel;

namespace InstructionSetAnalyzer;

internal class Program
{
    private static int Main(string[] args)
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

        return rootCommand.Parse(args).Invoke();

        static int Analyze(string inputPath, bool checkHost)
        {
            var path = Path.GetFullPath(inputPath);
            var analyzer = new ElfInstructionSetAnalyzer();

            try
            {
                IReadOnlySet<CpuidFeature> features;
                if (File.Exists(path))
                {
                    var result = analyzer.Analyze(path);
                    Console.WriteLine(
                        $"Binary {path} ({result.Architecture}, {result.ExecutableSectionCount} executable sections)");
                    features = result.Features;
                }
                else if (Directory.Exists(path))
                {
                    var result = AnalyzeDirectory(path, analyzer);
                    Console.WriteLine($"Directory {path}");
                    Console.WriteLine($"Analyzed ELF binaries: {result.AnalyzedCount}");
                    features = result.Features;

                    if (result.AnalyzedCount == 0)
                    {
                        Console.Error.WriteLine("No supported x86 or x86-64 ELF binaries were found.");
                        return 1;
                    }
                }
                else
                {
                    Console.Error.WriteLine($"Path does not exist: {path}");
                    return 1;
                }

                PrintFeatures(features);
                return checkHost ? CheckHostFeatures(features) : 0;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                Console.Error.WriteLine($"Error: {exception.Message}");
                return 1;
            }
        }

        static DirectoryAnalysisResult AnalyzeDirectory(string path, ElfInstructionSetAnalyzer analyzer)
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
                    Console.WriteLine($"Analyzing {file}");
                    var result = analyzer.Analyze(file);
                    features.UnionWith(result.Features);
                    analyzedCount++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
                {
                    Console.Error.WriteLine($"Warning: could not analyze {file}: {exception.Message}");
                }
            }

            Console.WriteLine();
            return new DirectoryAnalysisResult(analyzedCount, features);
        }

        static int CheckHostNames(IEnumerable<string> values)
        {
            try
            {
                return CheckHostFeatures(FeatureParser.Parse(values));
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine($"Error: {exception.Message}");
                return 1;
            }
        }

        static int CheckHostFeatures(IEnumerable<CpuidFeature> features)
        {
            try
            {
                var report = new LinuxHostFeatureChecker().Check(features);
                HostReportWriter.Write(report, Console.Out);
                return report.IsCompatible ? 0 : 2;
            }
            catch (Exception exception) when (
                exception is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Error: {exception.Message}");
                return 1;
            }
        }

        static void PrintFeatures(IEnumerable<CpuidFeature> features)
        {
            Console.WriteLine("CPU features used:");
            foreach (var feature in features.OrderBy(feature => feature.ToString(), StringComparer.Ordinal))
            {
                Console.WriteLine($"  {feature}");
            }
        }
    }
}

internal sealed record DirectoryAnalysisResult(
    int AnalyzedCount,
    IReadOnlySet<CpuidFeature> Features);
