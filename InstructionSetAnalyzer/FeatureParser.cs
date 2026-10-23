using Iced.Intel;

namespace InstructionSetAnalyzer;

public static class FeatureParser
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    public static IReadOnlySet<CpuidFeature> Parse(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var features = new HashSet<CpuidFeature>();
        var invalidNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in values.SelectMany(value =>
                     value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (Enum.TryParse<CpuidFeature>(name, true, out var feature) &&
                Enum.IsDefined(feature))
            {
                features.Add(feature);
            }
            else
            {
                invalidNames.Add(name);
            }
        }

        if (invalidNames.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown CPU feature name{(invalidNames.Count == 1 ? string.Empty : "s")}: " +
                string.Join(", ", invalidNames));
        }

        if (features.Count == 0)
        {
            throw new ArgumentException("At least one CPU feature is required.");
        }

        return features;
    }
}
