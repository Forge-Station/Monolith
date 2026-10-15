import tempfile
import unittest
from pathlib import Path
from instrument_headless_client import instrument


class HeadlessClientInstrumentationTests(unittest.TestCase):
    def test_preserves_windowed_preload_and_is_idempotent(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / 'Robust.Client/GameController/GameController.cs'
            path.parent.mkdir(parents=True)
            path.write_text('            _loadscr.LoadingStep(_resourceCache.PreloadTextures, "Texture preload");\n_resourceCache.AfterDeserialization();\n')
            interface = root / 'Robust.Client/ResourceManagement/IResourceCacheInternal.cs'
            interface.parent.mkdir(parents=True)
            interface.write_text('void AfterDeserialization();')
            cache = interface.with_name('ResourceCache.Preload.cs')
            cache.write_text('public void AfterDeserialization() { try { foreach (var sprite in _toDeserialize) {} } finally { _toDeserialize.Clear(); } }')
            first = instrument(root)
            self.assertIn('if (_displayMode != DisplayMode.Headless)', path.read_text())
            self.assertIn('_resourceCache.PreloadTextures', path.read_text())
            self.assertIn('if (_displayMode == DisplayMode.Headless) _stringSerializer.EnableCaching = false;', path.read_text())
            self.assertFalse(first['headlessStringCaching'])
            self.assertLess(path.read_text().index('_stringSerializer.EnableCaching = false'),path.read_text().index('_loadscr.LoadingStep'))
            self.assertIn('finally { _toDeserialize.Clear(); }', cache.read_text())
            self.assertEqual(first, instrument(root))

    def test_unknown_source_fails(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / 'Robust.Client/GameController/GameController.cs'
            path.parent.mkdir(parents=True)
            path.write_text('changed engine')
            with self.assertRaisesRegex(ValueError, 'Unexpected headless'):
                instrument(root)


if __name__ == '__main__':
    unittest.main()
