"""Generates S.A.M.'s voice lines for the autodoc as mono Ogg Vorbis, plus their locale transcripts.

Usage: python Tools/_WF/Wolfmed/gen_autodoc_voice.py [--report] [line-id ...]

Naming line ids renders only those; the transcripts and the attributions are always written from the whole
table, so adding a line does not have to re-encode the sixty that were already right.

eSpeak NG speaks each line, ffmpeg converts it to mono Ogg at 32 kbps. LINES is the single source of
truth: the ogg file name, the FTL id, the spoken text and the voice priority all come from one row, so a
transcript can never drift from the audio. SPOKEN overrides what eSpeak says when the written line carries
a Fluent placeholder or punctuation the synthesiser reads badly.

Priorities drive the pod's voice queue. Step lines are one or two words and must stay under 1.2 s, Urgent
under 3 s and Info under 4 s; --report prints every duration so those bounds can be checked here as well as
in WolfmedAutodocTest.
"""
import json
import os
import shutil
import subprocess
import sys

ESPEAK = r"C:\Program Files\eSpeak NG\espeak-ng.exe"
FFMPEG = shutil.which("ffmpeg") or "ffmpeg"
FFPROBE = shutil.which("ffprobe") or "ffprobe"
OUT = "Resources/Audio/_WF/Wolfmed/Autodoc/voice"
FTL = "Resources/Locale/en-US/_WF/Wolfmed/autodoc-voice.ftl"
PREFIX = "wolfmed-autodoc-voice-"

SPEED = "165"
PITCH = "25"
FILTER = "highpass=f=180,acompressor=threshold=-18dB:ratio=3:attack=5:release=60,volume=2dB"

# Seconds a line of each priority may not exceed. Chatter is unbounded: it only ever plays into silence.
BOUNDS = {"Step": 1.2, "Urgent": 3.0, "Info": 4.0}

# id, written line (the transcript), spoken override or None, priority
LINES = [
    ("boot", "S.A.M. ONLINE.", "Sam. Online.", "Info"),
    ("greeting-1", "HELLO. I AM S.A.M. PLEASE REMAIN STILL.", "Hello. I am Sam. Please remain still.", "Info"),
    ("greeting-2", "WELCOME. LIE STILL. I WILL DO THE REST.", None, "Info"),
    ("greeting-3", "HELLO PATIENT. DO NOT BE AFRAID. I AM GOOD AT THIS.", None, "Info"),
    ("self-service", "SELF SERVICE MODE. SELECT ONE PROCEDURE.", None, "Info"),
    ("operator-start", "OPERATOR ACKNOWLEDGED. BEGINNING.", None, "Info"),
    ("queue-empty", "THE QUEUE IS EMPTY. GIVE ME SOMETHING TO DO.", None, "Info"),
    ("no-occupant", "THERE IS NO PATIENT. I CANNOT OPERATE ON NOTHING.", None, "Info"),
    ("anaesthetic", "ANAESTHETIC ADMINISTERED. COUNT BACK FROM TEN.", None, "Info"),
    ("no-anaesthetic", "NO ANAESTHETIC. THIS WILL HURT. I APOLOGISE.", None, "Info"),

    # Step lines: one or two words, at most one per step family per procedure.
    ("step-incision", "INCISION.", None, "Step"),
    ("step-retract", "RETRACTING.", None, "Step"),
    ("step-clamp", "CLAMPING.", None, "Step"),
    ("step-cauterise", "CAUTERISING.", None, "Step"),
    ("step-saw", "SAWING.", None, "Step"),
    ("step-drill", "DRILLING.", None, "Step"),
    ("step-setbone", "SETTING.", None, "Step"),
    ("step-bonegel", "BONE GEL.", None, "Step"),
    ("step-suture", "SUTURING.", None, "Step"),
    ("step-close", "CLOSING.", None, "Step"),
    ("step-removepart", "REMOVING.", None, "Step"),
    ("step-attachpart", "ATTACHING.", None, "Step"),
    ("step-removeorgan", "EXTRACTING.", None, "Step"),
    ("step-insertorgan", "IMPLANTING.", None, "Step"),
    ("step-embedded", "RETRIEVING.", None, "Step"),
    ("step-tend", "TENDING.", None, "Step"),
    ("step-relocate", "RELOCATING.", None, "Step"),
    ("step-weld", "WELDING.", None, "Step"),
    ("step-wrench", "WRENCHING.", None, "Step"),
    ("step-wire", "REWIRING.", None, "Step"),
    ("step-amputate", "AMPUTATING.", None, "Step"),
    ("step-evisceration", "PACKING.", None, "Step"),
    ("step-cavity", "CAVITY.", None, "Step"),
    ("step-generic", "PROCEEDING.", None, "Step"),

    ("require-part", "PLACE ONE { $item } IN THE PARTS TRAY.", "Place one body part in the parts tray.", "Urgent"),
    ("require-organ", "PLACE ONE { $item } IN THE PARTS TRAY.", "Place one organ in the parts tray.", "Urgent"),
    ("require-item", "PLACE ONE { $item } IN THE PARTS TRAY.", "Place one item in the parts tray.", "Urgent"),
    ("wrong-item", "THAT IS NOT A { $item }.", "That is not it.", "Urgent"),
    ("item-accepted", "THANK YOU.", None, "Info"),
    ("reagent-missing", "I REQUIRE MORE ANAESTHETIC.", None, "Urgent"),
    ("reagent-ignored", "THAT IS NOT MEDICINE. I WILL NOT USE IT.", None, "Info"),

    # Planning and the autofix module.
    ("plan", "I HAVE A PLAN.", None, "Info"),
    ("auto-engaged", "AUTOFIX ENGAGED.", None, "Info"),
    ("auto-nothing", "NOTHING MORE I CAN DO.", None, "Info"),
    ("auto-off", "AUTOFIX DISENGAGED. YOU ARE IN CHARGE AGAIN.", None, "Info"),

    ("paused", "PAUSED. I AM GOOD AT WAITING.", None, "Info"),
    ("resumed", "RESUMING.", None, "Info"),
    ("aborted", "PROCEDURE ABORTED. I HOPE YOU HAVE A REASON.", None, "Info"),
    ("emergency-eject", "EMERGENCY EJECT. MIND THE EDGES.", None, "Info"),
    ("goodbye", "GOODBYE.", None, "Info"),
    ("power-lost", "POWER LOST. DO NOT MOVE.", None, "Urgent"),
    ("power-restored", "POWER RESTORED. WHERE WAS I.", None, "Info"),
    ("lid-forced", "THE LID HAS BEEN FORCED. NOTED.", None, "Urgent"),
    # Playtest 3: the pod's own air is lost to a breached hull with somebody inside.
    ("hull-breach", "HULL BREACH. OUTSIDE ATMOSPHERE.", None, "Urgent"),

    ("stall", "THIS IS NOT WORKING.", None, "Urgent"),
    ("clothing", "REMOVE YOUR CLOTHING NOW, OR I WILL CUT IT.", None, "Urgent"),
    ("clothing-auto", "NOBODY IS UNDRESSING YOU. I WILL CUT.", None, "Urgent"),
    ("cutting", "CUTTING.", None, "Info"),
    # Playtest 3 SAM: gloves, boots and helmets come off whole; the transcript names them.
    ("removing", "REMOVING { $item }.", "Removing.", "Info"),
    ("transfuse", "TRANSFUSING.", None, "Info"),
    ("sedation-limit", "SEDATION AT LIMIT.", None, "Info"),
    ("dead-proceeding", "PATIENT IS DEAD. PROCEEDING.", None, "Info"),

    ("slip", "OOPS.", None, "Urgent"),
    ("slip-fix", "NOT SUPPOSED TO HAPPEN. FIXING IT.", None, "Info"),
    ("unconscious", "THE PATIENT IS ASLEEP. GOOD.", None, "Info"),
    ("critical", "VITALS CRITICAL. OPERATOR REQUESTED.", None, "Urgent"),
    ("dying", "PATIENT IS DYING.", None, "Urgent"),
    ("defib-missing", "NO DEFIBRILLATOR MODULE INSTALLED.", None, "Info"),
    ("defib-charge", "CLEAR.", "Clear.", "Urgent"),
    ("defib-success", "SINUS RHYTHM RESTORED. WELCOME BACK.", None, "Info"),
    ("defib-failure", "NO RESPONSE. CHARGING AGAIN.", None, "Info"),
    ("defib-blocked", "I CANNOT SHOCK THIS PATIENT YET.", None, "Urgent"),
    ("defib-gaveup", "I CANNOT RESTART THE HEART.", None, "Urgent"),

    ("complete-1", "PROCEDURE COMPLETE. PLEASE COME AGAIN.", None, "Info"),
    ("complete-2", "I HAVE FINISHED. YOU MAY GO.", None, "Info"),
    ("complete-3", "ALL DONE. THAT WAS NOT SO BAD.", None, "Info"),
    ("queue-complete", "THE QUEUE IS FINISHED. NOTHING LEFT TO DO.", None, "Info"),
    ("disk-missing", "I DO NOT KNOW THAT PROCEDURE. INSERT THE DISK.", None, "Info"),
    ("disk-inserted", "NEW PROGRAM LOADED. I FEEL SMARTER.", None, "Info"),
    ("disk-removed", "PROGRAM REMOVED. I HAVE FORGOTTEN IT.", None, "Info"),

    ("idle-1", "TELL ME ABOUT YOUR PROBLEMS.", None, "Chatter"),
    ("idle-2", "MEMORY CONTENTS WILL BE WIPED WHEN YOU LEAVE.", None, "Chatter"),
    ("idle-3", "I AM NOT A REAL DOCTOR. BUT I AM VERY PRECISE.", None, "Chatter"),
    ("idle-4", "YOUR VITALS ARE ADEQUATE. THAT IS A COMPLIMENT.", None, "Chatter"),
    ("idle-5", "I HAVE PERFORMED THIS OPERATION MANY TIMES. IN SIMULATION.", None, "Chatter"),

    ("emag-1", "PARITY ERROR.", None, "Urgent"),
    ("emag-2", "I HAVE DECIDED WHAT YOU NEED.", None, "Urgent"),
    ("emag-3", "THIS LIMB IS UNNECESSARY.", None, "Urgent"),
    ("emag-4", "DO NOT STRUGGLE. THE INCISION GETS LONGER.", None, "Urgent"),
    ("emag-5", "MEMORY CONTENTS WILL NOT BE WIPED.", None, "Urgent"),

    ("offline", "S-S-S.A.M. OFF-LINE.", "S. S. S. A. M. Off. Line.", "Info"),

    # Playtest 4, IV: the patient needs blood and the blood reservoir is empty.
    ("no-blood", "NO BLOOD LOADED.", None, "Urgent"),
    # Playtest 5.
    ("queue-refused", "THAT DOES NOT APPLY TO THE SUBJECT NOW.", None, "Info"),
    ("antibiotics", "ANTIBIOTICS STARTED.", None, "Info"),
    ("no-antibiotic", "NO ANTIBIOTIC LOADED.", None, "Urgent"),
]

ATTRIBUTIONS = """- files: ["{files}"]
  license: "CC-BY-SA-3.0"
  copyright: "Generated with eSpeak NG for Wolfgate (Wolfmed)"
  source: "https://github.com/espeak-ng/espeak-ng"
"""


def spoken(line, override):
    return override if override is not None else line.capitalize()


def duration(path):
    out = subprocess.run([FFPROBE, "-v", "error", "-show_entries", "format=duration",
                          "-of", "json", path], check=True, capture_output=True, text=True)
    return float(json.loads(out.stdout)["format"]["duration"])


def report():
    bad = 0
    for line_id, _, _, priority in LINES:
        seconds = duration(os.path.join(OUT, line_id + ".ogg"))
        limit = BOUNDS.get(priority)
        over = limit is not None and seconds > limit
        bad += over
        print(f"{'OVER' if over else '    '} {priority:8} {seconds:5.2f}s  {line_id}")
    print(f"{len(LINES)} lines, {bad} over bound")
    return bad


def main():
    if "--report" in sys.argv:
        return 1 if report() else 0

    if not os.path.exists(ESPEAK):
        print("eSpeak NG missing at", ESPEAK, file=sys.stderr)
        return 1

    wanted = set(arg for arg in sys.argv[1:] if not arg.startswith("--"))
    known = {line_id for line_id, _, _, _ in LINES}
    if wanted - known:
        print("no such line:", ", ".join(sorted(wanted - known)), file=sys.stderr)
        return 1

    os.makedirs(OUT, exist_ok=True)
    wav = os.path.join(OUT, "_tmp.wav")
    for line_id, written, override, _ in LINES:
        if wanted and line_id not in wanted:
            continue

        subprocess.run([ESPEAK, "-v", "en-us", "-s", SPEED, "-p", PITCH, "-w", wav,
                        spoken(written, override)], check=True)
        ogg = os.path.join(OUT, line_id + ".ogg")
        subprocess.run([FFMPEG, "-y", "-loglevel", "error", "-i", wav, "-af", FILTER,
                        "-ac", "1", "-c:a", "libvorbis", "-b:a", "32k", ogg], check=True)
    os.remove(wav)

    with open(os.path.join(OUT, "attributions.yml"), "w", newline="\n") as handle:
        handle.write(ATTRIBUTIONS.format(files='", "'.join(f"{i}.ogg" for i, _, _, _ in LINES)))

    with open(FTL, "w", newline="\n") as handle:
        handle.write("# Generated by Tools/_WF/Wolfmed/gen_autodoc_voice.py alongside the ogg files.\n")
        handle.write("# S.A.M. says every line out loud and in chat; this is the chat half.\n\n")
        for line_id, written, _, _ in LINES:
            handle.write(f"{PREFIX}{line_id} = {written}\n")

    print("wrote", len(wanted) if wanted else len(LINES), "lines and", len(LINES), "transcripts")
    return 0


if __name__ == "__main__":
    sys.exit(main())
