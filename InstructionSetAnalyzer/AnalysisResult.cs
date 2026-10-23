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

/// <summary>A function that contains instructions requiring a CPU feature.</summary>
/// <param name="Name">Symbol name without version suffix.</param>
/// <param name="Address">Virtual address of the function.</param>
/// <param name="InstructionCount">How many of the feature's instructions are inside this function.</param>
/// <param name="DispatchedBy">
/// Names of the ifunc resolvers that select this function at run time. Empty when the function is not a
/// confirmed ifunc variant.
/// </param>
public sealed record SymbolUsage(
    string Name,
    ulong Address,
    long InstructionCount,
    IReadOnlyList<string> DispatchedBy);

/// <summary>How one CPU feature is used in one binary.</summary>
/// <param name="InstructionCount">Instructions that require the feature.</param>
/// <param name="DispatchedInstructionCount">
/// Those instructions that sit inside confirmed ifunc variants, so only run on CPUs the resolver selected them for.
/// </param>
/// <param name="UnattributedInstructionCount">
/// Instructions not inside any known function (stripped code). Always 0 when symbols were not collected.
/// </param>
/// <param name="Symbols">The functions using the feature, busiest first (capped).</param>
public sealed record FeatureUsage(
    long InstructionCount,
    long DispatchedInstructionCount,
    long UnattributedInstructionCount,
    IReadOnlyList<SymbolUsage> Symbols)
{
    /// <summary>True when every instruction using the feature is inside a confirmed ifunc variant.</summary>
    public bool IsDispatchedOnly => InstructionCount > 0 && DispatchedInstructionCount == InstructionCount;
}

public sealed record AnalysisResult(
    string Architecture,
    int ExecutableRegionCount,
    CodeSource Source,
    bool SymbolsCollected,
    IReadOnlyDictionary<CpuidFeature, FeatureUsage> Usage)
{
    /// <summary>Every feature the binary uses, including those used only by ifunc variants.</summary>
    public IReadOnlySet<CpuidFeature> Features { get; } = Usage.Keys.ToHashSet();

    /// <summary>Features whose every use is inside a confirmed ifunc variant.</summary>
    public IReadOnlySet<CpuidFeature> DispatchedOnlyFeatures { get; } =
        Usage.Where(pair => pair.Value.IsDispatchedOnly).Select(pair => pair.Key).ToHashSet();
}
