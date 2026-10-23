# FindInstructionSet

A .NET console application that reports the x86 CPU features used by Linux ELF
binaries. It is inspired by
[`IrreducibleOSS/instruction-set-analyzer`](https://github.com/IrreducibleOSS/instruction-set-analyzer)
and uses [Iced](https://www.nuget.org/packages/Iced) to decode instructions.

## Supported input

- A single 32-bit x86 or 64-bit x86-64 ELF executable or shared object (`.so`)
- A directory, scanned recursively for ELF files

File extensions and Unix executable permission bits are not used. Executable ELF
sections are identified from their section flags, so shared libraries and
extensionless Linux executables are both analyzed.

## Usage

```powershell
dotnet run --project .\FindInstructionSet -- analyze <binary-file-or-directory>
```

For example, from Windows:

```powershell
dotnet run --project .\FindInstructionSet -- analyze C:\artifacts\linux-x64\libexample.so
```

The binary is parsed and disassembled as data; it is never loaded or executed.
Consequently, the application can run on Windows and inspect Linux ELF files.
The current implementation only decodes little-endian x86 and x86-64 ELF files.
ARM, AArch64, and other architectures require a different disassembler.

To analyze binaries and immediately compare the result with the current Linux
host:

```bash
dotnet run --project ./FindInstructionSet -- analyze /opt/my-app --check-host
```

Host checking must run on the target x86 or x86-64 Linux machine. ELF analysis
itself remains cross-platform, so analysis can be performed on Windows and its
feature list passed to a separately deployed copy on Linux:

```bash
dotnet run --project ./FindInstructionSet -- check-host AVX AVX2,FMA AES
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

Some privileged, vendor-specific, or obsolete Iced feature categories do not
have a reliable Linux userspace capability flag. These are reported as
`Indeterminate`, never silently treated as supported.

Exit code `0` means all requested features are supported, `1` indicates invalid
input or an environment error, and `2` means at least one feature is unsupported
or indeterminate.
