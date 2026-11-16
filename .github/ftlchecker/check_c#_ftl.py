#!/usr/bin/env python3
"""Find static C# localization keys without any FTL declaration."""

from __future__ import annotations

import argparse
import bisect
import re
import sys
from dataclasses import dataclass
from pathlib import Path


DEFAULT_LOCALE_DIRS = (
    Path("Resources/Locale"),
    Path("RobustToolbox/Resources/Locale"),
)
DEFAULT_SOURCE_DIRS = (
    Path("Content.Client"),
    Path("Content.Server"),
    Path("Content.Shared"),
)
DEFAULT_PROTOTYPES_DIR = Path("Resources/Prototypes")
DEFAULT_RULES_FILE = Path(".github/ftlchecker/prototype_ftl_rules.yml")

FTL_KEY_RE = re.compile(r"^(?P<key>[A-Za-z][A-Za-z0-9_-]*)\s*=")
FTL_TERM_RE = re.compile(r"^(?P<key>-[A-Za-z][A-Za-z0-9_-]*)\s*=")
FTL_REFERENCE_RE = re.compile(
    r"\{\s*(?P<term>-)?(?P<key>[A-Za-z][A-Za-z0-9_-]*)"
    r"(?:\.[A-Za-z][A-Za-z0-9_-]*)?(?![A-Za-z0-9_-])"
)
LOC_CALL_RE = re.compile(
    r'\bLoc\.(?:GetString|TryGetString)\s*\(\s*(?P<prefix>\$@|@\$|\$|@)?'
    r'"(?P<key>[A-Za-z][A-Za-z0-9_-]*(?:\.[A-Za-z][A-Za-z0-9_-]*)?)"(?=\s*[,)])'
)
LOC_IDENTIFIER_CALL_RE = re.compile(
    r"\bLoc\.(?:GetString|TryGetString)\s*\(\s*@?(?P<name>[A-Za-z_]\w*)"
    r"\s*(?=[,)])"
)
CONST_STRING_RE = re.compile(
    r'\bconst\s+string\s+@?(?P<name>[A-Za-z_]\w*)\s*=\s*@?'
    r'"(?P<key>[A-Za-z][A-Za-z0-9_-]*(?:\.[A-Za-z][A-Za-z0-9_-]*)?)"(?=\s*;)'
)


@dataclass(frozen=True)
class CSharpReference:
    key: str
    path: Path
    line: int


@dataclass(frozen=True)
class FtlReference:
    key: str
    path: Path
    line: int


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Check static C# localization references against FTL messages."
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=Path("."),
        help="Repository root. Defaults to the current directory.",
    )
    parser.add_argument(
        "--locale-dir",
        type=Path,
        action="append",
        default=None,
        help=(
            "Directory containing locale FTL files, relative to repository root; "
            "can be specified multiple times."
        ),
    )
    parser.add_argument(
        "--source-dir",
        action="append",
        type=Path,
        default=None,
        help="C# source directory, relative to repository root; can be repeated.",
    )
    parser.add_argument(
        "--prototypes-dir",
        type=Path,
        default=DEFAULT_PROTOTYPES_DIR,
        help="Prototype YAML directory relative to repository root.",
    )
    parser.add_argument(
        "--rules-file",
        type=Path,
        default=DEFAULT_RULES_FILE,
        help="Prototype localization rules YAML file relative to repository root.",
    )
    parser.add_argument(
        "--show-used",
        action="store_true",
        help="Print additional scan statistics.",
    )
    return parser.parse_args()


def get_ftl_files(directory: Path) -> list[Path]:
    """Return all FTL files under a locale directory."""
    return sorted(path for path in directory.rglob("*.ftl") if path.is_file())


def mask_ftl_comments_and_strings(content: str) -> str:
    """Mask Fluent comments and quoted literals while preserving line positions."""
    characters = list(content)
    line_start = True
    in_comment = False
    in_string = False
    index = 0

    while index < len(content):
        char = content[index]

        if char in "\r\n":
            in_comment = False
            line_start = True
            index += 1
            continue

        if in_comment:
            characters[index] = " "
            index += 1
            continue

        if line_start and char.isspace():
            index += 1
            continue

        if line_start and char == "#":
            in_comment = True
            characters[index] = " "
            line_start = False
            index += 1
            continue

        line_start = False

        if in_string:
            characters[index] = " "
            if char == "\\" and index + 1 < len(content):
                if content[index + 1] not in "\r\n":
                    characters[index + 1] = " "
                index += 2
                continue
            if char == '"':
                in_string = False
            index += 1
            continue

        if char == '"':
            in_string = True
            characters[index] = " "
        index += 1

    return "".join(characters)


def read_ftl_data(
    locale_dirs: list[Path],
) -> tuple[set[str], list[FtlReference]]:
    """Collect FTL declarations and references to other messages."""
    keys: set[str] = set()
    references: list[FtlReference] = []
    for locale_dir in locale_dirs:
        for path in get_ftl_files(locale_dir):
            try:
                content = path.read_text(encoding="utf-8-sig")
            except OSError as exc:
                raise RuntimeError(f"Cannot read FTL file {path}: {exc}") from exc

            line_starts = [0]
            line_starts.extend(match.end() for match in re.finditer("\n", content))

            for line in content.splitlines():
                if line.lstrip().startswith("#"):
                    continue
                match = FTL_KEY_RE.match(line)
                if match:
                    keys.add(match.group("key"))
                    continue
                term_match = FTL_TERM_RE.match(line)
                if term_match:
                    keys.add(term_match.group("key"))

            reference_content = mask_ftl_comments_and_strings(content)
            for match in FTL_REFERENCE_RE.finditer(reference_content):
                key = (match.group("term") or "") + match.group("key")
                if not match.group("term") and key.isupper():
                    if reference_content[match.end():].lstrip().startswith("("):
                        continue
                references.append(
                    FtlReference(
                        key,
                        path,
                        line_number_at(
                            line_starts,
                            match.start("term")
                            if match.group("term")
                            else match.start("key"),
                        ),
                    )
                )
    return keys, references


def strip_csharp_comments(content: str) -> str:
    """Mask C# comments while leaving string literals and line positions intact."""
    characters = list(content)
    index = 0

    def mask(start: int, end: int) -> None:
        for offset in range(start, end):
            if characters[offset] not in "\r\n":
                characters[offset] = " "

    while index < len(content):
        if content.startswith("//", index):
            end = content.find("\n", index + 2)
            if end == -1:
                end = len(content)
            mask(index, end)
            index = end
            continue

        if content.startswith("/*", index):
            end = content.find("*/", index + 2)
            end = len(content) if end == -1 else end + 2
            mask(index, end)
            index = end
            continue

        if content.startswith('"""', index):
            end = content.find('"""', index + 3)
            index = len(content) if end == -1 else end + 3
            continue

        if content[index] == '"':
            verbatim = (
                index > 0 and content[index - 1] == "@"
                or index > 1 and content[index - 2 : index] == "@$"
            )
            index += 1
            while index < len(content):
                if verbatim and content.startswith('""', index):
                    index += 2
                elif content[index] == "\\" and not verbatim:
                    index += 2
                elif content[index] == '"':
                    index += 1
                    break
                else:
                    index += 1
            continue

        if content[index] == "'":
            index += 1
            while index < len(content):
                if content[index] == "\\":
                    index += 2
                elif content[index] == "'":
                    index += 1
                    break
                else:
                    index += 1
            continue

        index += 1

    return "".join(characters)


def line_number_at(line_starts: list[int], offset: int) -> int:
    """Return a one-based line number for a source offset."""
    return bisect.bisect_right(line_starts, offset)


def scan_csharp_file(path: Path) -> list[CSharpReference]:
    """Extract direct literal keys and uniquely resolvable string constants."""
    try:
        source = strip_csharp_comments(path.read_text(encoding="utf-8-sig"))
    except OSError as exc:
        raise RuntimeError(f"Cannot read C# file {path}: {exc}") from exc

    constants: dict[str, set[str]] = {}
    for match in CONST_STRING_RE.finditer(source):
        constants.setdefault(match.group("name"), set()).add(match.group("key"))

    line_starts = [0]
    line_starts.extend(
        match.end() for match in re.finditer("\n", source)
    )

    references = []
    for match in LOC_CALL_RE.finditer(source):
        key = match.group("key")
        if "$" in (match.group("prefix") or "") and "{" in key:
            continue
        references.append(
            CSharpReference(
                key,
                path,
                line_number_at(line_starts, match.start()),
            )
        )

    for match in LOC_IDENTIFIER_CALL_RE.finditer(source):
        values = constants.get(match.group("name"), set())
        if len(values) != 1:
            continue
        references.append(
            CSharpReference(
                next(iter(values)),
                path,
                line_number_at(line_starts, match.start()),
            )
        )

    return references


def scan_csharp_references(source_dirs: list[Path]) -> list[CSharpReference]:
    """Collect static localization references under the supplied source roots."""
    references: list[CSharpReference] = []
    for directory in source_dirs:
        for path in sorted(directory.rglob("*.cs")):
            if path.is_file():
                references.extend(scan_csharp_file(path))
    return references


def find_missing_references(
    references: list[CSharpReference],
    ftl_keys: set[str],
) -> list[CSharpReference]:
    """Return every static C# lookup whose key has no FTL message."""
    return sorted(
        (reference for reference in references if reference.key not in ftl_keys),
        key=lambda reference: (str(reference.path).casefold(), reference.line, reference.key),
    )


def find_missing_ftl_references(
    references: list[FtlReference],
    ftl_keys: set[str],
) -> list[FtlReference]:
    """Return FTL references without a matching message or term declaration."""
    return sorted(
        (reference for reference in references if reference.key not in ftl_keys),
        key=lambda reference: (str(reference.path).casefold(), reference.line, reference.key),
    )


def load_generated_prototype_keys(
    prototypes_dir: Path,
    rules_path: Path,
) -> set[str]:
    """Load keys that are generated from configured prototype IDs."""
    try:
        import check_prototype_ftl as prototype_checker

        rules = prototype_checker.load_prototype_rules(rules_path)
        prototype_ids, _, _ = prototype_checker.scan_prototypes(
            prototypes_dir,
            set(),
            rules,
        )
        generated_keys, _ = prototype_checker.generated_prototype_keys(
            prototype_ids,
            rules,
        )
    except RuntimeError as exc:
        raise RuntimeError(f"Cannot load generated prototype localization keys: {exc}") from exc

    return generated_keys


def main() -> int:
    args = parse_args()
    root = args.root.resolve()
    locale_dirs = args.locale_dir or list(DEFAULT_LOCALE_DIRS)
    locale_dirs = [
        (root / directory).resolve() if not directory.is_absolute()
        else directory.resolve()
        for directory in locale_dirs
    ]
    source_dirs = args.source_dir or list(DEFAULT_SOURCE_DIRS)
    source_dirs = [
        (root / directory).resolve() if not directory.is_absolute()
        else directory.resolve()
        for directory in source_dirs
    ]
    prototypes_dir = (
        (root / args.prototypes_dir).resolve()
        if not args.prototypes_dir.is_absolute()
        else args.prototypes_dir.resolve()
    )
    rules_path = (
        (root / args.rules_file).resolve()
        if not args.rules_file.is_absolute()
        else args.rules_file.resolve()
    )

    missing_locales = [directory for directory in locale_dirs if not directory.is_dir()]
    if missing_locales:
        print(
            "ERROR: FTL directory not found: "
            + ", ".join(str(directory) for directory in missing_locales),
            file=sys.stderr,
        )
        return 2

    missing_sources = [directory for directory in source_dirs if not directory.is_dir()]
    if missing_sources:
        print(
            "ERROR: C# source directory not found: "
            + ", ".join(str(directory) for directory in missing_sources),
            file=sys.stderr,
        )
        return 2
    if not prototypes_dir.is_dir():
        print(f"ERROR: Prototype directory not found: {prototypes_dir}", file=sys.stderr)
        return 2

    try:
        print("Reading FTL message declarations...")
        ftl_keys, ftl_references = read_ftl_data(locale_dirs)
        generated_keys = load_generated_prototype_keys(prototypes_dir, rules_path)
        ftl_keys.update(generated_keys)
        print("Scanning C# localization lookups...")
        references = scan_csharp_references(source_dirs)
    except RuntimeError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2

    missing = find_missing_references(references, ftl_keys)
    missing_ftl_references = find_missing_ftl_references(ftl_references, ftl_keys)

    if args.show_used:
        print()
        print("Scan statistics:")
        print(f"  FTL message keys: {len(ftl_keys)}")
        print(f"  FTL message references: {len(ftl_references)}")
        print(f"  Static C# lookups: {len(references)}")
        print(f"  Missing FTL keys: {len(missing)}")
        print(f"  Missing FTL references: {len(missing_ftl_references)}")
        print()

    if not missing and not missing_ftl_references:
        print("All static C# localization keys have FTL declarations.")
        return 0

    if missing:
        print(f"Found {len(missing)} C# localization lookups without an FTL declaration:")
        for reference in missing:
            try:
                relative_path = reference.path.relative_to(root)
            except ValueError:
                relative_path = reference.path
            print(f"{relative_path}:{reference.line}: {reference.key}")

    if missing_ftl_references:
        print(f"Found {len(missing_ftl_references)} FTL references without a declaration:")
        for reference in missing_ftl_references:
            try:
                relative_path = reference.path.relative_to(root)
            except ValueError:
                relative_path = reference.path
            print(f"{relative_path}:{reference.line}: {reference.key}")

    return 1


if __name__ == "__main__":
    sys.exit(main())
