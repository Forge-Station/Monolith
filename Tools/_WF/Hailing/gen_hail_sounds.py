"""Synthesises the hailing tones with ffmpeg. Run from the repository root."""
import subprocess
from pathlib import Path

OUT = Path("Resources/Audio/_WF/Hailing")

# Each sound is a run of (frequency in Hz, seconds) tones; 0 Hz is a gap.
SOUNDS = {
    "hail_ring": [(1568, 0.11), (2093, 0.11), (1568, 0.11), (2093, 0.16), (0, 0.12),
                  (1568, 0.11), (2093, 0.11), (1568, 0.11), (2093, 0.16)],
    "hail_calling": [(523, 0.14), (0, 0.08), (523, 0.14)],
    "hail_open": [(880, 0.08), (1175, 0.08), (1568, 0.14)],
    "hail_close": [(1568, 0.08), (1175, 0.08), (880, 0.14)],
    "hail_message": [(1760, 0.05), (2349, 0.07)],
}


def render(name, segments):
    args = ["ffmpeg", "-y", "-loglevel", "error"]
    chain = []

    for i, (freq, seconds) in enumerate(segments):
        # A quiet octave on top keeps the tone from sounding like a test signal.
        wave = f"0.35*sin(2*PI*{freq}*t)+0.1*sin(2*PI*{freq * 2}*t)" if freq else "0"
        args += ["-f", "lavfi", "-i", f"aevalsrc={wave}:d={seconds}:s=44100"]
        fade = min(0.02, seconds / 3)
        chain.append(f"[{i}:a]afade=t=in:d=0.005,afade=t=out:st={seconds - fade:.3f}:d={fade:.3f}[s{i}]")

    joined = "".join(f"[s{i}]" for i in range(len(segments)))
    chain.append(f"{joined}concat=n={len(segments)}:v=0:a=1[out]")
    args += ["-filter_complex", ";".join(chain), "-map", "[out]", "-ac", "1", "-c:a", "libvorbis", "-q:a", "4",
             str(OUT / f"{name}.ogg")]
    subprocess.run(args, check=True)


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for sound, tones in SOUNDS.items():
        render(sound, tones)
