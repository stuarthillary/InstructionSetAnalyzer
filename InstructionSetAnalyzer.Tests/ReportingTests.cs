using System.Text.Json;
using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public sealed class ReportingTests : CliTestBase
{
    private JsonElement Json() => JsonDocument.Parse(Output.ToString()).RootElement;

    private static JsonElement Feature(JsonElement file, string name) =>
        file.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("name").GetString() == name);

    // ---- breakdown -------------------------------------------------------------------------------------------

    [Fact]
    public void Detail_lists_instruction_counts_per_feature_and_file()
    {
        var path = Temp.Write("lib.so", DispatchFixture.Elf());

        var exitCode = Run("analyze", path, "--detail");

        Assert.Equal(0, exitCode);
        var output = Output.ToString();
        Assert.Contains("Feature breakdown:", output);
        Assert.Contains("  AVX2: 3 instructions in 1 file", output);
        Assert.Contains("    lib.so: 3", output);
        Assert.DoesNotContain("memchr_avx2", output); // functions only with --symbols
    }

    [Fact]
    public void Symbols_show_functions_and_mark_ifunc_variants()
    {
        var path = Temp.Write("lib.so", DispatchFixture.Elf());

        Run("analyze", path, "--detail", "--symbols");

        var output = Output.ToString();
        Assert.Contains("memchr_avx2: 2 [ifunc variant of my_memchr]", output);
        Assert.Contains("unreferenced_avx2: 1", output);
        Assert.DoesNotContain("unreferenced_avx2: 1 [ifunc", output);
        Assert.Contains("2 inside ifunc variants", output);
    }

    [Fact]
    public void Symbols_say_when_code_has_no_symbol()
    {
        var path = Temp.Write("lib.so", DispatchFixture.Elf(withSymbols: false));

        Run("analyze", path, "--detail", "--symbols");

        Assert.Contains("(3 instructions in code with no symbol)", Output.ToString());
    }

    [Fact]
    public void Detail_aggregates_across_a_directory_and_names_files_relative_to_it()
    {
        Temp.Write("a.so", TestElf.Build(TestElf.Avx2));
        Temp.Write("sub/b.so", TestElf.Build([.. TestElf.Avx2, .. TestElf.Avx2]));

        Run("analyze", Temp.Path, "--detail");

        var output = Output.ToString();
        Assert.Contains("  AVX2: 3 instructions in 2 files", output);
        Assert.Contains($"    {Path.Combine("sub", "b.so")}: 2", output);
        Assert.Contains("    a.so: 1", output);
        Assert.True(output.IndexOf("b.so", StringComparison.Ordinal) < output.IndexOf("a.so: 1", StringComparison.Ordinal),
            "files should be listed busiest first");
    }

    [Fact]
    public void Default_output_has_no_breakdown_or_baseline_sections()
    {
        var path = Temp.Write("lib.so", DispatchFixture.Elf());

        Run("analyze", path);

        Assert.DoesNotContain("Feature breakdown", Output.ToString());
        Assert.DoesNotContain("Baseline", Output.ToString());
    }

    // ---- baseline --------------------------------------------------------------------------------------------

    [Fact]
    public void Baseline_fails_with_exit_code_2_when_a_newer_feature_is_used()
    {
        var path = Temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        var exitCode = Run("analyze", path, "--baseline", "x86-64-v2");

        Assert.Equal(2, exitCode);
        Assert.Contains("Baseline x86-64-v2: exceeded by AVX2", Output.ToString());
    }

    [Fact]
    public void Baseline_passes_when_the_level_covers_every_feature()
    {
        var path = Temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        var exitCode = Run("analyze", path, "--baseline", "x86-64-v3");

        Assert.Equal(0, exitCode);
        Assert.Contains("Baseline x86-64-v3: met.", Output.ToString());
    }

    [Fact]
    public void Baseline_names_the_files_that_exceed_it_in_a_directory()
    {
        Temp.Write("new.so", TestElf.Build(TestElf.Avx2));
        Temp.Write("old.so", TestElf.Build(TestElf.Sse));

        var exitCode = Run("analyze", Temp.Path, "--baseline", "x86-64-v2");

        Assert.Equal(2, exitCode);
        Assert.Contains("  new.so: AVX2", Output.ToString());
        Assert.DoesNotContain("  old.so:", Output.ToString());
    }

    [Fact]
    public void Baseline_rejects_unknown_level_names()
    {
        var path = Temp.Write("lib.so", TestElf.Build(TestElf.Sse));

        var exitCode = Run("analyze", path, "--baseline", "v3");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("x86-64-v3", Error.ToString());
    }

    [Fact]
    public void Baseline_exit_code_2_wins_over_failed_files()
    {
        Temp.Write("new.so", TestElf.Build(TestElf.Avx2));
        Temp.Write("bad.so", TestElf.WithBadSectionNameIndex(TestElf.Build(TestElf.Avx)));

        Assert.Equal(2, Run("analyze", Temp.Path, "--baseline", "x86-64"));
    }

    // ---- ifunc-dispatched features -----------------------------------------------------------------------------

    [Fact]
    public void Exclude_dispatched_moves_variant_only_features_out_of_the_checked_set()
    {
        var path = Temp.Write("lib.so", DispatchFixture.ElfWithAvx2OnlyInVariant());

        var exitCode = Run("analyze", path, "--exclude-dispatched", "--baseline", "x86-64-v2");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("AVX2", PrintedFeatures());
        Assert.Contains("SSE", PrintedFeatures());
        Assert.Equal(
            ["AVX2"],
            FeaturesUnder("Used only inside ifunc-dispatched variants (not counted by checks):"));
        Assert.Contains("Baseline x86-64-v2: met.", Output.ToString());
    }

    [Fact]
    public void Without_exclude_dispatched_the_same_binary_exceeds_the_baseline()
    {
        var path = Temp.Write("lib.so", DispatchFixture.ElfWithAvx2OnlyInVariant());

        Assert.Equal(2, Run("analyze", path, "--baseline", "x86-64-v2"));
        Assert.Contains("AVX2", PrintedFeatures());
    }

    [Fact]
    public void Exclude_dispatched_keeps_features_that_are_also_used_elsewhere()
    {
        var path = Temp.Write("lib.so", DispatchFixture.Elf()); // unreferenced_avx2 also uses AVX2

        Run("analyze", path, "--exclude-dispatched");

        Assert.Contains("AVX2", PrintedFeatures());
    }

    [Fact]
    public void Exclude_dispatched_removes_variant_only_features_from_the_host_check()
    {
        var path = Temp.Write("lib.so", DispatchFixture.ElfWithAvx2OnlyInVariant());

        Run("analyze", path, "--check-host", "--exclude-dispatched");
        var excluded = Checker.LastRequest!.ToArray();
        Run("analyze", path, "--check-host");

        Assert.DoesNotContain(CpuidFeature.AVX2, excluded);
        Assert.Contains(CpuidFeature.AVX2, Checker.LastRequest!);
    }

    [Fact]
    public void Exclude_dispatched_does_nothing_for_stripped_code()
    {
        var path = Temp.Write("lib.so", DispatchFixture.Elf(withSymbols: false));

        Run("analyze", path, "--exclude-dispatched");

        Assert.Contains("AVX2", PrintedFeatures());
        Assert.DoesNotContain("Used only inside", Output.ToString());
    }

    // ---- JSON ------------------------------------------------------------------------------------------------

    [Fact]
    public void Json_describes_a_single_file()
    {
        var path = Temp.Write("lib.so", DispatchFixture.Elf());

        var exitCode = Run("analyze", path, "--json");

        Assert.Equal(0, exitCode);
        var root = Json(); // the whole of standard output must be one JSON document
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("file", root.GetProperty("kind").GetString());
        Assert.Equal(path, root.GetProperty("path").GetString());
        Assert.Equal(1, root.GetProperty("summary").GetProperty("analyzed").GetInt32());
        Assert.Contains("AVX2", root.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        var file = root.GetProperty("files")[0];
        Assert.Equal("x86-64", file.GetProperty("architecture").GetString());
        Assert.Equal("sections", file.GetProperty("codeSource").GetString());
        Assert.Equal(3, Feature(file, "AVX2").GetProperty("instructions").GetInt64());
        Assert.False(Feature(file, "AVX2").TryGetProperty("symbols", out _));
    }

    [Fact]
    public void Json_includes_symbols_only_when_requested()
    {
        var path = Temp.Write("lib.so", DispatchFixture.Elf());

        Run("analyze", path, "--json", "--symbols");

        var avx2 = Feature(Json().GetProperty("files")[0], "AVX2");
        Assert.Equal(2, avx2.GetProperty("dispatchedInstructions").GetInt64());
        var symbol = avx2.GetProperty("symbols")[0];
        Assert.Equal("memchr_avx2", symbol.GetProperty("name").GetString());
        Assert.Equal("0x1010", symbol.GetProperty("address").GetString());
        Assert.Equal(2, symbol.GetProperty("instructions").GetInt64());
        Assert.Equal("my_memchr", symbol.GetProperty("dispatchedBy")[0].GetString());
    }

    [Fact]
    public void Json_reports_skipped_and_failed_files_and_keeps_warnings_on_standard_error()
    {
        Temp.Write("good.so", TestElf.Build(TestElf.Avx2));
        var arm = Temp.Write("arm.so", TestElf.Build(TestElf.Sse, TestElf.MachineAArch64));
        var empty = Temp.Write("empty.o", TestElf.Build([]));
        var bad = Temp.Write("bad.so", TestElf.WithBadSectionNameIndex(TestElf.Build(TestElf.Avx)));

        var exitCode = Run("analyze", Temp.Path, "--json");

        Assert.Equal(1, exitCode);
        var root = Json();
        Assert.Equal("directory", root.GetProperty("kind").GetString());
        var summary = root.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("analyzed").GetInt32());
        Assert.Equal(2, summary.GetProperty("skipped").GetInt32());
        Assert.Equal(1, summary.GetProperty("failed").GetInt32());
        var skipped = root.GetProperty("skipped").EnumerateArray().ToDictionary(
            file => file.GetProperty("path").GetString()!, file => file.GetProperty("reason").GetString());
        Assert.Equal("unsupported-architecture", skipped[arm]);
        Assert.Equal("no-executable-code", skipped[empty]);
        Assert.Equal(bad, root.GetProperty("failed")[0].GetProperty("path").GetString());
        Assert.Contains($"Warning: could not analyze {bad}", Error.ToString());
    }

    [Fact]
    public void Json_includes_the_baseline_result()
    {
        var path = Temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        var exitCode = Run("analyze", path, "--json", "--baseline", "x86-64-v2");

        Assert.Equal(2, exitCode);
        var baseline = Json().GetProperty("baseline");
        Assert.Equal("x86-64-v2", baseline.GetProperty("level").GetString());
        Assert.False(baseline.GetProperty("met").GetBoolean());
        Assert.Equal(["AVX2"], baseline.GetProperty("exceeding").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(path, baseline.GetProperty("files")[0].GetProperty("path").GetString());
    }

    [Fact]
    public void Json_separates_required_from_dispatched_only_features()
    {
        var path = Temp.Write("lib.so", DispatchFixture.ElfWithAvx2OnlyInVariant());

        Run("analyze", path, "--json", "--exclude-dispatched");

        var root = Json();
        Assert.Contains("AVX2", root.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        Assert.DoesNotContain("AVX2", root.GetProperty("requiredFeatures").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(["AVX2"], root.GetProperty("dispatchedOnlyFeatures").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public void Json_includes_the_host_check()
    {
        var path = Temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        var exitCode = Run("analyze", path, "--json", "--check-host");

        Assert.Equal(0, exitCode);
        var host = Json().GetProperty("host");
        Assert.True(host.GetProperty("compatible").GetBoolean());
        var feature = host.GetProperty("features").EnumerateArray().Single(f => f.GetProperty("feature").GetString() == "AVX2");
        Assert.Equal("supported", feature.GetProperty("status").GetString());
    }

    [Fact]
    public void Json_check_host_command_writes_only_the_host_report()
    {
        Checker.Status = HostFeatureStatus.PermissionRequired;

        var exitCode = Run("check-host", "AVX2", "--json");

        Assert.Equal(2, exitCode);
        var root = Json();
        Assert.Equal("host", root.GetProperty("kind").GetString());
        Assert.False(root.TryGetProperty("files", out _));
        Assert.Equal(
            "permissionRequired",
            root.GetProperty("host").GetProperty("features")[0].GetProperty("status").GetString());
    }

    [Fact]
    public void Json_is_still_written_when_a_directory_has_no_supported_binaries()
    {
        Temp.Write("readme.txt", "not elf"u8.ToArray());

        var exitCode = Run("analyze", Temp.Path, "--json");

        Assert.Equal(1, exitCode);
        Assert.Equal(0, Json().GetProperty("summary").GetProperty("analyzed").GetInt32());
        Assert.Contains("No supported x86 or x86-64 ELF binaries were found.", Error.ToString());
    }

    // ---- files without code ----------------------------------------------------------------------------------

    [Fact]
    public void A_directory_scan_skips_valid_elf_files_that_contain_no_code()
    {
        Temp.Write("good.so", TestElf.Build(TestElf.Avx));
        var empty = Temp.Write("crt1.o", TestElf.Build([]));

        var exitCode = Run("analyze", Temp.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains("Analyzed ELF binaries: 1", Output.ToString());
        Assert.Contains("Skipped (no executable code): 1", Output.ToString());
        Assert.Equal(string.Empty, Error.ToString());

        Output.GetStringBuilder().Clear();
        Run("analyze", Temp.Path, "--verbose");
        Assert.Contains($"Warning: skipped {empty}", Error.ToString());
    }

    [Fact]
    public void Analyzing_a_single_file_without_code_is_still_an_error()
    {
        var path = Temp.Write("crt1.o", TestElf.Build([]));

        var exitCode = Run("analyze", path);

        Assert.Equal(1, exitCode);
        Assert.Contains("no executable sections or segments", Error.ToString());
    }

    // ---- parallel scanning ---------------------------------------------------------------------------------

    [Fact]
    public void Output_is_identical_whatever_the_degree_of_parallelism()
    {
        byte[][] codes = [TestElf.Avx, TestElf.Avx2, TestElf.Sse, [.. TestElf.Sse, .. TestElf.Avx2]];
        for (var i = 0; i < 60; i++)
        {
            Temp.Write($"d{i % 7}/lib{i}.so", TestElf.Build(codes[i % codes.Length]));
        }

        Temp.Write("d3/arm.so", TestElf.Build(TestElf.Sse, TestElf.MachineAArch64));
        Temp.Write("d5/bad.so", TestElf.WithBadSectionNameIndex(TestElf.Build(TestElf.Avx)));
        string[] args = ["analyze", Temp.Path, "--verbose", "--detail", "--baseline", "x86-64-v2"];

        var sequential = Run([.. args, "--jobs", "1"]);
        var sequentialOutput = (Output.ToString(), Error.ToString());
        Output.GetStringBuilder().Clear();
        Error.GetStringBuilder().Clear();
        var parallel = Run([.. args, "--jobs", "16"]);

        Assert.Equal(sequential, parallel);
        Assert.Equal(sequentialOutput.Item1, Output.ToString());
        Assert.Equal(sequentialOutput.Item2, Error.ToString());
        Assert.Contains("Analyzed ELF binaries: 60", Output.ToString());
    }

    [Fact]
    public void Jobs_must_be_at_least_one()
    {
        var exitCode = Run("analyze", Temp.Path, "--jobs", "0");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--jobs must be at least 1", Error.ToString());
    }

    // ---- cancellation ----------------------------------------------------------------------------------------

    [Fact]
    public void A_cancelled_directory_scan_stops_with_exit_code_130()
    {
        for (var i = 0; i < 20; i++)
        {
            Temp.Write($"lib{i}.so", TestElf.Build(TestElf.Avx2));
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exitCode = RunWith(cancellation.Token, "analyze", Temp.Path);

        Assert.Equal(130, exitCode);
        Assert.Contains("Analysis cancelled.", Error.ToString());
        Assert.DoesNotContain("CPU features used", Output.ToString());
    }

    [Fact]
    public void A_cancelled_single_file_analysis_stops_with_exit_code_130()
    {
        var path = Temp.Write("big.so", TestElf.Build(Enumerable.Repeat((byte)0x90, 20_000).ToArray()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(130, RunWith(cancellation.Token, "analyze", path));
    }
}
