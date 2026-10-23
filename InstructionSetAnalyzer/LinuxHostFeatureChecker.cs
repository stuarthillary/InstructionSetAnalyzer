using System.Runtime.InteropServices;
using Iced.Intel;

namespace InstructionSetAnalyzer;

public sealed class LinuxHostFeatureChecker : IHostFeatureChecker
{
    private const int ArchGetXcompSupp = 0x1021;
    private const int ArchGetXcompPerm = 0x1022;
    private const int XfeatureTileConfig = 17;
    private const int XfeatureTileData = 18;

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

    internal static HostFeatureResult CheckFeature(
        CpuidFeature feature,
        IReadOnlySet<string> flags,
        ulong? supportedXstate,
        ulong? xstatePermissions)
    {
        if (LinuxCpuFlagTables.Architectural.Contains(feature))
        {
            return new HostFeatureResult(feature, HostFeatureStatus.Supported, "x86 architectural feature");
        }

        if (IsAmxFeature(feature) && !flags.Contains("amx_tile"))
        {
            return new HostFeatureResult(
                feature,
                HostFeatureStatus.Unsupported,
                "AMX_TILE is required by every AMX instruction");
        }

        var aliases = LinuxCpuFlagTables.GetCandidateFlags(feature);
        var hasRequiredFlags = feature == CpuidFeature.PKU
            ? flags.Contains("pku") && flags.Contains("ospke")
            : aliases.Any(flags.Contains);
        if (!hasRequiredFlags)
        {
            var mappingIsKnown = LinuxCpuFlagTables.HasKnownFlagMapping(feature);
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
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
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
