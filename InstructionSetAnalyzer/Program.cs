namespace InstructionSetAnalyzer;

internal static class Program
{
    private static int Main(string[] args) =>
        new CliApplication(Console.Out, Console.Error, new LinuxHostFeatureChecker()).Run(args);
}
