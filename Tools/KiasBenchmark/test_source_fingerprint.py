import hashlib
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import source_fingerprint


class SourceFingerprintTests(unittest.TestCase):
    def test_captures_runtime_assets_and_ignores_review_outputs(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            files = {
                'Run_KIAS_Tests.bat': 'launcher',
                'Resources/Maps/_Forge/KIAS/Fixtures/briarKIAS.yml': 'map',
                'Resources/Locale/ru-RU/_Forge/kias.ftl': 'translation',
                'Content.Client/_Forge/KIAS/KiasWindow.xaml': 'layout',
                'Resources/Textures/_Forge/KIAS/example.rsi/frame.png': 'pixels',
                'Content.Shared/NewNative.cs': 'untracked source',
                'KIAS_REVIEW/results.json': 'review evidence',
            }
            for relative, value in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(value, encoding='utf8')

            def git(args, **kwargs):
                if 'ls-files' in args:
                    return b'Content.Shared/NewNative.cs\0KIAS_REVIEW/results.json\0'
                if 'rev-parse' in args:
                    return 'engine' if 'RobustToolbox' in str(args) else 'head'
                return b''

            with patch.object(source_fingerprint, 'ROOT', root), patch.object(source_fingerprint.subprocess, 'check_output', git):
                initial = source_fingerprint.capture()
                self.assertNotIn('KIAS_REVIEW/results.json', initial['files'])
                for relative in files:
                    if not relative.startswith('KIAS_REVIEW/'):
                        self.assertEqual(initial['files'][relative], hashlib.sha256((root/relative).read_bytes()).hexdigest())
                (root/'KIAS_REVIEW/results.json').write_text('new review result', encoding='utf8')
                self.assertEqual(source_fingerprint.capture()['sha256'], initial['sha256'])
                (root/'Content.Client/_Forge/KIAS/KiasWindow.xaml').write_text('changed layout', encoding='utf8')
                self.assertNotEqual(source_fingerprint.capture()['sha256'], initial['sha256'])
