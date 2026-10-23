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
`--verbose` (`-v`) to list every file analyzed and every file skipped.

The binary is parsed and disassembled as data; it is never loaded or executed.
Consequently, the application can run on Windows and inspect Linux ELF files.
The current implementation only decodes little-endian x86 and x86-64 ELF files.
ARM, AArch64, and other architectures require a different disassembler.

## Finding out why a feature is needed

The plain feature list says that a directory needs AVX2, not where. Add
`--detail` for a breakdown of how many instructions use each feature and in which
files (busiest first, relative to the scanned directory), and `--symbols` to also
list the functions responsible:

```bash
dotnet run --project ./InstructionSetAnalyzer -- analyze /opt/my-app --detail --symbols
```

Symbols come from `.symtab` and `.dynsym`. Stripped code only has its exported
functions, and instructions in code with no symbol are reported as such.

## Baselines

`--baseline` fails the run (exit code `2`) if the binaries need more than one of
the x86-64 microarchitecture levels, using the names gcc and glibc use:
`x86-64`, `x86-64-v2`, `x86-64-v3` and `x86-64-v4`.

```bash
dotnet run --project ./InstructionSetAnalyzer -- analyze /opt/my-app --baseline x86-64-v2
```

The levels are the ones defined by the x86-64 psABI. v2 adds SSE3 through SSE4.2,
`POPCNT` and `CMPXCHG16B`; v3 adds AVX, AVX2, BMI1/2, FMA, F16C, `LZCNT`, `MOVBE`
and `XSAVE`; v4 adds AVX-512 F, BW, CD, DQ and VL. Every other feature (AES,
SHA, ADX, ...) is beyond all four levels. Always allowed are the 64-bit
architecture itself, the legacy instruction sets and `ENDBR32`/`ENDBR64` (which are
NOPs on CPUs without indirect branch tracking). Shadow-stack instructions are not,
because most of them fault on CPUs without CET. Iced does not report `LAHF`/`SAHF`
(also part of v2) as a CPUID feature, so a use of them cannot be detected.

For a directory the output lists the files that exceed the baseline.

## Code that only runs where it is supported (ifunc)

Libraries such as glibc, and code built with GCC's `target_clones`, ship several
implementations of a function and pick one at load time through an `ifunc`
resolver. The AVX2 version of `memchr` is in the binary, but only a CPU with AVX2
ever runs it, so counting it as a requirement is misleading.

With a symbol table the analyzer finds these variants: a function is a variant
when an ifunc resolver loads its address. `--symbols` marks them
(`[ifunc variant of memchr]`), and `--exclude-dispatched` leaves features that are
used *only* inside variants out of the `--baseline` and `--check-host` checks. They
are listed separately, so nothing is hidden:

```bash
dotnet run --project ./InstructionSetAnalyzer -- analyze ./libexample.so --exclude-dispatched --baseline x86-64-v2
```

Limits to be aware of:

- It needs a symbol table with function sizes. A stripped library such as `libm`
  still has its exported ifunc symbols but not its (local) variants, so nothing
  can be classified and the flag has no effect.
- It recognizes resolvers that load the variant address with `lea` (position
  independent x86-64 code) or with an absolute `mov` (non-PIC code).
- A function that no resolver references stays counted. In glibc the `*_evex512`
  variants are only listed in a test table, never selected, so their AVX-512
  instructions still count as requirements.
- Code guarded by a hand-written CPUID check, as in OpenSSL, cannot be told apart
  from code that always runs.

## JSON output

`--json` writes one JSON document to standard output instead of text, for scripts
and CI. Warnings and errors still go to standard error, and the exit codes are the
same. The document has a `schemaVersion` (currently `1`; it changes only when a
field is removed or changes meaning) and these top-level fields:

| Field | Meaning |
| --- | --- |
| `kind` | `file`, `directory` or `host` |
| `path`, `summary` | What was scanned and the counts of analyzed, skipped and failed files |
| `features` | Every feature used |
| `requiredFeatures` | The features that count for checks (without `--exclude-dispatched`, the same as `features`) |
| `dispatchedOnlyFeatures` | Features used only inside ifunc variants, with `--exclude-dispatched` |
| `files` | Per file: architecture, `codeSource`, and each feature with its instruction counts |
| `skipped`, `failed` | Files that were not analyzed and why (`unsupported-architecture`, `no-executable-code`) |
| `baseline` | With `--baseline`: the level, whether it was met, and what exceeded it |
| `host` | With `--check-host`: the status of every feature |

Per-feature `symbols` appear only with `--symbols`. `check-host --json` writes just
the `host` part.

## Scanning many files

A directory is scanned with one worker per CPU core; `--jobs` (`-j`) changes that.
The output does not depend on the number of workers because results are sorted by
path. Ctrl+C or SIGTERM stops a running scan cleanly and exits with `130`.

ELF files that contain no code (an empty object file, for example) are skipped and
counted in the summary instead of failing the scan. Analyzing such a file directly
is still an error.

## Checking the host

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

## Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Success: every requested feature is supported and the baseline, if any, is met |
| `1` | Invalid input or an environment error, or some ELF files could not be analyzed |
| `2` | A feature is unsupported or indeterminate on the host, or the baseline is exceeded |
| `130` | The run was cancelled with Ctrl+C or SIGTERM |

When scanning a directory, ELF files for other architectures and files without
code are skipped (counted in the summary, and listed with `--verbose`) and do not
affect the exit code. ELF files that are malformed or cannot be read are reported
as warnings and the scan continues, but the exit code becomes `1` (unless a check
already produced `2`) because the feature list may be incomplete.

## Development

```bash
dotnet build
dotnet test
```

Tests build tiny ELF files in memory, so they run on any OS. The host feature
rules are tested against fake CPU flags and a fake CPUID, so only one test reads
the real hardware. The project publishes with Native AOT (`PublishAot`); CI builds
and tests on Linux and Windows and smoke-tests an AOT publish on Linux.
