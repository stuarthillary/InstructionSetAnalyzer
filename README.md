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

## How detection and host checking work

### What the analyzer can detect

The analyzer decodes machine code with [Iced](https://github.com/icedland/iced),
which knows which CPUID feature flags every x86 and x86-64 instruction needs. It
records those flags for each instruction it decodes, so it can report anything in
Iced's `CpuidFeature` list: 178 features in the version used here. The names in the
output are exactly these names, and `check-host` accepts them.

| Family | Examples |
| --- | --- |
| Original x86 and x87 | `INTEL8086` to `INTEL486`, `FPU`, `CPUID`, `TSC`, `PAUSE`, `MULTIBYTENOP` |
| x86-64 baseline | `X64`, `CMOV`, `CX8`, `SYSCALL`, `FXSR`, `CMPXCHG16B` |
| SSE family | `MMX`, `SSE`, `SSE2`, `SSE3`, `SSSE3`, `SSE4_1`, `SSE4_2`, `SSE4A` |
| AVX family | `AVX`, `AVX2`, `FMA`, `F16C`, `AVX_VNNI`, `AVX_IFMA`, `AVX_NE_CONVERT`, `AVX_VNNI_INT8` |
| AVX-512 | `AVX512F`, `AVX512BW`, `AVX512CD`, `AVX512DQ`, `AVX512VL`, `AVX512_VBMI`, `AVX512_FP16`, ... |
| AMX | `AMX_TILE`, `AMX_INT8`, `AMX_BF16`, `AMX_FP16`, `AMX_COMPLEX` |
| Bit manipulation and math | `BMI1`, `BMI2`, `LZCNT`, `POPCNT`, `ADX`, `MOVBE` |
| Cryptography and random numbers | `AES`, `VAES`, `PCLMULQDQ`, `SHA`, `SHA512`, `GFNI`, `RDRAND`, `RDSEED` |
| Transactions and protection | `RTM`, `HLE_or_RTM`, `PKU`, `CET_IBT`, `CET_SS`, `SGX1` |
| Other extensions | `MOVDIRI`, `MOVDIR64B`, `WAITPKG`, `SERIALIZE`, `CLWB`, `XSAVE` |
| Vendor-specific and obsolete | `D3NOW`, Cyrix, VIA PadLock, Xeon Phi (`KNC`), Itanium (`IA64`) |

Detection reads bytes, not behavior, so keep these limits in mind:

- It finds instructions that are *present*, not instructions that *run*. An AVX2
  code path behind a runtime check still counts as AVX2. The section on ifunc above
  covers the part of this the analyzer can recognize.
- Decoding is a linear sweep over the executable sections (or segments), so data
  placed inside code can decode as instructions. Features that only run in ring 0
  are hidden for that reason, but an obsolete feature such as `D3NOW` can still
  appear from misdecoded data.
- Only x86 and x86-64 are decoded. Other architectures are skipped.

### How the host is checked

Each requested feature gets one of four statuses: `Supported`, `PermissionRequired`
(AMX only), `Unsupported` or `Indeterminate`. How a feature is checked depends on
what Linux exposes about it:

| How the feature is checked | Features | Examples |
| --- | --- | --- |
| Always supported, because every x86-64 CPU has it | 11 | `CPUID`, `FPU`, `INTEL386`, `PAUSE` |
| Linux CPU flags | 102 | `AVX2`, `AVX512F`, `BMI2`, `AES`, `SSE4_2`, `PKU`, `AMX_TILE` |
| CPUID, plus what the OS must enable | 8 | `AVX_IFMA`, `AVX_VNNI_INT8`, `SHA512`, `AMX_FP16` |
| Cannot be determined, so `Indeterminate` | 57 | `UINTR`, `RDPMC`, `TDX`, vendor features such as `CYRIX_*` and `PADLOCK_*` |

For the 102 features checked through Linux flags:

1. The checker reads `/proc/cpuinfo`, takes the `flags` line of every logical
   processor and keeps only the flags all of them share. The kernel clears flags it
   cannot support, so a flag means the OS can run the instructions, not only that
   the silicon can.
2. It maps the feature to its flag name. Usually that is the lower-case Iced name
   (`AVX2` is `avx2`), with a table for the exceptions (`SSE3` is `pni`, `SHA` is
   `sha_ni`, `CMPXCHG16B` is `cx16`, `AVX512_VBMI` is `avx512vbmi`). `PKU` needs both
   `pku` and `ospke`.
3. For AMX it also asks the kernel which processor state it supports and whether
   this process has asked for it, using `arch_prctl` (`ARCH_GET_XCOMP_SUPP` and
   `ARCH_GET_XCOMP_PERM`). A program must request tile data itself with
   `ARCH_REQ_XCOMP_PERM`, so until it does the status is `PermissionRequired`.

The 8 features the kernel does not print in `/proc/cpuinfo` are checked by running
the CPUID instruction directly (leaf 7, subleaves 0 and 1) and reporting the exact
bit read, for example `CPUID.(EAX=07h,ECX=1):EAX[23] is clear`. CPUID only says what
the hardware can do, so these also need the OS to have enabled the matching state:
the `avx` flag for the AVX family, and the AMX checks above for the AMX extensions.

The check describes the machine it runs on. Inside a virtual machine, container or
WSL it sees what the hypervisor exposes, and on a processor whose cores differ in
instruction set support the CPUID result depends on the core that runs it. Run it on
the machine you deploy to.

### Looking at your own machine

```bash
grep -m1 '^flags' /proc/cpuinfo
```

```bash
lscpu | grep -i flags
```

The dynamic loader reports which x86-64 levels the CPU supports; a level without
`supported` after it is not available on this machine:

```bash
/lib64/ld-linux-x86-64.so.2 --help | grep x86-64-v
```

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

## Publishing with Native AOT

The project is set up for [Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
(`PublishAot` in the project file). Publishing compiles it to one native
executable that needs no .NET runtime on the target machine and starts in
milliseconds, which suits a tool you copy next to the binaries you want to check.

### Prerequisites

- The .NET 10 SDK.
- On Linux, a C toolchain and the zlib development files. The CI job installs
  `clang` and `zlib1g-dev`:

  ```bash
  sudo apt-get install clang zlib1g-dev
  ```

- On Windows, Visual Studio or the Build Tools with the "Desktop development with
  C++" workload.
- Native AOT does not cross-compile between operating systems, so publish on the
  operating system you are targeting.

### Publish

On Linux:

```bash
dotnet publish InstructionSetAnalyzer -c Release -r linux-x64 -o publish
```

```bash
./publish/InstructionSetAnalyzer analyze /opt/my-app --check-host
```

On Windows, from a Developer PowerShell for Visual Studio:

```powershell
dotnet publish InstructionSetAnalyzer -c Release -r win-x64 -o publish
```

```powershell
.\publish\InstructionSetAnalyzer.exe analyze C:\artifacts\linux-x64\libexample.so
```

The executable is about 5 MB and is the only file you need to copy. The `.dbg` or
`.pdb` file next to it holds debugging symbols and is optional. The Linux build
depends only on the C library (glibc), so it runs on the machine you publish for
without installing .NET.

To check a deployment target, copy the Linux executable there and run
`check-host` or `analyze --check-host` on it, as described above.

### Troubleshooting

- **`Unable to find package runtime.<distro>-x64.Microsoft.DotNet.ILCompiler`.** The
  SDK picked a distribution-specific runtime identifier, as happens on some Fedora
  container images. Pass `-r linux-x64` explicitly, as above. A plain `dotnet build`
  on such a machine fails the same way because `PublishAot` makes restore look for
  that package; use `dotnet build -p:PublishAot=false` or `-r linux-x64` there.
- **`'vswhere.exe' is not recognized` on Windows.** The build could not find Visual
  Studio. Run the command from a Developer PowerShell for Visual Studio, and check
  that the C++ workload is installed. A shell started from WSL does not inherit the
  Visual Studio environment.

### Keeping it AOT-safe

The project builds with warnings as errors, so trimming and AOT warnings fail the
publish. The code therefore avoids reflection: JSON output uses source generation
and feature names come from lookup tables. The `aot-publish` CI job publishes the
Linux executable and runs it on itself, so a change that breaks AOT shows up there.

## Development

```bash
dotnet build
dotnet test
```

Tests build tiny ELF files in memory, so they run on any OS. The host feature
rules are tested against fake CPU flags and a fake CPUID, so only one test reads
the real hardware. The project publishes with Native AOT (`PublishAot`); CI builds
and tests on Linux and Windows and smoke-tests an AOT publish on Linux.

## Learning more

### Instruction set references

- [Intel 64 and IA-32 Architectures Software Developer's Manual](https://www.intel.com/content/www/us/en/developer/articles/technical/intel-sdm.html):
  the authority on Intel instructions and on CPUID leaves and bits.
- [AMD64 architecture documentation](https://www.amd.com/en/search/documentation/hub.html):
  the equivalent for AMD processors.
- [x86 and amd64 instruction reference](https://www.felixcloutier.com/x86/): the
  Intel manual's instruction pages, easy to search.
- [Intel Intrinsics Guide](https://www.intel.com/content/www/us/en/docs/intrinsics-guide/index.html):
  which C intrinsic maps to which instruction and which extension it needs.
- [uops.info](https://uops.info/) and [Agner Fog's optimization manuals](https://www.agner.org/optimize/):
  latency, throughput and instruction tables for real CPUs.

### Linux

- [x86 feature flags](https://docs.kernel.org/arch/x86/cpuinfo.html): what the
  flags in `/proc/cpuinfo` mean and how the kernel decides to show them.
- [`cpufeatures.h`](https://github.com/torvalds/linux/blob/master/arch/x86/include/asm/cpufeatures.h):
  every flag the kernel knows, including the ones it hides.
- [Using XSTATE features in user space applications](https://docs.kernel.org/arch/x86/xstate.html):
  why AMX needs a permission request.
- [Control-flow enforcement: shadow stacks](https://docs.kernel.org/arch/x86/shstk.html):
  the `user_shstk` capability behind `CET_SS`.
- [`proc(5)`](https://man7.org/linux/man-pages/man5/proc.5.html) and
  [`arch_prctl(2)`](https://man7.org/linux/man-pages/man2/arch_prctl.2.html): the
  `/proc/cpuinfo` format and the call used for the AMX state checks.

### Compilers and runtime dispatch

- [GCC x86 options](https://gcc.gnu.org/onlinedocs/gcc/x86-Options.html): including
  the `-march=x86-64-v2`, `-v3` and `-v4` levels used by `--baseline`.
- [GCC function multiversioning](https://gcc.gnu.org/onlinedocs/gcc/Function-Multiversioning.html)
  and the [GNU ifunc wiki](https://sourceware.org/glibc/wiki/GNU_IFUNC): how one
  binary carries several implementations and picks one at load time.

### This project's building blocks

- [Iced](https://github.com/icedland/iced), the disassembler behind the feature
  list, and the original
  [instruction-set-analyzer](https://github.com/IrreducibleOSS/instruction-set-analyzer)
  this tool is inspired by.
