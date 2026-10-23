using Iced.Intel;

namespace InstructionSetAnalyzer;

/// <summary>Everything known about the host that a feature check can consult.</summary>
internal sealed record HostState(
    IReadOnlySet<string> Flags,
    ulong? SupportedXstate,
    ulong? XstatePermissions,
    ICpuid? Cpuid);

/// <summary>
/// Decides whether the host supports one feature. Evidence comes from the Linux CPU flags, which already
/// account for what the OS has enabled; CPUID is consulted only for features the kernel hides.
/// </summary>
internal static class HostFeatureEvaluator
{
    private const int XfeatureTileConfig = 17;
    private const int XfeatureTileData = 18;

    public static HostFeatureResult Evaluate(CpuidFeature feature, HostState host)
    {
        if (LinuxCpuFlagTables.Architectural.Contains(feature))
        {
            return Result(feature, HostFeatureStatus.Supported, "x86 architectural feature");
        }

        if (IsAmxFeature(feature) && !host.Flags.Contains("amx_tile"))
        {
            return Result(feature, HostFeatureStatus.Unsupported, "AMX_TILE is required by every AMX instruction");
        }

        if (LinuxCpuFlagTables.CpuidOnlyFeatures.TryGetValue(feature, out var rule))
        {
            return EvaluateWithCpuid(feature, rule, host);
        }

        if (CheckKernelFlags(feature, host.Flags) is { } missing)
        {
            return missing;
        }

        return IsAmxFeature(feature)
            ? EvaluateAmxState(feature, host)
            : Result(feature, HostFeatureStatus.Supported, "Linux CPU flag is enabled");
    }

    /// <summary>Returns null when the kernel reports every flag the feature needs.</summary>
    private static HostFeatureResult? CheckKernelFlags(CpuidFeature feature, IReadOnlySet<string> flags)
    {
        if (feature == CpuidFeature.PKU)
        {
            return flags.Contains("pku") && flags.Contains("ospke")
                ? null
                : Result(feature, HostFeatureStatus.Unsupported, "Linux CPU flags pku and ospke are both required");
        }

        var aliases = LinuxCpuFlagTables.GetCandidateFlags(feature);
        if (aliases.Any(flags.Contains))
        {
            return null;
        }

        return LinuxCpuFlagTables.HasKnownFlagMapping(feature)
            ? Result(
                feature,
                HostFeatureStatus.Unsupported,
                $"missing Linux CPU flag ({string.Join(" or ", aliases)})")
            : Result(
                feature,
                HostFeatureStatus.Indeterminate,
                "no reliable Linux userspace capability mapping is available");
    }

    private static HostFeatureResult EvaluateWithCpuid(CpuidFeature feature, CpuidOnlyRule rule, HostState host)
    {
        if (host.Cpuid is null)
        {
            return Result(
                feature,
                HostFeatureStatus.Indeterminate,
                "the kernel does not report this feature and CPUID is unavailable");
        }

        if (!host.Cpuid.IsSet(rule.Bit))
        {
            return Result(feature, HostFeatureStatus.Unsupported, $"{rule.Bit} is clear");
        }

        switch (rule.OsRequirement)
        {
            case OsRequirement.AmxState:
                return EvaluateAmxState(feature, host);
            case OsRequirement.AvxState:
                return host.Flags.Contains("avx")
                    ? Result(feature, HostFeatureStatus.Supported, $"{rule.Bit} is set and Linux AVX state is enabled")
                    : Result(
                        feature,
                        HostFeatureStatus.Unsupported,
                        $"{rule.Bit} is set, but Linux does not report AVX state (no avx flag)");
            default:
                return Result(feature, HostFeatureStatus.Supported, $"{rule.Bit} is set");
        }
    }

    private static HostFeatureResult EvaluateAmxState(CpuidFeature feature, HostState host)
    {
        const ulong requiredAmxState = (1UL << XfeatureTileConfig) | (1UL << XfeatureTileData);
        if (host.SupportedXstate is not { } supported)
        {
            return Result(
                feature,
                HostFeatureStatus.Indeterminate,
                "the CPU reports AMX, but Linux AMX XSTATE support could not be queried");
        }

        if ((supported & requiredAmxState) != requiredAmxState)
        {
            return Result(
                feature,
                HostFeatureStatus.Unsupported,
                "Linux does not support the required AMX tile XSTATE components");
        }

        if (host.XstatePermissions is not { } permissions)
        {
            return Result(feature, HostFeatureStatus.Indeterminate, "Linux AMX process permission could not be queried");
        }

        return (permissions & (1UL << XfeatureTileData)) == 0
            ? Result(
                feature,
                HostFeatureStatus.PermissionRequired,
                "host supports AMX, but this process has not requested tile-data permission")
            : Result(
                feature,
                HostFeatureStatus.Supported,
                "Linux AMX tile-data permission is active for this process");
    }

    private static bool IsAmxFeature(CpuidFeature feature) =>
        LinuxCpuFlagTables.AmxFeatures.Contains(feature);

    private static HostFeatureResult Result(CpuidFeature feature, HostFeatureStatus status, string detail) =>
        new(feature, status, detail);
}
