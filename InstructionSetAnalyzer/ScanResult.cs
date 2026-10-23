using Iced.Intel;

namespace InstructionSetAnalyzer;

public enum SkipReason
{
    UnsupportedArchitecture,
    NoExecutableCode
}

public sealed record AnalyzedFile(string Path, AnalysisResult Result);

public sealed record SkippedFile(string Path, SkipReason Reason, string Message);

public sealed record FailedFile(string Path, string Message);

/// <summary>The outcome of analyzing one file or a whole directory tree, with files sorted by path.</summary>
public sealed class ScanResult
{
    public ScanResult(
        IEnumerable<AnalyzedFile> analyzed,
        IEnumerable<SkippedFile> skipped,
        IEnumerable<FailedFile> failed)
    {
        Analyzed = analyzed.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        Skipped = skipped.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        Failed = failed.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<AnalyzedFile> Analyzed { get; }

    public IReadOnlyList<SkippedFile> Skipped { get; }

    public IReadOnlyList<FailedFile> Failed { get; }

    /// <summary>Every feature any analyzed file uses.</summary>
    public IReadOnlySet<CpuidFeature> AllFeatures() => RequiredFeatures(excludeDispatched: false);

    /// <summary>
    /// The features the binaries need on every CPU. With <paramref name="excludeDispatched"/>, features a file
    /// uses only inside confirmed ifunc variants are left out, because those variants only run where supported.
    /// </summary>
    public IReadOnlySet<CpuidFeature> RequiredFeatures(bool excludeDispatched) =>
        Analyzed.SelectMany(file => RequiredFeatures(file.Result, excludeDispatched)).ToHashSet();

    /// <summary>Features that appear, but never outside confirmed ifunc variants.</summary>
    public IReadOnlySet<CpuidFeature> DispatchedOnlyFeatures()
    {
        var required = RequiredFeatures(excludeDispatched: true);
        return AllFeatures().Where(feature => !required.Contains(feature)).ToHashSet();
    }

    public static IEnumerable<CpuidFeature> RequiredFeatures(AnalysisResult result, bool excludeDispatched) =>
        excludeDispatched
            ? result.Features.Where(feature => !result.DispatchedOnlyFeatures.Contains(feature))
            : result.Features;
}
