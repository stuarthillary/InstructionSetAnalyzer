using Iced.Intel;

namespace InstructionSetAnalyzer;

public sealed record AnalysisResult(
    string Architecture,
    int ExecutableSectionCount,
    IReadOnlySet<CpuidFeature> Features);

internal sealed record DirectoryAnalysisResult(
    int AnalyzedCount,
    IReadOnlySet<CpuidFeature> Features);
