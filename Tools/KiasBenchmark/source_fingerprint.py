"""Hash current laboratory inputs independently of previous compiled manifests."""
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def capture():
    paths = set()
    for relative in (
        "Content.Server/_Forge/KIAS", "Content.Shared/_Forge/KIAS", "Content.Client/_Forge/KIAS",
        "Content.IntegrationTests/Tests/_Forge/KIAS", "Resources/Prototypes/_Forge/KIAS",
        "Resources/Locale/ru-RU/_Forge/KIAS", "Resources/Locale/en-US/_Forge/KIAS", "Tools/KiasBenchmark",
        "Resources/Textures/_Forge/KIAS",
    ):
        paths.update(path for path in (ROOT / relative).rglob("*")
                     if path.is_file() and path.suffix in (".cs", ".xaml", ".yml", ".ftl", ".py", ".ps1", ".md", ".json", ".png"))
    for culture in ('ru-RU', 'en-US'):
        paths.update((ROOT/f'Resources/Locale/{culture}/_Forge').glob('kias*.ftl'))
    paths.update((ROOT / "Run_KIAS_Tests.bat", ROOT / "Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml"))
    untracked = subprocess.check_output(["git", "-C", str(ROOT), "ls-files", "--others", "--exclude-standard", "-z"]).decode().split("\0")
    paths.update(ROOT / name for name in untracked if name and not name.replace('\\', '/').startswith('KIAS_REVIEW/') and (ROOT / name).is_file()
                 and Path(name).suffix in (".cs", ".csproj", ".props", ".targets", ".yml", ".ftl", ".json", ".py", ".ps1", ".md", ".bat"))
    files = {path.relative_to(ROOT).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest() for path in sorted(paths)}
    engine = subprocess.check_output(["git", "-C", str(ROOT / "RobustToolbox"), "rev-parse", "HEAD"], text=True).strip()
    head = subprocess.check_output(["git", "-C", str(ROOT), "rev-parse", "HEAD"], text=True).strip()
    working_patch = subprocess.check_output(["git", "-C", str(ROOT), "diff", "--binary", "HEAD"], stderr=subprocess.PIPE)
    engine_patch = subprocess.check_output(["git", "-C", str(ROOT / "RobustToolbox"), "diff", "--binary", "HEAD"], stderr=subprocess.PIPE)
    value = {"head": head, "engine": engine, "files": files,
             "workingTreePatchSha256": hashlib.sha256(working_patch).hexdigest(),
             "engineWorkingTreePatchSha256": hashlib.sha256(engine_patch).hexdigest()}
    value["sha256"] = hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()
    return value


if __name__ == "__main__":
    print(json.dumps(capture(), indent=2))
