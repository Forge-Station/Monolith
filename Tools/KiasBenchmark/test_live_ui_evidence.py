import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import lab


class LiveUiEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.output = self.root / 'output'
        self.run = self.output / 'live-ui' / 'run-1'
        self.run.mkdir(parents=True)
        self.sizes = ['850x500', '1200x720', '1600x900']
        for name in lab.LIVE_UI_SOURCES:
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('original')
        self.source = self.root / lab.LIVE_UI_SOURCES[-1]
        self.write('run-manifest.json', {'sources': {name: lab.digest(b'original') for name in lab.LIVE_UI_SOURCES}, 'scope': 'fixture'})
        steps = ['WRITE actual card', 'native discard', 'observe actual ejection',
                 'observe native reinsert', 'reject Bool to Signal', 'native zoomed connection']
        self.write('client-completed.json', {'status': 'PASS', 'realDesktopRenderer': True,
                   'actualServerBui': True, 'steps': [f'{size}: {step}' for size in self.sizes for step in steps]})
        self.write('server-writes.json', {'status': 'RECORDED', 'actualNetworkBui': True,
                   'writes': [{'actualCard': 'one-card', 'name': size.replace('x', '×'), 'number': .25,
                               'manualConnection': True, 'actualAdapterBinding': 'real-adapter',
                               'inferredAudioChannel': 2} for size in self.sizes]})
        for size in self.sizes:
            (self.run / f'live-{size}.png').write_bytes(b'capture-fixture')
        self.write('review.json', {'reviewedSizes': self.sizes,
                   'captureSha256': {size: lab.digest(b'capture-fixture') for size in self.sizes}})

    def write(self, name, data):
        (self.run / name).write_text(json.dumps(data), encoding='utf-8')

    def status(self):
        with patch.object(lab, 'ROOT', self.root), patch.object(lab, 'OUT', self.output):
            return lab.desktop_live_ui()['status']

    def test_complete_current_evidence_is_accepted(self):
        self.assertEqual(self.status(), 'PASS')

    def test_changed_source_is_rejected(self):
        self.source.write_text('changed')
        self.assertNotEqual(self.status(), 'PASS')

    def test_changed_capture_requires_new_review(self):
        (self.run / 'live-850x500.png').write_bytes(b'changed capture')
        self.assertNotEqual(self.status(), 'PASS')

    def test_missing_native_ejection_step_is_rejected(self):
        path = self.run / 'client-completed.json'
        data = json.loads(path.read_text())
        data['steps'].remove('850x500: observe actual ejection')
        self.write(path.name, data)
        self.assertNotEqual(self.status(), 'PASS')
