using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public class ElfInstructionSetAnalyzerTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly ElfInstructionSetAnalyzer _analyzer = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Reports_features_architecture_and_section_count_for_x86_64()
    {
        var path = _temp.Write("lib.so", TestElf.Build(TestElf.Avx2));

        var result = _analyzer.Analyze(path);

        Assert.Equal("x86-64", result.Architecture);
        Assert.Equal(1, result.ExecutableRegionCount);
        Assert.Equal(CodeSource.Sections, result.Source);
        Assert.Contains(CpuidFeature.AVX2, result.Features);
        Assert.DoesNotContain(CpuidFeature.AVX512F, result.Features);
    }

    [Fact]
    public void Decodes_32_bit_binaries()
    {
        var path = _temp.Write("tool32", TestElf.Build(
            TestElf.Sse, TestElf.MachineI386, is64Bit: false));

        var result = _analyzer.Analyze(path);

        Assert.Equal("x86", result.Architecture);
        Assert.Contains(CpuidFeature.SSE, result.Features);
    }

    [Fact]
    public void Does_not_depend_on_file_extension()
    {
        var path = _temp.Write("no-extension", TestElf.Build(TestElf.Avx));

        Assert.Contains(CpuidFeature.AVX, _analyzer.Analyze(path).Features);
    }

    [Fact]
    public void Rejects_files_that_are_not_elf()
    {
        var path = _temp.Write("notes.txt", "hello"u8.ToArray());

        Assert.Throws<InvalidDataException>(() => _analyzer.Analyze(path));
    }

    [Fact]
    public void Rejects_unsupported_architectures()
    {
        var path = _temp.Write("arm.so", TestElf.Build(TestElf.Sse, TestElf.MachineAArch64));

        var exception = Assert.Throws<NotSupportedException>(() => _analyzer.Analyze(path));

        Assert.Contains("only x86 and x86-64 are supported", exception.Message);
    }

    [Fact]
    public void Rejects_binaries_without_executable_sections()
    {
        var path = _temp.Write("data.so", TestElf.Build(TestElf.Avx, executable: false));

        var exception = Assert.Throws<InvalidDataException>(() => _analyzer.Analyze(path));

        Assert.Contains("no executable sections", exception.Message);
    }

    [Fact]
    public void Validates_the_path_argument()
    {
        Assert.Throws<ArgumentException>(() => _analyzer.Analyze(" "));
        Assert.Throws<ArgumentNullException>(() => _analyzer.Analyze(null!));
    }

    [Fact]
    public void IsElf_checks_the_magic_number_only()
    {
        Assert.True(ElfInstructionSetAnalyzer.IsElf(_temp.Write("a", TestElf.Build(TestElf.Avx))));
        Assert.False(ElfInstructionSetAnalyzer.IsElf(_temp.Write("b", "MZ-not-elf"u8.ToArray())));
        Assert.False(ElfInstructionSetAnalyzer.IsElf(_temp.Write("tiny", [0x7F, (byte)'E'])));
        Assert.False(ElfInstructionSetAnalyzer.IsElf(_temp.Write("empty", [])));
    }

    [Fact]
    public void IsElf_returns_false_for_missing_files()
    {
        Assert.False(ElfInstructionSetAnalyzer.IsElf(System.IO.Path.Combine(_temp.Path, "missing")));
    }

    [Fact]
    public void Falls_back_to_program_headers_when_there_are_no_section_headers()
    {
        var path = _temp.Write("stripped.so", TestElf.Build(
            TestElf.Avx2, sectionHeaders: false, programHeader: true));

        var result = _analyzer.Analyze(path);

        Assert.Equal(CodeSource.Segments, result.Source);
        Assert.Equal(1, result.ExecutableRegionCount);
        Assert.Contains(CpuidFeature.AVX2, result.Features);
    }

    [Fact]
    public void Program_header_fallback_supports_32_bit_binaries()
    {
        var path = _temp.Write("stripped32.so", TestElf.Build(
            TestElf.Sse, TestElf.MachineI386, is64Bit: false, sectionHeaders: false, programHeader: true));

        var result = _analyzer.Analyze(path);

        Assert.Equal("x86", result.Architecture);
        Assert.Contains(CpuidFeature.SSE, result.Features);
    }

    [Fact]
    public void Prefers_sections_over_segments_when_both_exist()
    {
        var path = _temp.Write("both.so", TestElf.Build(TestElf.Avx, programHeader: true));

        Assert.Equal(CodeSource.Sections, _analyzer.Analyze(path).Source);
    }

    [Fact]
    public void Decodes_x32_binaries_as_64_bit_code()
    {
        // x32 ABI: ELFCLASS32 with EM_X86_64. REX-prefixed code must decode as 64-bit.
        var path = _temp.Write("x32.so", TestElf.Build(
            [0xC5, 0xFD, 0x74, 0xC1], TestElf.MachineX86_64, is64Bit: false));

        var result = _analyzer.Analyze(path);

        Assert.Equal("x86-64 (x32)", result.Architecture);
        Assert.Contains(CpuidFeature.AVX2, result.Features);
    }

    [Fact]
    public void Reports_corrupt_section_headers_as_invalid_data_not_a_crash()
    {
        var path = _temp.Write("corrupt.so", TestElf.WithBadSectionNameIndex(TestElf.Build(TestElf.Avx)));

        var exception = Assert.Throws<InvalidDataException>(() => _analyzer.Analyze(path));

        Assert.Contains("malformed", exception.Message);
        Assert.Contains(path, exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Releases_the_file_even_when_parsing_fails(bool collectSymbols)
    {
        // ELFSharp keeps the handle it opened when it throws on a corrupt file, which locks the file on Windows.
        // FileShare.None is enforced with advisory locks on Unix, so an exclusive open exposes a leaked handle.
        var corrupt = _temp.Write("corrupt.so", TestElf.WithBadSectionNameIndex(DispatchFixture.Elf()));
        var truncated = _temp.Write("truncated.so", TestElf.Build(TestElf.Avx, programHeader: true)[..100]);
        var analyzer = new ElfInstructionSetAnalyzer(collectSymbols: collectSymbols);

        foreach (var path in new[] { corrupt, truncated })
        {
            Assert.Throws<InvalidDataException>(() => analyzer.Analyze(path));

            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
    }

    [Fact]
    public void Truncated_files_are_reported_as_invalid_data()
    {
        var full = TestElf.Build(TestElf.Avx, programHeader: true);
        var path = _temp.Write("truncated.so", full[..100]);

        Assert.Throws<InvalidDataException>(() => _analyzer.Analyze(path));
    }

    [Fact]
    public void Random_corruption_only_ever_produces_documented_exceptions()
    {
        var original = TestElf.Build([.. TestElf.Sse, .. TestElf.Avx2], programHeader: true);
        var random = new Random(20260101);
        var path = System.IO.Path.Combine(_temp.Path, "fuzz.bin");

        for (var iteration = 0; iteration < 1500; iteration++)
        {
            var data = (byte[])original.Clone();
            for (var edit = random.Next(1, 8); edit > 0; edit--)
            {
                data[random.Next(data.Length)] = (byte)random.Next(256);
            }

            if (iteration % 5 == 0)
            {
                data = data[..random.Next(data.Length)];
            }

            File.WriteAllBytes(path, data);
            try
            {
                _analyzer.Analyze(path);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
                // Documented outcomes for malformed or unsupported input.
            }
            catch (Exception exception)
            {
                Assert.Fail($"Iteration {iteration}: unexpected {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    [Fact]
    public void Excludes_privileged_only_features_by_default()
    {
        var path = _temp.Write("kernelish.so", TestElf.Build([.. TestElf.Rdmsr, .. TestElf.Avx]));

        var features = _analyzer.Analyze(path).Features;

        Assert.Contains(CpuidFeature.AVX, features);
        Assert.DoesNotContain(CpuidFeature.MSR, features);
    }

    [Fact]
    public void Can_include_privileged_features_on_request()
    {
        var path = _temp.Write("kernelish.so", TestElf.Build([.. TestElf.Rdmsr, .. TestElf.Avx]));
        var analyzer = new ElfInstructionSetAnalyzer(includePrivilegedFeatures: true);

        var features = analyzer.Analyze(path).Features;

        Assert.Contains(CpuidFeature.MSR, features);
        Assert.Contains(CpuidFeature.AVX, features);
    }
}
