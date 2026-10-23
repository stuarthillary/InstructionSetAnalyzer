using System.CommandLine;
using System.CommandLine.Invocation;
using Iced.Intel;

namespace InstructionSetAnalyzer;

internal sealed record AnalyzeRequest(
    string Path,
    bool CheckHost = false,
    bool IncludePrivileged = false,
    bool Verbose = false,
    bool Detail = false,
    bool Symbols = false,
    bool ExcludeDispatched = false,
    BaselineLevel? Baseline = null,
    bool Json = false,
    int Jobs = 1);

internal sealed class CliApplication
{
    private const int CancelledExitCode = 130;

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly IHostFeatureChecker _hostChecker;
    private CancellationToken _cancellationToken;

    public CliApplication(TextWriter output, TextWriter error, IHostFeatureChecker hostChecker)
    {
        _output = output;
        _error = error;
        _hostChecker = hostChecker;
    }

    /// <summary>Runs the command line. Cancelling the token stops a running analysis and exits with 130.</summary>
    public int Run(string[] args, CancellationToken cancellationToken = default)
    {
        _cancellationToken = cancellationToken;
        return BuildRootCommand().Parse(args).Invoke(new InvocationConfiguration { Output = _output, Error = _error });
    }

    private RootCommand BuildRootCommand()
    {
        var rootCommand = new RootCommand("Analyze x86 ELF binaries and check Linux host CPU compatibility.");

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Write one JSON document to standard output instead of text. Warnings and errors still go to " +
                          "standard error."
        };

        var pathArgument = new Argument<string>("path")
        {
            Description = "An ELF file or a directory to scan recursively."
        };
        var checkHostOption = new Option<bool>("--check-host")
        {
            Description = "Check whether this Linux host supports every detected feature."
        };
        var includePrivilegedOption = new Option<bool>("--include-privileged")
        {
            Description = "Also report features whose instructions only run in ring 0 (such as VMX or MSR). " +
                          "By default these are hidden because user-space binaries only contain them as misdecoded data."
        };
        var verboseOption = new Option<bool>("--verbose", "-v")
        {
            Description = "List every file as it is analyzed and every file skipped."
        };
        var detailOption = new Option<bool>("--detail")
        {
            Description = "Show, for each feature, how many instructions use it and in which files."
        };
        var symbolsOption = new Option<bool>("--symbols")
        {
            Description = "With the breakdown, show which functions use each feature and which are ifunc variants. " +
                          "Needs a symbol table; stripped code is reported as having no symbol."
        };
        var excludeDispatchedOption = new Option<bool>("--exclude-dispatched")
        {
            Description = "Leave features used only inside ifunc-dispatched variants out of the baseline and host " +
                          "checks, because those variants only run on CPUs that support them. Needs a symbol table."
        };
        var baselineOption = new Option<string?>("--baseline")
        {
            Description = "Fail (exit code 2) if the binaries need more than this x86-64 microarchitecture level."
        };
        baselineOption.AcceptOnlyFromAmong(Baseline.AllNames.ToArray());
        var jobsOption = new Option<int>("--jobs", "-j")
        {
            Description = "How many files to analyze in parallel when scanning a directory.",
            DefaultValueFactory = _ => Environment.ProcessorCount
        };
        jobsOption.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int>() < 1)
            {
                result.AddError("--jobs must be at least 1.");
            }
        });

        var analyzeCommand = new Command("analyze", "Find CPU features used by an ELF file or directory.");
        analyzeCommand.Arguments.Add(pathArgument);
        foreach (var option in new Option[]
                 {
                     checkHostOption, includePrivilegedOption, verboseOption, detailOption, symbolsOption,
                     excludeDispatchedOption, baselineOption, jobsOption, jsonOption
                 })
        {
            analyzeCommand.Options.Add(option);
        }

        analyzeCommand.SetAction(parseResult => Analyze(new AnalyzeRequest(
            parseResult.GetValue(pathArgument)!,
            parseResult.GetValue(checkHostOption),
            parseResult.GetValue(includePrivilegedOption),
            parseResult.GetValue(verboseOption),
            parseResult.GetValue(detailOption),
            parseResult.GetValue(symbolsOption),
            parseResult.GetValue(excludeDispatchedOption),
            Baseline.TryParse(parseResult.GetValue(baselineOption), out var level) ? level : null,
            parseResult.GetValue(jsonOption),
            parseResult.GetValue(jobsOption))));

        var featuresArgument = new Argument<string[]>("features")
        {
            Description = "Required Iced CPUID feature names. Values can be separated by spaces or commas.",
            Arity = ArgumentArity.OneOrMore
        };
        var checkHostCommand = new Command("check-host", "Check specified CPU features against this Linux host.");
        checkHostCommand.Arguments.Add(featuresArgument);
        var hostJsonOption = new Option<bool>("--json")
        {
            Description = jsonOption.Description
        };
        checkHostCommand.Options.Add(hostJsonOption);
        checkHostCommand.SetAction(parseResult =>
            CheckHostNames(parseResult.GetValue(featuresArgument)!, parseResult.GetValue(hostJsonOption)));

        rootCommand.Subcommands.Add(analyzeCommand);
        rootCommand.Subcommands.Add(checkHostCommand);
        return rootCommand;
    }

    internal int Analyze(AnalyzeRequest request)
    {
        var path = Path.GetFullPath(request.Path);
        var analyzer = new ElfInstructionSetAnalyzer(
            request.IncludePrivileged,
            collectSymbols: request.Symbols || request.ExcludeDispatched);

        try
        {
            ScanResult scan;
            bool isDirectory;
            if (File.Exists(path))
            {
                scan = new ScanResult([new AnalyzedFile(path, analyzer.Analyze(path, _cancellationToken))], [], []);
                isDirectory = false;
            }
            else if (Directory.Exists(path))
            {
                scan = DirectoryScanner.Scan(path, analyzer, request.Jobs, _cancellationToken);
                isDirectory = true;
                WriteScanWarnings(scan, request.Verbose);
            }
            else
            {
                _error.WriteLine($"Path does not exist: {path}");
                return 1;
            }

            var options = new ReportOptions(request.Detail, request.Symbols, request.ExcludeDispatched, request.Verbose);
            var baseline = request.Baseline is { } level
                ? BaselineReport.Evaluate(scan, level, request.ExcludeDispatched)
                : null;

            if (isDirectory && scan.Analyzed.Count == 0)
            {
                _error.WriteLine("No supported x86 or x86-64 ELF binaries were found.");
                if (request.Json)
                {
                    JsonReportWriter.Write(new AnalysisReport(path, isDirectory, scan, options, baseline, null), _output);
                }

                return 1;
            }

            string? hostError = null;
            HostCompatibilityReport? host = null;
            if (request.CheckHost)
            {
                try
                {
                    host = _hostChecker.Check(scan.RequiredFeatures(request.ExcludeDispatched));
                }
                catch (Exception exception) when (
                    exception is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
                {
                    hostError = exception.Message;
                }
            }

            var report = new AnalysisReport(path, isDirectory, scan, options, baseline, host);
            if (request.Json)
            {
                JsonReportWriter.Write(report, _output);
            }
            else
            {
                TextReportWriter.Write(report, _output);
            }

            if (hostError is not null)
            {
                _error.WriteLine($"Error: {hostError}");
                return 1;
            }

            if (scan.Failed.Count > 0)
            {
                _error.WriteLine(
                    $"Error: {scan.Failed.Count} ELF file(s) could not be analyzed, so the feature list may be incomplete.");
            }

            return report.ExitCode;
        }
        catch (OperationCanceledException)
        {
            _error.WriteLine("Analysis cancelled.");
            return CancelledExitCode;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private void WriteScanWarnings(ScanResult scan, bool verbose)
    {
        foreach (var file in scan.Failed)
        {
            _error.WriteLine($"Warning: could not analyze {file.Path}: {file.Message}");
        }

        if (!verbose)
        {
            return;
        }

        foreach (var file in scan.Skipped)
        {
            _error.WriteLine($"Warning: skipped {file.Path}: {file.Message}");
        }
    }

    private int CheckHostNames(IEnumerable<string> values, bool json)
    {
        try
        {
            var report = _hostChecker.Check(FeatureParser.Parse(values));
            if (json)
            {
                JsonReportWriter.WriteHost(report, _output);
            }
            else
            {
                HostReportWriter.Write(report, _output);
            }

            return report.IsCompatible ? 0 : 2;
        }
        catch (ArgumentException exception)
        {
            _error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
        catch (Exception exception) when (
            exception is PlatformNotSupportedException or IOException or UnauthorizedAccessException)
        {
            _error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }
}
