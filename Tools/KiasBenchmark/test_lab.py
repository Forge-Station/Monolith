import unittest

from lab import XorShift32, canonical, digest, inventory, scenario
from prepare_pair import vanilla


class ScenarioTests(unittest.TestCase):
    def test_prng_known_vector(self):
        rng = XorShift32(1)
        self.assertEqual([rng.next() for _ in range(5)],
                         [270369, 67634689, 2647435461, 307599695, 2398689233])

    def test_schedule_is_repeatable_and_changes_with_seed(self):
        first, state = scenario(20261009, 40)
        second, second_state = scenario(20261009, 40)
        self.assertEqual(canonical(first), canonical(second))
        self.assertEqual(state, second_state)
        third, _ = scenario(20261010, 40)
        self.assertNotEqual(digest(canonical(first)), digest(canonical(third)))

    def test_recovery_covers_every_stimulus_and_ends_before_window(self):
        events, _ = scenario(20261009, 40)
        by_id = {row['id']: row for row in events}
        self.assertEqual(len(by_id), len(events))
        self.assertEqual(events, sorted(events, key=lambda row: (row['tick'], row['id'])))
        starts = {row['id'] for row in events if 'relatesTo' not in row}
        restored = set()
        for event in events:
            self.assertLess(event['tick'], 72000)
            self.assertGreaterEqual(event['tick'], 7200)
            if 'relatesTo' not in event:
                continue
            start = by_id[event['relatesTo']]
            self.assertEqual(event['ship'], start['ship'])
            self.assertEqual(event['tick'], start['tick'] + start['ttlTicks'])
            self.assertNotIn(start['id'], restored)
            restored.add(start['id'])
        self.assertEqual(starts, restored)

    def test_no_simultaneous_danger_on_same_ship(self):
        events, _ = scenario(20261009, 40)
        active = set()
        for event in events:
            if 'relatesTo' in event:
                self.assertIn(event['ship'], active)
                active.remove(event['ship'])
            else:
                self.assertNotIn(event['ship'], active)
                active.add(event['ship'])
        self.assertFalse(active)

    def test_invalid_parameters_are_rejected(self):
        for seed, ships in [(0, 40), (-1, 40), (2**32, 40), (1, 0), (1, 41)]:
            with self.assertRaises(ValueError):
                scenario(seed, ships)

    def test_real_map_inventory_retains_robust_tags(self):
        data, rows = inventory()
        self.assertEqual(len(rows), data['meta']['entityCount'])
        self.assertIn('robustYamlTag', canonical(rows).decode('utf-8'))

    def test_vanilla_map_removes_nested_items_without_orphan_parents(self):
        source = '''meta: {entityCount: 5}
entities:
- proto: ''
  entities:
  - uid: 1
    components: [{type: Transform, parent: invalid}]
- proto: KiasJammer
  entities:
  - uid: 2
    components: [{type: Transform, parent: 1}]
- proto: PowerCellHigh
  entities:
  - uid: 3
    components: [{type: Transform, parent: 2}]
  - uid: 4
    components: [{type: Transform, parent: 1}]
- proto: ItemNestedInBattery
  entities:
  - uid: 5
    components: [{type: Transform, parent: 3}]
'''
        cleaned, removed = vanilla(source)
        self.assertEqual(removed, {2, 3, 5})
        self.assertEqual({e['uid'] for g in cleaned['entities'] for e in g['entities']}, {1, 4})
        self.assertEqual(cleaned['meta']['entityCount'], 2)


if __name__ == '__main__':
    unittest.main()
