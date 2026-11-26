"""Converts the breathing vents' Black Mesa sounds to Vorbis under Resources/Audio/_WF/PlanetVents/<group>/, with the
plant rules of Tools/_WF/LivingWorld/import_bms.py (mono, one-shot loudness ceiling, -2 dB limiter).

Run from the repository root: python Tools/_WF/PlanetVents/import_vent_sounds.py [path to the Black Mesa sound folder]
"""
import importlib.util
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Resources/Audio/_WF/PlanetVents"

_spec = importlib.util.spec_from_file_location("import_bms", ROOT / "Tools/_WF/LivingWorld/import_bms.py")
bms = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(bms)

GROUPS = {
    # The slow wet heave of a breath drawn in.
    "inhale": (False, [
        "npc/gonarch/gon_breath2.wav",
        "npc/gonarch/gon_breath4.wav",
    ]),
    # The sigh and hiss of it going back out.
    "exhale": (False, [
        "xen/object/gas_hiss.wav",
        "npc/gonarch/gon_breath1.wav",
        "npc/gonarch/gon_breath3.wav",
    ]),
}


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    source = Path(args[0]) if args else bms.DEFAULT_SOURCE
    missing = [rel for _, files in GROUPS.values() for rel in files if not (source / rel).is_file()]
    if missing:
        sys.exit("Missing source files:\n  " + "\n  ".join(missing))
    count = bms.import_groups(source, OUT, GROUPS)
    # Some sources carry embedded art that ffmpeg turns into a video stream, which the engine can't decode.
    for ogg in OUT.glob("*/*.ogg"):
        stripped = ogg.with_name(ogg.stem + ".tmp.ogg")
        subprocess.run(["ffmpeg", "-y", "-v", "error", "-i", str(ogg), "-map", "0:a", "-c:a", "copy", str(stripped)],
                       check=True)
        stripped.replace(ogg)
    print(f"Wrote {count} files")


if __name__ == "__main__":
    main()
