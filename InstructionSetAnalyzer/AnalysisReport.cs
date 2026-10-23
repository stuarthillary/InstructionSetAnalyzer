using Iced.Intel;

namespace InstructionSetAnalyzer;

/// <summary>How the analyzed binaries compare with an x86-64 microarchitecture level.</summary>
internal sealed record BaselineReport(
    BaselineLevel Level,
    IReadOnlySet<CpuidFeature> Exceeding,
    IReadOnlyList<(string Path, IReadOnlySet<CpuidFeature> Exceeding)> Files)
{
    public bool IsMet => Exceeding.Count == 0;

    public static BaselineReport Evaluate(ScanResult scan, BaselineLevel level, bool excludeDispatched)
    {
        var files = scan.Analyzed
            .Select(file => (
                file.Path,
                Exceeding: Baseline.Exceeding(ScanResult.RequiredFeatures(file.Result, excludeDispatched), level)))
            .Where(file => file.Exceeding.Count > 0)
            .ToArray();
        var exceeding = Baseline.Exceeding(scan.RequiredFeatures(excludeDispatched), level);
        return new BaselineReport(level, exceeding, files);
    }
}

internal sealed record ReportOptions(bool Detail, bool Symbols, bool ExcludeDispatched, bool Verbose);

/// <summary>Everything an analysis produced, ready to be written as text or JSON.</summary>
internal sealed record AnalysisReport(
    string Path,
    bool IsDirectory,
    ScanResult Scan,
    ReportOptions Options,
    BaselineReport? Baseline,
    HostCompatibilityReport? Host)
{
    /// <summary>The features that count towards baseline and host checks.</summary>
    public IReadOnlySet<CpuidFeature> RequiredFeatures => Scan.RequiredFeatures(Options.ExcludeDispatched);

    public IReadOnlySet<CpuidFeature> DispatchedOnlyFeatures => Scan.DispatchedOnlyFeatures();

    /// <summary>The exit code: 2 when a check failed, 1 when files could not be analyzed, otherwise 0.</summary>
    public int ExitCode
    {
        get
        {
            if (Baseline is { IsMet: false } || Host is { IsCompatible: false })
            {
                return 2;
            }

            return Scan.Failed.Count > 0 ? 1 : 0;
        }
    }
}
