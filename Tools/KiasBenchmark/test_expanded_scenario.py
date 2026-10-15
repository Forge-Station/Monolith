import unittest
from expanded_scenario import PAIRS, generate


class ExpandedScenarioTests(unittest.TestCase):
    def test_seed_is_reproducible_and_changes_ship_choices(self):
        self.assertEqual(generate(1200), generate(1200))
        self.assertNotEqual(generate(1200), generate(1200, 20261010))

    def test_every_driver_has_a_bounded_cleanup_pair(self):
        for ticks in (1200, 72000):
            events = generate(ticks)
            self.assertEqual({event['type'] for event in events}, {kind for pair in PAIRS for kind in pair})
            for start, clear in zip(events[::2], events[1::2]):
                self.assertEqual(start['ship'], clear['ship'])
                self.assertEqual(clear['tick']-start['tick'], 120 if ticks == 72000 else 30)
                self.assertLess(clear['tick'], ticks)

    def test_anomalies_explicitly_use_short_lived_flesh(self):
        for ticks in (1200, 72000):
            events = generate(ticks)
            anomalies = [event for event in events if event['type'] == 'anomaly.start']
            self.assertTrue(anomalies)
            self.assertTrue(all(event.get('prototype') == 'AnomalyFlesh' for event in anomalies))
            for start, clear in zip(events[::2], events[1::2]):
                if start['type'] == 'anomaly.start':
                    self.assertLess(clear['tick'] - start['tick'], 180 * 60)

    def test_too_short_workload_is_rejected(self):
        with self.assertRaises(ValueError):
            generate(600)
