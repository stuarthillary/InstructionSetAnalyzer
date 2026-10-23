using Iced.Intel;

namespace InstructionSetAnalyzer;

public enum HostFeatureStatus
{
    Supported,
    PermissionRequired,
    Unsupported,
    Indeterminate
}

public sealed record HostFeatureResult(
    CpuidFeature Feature,
    HostFeatureStatus Status,
    string Detail);

public sealed record HostCompatibilityReport(
    string OperatingSystem,
    string Architecture,
    IReadOnlyList<HostFeatureResult> Features)
{
    public bool IsCompatible => Features.All(feature => feature.Status == HostFeatureStatus.Supported);
}

public interface IHostFeatureChecker
{
    HostCompatibilityReport Check(IEnumerable<CpuidFeature> requiredFeatures);
}
