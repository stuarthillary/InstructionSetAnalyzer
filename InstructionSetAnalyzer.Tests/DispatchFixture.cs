namespace InstructionSetAnalyzer.Tests;

/// <summary>
/// An x86-64 (or x86) code image with an ifunc resolver, one function it can select, and plain functions:
/// <code>
///   0x00 my_memchr          ifunc resolver: loads the address of memchr_avx2 and returns it
///   0x10 memchr_avx2        two AVX2 instructions                     (only reachable through the resolver)
///   0x20 plain_sse          one SSE instruction
///   0x30 plain_avx          one AVX instruction                       (called directly, not dispatched)
///   0x40 unreferenced_avx2  one AVX2 instruction                      (no resolver loads its address)
/// </code>
/// </summary>
internal static class DispatchFixture
{
    public const int VariantOffset = 0x10;
    public const int Length = 0x50;

    public static byte[] Code(bool is64Bit = true)
    {
        var code = new byte[Length];
        Array.Fill(code, (byte)0x90); // nop padding

        var variantAddress = TestElf.CodeAddress + VariantOffset;
        if (is64Bit)
        {
            // lea rax, [rip + rel32]; ret
            var rel = (int)(variantAddress - (TestElf.CodeAddress + 7));
            byte[] resolver = [0x48, 0x8D, 0x05, .. BitConverter.GetBytes(rel), 0xC3];
            resolver.CopyTo(code, 0);
        }
        else
        {
            // mov eax, imm32; ret  (non-PIC resolvers load the absolute address)
            byte[] resolver = [0xB8, .. BitConverter.GetBytes((uint)variantAddress), 0xC3];
            resolver.CopyTo(code, 0);
        }

        // memchr_avx2: vpcmpeqb ymm0, ymm0, ymm1 (x2); ret
        byte[] variant = [.. TestElf.Avx2, .. TestElf.Avx2, 0xC3];
        variant.CopyTo(code, VariantOffset);

        // plain_sse: movaps xmm0, xmm1; ret
        byte[] sse = [.. TestElf.Sse, 0xC3];
        sse.CopyTo(code, 0x20);

        // plain_avx: vmovdqa ymm0, ymm1; ret
        byte[] avx = [.. TestElf.Avx, 0xC3];
        avx.CopyTo(code, 0x30);

        // unreferenced_avx2: vpcmpeqb; ret
        byte[] unreferenced = [.. TestElf.Avx2, 0xC3];
        unreferenced.CopyTo(code, 0x40);

        return code;
    }

    public static TestSymbol[] Symbols(bool includeUnreferenced = true)
    {
        var symbols = new List<TestSymbol>
        {
            new("my_memchr", 0x00, 0x10, Ifunc: true),
            new("memchr_avx2", 0x10, 0x10),
            new("plain_sse", 0x20, 0x10),
            new("plain_avx", 0x30, 0x10)
        };
        if (includeUnreferenced)
        {
            symbols.Add(new TestSymbol("unreferenced_avx2", 0x40, 0x10));
        }

        return [.. symbols];
    }

    /// <summary>The fixture as an ELF file; <paramref name="withSymbols"/> false leaves it stripped.</summary>
    public static byte[] Elf(bool withSymbols = true, bool is64Bit = true, bool includeUnreferenced = true) =>
        TestElf.Build(
            Code(is64Bit),
            is64Bit ? TestElf.MachineX86_64 : TestElf.MachineI386,
            is64Bit,
            symbols: withSymbols ? Symbols(includeUnreferenced) : null);

    /// <summary>The code with the AVX2-only unreferenced function and the AVX function zeroed to nops.</summary>
    public static byte[] ElfWithAvx2OnlyInVariant()
    {
        var code = Code();
        Array.Fill(code, (byte)0x90, 0x30, 0x20); // drop plain_avx and unreferenced_avx2
        return TestElf.Build(code, symbols: Symbols(includeUnreferenced: false));
    }
}
