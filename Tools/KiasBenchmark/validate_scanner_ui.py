"""Validate actual network scanner UI captures against the server geometry."""
import argparse
import json
import struct
from pathlib import Path


def validate(directory):
    def read(name):
        return json.loads((directory / name).read_text(encoding="utf-8-sig"))
    assert read("client-completed.json")["realDesktopRenderer"] is True
    assert read("server-completed.json")["cases"] == 4
    rows = []
    for index in range(4):
        server = read(f"server-case-{index}.json")
        client = read(f"client-case-{index}.json")
        assert server["powered"] is True
        assert all(client[key] is True for key in ("actualNetworkBui", "actualWorldOverlay", "hiddenRangeEditor", "readableRussianText"))
        assert "Покрытие:" in client["details"] and "Рџ" not in client["details"]
        expected, actual = server["geometry"], client["geometry"]
        for key in ("Target", "Grid", "Shape", "Radius", "Revision", "RoomStatus", "TileCount", "DoorCount", "Cells", "BoundaryCells", "PreviewTruncated"):
            assert expected[key] == actual[key], f"Scanner {index}: server/client {key} differs"
        assert not actual["PreviewTruncated"]
        assert len(actual["Cells"]) == actual["TileCount"]
        assert len(actual["BoundaryCells"]) == actual["DoorCount"]
        if index == 0:
            assert actual["RoomStatus"] == "Ok" and actual["TileCount"] == 9 and actual["DoorCount"] == 4
        if index >= 2:
            assert actual["RoomStatus"] == "ExteriorSector" and actual["TileCount"] > 0
        png = (directory / f"scanner-{index}.png").read_bytes()
        assert png[:8] == b"\x89PNG\r\n\x1a\n" and struct.unpack(">II", png[16:24]) == (1600, 900)
        rows.append(dict(index=index, position=server["position"], roomStatus=actual["RoomStatus"], tiles=actual["TileCount"], doors=actual["DoorCount"]))
    return dict(status="PASS", cases=rows, scope="Actual desktop rendering and network BUI; screenshot appearance requires visual review")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", type=Path)
    options = parser.parse_args()
    result = validate(options.directory)
    (options.directory / "scanner-ui-proof.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps(result))
