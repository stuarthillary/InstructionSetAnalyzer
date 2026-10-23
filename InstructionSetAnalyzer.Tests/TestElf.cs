using System.Text;

namespace InstructionSetAnalyzer.Tests;

/// <summary>A function symbol placed in the test ELF's .symtab. <paramref name="Offset"/> is relative to the code.</summary>
internal sealed record TestSymbol(
    string Name, int Offset, int Size, bool Ifunc = false, ulong? RawValue = null, ulong? RawSize = null);

/// <summary>Builds minimal little-endian ELF files containing a single code region.</summary>
internal static class TestElf
{
    public const ulong CodeAddress = 0x1000;

    public const ushort MachineI386 = 3;
    public const ushort MachineX86_64 = 62;
    public const ushort MachineAArch64 = 183;

    private const uint ShtProgBits = 1;
    private const uint ShtSymTab = 2;
    private const uint ShtStrTab = 3;
    private const uint ShfAlloc = 0x2;
    private const uint ShfExecInstr = 0x4;
    private const uint PtLoad = 1;
    private const uint PfExecuteRead = 0x5;

    // vmovdqa ymm0, ymm1
    public static readonly byte[] Avx = [0xC5, 0xFD, 0x6F, 0xC1];

    // vpcmpeqb ymm0, ymm0, ymm1
    public static readonly byte[] Avx2 = [0xC5, 0xFD, 0x74, 0xC1];

    // movaps xmm0, xmm1
    public static readonly byte[] Sse = [0x0F, 0x28, 0xC1];

    // rdmsr (privileged: only meaningful in ring 0)
    public static readonly byte[] Rdmsr = [0x0F, 0x32];

    /// <param name="executable">Whether the code section carries the executable flag.</param>
    /// <param name="sectionHeaders">When false the file has no section header table, like an sstrip'ed binary.</param>
    /// <param name="programHeader">When true a single executable PT_LOAD segment covers the code.</param>
    public static byte[] Build(
        byte[] code,
        ushort machine = MachineX86_64,
        bool is64Bit = true,
        bool executable = true,
        bool sectionHeaders = true,
        bool programHeader = false,
        IReadOnlyList<TestSymbol>? symbols = null)
    {
        var hasSymbols = symbols is not null && sectionHeaders;
        var sectionNames = "\0.text\0.shstrtab\0.symtab\0.strtab\0"u8.ToArray();
        const int textNameOffset = 1;
        const int shstrtabNameOffset = 7;
        const int symtabNameOffset = 17;
        const int strtabNameOffset = 25;
        var (symbolTable, symbolNames) = hasSymbols
            ? BuildSymbolTable(symbols!, is64Bit)
            : ([], []);

        var headerSize = is64Bit ? 64 : 52;
        var programHeaderSize = is64Bit ? 56 : 32;
        var sectionHeaderSize = is64Bit ? 64 : 40;
        var programHeadersOffset = programHeader ? headerSize : 0;
        var textOffset = headerSize + (programHeader ? programHeaderSize : 0);
        var strtabOffset = textOffset + code.Length;
        var symtabOffset = (strtabOffset + sectionNames.Length + 7) & ~7;
        var symbolNamesOffset = symtabOffset + symbolTable.Length;
        var sectionHeadersEnd = hasSymbols ? symbolNamesOffset + symbolNames.Length : strtabOffset + sectionNames.Length;
        var sectionHeadersOffset = sectionHeaders ? (sectionHeadersEnd + 7) & ~7 : 0;
        var sectionCount = hasSymbols ? 5 : 3;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        void Word(ulong value)
        {
            if (is64Bit)
            {
                writer.Write(value);
            }
            else
            {
                writer.Write((uint)value);
            }
        }

        // ELF header
        writer.Write([0x7F, (byte)'E', (byte)'L', (byte)'F']);
        writer.Write((byte)(is64Bit ? 2 : 1)); // class
        writer.Write((byte)1); // little-endian
        writer.Write((byte)1); // version
        writer.Write(new byte[9]); // OS ABI, ABI version, padding
        writer.Write((ushort)3); // ET_DYN
        writer.Write(machine);
        writer.Write(1u); // e_version
        Word(0); // e_entry
        Word((ulong)programHeadersOffset);
        Word((ulong)sectionHeadersOffset);
        writer.Write(0u); // e_flags
        writer.Write((ushort)headerSize);
        writer.Write((ushort)programHeaderSize);
        writer.Write((ushort)(programHeader ? 1 : 0));
        writer.Write((ushort)sectionHeaderSize);
        writer.Write((ushort)(sectionHeaders ? sectionCount : 0)); // e_shnum
        writer.Write((ushort)(sectionHeaders ? 2 : 0)); // e_shstrndx

        if (programHeader)
        {
            writer.Write(PtLoad);
            if (is64Bit)
            {
                writer.Write(PfExecuteRead);
            }

            Word((ulong)textOffset); // p_offset
            Word(CodeAddress); // p_vaddr
            Word(CodeAddress); // p_paddr
            Word((ulong)code.Length); // p_filesz
            Word((ulong)code.Length); // p_memsz
            if (!is64Bit)
            {
                writer.Write(PfExecuteRead);
            }

            Word(1); // p_align
        }

        writer.Write(code);
        if (!sectionHeaders)
        {
            return stream.ToArray();
        }

        writer.Write(sectionNames);
        if (hasSymbols)
        {
            while (stream.Position < symtabOffset)
            {
                writer.Write((byte)0);
            }

            writer.Write(symbolTable);
            writer.Write(symbolNames);
        }

        while (stream.Position < sectionHeadersOffset)
        {
            writer.Write((byte)0);
        }

        void SectionHeader(
            uint name, uint type, ulong flags, ulong address, ulong offset, ulong size,
            uint link = 0, uint info = 0, ulong entrySize = 0)
        {
            writer.Write(name);
            writer.Write(type);
            Word(flags);
            Word(address);
            Word(offset);
            Word(size);
            writer.Write(link);
            writer.Write(info);
            Word(1); // sh_addralign
            Word(entrySize);
        }

        SectionHeader(0, 0, 0, 0, 0, 0);
        SectionHeader(
            textNameOffset,
            ShtProgBits,
            executable ? ShfAlloc | ShfExecInstr : ShfAlloc,
            CodeAddress,
            (ulong)textOffset,
            (ulong)code.Length);
        SectionHeader(shstrtabNameOffset, ShtStrTab, 0, 0, (ulong)strtabOffset, (ulong)sectionNames.Length);
        if (hasSymbols)
        {
            SectionHeader(
                symtabNameOffset, ShtSymTab, 0, 0, (ulong)symtabOffset, (ulong)symbolTable.Length,
                link: 4, info: 1, entrySize: (ulong)(is64Bit ? 24 : 16));
            SectionHeader(strtabNameOffset, ShtStrTab, 0, 0, (ulong)symbolNamesOffset, (ulong)symbolNames.Length);
        }

        return stream.ToArray();
    }

    /// <summary>Points the .text section's name at a string offset far outside the string table.</summary>
    public static byte[] WithBadSectionNameIndex(byte[] elf)
    {
        var corrupted = (byte[])elf.Clone();
        var sectionHeadersOffset = (int)BitConverter.ToUInt64(corrupted, 0x28);
        BitConverter.GetBytes(0x00FFFFFFu).CopyTo(corrupted, sectionHeadersOffset + 64); // .text sh_name
        return corrupted;
    }

    private static (byte[] SymbolTable, byte[] Names) BuildSymbolTable(IReadOnlyList<TestSymbol> symbols, bool is64Bit)
    {
        using var table = new MemoryStream();
        using var writer = new BinaryWriter(table);
        var names = new MemoryStream();
        names.WriteByte(0);

        void Entry(uint nameOffset, byte info, ulong value, ulong size)
        {
            writer.Write(nameOffset);
            if (is64Bit)
            {
                writer.Write(info);
                writer.Write((byte)0); // st_other
                writer.Write((ushort)1); // st_shndx: .text
                writer.Write(value);
                writer.Write(size);
            }
            else
            {
                writer.Write((uint)value);
                writer.Write((uint)size);
                writer.Write(info);
                writer.Write((byte)0);
                writer.Write((ushort)1);
            }
        }

        Entry(0, 0, 0, 0); // the mandatory null symbol
        foreach (var symbol in symbols)
        {
            var nameOffset = (uint)names.Length;
            names.Write(Encoding.ASCII.GetBytes(symbol.Name));
            names.WriteByte(0);
            const byte global = 1;
            const byte function = 2;
            const byte ifunc = 10;
            Entry(
                nameOffset,
                (byte)(global << 4 | (symbol.Ifunc ? ifunc : function)),
                symbol.RawValue ?? CodeAddress + (ulong)symbol.Offset,
                symbol.RawSize ?? (ulong)symbol.Size);
        }

        return (table.ToArray(), names.ToArray());
    }
}
