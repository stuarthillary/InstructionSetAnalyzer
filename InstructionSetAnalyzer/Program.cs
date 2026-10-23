using System.Runtime.InteropServices;

namespace InstructionSetAnalyzer;

internal static class Program
{
    private static int Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();

        // Ctrl+C and SIGTERM stop the analysis cleanly instead of killing the process mid-scan.
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        using var terminate = TryRegisterTerminate(cancellation);

        return new CliApplication(Console.Out, Console.Error, new LinuxHostFeatureChecker())
            .Run(args, cancellation.Token);
    }

    private static PosixSignalRegistration? TryRegisterTerminate(CancellationTokenSource cancellation)
    {
        try
        {
            return PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
            {
                context.Cancel = true;
                cancellation.Cancel();
            });
        }
        catch (PlatformNotSupportedException)
        {
            // Ctrl+C still works through Console.CancelKeyPress.
            return null;
        }
    }
}
