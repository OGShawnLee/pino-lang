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
import threading
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


def detect_rid() -> str:
    os_name = norm_os()
    arch = norm_arch()
    if os_name == "windows":
        return "win-arm64" if arch == "arm64" else "win-x64"
    if os_name == "macos":
        return "osx-arm64" if arch == "arm64" else "osx-x64"
    return "linux-arm64" if arch == "arm64" else "linux-x64"


def _build_pino_cli_jit(proj: Path) -> tuple[Path | None, float, str | None]:
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


def build_pino_cli(no_aot: bool = False) -> tuple[Path | None, float, str | None]:
    proj = REPO_ROOT / "pino-csharp-re" / "Pino.Cli" / "Pino.Cli.csproj"
    if no_aot:
        return _build_pino_cli_jit(proj)

    rid = detect_rid()
    aot_dir = BUILD_DIR / "pino-aot"
    aot_dir.mkdir(parents=True, exist_ok=True)

    env = os.environ.copy()
    if is_windows():
        pf86 = env.get("ProgramFiles(x86)") or r"C:\Program Files (x86)"
        vs_installer = Path(pf86) / "Microsoft Visual Studio" / "Installer"
        if (vs_installer / "vswhere.exe").exists():
            cur_path = env.get("PATH", "")
            if str(vs_installer).lower() not in cur_path.lower():
                env["PATH"] = f"{vs_installer}{os.pathsep}{cur_path}"

    cmd = [
        "dotnet", "publish", str(proj),
        "-c", "Release",
        "-r", rid,
        "-p:PublishAot=true",
        "-p:StripSymbols=true",
        "-p:OptimizationPreference=Speed",
        "-p:DebugType=none",
        "-p:DebugSymbols=false",
        "-p:WarningsNotAsErrors=NU1903",
        "-o", str(aot_dir),
        "--nologo", "-v", "q",
    ]
    t0 = time.perf_counter()
    p = subprocess.run(
        cmd,
        cwd=str(REPO_ROOT),
        env=env,
        capture_output=True,
        text=True,
        stdin=subprocess.DEVNULL,
    )
    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    aot_exe = aot_dir / f"pino{exe_suffix()}"
    if p.returncode == 0 and aot_exe.exists():
        return aot_exe, elapsed_ms, None

    err_msg = (p.stderr or p.stdout or "unknown error").strip()
    first_line = err_msg.splitlines()[0] if err_msg else "unknown error"
    print(f"  WARNING: NativeAOT publish failed ({first_line}); falling back to Release JIT build.")
    jit_exe, jit_ms, jit_err = _build_pino_cli_jit(proj)
    return jit_exe, elapsed_ms + jit_ms, jit_err


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


def parse_bench_output(stdout: str) -> tuple[str, float | None]:
    text = stdout.replace("\r\n", "\n").strip()
    if not text:
        return "", None
    lines = text.splitlines()
    last = lines[-1].strip()
    if last.startswith("PINO_BENCH_MS="):
        raw_val = last[len("PINO_BENCH_MS="):].strip()
        try:
            work_ms = float(raw_val)
        except ValueError:
            return text, None
        stripped = "\n".join(lines[:-1]).strip()
        return stripped, work_ms
    return text, None


def _get_windows_peak_rss_mb(handle: int) -> float | None:
    try:
        class PROCESS_MEMORY_COUNTERS(ctypes.Structure):
            _fields_ = [
                ("cb", ctypes.c_ulong),
                ("PageFaultCount", ctypes.c_ulong),
                ("PeakWorkingSetSize", ctypes.c_size_t),
                ("WorkingSetSize", ctypes.c_size_t),
                ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
                ("PagefileUsage", ctypes.c_size_t),
                ("PeakPagefileUsage", ctypes.c_size_t),
            ]

        fn = getattr(ctypes.windll.kernel32, "K32GetProcessMemoryInfo", None)
        if fn is None:
            fn = ctypes.windll.psapi.GetProcessMemoryInfo
        fn.argtypes = [ctypes.c_void_p, ctypes.POINTER(PROCESS_MEMORY_COUNTERS), ctypes.c_ulong]
        fn.restype = ctypes.c_int

        pmc = PROCESS_MEMORY_COUNTERS()
        pmc.cb = ctypes.sizeof(PROCESS_MEMORY_COUNTERS)
        if fn(ctypes.c_void_p(handle), ctypes.byref(pmc), pmc.cb):
            return pmc.PeakWorkingSetSize / (1024.0 * 1024.0)
    except Exception:
        return None
    return None


def _run_child_with_rss(cmd: list[str], timeout_s: float) -> tuple[int, str, str, float | None, float, bool]:
    t0 = time.perf_counter()
    if is_windows():
        proc = subprocess.Popen(
            cmd,
            cwd=str(REPO_ROOT),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            stdin=subprocess.DEVNULL,
        )
        try:
            stdout, stderr = proc.communicate(timeout=timeout_s)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.communicate()
            return -1, "", "", None, (time.perf_counter() - t0) * 1000.0, True
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        rss_mb = _get_windows_peak_rss_mb(int(proc._handle))
        return proc.returncode, stdout, stderr, rss_mb, elapsed_ms, False

    if hasattr(os, "wait4"):
        proc = subprocess.Popen(
            cmd,
            cwd=str(REPO_ROOT),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            stdin=subprocess.DEVNULL,
        )
        timed_out = False

        def on_timeout() -> None:
            nonlocal timed_out
            timed_out = True
            try:
                proc.kill()
            except OSError:
                pass

        out_buf: list[str] = []
        err_buf: list[str] = []

        def drain(stream: Any, target: list[str]) -> None:
            if stream is not None:
                try:
                    target.append(stream.read())
                finally:
                    stream.close()

        t_out = threading.Thread(target=drain, args=(proc.stdout, out_buf), daemon=True)
        t_err = threading.Thread(target=drain, args=(proc.stderr, err_buf), daemon=True)
        timer = threading.Timer(timeout_s, on_timeout)
        t_out.start()
        t_err.start()
        timer.start()
        try:
            _, status, rusage = os.wait4(proc.pid, 0)
        finally:
            timer.cancel()
        elapsed_ms = (time.perf_counter() - t0) * 1000.0
        t_out.join()
        t_err.join()
        proc.returncode = os.waitstatus_to_exitcode(status)
        if timed_out:
            return -1, "", "", None, elapsed_ms, True
        rss_mb = (
            rusage.ru_maxrss / (1024.0 * 1024.0)
            if sys.platform == "darwin"
            else rusage.ru_maxrss / 1024.0
        )
        return proc.returncode, "".join(out_buf), "".join(err_buf), rss_mb, elapsed_ms, False

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
        return -1, "", "", None, (time.perf_counter() - t0) * 1000.0, True
    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    return p.returncode, p.stdout, p.stderr, None, elapsed_ms, False


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

    actual, _ = parse_bench_output(p.stdout)
    if expected is not None and actual != expected:
        return {
            "status": "WRONG",
            "expected": expected[:200],
            "actual": actual[:200],
        }

    samples: list[float] = []
    work_samples: list[float] = []
    rss_samples: list[float] = []
    for _ in range(runs):
        rc, stdout, stderr, rss_mb, elapsed_ms, timed_out = _run_child_with_rss(cmd, timeout_s)
        if timed_out:
            return {"status": "TIMEOUT", "samples_ms": samples}
        if rc != 0:
            return {"status": "CRASH", "returncode": rc, "error": (stderr or stdout).strip()[:300]}
        actual, work_ms = parse_bench_output(stdout)
        if expected is not None and actual != expected:
            return {"status": "WRONG", "expected": expected[:200], "actual": actual[:200]}
        samples.append(round(elapsed_ms, 3))
        if work_ms is not None:
            work_samples.append(round(work_ms, 3))
        if rss_mb is not None:
            rss_samples.append(round(rss_mb, 2))

    entry: dict[str, Any] = {
        "status": "ok",
        "median_ms": round(statistics.median(samples), 3),
        "min_ms": round(min(samples), 3),
        "max_ms": round(max(samples), 3),
        "samples_ms": samples,
    }
    if len(work_samples) == len(samples) and len(work_samples) > 0:
        entry["work_median_ms"] = round(statistics.median(work_samples), 3)
        entry["work_min_ms"] = round(min(work_samples), 3)
        entry["work_max_ms"] = round(max(work_samples), 3)
        entry["work_samples_ms"] = work_samples
    if len(rss_samples) == len(samples) and len(rss_samples) > 0:
        entry["peak_rss_mb"] = round(statistics.median(rss_samples), 2)
    return entry


def parse_pino_stats(stderr: str) -> dict[str, Any] | None:
    for raw in reversed(stderr.replace("\r\n", "\n").splitlines()):
        line = raw.strip()
        if not line.startswith("PINO_STATS "):
            continue
        parsed: dict[str, int] = {}
        for token in line[len("PINO_STATS "):].split():
            if "=" not in token:
                continue
            k, v = token.split("=", 1)
            try:
                parsed[k] = int(v)
            except ValueError:
                return None
        required = ("allocs", "reallocs", "frees", "alloc_bytes", "peak_live_bytes", "rc_inc", "rc_dec")
        if all(k in parsed for k in required):
            return {
                "allocs": parsed["allocs"],
                "reallocs": parsed["reallocs"],
                "frees": parsed["frees"],
                "alloc_bytes": parsed["alloc_bytes"],
                "peak_live_bytes": parsed["peak_live_bytes"],
                "peak_live_kb": round(parsed["peak_live_bytes"] / 1024.0, 2),
                "rc_inc": parsed["rc_inc"],
                "rc_dec": parsed["rc_dec"],
            }
    return None


def collect_pino_stats(pino_exe: Path, benchmarks: list[str], quick: bool, timeout_s: float) -> dict[str, Any]:
    cc = find_c_compiler()
    if cc is None:
        return {}
    out_dir = BUILD_DIR / "pino-gcc-stats"
    out_dir.mkdir(parents=True, exist_ok=True)
    runtime_re = REPO_ROOT / "runtime-re"
    runtime_sources = [
        str(runtime_re / "pino_alloc.c"),
        str(runtime_re / "pino_str.c"),
        str(runtime_re / "pino_io.c"),
        str(runtime_re / "pino_regex.c"),
    ]
    stats_by_bench: dict[str, Any] = {}
    for b in benchmarks:
        src = BENCH_ROOT / b / "main.pino"
        c_path = out_dir / f"{b}.c"
        bin_path = out_dir / f"{b}{exe_suffix()}"
        p_emit = subprocess.run(
            [str(pino_exe), "compile", str(src), str(c_path)],
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
            stdin=subprocess.DEVNULL,
        )
        if p_emit.returncode != 0:
            stats_by_bench[b] = {"status": "BUILD_ERR", "error": (p_emit.stderr or p_emit.stdout).strip()[:300]}
            continue

        cmd_cc = [
            cc,
            "-O3",
            "-flto",
            "-std=c99",
            "-DPINO_STATS",
            f"-I{runtime_re}",
            str(c_path),
            *runtime_sources,
            "-lm",
            "-o",
            str(bin_path),
        ]
        p_cc = subprocess.run(
            cmd_cc,
            cwd=str(REPO_ROOT),
            capture_output=True,
            text=True,
            stdin=subprocess.DEVNULL,
        )
        if p_cc.returncode != 0:
            stats_by_bench[b] = {"status": "BUILD_ERR", "error": (p_cc.stderr or p_cc.stdout).strip()[:300]}
            continue

        try:
            p_run = subprocess.run(
                [str(bin_path)],
                cwd=str(REPO_ROOT),
                capture_output=True,
                text=True,
                stdin=subprocess.DEVNULL,
                timeout=timeout_s,
            )
        except subprocess.TimeoutExpired:
            stats_by_bench[b] = {"status": "TIMEOUT"}
            continue

        if p_run.returncode != 0:
            stats_by_bench[b] = {"status": "CRASH", "error": (p_run.stderr or p_run.stdout).strip()[:300]}
            continue

        expected = load_expected(b, quick)
        actual, _ = parse_bench_output(p_run.stdout)
        if expected is not None and actual != expected:
            stats_by_bench[b] = {"status": "WRONG", "expected": expected[:200], "actual": actual[:200]}
            continue

        parsed = parse_pino_stats(p_run.stderr)
        if parsed is None:
            parsed = {
                "allocs": 0,
                "reallocs": 0,
                "frees": 0,
                "alloc_bytes": 0,
                "peak_live_bytes": 0,
                "peak_live_kb": 0.0,
                "rc_inc": 0,
                "rc_dec": 0,
            }
        parsed["status"] = "ok"
        stats_by_bench[b] = parsed
    return stats_by_bench


def fmt_ms(v: float | None) -> str:
    if v is None:
        return "-"
    if v >= 1000:
        return f"{v:,.1f}"
    return f"{v:.1f}"


def fmt_work_ms(v: float | None) -> str:
    if v is None:
        return "-"
    if v >= 1000:
        return f"{v:,.1f}"
    if v >= 100:
        return f"{v:.1f}"
    if v >= 10:
        return f"{v:.2f}"
    return f"{v:.3f}"


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


def fmt_work_cell(entry: dict[str, Any] | None, rust_ms: float | None, show_ratio: bool) -> str:
    if entry is None:
        return "-"
    st = entry.get("status")
    if st != "ok":
        return str(st)
    med = entry.get("work_median_ms")
    if med is None:
        return "-"
    s = fmt_work_ms(med)
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

    # In-process work time table
    work_note = (
        "Median in-process computation time in **ms** (`PINO_BENCH_MS`, excluding startup and file I/O; "
        "parentheses: ratio **relative to Rust**, `1.00x` = Rust; lower is faster)."
        if has_rust
        else "Median in-process computation time in **ms** (`PINO_BENCH_MS`, excluding startup and file I/O; lower is faster)."
    )
    lines.append("## In-Process Work Time (ms)")
    lines.append("")
    lines.append(work_note)
    lines.append("")
    lines.append("| " + " | ".join(cols) + " |")
    lines.append("| " + " | ".join([":---"] + ["---:"] * len(langs)) + " |")
    for b in benches:
        rust_entry = results.get(b, {}).get("rust")
        rust_work_ms = rust_entry.get("work_median_ms") if (rust_entry and rust_entry.get("status") == "ok") else None
        row = [f"`{b}`"]
        for lang in langs:
            entry = results.get(b, {}).get(lang)
            row.append(fmt_work_cell(entry, rust_work_ms, show_ratio=has_rust and lang != "rust"))
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

    # Peak memory (RSS, MB) table
    lines.append("## Peak Memory (RSS, MB)")
    lines.append("")
    lines.append("Median peak resident set size in **MB** per timed run (lower is smaller).")
    lines.append("")
    lines.append("| " + " | ".join(cols) + " |")
    lines.append("| " + " | ".join([":---"] + ["---:"] * len(langs)) + " |")
    for b in benches:
        row = [f"`{b}`"]
        for lang in langs:
            entry = results.get(b, {}).get(lang)
            if entry and entry.get("status") == "ok" and entry.get("peak_rss_mb") is not None:
                row.append(f"{entry['peak_rss_mb']:.2f}")
            elif entry and entry.get("status") != "ok":
                row.append(str(entry.get("status")))
            else:
                row.append("-")
        lines.append("| " + " | ".join(row) + " |")
    lines.append("")

    # Binary size (KB) table
    size_langs = [l for l in langs if l in ("pino-c", "pino-gcc", "go", "rust")]
    if size_langs:
        lines.append("## Binary Size (KB)")
        lines.append("")
        lines.append("Compiled native executable size on disk in **KB**.")
        lines.append("")
        lines.append("| Benchmark | " + " | ".join(size_langs) + " |")
        lines.append("| :--- | " + " | ".join(["---:"] * len(size_langs)) + " |")
        for b in benches:
            row = [f"`{b}`"]
            for l in size_langs:
                sz = builds.get(l, {}).get("binary_kb", {}).get(b)
                row.append(f"{sz:,.1f}" if sz is not None else "-")
            lines.append("| " + " | ".join(row) + " |")
        lines.append("")

    # Optional -DPINO_STATS runtime counters table
    pino_stats = report.get("pino_stats")
    if pino_stats:
        lines.append("## Runtime Counters (-DPINO_STATS, pino-gcc)")
        lines.append("")
        lines.append("Deterministic runtime allocation and RC counters (`pino-gcc` compiled with `-DPINO_STATS`).")
        lines.append("")
        lines.append("| Benchmark | allocs | reallocs | frees | peak_live_kb | rc_inc | rc_dec |")
        lines.append("| :--- | ---: | ---: | ---: | ---: | ---: | ---: |")
        for b in benches:
            st = pino_stats.get(b)
            if not st or st.get("status") != "ok":
                err_st = st.get("status") if st else "-"
                lines.append(f"| `{b}` | {err_st} | - | - | - | - | - |")
            else:
                lines.append(
                    f"| `{b}` | {st['allocs']:,} | {st['reallocs']:,} | {st['frees']:,} | "
                    f"{st['peak_live_kb']:,.2f} | {st['rc_inc']:,} | {st['rc_dec']:,} |"
                )
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
    parser.add_argument("--no-aot", action="store_true", help="Build Pino.Cli as regular Release JIT instead of NativeAOT.")
    parser.add_argument("--stats", action="store_true", help="Also compile pino-gcc benchmarks with -DPINO_STATS and report RC/allocation counters.")
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
        mode_label = "Release" if args.no_aot else "NativeAOT"
        print(f"Building Pino.Cli ({mode_label})...")
        pino_exe, ms, err = build_pino_cli(no_aot=args.no_aot)
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
        rust_sizes = {b: round(p.stat().st_size / 1024.0, 1) for b, p in rust_bins.items() if p.exists()}
        builds["rust"] = {"project_ms": round(ms, 2), "error": err, "binary_kb": rust_sizes}
        if err:
            print(f"  ERROR building Rust: {err}", file=sys.stderr)

    if "go" in langs:
        builds["go"] = {"per_benchmark_ms": {}, "binary_kb": {}}
        for b in benchmarks:
            bin_path, ms, err = build_go_one(b)
            builds["go"]["per_benchmark_ms"][b] = round(ms, 2)
            go_bins[b] = bin_path
            if bin_path is not None and bin_path.exists():
                builds["go"]["binary_kb"][b] = round(bin_path.stat().st_size / 1024.0, 1)
            if err:
                build_errors[(b, "go")] = err

    if "pino-c" in langs and pino_exe is not None:
        builds["pino-c"] = {"per_benchmark_ms": {}, "binary_kb": {}}
        for b in benchmarks:
            bin_path, ms, err = build_pino_native_one(pino_exe, b, release=False)
            builds["pino-c"]["per_benchmark_ms"][b] = round(ms, 2)
            pino_c_bins[b] = bin_path
            if bin_path is not None and bin_path.exists():
                builds["pino-c"]["binary_kb"][b] = round(bin_path.stat().st_size / 1024.0, 1)
            if err:
                build_errors[(b, "pino-c")] = err

    if "pino-gcc" in langs and pino_exe is not None:
        builds["pino-gcc"] = {"per_benchmark_ms": {}, "binary_kb": {}}
        for b in benchmarks:
            bin_path, ms, err = build_pino_native_one(pino_exe, b, release=True)
            builds["pino-gcc"]["per_benchmark_ms"][b] = round(ms, 2)
            pino_gcc_bins[b] = bin_path
            if bin_path is not None and bin_path.exists():
                builds["pino-gcc"]["binary_kb"][b] = round(bin_path.stat().st_size / 1024.0, 1)
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
                work_str = f"  work={entry['work_median_ms']:7.3f} ms" if "work_median_ms" in entry else ""
                rss_str = f"  rss={entry['peak_rss_mb']:.2f} MB" if "peak_rss_mb" in entry else ""
                print(
                    f"  {b:14s}  {lang:10s}  ok       "
                    f"median={entry['median_ms']:8.1f} ms  "
                    f"(min={entry['min_ms']:.1f}, max={entry['max_ms']:.1f})"
                    f"{work_str}{rss_str}"
                )
            elif st == "TIMEOUT":
                print(f"  {b:14s}  {lang:10s}  TIMEOUT  (>{args.timeout:.0f}s)")
            else:
                any_failure = True
                detail = entry.get("error") or f"expected={entry.get('expected')!r} actual={entry.get('actual')!r}"
                print(f"  {b:14s}  {lang:10s}  {st:8s} {detail}")

    pino_stats: dict[str, Any] = {}
    if args.stats:
        if pino_exe is None:
            print("WARNING: --stats skipped because Pino.Cli was not built.", file=sys.stderr)
        elif find_c_compiler() is None:
            print("WARNING: --stats skipped because neither 'gcc' nor 'clang' was found on PATH.", file=sys.stderr)
        else:
            print("\nCollecting -DPINO_STATS runtime counters (pino-gcc)...")
            pino_stats = collect_pino_stats(pino_exe, benchmarks, args.quick, args.timeout)
            for b, st in pino_stats.items():
                if st.get("status") != "ok":
                    any_failure = True

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
    report: dict[str, Any] = {
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
    if pino_stats:
        report["pino_stats"] = pino_stats

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
