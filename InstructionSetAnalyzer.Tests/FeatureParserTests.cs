using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public class FeatureParserTests
{
    [Fact]
    public void Parses_names_case_insensitively()
    {
        var features = FeatureParser.Parse(["avx2", "Fma"]);

        Assert.Equal(new[] { CpuidFeature.AVX2, CpuidFeature.FMA }.ToHashSet(), features);
    }

    [Theory]
    [InlineData("AVX,AVX2")]
    [InlineData("AVX;AVX2")]
    [InlineData("AVX AVX2")]
    [InlineData("AVX\tAVX2")]
    [InlineData("AVX\r\nAVX2")]
    [InlineData(" AVX , AVX2 ")]
    public void Splits_a_single_value_on_any_separator(string value)
    {
        var features = FeatureParser.Parse([value]);

        Assert.Equal(new[] { CpuidFeature.AVX, CpuidFeature.AVX2 }.ToHashSet(), features);
    }

    [Fact]
    public void Combines_separate_arguments_and_removes_duplicates()
    {
        var features = FeatureParser.Parse(["AVX", "avx,AES", "AVX"]);

        Assert.Equal(new[] { CpuidFeature.AVX, CpuidFeature.AES }.ToHashSet(), features);
    }

    [Fact]
    public void Reports_every_unknown_name_sorted()
    {
        var exception = Assert.Throws<ArgumentException>(() => FeatureParser.Parse(["AVX", "zzz", "bogus"]));

        Assert.Equal("Unknown CPU feature names: bogus, zzz", exception.Message);
    }

    [Fact]
    public void Uses_the_singular_form_for_one_unknown_name()
    {
        var exception = Assert.Throws<ArgumentException>(() => FeatureParser.Parse(["bogus"]));

        Assert.Equal("Unknown CPU feature name: bogus", exception.Message);
    }

    [Theory]
    [InlineData]
    [InlineData("")]
    [InlineData(" , ; ")]
    public void Requires_at_least_one_feature(params string[] values)
    {
        var exception = Assert.Throws<ArgumentException>(() => FeatureParser.Parse(values));

        Assert.Equal("At least one CPU feature is required.", exception.Message);
    }

    [Fact]
    public void Rejects_null_input()
    {
        Assert.Throws<ArgumentNullException>(() => FeatureParser.Parse(null!));
    }

    // Documents current behavior; Phase 2 will reject numeric names.
    [Fact]
    public void Currently_accepts_numeric_names_for_defined_values()
    {
        var features = FeatureParser.Parse([((int)CpuidFeature.AVX2).ToString()]);

        Assert.Contains(CpuidFeature.AVX2, features);
    }
}
