using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

/// <summary>Shared setup for tests that drive the command line against files in a temporary directory.</summary>
public abstract class CliTestBase : IDisposable
{
    protected CliTestBase()
    {
        Output = new StringWriter();
        Error = new StringWriter();
        Checker = new FakeHostChecker();
    }

    internal TempDirectory Temp { get; } = new();

    protected StringWriter Output { get; }

    protected StringWriter Error { get; }

    internal FakeHostChecker Checker { get; }

    public void Dispose() => Temp.Dispose();

    protected int Run(params string[] args) => RunWith(CancellationToken.None, args);

    internal int RunWith(CancellationToken cancellationToken, params string[] args) =>
        new CliApplication(Output, Error, Checker).Run(args, cancellationToken);

    protected string[] PrintedFeatures() => FeaturesUnder("CPU features used:");

    protected string[] FeaturesUnder(string heading) =>
        Output.ToString()
            .Split(Environment.NewLine)
            .SkipWhile(line => line != heading)
            .Skip(1)
            .TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Select(line => line.Trim())
            .ToArray();

    internal sealed class FakeHostChecker : IHostFeatureChecker
    {
        public HostFeatureStatus Status { get; set; } = HostFeatureStatus.Supported;

        public Exception? Throw { get; set; }

        public IReadOnlyList<CpuidFeature>? LastRequest { get; private set; }

        public HostCompatibilityReport Check(IEnumerable<CpuidFeature> requiredFeatures)
        {
            if (Throw is not null)
            {
                throw Throw;
            }

            LastRequest = requiredFeatures.ToArray();
            return new HostCompatibilityReport(
                "FakeOS",
                "X64",
                LastRequest.Select(f => new HostFeatureResult(f, Status, "fake")).ToArray());
        }
    }
}
