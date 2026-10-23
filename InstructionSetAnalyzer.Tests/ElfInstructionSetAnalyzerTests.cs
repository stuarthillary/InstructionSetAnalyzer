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
        Assert.Equal(1, result.ExecutableSectionCount);
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
}
