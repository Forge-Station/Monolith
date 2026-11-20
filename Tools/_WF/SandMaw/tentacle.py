"""Draws the sand maw's tentacle texture: one ridged, sucker-studded segment, drawn once per rope segment.

Run from the repository root: python Tools/_WF/SandMaw/tentacle.py
"""
import math
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Resources/Textures/_WF/SandMaw/tentacle.png"
W, H = 16, 16  # u runs across the tentacle, v along it


def main():
    img = Image.new("RGBA", (W, H))
    px = img.load()
    for v in range(H):
        for u in range(W):
            across = (u + 0.5) / W * 2 - 1  # -1 .. 1
            if abs(across) > 0.95:
                continue
            # Round shading across, a groove between segments along.
            light = math.sqrt(max(0.0, 1 - across * across))
            groove = 0.55 if v in (0, H - 1) else 1.0
            r = int((70 + 120 * light) * groove)
            g = int((14 + 34 * light) * groove)
            b = int((22 + 40 * light) * groove)
            px[u, v] = (r, g, b, 255)
    # A pale sucker on one side of each segment.
    for v in range(5, 11):
        for u in range(2, 7):
            if (u - 4) ** 2 + (v - 8) ** 2 <= 5:
                px[u, v] = (226, 168, 160, 255) if (u - 4) ** 2 + (v - 8) ** 2 > 1 else (120, 40, 50, 255)
    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.save(OUT)
    print(f"Wrote {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
