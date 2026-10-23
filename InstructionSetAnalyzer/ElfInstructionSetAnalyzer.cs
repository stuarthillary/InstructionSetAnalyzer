using ELFSharp;
using ELFSharp.ELF;
using ELFSharp.ELF.Sections;
using Iced.Intel;

namespace InstructionSetAnalyzer;

public sealed class ElfInstructionSetAnalyzer
{
    private const ushort MachineIntel386 = 3;
    private const ushort MachineIntel486 = 6;
    private const ushort MachineAmd64 = 62;

    private static readonly byte[] ElfMagic = [0x7F, (byte)'E', (byte)'L', (byte)'F'];

    // Instructions that fault outside ring 0. Finding one in a user-space binary means the bytes are
    // data that happened to decode as code, so these are hidden unless explicitly requested.
    private static readonly HashSet<CpuidFeature> PrivilegedOnlyFeatures =
    [
        CpuidFeature.INVPCID,
        CpuidFeature.MSR,
        CpuidFeature.SKINIT_or_SVM,
        CpuidFeature.SMAP,
        CpuidFeature.SMM,
        CpuidFeature.SVM,
        CpuidFeature.VMX,
        CpuidFeature.WBNOINVD
    ];

    private readonly bool _includePrivilegedFeatures;

    public ElfInstructionSetAnalyzer(bool includePrivilegedFeatures = false)
    {
        _includePrivilegedFeatures = includePrivilegedFeatures;
    }

    public AnalysisResult Analyze(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!IsElf(path))
        {
            throw new InvalidDataException($"File is not an ELF binary: {path}");
        }

        try
        {
            return AnalyzeElf(path);
        }
        catch (Exception exception) when (IsMalformedInput(exception))
        {
            throw new InvalidDataException($"ELF file is malformed or unsupported: {path}", exception);
        }
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

    // ELFSharp parses lazily and signals corrupt input with whatever the failing read happens to throw,
    // including a plain IOException when a corrupt offset makes a seek fail.
    private static bool IsMalformedInput(Exception exception) =>
        exception is IndexOutOfRangeException
            or ArgumentException
            or OverflowException
            or InvalidOperationException
            or IOException and not (FileNotFoundException or DirectoryNotFoundException);

    private AnalysisResult AnalyzeElf(string path)
    {
        using var stream = File.OpenRead(path);
        var header = ElfFile.ReadHeader(stream);
        var bitness = GetBitness(header);
        var features = new HashSet<CpuidFeature>();
        var regionCount = 0;

        if (header.HasSectionHeaders)
        {
            using var elf = ELFReader.Load(path);
            foreach (var section in elf.Sections.Where(IsExecutableCodeSection))
            {
                var bytes = section.GetContents();
                if (bytes.Length == 0)
                {
                    continue;
                }

                regionCount++;
                DecodeRegion(bytes, bitness, GetLoadAddress(section), features);
            }
        }

        var source = CodeSource.Sections;
        if (regionCount == 0)
        {
            // Stripped binaries (for example after sstrip) have no section headers at all.
            source = CodeSource.Segments;
            foreach (var region in ElfFile.ReadExecutableSegments(stream, header))
            {
                regionCount++;
                DecodeRegion(region.Bytes, bitness, region.Address, features);
            }
        }

        if (regionCount == 0)
        {
            throw new InvalidDataException($"ELF binary has no executable sections or segments: {path}");
        }

        return new AnalysisResult(DescribeArchitecture(header), regionCount, source, features);
    }

    private static string DescribeArchitecture(ElfHeader header) =>
        header.Machine switch
        {
            MachineAmd64 when !header.Is64BitClass => "x86-64 (x32)",
            MachineAmd64 => "x86-64",
            _ => "x86"
        };

    private static int GetBitness(ElfHeader header) =>
        (header.Machine, header.Is64BitClass) switch
        {
            // The x32 ABI pairs the 64-bit machine type with the 32-bit ELF class but runs 64-bit code.
            (MachineAmd64, _) => 64,
            (MachineIntel386 or MachineIntel486, false) => 32,
            _ => throw new NotSupportedException(
                $"ELF architecture {FormatMachine(header.Machine)} ({(header.Is64BitClass ? "64-bit" : "32-bit")}) " +
                "is not supported; only x86 and x86-64 are supported.")
        };

    private static string FormatMachine(ushort machine) =>
        Enum.IsDefined((Machine)machine) ? ((Machine)machine).ToString() : $"machine {machine}";

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

    private void DecodeRegion(
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
            if (instruction.IsInvalid)
            {
                continue;
            }

            foreach (var feature in instruction.CpuidFeatures)
            {
                if (_includePrivilegedFeatures || !PrivilegedOnlyFeatures.Contains(feature))
                {
                    features.Add(feature);
                }
            }
        }
    }
}
