#!/usr/bin/env python3
"""Focused tests for the C# localization key checker."""

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).with_name("check_c#_ftl.py")
SPEC = importlib.util.spec_from_file_location("check_csharp_ftl", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Cannot load checker from {SCRIPT_PATH}")
checker = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = checker
SPEC.loader.exec_module(checker)


class CSharpFtlCheckerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        self.locale_dir = self.root / "locale"
        self.source_dir = self.root / "source"
        self.locale_dir.mkdir()
        self.source_dir.mkdir()

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def test_static_calls_constants_and_line_numbers(self) -> None:
        (self.locale_dir / "en-US").mkdir()
        (self.locale_dir / "ru-RU").mkdir()
        (self.locale_dir / "en-US" / "strings.ftl").write_text(
            "translated-key = Value\n"
            "attribute-owner = Value\n"
            "    .desc = Description\n",
            encoding="utf-8",
        )
        (self.locale_dir / "ru-RU" / "strings.ftl").write_text(
            "second-locale-key = Значение\n",
            encoding="utf-8",
        )
        engine_locale_dir = self.root / "engine-locale" / "en-US"
        engine_locale_dir.mkdir(parents=True)
        (engine_locale_dir / "engine.ftl").write_text(
            "engine-key = Engine value\n",
            encoding="utf-8",
        )

        source = self.source_dir / "Example.cs"
        source.write_text(
            'const string ConstantKey = "constant-key";\n'
            'Loc.GetString("translated-key");\n'
            'Loc.GetString("engine-key");\n'
            'Loc.TryGetString(@"second-locale-key", out var value);\n'
            "Loc.GetString(ConstantKey);\n"
            'Loc.GetString("missing-key");\n'
            'Loc.GetString("attribute-owner.desc");\n',
            encoding="utf-8",
        )

        keys = checker.read_ftl_keys([self.locale_dir, engine_locale_dir.parent])
        references = checker.scan_csharp_references([self.source_dir])
        missing = checker.find_missing_references(references, keys)

        self.assertEqual(
            [(reference.line, reference.key) for reference in missing],
            [
                (5, "constant-key"),
                (6, "missing-key"),
                (7, "attribute-owner.desc"),
            ],
        )

    def test_comments_and_dynamic_lookups_are_not_reported_as_static(self) -> None:
        (self.locale_dir / "strings.ftl").write_text(
            "real-key = Value\n",
            encoding="utf-8",
        )
        (self.source_dir / "Example.cs").write_text(
            '// Loc.GetString("commented-key");\n'
            '/* Loc.GetString("also-commented"); */\n'
            'var url = "https://example.test/a";\n'
            'Loc.GetString($"dynamic-{suffix}");\n'
            'Loc.GetString("prefix-" + dynamicKey);\n'
            "Loc.GetString(dynamicKey);\n"
            'Loc.GetString("real-key");\n',
            encoding="utf-8",
        )

        references = checker.scan_csharp_references([self.source_dir])
        self.assertEqual([reference.key for reference in references], ["real-key"])
        self.assertEqual(
            checker.find_missing_references(
                references,
                checker.read_ftl_keys([self.locale_dir]),
            ),
            [],
        )

    def test_unused_ftl_attributes_do_not_create_independent_keys(self) -> None:
        (self.locale_dir / "strings.ftl").write_text(
            "only-message = Value\n"
            "    .desc = Attribute\n",
            encoding="utf-8",
        )
        (self.source_dir / "Example.cs").write_text(
            'Loc.GetString("only-message.desc");\n',
            encoding="utf-8",
        )

        references = checker.scan_csharp_references([self.source_dir])
        missing = checker.find_missing_references(
            references,
            checker.read_ftl_keys([self.locale_dir]),
        )
        self.assertEqual([reference.key for reference in missing], ["only-message.desc"])


if __name__ == "__main__":
    unittest.main()
