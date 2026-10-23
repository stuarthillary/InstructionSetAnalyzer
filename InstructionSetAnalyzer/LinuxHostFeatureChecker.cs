using System.Runtime.InteropServices;
using Iced.Intel;

namespace InstructionSetAnalyzer;

public sealed class LinuxHostFeatureChecker
{
    private const int ArchGetXcompSupp = 0x1021;
    private const int ArchGetXcompPerm = 0x1022;
    private const int XfeatureTileConfig = 17;
    private const int XfeatureTileData = 18;

    private static readonly IReadOnlyDictionary<CpuidFeature, string[]> LinuxFlagAliases =
        new Dictionary<CpuidFeature, string[]>
        {
            [CpuidFeature.CMPXCHG16B] = ["cx16"],
            [CpuidFeature.CET_IBT] = ["ibt"],
            [CpuidFeature.CET_SS] = ["user_shstk"],
            [CpuidFeature.CLFSH] = ["clflush"],
            [CpuidFeature.CX8] = ["cx8"],
            [CpuidFeature.D3NOW] = ["3dnow"],
            [CpuidFeature.D3NOWEXT] = ["3dnowext"],
            [CpuidFeature.HLE_or_RTM] = ["hle", "rtm"],
            [CpuidFeature.LZCNT] = ["abm", "lzcnt"],
            [CpuidFeature.PREFETCHW] = ["3dnowprefetch", "prefetchw"],
            [CpuidFeature.SGX1] = ["sgx"],
            [CpuidFeature.SHA] = ["sha_ni", "sha"],
            [CpuidFeature.SKINIT_or_SVM] = ["skinit", "svm"],
            [CpuidFeature.SSE3] = ["pni", "sse3"],
            [CpuidFeature.SYSCALL] = ["syscall"],
            [CpuidFeature.X64] = ["lm"]
        };

    private static readonly HashSet<CpuidFeature> KernelReportedFeatures =
    [
        CpuidFeature.ADX,
        CpuidFeature.AES,
        CpuidFeature.AMX_BF16,
        CpuidFeature.AMX_COMPLEX,
        CpuidFeature.AMX_FP16,
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
        CpuidFeature.AVX_IFMA,
        CpuidFeature.AVX_NE_CONVERT,
        CpuidFeature.AVX_VNNI,
        CpuidFeature.AVX_VNNI_INT8,
        CpuidFeature.AVX_VNNI_INT16,
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
        CpuidFeature.PREFETCHWT1,
        CpuidFeature.RDPID,
        CpuidFeature.RDPMC,
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
        CpuidFeature.SHA512,
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
        CpuidFeature.TDX,
        CpuidFeature.TSC,
        CpuidFeature.TSXLDTRK,
        CpuidFeature.UINTR,
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

    private static readonly HashSet<CpuidFeature> ArchitecturalFeatures =
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

    public HostCompatibilityReport Check(IEnumerable<CpuidFeature> requiredFeatures)
    {
        ArgumentNullException.ThrowIfNull(requiredFeatures);
        EnsureSupportedPlatform();

        var flags = ReadCommonCpuFlags();
        var xstate = GetSupportedXstate();
        var xstatePermissions = GetXstatePermissions();
        var results = requiredFeatures
            .Distinct()
            .OrderBy(feature => feature.ToString(), StringComparer.Ordinal)
            .Select(feature => CheckFeature(feature, flags, xstate, xstatePermissions))
            .ToArray();

        return new HostCompatibilityReport(
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            results);
    }

    private static HostFeatureResult CheckFeature(
        CpuidFeature feature,
        IReadOnlySet<string> flags,
        ulong? supportedXstate,
        ulong? xstatePermissions)
    {
        if (ArchitecturalFeatures.Contains(feature))
        {
            return new HostFeatureResult(feature, HostFeatureStatus.Supported, "x86 architectural feature");
        }

        var aliases = LinuxFlagAliases.TryGetValue(feature, out var configuredAliases)
            ? configuredAliases
            : [feature.ToString().ToLowerInvariant()];

        var hasRequiredFlags = feature == CpuidFeature.PKU
            ? flags.Contains("pku") && flags.Contains("ospke")
            : aliases.Any(flags.Contains);
        if (!hasRequiredFlags)
        {
            var mappingIsKnown =
                LinuxFlagAliases.ContainsKey(feature) ||
                KernelReportedFeatures.Contains(feature);
            return new HostFeatureResult(
                feature,
                mappingIsKnown ? HostFeatureStatus.Unsupported : HostFeatureStatus.Indeterminate,
                mappingIsKnown
                    ? feature == CpuidFeature.PKU
                        ? "Linux CPU flags pku and ospke are both required"
                        : $"missing Linux CPU flag ({string.Join(" or ", aliases)})"
                    : "no reliable Linux userspace capability mapping is available");
        }

        if (IsAmxFeature(feature))
        {
            if (!flags.Contains("amx_tile"))
            {
                return new HostFeatureResult(
                    feature,
                    HostFeatureStatus.Unsupported,
                    "AMX_TILE is required by every AMX instruction");
            }

            const ulong requiredAmxState = (1UL << XfeatureTileConfig) | (1UL << XfeatureTileData);
            if (supportedXstate is null)
            {
                return new HostFeatureResult(
                    feature,
                    HostFeatureStatus.Indeterminate,
                    "CPU flag is present, but Linux AMX XSTATE support could not be queried");
            }

            if ((supportedXstate.Value & requiredAmxState) != requiredAmxState)
            {
                return new HostFeatureResult(
                    feature,
                    HostFeatureStatus.Unsupported,
                    "Linux does not support the required AMX tile XSTATE components");
            }

            if (xstatePermissions is null)
            {
                return new HostFeatureResult(
                    feature,
                    HostFeatureStatus.Indeterminate,
                    "Linux AMX process permission could not be queried");
            }

            if ((xstatePermissions.Value & (1UL << XfeatureTileData)) == 0)
            {
                return new HostFeatureResult(
                    feature,
                    HostFeatureStatus.PermissionRequired,
                    "host supports AMX, but this process has not requested tile-data permission");
            }

            return new HostFeatureResult(
                feature,
                HostFeatureStatus.Supported,
                "Linux AMX tile-data permission is active for this process");
        }

        return new HostFeatureResult(feature, HostFeatureStatus.Supported, "Linux CPU flag is enabled");
    }

    private static bool IsAmxFeature(CpuidFeature feature) =>
        feature.ToString().StartsWith("AMX_", StringComparison.Ordinal);

    private static HashSet<string> ReadCommonCpuFlags()
    {
        const string cpuInfoPath = "/proc/cpuinfo";
        if (!File.Exists(cpuInfoPath))
        {
            throw new PlatformNotSupportedException("Linux /proc/cpuinfo is unavailable.");
        }

        var processorFlags = File.ReadLines(cpuInfoPath)
            .Where(line => line.StartsWith("flags", StringComparison.OrdinalIgnoreCase))
            .Select(line => line[(line.IndexOf(':') + 1)..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (processorFlags.Length == 0)
        {
            throw new PlatformNotSupportedException("Linux did not expose x86 CPU flags in /proc/cpuinfo.");
        }

        var commonFlags = processorFlags[0];
        foreach (var flags in processorFlags.Skip(1))
        {
            commonFlags.IntersectWith(flags);
        }

        return commonFlags;
    }

    private static ulong? GetSupportedXstate()
        => GetXstateMask(ArchGetXcompSupp);

    private static ulong? GetXstatePermissions()
        => GetXstateMask(ArchGetXcompPerm);

    private static ulong? GetXstateMask(int operation)
    {
        try
        {
            return ArchPrctl(operation, out var mask) == 0 ? mask : null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    private static void EnsureSupportedPlatform()
    {
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.X86))
        {
            throw new PlatformNotSupportedException(
                "Host checking must run on an x86 or x86-64 Linux host. ELF analysis remains cross-platform.");
        }
    }

    [DllImport("libc", EntryPoint = "arch_prctl", SetLastError = true)]
    private static extern int ArchPrctl(int code, out ulong address);
}

public enum HostFeatureStatus
{
    Supported,
    PermissionRequired,
    Unsupported,
    Indeterminate
}

public sealed record HostFeatureResult(
    CpuidFeature Feature,
    HostFeatureStatus Status,
    string Detail);

public sealed record HostCompatibilityReport(
    string OperatingSystem,
    string Architecture,
    IReadOnlyList<HostFeatureResult> Features)
{
    public bool IsCompatible => Features.All(feature => feature.Status == HostFeatureStatus.Supported);
}

public static class HostReportWriter
{
    public static void Write(HostCompatibilityReport report, TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine($"Host: {report.OperatingSystem} ({report.Architecture})");
        foreach (var result in report.Features)
        {
            writer.WriteLine($"  {result.Feature,-20} {result.Status,-13} {result.Detail}");
        }

        writer.WriteLine(report.IsCompatible
            ? "Compatibility: all required features are supported."
            : "Compatibility: one or more required features are unsupported or indeterminate.");
    }
}
