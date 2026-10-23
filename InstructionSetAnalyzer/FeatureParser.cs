using Iced.Intel;

namespace InstructionSetAnalyzer;

public static class FeatureParser
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    // Looked up by name only: Enum.TryParse would also accept numeric strings such as "5".
    private static readonly Dictionary<string, CpuidFeature> FeaturesByName = CreateNameMap();

    public static IReadOnlySet<CpuidFeature> Parse(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var features = new HashSet<CpuidFeature>();
        var invalidNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in values.SelectMany(value =>
                     value.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (FeaturesByName.TryGetValue(name, out var feature))
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

    private static Dictionary<string, CpuidFeature> CreateNameMap()
    {
        var map = new Dictionary<string, CpuidFeature>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames<CpuidFeature>())
        {
            map[name] = Enum.Parse<CpuidFeature>(name);
        }

        return map;
    }
}
