#!/usr/bin/env python3
"""Focused tests for the prototype FTL checker."""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

import check_prototype_ftl as checker


class PrototypeFtlCheckerTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp_dir = tempfile.TemporaryDirectory()
        self.root = Path(self.temp_dir.name)
        self.locale_dir = self.root / "locale"
        self.prototypes_dir = self.root / "prototypes"
        self.locale_dir.mkdir()
        self.prototypes_dir.mkdir()
        self.rules = checker.load_prototype_rules(
            Path(__file__).with_name("prototype_ftl_rules.yml")
        )

    def tearDown(self) -> None:
        self.temp_dir.cleanup()

    def write_ftl(self, content: str) -> None:
        (self.locale_dir / "test.ftl").write_text(content, encoding="utf-8")

    def write_yaml(self, content: str) -> None:
        (self.prototypes_dir / "test.yml").write_text(content, encoding="utf-8")

    def scan(self) -> list[str]:
        messages, ftl_references = checker.read_ftl_messages(self.locale_dir)
        prototype_ids_by_type, yaml_references, _ = checker.scan_prototypes(
            self.prototypes_dir,
            {checker.normalize_field(field) for field in checker.LOCALIZED_FIELDS},
            self.rules,
        )
        return checker.find_unused_keys(
            messages,
            prototype_ids_by_type,
            ftl_references | yaml_references,
            self.rules,
        )

    def test_entity_keys_match_explicit_entity_ids_and_attributes_are_not_keys(self) -> None:
        self.write_yaml(
            "- type: entity\n"
            "  id: ActionToggleSneakMode\n"
            "  name: entity-name-existing\n"
            "  description: \"{ missing-entity-reference }\"\n"
        )
        self.write_ftl(
            "ent-ActionToggleSneakMode = Existing entity\n"
            "    .desc = Entity description\n"
            "    .suffix = Entity suffix\n"
            "ent-MissingEntity = Missing entity\n"
            "    .desc = This attribute does not create another message\n"
            "ent-ReferencedButMissing = Missing entity\n"
            "missing-entity-reference = { ent-ReferencedButMissing }\n"
            "entity-name-existing = Existing name\n"
        )

        self.assertEqual(
            self.scan(),
            ["ent-MissingEntity", "ent-ReferencedButMissing"],
        )

    def test_construction_keys_cover_full_and_runtime_generated_suffixes(self) -> None:
        self.write_yaml(
            "- type: construction\n"
            "  id: LightSwitchDirectionalRecipe\n"
        )
        self.write_ftl(
            "construction-recipe-light-switch-directional-recipe = Legacy name\n"
            "construction-recipe-light-switch-directional = Runtime name\n"
            "construction-recipe-light-switch-directional-desc = Runtime description\n"
            "construction-recipe-no-such-recipe = Unused\n"
        )

        self.assertEqual(self.scan(), ["construction-recipe-no-such-recipe"])

    def test_nested_yaml_localization_and_ftl_references_count_as_usage(self) -> None:
        self.write_yaml(
            "- type: job\n"
            "  id: Bailiff\n"
            "  name: job-name-bailiff\n"
            "  description: \"{ message-referrer }\"\n"
            "  requirements:\n"
            "    - failureMessage: job-requirement-failed\n"
            "- type: company\n"
            "  id: ExampleCompany\n"
            "  note: example-company-note\n"
        )
        self.write_ftl(
            "job-name-bailiff = Bailiff\n"
            "job-requirement-failed = Requirement failed\n"
            "example-company-note = Company note\n"
            "message-used-from-ftl = Used by another message\n"
            "message-referrer = {\n"
            "    message-used-from-ftl\n"
            "}\n"
            "message-never-used = Never used\n"
        )

        self.assertEqual(self.scan(), ["message-never-used"])

    def test_static_csharp_localization_calls_count_but_comments_do_not(self) -> None:
        self.write_ftl(
            "admin-action-name = An action\n"
            "unused-csharp-key = Unused\n"
            "commented-out-key = Not used\n"
        )
        source_dir = self.root / "source"
        source_dir.mkdir()
        (source_dir / "Example.cs").write_text(
            'var name = Loc.GetString("admin-action-name");\n'
            '// Loc.GetString("commented-out-key");\n'
            '/* Loc.TryGetString("commented-out-key", out var value); */\n',
            encoding="utf-8",
        )

        messages, ftl_references = checker.read_ftl_messages(self.locale_dir)
        csharp_references = checker.scan_csharp_references([source_dir])
        self.assertEqual(csharp_references, {"admin-action-name"})
        self.assertEqual(
            checker.find_unused_keys(
                messages,
                {},
                ftl_references | csharp_references,
                self.rules,
            ),
            ["commented-out-key", "unused-csharp-key"],
        )

    def test_entity_parent_and_localization_id_do_not_create_entity_ids(self) -> None:
        self.write_yaml(
            "- type: entity\n"
            "  id: ChildEntity\n"
            "  parent: ParentEntity\n"
            "  localizationId: AlternateEntity\n"
        )
        self.write_ftl(
            "ent-ChildEntity = Child\n"
            "ent-ParentEntity = Parent\n"
            "ent-AlternateEntity = Alternate\n"
        )

        self.assertEqual(
            self.scan(),
            ["ent-AlternateEntity", "ent-ParentEntity"],
        )

    def test_type_specific_field_patterns_report_invalid_localization_ids(self) -> None:
        self.write_yaml(
            "- type: job\n"
            "  id: Bailiff\n"
            "  name: job-name-bailiff\n"
            "  description: invalid-description-key\n"
        )
        self.write_ftl(
            "job-name-bailiff = Bailiff\n"
            "invalid-description-key = Invalid format\n"
        )

        messages, ftl_references = checker.read_ftl_messages(self.locale_dir)
        prototype_ids, yaml_references, issues = checker.scan_prototypes(
            self.prototypes_dir,
            {checker.normalize_field(field) for field in checker.LOCALIZED_FIELDS},
            self.rules,
        )

        self.assertEqual(issues[0].prototype_type, "job")
        self.assertEqual(issues[0].prototype_id, "Bailiff")
        self.assertEqual(issues[0].field, "description")
        self.assertEqual(issues[0].line, 4)
        self.assertEqual(
            checker.find_unused_keys(
                messages,
                prototype_ids,
                ftl_references | yaml_references,
                self.rules,
            ),
            [],
        )

    def test_type_rules_accept_repository_localization_prefix_variants(self) -> None:
        self.write_yaml(
            "- type: job\n"
            "  id: Arbiter\n"
            "  name: forge-job-name-arbiter\n"
            "  description: forge-job-desc-arbiter\n"
            "- type: emote\n"
            "  id: HarpyHonk\n"
            "  name: delta-chat-emote-name-honk\n"
            "  chatMessages: [delta-chat-emote-msg-honk]\n"
        )
        self.write_ftl(
            "forge-job-name-arbiter = Arbiter\n"
            "forge-job-desc-arbiter = Description\n"
            "delta-chat-emote-name-honk = Honk\n"
            "delta-chat-emote-msg-honk = honks\n"
        )

        _, _, issues = checker.scan_prototypes(
            self.prototypes_dir,
            {checker.normalize_field(field) for field in checker.LOCALIZED_FIELDS},
            self.rules,
        )

        self.assertEqual(issues, [])

    def test_unknown_prototype_types_are_collected_and_generic_references_remain(self) -> None:
        self.write_yaml(
            "- type: company\n"
            "  id: ExampleCompany\n"
            "  note: example-company-note\n"
        )
        self.write_ftl(
            "example-company-note = Company note\n"
            "ent-ExampleCompany = Not inferred for unknown type\n"
        )

        messages, ftl_references = checker.read_ftl_messages(self.locale_dir)
        prototype_ids, yaml_references, issues = checker.scan_prototypes(
            self.prototypes_dir,
            {checker.normalize_field(field) for field in checker.LOCALIZED_FIELDS},
            self.rules,
        )

        self.assertEqual(set(prototype_ids), {"company"})
        self.assertEqual(issues, [])
        self.assertEqual(
            checker.find_unused_keys(
                messages,
                prototype_ids,
                ftl_references | yaml_references,
                self.rules,
            ),
            ["ent-ExampleCompany"],
        )

    def test_invalid_rules_file_reports_configuration_error(self) -> None:
        rules_path = self.root / "invalid-rules.yml"
        rules_path.write_text("prototype_types: [entity]\n", encoding="utf-8")

        with self.assertRaisesRegex(RuntimeError, "prototype_types.*mapping"):
            checker.load_prototype_rules(rules_path)


if __name__ == "__main__":
    unittest.main()
