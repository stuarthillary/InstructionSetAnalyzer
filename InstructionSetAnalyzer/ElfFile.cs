using System.Buffers.Binary;

namespace InstructionSetAnalyzer;

internal readonly record struct ElfHeader(
    bool Is64BitClass,
    ushort Machine,
    ulong ProgramHeaderOffset,
    ushort ProgramHeaderEntrySize,
    ushort ProgramHeaderCount,
    ulong SectionHeaderOffset)
{
    /// <summary>
    /// Per the ELF specification a zero section header offset means there is no section header table
    /// (for example after sstrip). ELFSharp mishandles that case, so callers must not hand it such files.
    /// </summary>
    public bool HasSectionHeaders => SectionHeaderOffset != 0;
}

internal readonly record struct CodeRegion(ulong Address, byte[] Bytes);

/// <summary>
/// A small bounds-checked reader for the ELF header and program headers. ELFSharp handles section
/// parsing, but it cannot be trusted on files without a section header table.
/// </summary>
internal static class ElfFile
{
    private const int ElfClass32 = 1;
    private const int ElfClass64 = 2;
    private const int ElfDataLittleEndian = 1;
    private const int ElfDataBigEndian = 2;
    private const int EIClass = 4;
    private const int EIData = 5;
    private const int Elf32HeaderSize = 52;
    private const int Elf64HeaderSize = 64;
    private const int Elf32ProgramHeaderSize = 32;
    private const int Elf64ProgramHeaderSize = 56;
    private const uint PtLoad = 1;
    private const uint PfExecute = 1;

    public static ElfHeader ReadHeader(Stream stream)
    {
        Span<byte> identification = stackalloc byte[Elf64HeaderSize];
        stream.Position = 0;
        var read = stream.ReadAtLeast(identification, EIData + 1, throwOnEndOfStream: false);
        if (read <= EIData)
        {
            throw new InvalidDataException("ELF header is truncated.");
        }

        var elfClass = identification[EIClass];
        var is64 = elfClass == ElfClass64;
        if (!is64 && elfClass != ElfClass32)
        {
            throw new InvalidDataException($"ELF header has an invalid class value ({elfClass}).");
        }

        switch (identification[EIData])
        {
            case ElfDataLittleEndian:
                break;
            case ElfDataBigEndian:
                throw new NotSupportedException("Big-endian ELF binaries are not supported.");
            default:
                throw new InvalidDataException($"ELF header has an invalid data encoding ({identification[EIData]}).");
        }

        var headerSize = is64 ? Elf64HeaderSize : Elf32HeaderSize;
        if (read < headerSize)
        {
            throw new InvalidDataException("ELF header is truncated.");
        }

        var header = identification[..headerSize];
        var machine = BinaryPrimitives.ReadUInt16LittleEndian(header[18..]);
        return is64
            ? new ElfHeader(
                true,
                machine,
                BinaryPrimitives.ReadUInt64LittleEndian(header[32..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[54..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[56..]),
                BinaryPrimitives.ReadUInt64LittleEndian(header[40..]))
            : new ElfHeader(
                false,
                machine,
                BinaryPrimitives.ReadUInt32LittleEndian(header[28..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[42..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[44..]),
                BinaryPrimitives.ReadUInt32LittleEndian(header[32..]));
    }

    /// <summary>Reads the file contents of every executable PT_LOAD segment.</summary>
    public static IReadOnlyList<CodeRegion> ReadExecutableSegments(Stream stream, ElfHeader header)
    {
        var regions = new List<CodeRegion>();
        if (header.ProgramHeaderCount == 0)
        {
            return regions;
        }

        var minimumEntrySize = header.Is64BitClass ? Elf64ProgramHeaderSize : Elf32ProgramHeaderSize;
        if (header.ProgramHeaderEntrySize < minimumEntrySize)
        {
            throw new InvalidDataException("ELF program header entries are too small.");
        }

        var fileLength = (ulong)stream.Length;
        var tableSize = (ulong)header.ProgramHeaderEntrySize * header.ProgramHeaderCount;
        if (header.ProgramHeaderOffset > fileLength || tableSize > fileLength - header.ProgramHeaderOffset)
        {
            throw new InvalidDataException("ELF program header table extends past the end of the file.");
        }

        var table = new byte[tableSize];
        stream.Position = (long)header.ProgramHeaderOffset;
        stream.ReadExactly(table);

        for (var index = 0; index < header.ProgramHeaderCount; index++)
        {
            var entry = table.AsSpan(index * header.ProgramHeaderEntrySize, header.ProgramHeaderEntrySize);
            var (type, flags, offset, address, size) = header.Is64BitClass
                ? (BinaryPrimitives.ReadUInt32LittleEndian(entry),
                    BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]),
                    BinaryPrimitives.ReadUInt64LittleEndian(entry[32..]))
                : (BinaryPrimitives.ReadUInt32LittleEndian(entry),
                    BinaryPrimitives.ReadUInt32LittleEndian(entry[24..]),
                    (ulong)BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]),
                    (ulong)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]),
                    (ulong)BinaryPrimitives.ReadUInt32LittleEndian(entry[16..]));

            if (type != PtLoad || (flags & PfExecute) == 0 || size == 0)
            {
                continue;
            }

            if (offset > fileLength || size > fileLength - offset || size > int.MaxValue)
            {
                throw new InvalidDataException("ELF executable segment extends past the end of the file.");
            }

            var bytes = new byte[size];
            stream.Position = (long)offset;
            stream.ReadExactly(bytes);
            regions.Add(new CodeRegion(address, bytes));
        }

        return regions;
    }
}
