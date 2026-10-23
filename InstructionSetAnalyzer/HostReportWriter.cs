namespace InstructionSetAnalyzer;

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
