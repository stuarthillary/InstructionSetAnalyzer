using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public sealed class CliApplicationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();
    private readonly FakeHostChecker _checker = new();

    public void Dispose() => _temp.Dispose();

    private int Run(params string[] args) =>
        new CliApplication(_output, _error, _checker).Run(args);

    private string[] PrintedFeatures() =>
        _output.ToString()
            .Split(Environment.NewLine)
            .SkipWhile(line => line != "CPU features used:")
            .Skip(1)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

    [Fact]
    public void Analyze_prints_features_for_a_single_file()
    {
        var path = _temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        var exitCode = Run("analyze", path);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Binary {path} (x86-64, 1 executable sections)", _output.ToString());
        Assert.Contains("CPU features used:", _output.ToString());
        Assert.Contains("AVX2", PrintedFeatures());
        Assert.Equal(string.Empty, _error.ToString());
        Assert.Null(_checker.LastRequest);
    }

    [Fact]
    public void Analyze_lists_features_in_ordinal_order()
    {
        var path = _temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        Run("analyze", path);

        var features = PrintedFeatures();

        Assert.Equal(features.Order(StringComparer.Ordinal), features);
    }

    [Fact]
    public void Analyze_unions_features_across_a_directory_recursively()
    {
        _temp.Write("a.so", TestElf.Build(TestElf.Avx));
        _temp.Write("nested/deeper/b", TestElf.Build([.. TestElf.Sse, .. TestElf.Avx2]));
        _temp.Write("readme.txt", "not elf"u8.ToArray());

        var exitCode = Run("analyze", _temp.Path);

        Assert.Equal(0, exitCode);
        var output = _output.ToString();
        Assert.Contains("Analyzed ELF binaries: 2", output);
        Assert.DoesNotContain("readme.txt", output);
        var features = PrintedFeatures();
        Assert.Contains("AVX", features);
        Assert.Contains("SSE", features);
        Assert.Contains("AVX2", features);
    }

    [Fact]
    public void Analyze_skips_unsupported_architectures_in_a_directory_without_failing()
    {
        _temp.Write("good.so", TestElf.Build(TestElf.Avx));
        var arm = _temp.Write("arm.so", TestElf.Build(TestElf.Sse, TestElf.MachineAArch64));

        var exitCode = Run("analyze", _temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("Analyzed ELF binaries: 1", _output.ToString());
        Assert.Contains("Skipped (unsupported architecture): 1", _output.ToString());
        Assert.Contains($"Warning: skipped {arm}", _error.ToString());
    }

    [Fact]
    public void Analyze_keeps_going_past_a_corrupt_elf_but_exits_with_an_error()
    {
        _temp.Write("good.so", TestElf.Build(TestElf.Avx2));
        var corrupt = _temp.Write("bad.so", TestElf.WithBadSectionNameIndex(TestElf.Build(TestElf.Avx)));

        var exitCode = Run("analyze", _temp.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("Analyzed ELF binaries: 1", _output.ToString());
        Assert.Contains("AVX2", PrintedFeatures());
        Assert.Contains($"Warning: could not analyze {corrupt}", _error.ToString());
        Assert.Contains("1 ELF file(s) could not be analyzed", _error.ToString());
    }

    [Fact]
    public void Analyze_keeps_an_incompatible_host_exit_code_when_some_files_failed()
    {
        _checker.Status = HostFeatureStatus.Unsupported;
        _temp.Write("good.so", TestElf.Build(TestElf.Avx2));
        _temp.Write("bad.so", TestElf.WithBadSectionNameIndex(TestElf.Build(TestElf.Avx)));

        Assert.Equal(2, Run("analyze", _temp.Path, "--check-host"));
    }

    [Fact]
    public void Analyze_describes_files_found_through_program_headers()
    {
        var path = _temp.Write("stripped.so", TestElf.Build(
            TestElf.Avx2, sectionHeaders: false, programHeader: true));

        var exitCode = Run("analyze", path);

        Assert.Equal(0, exitCode);
        Assert.Contains("1 executable segments, no executable sections", _output.ToString());
        Assert.Contains("AVX2", PrintedFeatures());
    }

    [Fact]
    public void Analyze_hides_privileged_features_unless_asked()
    {
        var path = _temp.Write("kernelish.so", TestElf.Build([.. TestElf.Rdmsr, .. TestElf.Avx]));

        Run("analyze", path);
        var defaultFeatures = PrintedFeatures();
        _output.GetStringBuilder().Clear();
        Run("analyze", path, "--include-privileged");
        var allFeatures = PrintedFeatures();

        Assert.DoesNotContain("MSR", defaultFeatures);
        Assert.Contains("MSR", allFeatures);
        Assert.Contains("AVX", allFeatures);
    }

    [Fact]
    public void Analyze_fails_when_a_directory_has_no_supported_binaries()
    {
        _temp.Write("readme.txt", "not elf"u8.ToArray());

        var exitCode = Run("analyze", _temp.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("No supported x86 or x86-64 ELF binaries were found.", _error.ToString());
    }

    [Fact]
    public void Analyze_fails_for_a_missing_path()
    {
        var exitCode = Run("analyze", System.IO.Path.Combine(_temp.Path, "missing"));

        Assert.Equal(1, exitCode);
        Assert.Contains("Path does not exist:", _error.ToString());
    }

    [Fact]
    public void Analyze_fails_for_a_non_elf_file()
    {
        var path = _temp.Write("notes.txt", "hello"u8.ToArray());

        var exitCode = Run("analyze", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("File is not an ELF binary", _error.ToString());
    }

    [Fact]
    public void Analyze_check_host_passes_detected_features_to_the_checker()
    {
        var path = _temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        var exitCode = Run("analyze", path, "--check-host");

        Assert.Equal(0, exitCode);
        Assert.NotNull(_checker.LastRequest);
        Assert.Contains(CpuidFeature.AVX2, _checker.LastRequest);
        Assert.Contains("Compatibility: all required features are supported.", _output.ToString());
    }

    [Fact]
    public void Analyze_check_host_returns_2_when_the_host_is_incompatible()
    {
        _checker.Status = HostFeatureStatus.Unsupported;
        var path = _temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        Assert.Equal(2, Run("analyze", path, "--check-host"));
    }

    [Fact]
    public void Check_host_parses_feature_names_from_all_supported_formats()
    {
        var exitCode = Run("check-host", "AVX", "avx2,FMA", "AES BMI1");

        Assert.Equal(0, exitCode);
        Assert.Equal(
            new[] { CpuidFeature.AVX, CpuidFeature.AVX2, CpuidFeature.FMA, CpuidFeature.AES, CpuidFeature.BMI1 }
                .ToHashSet(),
            _checker.LastRequest!.ToHashSet());
    }

    [Fact]
    public void Check_host_returns_2_when_a_feature_is_not_supported()
    {
        _checker.Status = HostFeatureStatus.Indeterminate;

        Assert.Equal(2, Run("check-host", "AVX"));
    }

    [Fact]
    public void Check_host_returns_1_for_unknown_feature_names()
    {
        var exitCode = Run("check-host", "NOT_A_FEATURE");

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown CPU feature name: NOT_A_FEATURE", _error.ToString());
        Assert.Null(_checker.LastRequest);
    }

    [Theory]
    [InlineData(typeof(PlatformNotSupportedException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Check_host_returns_1_when_the_environment_is_unusable(Type exceptionType)
    {
        _checker.Throw = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var exitCode = Run("check-host", "AVX");

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: boom", _error.ToString());
    }

    [Fact]
    public void Check_host_requires_at_least_one_feature()
    {
        Assert.NotEqual(0, Run("check-host"));
        Assert.Null(_checker.LastRequest);
    }

    [Fact]
    public void Help_lists_both_commands()
    {
        var exitCode = Run("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("analyze", _output.ToString());
        Assert.Contains("check-host", _output.ToString());
    }

    private sealed class FakeHostChecker : IHostFeatureChecker
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
