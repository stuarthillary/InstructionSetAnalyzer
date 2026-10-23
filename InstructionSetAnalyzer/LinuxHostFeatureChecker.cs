using System.Runtime.InteropServices;
using Iced.Intel;

namespace InstructionSetAnalyzer;

public sealed class LinuxHostFeatureChecker : IHostFeatureChecker
{
    private const int ArchGetXcompSupp = 0x1021;
    private const int ArchGetXcompPerm = 0x1022;

    private readonly ICpuid? _cpuid;

    public LinuxHostFeatureChecker()
        : this(HardwareCpuid.TryCreate())
    {
    }

    internal LinuxHostFeatureChecker(ICpuid? cpuid)
    {
        _cpuid = cpuid;
    }

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
            .Select(feature => CheckFeature(feature, flags, xstate, xstatePermissions, _cpuid))
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
        ulong? xstatePermissions,
        ICpuid? cpuid = null) =>
        HostFeatureEvaluator.Evaluate(feature, new HostState(flags, supportedXstate, xstatePermissions, cpuid));

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
