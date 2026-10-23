using System.Text;

namespace InstructionSetAnalyzer.Tests;

/// <summary>Builds minimal little-endian ELF files containing a single code section.</summary>
internal static class TestElf
{
    public const ushort MachineI386 = 3;
    public const ushort MachineX86_64 = 62;
    public const ushort MachineAArch64 = 183;

    private const uint ShtProgBits = 1;
    private const uint ShtStrTab = 3;
    private const uint ShfAlloc = 0x2;
    private const uint ShfExecInstr = 0x4;

    // vmovdqa ymm0, ymm1
    public static readonly byte[] Avx = [0xC5, 0xFD, 0x6F, 0xC1];

    // vpcmpeqb ymm0, ymm0, ymm1
    public static readonly byte[] Avx2 = [0xC5, 0xFD, 0x74, 0xC1];

    // movaps xmm0, xmm1
    public static readonly byte[] Sse = [0x0F, 0x28, 0xC1];

    public static byte[] Build(
        byte[] code,
        ushort machine = MachineX86_64,
        bool is64Bit = true,
        bool executable = true)
    {
        var sectionNames = "\0.text\0.shstrtab\0"u8.ToArray();
        const int textNameOffset = 1;
        const int shstrtabNameOffset = 7;

        var headerSize = is64Bit ? 64 : 52;
        var sectionHeaderSize = is64Bit ? 64 : 40;
        var textOffset = headerSize;
        var strtabOffset = textOffset + code.Length;
        var sectionHeadersOffset = (strtabOffset + sectionNames.Length + 7) & ~7;

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
        Word(0); // e_phoff
        Word((ulong)sectionHeadersOffset);
        writer.Write(0u); // e_flags
        writer.Write((ushort)headerSize);
        writer.Write((ushort)(is64Bit ? 56 : 32)); // e_phentsize
        writer.Write((ushort)0); // e_phnum
        writer.Write((ushort)sectionHeaderSize);
        writer.Write((ushort)3); // e_shnum
        writer.Write((ushort)2); // e_shstrndx

        writer.Write(code);
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
}
