using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public class LinuxHostFeatureCheckerTests
{
    private const ulong TileConfig = 1UL << 17;
    private const ulong TileData = 1UL << 18;

    private static HostFeatureResult Check(
        CpuidFeature feature,
        string[] flags,
        ulong? supportedXstate = null,
        ulong? xstatePermissions = null) =>
        LinuxHostFeatureChecker.CheckFeature(
            feature,
            new HashSet<string>(flags, StringComparer.OrdinalIgnoreCase),
            supportedXstate,
            xstatePermissions);

    [Fact]
    public void Every_mapped_flag_name_exists_in_the_kernel()
    {
        var mapped = LinuxCpuFlagTables.KernelReported
            .Concat(LinuxCpuFlagTables.FlagAliases.Keys)
            .Distinct();

        var unknown = mapped
            .Where(feature => !LinuxCpuFlagTables.GetCandidateFlags(feature).Any(KernelFlagNames.All.Contains))
            .Select(feature => $"{feature} -> [{string.Join(", ", LinuxCpuFlagTables.GetCandidateFlags(feature))}]")
            .Order()
            .ToArray();

        Assert.True(
            unknown.Length == 0,
            "These features map to flag names the kernel does not print:" +
            Environment.NewLine + string.Join(Environment.NewLine, unknown));
    }

    [Fact]
    public void Features_are_never_both_mapped_and_hidden_by_the_kernel()
    {
        // These have no kernel-printed flag, so claiming a mapping would report them as Unsupported.
        CpuidFeature[] hidden =
        [
            CpuidFeature.AMX_COMPLEX, CpuidFeature.AMX_FP16, CpuidFeature.AVX_IFMA,
            CpuidFeature.AVX_NE_CONVERT, CpuidFeature.AVX_VNNI_INT8, CpuidFeature.AVX_VNNI_INT16,
            CpuidFeature.PREFETCHWT1, CpuidFeature.RDPMC, CpuidFeature.SHA512, CpuidFeature.TDX,
            CpuidFeature.UINTR
        ];

        Assert.All(hidden, feature => Assert.False(LinuxCpuFlagTables.HasKnownFlagMapping(feature), feature.ToString()));
    }

    [Theory]
    [InlineData(CpuidFeature.AVX2, "avx2")]
    [InlineData(CpuidFeature.AVX512_IFMA, "avx512ifma")]
    [InlineData(CpuidFeature.AVX512_VBMI, "avx512vbmi")]
    [InlineData(CpuidFeature.AVX512_VBMI2, "avx512_vbmi2")]
    [InlineData(CpuidFeature.MONITORX, "mwaitx")]
    [InlineData(CpuidFeature.SHA, "sha_ni")]
    [InlineData(CpuidFeature.SSE3, "pni")]
    [InlineData(CpuidFeature.SSE4_2, "sse4_2")]
    [InlineData(CpuidFeature.LZCNT, "abm")]
    public void Reports_supported_when_the_kernel_flag_is_present(CpuidFeature feature, string flag)
    {
        var result = Check(feature, [flag]);

        Assert.Equal(HostFeatureStatus.Supported, result.Status);
    }

    [Fact]
    public void Reports_unsupported_with_the_missing_flag_name()
    {
        var result = Check(CpuidFeature.AVX512_VBMI, ["avx2"]);

        Assert.Equal(HostFeatureStatus.Unsupported, result.Status);
        Assert.Contains("avx512vbmi", result.Detail);
    }

    [Theory]
    [InlineData(CpuidFeature.AMX_COMPLEX)]
    [InlineData(CpuidFeature.AMX_FP16)]
    [InlineData(CpuidFeature.AVX_IFMA)]
    [InlineData(CpuidFeature.AVX_NE_CONVERT)]
    [InlineData(CpuidFeature.AVX_VNNI_INT8)]
    [InlineData(CpuidFeature.AVX_VNNI_INT16)]
    [InlineData(CpuidFeature.PREFETCHWT1)]
    [InlineData(CpuidFeature.RDPMC)]
    [InlineData(CpuidFeature.SHA512)]
    [InlineData(CpuidFeature.TDX)]
    [InlineData(CpuidFeature.UINTR)]
    public void Reports_indeterminate_for_features_the_kernel_hides_when_cpuid_is_unavailable(CpuidFeature feature)
    {
        // amx_tile is present so the AMX variants get past the AMX_TILE prerequisite.
        var result = Check(feature, ["avx2", "amx_tile"]);

        Assert.Equal(HostFeatureStatus.Indeterminate, result.Status);
    }

    [Theory]
    [InlineData(CpuidFeature.CPUID)]
    [InlineData(CpuidFeature.FPU)]
    [InlineData(CpuidFeature.INTEL386)]
    [InlineData(CpuidFeature.PAUSE)]
    public void Architectural_features_are_always_supported(CpuidFeature feature)
    {
        Assert.Equal(HostFeatureStatus.Supported, Check(feature, []).Status);
    }

    [Theory]
    [InlineData(CpuidFeature.AMX_TILE)]
    [InlineData(CpuidFeature.AMX_INT8)]
    [InlineData(CpuidFeature.AMX_BF16)]
    [InlineData(CpuidFeature.AMX_FP16)]
    public void Amx_is_unsupported_without_amx_tile(CpuidFeature feature)
    {
        var result = Check(feature, ["avx2", "amx_int8", "amx_bf16"], TileConfig | TileData, TileConfig | TileData);

        Assert.Equal(HostFeatureStatus.Unsupported, result.Status);
        Assert.Contains("AMX_TILE", result.Detail);
    }

    [Fact]
    public void Amx_requires_the_kernel_to_support_both_tile_xstate_components()
    {
        var result = Check(CpuidFeature.AMX_INT8, ["amx_tile", "amx_int8"], TileConfig, TileConfig | TileData);

        Assert.Equal(HostFeatureStatus.Unsupported, result.Status);
    }

    [Fact]
    public void Amx_is_indeterminate_when_xstate_support_cannot_be_queried()
    {
        var result = Check(CpuidFeature.AMX_INT8, ["amx_tile", "amx_int8"], supportedXstate: null);

        Assert.Equal(HostFeatureStatus.Indeterminate, result.Status);
    }

    [Fact]
    public void Amx_is_indeterminate_when_process_permission_cannot_be_queried()
    {
        var result = Check(
            CpuidFeature.AMX_INT8, ["amx_tile", "amx_int8"], TileConfig | TileData, xstatePermissions: null);

        Assert.Equal(HostFeatureStatus.Indeterminate, result.Status);
    }

    [Fact]
    public void Amx_needs_a_permission_request_until_the_process_asks_for_tile_data()
    {
        var result = Check(
            CpuidFeature.AMX_INT8, ["amx_tile", "amx_int8"], TileConfig | TileData, xstatePermissions: 0);

        Assert.Equal(HostFeatureStatus.PermissionRequired, result.Status);
    }

    [Fact]
    public void Amx_is_supported_once_tile_data_permission_is_granted()
    {
        var result = Check(
            CpuidFeature.AMX_INT8, ["amx_tile", "amx_int8"], TileConfig | TileData, TileData);

        Assert.Equal(HostFeatureStatus.Supported, result.Status);
    }

    [Theory]
    [InlineData(new[] { "pku", "ospke" }, HostFeatureStatus.Supported)]
    [InlineData(new[] { "pku" }, HostFeatureStatus.Unsupported)]
    [InlineData(new[] { "ospke" }, HostFeatureStatus.Unsupported)]
    public void Pku_requires_both_hardware_and_os_flags(string[] flags, HostFeatureStatus expected)
    {
        Assert.Equal(expected, Check(CpuidFeature.PKU, flags).Status);
    }
}
