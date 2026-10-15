"""Avoid graphical asset preloading in isolated headless network probes."""
import argparse
import hashlib
import json
from pathlib import Path


def instrument(engine):
    engine = engine.resolve()
    production = Path(__file__).resolve().parents[2] / "RobustToolbox"
    if engine == production.resolve():
        raise ValueError("Refusing to change the production engine checkout")
    path = engine / "Robust.Client/GameController/GameController.cs"
    original = path.read_text(encoding="utf-8-sig")
    anchor = '            _loadscr.LoadingStep(_resourceCache.PreloadTextures, "Texture preload");'
    previous = ('            if (_displayMode != DisplayMode.Headless)\n' + '    ' + anchor)
    replacement = ('            _loadscr.LoadingStep(() =>\n'
                   '            {\n'
                   '                if (_displayMode != DisplayMode.Headless) _resourceCache.PreloadTextures();\n'
                   '            }, "Texture preload");')
    if replacement not in original:
        if previous in original:
            original = original.replace(previous, replacement)
        elif original.count(anchor) == 1:
            original = original.replace(anchor, replacement)
        else:
            raise ValueError("Unexpected headless client source; cannot instrument safely")
        path.write_text(original, encoding="utf-8")
    call = '_resourceCache.AfterDeserialization();'
    deferred = '_resourceCache.AfterDeserialization(_displayMode != DisplayMode.Headless);'
    controller = path.read_text(encoding="utf-8")
    cache_guard = '            if (_displayMode == DisplayMode.Headless) _stringSerializer.EnableCaching = false;\n'
    if cache_guard not in controller:
        if controller.count(replacement) != 1:
            raise ValueError("Unexpected headless startup; cannot isolate concurrent string-cache writers")
        controller = controller.replace(replacement, cache_guard + replacement)
        path.write_text(controller, encoding="utf-8")
    if deferred not in controller:
        if controller.count(call) != 1:
            raise ValueError("Unexpected prototype graphics initialization")
        path.write_text(controller.replace(call, deferred), encoding="utf-8")
    interface = engine / "Robust.Client/ResourceManagement/IResourceCacheInternal.cs"
    cache = engine / "Robust.Client/ResourceManagement/ResourceCache.Preload.cs"
    for target, old, new in (
        (interface, 'void AfterDeserialization();', 'void AfterDeserialization(bool preloadGraphics = true);'),
        (cache, 'public void AfterDeserialization()', 'public void AfterDeserialization(bool preloadGraphics = true)'),
        (cache, 'foreach (var sprite in _toDeserialize)', 'if (!preloadGraphics) return;\n                foreach (var sprite in _toDeserialize)'),
    ):
        code = target.read_text(encoding="utf-8-sig")
        if new not in code:
            if code.count(old) != 1:
                raise ValueError("Unexpected prototype graphics cache source")
            target.write_text(code.replace(old, new), encoding="utf-8")
    return dict(scope="Isolated headless clients defer prototype graphics and use uncached verified mapped-string handshakes; windowed behavior unchanged",
                headlessStringCaching=False,
                files={str(target.relative_to(engine)): hashlib.sha256(target.read_bytes()).hexdigest()
                       for target in (path, interface, cache)})


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("engine", type=Path)
    print(json.dumps(instrument(parser.parse_args().engine)))
