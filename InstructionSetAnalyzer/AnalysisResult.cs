using Iced.Intel;

namespace InstructionSetAnalyzer;

/// <summary>Where the analyzed machine code was found in the ELF file.</summary>
public enum CodeSource
{
    /// <summary>Executable section headers (the normal case).</summary>
    Sections,

    /// <summary>Executable PT_LOAD segments, used when the file has no executable sections.</summary>
    Segments
}

public sealed record AnalysisResult(
    string Architecture,
    int ExecutableRegionCount,
    CodeSource Source,
    IReadOnlySet<CpuidFeature> Features);

internal sealed record DirectoryAnalysisResult(
    int AnalyzedCount,
    int SkippedCount,
    int FailedCount,
    IReadOnlySet<CpuidFeature> Features);
