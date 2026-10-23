using System.Collections.Concurrent;

namespace InstructionSetAnalyzer;

internal static class DirectoryScanner
{
    /// <summary>
    /// Analyzes every ELF file under <paramref name="directory"/> using several threads. The result lists files
    /// sorted by path, so the output does not depend on thread timing.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static ScanResult Scan(
        string directory,
        ElfInstructionSetAnalyzer analyzer,
        int maxDegreeOfParallelism,
        CancellationToken cancellationToken)
    {
        var analyzed = new ConcurrentBag<AnalyzedFile>();
        var skipped = new ConcurrentBag<SkippedFile>();
        var failed = new ConcurrentBag<FailedFile>();
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };
        var parallelism = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism),
            CancellationToken = cancellationToken
        };

        Parallel.ForEach(Directory.EnumerateFiles(directory, "*", enumeration), parallelism, file =>
        {
            if (!ElfInstructionSetAnalyzer.IsElf(file))
            {
                return;
            }

            try
            {
                if (analyzer.AnalyzeIfCode(file, cancellationToken) is { } result)
                {
                    analyzed.Add(new AnalyzedFile(file, result));
                }
                else
                {
                    skipped.Add(new SkippedFile(
                        file,
                        SkipReason.NoExecutableCode,
                        "ELF binary has no executable code."));
                }
            }
            catch (NotSupportedException exception)
            {
                // Unsupported architectures are expected in mixed directories and do not make the scan fail.
                skipped.Add(new SkippedFile(file, SkipReason.UnsupportedArchitecture, exception.Message));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failed.Add(new FailedFile(file, exception.Message));
            }
        });

        return new ScanResult(analyzed, skipped, failed);
    }
}
