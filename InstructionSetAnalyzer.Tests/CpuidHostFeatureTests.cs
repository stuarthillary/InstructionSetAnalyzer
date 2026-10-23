using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public class CpuidHostFeatureTests
{
    private const ulong TileConfig = 1UL << 17;
    private const ulong TileData = 1UL << 18;

    private static HostFeatureResult Check(
        CpuidFeature feature,
        FakeCpuid? cpuid,
        string[]? flags = null,
        ulong? supportedXstate = null,
        ulong? xstatePermissions = null) =>
        LinuxHostFeatureChecker.CheckFeature(
            feature,
            new HashSet<string>(flags ?? ["avx"], StringComparer.OrdinalIgnoreCase),
            supportedXstate,
            xstatePermissions,
            cpuid);

    private static FakeCpuid WithBit(CpuidFeature feature) =>
        new FakeCpuid().SetBit(LinuxCpuFlagTables.CpuidOnlyFeatures[feature].Bit);

    public static TheoryData<CpuidFeature> AvxFeatures() =>
        new(LinuxCpuFlagTables.CpuidOnlyFeatures
            .Where(rule => rule.Value.OsRequirement == OsRequirement.AvxState)
            .Select(rule => rule.Key));

    [Theory]
    [MemberData(nameof(AvxFeatures))]
    public void Avx_family_features_are_supported_when_cpuid_and_avx_state_agree(CpuidFeature feature)
    {
        var result = Check(feature, WithBit(feature), ["avx"]);

        Assert.Equal(HostFeatureStatus.Supported, result.Status);
        Assert.Contains("CPUID.(EAX=07h", result.Detail);
    }

    [Theory]
    [MemberData(nameof(AvxFeatures))]
    public void Avx_family_features_are_unsupported_when_the_cpuid_bit_is_clear(CpuidFeature feature)
    {
        var cpuid = new FakeCpuid().WithMaxLeaf7Subleaf(1);

        var result = Check(feature, cpuid, ["avx"]);

        Assert.Equal(HostFeatureStatus.Unsupported, result.Status);
        Assert.Contains("is clear", result.Detail);
    }

    [Theory]
    [MemberData(nameof(AvxFeatures))]
    public void Avx_family_features_are_unsupported_when_linux_does_not_enable_avx_state(CpuidFeature feature)
    {
        var result = Check(feature, WithBit(feature), flags: ["sse4_2"]);

        Assert.Equal(HostFeatureStatus.Unsupported, result.Status);
        Assert.Contains("no avx flag", result.Detail);
    }

    [Fact]
    public void A_set_bit_is_ignored_when_the_cpu_lacks_the_leaf_7_subleaf()
    {
        var cpuid = WithBit(CpuidFeature.AVX_IFMA).WithMaxLeaf7Subleaf(0);

        Assert.Equal(HostFeatureStatus.Unsupported, Check(CpuidFeature.AVX_IFMA, cpuid).Status);
    }

    [Fact]
    public void A_cpu_without_leaf_7_supports_none_of_these_features()
    {
        var cpuid = WithBit(CpuidFeature.SHA512);
        cpuid.MaxLeaf = 6;

        Assert.Equal(HostFeatureStatus.Unsupported, Check(CpuidFeature.SHA512, cpuid).Status);
    }

    [Fact]
    public void Prefetchwt1_needs_no_os_support()
    {
        var result = Check(CpuidFeature.PREFETCHWT1, WithBit(CpuidFeature.PREFETCHWT1), flags: []);

        Assert.Equal(HostFeatureStatus.Supported, result.Status);
    }

    [Theory]
    [InlineData(CpuidFeature.AMX_FP16)]
    [InlineData(CpuidFeature.AMX_COMPLEX)]
    public void Amx_extensions_use_cpuid_then_the_linux_state_checks(CpuidFeature feature)
    {
        string[] flags = ["avx", "amx_tile"];

        Assert.Equal(
            HostFeatureStatus.Supported,
            Check(feature, WithBit(feature), flags, TileConfig | TileData, TileData).Status);
        Assert.Equal(
            HostFeatureStatus.PermissionRequired,
            Check(feature, WithBit(feature), flags, TileConfig | TileData, 0).Status);
        Assert.Equal(
            HostFeatureStatus.Unsupported,
            Check(feature, WithBit(feature), flags, TileConfig, TileData).Status);
        Assert.Equal(
            HostFeatureStatus.Indeterminate,
            Check(feature, WithBit(feature), flags, supportedXstate: null).Status);
    }

    [Theory]
    [InlineData(CpuidFeature.AMX_FP16)]
    [InlineData(CpuidFeature.AMX_COMPLEX)]
    public void Amx_extensions_are_unsupported_when_the_cpuid_bit_is_clear(CpuidFeature feature)
    {
        var result = Check(
            feature,
            new FakeCpuid().WithMaxLeaf7Subleaf(1),
            ["avx", "amx_tile"],
            TileConfig | TileData,
            TileData);

        Assert.Equal(HostFeatureStatus.Unsupported, result.Status);
    }

    [Theory]
    [InlineData(CpuidFeature.UINTR)]
    [InlineData(CpuidFeature.RDPMC)]
    [InlineData(CpuidFeature.TDX)]
    public void Features_cpuid_cannot_vouch_for_stay_indeterminate_even_with_cpuid_available(CpuidFeature feature)
    {
        var everythingSet = new FakeCpuid { FillAllBits = true };

        Assert.Equal(HostFeatureStatus.Indeterminate, Check(feature, everythingSet).Status);
    }

    [Theory]
    [MemberData(nameof(AvxFeatures))]
    public void Cpuid_rules_are_indeterminate_when_cpuid_is_unavailable(CpuidFeature feature)
    {
        Assert.Equal(HostFeatureStatus.Indeterminate, Check(feature, cpuid: null).Status);
    }

    [Fact]
    public void Cpuid_only_features_are_not_also_mapped_to_kernel_flags()
    {
        var overlap = LinuxCpuFlagTables.CpuidOnlyFeatures.Keys
            .Where(LinuxCpuFlagTables.HasKnownFlagMapping)
            .ToArray();

        Assert.Empty(overlap);
    }

    [Fact]
    public void Cpuid_only_features_are_really_hidden_by_the_kernel()
    {
        // If a kernel starts printing one of these flags, move the feature to the kernel-flag mapping:
        // that source also reflects what the OS has enabled.
        var nowExposed = LinuxCpuFlagTables.CpuidOnlyFeatures.Keys
            .Where(feature => LinuxCpuFlagTables.GetCandidateFlags(feature).Any(KernelFlagNames.All.Contains))
            .ToArray();

        Assert.Empty(nowExposed);
    }

    // Expected values are written out independently of the production table, from gcc 14's cpuid.h
    // ("Extended Features Leaf" for 7.0 and "Extended Features Sub-leaf" for 7.1).
    [Theory]
    [InlineData(CpuidFeature.PREFETCHWT1, 7u, 0u, "ECX", 0)]
    [InlineData(CpuidFeature.SHA512, 7u, 1u, "EAX", 0)]
    [InlineData(CpuidFeature.AMX_COMPLEX, 7u, 1u, "EAX", 8)]
    [InlineData(CpuidFeature.AMX_FP16, 7u, 1u, "EAX", 21)]
    [InlineData(CpuidFeature.AVX_IFMA, 7u, 1u, "EAX", 23)]
    [InlineData(CpuidFeature.AVX_VNNI_INT8, 7u, 1u, "EDX", 4)]
    [InlineData(CpuidFeature.AVX_NE_CONVERT, 7u, 1u, "EDX", 5)]
    [InlineData(CpuidFeature.AVX_VNNI_INT16, 7u, 1u, "EDX", 10)]
    public void Cpuid_bit_positions_match_the_documented_values(
        CpuidFeature feature, uint leaf, uint subleaf, string register, int bit)
    {
        var expected = new CpuidBit(leaf, subleaf, Enum.Parse<CpuidRegister>(register, ignoreCase: true), bit);

        Assert.Equal(expected, LinuxCpuFlagTables.CpuidOnlyFeatures[feature].Bit);
    }

    [Fact]
    public void Every_cpuid_only_feature_has_a_pinned_bit_position()
    {
        Assert.Equal(8, LinuxCpuFlagTables.CpuidOnlyFeatures.Count);
    }

    [Fact]
    public void Formats_cpuid_bits_the_way_the_intel_manual_writes_them()
    {
        Assert.Equal("CPUID.(EAX=07h,ECX=1):EAX[23]", new CpuidBit(7, 1, CpuidRegister.Eax, 23).ToString());
        Assert.Equal("CPUID.(EAX=07h,ECX=0):ECX[0]", new CpuidBit(7, 0, CpuidRegister.Ecx, 0).ToString());
    }

    [Fact]
    public void Reads_each_register_and_bit_independently()
    {
        var cpuid = new FakeCpuid();
        cpuid.SetBit(new CpuidBit(1, 0, CpuidRegister.Edx, 31));
        cpuid.SetBit(new CpuidBit(1, 0, CpuidRegister.Ebx, 0));

        Assert.True(cpuid.IsSet(new CpuidBit(1, 0, CpuidRegister.Edx, 31)));
        Assert.True(cpuid.IsSet(new CpuidBit(1, 0, CpuidRegister.Ebx, 0)));
        Assert.False(cpuid.IsSet(new CpuidBit(1, 0, CpuidRegister.Eax, 31)));
        Assert.False(cpuid.IsSet(new CpuidBit(1, 0, CpuidRegister.Ecx, 0)));
        Assert.False(cpuid.IsSet(new CpuidBit(1, 0, CpuidRegister.Edx, 30)));
    }

    [Fact]
    public void Hardware_cpuid_agrees_with_the_linux_flags_it_is_used_alongside()
    {
        if (HardwareCpuid.TryCreate() is not { } cpuid || !OperatingSystem.IsLinux())
        {
            return; // not an x86 Linux host
        }

        var flags = File.ReadLines("/proc/cpuinfo")
            .First(line => line.StartsWith("flags", StringComparison.Ordinal))
            .Split(':')[1]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        // The kernel never reports a flag the hardware lacks, so a flag without its CPUID bit means
        // the bit positions used for CPUID-only features cannot be trusted.
        (string Flag, CpuidBit Bit)[] pairs =
        [
            ("bmi1", new CpuidBit(7, 0, CpuidRegister.Ebx, 3)),
            ("avx2", new CpuidBit(7, 0, CpuidRegister.Ebx, 5)),
            ("bmi2", new CpuidBit(7, 0, CpuidRegister.Ebx, 8)),
            ("sha_ni", new CpuidBit(7, 0, CpuidRegister.Ebx, 29)),
            ("waitpkg", new CpuidBit(7, 0, CpuidRegister.Ecx, 5)),
            ("gfni", new CpuidBit(7, 0, CpuidRegister.Ecx, 8)),
            ("vaes", new CpuidBit(7, 0, CpuidRegister.Ecx, 9)),
            ("movdiri", new CpuidBit(7, 0, CpuidRegister.Ecx, 27)),
            ("serialize", new CpuidBit(7, 0, CpuidRegister.Edx, 14)),
            ("avx_vnni", new CpuidBit(7, 1, CpuidRegister.Eax, 4)),
            ("popcnt", new CpuidBit(1, 0, CpuidRegister.Ecx, 23))
        ];

        var contradictions = pairs
            .Where(pair => flags.Contains(pair.Flag) && !cpuid.IsSet(pair.Bit))
            .Select(pair => $"{pair.Flag} is set but {pair.Bit} is clear")
            .ToArray();

        Assert.Empty(contradictions);
    }

    [Fact]
    public void Hardware_cpuid_returns_null_above_the_highest_leaf()
    {
        if (HardwareCpuid.TryCreate() is not { } cpuid)
        {
            return;
        }

        Assert.NotNull(cpuid.Query(0, 0));
        Assert.Null(cpuid.Query(0x7FFFFFFF, 0));
    }
}

internal sealed class FakeCpuid : ICpuid
{
    private readonly Dictionary<(uint Leaf, uint Subleaf), uint[]> _leaves = [];

    public uint MaxLeaf { get; set; } = 0x20;

    /// <summary>When true every bit of every leaf reads as set.</summary>
    public bool FillAllBits { get; set; }

    public FakeCpuid SetBit(CpuidBit bit)
    {
        var registers = GetOrAdd(bit.Leaf, bit.Subleaf);
        registers[(int)bit.Register] |= 1u << bit.Bit;

        if (bit is { Leaf: 7, Subleaf: > 0 })
        {
            WithMaxLeaf7Subleaf(Math.Max(GetOrAdd(7, 0)[0], bit.Subleaf));
        }

        return this;
    }

    public FakeCpuid WithMaxLeaf7Subleaf(uint maxSubleaf)
    {
        GetOrAdd(7, 0)[0] = maxSubleaf;
        return this;
    }

    public CpuidRegisters? Query(uint leaf, uint subleaf)
    {
        if (leaf > MaxLeaf)
        {
            return null;
        }

        if (FillAllBits)
        {
            return new CpuidRegisters(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue);
        }

        var r = _leaves.TryGetValue((leaf, subleaf), out var registers) ? registers : new uint[4];
        return new CpuidRegisters(r[0], r[1], r[2], r[3]);
    }

    private uint[] GetOrAdd(uint leaf, uint subleaf)
    {
        if (!_leaves.TryGetValue((leaf, subleaf), out var registers))
        {
            _leaves[(leaf, subleaf)] = registers = new uint[4];
        }

        return registers;
    }
}
