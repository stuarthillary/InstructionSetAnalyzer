using Iced.Intel;

namespace InstructionSetAnalyzer;

internal static class LinuxCpuFlagTables
{
    internal static readonly IReadOnlyDictionary<CpuidFeature, string[]> FlagAliases =
        new Dictionary<CpuidFeature, string[]>
        {
            [CpuidFeature.AVX512_IFMA] = ["avx512ifma"],
            [CpuidFeature.AVX512_VBMI] = ["avx512vbmi"],
            [CpuidFeature.CMPXCHG16B] = ["cx16"],
            [CpuidFeature.CET_IBT] = ["ibt"],
            [CpuidFeature.CET_SS] = ["user_shstk"],
            [CpuidFeature.CLFSH] = ["clflush"],
            [CpuidFeature.CX8] = ["cx8"],
            [CpuidFeature.D3NOW] = ["3dnow"],
            [CpuidFeature.D3NOWEXT] = ["3dnowext"],
            [CpuidFeature.HLE_or_RTM] = ["hle", "rtm"],
            [CpuidFeature.LZCNT] = ["abm", "lzcnt"],
            [CpuidFeature.MONITORX] = ["mwaitx"],
            [CpuidFeature.PREFETCHW] = ["3dnowprefetch", "prefetchw"],
            [CpuidFeature.SGX1] = ["sgx"],
            [CpuidFeature.SHA] = ["sha_ni", "sha"],
            [CpuidFeature.SKINIT_or_SVM] = ["skinit", "svm"],
            [CpuidFeature.SSE3] = ["pni", "sse3"],
            [CpuidFeature.SYSCALL] = ["syscall"],
            [CpuidFeature.X64] = ["lm"]
        };

    // Features whose /proc/cpuinfo flag name is known. Anything absent from here and from FlagAliases is
    // reported as Indeterminate: the kernel either hides the flag (AMX_FP16, AVX_IFMA, SHA512, ...),
    // names it differently (TDX is tdx_guest) or has no userspace flag at all (UINTR, RDPMC).
    internal static readonly HashSet<CpuidFeature> KernelReported =
    [
        CpuidFeature.ADX,
        CpuidFeature.AES,
        CpuidFeature.AMX_BF16,
        CpuidFeature.AMX_INT8,
        CpuidFeature.AMX_TILE,
        CpuidFeature.AVX,
        CpuidFeature.AVX2,
        CpuidFeature.AVX512_4FMAPS,
        CpuidFeature.AVX512_4VNNIW,
        CpuidFeature.AVX512_BF16,
        CpuidFeature.AVX512_BITALG,
        CpuidFeature.AVX512_FP16,
        CpuidFeature.AVX512_IFMA,
        CpuidFeature.AVX512_VBMI,
        CpuidFeature.AVX512_VBMI2,
        CpuidFeature.AVX512_VNNI,
        CpuidFeature.AVX512_VP2INTERSECT,
        CpuidFeature.AVX512_VPOPCNTDQ,
        CpuidFeature.AVX512BW,
        CpuidFeature.AVX512CD,
        CpuidFeature.AVX512DQ,
        CpuidFeature.AVX512ER,
        CpuidFeature.AVX512F,
        CpuidFeature.AVX512PF,
        CpuidFeature.AVX512VL,
        CpuidFeature.AVX_VNNI,
        CpuidFeature.BMI1,
        CpuidFeature.BMI2,
        CpuidFeature.CLDEMOTE,
        CpuidFeature.CLFLUSHOPT,
        CpuidFeature.CLWB,
        CpuidFeature.CLZERO,
        CpuidFeature.CMOV,
        CpuidFeature.ENQCMD,
        CpuidFeature.F16C,
        CpuidFeature.FMA,
        CpuidFeature.FMA4,
        CpuidFeature.FSGSBASE,
        CpuidFeature.FXSR,
        CpuidFeature.GFNI,
        CpuidFeature.HLE,
        CpuidFeature.INVPCID,
        CpuidFeature.LWP,
        CpuidFeature.MMX,
        CpuidFeature.MONITOR,
        CpuidFeature.MONITORX,
        CpuidFeature.MOVBE,
        CpuidFeature.MOVDIR64B,
        CpuidFeature.MOVDIRI,
        CpuidFeature.MPX,
        CpuidFeature.MSR,
        CpuidFeature.PCLMULQDQ,
        CpuidFeature.PKU,
        CpuidFeature.POPCNT,
        CpuidFeature.RDPID,
        CpuidFeature.RDPRU,
        CpuidFeature.RDRAND,
        CpuidFeature.RDSEED,
        CpuidFeature.RDTSCP,
        CpuidFeature.RTM,
        CpuidFeature.SEP,
        CpuidFeature.SERIALIZE,
        CpuidFeature.SEV_ES,
        CpuidFeature.SEV_SNP,
        CpuidFeature.SGX1,
        CpuidFeature.SMAP,
        CpuidFeature.SMX,
        CpuidFeature.SSE,
        CpuidFeature.SSE2,
        CpuidFeature.SSE4_1,
        CpuidFeature.SSE4_2,
        CpuidFeature.SSE4A,
        CpuidFeature.SSSE3,
        CpuidFeature.SVM,
        CpuidFeature.TBM,
        CpuidFeature.TSC,
        CpuidFeature.TSXLDTRK,
        CpuidFeature.VAES,
        CpuidFeature.VMX,
        CpuidFeature.VPCLMULQDQ,
        CpuidFeature.WAITPKG,
        CpuidFeature.WBNOINVD,
        CpuidFeature.XOP,
        CpuidFeature.XSAVE,
        CpuidFeature.XSAVEC,
        CpuidFeature.XSAVEOPT,
        CpuidFeature.XSAVES
    ];

    internal static readonly HashSet<CpuidFeature> Architectural =
    [
        CpuidFeature.CPUID,
        CpuidFeature.FPU,
        CpuidFeature.FPU287,
        CpuidFeature.FPU387,
        CpuidFeature.INTEL186,
        CpuidFeature.INTEL286,
        CpuidFeature.INTEL386,
        CpuidFeature.INTEL486,
        CpuidFeature.INTEL8086,
        CpuidFeature.MULTIBYTENOP,
        CpuidFeature.PAUSE
    ];

    internal static readonly HashSet<CpuidFeature> AmxFeatures =
    [
        CpuidFeature.AMX_BF16,
        CpuidFeature.AMX_COMPLEX,
        CpuidFeature.AMX_FP16,
        CpuidFeature.AMX_INT8,
        CpuidFeature.AMX_TILE
    ];

    /// <summary>
    /// Features the kernel does not print in /proc/cpuinfo but that CPUID reports reliably, with what the OS
    /// must additionally have enabled. Bit positions are from the Intel SDM, cross-checked against gcc's
    /// cpuid.h and the kernel's cpufeatures.h. UINTR, RDPMC and TDX are deliberately absent: CPUID only shows
    /// hardware capability and the OS must also enable them.
    /// </summary>
    internal static readonly IReadOnlyDictionary<CpuidFeature, CpuidOnlyRule> CpuidOnlyFeatures =
        new Dictionary<CpuidFeature, CpuidOnlyRule>
        {
            [CpuidFeature.AMX_COMPLEX] = new(new CpuidBit(7, 1, CpuidRegister.Eax, 8), OsRequirement.AmxState),
            [CpuidFeature.AMX_FP16] = new(new CpuidBit(7, 1, CpuidRegister.Eax, 21), OsRequirement.AmxState),
            [CpuidFeature.AVX_IFMA] = new(new CpuidBit(7, 1, CpuidRegister.Eax, 23), OsRequirement.AvxState),
            [CpuidFeature.AVX_NE_CONVERT] = new(new CpuidBit(7, 1, CpuidRegister.Edx, 5), OsRequirement.AvxState),
            [CpuidFeature.AVX_VNNI_INT16] = new(new CpuidBit(7, 1, CpuidRegister.Edx, 10), OsRequirement.AvxState),
            [CpuidFeature.AVX_VNNI_INT8] = new(new CpuidBit(7, 1, CpuidRegister.Edx, 4), OsRequirement.AvxState),
            [CpuidFeature.PREFETCHWT1] = new(new CpuidBit(7, 0, CpuidRegister.Ecx, 0), OsRequirement.None),
            [CpuidFeature.SHA512] = new(new CpuidBit(7, 1, CpuidRegister.Eax, 0), OsRequirement.AvxState)
        };

    /// <summary>The /proc/cpuinfo flag names that indicate the feature; any one of them is enough.</summary>
    internal static IReadOnlyList<string> GetCandidateFlags(CpuidFeature feature) =>
        FlagAliases.TryGetValue(feature, out var aliases)
            ? aliases
            : [feature.ToString().ToLowerInvariant()];

    internal static bool HasKnownFlagMapping(CpuidFeature feature) =>
        FlagAliases.ContainsKey(feature) || KernelReported.Contains(feature);
}

/// <summary>What the operating system must have enabled for a CPUID-reported feature to be usable.</summary>
internal enum OsRequirement
{
    None,

    /// <summary>YMM register state: Linux reports it as the "avx" flag.</summary>
    AvxState,

    /// <summary>AMX tile state: checked through the AMX_TILE flag and Linux XSTATE support.</summary>
    AmxState
}

internal readonly record struct CpuidOnlyRule(CpuidBit Bit, OsRequirement OsRequirement);
