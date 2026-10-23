using Iced.Intel;

namespace InstructionSetAnalyzer;

/// <summary>The x86-64 microarchitecture levels defined by the x86-64 psABI.</summary>
public enum BaselineLevel
{
    X86_64,
    X86_64_V2,
    X86_64_V3,
    X86_64_V4
}

public static class Baseline
{
    private static readonly IReadOnlyDictionary<BaselineLevel, string> Names = new Dictionary<BaselineLevel, string>
    {
        [BaselineLevel.X86_64] = "x86-64",
        [BaselineLevel.X86_64_V2] = "x86-64-v2",
        [BaselineLevel.X86_64_V3] = "x86-64-v3",
        [BaselineLevel.X86_64_V4] = "x86-64-v4"
    };

    // Features that never raise the requirement, whatever the level: the 64-bit architecture itself, the legacy
    // instruction set levels every x86-64 CPU implements, and endbr32/endbr64, which are encoded as NOPs on CPUs
    // without CET indirect branch tracking. CET_SS is deliberately not here: incssp, wrss and similar
    // instructions fault on CPUs without shadow stacks.
    private static readonly CpuidFeature[] AlwaysAllowed =
    [
        CpuidFeature.X64,
        CpuidFeature.CPUID,
        CpuidFeature.TSC,
        CpuidFeature.PAUSE,
        CpuidFeature.MULTIBYTENOP,
        CpuidFeature.INTEL8086,
        CpuidFeature.INTEL186,
        CpuidFeature.INTEL286,
        CpuidFeature.INTEL386,
        CpuidFeature.INTEL486,
        CpuidFeature.FPU287,
        CpuidFeature.FPU387,
        CpuidFeature.CET_IBT
    ];

    // What each level adds to the previous one. LAHF/SAHF belong to v2 in the psABI but Iced does not report
    // them as a CPUID feature, so a use of them cannot be detected.
    private static readonly IReadOnlyDictionary<BaselineLevel, CpuidFeature[]> Additions =
        new Dictionary<BaselineLevel, CpuidFeature[]>
        {
            [BaselineLevel.X86_64] =
            [
                CpuidFeature.CMOV, CpuidFeature.CX8, CpuidFeature.FPU, CpuidFeature.FXSR, CpuidFeature.MMX,
                CpuidFeature.SSE, CpuidFeature.SSE2, CpuidFeature.SYSCALL
            ],
            [BaselineLevel.X86_64_V2] =
            [
                CpuidFeature.CMPXCHG16B, CpuidFeature.POPCNT, CpuidFeature.SSE3, CpuidFeature.SSE4_1,
                CpuidFeature.SSE4_2, CpuidFeature.SSSE3
            ],
            [BaselineLevel.X86_64_V3] =
            [
                CpuidFeature.AVX, CpuidFeature.AVX2, CpuidFeature.BMI1, CpuidFeature.BMI2, CpuidFeature.F16C,
                CpuidFeature.FMA, CpuidFeature.LZCNT, CpuidFeature.MOVBE, CpuidFeature.XSAVE
            ],
            [BaselineLevel.X86_64_V4] =
            [
                CpuidFeature.AVX512BW, CpuidFeature.AVX512CD, CpuidFeature.AVX512DQ, CpuidFeature.AVX512F,
                CpuidFeature.AVX512VL
            ]
        };

    /// <summary>The names accepted on the command line, which match gcc's -march values.</summary>
    public static IReadOnlyList<string> AllNames { get; } = Names.Values.ToArray();

    public static string Format(BaselineLevel level) => Names[level];

    public static bool TryParse(string? name, out BaselineLevel level)
    {
        foreach (var (candidate, candidateName) in Names)
        {
            if (string.Equals(name, candidateName, StringComparison.OrdinalIgnoreCase))
            {
                level = candidate;
                return true;
            }
        }

        level = default;
        return false;
    }

    /// <summary>Every feature a CPU of the level (or older) can be assumed to have.</summary>
    public static IReadOnlySet<CpuidFeature> Allowed(BaselineLevel level)
    {
        var allowed = new HashSet<CpuidFeature>(AlwaysAllowed);
        foreach (var (candidate, features) in Additions)
        {
            if (candidate <= level)
            {
                allowed.UnionWith(features);
            }
        }

        return allowed;
    }

    /// <summary>The features that need a newer CPU than the baseline guarantees.</summary>
    public static IReadOnlySet<CpuidFeature> Exceeding(IEnumerable<CpuidFeature> features, BaselineLevel level)
    {
        var allowed = Allowed(level);
        return features.Where(feature => !allowed.Contains(feature)).ToHashSet();
    }
}
