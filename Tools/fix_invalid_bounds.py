import re
from pathlib import Path

root = Path(r"G:\Monolith_Forge\Resources")
bounds_pat = re.compile(r'(bounds:\s*)(["\']?)([^"\'\n]+)\2', re.I)


def norm_box(vals):
    l, b, r, t = vals
    if l > r:
        l, r = r, l
    if b > t:
        b, t = t, b
    return l, b, r, t


def fmt(vals):
    return ", ".join(f"{v:g}" for v in vals)


changed_files = 0
changed_bounds = [0]

for path in root.rglob("*.yml"):
    text = path.read_text(encoding="utf-8")
    orig = text

    def fix_bounds(m):
        prefix, quote, val = m.group(1), m.group(2), m.group(3)
        parts = [p for p in re.split(r"[\s,]+", val.strip()) if p]
        if len(parts) != 4:
            return m.group(0)
        try:
            vals = [float(p) for p in parts]
        except ValueError:
            return m.group(0)
        l, b, r, t = vals
        if l <= r and b <= t:
            return m.group(0)
        nl, nb, nr, nt = norm_box(vals)
        changed_bounds[0] += 1
        new_val = fmt((nl, nb, nr, nt))
        return f"{prefix}{quote}{new_val}{quote}"

    text = bounds_pat.sub(fix_bounds, text)

    if text != orig:
        path.write_text(text, encoding="utf-8", newline="\n")
        changed_files += 1

print(f"fixed {changed_bounds[0]} bounds in {changed_files} files")
