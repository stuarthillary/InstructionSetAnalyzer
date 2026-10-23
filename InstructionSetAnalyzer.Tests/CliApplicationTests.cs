using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public sealed class CliApplicationTests : CliTestBase
{
    [Fact]
    public void Analyze_prints_features_for_a_single_file()
    {
        var path = Temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        var exitCode = Run("analyze", path);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Binary {path} (x86-64, 1 executable sections)", Output.ToString());
        Assert.Contains("CPU features used:", Output.ToString());
        Assert.Contains("AVX2", PrintedFeatures());
        Assert.Equal(string.Empty, Error.ToString());
        Assert.Null(Checker.LastRequest);
    }

    [Fact]
    public void Analyze_lists_features_in_ordinal_order()
    {
        var path = Temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        Run("analyze", path);

        var features = PrintedFeatures();

        Assert.Equal(features.Order(StringComparer.Ordinal), features);
    }

    [Fact]
    public void Analyze_unions_features_across_a_directory_recursively()
    {
        Temp.Write("a.so", TestElf.Build(TestElf.Avx));
        Temp.Write("nested/deeper/b", TestElf.Build([.. TestElf.Sse, .. TestElf.Avx2]));
        Temp.Write("readme.txt", "not elf"u8.ToArray());

        var exitCode = Run("analyze", Temp.Path);

        Assert.Equal(0, exitCode);
        var output = Output.ToString();
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
        Temp.Write("good.so", TestElf.Build(TestElf.Avx));
        Temp.Write("arm.so", TestElf.Build(TestElf.Sse, TestElf.MachineAArch64));

        var exitCode = Run("analyze", Temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("Analyzed ELF binaries: 1", Output.ToString());
        Assert.Contains("Skipped (unsupported architecture): 1", Output.ToString());
        Assert.Equal(string.Empty, Error.ToString());
    }

    [Fact]
    public void Analyze_directory_output_is_quiet_by_default()
    {
        var good = Temp.Write("good.so", TestElf.Build(TestElf.Avx));

        Run("analyze", Temp.Path);

        Assert.DoesNotContain($"Analyzed {good}", Output.ToString());
        Assert.DoesNotContain(good, Output.ToString());
    }

    [Theory]
    [InlineData("--verbose")]
    [InlineData("-v")]
    public void Analyze_verbose_lists_each_file_and_each_skip(string flag)
    {
        var good = Temp.Write("good.so", TestElf.Build(TestElf.Avx));
        var arm = Temp.Write("arm.so", TestElf.Build(TestElf.Sse, TestElf.MachineAArch64));

        var exitCode = Run("analyze", Temp.Path, flag);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Analyzed {good}", Output.ToString());
        Assert.Contains($"Warning: skipped {arm}", Error.ToString());
    }

    [Fact]
    public void Analyze_reports_failed_files_even_when_not_verbose()
    {
        Temp.Write("good.so", TestElf.Build(TestElf.Avx));
        var corrupt = Temp.Write("bad.so", TestElf.WithBadSectionNameIndex(TestElf.Build(TestElf.Avx)));

        Run("analyze", Temp.Path);

        Assert.Contains($"Warning: could not analyze {corrupt}", Error.ToString());
    }

    [Fact]
    public void Analyze_keeps_going_past_a_corrupt_elf_but_exits_with_an_error()
    {
        Temp.Write("good.so", TestElf.Build(TestElf.Avx2));
        var corrupt = Temp.Write("bad.so", TestElf.WithBadSectionNameIndex(TestElf.Build(TestElf.Avx)));

        var exitCode = Run("analyze", Temp.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("Analyzed ELF binaries: 1", Output.ToString());
        Assert.Contains("AVX2", PrintedFeatures());
        Assert.Contains($"Warning: could not analyze {corrupt}", Error.ToString());
        Assert.Contains("1 ELF file(s) could not be analyzed", Error.ToString());
    }

    [Fact]
    public void Analyze_keeps_an_incompatible_host_exit_code_when_some_files_failed()
    {
        Checker.Status = HostFeatureStatus.Unsupported;
        Temp.Write("good.so", TestElf.Build(TestElf.Avx2));
        Temp.Write("bad.so", TestElf.WithBadSectionNameIndex(TestElf.Build(TestElf.Avx)));

        Assert.Equal(2, Run("analyze", Temp.Path, "--check-host"));
    }

    [Fact]
    public void Analyze_describes_files_found_through_program_headers()
    {
        var path = Temp.Write("stripped.so", TestElf.Build(
            TestElf.Avx2, sectionHeaders: false, programHeader: true));

        var exitCode = Run("analyze", path);

        Assert.Equal(0, exitCode);
        Assert.Contains("1 executable segments, no executable sections", Output.ToString());
        Assert.Contains("AVX2", PrintedFeatures());
    }

    [Fact]
    public void Analyze_hides_privileged_features_unless_asked()
    {
        var path = Temp.Write("kernelish.so", TestElf.Build([.. TestElf.Rdmsr, .. TestElf.Avx]));

        Run("analyze", path);
        var defaultFeatures = PrintedFeatures();
        Output.GetStringBuilder().Clear();
        Run("analyze", path, "--include-privileged");
        var allFeatures = PrintedFeatures();

        Assert.DoesNotContain("MSR", defaultFeatures);
        Assert.Contains("MSR", allFeatures);
        Assert.Contains("AVX", allFeatures);
    }

    [Fact]
    public void Analyze_fails_when_a_directory_has_no_supported_binaries()
    {
        Temp.Write("readme.txt", "not elf"u8.ToArray());

        var exitCode = Run("analyze", Temp.Path);

        Assert.Equal(1, exitCode);
        Assert.Contains("No supported x86 or x86-64 ELF binaries were found.", Error.ToString());
    }

    [Fact]
    public void Analyze_fails_for_a_missing_path()
    {
        var exitCode = Run("analyze", System.IO.Path.Combine(Temp.Path, "missing"));

        Assert.Equal(1, exitCode);
        Assert.Contains("Path does not exist:", Error.ToString());
    }

    [Fact]
    public void Analyze_fails_for_a_non_elf_file()
    {
        var path = Temp.Write("notes.txt", "hello"u8.ToArray());

        var exitCode = Run("analyze", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("File is not an ELF binary", Error.ToString());
    }

    [Fact]
    public void Analyze_check_host_passes_detected_features_to_theChecker()
    {
        var path = Temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        var exitCode = Run("analyze", path, "--check-host");

        Assert.Equal(0, exitCode);
        Assert.NotNull(Checker.LastRequest);
        Assert.Contains(CpuidFeature.AVX2, Checker.LastRequest);
        Assert.Contains("Compatibility: all required features are supported.", Output.ToString());
    }

    [Fact]
    public void Analyze_check_host_returns_2_when_the_host_is_incompatible()
    {
        Checker.Status = HostFeatureStatus.Unsupported;
        var path = Temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

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
            Checker.LastRequest!.ToHashSet());
    }

    [Fact]
    public void Check_host_returns_2_when_a_feature_is_not_supported()
    {
        Checker.Status = HostFeatureStatus.Indeterminate;

        Assert.Equal(2, Run("check-host", "AVX"));
    }

    [Fact]
    public void Check_host_returns_1_for_unknown_feature_names()
    {
        var exitCode = Run("check-host", "NOT_A_FEATURE");

        Assert.Equal(1, exitCode);
        Assert.Contains("Unknown CPU feature name: NOT_A_FEATURE", Error.ToString());
        Assert.Null(Checker.LastRequest);
    }

    [Theory]
    [InlineData(typeof(PlatformNotSupportedException))]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public void Check_host_returns_1_when_the_environment_is_unusable(Type exceptionType)
    {
        Checker.Throw = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var exitCode = Run("check-host", "AVX");

        Assert.Equal(1, exitCode);
        Assert.Contains("Error: boom", Error.ToString());
    }

    [Fact]
    public void Check_host_requires_at_least_one_feature()
    {
        Assert.NotEqual(0, Run("check-host"));
        Assert.Null(Checker.LastRequest);
    }

    [Fact]
    public void Help_lists_both_commands()
    {
        var exitCode = Run("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("analyze", Output.ToString());
        Assert.Contains("check-host", Output.ToString());
    }
}
