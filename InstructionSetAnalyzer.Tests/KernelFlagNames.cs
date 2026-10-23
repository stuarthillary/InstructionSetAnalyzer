namespace InstructionSetAnalyzer.Tests;

/// <summary>
/// Flag names the Linux kernel prints in the "flags" line of /proc/cpuinfo: every feature in
/// arch/x86/include/asm/cpufeatures.h whose comment carries a quoted name. Features without a quoted
/// name (for example AMX_FP16, AVX_IFMA and SHA512) are deliberately absent because the kernel hides them.
/// Captured from the mainline kernel source on 2026-10-23; refresh it when mapping new features.
/// </summary>
internal static class KernelFlagNames
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "3dnow", "3dnowext", "3dnowprefetch", "abm", "acc_power", "ace", "ace2", "ace2_en", "ace_en", "acpi",
        "adx", "aes", "amd_dcm", "amd_lbr_pmc_freeze", "amd_lbr_v2", "amd_ppin", "amx_bf16", "amx_int8",
        "amx_tile", "aperfmperf", "apic", "arat", "arch_capabilities", "arch_lbr", "arch_perfmon", "art",
        "avic", "avx", "avx2", "avx512_4fmaps", "avx512_4vnniw", "avx512_bf16", "avx512_bitalg",
        "avx512_fp16", "avx512_vbmi2", "avx512_vnni", "avx512_vp2intersect", "avx512_vpopcntdq", "avx512bw",
        "avx512cd", "avx512dq", "avx512er", "avx512f", "avx512ifma", "avx512pf", "avx512vbmi", "avx512vl",
        "avx_vnni", "bmi1", "bmi2", "bpext", "brs", "bts", "bus_lock_detect", "cat_l2", "cat_l3", "ccs",
        "ccs_en", "cdp_l2", "cdp_l3", "centaur_mcr", "cid", "cldemote", "clflush", "clflushopt", "clwb",
        "clzero", "cmov", "cmp_legacy", "constant_tsc", "cpb", "cppc", "cpuid", "cpuid_fault", "cqm",
        "cqm_llc", "cqm_mbm_local", "cqm_mbm_total", "cqm_occup_llc", "cr8_legacy", "cx16", "cx8", "cxmmx",
        "cyrix_arr", "dca", "de", "debug_swap", "decodeassists", "ds_cpl", "dtes64", "dtherm", "dts",
        "enqcmd", "epb", "ept", "ept_ad", "erms", "est", "extapic", "extd_apicid", "f16c", "flexpriority",
        "flush_l1d", "flushbyasid", "fma", "fma4", "fpu", "fred", "fsgsbase", "fsrm", "fxsr", "fxsr_opt",
        "gfni", "hfi", "hle", "ht", "hw_pstate", "hwp", "hwp_act_window", "hwp_epp", "hwp_notify",
        "hwp_pkg_req", "hypervisor", "ia64", "ibpb", "ibrs", "ibrs_enhanced", "ibs", "ibt", "ida",
        "intel_ppin", "intel_pt", "invpcid", "irperf", "k6_mtrr", "la57", "lahf_lm", "lam", "lass", "lbrv",
        "lm", "longrun", "lrti", "lwp", "mba", "mca", "mce", "md_clear", "misalignsse", "mmx", "mmxext",
        "monitor", "movbe", "movdir64b", "movdiri", "mp", "mpx", "msr", "mtrr", "mwaitx", "nodeid_msr",
        "nonstop_tsc", "nonstop_tsc_s3", "nopl", "npt", "nrip_save", "nx", "ospke", "osvw", "overflow_recov",
        "pae", "pat", "pausefilter", "pbe", "pcid", "pclmulqdq", "pconfig", "pdcm", "pdpe1gb", "pebs",
        "perfctr_core", "perfctr_llc", "perfctr_nb", "perfmon_v2", "pfthreshold", "pge", "phe", "phe2",
        "phe2_en", "phe_en", "pku", "pln", "pmm", "pmm_en", "pn", "pni", "popcnt", "proc_feedback", "pse",
        "pse36", "pti", "pts", "ptsc", "rapl", "rdpid", "rdpru", "rdrand", "rdseed", "rdt_a", "rdtscp",
        "recovery", "rep_good", "ring3mwait", "rng", "rng2", "rng2_en", "rng_en", "rsa", "rsa_en", "rtm",
        "sdbg", "sep", "serialize", "sev", "sev_es", "sev_snp", "sgx", "sgx_lc", "sha_ni", "skinit", "sm2",
        "sm2_en", "smap", "smca", "sme", "smep", "smx", "split_lock_detect", "ss", "ssbd", "sse", "sse2",
        "sse4_1", "sse4_2", "sse4a", "ssse3", "stibp", "succor", "svm", "svm_lock", "svsm", "syscall", "tbm",
        "tce", "tdx_guest", "tdx_host_platform", "tm", "tm2", "tme", "topoext", "tpr_shadow", "tsc",
        "tsc_adjust", "tsc_deadline_timer", "tsc_known_freq", "tsc_reliable", "tsc_scale", "tsxldtrk",
        "umip", "user_shstk", "v_spec_ctrl", "v_vmsave_vmload", "vaes", "vgif", "virt_ssbd", "vmcb_clean",
        "vme", "vmmcall", "vmx", "vnmi", "vpclmulqdq", "vpid", "waitpkg", "wbnoinvd", "wdt", "x2apic",
        "x2avic", "xgetbv1", "xop", "xsave", "xsavec", "xsaveerptr", "xsaveopt", "xsaves", "xtopology",
        "xtpr"
    };
}
