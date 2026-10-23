using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public class HostReportWriterTests
{
    private static HostCompatibilityReport Report(params HostFeatureResult[] results) =>
        new("TestOS", "X64", results);

    [Fact]
    public void Reports_compatible_when_every_feature_is_supported()
    {
        var report = Report(new HostFeatureResult(CpuidFeature.AVX2, HostFeatureStatus.Supported, "ok"));
        var writer = new StringWriter();

        HostReportWriter.Write(report, writer);

        Assert.True(report.IsCompatible);
        var text = writer.ToString();
        Assert.Contains("Host: TestOS (X64)", text);
        Assert.Contains("AVX2", text);
        Assert.Contains("Compatibility: all required features are supported.", text);
    }

    [Theory]
    [InlineData(HostFeatureStatus.PermissionRequired)]
    [InlineData(HostFeatureStatus.Unsupported)]
    [InlineData(HostFeatureStatus.Indeterminate)]
    public void Reports_incompatible_for_any_non_supported_status(HostFeatureStatus status)
    {
        var report = Report(
            new HostFeatureResult(CpuidFeature.AVX2, HostFeatureStatus.Supported, "ok"),
            new HostFeatureResult(CpuidFeature.AMX_TILE, status, "detail"));
        var writer = new StringWriter();

        HostReportWriter.Write(report, writer);

        Assert.False(report.IsCompatible);
        Assert.Contains(
            "Compatibility: one or more required features are unsupported or indeterminate.",
            writer.ToString());
    }

    [Fact]
    public void Lists_each_feature_with_its_status_and_detail()
    {
        var report = Report(new HostFeatureResult(CpuidFeature.PKU, HostFeatureStatus.Unsupported, "needs ospke"));
        var writer = new StringWriter();

        HostReportWriter.Write(report, writer);

        var line = Assert.Single(writer.ToString().Split(Environment.NewLine), l => l.Contains("PKU"));
        Assert.Contains("Unsupported", line);
        Assert.Contains("needs ospke", line);
    }
}
