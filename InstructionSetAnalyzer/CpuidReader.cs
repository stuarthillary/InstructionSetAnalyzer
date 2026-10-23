using System.Runtime.Intrinsics.X86;

namespace InstructionSetAnalyzer;

internal readonly record struct CpuidRegisters(uint Eax, uint Ebx, uint Ecx, uint Edx);

internal enum CpuidRegister
{
    Eax,
    Ebx,
    Ecx,
    Edx
}

/// <summary>A single feature bit in the CPUID output.</summary>
internal readonly record struct CpuidBit(uint Leaf, uint Subleaf, CpuidRegister Register, int Bit)
{
    public override string ToString() =>
        $"CPUID.(EAX={Leaf:X2}h,ECX={Subleaf}):{Register.ToString().ToUpperInvariant()}[{Bit}]";
}

internal interface ICpuid
{
    /// <summary>Executes CPUID, or returns null when the leaf is above the CPU's highest basic leaf.</summary>
    CpuidRegisters? Query(uint leaf, uint subleaf);
}

internal static class CpuidExtensions
{
    /// <summary>
    /// Reads one feature bit. A bit the CPU does not implement at all (leaf or subleaf out of range) is clear.
    /// Leaf 7 reports its highest valid subleaf in EAX of subleaf 0.
    /// </summary>
    public static bool IsSet(this ICpuid cpuid, CpuidBit bit)
    {
        if (bit is { Leaf: 7, Subleaf: > 0 })
        {
            var maxSubleaf = cpuid.Query(7, 0)?.Eax ?? 0;
            if (bit.Subleaf > maxSubleaf)
            {
                return false;
            }
        }

        if (cpuid.Query(bit.Leaf, bit.Subleaf) is not { } registers)
        {
            return false;
        }

        var value = bit.Register switch
        {
            CpuidRegister.Eax => registers.Eax,
            CpuidRegister.Ebx => registers.Ebx,
            CpuidRegister.Ecx => registers.Ecx,
            CpuidRegister.Edx => registers.Edx,
            _ => 0u
        };
        return (value >> bit.Bit & 1) != 0;
    }
}

/// <summary>Runs CPUID on the current core. Only available on x86 and x86-64 processes.</summary>
internal sealed class HardwareCpuid : ICpuid
{
    public static HardwareCpuid? TryCreate() => X86Base.IsSupported ? new HardwareCpuid() : null;

    public CpuidRegisters? Query(uint leaf, uint subleaf)
    {
        if (leaf > (uint)X86Base.CpuId(0, 0).Eax)
        {
            return null;
        }

        var (eax, ebx, ecx, edx) = X86Base.CpuId((int)leaf, (int)subleaf);
        return new CpuidRegisters((uint)eax, (uint)ebx, (uint)ecx, (uint)edx);
    }
}
