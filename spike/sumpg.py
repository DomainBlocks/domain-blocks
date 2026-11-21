"""SPIKE: summarize run-pg-delay.sh outputs (<label>-<round>-<variant>.txt), one row per case, one column per variant.

Usage: python3 spike/sumpg.py <dir> [variant ...]   (variant order = column order; default: as found)
Each cell: median ops/s (range), then median p50 / p99 in ms, with the change in ops/s against the first variant.
Reads either decimal separator and any thousands separator, since the Mac's culture prints "1 000" and "0,780 ms".
"""
import glob
import os
import re
import statistics as st
import sys


def dec(x):
    return float(x.replace(",", "."))


def whole(x):
    return float(re.sub(r"\D", "", x))


d, order = sys.argv[1], sys.argv[2:]
data = {}
for path in glob.glob(os.path.join(d, "*.txt")):
    name = os.path.basename(path)[:-4]
    parts = name.rsplit("-", 2)
    if len(parts) != 3 or not parts[1].isdigit():
        continue
    label, _, variant = parts
    text = open(path, encoding="utf-8", errors="ignore").read()
    t = re.search(r"^throughput:\s+(\d[^a-z\n]*?)\s*ops/s", text, re.M)
    if not t:
        continue
    r = {"ops": whole(t.group(1)), "failed": bool(re.search(r"^\s*Failed ", text, re.M))}
    for k in ("p50", "p99"):
        x = re.search(rf"^{k}:\s+([\d.,]+) ms", text, re.M)
        r[k] = dec(x.group(1)) if x else float("nan")
    e = re.search(r"^errors:\s+(\d[^a-z\n]*)", text, re.M)
    r["errors"] = int(whole(e.group(1))) if e else 0
    data.setdefault(label, {}).setdefault(variant, []).append(r)

variants = order or sorted({v for c in data.values() for v in c})
print("| Case | " + " | ".join(variants) + " |")
print("| --- |" + " ---: |" * len(variants))
for label in sorted(data, key=lambda c: ("open" in c, c)):
    cells, first = [], None
    for v in variants:
        rs = data[label].get(v, [])
        if not rs:
            cells.append("")
            continue
        ops = [r["ops"] for r in rs]
        med = st.median(ops)
        first = first or med
        flag = " FAILED" if any(r["failed"] for r in rs) else ""
        err = sum(r["errors"] for r in rs)
        cells.append(
            f"{med:,.0f} ({min(ops):,.0f}-{max(ops):,.0f}) {100 * (med - first) / first:+.1f}%, "
            f"{st.median(r['p50'] for r in rs):.2f} / {st.median(r['p99'] for r in rs):.2f} ms, n={len(rs)}"
            + (f", errors {err}" if err else "") + flag)
    print(f"| {label} | " + " | ".join(cells) + " |")
