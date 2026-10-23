using ELFSharp;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using Iced.Intel;

namespace InstructionSetAnalyzer;

public sealed class ElfInstructionSetAnalyzer
{
    private static readonly byte[] ElfMagic = [0x7F, (byte)'E', (byte)'L', (byte)'F'];

    public AnalysisResult Analyze(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!IsElf(path))
        {
            throw new InvalidDataException($"File is not an ELF binary: {path}");
        }

        using var elf = LoadElf(path);
        var bitness = GetBitness(elf);
        var features = new HashSet<CpuidFeature>();
        var executableSectionCount = 0;

        foreach (var section in elf.Sections.Where(IsExecutableCodeSection))
        {
            var bytes = section.GetContents();
            if (bytes.Length == 0)
            {
                continue;
            }

            executableSectionCount++;
            DecodeSection(bytes, bitness, GetLoadAddress(section), features);
        }

        if (executableSectionCount == 0)
        {
            throw new InvalidDataException($"ELF binary has no executable sections: {path}");
        }

        return new AnalysisResult(
            elf.Machine == Machine.AMD64 ? "x86-64" : "x86",
            executableSectionCount,
            features);
    }

    public static bool IsElf(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[ElfMagic.Length];
            return stream.Read(header) == header.Length && header.SequenceEqual(ElfMagic);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IELF LoadElf(string path)
    {
        try
        {
            return ELFReader.Load(path);
        }
        catch (Exception exception) when (exception is ArgumentException or EndOfStreamException or OverflowException)
        {
            throw new InvalidDataException($"ELF file is malformed or unsupported: {path}", exception);
        }
    }

    private static int GetBitness(IELF elf)
    {
        if (elf.Endianess != Endianess.LittleEndian)
        {
            throw new NotSupportedException("Big-endian ELF binaries are not supported.");
        }

        return (elf.Machine, elf.Class) switch
        {
            (Machine.AMD64, Class.Bit64) => 64,
            (Machine.Intel386 or Machine.Intel486, Class.Bit32) => 32,
            _ => throw new NotSupportedException(
                $"ELF architecture {elf.Machine} ({elf.Class}) is not supported; only x86 and x86-64 are supported.")
        };
    }

    private static bool IsExecutableCodeSection(ISection section) =>
        section.Type == SectionType.ProgBits &&
        (section.Flags & (SectionFlags.Allocatable | SectionFlags.Executable)) ==
        (SectionFlags.Allocatable | SectionFlags.Executable);

    private static ulong GetLoadAddress(ISection section) =>
        section switch
        {
            Section<ulong> section64 => section64.LoadAddress,
            Section<uint> section32 => section32.LoadAddress,
            _ => 0
        };

    private static void DecodeSection(
        byte[] bytes,
        int bitness,
        ulong instructionPointer,
        ISet<CpuidFeature> features)
    {
        var codeReader = new ByteArrayCodeReader(bytes);
        var decoder = Decoder.Create(bitness, codeReader, instructionPointer);

        while (codeReader.CanReadByte)
        {
            decoder.Decode(out var instruction);
            foreach (var feature in instruction.CpuidFeatures)
            {
                features.Add(feature);
            }
        }
    }
}

public sealed record AnalysisResult(
    string Architecture,
    int ExecutableSectionCount,
    IReadOnlySet<CpuidFeature> Features);
