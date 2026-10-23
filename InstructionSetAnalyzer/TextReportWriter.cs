using Iced.Intel;

namespace InstructionSetAnalyzer;

internal static class TextReportWriter
{
    private const int MaxFilesPerFeature = 5;
    private const int MaxSymbolsPerFile = 3;

    public static void Write(AnalysisReport report, TextWriter output)
    {
        WriteHeader(report, output);

        var options = report.Options;
        var printed = options.ExcludeDispatched ? report.RequiredFeatures : report.Scan.AllFeatures();
        WriteFeatureList(output, "CPU features used:", printed);

        if (options.ExcludeDispatched && report.DispatchedOnlyFeatures.Count > 0)
        {
            WriteFeatureList(
                output,
                "Used only inside ifunc-dispatched variants (not counted by checks):",
                report.DispatchedOnlyFeatures);
        }

        if (options.Detail || options.Symbols)
        {
            WriteBreakdown(report, output);
        }

        if (report.Baseline is { } baseline)
        {
            WriteBaseline(report, baseline, output);
        }

        if (report.Host is { } host)
        {
            HostReportWriter.Write(host, output);
        }
    }

    private static void WriteHeader(AnalysisReport report, TextWriter output)
    {
        if (!report.IsDirectory)
        {
            if (report.Scan.Analyzed.Count > 0)
            {
                var result = report.Scan.Analyzed[0].Result;
                var regions = result.Source == CodeSource.Sections
                    ? $"{result.ExecutableRegionCount} executable sections"
                    : $"{result.ExecutableRegionCount} executable segments, no executable sections";
                output.WriteLine($"Binary {report.Path} ({result.Architecture}, {regions})");
            }

            return;
        }

        if (report.Options.Verbose)
        {
            foreach (var file in report.Scan.Analyzed)
            {
                output.WriteLine($"Analyzed {file.Path}");
            }
        }

        output.WriteLine();
        output.WriteLine($"Directory {report.Path}");
        output.WriteLine($"Analyzed ELF binaries: {report.Scan.Analyzed.Count}");
        WriteSkipCount(report.Scan, SkipReason.UnsupportedArchitecture, "unsupported architecture", output);
        WriteSkipCount(report.Scan, SkipReason.NoExecutableCode, "no executable code", output);
    }

    private static void WriteSkipCount(ScanResult scan, SkipReason reason, string description, TextWriter output)
    {
        var count = scan.Skipped.Count(file => file.Reason == reason);
        if (count > 0)
        {
            output.WriteLine($"Skipped ({description}): {count}");
        }
    }

    private static void WriteFeatureList(TextWriter output, string heading, IEnumerable<CpuidFeature> features)
    {
        output.WriteLine(heading);
        foreach (var feature in features.OrderBy(feature => feature.ToString(), StringComparer.Ordinal))
        {
            output.WriteLine($"  {feature}");
        }
    }

    private static void WriteBreakdown(AnalysisReport report, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine("Feature breakdown:");
        var root = report.IsDirectory ? report.Path : System.IO.Path.GetDirectoryName(report.Path) ?? string.Empty;

        foreach (var feature in report.Scan.AllFeatures().OrderBy(f => f.ToString(), StringComparer.Ordinal))
        {
            var users = report.Scan.Analyzed
                .Where(file => file.Result.Usage.ContainsKey(feature))
                .Select(file => (file.Path, Usage: file.Result.Usage[feature]))
                .OrderByDescending(user => user.Usage.InstructionCount)
                .ThenBy(user => user.Path, StringComparer.Ordinal)
                .ToArray();
            var total = users.Sum(user => user.Usage.InstructionCount);
            var dispatched = users.Sum(user => user.Usage.DispatchedInstructionCount);
            var dispatchNote = dispatched > 0 ? $", {dispatched} inside ifunc variants" : string.Empty;
            output.WriteLine($"  {feature}: {total} instructions in {Plural(users.Length, "file")}{dispatchNote}");

            foreach (var (path, usage) in users.Take(MaxFilesPerFeature))
            {
                var label = System.IO.Path.GetRelativePath(root, path);
                output.WriteLine($"    {label}: {usage.InstructionCount}");
                if (report.Options.Symbols)
                {
                    WriteSymbols(usage, output);
                }
            }

            if (users.Length > MaxFilesPerFeature)
            {
                output.WriteLine($"    (+{users.Length - MaxFilesPerFeature} more files)");
            }
        }
    }

    private static void WriteSymbols(FeatureUsage usage, TextWriter output)
    {
        foreach (var symbol in usage.Symbols.Take(MaxSymbolsPerFile))
        {
            var dispatch = symbol.DispatchedBy.Count > 0
                ? $" [ifunc variant of {string.Join(", ", symbol.DispatchedBy)}]"
                : string.Empty;
            output.WriteLine($"        {symbol.Name}: {symbol.InstructionCount}{dispatch}");
        }

        var listed = usage.Symbols.Take(MaxSymbolsPerFile).Sum(symbol => symbol.InstructionCount);
        var remaining = usage.InstructionCount - usage.UnattributedInstructionCount - listed;
        if (remaining > 0)
        {
            output.WriteLine($"        (+{remaining} instructions in other functions)");
        }

        if (usage.UnattributedInstructionCount > 0)
        {
            output.WriteLine($"        ({usage.UnattributedInstructionCount} instructions in code with no symbol)");
        }
    }

    private static void WriteBaseline(AnalysisReport report, BaselineReport baseline, TextWriter output)
    {
        output.WriteLine();
        var name = Baseline.Format(baseline.Level);
        if (baseline.IsMet)
        {
            output.WriteLine($"Baseline {name}: met.");
            return;
        }

        output.WriteLine($"Baseline {name}: exceeded by {FormatFeatures(baseline.Exceeding)}");
        if (report.IsDirectory)
        {
            foreach (var (path, exceeding) in baseline.Files)
            {
                output.WriteLine($"  {System.IO.Path.GetRelativePath(report.Path, path)}: {FormatFeatures(exceeding)}");
            }
        }
    }

    private static string FormatFeatures(IEnumerable<CpuidFeature> features) =>
        string.Join(", ", features.Select(feature => feature.ToString()).Order(StringComparer.Ordinal));

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}
