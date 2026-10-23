using ELFSharp.ELF;
using ELFSharp.ELF.Sections;

namespace InstructionSetAnalyzer;

internal readonly record struct FunctionSymbol(ulong Address, ulong Size, string Name, bool IsIfuncResolver);

/// <summary>
/// The function symbols of an ELF file (from .symtab and .dynsym), sorted by address. Stripped binaries only
/// keep their exported functions, so the index may cover just a fraction of the code.
/// </summary>
internal sealed class FunctionIndex
{
    // STT_GNU_IFUNC: the symbol is a resolver that returns the address of the implementation to use.
    private const int SymbolTypeIfunc = 10;
    private const int SymbolTypeFunction = 2;

    private readonly FunctionSymbol[] _sorted;

    private FunctionIndex(FunctionSymbol[] sorted)
    {
        _sorted = sorted;
    }

    public static FunctionIndex Empty { get; } = new([]);

    public int Count => _sorted.Length;

    public FunctionSymbol this[int index] => _sorted[index];

    public static FunctionIndex Read(IELF elf)
    {
        var candidates = new List<(ulong Address, ulong Size, string Name, bool Ifunc, int Rank)>();
        foreach (var section in elf.Sections)
        {
            switch (section)
            {
                case SymbolTable<ulong> table64:
                    foreach (var entry in table64.Entries)
                    {
                        Add(candidates, entry.Value, entry.Size, entry.Name, (int)entry.Type, (int)entry.Binding);
                    }

                    break;
                case SymbolTable<uint> table32:
                    foreach (var entry in table32.Entries)
                    {
                        Add(candidates, entry.Value, entry.Size, entry.Name, (int)entry.Type, (int)entry.Binding);
                    }

                    break;
            }
        }

        // One entry per address: aliases such as strrchr and rindex share code, so keep the best-ranked name
        // and remember whether any alias is an ifunc resolver.
        var functions = candidates
            .GroupBy(candidate => candidate.Address)
            .Select(group =>
            {
                var best = group
                    .OrderByDescending(candidate => candidate.Rank)
                    .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                    .First();
                return new FunctionSymbol(
                    group.Key,
                    group.Max(candidate => candidate.Size),
                    best.Name,
                    group.Any(candidate => candidate.Ifunc));
            })
            .OrderBy(function => function.Address)
            .ToArray();
        return new FunctionIndex(functions);
    }

    /// <summary>Index of the function containing the address, or -1.</summary>
    public int Find(ulong address)
    {
        int low = 0, high = _sorted.Length - 1;
        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (_sorted[middle].Address <= address)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        // The search guarantees address >= _sorted[high].Address, so this cannot overflow.
        return high >= 0 && address - _sorted[high].Address < _sorted[high].Size ? high : -1;
    }

    /// <summary>Index of the function that starts exactly at the address, or -1.</summary>
    public int FindStart(ulong address)
    {
        var index = Find(address);
        return index >= 0 && _sorted[index].Address == address ? index : -1;
    }

    private static void Add(
        List<(ulong Address, ulong Size, string Name, bool Ifunc, int Rank)> candidates,
        ulong address,
        ulong size,
        string name,
        int type,
        int binding)
    {
        if ((type != SymbolTypeFunction && type != SymbolTypeIfunc) || size == 0 || address == 0)
        {
            return;
        }

        var versionStart = name.IndexOf('@', StringComparison.Ordinal);
        var cleanName = versionStart > 0 ? name[..versionStart] : name;
        if (cleanName.Length == 0)
        {
            return;
        }

        // Prefer global over weak over local symbols when several names share an address.
        var rank = binding switch { 1 => 3, 2 => 2, _ => 1 };
        candidates.Add((address, size, cleanName, type == SymbolTypeIfunc, rank));
    }
}
