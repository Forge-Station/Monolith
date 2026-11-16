#!/usr/bin/env python3
"""Find potentially unused prototype localization messages in FTL files."""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path

try:
    import yaml
    from yaml.nodes import MappingNode, Node, ScalarNode, SequenceNode
except ImportError:
    print(
        "ERROR: PyYAML is not installed.\n"
        "Install it using: python -m pip install PyYAML",
        file=sys.stderr,
    )
    sys.exit(2)


DEFAULT_LOCALE_DIR = Path("Resources/Locale/ru-RU/ss14-ru/prototypes")
DEFAULT_PROTOTYPES_DIR = Path("Resources/Prototypes")
DEFAULT_SOURCE_DIRS = (
    Path("Content.Client"),
    Path("Content.Server"),
    Path("Content.Shared"),
)

# Values of these prototype fields can be localization IDs or Fluent references.
LOCALIZED_FIELDS = {
    "name",
    "description",
    "suffix",
    "title",
    "label",
    "verb",
    "noun",
    "popup",
    "popupText",
    "message",
    "examineText",
    "examine",
    "status",
    "statusText",
    "failureMessage",
    "successMessage",
    "interactionText",
    "useText",
    "boardName",
    "toolName",
    "hypoFailedMessage",
    "gunFailedMessage",
    "pulseLog",
    "prefix",
    "subtype",
    "displayName",
    "flavorText",
    "tooltip",
    "announcement",
    "announcementText",
    "jobTitle",
    "note",
}

FTL_KEY_RE = re.compile(r"^(?P<key>[A-Za-z][A-Za-z0-9_-]*)\s*=")
FTL_REFERENCE_RE = re.compile(
    r"\{\s*([A-Za-z][A-Za-z0-9_-]*)"
    r"(?:\.[A-Za-z][A-Za-z0-9_-]*)?"
    r"\s*(?:![^}]*)?\}"
)
CSHARP_LOC_CALL_RE = re.compile(
    r'\bLoc\.(?:GetString|TryGetString)\s*\(\s*(?:\$@|@\$|\$|@)?'
    r'"(?P<key>[A-Za-z][A-Za-z0-9_-]*)"'
)
DIRECT_KEY_RE = re.compile(r"[A-Za-z][A-Za-z0-9_-]*")


@dataclass(frozen=True)
class FtlMessage:
    path: Path
    line: int


@dataclass(frozen=True)
class PrototypeFieldFormatIssue:
    path: Path
    line: int
    prototype_type: str
    prototype_id: str
    field: str
    value: str
    pattern: str


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Find unused prototype FTL localization messages."
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
        default=DEFAULT_LOCALE_DIR,
        help="Prototype FTL directory relative to repository root.",
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
        default=None,
        help="YAML prototype type rules. Defaults to prototype_ftl_rules.yml beside the script.",
    )
    parser.add_argument(
        "--source-dir",
        action="append",
        type=Path,
        default=None,
        help="C# source directory relative to repository root; can be repeated.",
    )
    parser.add_argument(
        "--field",
        action="append",
        default=[],
        help="Additional YAML localization field; can be specified multiple times.",
    )
    parser.add_argument(
        "--no-default-fields",
        action="store_true",
        help="Disable the built-in localized field list.",
    )
    parser.add_argument(
        "--show-used",
        action="store_true",
        help="Print additional scan statistics.",
    )
    return parser.parse_args()


def get_yaml_files(directory: Path) -> list[Path]:
    """Return all YAML files in the prototype directory."""
    return sorted(
        path
        for pattern in ("*.yml", "*.yaml")
        for path in directory.rglob(pattern)
        if path.is_file()
    )


def get_ftl_files(directory: Path) -> list[Path]:
    """Return all FTL files in the selected locale directory."""
    return sorted(path for path in directory.rglob("*.ftl") if path.is_file())


def read_ftl_messages(
    locale_dir: Path,
) -> tuple[dict[str, FtlMessage], set[str]]:
    """Read top-level FTL messages and references to other messages."""
    messages: dict[str, FtlMessage] = {}
    references: set[str] = set()

    for path in get_ftl_files(locale_dir):
        try:
            content = path.read_text(encoding="utf-8-sig")
        except OSError as exc:
            raise RuntimeError(f"Cannot read FTL file {path}: {exc}") from exc

        current_message: str | None = None
        message_lines: list[str] = []
        for line_number, line in enumerate(content.splitlines(), start=1):
            if line.lstrip().startswith("#"):
                continue

            match = FTL_KEY_RE.match(line)
            if match:
                if current_message is not None:
                    references.update(
                        reference.group(1)
                        for reference in FTL_REFERENCE_RE.finditer(
                            "\n".join(message_lines)
                        )
                        if reference.group(1) != current_message
                    )

                current_message = match.group("key")
                message_lines = [line]
                if current_message in messages:
                    first = messages[current_message]
                    print(
                        f"WARNING: duplicate FTL key '{current_message}': "
                        f"{first.path}:{first.line} and {path}:{line_number}",
                        file=sys.stderr,
                    )
                else:
                    messages[current_message] = FtlMessage(path, line_number)
            elif current_message is not None:
                message_lines.append(line)

        if current_message is not None:
            references.update(
                reference.group(1)
                for reference in FTL_REFERENCE_RE.finditer("\n".join(message_lines))
                if reference.group(1) != current_message
            )

    return messages, references


def get_scalar(node: Node | None) -> str | None:
    """Return the value of a YAML scalar node."""
    if isinstance(node, ScalarNode):
        return node.value
    return None


def walk_mappings(node: Node, seen: set[int] | None = None):
    """Recursively yield each YAML mapping, including mappings in aliases once."""
    if seen is None:
        seen = set()

    node_id = id(node)
    if node_id in seen:
        return
    seen.add(node_id)

    if isinstance(node, MappingNode):
        yield node
        for key_node, value_node in node.value:
            yield from walk_mappings(key_node, seen)
            yield from walk_mappings(value_node, seen)
    elif isinstance(node, SequenceNode):
        for child in node.value:
            yield from walk_mappings(child, seen)


def iter_scalar_nodes(node: Node, seen: set[int] | None = None):
    """Yield scalar nodes in a field value."""
    if seen is None:
        seen = set()

    node_id = id(node)
    if node_id in seen:
        return
    seen.add(node_id)

    if isinstance(node, ScalarNode):
        yield node
    elif isinstance(node, MappingNode):
        for _, value_node in node.value:
            yield from iter_scalar_nodes(value_node, seen)
    elif isinstance(node, SequenceNode):
        for child in node.value:
            yield from iter_scalar_nodes(child, seen)


def mapping_to_dict(mapping: MappingNode) -> dict[str, Node]:
    """Convert a YAML mapping into a simple dictionary."""
    result: dict[str, Node] = {}
    for key_node, value_node in mapping.value:
        key = get_scalar(key_node)
        if key is not None:
            result[key] = value_node
    return result


def iter_prototype_mappings(document: Node):
    """Yield prototype definitions at the document root, not nested component maps."""
    if isinstance(document, SequenceNode):
        for child in document.value:
            if isinstance(child, MappingNode):
                yield child
    elif isinstance(document, MappingNode):
        yield document


def normalize_field(field_name: str) -> str:
    """Normalize YAML field spelling so casing and separators do not matter."""
    return re.sub(r"[^a-z0-9]", "", field_name.casefold())


def load_prototype_rules(
    rules_path: Path,
) -> dict[str, dict[str, object]]:
    """Load and validate type-specific generated-key and field rules."""
    try:
        raw_rules = yaml.safe_load(rules_path.read_text(encoding="utf-8-sig"))
    except yaml.YAMLError as exc:
        raise RuntimeError(f"YAML parsing error in rules file {rules_path}: {exc}") from exc
    except OSError as exc:
        raise RuntimeError(f"Cannot read rules file {rules_path}: {exc}") from exc

    if not isinstance(raw_rules, dict) or not isinstance(
        raw_rules.get("prototype_types"), dict
    ):
        raise RuntimeError(
            f"Rules file must contain a 'prototype_types' mapping: {rules_path}"
        )

    supported_transforms = {
        "identity",
        "kebab",
        "construction_recipe_locale",
    }
    rules: dict[str, dict[str, object]] = {}

    for prototype_type, raw_rule in raw_rules["prototype_types"].items():
        if not isinstance(prototype_type, str) or not isinstance(raw_rule, dict):
            raise RuntimeError(
                f"Invalid rule for prototype type {prototype_type!r} in {rules_path}"
            )

        generated = raw_rule.get("generated_keys", [])
        if not isinstance(generated, list):
            raise RuntimeError(
                f"'generated_keys' for {prototype_type} must be a list"
            )

        generated_rules: list[dict[str, object]] = []
        for generated_rule in generated:
            if not isinstance(generated_rule, dict):
                raise RuntimeError(
                    f"Generated key rule for {prototype_type} must be a mapping"
                )

            template = generated_rule.get("template")
            transforms = generated_rule.get("id_transforms", ["identity"])
            suffixes = generated_rule.get("suffixes", [""])

            if (
                not isinstance(template, str)
                or template.count("{id}") != 1
                or "{" in template.replace("{id}", "")
                or "}" in template.replace("{id}", "")
            ):
                raise RuntimeError(
                    f"Generated key template for {prototype_type} must contain "
                    "exactly one {id} placeholder"
                )
            if (
                not isinstance(transforms, list)
                or not transforms
                or any(
                    not isinstance(transform, str)
                    or transform not in supported_transforms
                    for transform in transforms
                )
            ):
                raise RuntimeError(
                    f"Unsupported ID transform in generated key rule for {prototype_type}"
                )
            if not isinstance(suffixes, list) or any(
                not isinstance(suffix, str) for suffix in suffixes
            ):
                raise RuntimeError(
                    f"'suffixes' for generated key rule {prototype_type} must be strings"
                )
            generated_rules.append(
                {
                    "template": template,
                    "id_transforms": transforms,
                    "suffixes": suffixes,
                }
            )

        raw_field_patterns = raw_rule.get("field_patterns", {})
        if not isinstance(raw_field_patterns, dict):
            raise RuntimeError(f"'field_patterns' for {prototype_type} must be a mapping")

        field_patterns: dict[str, tuple[str, re.Pattern[str]]] = {}
        for field_name, pattern in raw_field_patterns.items():
            if not isinstance(field_name, str) or not isinstance(pattern, str):
                raise RuntimeError(
                    f"Field patterns for {prototype_type} must map field names to regex strings"
                )
            try:
                compiled_pattern = re.compile(pattern)
            except re.error as exc:
                raise RuntimeError(
                    f"Invalid field pattern for {prototype_type}.{field_name}: {exc}"
                ) from exc
            field_patterns[normalize_field(field_name)] = (pattern, compiled_pattern)

        rules[prototype_type] = {
            "generated_keys": generated_rules,
            "field_patterns": field_patterns,
        }

    return rules


def strip_csharp_comments(content: str) -> str:
    """Mask C# comments while leaving string literals intact."""
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


def scan_csharp_references(source_dirs: list[Path]) -> set[str]:
    """Collect statically specified keys passed to C# localization lookups."""
    references: set[str] = set()
    for directory in source_dirs:
        if not directory.is_dir():
            continue
        for path in directory.rglob("*.cs"):
            if not path.is_file():
                continue
            try:
                content = strip_csharp_comments(path.read_text(encoding="utf-8-sig"))
            except OSError as exc:
                raise RuntimeError(f"Cannot read C# file {path}: {exc}") from exc

            references.update(
                match.group("key")
                for match in CSHARP_LOC_CALL_RE.finditer(content)
            )
    return references


def construction_recipe_locale_suffix(prototype_id: str) -> str:
    """Mirror ConstructionRecipeLocale.PrototypeIdToRecipeKeySuffix."""
    working = prototype_id
    if working.endswith("Recipe") and len(working) > 6:
        working = working[:-6]

    if working and all("A" <= char <= "Z" or "a" <= char <= "z" for char in working):
        if all("A" <= char <= "Z" for char in working):
            return working.lower()

    if not any("A" <= char <= "Z" for char in working):
        return working.lower()

    kebab = re.sub(r"([A-Z])", r"-\1", working).lstrip("-").lower()
    return re.sub(r"([a-z])([0-9])", r"\1-\2", kebab)


def prototype_id_to_kebab(prototype_id: str) -> str:
    """Convert a prototype ID to its conventional kebab-case localization suffix."""
    separated = re.sub(r"([A-Z]+)([A-Z][a-z])", r"\1-\2", prototype_id)
    separated = re.sub(r"([a-z0-9])([A-Z])", r"\1-\2", separated)
    separated = re.sub(r"([A-Za-z])([0-9])", r"\1-\2", separated)
    return separated.lower()


def transform_prototype_id(prototype_id: str, transform: str) -> str:
    if transform == "identity":
        return prototype_id
    if transform == "kebab":
        return prototype_id_to_kebab(prototype_id)
    if transform == "construction_recipe_locale":
        return construction_recipe_locale_suffix(prototype_id)
    raise ValueError(f"Unsupported prototype ID transform: {transform}")


def scan_prototypes(
    prototypes_dir: Path,
    localized_fields: set[str],
    rules: dict[str, dict[str, object]],
) -> tuple[dict[str, set[str]], set[str], list[PrototypeFieldFormatIssue]]:
    """Collect prototype IDs, YAML references, and type-specific format issues."""
    prototype_ids_by_type: dict[str, set[str]] = {}
    references: set[str] = set()
    format_issues: list[PrototypeFieldFormatIssue] = []

    for path in get_yaml_files(prototypes_dir):
        try:
            content = path.read_text(encoding="utf-8-sig")
            documents = yaml.compose_all(content, Loader=yaml.BaseLoader)

            for document in documents:
                if document is None:
                    continue

                for mapping in iter_prototype_mappings(document):
                    values = mapping_to_dict(mapping)
                    prototype_type = get_scalar(values.get("type"))
                    prototype_id = get_scalar(values.get("id"))
                    if prototype_type is None:
                        continue

                    prototype_ids = prototype_ids_by_type.setdefault(
                        prototype_type, set()
                    )
                    if not prototype_id:
                        continue
                    prototype_ids.add(prototype_id)
                    rule = rules.get(prototype_type)
                    if rule is None:
                        continue

                    field_patterns = rule["field_patterns"]
                    assert isinstance(field_patterns, dict)
                    for key_node, value_node in mapping.value:
                        field_name = get_scalar(key_node)
                        if field_name is None:
                            continue
                        field_rule = field_patterns.get(normalize_field(field_name))
                        if field_rule is None:
                            continue

                        pattern, compiled_pattern = field_rule
                        for scalar_node in iter_scalar_nodes(value_node):
                            value = scalar_node.value.strip()
                            if value and not compiled_pattern.fullmatch(value):
                                format_issues.append(
                                    PrototypeFieldFormatIssue(
                                        path=path,
                                        line=scalar_node.start_mark.line + 1,
                                        prototype_type=prototype_type,
                                        prototype_id=prototype_id,
                                        field=field_name,
                                        value=value,
                                        pattern=pattern,
                                    )
                                )

                for mapping in walk_mappings(document):
                    for key_node, value_node in mapping.value:
                        field_name = get_scalar(key_node)
                        if field_name is None:
                            continue
                        if normalize_field(field_name) not in localized_fields:
                            continue

                        for scalar_node in iter_scalar_nodes(value_node):
                            value = scalar_node.value.strip()
                            if DIRECT_KEY_RE.fullmatch(value):
                                references.add(value)
                            references.update(
                                match.group(1)
                                for match in FTL_REFERENCE_RE.finditer(value)
                            )

        except yaml.YAMLError as exc:
            raise RuntimeError(f"YAML parsing error in {path}: {exc}") from exc
        except OSError as exc:
            raise RuntimeError(f"Cannot read YAML file {path}: {exc}") from exc

    return prototype_ids_by_type, references, format_issues


def generated_prototype_keys(
    prototype_ids_by_type: dict[str, set[str]],
    rules: dict[str, dict[str, object]],
) -> tuple[set[str], set[str]]:
    """Return generated keys and key-family prefixes described in the rules."""
    generated_keys: set[str] = set()
    generated_prefixes: set[str] = set()

    for prototype_type, prototype_ids in prototype_ids_by_type.items():
        rule = rules.get(prototype_type)
        if rule is None:
            continue

        generated_rules = rule["generated_keys"]
        assert isinstance(generated_rules, list)
        for generated_rule in generated_rules:
            template = generated_rule["template"]
            transforms = generated_rule["id_transforms"]
            suffixes = generated_rule["suffixes"]
            assert isinstance(template, str)
            assert isinstance(transforms, list)
            assert isinstance(suffixes, list)

            prefix = template.split("{id}", maxsplit=1)[0]
            if prefix:
                generated_prefixes.add(prefix)

            for prototype_id in prototype_ids:
                for transform in transforms:
                    suffix = transform_prototype_id(prototype_id, transform)
                    base_key = template.replace("{id}", suffix)
                    generated_keys.update(base_key + ending for ending in suffixes)

    return generated_keys, generated_prefixes


def find_unused_keys(
    messages: dict[str, FtlMessage],
    prototype_ids_by_type: dict[str, set[str]],
    references: set[str],
    rules: dict[str, dict[str, object]],
) -> list[str]:
    """Return FTL message keys not referenced or generated by a prototype."""
    used = set(references)
    generated_keys, generated_prefixes = generated_prototype_keys(
        prototype_ids_by_type,
        rules,
    )
    used.update(generated_keys)

    for key in messages:
        if any(key.startswith(prefix) for prefix in generated_prefixes):
            if key not in generated_keys:
                used.discard(key)

    return sorted(set(messages) - used)


def main() -> int:
    args = parse_args()
    root = args.root.resolve()
    locale_dir = (root / args.locale_dir).resolve()
    prototypes_dir = (root / args.prototypes_dir).resolve()
    rules_path = args.rules_file or Path(__file__).resolve().with_name(
        "prototype_ftl_rules.yml"
    )
    if not rules_path.is_absolute():
        rules_path = root / rules_path

    if not locale_dir.is_dir():
        print(f"ERROR: FTL directory not found: {locale_dir}", file=sys.stderr)
        return 2
    if not prototypes_dir.is_dir():
        print(f"ERROR: prototypes directory not found: {prototypes_dir}", file=sys.stderr)
        return 2

    localized_fields = (
        set()
        if args.no_default_fields
        else {normalize_field(field) for field in LOCALIZED_FIELDS}
    )
    localized_fields.update(normalize_field(field) for field in args.field)

    try:
        rules = load_prototype_rules(rules_path)
        for rule in rules.values():
            field_patterns = rule["field_patterns"]
            assert isinstance(field_patterns, dict)
            localized_fields.update(field_patterns)

        source_dirs = args.source_dir or [
            root / directory for directory in DEFAULT_SOURCE_DIRS
        ]
        source_dirs = [
            directory if directory.is_absolute() else root / directory
            for directory in source_dirs
        ]

        print("Reading FTL messages...")
        messages, ftl_references = read_ftl_messages(locale_dir)
        print("Scanning prototype YAML...")
        prototype_ids_by_type, yaml_references, format_issues = scan_prototypes(
            prototypes_dir,
            localized_fields,
            rules,
        )
        print("Scanning C# localization references...")
        csharp_references = scan_csharp_references(source_dirs)
    except RuntimeError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2

    unused_keys = find_unused_keys(
        messages,
        prototype_ids_by_type,
        yaml_references | ftl_references | csharp_references,
        rules,
    )
    unknown_types = sorted(set(prototype_ids_by_type) - set(rules))

    if args.show_used:
        used_count = len(set(messages) - set(unused_keys))
        print()
        print("Scan statistics:")
        print(f"  FTL messages: {len(messages)}")
        print(f"  Prototype types: {len(prototype_ids_by_type)}")
        print(f"  Configured prototype types: {len(rules)}")
        print(f"  Unsupported prototype types: {len(unknown_types)}")
        print(f"  YAML references: {len(yaml_references)}")
        print(f"  FTL references: {len(ftl_references)}")
        print(f"  C# references: {len(csharp_references)}")
        print(f"  Used FTL messages: {used_count}")
        print(f"  Unused FTL messages: {len(unused_keys)}")
        print()

    if unused_keys:
        print(f"Found {len(unused_keys)} potentially unused FTL messages:")
        for key in unused_keys:
            message = messages[key]
            try:
                relative_path = message.path.relative_to(root)
            except ValueError:
                relative_path = message.path
            print(f"{relative_path}:{message.line}: {key}")
    else:
        print("No unused prototype localization messages found.")

    if format_issues:
        print(f"Found {len(format_issues)} prototype localization format issues:")
        for issue in format_issues:
            try:
                relative_path = issue.path.relative_to(root)
            except ValueError:
                relative_path = issue.path
            print(
                f"{relative_path}:{issue.line}: type {issue.prototype_type} "
                f"id {issue.prototype_id}, field {issue.field}: {issue.value!r} "
                f"does not match {issue.pattern!r}"
            )
    else:
        print("No prototype localization format issues found.")

    if unknown_types:
        print(f"Unsupported prototype types (type-specific checks skipped): {len(unknown_types)}")
        for prototype_type in unknown_types:
            print(f"  {prototype_type}")

    return 1 if unused_keys or format_issues else 0


if __name__ == "__main__":
    sys.exit(main())
