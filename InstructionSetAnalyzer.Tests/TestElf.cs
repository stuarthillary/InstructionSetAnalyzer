using System.Text;

namespace InstructionSetAnalyzer.Tests;

/// <summary>Builds minimal little-endian ELF files containing a single code region.</summary>
internal static class TestElf
{
    public const ushort MachineI386 = 3;
    public const ushort MachineX86_64 = 62;
    public const ushort MachineAArch64 = 183;

    private const uint ShtProgBits = 1;
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
        bool programHeader = false)
    {
        var sectionNames = "\0.text\0.shstrtab\0"u8.ToArray();
        const int textNameOffset = 1;
        const int shstrtabNameOffset = 7;

        var headerSize = is64Bit ? 64 : 52;
        var programHeaderSize = is64Bit ? 56 : 32;
        var sectionHeaderSize = is64Bit ? 64 : 40;
        var programHeadersOffset = programHeader ? headerSize : 0;
        var textOffset = headerSize + (programHeader ? programHeaderSize : 0);
        var strtabOffset = textOffset + code.Length;
        var sectionHeadersOffset = sectionHeaders ? (strtabOffset + sectionNames.Length + 7) & ~7 : 0;

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
        writer.Write((ushort)(sectionHeaders ? 3 : 0)); // e_shnum
        writer.Write((ushort)(sectionHeaders ? 2 : 0)); // e_shstrndx

        if (programHeader)
        {
            writer.Write(PtLoad);
            if (is64Bit)
            {
                writer.Write(PfExecuteRead);
            }

            Word((ulong)textOffset); // p_offset
            Word(0x1000); // p_vaddr
            Word(0x1000); // p_paddr
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
        while (stream.Position < sectionHeadersOffset)
        {
            writer.Write((byte)0);
        }

        void SectionHeader(uint name, uint type, ulong flags, ulong address, ulong offset, ulong size)
        {
            writer.Write(name);
            writer.Write(type);
            Word(flags);
            Word(address);
            Word(offset);
            Word(size);
            writer.Write(0u); // sh_link
            writer.Write(0u); // sh_info
            Word(1); // sh_addralign
            Word(0); // sh_entsize
        }

        SectionHeader(0, 0, 0, 0, 0, 0);
        SectionHeader(
            textNameOffset,
            ShtProgBits,
            executable ? ShfAlloc | ShfExecInstr : ShfAlloc,
            0x1000,
            (ulong)textOffset,
            (ulong)code.Length);
        SectionHeader(shstrtabNameOffset, ShtStrTab, 0, 0, (ulong)strtabOffset, (ulong)sectionNames.Length);

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
}
