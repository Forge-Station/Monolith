"""Package evidence even when preparation or a native server fails early."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile


def package(run: Path):
    run = run.resolve()
    output = run / "KIAS_web-agent.zip"
    temporary = output.with_suffix(".zip.tmp")
    excluded = {"builds", "obj", "bin", ".git", "data", "__pycache__"}
    hashes = {}
    with zipfile.ZipFile(temporary, "w", zipfile.ZIP_DEFLATED, compresslevel=3) as archive:
        for directory, names, files in __import__("os").walk(run):
            names[:] = [name for name in names if name not in excluded]
            for name in files:
                path = Path(directory) / name
                if path in (output, temporary) or path.suffix.lower() in (".dll", ".pdb", ".exe"):
                    continue
                relative = path.relative_to(run).as_posix()
                archive.write(path, relative)
                hashes[relative] = hashlib.sha256(path.read_bytes()).hexdigest()
        archive.writestr("ANALYSIS_README.txt",
            "Inspect launcher-status.json and native failed.json/run-failed.json first. "
            "An archive is preserved evidence, not a PASS. FULL GAME FINAL RUN NOT YET EXECUTED "
            "unless an explicit validated FullGame completion report is present.\n")
        archive.writestr("ARCHIVE_SHA256.json", json.dumps(hashes, indent=2, sort_keys=True))
    temporary.replace(output)
    print(f"Analysis archive: {output}")
    return output


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("run", type=Path)
    package(parser.parse_args().run)
