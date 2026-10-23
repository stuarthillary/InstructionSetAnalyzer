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
    private const int CancellationCheckInterval = 0x1000;

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
    private readonly bool _collectSymbols;

    /// <param name="includePrivilegedFeatures">Also report features whose instructions only run in ring 0.</param>
    /// <param name="collectSymbols">
    /// Attribute each feature to the functions using it and detect ifunc variants. Needs a symbol table with
    /// function sizes, so stripped code stays unattributed.
    /// </param>
    public ElfInstructionSetAnalyzer(bool includePrivilegedFeatures = false, bool collectSymbols = false)
    {
        _includePrivilegedFeatures = includePrivilegedFeatures;
        _collectSymbols = collectSymbols;
    }

    /// <summary>Analyzes an ELF file; a file without any machine code is reported as invalid data.</summary>
    public AnalysisResult Analyze(string path, CancellationToken cancellationToken = default) =>
        AnalyzeIfCode(path, cancellationToken)
        ?? throw new InvalidDataException($"ELF binary has no executable sections or segments: {path}");

    /// <summary>
    /// Analyzes an ELF file, returning null when it is valid for a supported architecture but contains no
    /// machine code (an empty object file, for example).
    /// </summary>
    public AnalysisResult? AnalyzeIfCode(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!IsElf(path))
        {
            throw new InvalidDataException($"File is not an ELF binary: {path}");
        }

        try
        {
            return AnalyzeElf(path, cancellationToken);
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
            or InvalidCastException
            or IOException and not (FileNotFoundException or DirectoryNotFoundException);

    private AnalysisResult? AnalyzeElf(string path, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var header = ElfFile.ReadHeader(stream);
        var bitness = GetBitness(header);
        var regions = new List<CodeRegion>();
        var functions = FunctionIndex.Empty;

        if (header.HasSectionHeaders)
        {
            // ELFSharp leaks the file handle when it throws on a corrupt file if it opens the path itself, which
            // locks the file on Windows, so hand it the stream this method owns and always disposes.
            stream.Position = 0; // ELFSharp parses from the current position
            using var elf = ELFReader.Load(stream, shouldOwnStream: false);
            foreach (var section in elf.Sections.Where(IsExecutableCodeSection))
            {
                var bytes = section.GetContents();
                if (bytes.Length > 0)
                {
                    regions.Add(new CodeRegion(GetLoadAddress(section), bytes));
                }
            }

            if (_collectSymbols)
            {
                functions = FunctionIndex.Read(elf);
            }
        }

        var source = CodeSource.Sections;
        if (regions.Count == 0)
        {
            // Stripped binaries (for example after sstrip) have no section headers at all.
            source = CodeSource.Segments;
            regions.AddRange(ElfFile.ReadExecutableSegments(stream, header));
        }

        if (regions.Count == 0)
        {
            return null;
        }

        var variants = _collectSymbols
            ? FindDispatchedVariants(functions, regions, bitness, cancellationToken)
            : new Dictionary<int, List<int>>();
        var usage = new UsageAccumulator(functions, variants, _collectSymbols, _includePrivilegedFeatures);
        foreach (var region in regions)
        {
            DecodeRegion(region, bitness, usage, cancellationToken);
        }

        return new AnalysisResult(DescribeArchitecture(header), regions.Count, source, _collectSymbols, usage.Build());
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

    /// <summary>
    /// Finds the functions that ifunc resolvers can return. A resolver selects an implementation by loading its
    /// address (lea, or mov of an absolute address), so every function whose start a resolver loads is a variant
    /// that only runs on CPUs the resolver picked it for. Maps each variant to the resolvers referencing it.
    /// </summary>
    private static Dictionary<int, List<int>> FindDispatchedVariants(
        FunctionIndex functions,
        List<CodeRegion> regions,
        int bitness,
        CancellationToken cancellationToken)
    {
        var variants = new Dictionary<int, List<int>>();
        for (var resolverIndex = 0; resolverIndex < functions.Count; resolverIndex++)
        {
            var resolver = functions[resolverIndex];
            if (!resolver.IsIfuncResolver)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            // Written as subtractions so a corrupt symbol table cannot overflow the comparison.
            var region = regions.FirstOrDefault(candidate =>
                resolver.Address >= candidate.Address &&
                resolver.Address - candidate.Address <= (ulong)candidate.Bytes.Length &&
                resolver.Size <= (ulong)candidate.Bytes.Length - (resolver.Address - candidate.Address));
            if (region.Bytes is null)
            {
                continue;
            }

            var body = region.Bytes.AsMemory((int)(resolver.Address - region.Address), (int)resolver.Size).ToArray();
            var reader = new ByteArrayCodeReader(body);
            var decoder = Decoder.Create(bitness, reader, resolver.Address);
            while (reader.CanReadByte)
            {
                decoder.Decode(out var instruction);
                if (TryGetLoadedAddress(instruction) is not { } target)
                {
                    continue;
                }

                var variantIndex = functions.FindStart(target);
                if (variantIndex < 0 || functions[variantIndex].IsIfuncResolver)
                {
                    continue;
                }

                if (!variants.TryGetValue(variantIndex, out var resolverIndices))
                {
                    variants[variantIndex] = resolverIndices = [];
                }

                if (!resolverIndices.Contains(resolverIndex))
                {
                    resolverIndices.Add(resolverIndex);
                }
            }
        }

        return variants;
    }

    private static ulong? TryGetLoadedAddress(in Instruction instruction)
    {
        if (instruction.Mnemonic == Mnemonic.Lea && instruction.IsIPRelativeMemoryOperand)
        {
            return instruction.IPRelativeMemoryAddress;
        }

        // Non-PIC code loads the address as an immediate.
        if (instruction.Mnemonic == Mnemonic.Mov && instruction.OpCount == 2)
        {
            return instruction.Op1Kind switch
            {
                OpKind.Immediate32to64 => (ulong)instruction.Immediate32to64,
                OpKind.Immediate32 => instruction.Immediate32,
                OpKind.Immediate64 => instruction.Immediate64,
                _ => null
            };
        }

        return null;
    }

    private void DecodeRegion(
        CodeRegion region,
        int bitness,
        UsageAccumulator usage,
        CancellationToken cancellationToken)
    {
        var codeReader = new ByteArrayCodeReader(region.Bytes);
        var decoder = Decoder.Create(bitness, codeReader, region.Address);
        var decoded = 0;

        while (codeReader.CanReadByte)
        {
            if ((++decoded & (CancellationCheckInterval - 1)) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            decoder.Decode(out var instruction);
            if (!instruction.IsInvalid)
            {
                usage.Record(instruction);
            }
        }
    }

    /// <summary>Counts, per feature, the instructions that need it and (optionally) where they are.</summary>
    private sealed class UsageAccumulator
    {
        private const int MaxSymbolsPerFeature = 50;
        private const int MaxDispatchersListed = 3;

        private static readonly int FeatureSlots = (int)Enum.GetValues<CpuidFeature>().Max() + 1;

        private readonly FunctionIndex _functions;
        private readonly Dictionary<int, List<int>> _variants;
        private readonly bool _collectSymbols;
        private readonly bool _includePrivileged;
        private readonly long[] _counts = new long[FeatureSlots];
        private readonly long[] _dispatched = new long[FeatureSlots];
        private readonly long[] _unattributed = new long[FeatureSlots];
        private readonly Dictionary<int, long>?[] _perSymbol = new Dictionary<int, long>?[FeatureSlots];

        // The decoder walks addresses in order, so most lookups land in the function found last time.
        private int _currentFunction = -1;
        private ulong _currentStart;
        private ulong _currentEnd;

        public UsageAccumulator(
            FunctionIndex functions,
            Dictionary<int, List<int>> variants,
            bool collectSymbols,
            bool includePrivileged)
        {
            _functions = functions;
            _variants = variants;
            _collectSymbols = collectSymbols;
            _includePrivileged = includePrivileged;
        }

        public void Record(in Instruction instruction)
        {
            var symbolIndex = -2;
            foreach (var feature in instruction.CpuidFeatures)
            {
                if (!_includePrivileged && PrivilegedOnlyFeatures.Contains(feature))
                {
                    continue;
                }

                var slot = (int)feature;
                _counts[slot]++;
                if (!_collectSymbols)
                {
                    continue;
                }

                if (symbolIndex == -2)
                {
                    symbolIndex = FindFunction(instruction.IP);
                }

                if (symbolIndex < 0)
                {
                    _unattributed[slot]++;
                    continue;
                }

                var perSymbol = _perSymbol[slot] ??= [];
                perSymbol[symbolIndex] = perSymbol.GetValueOrDefault(symbolIndex) + 1;
                if (_variants.ContainsKey(symbolIndex))
                {
                    _dispatched[slot]++;
                }
            }
        }

        public Dictionary<CpuidFeature, FeatureUsage> Build()
        {
            var result = new Dictionary<CpuidFeature, FeatureUsage>();
            for (var slot = 0; slot < FeatureSlots; slot++)
            {
                if (_counts[slot] == 0)
                {
                    continue;
                }

                var symbols = (_perSymbol[slot] ?? [])
                    .Select(pair => CreateSymbolUsage(pair.Key, pair.Value))
                    .OrderByDescending(symbol => symbol.InstructionCount)
                    .ThenBy(symbol => symbol.Name, StringComparer.Ordinal)
                    .Take(MaxSymbolsPerFeature)
                    .ToArray();
                result[(CpuidFeature)slot] = new FeatureUsage(_counts[slot], _dispatched[slot], _unattributed[slot], symbols);
            }

            return result;
        }

        private SymbolUsage CreateSymbolUsage(int functionIndex, long count)
        {
            var function = _functions[functionIndex];
            var dispatchers = _variants.TryGetValue(functionIndex, out var resolvers)
                ? resolvers
                    .Select(index => _functions[index].Name)
                    .Order(StringComparer.Ordinal)
                    .Take(MaxDispatchersListed)
                    .ToArray()
                : [];
            return new SymbolUsage(function.Name, function.Address, count, dispatchers);
        }

        private int FindFunction(ulong address)
        {
            if (_currentFunction >= 0 && address >= _currentStart && address < _currentEnd)
            {
                return _currentFunction;
            }

            _currentFunction = _functions.Find(address);
            if (_currentFunction >= 0)
            {
                var function = _functions[_currentFunction];
                _currentStart = function.Address;
                _currentEnd = function.Address + function.Size;
            }

            return _currentFunction;
        }
    }
}
