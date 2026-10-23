using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public class BaselineTests
{
    // Written out independently of the production tables from gcc 14's feature macros for -march=x86-64,
    // -march=x86-64-v2/-v3/-v4 on 2026-10-24 (each level lists only what it adds to the previous one).
    public static TheoryData<BaselineLevel, string[]> Additions() => new()
    {
        { BaselineLevel.X86_64, ["CMOV", "CX8", "FPU", "FXSR", "MMX", "SSE", "SSE2", "SYSCALL"] },
        { BaselineLevel.X86_64_V2, ["CMPXCHG16B", "POPCNT", "SSE3", "SSE4_1", "SSE4_2", "SSSE3"] },
        {
            BaselineLevel.X86_64_V3,
            ["AVX", "AVX2", "BMI1", "BMI2", "F16C", "FMA", "LZCNT", "MOVBE", "XSAVE"]
        },
        { BaselineLevel.X86_64_V4, ["AVX512BW", "AVX512CD", "AVX512DQ", "AVX512F", "AVX512VL"] }
    };

    private static CpuidFeature[] Parse(IEnumerable<string> names) =>
        names.Select(Enum.Parse<CpuidFeature>).ToArray();

    [Theory]
    [MemberData(nameof(Additions))]
    public void Each_level_allows_what_it_adds_and_everything_older(BaselineLevel level, string[] added)
    {
        var allowed = Baseline.Allowed(level);

        Assert.All(Parse(added), feature => Assert.Contains(feature, allowed));
        foreach (var older in Enum.GetValues<BaselineLevel>().Where(candidate => candidate < level))
        {
            Assert.True(Baseline.Allowed(older).IsSubsetOf(allowed), $"{older} is not a subset of {level}");
        }
    }

    [Theory]
    [MemberData(nameof(Additions))]
    public void A_level_does_not_allow_what_only_newer_levels_add(BaselineLevel level, string[] added)
    {
        foreach (var older in Enum.GetValues<BaselineLevel>().Where(candidate => candidate < level))
        {
            Assert.All(Parse(added), feature => Assert.DoesNotContain(feature, Baseline.Allowed(older)));
        }
    }

    [Fact]
    public void Always_allows_the_architecture_legacy_isa_and_hint_nop_cet()
    {
        CpuidFeature[] neutral =
        [
            CpuidFeature.X64, CpuidFeature.CPUID, CpuidFeature.TSC, CpuidFeature.PAUSE,
            CpuidFeature.MULTIBYTENOP, CpuidFeature.INTEL8086, CpuidFeature.INTEL186,
            CpuidFeature.INTEL286, CpuidFeature.INTEL386, CpuidFeature.INTEL486,
            CpuidFeature.FPU287, CpuidFeature.FPU387, CpuidFeature.CET_IBT
        ];

        Assert.All(neutral, feature => Assert.Contains(feature, Baseline.Allowed(BaselineLevel.X86_64)));
    }

    [Fact]
    public void Shadow_stack_instructions_are_not_free_because_they_fault_without_cet()
    {
        Assert.DoesNotContain(CpuidFeature.CET_SS, Baseline.Allowed(BaselineLevel.X86_64_V4));
    }

    [Fact]
    public void Reports_exactly_the_features_beyond_the_level()
    {
        CpuidFeature[] used = [CpuidFeature.SSE2, CpuidFeature.SSE4_2, CpuidFeature.AVX2, CpuidFeature.AES];

        Assert.Equal(
            new[] { CpuidFeature.SSE4_2, CpuidFeature.AVX2, CpuidFeature.AES }.ToHashSet(),
            Baseline.Exceeding(used, BaselineLevel.X86_64));
        Assert.Equal(
            new[] { CpuidFeature.AVX2, CpuidFeature.AES }.ToHashSet(),
            Baseline.Exceeding(used, BaselineLevel.X86_64_V2));
        Assert.Equal(
            new[] { CpuidFeature.AES }.ToHashSet(),
            Baseline.Exceeding(used, BaselineLevel.X86_64_V4));
    }

    [Theory]
    [InlineData("x86-64", BaselineLevel.X86_64)]
    [InlineData("x86-64-v2", BaselineLevel.X86_64_V2)]
    [InlineData("X86-64-V3", BaselineLevel.X86_64_V3)]
    [InlineData("x86-64-v4", BaselineLevel.X86_64_V4)]
    public void Parses_the_gcc_march_names(string name, BaselineLevel expected)
    {
        Assert.True(Baseline.TryParse(name, out var level));
        Assert.Equal(expected, level);
        Assert.Equal(name.ToLowerInvariant(), Baseline.Format(level));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v3")]
    [InlineData("x86-64-v5")]
    [InlineData(null)]
    public void Rejects_unknown_names(string? name)
    {
        Assert.False(Baseline.TryParse(name, out _));
    }

    [Fact]
    public void Lists_every_level_once()
    {
        Assert.Equal(["x86-64", "x86-64-v2", "x86-64-v3", "x86-64-v4"], Baseline.AllNames);
    }
}
