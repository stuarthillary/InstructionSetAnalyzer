using System.Text.Json;
using System.Text.Json.Serialization;
using Iced.Intel;

namespace InstructionSetAnalyzer;

internal sealed record JsonReport(
    int SchemaVersion,
    string Kind,
    string? Path,
    JsonSummary? Summary,
    IReadOnlyList<string>? Features,
    IReadOnlyList<string>? RequiredFeatures,
    IReadOnlyList<string>? DispatchedOnlyFeatures,
    IReadOnlyList<JsonFile>? Files,
    IReadOnlyList<JsonSkipped>? Skipped,
    IReadOnlyList<JsonFailed>? Failed,
    JsonBaseline? Baseline,
    JsonHost? Host);

internal sealed record JsonSummary(int Analyzed, int Skipped, int Failed);

internal sealed record JsonFile(
    string Path,
    string Architecture,
    string CodeSource,
    int ExecutableRegions,
    bool SymbolsCollected,
    IReadOnlyList<JsonFeature> Features);

internal sealed record JsonFeature(
    string Name,
    long Instructions,
    long DispatchedInstructions,
    long UnattributedInstructions,
    IReadOnlyList<JsonSymbol>? Symbols);

internal sealed record JsonSymbol(
    string Name,
    string Address,
    long Instructions,
    IReadOnlyList<string> DispatchedBy);

internal sealed record JsonSkipped(string Path, string Reason, string Message);

internal sealed record JsonFailed(string Path, string Error);

internal sealed record JsonBaseline(
    string Level,
    bool Met,
    IReadOnlyList<string> Exceeding,
    IReadOnlyList<JsonBaselineFile> Files);

internal sealed record JsonBaselineFile(string Path, IReadOnlyList<string> Exceeding);

internal sealed record JsonHost(
    string OperatingSystem,
    string Architecture,
    bool Compatible,
    IReadOnlyList<JsonHostFeature> Features);

internal sealed record JsonHostFeature(string Feature, string Status, string Detail);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(JsonReport))]
internal sealed partial class ReportJsonContext : JsonSerializerContext;

internal static class JsonReportWriter
{
    /// <summary>Bumped whenever a field is removed or changes meaning.</summary>
    public const int SchemaVersion = 1;

    public static void Write(AnalysisReport report, TextWriter output) =>
        output.WriteLine(JsonSerializer.Serialize(Convert(report), ReportJsonContext.Default.JsonReport));

    public static void WriteHost(HostCompatibilityReport host, TextWriter output)
    {
        var document = new JsonReport(
            SchemaVersion, "host", null, null, null, null, null, null, null, null, null, ConvertHost(host));
        output.WriteLine(JsonSerializer.Serialize(document, ReportJsonContext.Default.JsonReport));
    }

    private static JsonReport Convert(AnalysisReport report)
    {
        var scan = report.Scan;
        return new JsonReport(
            SchemaVersion,
            report.IsDirectory ? "directory" : "file",
            report.Path,
            new JsonSummary(scan.Analyzed.Count, scan.Skipped.Count, scan.Failed.Count),
            Names(scan.AllFeatures()),
            Names(report.RequiredFeatures),
            Names(report.Options.ExcludeDispatched ? report.DispatchedOnlyFeatures : []),
            scan.Analyzed.Select(file => ConvertFile(file, report.Options.Symbols)).ToArray(),
            scan.Skipped.Select(file => new JsonSkipped(file.Path, FormatReason(file.Reason), file.Message)).ToArray(),
            scan.Failed.Select(file => new JsonFailed(file.Path, file.Message)).ToArray(),
            report.Baseline is { } baseline ? ConvertBaseline(baseline) : null,
            report.Host is { } host ? ConvertHost(host) : null);
    }

    private static JsonFile ConvertFile(AnalyzedFile file, bool includeSymbols)
    {
        var result = file.Result;
        var features = result.Usage
            .OrderBy(pair => pair.Key.ToString(), StringComparer.Ordinal)
            .Select(pair => new JsonFeature(
                pair.Key.ToString(),
                pair.Value.InstructionCount,
                pair.Value.DispatchedInstructionCount,
                pair.Value.UnattributedInstructionCount,
                includeSymbols
                    ? pair.Value.Symbols
                        .Select(symbol => new JsonSymbol(
                            symbol.Name,
                            $"0x{symbol.Address:x}",
                            symbol.InstructionCount,
                            symbol.DispatchedBy))
                        .ToArray()
                    : null))
            .ToArray();
        return new JsonFile(
            file.Path,
            result.Architecture,
            result.Source == CodeSource.Sections ? "sections" : "segments",
            result.ExecutableRegionCount,
            result.SymbolsCollected,
            features);
    }

    private static JsonBaseline ConvertBaseline(BaselineReport baseline) =>
        new(
            Baseline.Format(baseline.Level),
            baseline.IsMet,
            Names(baseline.Exceeding),
            baseline.Files.Select(file => new JsonBaselineFile(file.Path, Names(file.Exceeding))).ToArray());

    private static JsonHost ConvertHost(HostCompatibilityReport host) =>
        new(
            host.OperatingSystem,
            host.Architecture,
            host.IsCompatible,
            host.Features
                .Select(result => new JsonHostFeature(
                    result.Feature.ToString(),
                    JsonNamingPolicy.CamelCase.ConvertName(result.Status.ToString()),
                    result.Detail))
                .ToArray());

    private static string[] Names(IEnumerable<CpuidFeature> features) =>
        features.Select(feature => feature.ToString()).Order(StringComparer.Ordinal).ToArray();

    private static string FormatReason(SkipReason reason) =>
        reason == SkipReason.UnsupportedArchitecture ? "unsupported-architecture" : "no-executable-code";
}
