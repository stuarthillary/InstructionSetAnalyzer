using Iced.Intel;

namespace InstructionSetAnalyzer.Tests;

public class AnalyzerAttributionTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private AnalysisResult Analyze(byte[] elf, bool collectSymbols = true, bool includePrivileged = false) =>
        new ElfInstructionSetAnalyzer(includePrivileged, collectSymbols).Analyze(_temp.Write("lib.so", elf));

    [Fact]
    public void Counts_instructions_per_feature_without_collecting_symbols()
    {
        var result = Analyze(DispatchFixture.Elf(), collectSymbols: false);

        Assert.False(result.SymbolsCollected);
        Assert.Equal(3, result.Usage[CpuidFeature.AVX2].InstructionCount);
        Assert.Equal(1, result.Usage[CpuidFeature.AVX].InstructionCount);
        Assert.Equal(1, result.Usage[CpuidFeature.SSE].InstructionCount);
        Assert.All(result.Usage.Values, usage =>
        {
            Assert.Empty(usage.Symbols);
            Assert.Equal(0, usage.UnattributedInstructionCount);
            Assert.Equal(0, usage.DispatchedInstructionCount);
        });
        Assert.Empty(result.DispatchedOnlyFeatures);
    }

    [Fact]
    public void Attributes_features_to_the_functions_that_use_them()
    {
        var result = Analyze(DispatchFixture.Elf());

        var avx2 = result.Usage[CpuidFeature.AVX2];
        Assert.True(result.SymbolsCollected);
        Assert.Equal(
            ["memchr_avx2", "unreferenced_avx2"],
            avx2.Symbols.Select(symbol => symbol.Name));
        Assert.Equal([2L, 1L], avx2.Symbols.Select(symbol => symbol.InstructionCount));
        Assert.Equal(
            [TestElf.CodeAddress + DispatchFixture.VariantOffset, TestElf.CodeAddress + 0x40],
            avx2.Symbols.Select(symbol => symbol.Address));
        Assert.Equal(0, avx2.UnattributedInstructionCount);
    }

    [Fact]
    public void Marks_functions_loaded_by_an_ifunc_resolver_as_variants()
    {
        var result = Analyze(DispatchFixture.Elf());

        var avx2 = result.Usage[CpuidFeature.AVX2];
        Assert.Equal(["my_memchr"], avx2.Symbols.Single(symbol => symbol.Name == "memchr_avx2").DispatchedBy);
        Assert.Empty(avx2.Symbols.Single(symbol => symbol.Name == "unreferenced_avx2").DispatchedBy);
        Assert.Equal(2, avx2.DispatchedInstructionCount);
        Assert.False(avx2.IsDispatchedOnly); // the unreferenced function uses AVX2 as well
    }

    [Fact]
    public void Reports_a_feature_used_only_by_variants_as_dispatched_only()
    {
        var result = Analyze(DispatchFixture.ElfWithAvx2OnlyInVariant());

        Assert.True(result.Usage[CpuidFeature.AVX2].IsDispatchedOnly);
        Assert.Contains(CpuidFeature.AVX2, result.DispatchedOnlyFeatures);
        Assert.DoesNotContain(CpuidFeature.SSE, result.DispatchedOnlyFeatures);
        Assert.Contains(CpuidFeature.AVX2, result.Features);
    }

    [Fact]
    public void Lists_the_busiest_functions_first()
    {
        // Two SSE functions: busy has three instructions, quiet has one.
        var code = new byte[0x40];
        Array.Fill(code, (byte)0x90);
        byte[] busy = [.. TestElf.Sse, .. TestElf.Sse, .. TestElf.Sse, 0xC3];
        byte[] quiet = [.. TestElf.Sse, 0xC3];
        quiet.CopyTo(code, 0x00);
        busy.CopyTo(code, 0x20);
        var elf = TestElf.Build(code, symbols: [new TestSymbol("quiet", 0, 0x20), new TestSymbol("busy", 0x20, 0x20)]);

        var symbols = Analyze(elf).Usage[CpuidFeature.SSE].Symbols;

        Assert.Equal(["busy", "quiet"], symbols.Select(symbol => symbol.Name));
    }

    [Fact]
    public void Leaves_stripped_code_unattributed()
    {
        var result = Analyze(DispatchFixture.Elf(withSymbols: false));

        var avx2 = result.Usage[CpuidFeature.AVX2];
        Assert.Empty(avx2.Symbols);
        Assert.Equal(3, avx2.UnattributedInstructionCount);
        Assert.Equal(0, avx2.DispatchedInstructionCount);
        Assert.Empty(result.DispatchedOnlyFeatures);
    }

    [Fact]
    public void Finds_variants_in_32_bit_code_that_loads_absolute_addresses()
    {
        var result = Analyze(DispatchFixture.Elf(is64Bit: false));

        var variant = result.Usage[CpuidFeature.AVX2].Symbols.Single(symbol => symbol.Name == "memchr_avx2");
        Assert.Equal(["my_memchr"], variant.DispatchedBy);
    }

    [Fact]
    public void Lists_an_aliased_resolver_once()
    {
        var code = DispatchFixture.Code();
        var symbols = DispatchFixture.Symbols()
            .Append(new TestSymbol("memchr_alias", 0x00, 0x10, Ifunc: true))
            .ToArray();

        var result = Analyze(TestElf.Build(code, symbols: symbols));

        var variant = result.Usage[CpuidFeature.AVX2].Symbols.Single(symbol => symbol.Name == "memchr_avx2");
        Assert.Single(variant.DispatchedBy);
    }

    [Fact]
    public void Does_not_count_privileged_features_unless_asked()
    {
        var elf = TestElf.Build([.. TestElf.Rdmsr, .. TestElf.Avx]);

        Assert.DoesNotContain(CpuidFeature.MSR, Analyze(elf).Usage.Keys);
        Assert.Contains(CpuidFeature.MSR, Analyze(elf, includePrivileged: true).Usage.Keys);
    }

    [Theory]
    [InlineData(ulong.MaxValue - 4UL, 0x20UL)] // address + size wraps around
    [InlineData(TestElf.CodeAddress + 4, ulong.MaxValue)] // size larger than the whole section
    [InlineData(TestElf.CodeAddress + 8, 0x7FFFFFFFFFFFFFFFUL)]
    [InlineData(0UL, 0x10UL)] // address below the section
    public void Ignores_resolvers_with_corrupt_addresses_or_sizes(ulong value, ulong size)
    {
        var symbols = DispatchFixture.Symbols()
            .Append(new TestSymbol("corrupt", 0, 0, Ifunc: true, RawValue: value, RawSize: size))
            .ToArray();

        var result = Analyze(TestElf.Build(DispatchFixture.Code(), symbols: symbols));

        // The well-formed resolver and variant are still found.
        var variant = result.Usage[CpuidFeature.AVX2].Symbols.Single(symbol => symbol.Name == "memchr_avx2");
        Assert.Equal(["my_memchr"], variant.DispatchedBy);
    }

    [Fact]
    public void Random_corruption_with_symbol_collection_only_produces_documented_exceptions()
    {
        var original = DispatchFixture.Elf();
        var random = new Random(20261024);
        var path = System.IO.Path.Combine(_temp.Path, "fuzz.bin");
        var analyzer = new ElfInstructionSetAnalyzer(collectSymbols: true);

        for (var iteration = 0; iteration < 2000; iteration++)
        {
            var data = (byte[])original.Clone();
            for (var edit = random.Next(1, 10); edit > 0; edit--)
            {
                // Bias the damage towards the symbol table, which sits after the code.
                var offset = random.Next(3) == 0 ? random.Next(data.Length) : random.Next(data.Length / 2, data.Length);
                data[offset] = (byte)random.Next(256);
            }

            if (iteration % 7 == 0)
            {
                data = data[..random.Next(data.Length)];
            }

            File.WriteAllBytes(path, data);
            try
            {
                analyzer.AnalyzeIfCode(path);
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
    public void Honors_cancellation_during_a_long_decode()
    {
        var path = _temp.Write("big.so", TestElf.Build(Enumerable.Repeat((byte)0x90, 20_000).ToArray()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => new ElfInstructionSetAnalyzer().Analyze(path, cancellation.Token));
    }

    [Fact]
    public void AnalyzeIfCode_returns_null_for_a_valid_file_without_code()
    {
        var path = _temp.Write("empty.o", TestElf.Build([]));
        var analyzer = new ElfInstructionSetAnalyzer();

        Assert.Null(analyzer.AnalyzeIfCode(path));
        Assert.Throws<InvalidDataException>(() => analyzer.Analyze(path));
    }
}
