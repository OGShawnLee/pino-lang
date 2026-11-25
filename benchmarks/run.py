#!/usr/bin/env python3
"""Cross-platform benchmark runner for Pino vs Go, Rust, C#, Bun, and Python."""

from __future__ import annotations

import argparse
import ctypes
import datetime
import json
import os
import platform
import shutil
import statistics
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent
BENCH_ROOT = REPO_ROOT / "benchmarks"
DATA_DIR = BENCH_ROOT / "data"
BUILD_DIR = BENCH_ROOT / "build"
RESULTS_DIR = BENCH_ROOT / "results"

ALL_BENCHMARKS = [
    "hello",
    "fib",
    "binary-trees",
    "nbody",
    "spectral-norm",
    "mandelbrot",
    "sort",
    "word-count",
    "string-build",
    "regex",
    "lexer",
]

ALL_LANGS = [
    "pino-vm",
    "pino-c",
    "pino-gcc",
    "go",
    "rust",
    "csharp",
    "bun",
    "python",
]

# Shared LCG constants (also documented in benchmarks/README.md)
LCG_SEED = 123456789
LCG_A = 1664525
LCG_C = 1013904223
LCG_M = 2147483648


class LCG:
    def __init__(self, seed: int = LCG_SEED) -> None:
        self.state = seed

    def next(self) -> int:
        self.state = (self.state * LCG_A + LCG_C) % LCG_M
        return self.state


def is_windows() -> bool:
    return sys.platform.startswith("win")


def exe_suffix() -> str:
    return ".exe" if is_windows() else ""


def norm_os() -> str:
    s = platform.system().lower()
    if s.startswith("win"):
        return "windows"
    if s == "darwin":
        return "macos"
    return s


def norm_arch() -> str:
    m = platform.machine().lower()
    if m in ("amd64", "x86_64", "x64"):
        return "x64"
    if m in ("arm64", "aarch64"):
        return "arm64"
    return m or "unknown"


def get_ram_gb() -> float | None:
    try:
        if is_windows():
            class MEMORYSTATUSEX(ctypes.Structure):
                _fields_ = [
                    ("dwLength", ctypes.c_ulong),
                    ("dwMemoryLoad", ctypes.c_ulong),
                    ("ullTotalPhys", ctypes.c_ulonglong),
                    ("ullAvailPhys", ctypes.c_ulonglong),
                    ("ullTotalPageFile", ctypes.c_ulonglong),
                    ("ullAvailPageFile", ctypes.c_ulonglong),
                    ("ullTotalVirtual", ctypes.c_ulonglong),
                    ("ullAvailVirtual", ctypes.c_ulonglong),
                    ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
                ]

            stat = MEMORYSTATUSEX()
            stat.dwLength = ctypes.sizeof(MEMORYSTATUSEX)
            ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(stat))
            return round(stat.ullTotalPhys / (1024 ** 3), 1)
        if sys.platform.startswith("linux"):
            with open("/proc/meminfo", "r", encoding="utf-8") as f:
                for line in f:
                    if line.startswith("MemTotal:"):
                        kb = int(line.split()[1])
                        return round(kb / (1024 ** 2), 1)
        if sys.platform == "darwin":
            out = subprocess.check_output(["sysctl", "-n", "hw.memsize"], text=True, stdin=subprocess.DEVNULL)
            return round(int(out.strip()) / (1024 ** 3), 1)
    except Exception:
        return None
    return None


def get_cpu_model() -> str:
    try:
        if is_windows():
            out = subprocess.check_output(
                ["powershell", "-NoProfile", "-Command", "(Get-CimInstance Win32_Processor | Select-Object -First 1 -ExpandProperty Name)"],
                text=True,
                stdin=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            ).strip()
            if out:
                return out
        elif sys.platform.startswith("linux"):
            with open("/proc/cpuinfo", "r", encoding="utf-8") as f:
                for line in f:
                    if line.startswith("model name"):
                        return line.split(":", 1)[1].strip()
        elif sys.platform == "darwin":
            out = subprocess.check_output(
                ["sysctl", "-n", "machdep.cpu.brand_string"],
                text=True,
                stdin=subprocess.DEVNULL,
            ).strip()
            if out:
                return out
    except Exception:
        pass
    return platform.processor() or "unknown"


def run_version_cmd(cmd: list[str]) -> str | None:
    try:
        p = subprocess.run(
            cmd,
            capture_output=True,
            text=True,
            stdin=subprocess.DEVNULL,
            timeout=15,
        )
        out = (p.stdout or p.stderr or "").strip()
        return out.splitlines()[0] if out else None
    except Exception:
        return None


def get_git_commit() -> str:
    out = run_version_cmd(["git", "-C", str(REPO_ROOT), "rev-parse", "--short", "HEAD"])
    return out or "unknown"


def generate_data(quick: bool) -> None:
    DATA_DIR.mkdir(parents=True, exist_ok=True)
    quick_flag = DATA_DIR / "quick"
    mode = "quick" if quick else "full"
    mode_file = DATA_DIR / ".mode"

    if quick:
        quick_flag.write_text("quick\n", encoding="utf-8")
    elif quick_flag.exists():
        quick_flag.unlink()

    wc_path = DATA_DIR / "word-count.txt"
    rx_path = DATA_DIR / "regex.txt"
    lx_path = DATA_DIR / "lexer.txt"

    if mode_file.exists() and mode_file.read_text(encoding="utf-8").strip() == mode:
        if wc_path.exists() and rx_path.exists() and lx_path.exists():
            return

    print(f"Generating deterministic input data ({mode})...")

    # 1. word-count.txt (~50 KB quick, ~250 KB full)
    rng = LCG(LCG_SEED)
    vocab = [f"word{i:04d}" for i in range(2000)]
    target_bytes = 50_000 if quick else 250_000
    lines: list[str] = []
    size = 0
    while size < target_bytes:
        row: list[str] = []
        for _ in range(16):
            r = rng.next()
            if (r & 1) == 0:
                idx = (r >> 1) % 120
            else:
                idx = (r >> 1) % 2000
            row.append(vocab[idx])
        line = " ".join(row)
        lines.append(line)
        size += len(line) + 1
    wc_path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")

    # 2. regex.txt (~50 KB quick, ~250 KB full)
    rng = LCG(LCG_SEED + 1)
    target_bytes = 50_000 if quick else 250_000
    lines = []
    size = 0
    domains = ["alpha", "beta", "gamma", "delta", "pino", "bench", "cloud", "systems"]
    words = ["val", "item", "count", "index", "total", "node", "tree", "score", "result", "value", "delta_x", "var_name"]
    while size < target_bytes:
        parts: list[str] = []
        for _ in range(10):
            r = rng.next()
            kind = r % 8
            if kind == 0:
                u = (r >> 3) % 500
                d = domains[(r >> 12) % len(domains)]
                parts.append(f"user{u}@{d}.com")
            elif kind == 1:
                parts.append(str((r >> 3) % 99999))
            elif kind == 2:
                parts.append(f"{words[(r >> 3) % len(words)]}_{(r >> 8) % 97}")
            else:
                parts.append(words[(r >> 3) % len(words)])
        line = " ".join(parts) + ";"
        lines.append(line)
        size += len(line) + 1
    rx_path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")

    # 3. lexer.txt (~20 KB quick, ~100 KB full)
    rng = LCG(LCG_SEED + 2)
    target_bytes = 20_000 if quick else 100_000
    lines = []
    size = 0
    idents = ["alpha", "beta", "count", "total", "node", "value", "score", "index", "result", "state", "items", "limit"]
    block_idx = 0
    while size < target_bytes:
        r = rng.next()
        a = idents[r % len(idents)]
        b = idents[(r >> 4) % len(idents)]
        n1 = (r >> 8) % 1000
        n2 = (r >> 16) % 500
        flag = "true" if (r & 1) == 0 else "false"
        block = (
            f"pub fn compute_{block_idx}({a} int, {b} int) int {{\n"
            f"  var acc = {n1}\n"
            f"  val active = {flag}\n"
            f"  for i in {n2} {{\n"
            f"    if active {{\n"
            f"      acc = acc + i\n"
            f"    }} else {{\n"
            f"      continue\n"
            f"    }}\n"
            f"  }}\n"
            f"  return acc\n"
            f"}}\n"
        )
        lines.append(block)
        size += len(block)
        block_idx += 1
    lx_path.write_text("".join(lines), encoding="utf-8", newline="\n")

    mode_file.write_text(mode + "\n", encoding="utf-8")


def load_expected(bench: str, quick: bool) -> str | None:
    path = BENCH_ROOT / bench / "expected.txt"
    if not path.exists():
        return None
    raw = path.read_text(encoding="utf-8").replace("\r\n", "\n")
    lines = raw.splitlines()
    target_header = "# quick" if quick else "# full"
    other_header = "# full" if quick else "# quick"

    if target_header in lines:
        in_section = False
        collected: list[str] = []
        for line in lines:
            if line == target_header:
                in_section = True
                continue
            if line == other_header:
                in_section = False
                continue
            if in_section:
                collected.append(line)
        return "\n".join(collected).strip()

    return raw.strip()


def find_c_compiler() -> str | None:
    for name in ("gcc", "clang"):
        if shutil.which(name):
            return name
    return None


def detect_available_langs(requested: list[str]) -> tuple[list[str], dict[str, str]]:
    available: list[str] = []
    skipped: dict[str, str] = {}

    has_dotnet = shutil.which("dotnet") is not None
    has_go = shutil.which("go") is not None
    has_cargo = shutil.which("cargo") is not None
    has_bun = shutil.which("bun") is not None
    has_python = shutil.which("python") is not None or shutil.which("python3") is not None
    has_gcc = find_c_compiler() is not None
    has_tcc = (REPO_ROOT / "tooling" / "tcc" / "tcc.exe").exists() if is_windows() else has_gcc

    for lang in requested:
        if lang == "pino-vm":
            if has_dotnet:
                available.append(lang)
            else:
                skipped[lang] = "'dotnet' not found on PATH"
        elif lang == "pino-c":
            if not has_dotnet:
                skipped[lang] = "'dotnet' not found on PATH"
            elif not has_tcc:
                skipped[lang] = "TinyCC (tooling/tcc/tcc.exe) not found"
            else:
                available.append(lang)
        elif lang == "pino-gcc":
            if not has_dotnet:
                skipped[lang] = "'dotnet' not found on PATH"
            elif not has_gcc:
                skipped[lang] = "neither 'gcc' nor 'clang' found on PATH"
            else:
                available.append(lang)
        elif lang == "go":
            if has_go:
                available.append(lang)
            else:
                skipped[lang] = "'go' not found on PATH"
        elif lang == "rust":
            if has_cargo:
                available.append(lang)
            else:
                skipped[lang] = "'cargo' not found on PATH"
        elif lang == "csharp":
            if has_dotnet:
                available.append(lang)
            else:
                skipped[lang] = "'dotnet' not found on PATH"
        elif lang == "bun":
            if has_bun:
                available.append(lang)
            else:
                skipped[lang] = "'bun' not found on PATH"
        elif lang == "python":
            if has_python:
                available.append(lang)
            else:
                skipped[lang] = "'python' not found on PATH"
        else:
            skipped[lang] = f"unknown language '{lang}'"

    return available, skipped


def build_pino_cli() -> tuple[Path | None, float, str | None]:
    proj = REPO_ROOT / "pino-csharp-re" / "Pino.Cli" / "Pino.Cli.csproj"
    t0 = time.perf_counter()
    p = subprocess.run(
        ["dotnet", "build", str(proj), "-c", "Release", "--nologo", "-v", "q"],
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
        stdin=subprocess.DEVNULL,
    )
    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    if p.returncode != 0:
        return None, elapsed_ms, (p.stderr or p.stdout).strip()
    exe = REPO_ROOT / "pino-csharp-re" / "Pino.Cli" / "bin" / "Release" / "net10.0" / f"pino{exe_suffix()}"
    if not exe.exists():
        return None, elapsed_ms, f"expected built Pino CLI at {exe}"
    return exe, elapsed_ms, None


def build_csharp() -> tuple[Path | None, float, str | None]:
    proj = BENCH_ROOT / "csharp" / "Benchmarks.csproj"
    t0 = time.perf_counter()
    p = subprocess.run(
        ["dotnet", "build", str(proj), "-c", "Release", "--nologo", "-v", "q"],
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
        stdin=subprocess.DEVNULL,
    )
    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    if p.returncode != 0:
        return None, elapsed_ms, (p.stderr or p.stdout).strip()
    exe = BENCH_ROOT / "csharp" / "bin" / "Release" / "net10.0" / f"Benchmarks{exe_suffix()}"
    if not exe.exists():
        return None, elapsed_ms, f"expected built C# binary at {exe}"
    return exe, elapsed_ms, None


def build_rust(benchmarks: list[str]) -> tuple[dict[str, Path], float, str | None]:
    cmd = ["cargo", "build", "--release", "--quiet"]
    for b in benchmarks:
        cmd.extend(["--bin", b])
    t0 = time.perf_counter()
    p = subprocess.run(
        cmd,
        cwd=str(BENCH_ROOT / "rust"),
        capture_output=True,
        text=True,
        stdin=subprocess.DEVNULL,
    )
    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    if p.returncode != 0:
        return {}, elapsed_ms, (p.stderr or p.stdout).strip()
    bins: dict[str, Path] = {}
    for b in benchmarks:
        bins[b] = BENCH_ROOT / "rust" / "target" / "release" / f"{b}{exe_suffix()}"
    return bins, elapsed_ms, None


def build_go_one(bench: str) -> tuple[Path | None, float, str | None]:
    out_dir = BUILD_DIR / "go"
    out_dir.mkdir(parents=True, exist_ok=True)
    out_bin = out_dir / f"{bench}{exe_suffix()}"
    src = BENCH_ROOT / bench / "main.go"
    t0 = time.perf_counter()
    p = subprocess.run(
        ["go", "build", "-o", str(out_bin), str(src)],
        cwd=str(BENCH_ROOT),
        capture_output=True,
        text=True,
        stdin=subprocess.DEVNULL,
    )
    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    if p.returncode != 0:
        return None, elapsed_ms, (p.stderr or p.stdout).strip()
    return out_bin, elapsed_ms, None


def build_pino_native_one(pino_exe: Path, bench: str, release: bool) -> tuple[Path | None, float, str | None]:
    sub = "pino-gcc" if release else "pino-c"
    out_dir = BUILD_DIR / sub
    out_dir.mkdir(parents=True, exist_ok=True)
    out_bin = out_dir / f"{bench}{exe_suffix()}"
    src = BENCH_ROOT / bench / "main.pino"
    cmd = [str(pino_exe), "build"]
    if release:
        cmd.append("--release")
    cmd.extend(["-o", str(out_bin), str(src)])
    t0 = time.perf_counter()
    p = subprocess.run(
        cmd,
        cwd=str(REPO_ROOT),
        capture_output=True,
        text=True,
        stdin=subprocess.DEVNULL,
    )
    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    if p.returncode != 0:
        return None, elapsed_ms, (p.stderr or p.stdout).strip()
    return out_bin, elapsed_ms, None


def run_timed(
    cmd: list[str],
    expected: str | None,
    runs: int,
    timeout_s: float,
) -> dict[str, Any]:
    # 1 warm-up run (also checks output and exit code)
    try:
        p = subprocess.run(
            cmd,
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
            stdin=subprocess.DEVNULL,
            timeout=timeout_s,
        )
    except subprocess.TimeoutExpired:
        return {"status": "TIMEOUT"}

    if p.returncode != 0:
        err = (p.stderr or p.stdout or "").strip()
        return {"status": "CRASH", "returncode": p.returncode, "error": err[:300]}

    actual = p.stdout.replace("\r\n", "\n").strip()
    if expected is not None and actual != expected:
        return {
            "status": "WRONG",
            "expected": expected[:200],
            "actual": actual[:200],
        }

    samples: list[float] = []
    for _ in range(runs):
        t0 = time.perf_counter()
        try:
            p = subprocess.run(
                cmd,
                cwd=str(REPO_ROOT),
                capture_output=True,
                text=True,
                stdin=subprocess.DEVNULL,
                timeout=timeout_s,
            )
        except subprocess.TimeoutExpired:
            return {"status": "TIMEOUT", "samples_ms": samples}
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        if p.returncode != 0:
            return {"status": "CRASH", "returncode": p.returncode, "error": (p.stderr or p.stdout).strip()[:300]}
        actual = p.stdout.replace("\r\n", "\n").strip()
        if expected is not None and actual != expected:
            return {"status": "WRONG", "expected": expected[:200], "actual": actual[:200]}
        samples.append(round(elapsed_ms, 3))

    return {
        "status": "ok",
        "median_ms": round(statistics.median(samples), 3),
        "min_ms": round(min(samples), 3),
        "max_ms": round(max(samples), 3),
        "samples_ms": samples,
    }


def fmt_ms(v: float | None) -> str:
    if v is None:
        return "-"
    if v >= 1000:
        return f"{v:,.1f}"
    return f"{v:.1f}"


def fmt_cell(entry: dict[str, Any] | None, rust_ms: float | None, show_ratio: bool) -> str:
    if entry is None:
        return "-"
    st = entry.get("status")
    if st != "ok":
        return str(st)
    med = entry["median_ms"]
    s = fmt_ms(med)
    if show_ratio and rust_ms and rust_ms > 0:
        ratio = med / rust_ms
        return f"{s} ({ratio:.2f}x)"
    return s


def render_markdown(report: dict[str, Any]) -> str:
    m = report["machine"]
    langs = report["languages"]
    benches = report["benchmarks"]
    results = report["results"]
    builds = report["builds"]
    has_rust = "rust" in langs

    lines: list[str] = []
    lines.append(f"# Pino Benchmark Results ({report['date']})")
    lines.append("")
    lines.append(f"- **Pino commit:** `{report['pino_commit']}`")
    lines.append(f"- **Mode:** `{report['mode']}` ({report['runs']} timed runs + 1 warm-up, timeout {report['timeout_s']}s)")
    lines.append(f"- **Machine:** {m['cpu']} ({m['cores']} logical cores, {m['ram_gb']} GB RAM) — {m['os']} ({m['arch']})")
    lines.append("- **Tool versions:**")
    for k, v in report["tool_versions"].items():
        if v:
            lines.append(f"  - `{k}`: `{v}`")
    lines.append("")

    # Execution time table
    header_note = "Median execution time in **ms** (parentheses: ratio **relative to Rust**, `1.00x` = Rust; lower is faster)." if has_rust else "Median execution time in **ms** (lower is faster)."
    lines.append("## Execution Time (ms)")
    lines.append("")
    lines.append(header_note)
    lines.append("")
    cols = ["Benchmark"] + langs
    lines.append("| " + " | ".join(cols) + " |")
    lines.append("| " + " | ".join([":---"] + ["---:"] * len(langs)) + " |")
    for b in benches:
        rust_entry = results.get(b, {}).get("rust")
        rust_ms = rust_entry["median_ms"] if (rust_entry and rust_entry.get("status") == "ok") else None
        row = [f"`{b}`"]
        for lang in langs:
            entry = results.get(b, {}).get(lang)
            row.append(fmt_cell(entry, rust_ms, show_ratio=has_rust and lang != "rust"))
        lines.append("| " + " | ".join(row) + " |")
    lines.append("")

    # Build time table
    lines.append("## Build / Compile Time (ms)")
    lines.append("")
    lines.append("Measured separately from execution time. Rust and C# build their shared project once; Go, `pino-c`, and `pino-gcc` compile per benchmark.")
    lines.append("")
    build_langs = [l for l in langs if l in ("pino-c", "pino-gcc", "go", "rust", "csharp")]
    if build_langs:
        lines.append("| Benchmark | " + " | ".join(build_langs) + " |")
        lines.append("| :--- | " + " | ".join(["---:"] * len(build_langs)) + " |")
        proj_row = ["*(shared project)*"]
        for l in build_langs:
            if l in ("rust", "csharp"):
                proj_row.append(fmt_ms(builds.get(l, {}).get("project_ms")))
            elif l in ("pino-c", "pino-gcc"):
                proj_row.append(fmt_ms(builds.get("pino-cli", {}).get("project_ms")))
            else:
                proj_row.append("-")
        lines.append("| " + " | ".join(proj_row) + " |")
        for b in benches:
            row = [f"`{b}`"]
            for l in build_langs:
                per = builds.get(l, {}).get("per_benchmark_ms", {})
                row.append(fmt_ms(per.get(b)))
            lines.append("| " + " | ".join(row) + " |")
        lines.append("")

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description="Run the Pino cross-language benchmark suite.")
    parser.add_argument("--only", help="Comma-separated list of benchmarks to run.")
    parser.add_argument("--lang", help="Comma-separated list of languages to run.")
    parser.add_argument("--runs", type=int, default=None, help="Number of timed runs per cell (default: 5, or 1 with --quick).")
    parser.add_argument("--quick", action="store_true", help="Use smaller input sizes for a fast smoke test.")
    parser.add_argument("--timeout", type=float, default=60.0, help="Timeout per run in seconds (default: 60).")
    parser.add_argument("--save", action="store_true", help="Force writing JSON/Markdown results even with --quick or filters.")
    args = parser.parse_args()
    if args.runs is None:
        args.runs = 1 if args.quick else 5

    if args.only:
        benchmarks = [b.strip() for b in args.only.split(",") if b.strip()]
        unknown = [b for b in benchmarks if b not in ALL_BENCHMARKS]
        if unknown:
            print(f"Error: unknown benchmark(s): {', '.join(unknown)}", file=sys.stderr)
            return 2
    else:
        benchmarks = list(ALL_BENCHMARKS)

    if args.lang:
        requested_langs = [l.strip() for l in args.lang.split(",") if l.strip()]
        unknown = [l for l in requested_langs if l not in ALL_LANGS]
        if unknown:
            print(f"Error: unknown language(s): {', '.join(unknown)}", file=sys.stderr)
            return 2
    else:
        requested_langs = list(ALL_LANGS)

    langs, skipped = detect_available_langs(requested_langs)
    for lang, reason in skipped.items():
        print(f"Skipping {lang}: {reason}")
    if not langs:
        print("Error: no requested languages are available.", file=sys.stderr)
        return 2

    generate_data(args.quick)

    # Build phase (timed separately)
    builds: dict[str, Any] = {}
    pino_exe: Path | None = None
    csharp_exe: Path | None = None
    rust_bins: dict[str, Path] = {}
    go_bins: dict[str, Path | None] = {}
    pino_c_bins: dict[str, Path | None] = {}
    pino_gcc_bins: dict[str, Path | None] = {}
    build_errors: dict[tuple[str, str], str] = {}

    if any(l in langs for l in ("pino-vm", "pino-c", "pino-gcc")):
        print("Building Pino.Cli (Release)...")
        pino_exe, ms, err = build_pino_cli()
        builds["pino-cli"] = {"project_ms": round(ms, 2), "error": err}
        if err:
            print(f"  ERROR building Pino.Cli: {err}", file=sys.stderr)

    if "csharp" in langs:
        print("Building C# project (Release)...")
        csharp_exe, ms, err = build_csharp()
        builds["csharp"] = {"project_ms": round(ms, 2), "error": err}
        if err:
            print(f"  ERROR building C#: {err}", file=sys.stderr)

    if "rust" in langs:
        print("Building Rust workspace (--release)...")
        rust_bins, ms, err = build_rust(benchmarks)
        builds["rust"] = {"project_ms": round(ms, 2), "error": err}
        if err:
            print(f"  ERROR building Rust: {err}", file=sys.stderr)

    if "go" in langs:
        builds["go"] = {"per_benchmark_ms": {}}
        for b in benchmarks:
            bin_path, ms, err = build_go_one(b)
            builds["go"]["per_benchmark_ms"][b] = round(ms, 2)
            go_bins[b] = bin_path
            if err:
                build_errors[(b, "go")] = err

    if "pino-c" in langs and pino_exe is not None:
        builds["pino-c"] = {"per_benchmark_ms": {}}
        for b in benchmarks:
            bin_path, ms, err = build_pino_native_one(pino_exe, b, release=False)
            builds["pino-c"]["per_benchmark_ms"][b] = round(ms, 2)
            pino_c_bins[b] = bin_path
            if err:
                build_errors[(b, "pino-c")] = err

    if "pino-gcc" in langs and pino_exe is not None:
        builds["pino-gcc"] = {"per_benchmark_ms": {}}
        for b in benchmarks:
            bin_path, ms, err = build_pino_native_one(pino_exe, b, release=True)
            builds["pino-gcc"]["per_benchmark_ms"][b] = round(ms, 2)
            pino_gcc_bins[b] = bin_path
            if err:
                build_errors[(b, "pino-gcc")] = err

    python_exe = shutil.which("python") or shutil.which("python3") or sys.executable
    quick_arg = ["--quick"] if args.quick else []

    results: dict[str, dict[str, Any]] = {}
    any_failure = False

    print(f"\nRunning {len(benchmarks)} benchmark(s) x {len(langs)} language(s) ({args.runs} runs each, {'quick' if args.quick else 'full'})...\n")

    for b in benchmarks:
        expected = load_expected(b, args.quick)
        results[b] = {}
        for lang in langs:
            if (b, lang) in build_errors:
                entry = {"status": "BUILD_ERR", "error": build_errors[(b, lang)][:300]}
                results[b][lang] = entry
                any_failure = True
                print(f"  {b:14s}  {lang:10s}  BUILD_ERR")
                continue

            cmd: list[str] | None = None
            if lang == "pino-vm":
                if pino_exe is None:
                    entry = {"status": "BUILD_ERR", "error": "Pino.Cli build failed"}
                    results[b][lang] = entry
                    any_failure = True
                    continue
                cmd = [str(pino_exe), "run", str(BENCH_ROOT / b / "main.pino")]
            elif lang == "pino-c":
                bin_path = pino_c_bins.get(b)
                if bin_path is None:
                    entry = {"status": "BUILD_ERR", "error": "pino-c binary missing"}
                    results[b][lang] = entry
                    any_failure = True
                    continue
                cmd = [str(bin_path)]
            elif lang == "pino-gcc":
                bin_path = pino_gcc_bins.get(b)
                if bin_path is None:
                    entry = {"status": "BUILD_ERR", "error": "pino-gcc binary missing"}
                    results[b][lang] = entry
                    any_failure = True
                    continue
                cmd = [str(bin_path)]
            elif lang == "go":
                bin_path = go_bins.get(b)
                if bin_path is None:
                    entry = {"status": "BUILD_ERR", "error": "go binary missing"}
                    results[b][lang] = entry
                    any_failure = True
                    continue
                cmd = [str(bin_path), *quick_arg]
            elif lang == "rust":
                bin_path = rust_bins.get(b)
                if bin_path is None or not bin_path.exists():
                    entry = {"status": "BUILD_ERR", "error": "rust binary missing"}
                    results[b][lang] = entry
                    any_failure = True
                    continue
                cmd = [str(bin_path), *quick_arg]
            elif lang == "csharp":
                if csharp_exe is None:
                    entry = {"status": "BUILD_ERR", "error": "csharp binary missing"}
                    results[b][lang] = entry
                    any_failure = True
                    continue
                cmd = [str(csharp_exe), b, *quick_arg]
            elif lang == "bun":
                cmd = ["bun", str(BENCH_ROOT / b / "main.ts"), *quick_arg]
            elif lang == "python":
                cmd = [python_exe, "-I", str(BENCH_ROOT / b / "main.py"), *quick_arg]

            assert cmd is not None
            entry = run_timed(cmd, expected, args.runs, args.timeout)
            results[b][lang] = entry
            st = entry["status"]
            if st == "ok":
                print(
                    f"  {b:14s}  {lang:10s}  ok       "
                    f"median={entry['median_ms']:8.1f} ms  "
                    f"(min={entry['min_ms']:.1f}, max={entry['max_ms']:.1f})"
                )
            elif st == "TIMEOUT":
                print(f"  {b:14s}  {lang:10s}  TIMEOUT  (>{args.timeout:.0f}s)")
            else:
                any_failure = True
                detail = entry.get("error") or f"expected={entry.get('expected')!r} actual={entry.get('actual')!r}"
                print(f"  {b:14s}  {lang:10s}  {st:8s} {detail}")

    tool_versions = {
        "pino": run_version_cmd([str(pino_exe), "--version"]) if pino_exe else None,
        "go": run_version_cmd(["go", "version"]) if shutil.which("go") else None,
        "rust": run_version_cmd(["rustc", "--version"]) if shutil.which("rustc") else None,
        "csharp": run_version_cmd(["dotnet", "--version"]) if shutil.which("dotnet") else None,
        "bun": run_version_cmd(["bun", "--version"]) if shutil.which("bun") else None,
        "python": run_version_cmd([python_exe, "--version"]),
        "c_compiler": run_version_cmd([find_c_compiler(), "--version"]) if find_c_compiler() else None,
    }

    today = datetime.date.today().isoformat()
    report = {
        "date": today,
        "mode": "quick" if args.quick else "full",
        "runs": args.runs,
        "timeout_s": args.timeout,
        "pino_commit": get_git_commit(),
        "machine": {
            "cpu": get_cpu_model(),
            "cores": os.cpu_count(),
            "ram_gb": get_ram_gb(),
            "os": f"{platform.system()} {platform.release()}",
            "arch": norm_arch(),
        },
        "tool_versions": tool_versions,
        "languages": langs,
        "skipped_languages": skipped,
        "benchmarks": benchmarks,
        "builds": builds,
        "results": results,
    }

    md = render_markdown(report)
    print("\n" + md)

    should_write = args.save or (not args.quick and not args.only and not args.lang)
    if should_write:
        RESULTS_DIR.mkdir(parents=True, exist_ok=True)
        stem = f"{today}-{norm_os()}-{norm_arch()}"
        json_path = RESULTS_DIR / f"{stem}.json"
        md_path = RESULTS_DIR / f"{stem}.md"
        json_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        md_path.write_text(md + "\n", encoding="utf-8")
        print(f"Wrote {json_path.relative_to(REPO_ROOT)}")
        print(f"Wrote {md_path.relative_to(REPO_ROOT)}")

    return 1 if any_failure else 0


if __name__ == "__main__":
    raise SystemExit(main())
