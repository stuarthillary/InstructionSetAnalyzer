# InstructionSetAnalyzer

A .NET console application that reports the x86 CPU features used by Linux ELF
binaries. It is inspired by
[`IrreducibleOSS/instruction-set-analyzer`](https://github.com/IrreducibleOSS/instruction-set-analyzer)
and uses [Iced](https://www.nuget.org/packages/Iced) to decode instructions.

## Supported input

- A single 32-bit x86 or 64-bit x86-64 ELF executable or shared object (`.so`)
- A directory, scanned recursively for ELF files

File extensions and Unix executable permission bits are not used. Executable ELF
sections are identified from their section flags, so shared libraries and
extensionless Linux executables are both analyzed. Binaries without a section
header table (for example after `sstrip`) are analyzed through their executable
`PT_LOAD` segments instead. The x32 ABI (64-bit code in a 32-bit ELF file) is
decoded as 64-bit code.

Instructions that only run in ring 0 (`VMX`, `SVM`, `MSR`, `SMM` and a few others)
are not reported by default. In a user-space binary they are almost always data
that happened to decode as code. Pass `--include-privileged` to see them, for
example when analyzing a kernel module.

## Usage

```powershell
dotnet run --project .\InstructionSetAnalyzer -- analyze <binary-file-or-directory>
```

For example, from Windows:

```powershell
dotnet run --project .\InstructionSetAnalyzer -- analyze C:\artifacts\linux-x64\libexample.so
```

When scanning a directory the output is quiet by default: it summarizes the files
analyzed and skipped and warns about files that could not be read. Add
`--verbose` (`-v`) to list every file as it is analyzed and every file skipped
for its architecture.

The binary is parsed and disassembled as data; it is never loaded or executed.
Consequently, the application can run on Windows and inspect Linux ELF files.
The current implementation only decodes little-endian x86 and x86-64 ELF files.
ARM, AArch64, and other architectures require a different disassembler.

To analyze binaries and immediately compare the result with the current Linux
host:

```bash
dotnet run --project ./InstructionSetAnalyzer -- analyze /opt/my-app --check-host
```

Host checking must run on the target x86 or x86-64 Linux machine. ELF analysis
itself remains cross-platform, so analysis can be performed on Windows and its
feature list passed to a separately deployed copy on Linux:

```bash
dotnet run --project ./InstructionSetAnalyzer -- check-host AVX AVX2,FMA AES
```

Feature names are case-insensitive Iced `CpuidFeature` names. Values may be
separate arguments, comma-separated, or supplied as a quoted whitespace-separated
list.

The checker uses the intersection of Linux CPU flags from every logical
processor, avoiding a false positive on heterogeneous hosts. For AMX it also
checks Linux XSTATE support. Applications using AMX must still request their own
per-process tile-data permission with `ARCH_REQ_XCOMP_PERM`; until they do, AMX
is reported as `PermissionRequired`. PKU requires both the hardware `pku` flag
and the kernel-enabled `ospke` flag. CET shadow-stack support requires Linux's
userspace `user_shstk` capability.

The kernel hides several recent features from `/proc/cpuinfo` (`AMX_FP16`,
`AMX_COMPLEX`, `AVX_IFMA`, `AVX_NE_CONVERT`, `AVX_VNNI_INT8`, `AVX_VNNI_INT16`,
`SHA512` and `PREFETCHWT1`). For these the checker executes CPUID directly and
reports the exact bit it read. CPUID only shows what the hardware can do, so the
checker also requires what the OS must have enabled: the `avx` flag for the AVX
family, and the same AMX state checks as above for the AMX extensions. CPUID runs
on whichever core executes the check, so a processor whose cores differ in
instruction set support could be misreported for these features.

Some privileged, vendor-specific, or obsolete Iced feature categories do not
have a reliable Linux userspace capability flag, and CPUID cannot show whether the
OS enabled them (`UINTR`, `RDPMC`, `TDX`). These are reported as `Indeterminate`,
never silently treated as supported or unsupported. The same applies to the CPUID
features above if CPUID is unavailable.

Exit code `0` means all requested features are supported, `1` indicates invalid
input or an environment error, and `2` means at least one feature is unsupported
or indeterminate.

When scanning a directory, ELF files for other architectures are skipped (counted in
the summary, and listed with `--verbose`) and do not affect the exit code. ELF files that are malformed or cannot
be read are also reported as warnings and the scan continues, but the exit code
becomes `1` (unless the host check already produced `2`) because the feature
list may be incomplete.

## Development

```bash
dotnet build
dotnet test
```

Tests build tiny ELF files in memory, so they run on any OS. The host feature
rules are tested against fake CPU flags and a fake CPUID, so only one test reads the
real hardware. The project publishes with Native AOT (`PublishAot`); CI builds and tests on Linux and
Windows and smoke-tests an AOT publish on Linux.
