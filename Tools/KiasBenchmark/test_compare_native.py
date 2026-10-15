import json
import tempfile
import unittest
from pathlib import Path

from compare_native import validate


class NativeTraceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name)
        self.write('started.json', {'ships': 40, 'tickRate': 60, 'measuredTicks': 10})
        self.write('completed.json', {'status': 'PARTIAL_NATIVE_DRY_RUN_COMPLETE', 'ships': 40, 'measuredTicks': 10, 'deliveredEvents': 8})
        self.write('bindings.json', [{'ship': f'ship-{index:04d}'} for index in range(1, 41)])
        (self.path / 'ticks.csv').write_text('relative_tick,content_engine_ms,allocated_bytes\n' + ''.join(f'{index},1,0\n' for index in range(10)))
        effects = [('fire.start', 'burning', True), ('fire.clear', 'burning', False),
                   ('power.loss', 'disabled', True), ('power.restore', 'disabled', False),
                   ('crew.critical', 'mobState', 'Critical'), ('crew.recover', 'mobState', 'Alive'),
                   ('light.power_off', 'on', False), ('light.power_on', 'on', True)]
        self.events = [{'id': str(index), 'type': kind, 'ship': 'ship-0001', 'scheduledTick': index, 'actualTick': index, 'nativeEffect': {field: value}}
                       for index, (kind, field, value) in enumerate(effects)]
        self.save_events()

    def write(self, name, value):
        (self.path / name).write_text(json.dumps(value), encoding='utf-8')

    def save_events(self):
        (self.path / 'events.jsonl').write_text('\n'.join(json.dumps(event) for event in self.events), encoding='utf-8')

    def test_complete_native_trace_is_accepted(self):
        self.assertEqual(len(validate(self.path)[2]), 8)

    def test_repeated_driver_cannot_replace_missing_driver(self):
        self.events[-1] = dict(self.events[0], id='7', scheduledTick=7, actualTick=7)
        self.save_events()
        with self.assertRaises(AssertionError):
            validate(self.path)

    def test_missing_tick_is_rejected(self):
        path = self.path / 'ticks.csv'
        path.write_text(path.read_text().replace('5,1,0\n', ''))
        with self.assertRaises(AssertionError):
            validate(self.path)

    def test_late_input_is_rejected(self):
        self.events[0]['actualTick'] = 1
        self.save_events()
        with self.assertRaises(AssertionError):
            validate(self.path)

    def test_unbound_ship_is_rejected(self):
        self.events[0]['ship'] = 'ship-9999'
        self.save_events()
        with self.assertRaises(AssertionError):
            validate(self.path)

    def test_failed_world_effect_is_rejected(self):
        self.events[0]['nativeEffect']['burning'] = False
        self.save_events()
        with self.assertRaises(AssertionError):
            validate(self.path)

    def test_server_load_error_invalidates_completed_trace(self):
        (self.path / 'server.log').write_text('[ERRO] entity_deserializer: unknown parent')
        with self.assertRaises(AssertionError):
            validate(self.path)

    def test_whole_tick_shorter_than_content_is_rejected(self):
        (self.path / 'ticks.csv').write_text('relative_tick,content_engine_ms,allocated_bytes,whole_tick_ms,whole_allocated_bytes,gc_pause_ms\n' + ''.join(f'{index},2,0,1,0,0\n' for index in range(10)))
        with self.assertRaises(AssertionError):
            validate(self.path)

    def test_declared_unimplemented_driver_is_rejected(self):
        start = json.loads((self.path / 'started.json').read_text())
        start['driverTypes'] = ['ftl.unimplemented']
        self.write('started.json', start)
        with self.assertRaises(AssertionError):
            validate(self.path)

    def test_anomaly_requires_flesh_and_component_audit(self):
        self.events = [dict(id='a', type='anomaly.start', ship='ship-0001', scheduledTick=0, actualTick=0,
                            nativeEffect=dict(active=True, prototype='AnomalyFlesh', excludedHazardsAbsent=True))]
        self.write('started.json', dict(ships=40, tickRate=60, measuredTicks=10, plannedEvents=1, driverTypes=['anomaly.start']))
        self.write('completed.json', dict(status='PARTIAL_NATIVE_DRY_RUN_COMPLETE', ships=40, measuredTicks=10, deliveredEvents=1))
        self.save_events()
        validate(self.path)
        self.events[0]['nativeEffect']['prototype'] = 'AnomalyGravity'
        self.save_events()
        with self.assertRaises(AssertionError):
            validate(self.path)
        self.events[0]['nativeEffect']['prototype'] = 'AnomalyFlesh'
        self.events[0]['nativeEffect']['excludedHazardsAbsent'] = False
        self.save_events()
        with self.assertRaises(AssertionError):
            validate(self.path)
