"""Render exported server room geometry without changing the source map."""
import argparse
import json
from pathlib import Path
from PIL import Image, ImageDraw


def render(source: Path):
    data = json.loads(source.read_text(encoding="utf-8-sig"))
    rooms = {room["Id"]: room for room in data["rooms"]}
    cells = [cell for room in rooms.values() for cell in room["tiles"] + room["doors"]]
    cells += [cell for scanner in data["scanners"] for cell in scanner.get("cells", [])]
    geometry = data.get("geometry", {})
    cells += geometry.get("floors", [])
    xmin, xmax = min(c[0] for c in cells) - 2, max(c[0] for c in cells) + 2
    ymin, ymax = min(c[1] for c in cells) - 2, max(c[1] for c in cells) + 2
    scale = 16
    output = source.parent / "previews"
    output.mkdir(exist_ok=True)
    recommendations = ["# Briar: рекомендации по размещению", "",
                       "Авторская карта не изменялась. Геометрия взята из серверного экспорта.", ""]
    for scanner in data["scanners"]:
        image = Image.new("RGB", ((xmax - xmin + 1) * scale, (ymax - ymin + 1) * scale + 55), "#101820")
        draw = ImageDraw.Draw(image)

        def cell(position, color):
            x, y = position
            left, top = (x - xmin) * scale, (ymax - y) * scale + 55
            draw.rectangle((left + 1, top + 1, left + scale - 1, top + scale - 1), fill=color)

        for tile in geometry.get("floors", []):
            cell(tile, "#303b46")
        for tile in geometry.get("walls", []):
            cell(tile, "#88939e")
        for tile in geometry.get("doors", []):
            cell(tile, "#d89032")
        for room in rooms.values():
            for tile in room["tiles"]:
                cell(tile, "#303b46")
        room = rooms.get(scanner["roomId"])
        if room or scanner.get("cells"):
            for tile in scanner.get("cells", room["tiles"] if room else []):
                cell(tile, "#168ca3")
            for tile in scanner.get("doors", room["doors"] if room else []):
                cell(tile, "#d89032")
        cell((scanner["x"], scanner["y"]), "#ef4a58")
        draw.text((8, 8), f"Scanner {scanner['uid']} ({scanner['x']}, {scanner['y']}) rotation={scanner['rotation']}", fill="white")
        draw.text((8, 25), f"{scanner['status']}; room={scanner['roomId']}; tiles={scanner['tileCount']}; doors={scanner['doorCount']}", fill="white")
        image.save(output / f"scanner-{scanner['uid']}.png")
        if scanner["status"] == "NoInteriorSeed":
            recommendations.append(f"- UID {scanner['uid']}, ({scanner['x']}, {scanner['y']}), {scanner['rotation']}°: нет интерьерного seed. Проверить сторону стены и ориентацию; покрытие отключено.")
        elif scanner["status"] == "OpenToSpace":
            recommendations.append(f"- UID {scanner['uid']}, ({scanner['x']}, {scanner['y']}): область открыта в космос. Fallback: достижимые клетки грида в радиусе 7, без прохода через двери.")
    uncovered = [room["Id"] for room in rooms.values() if not room["scanners"]]
    recommendations += ["", f"Области без сканеров: {uncovered}. В список входят малые изолированные области; требуется оценка по превью."]
    (source.parent / "BriarPlacementRecommendations.md").write_text("\n".join(recommendations) + "\n", encoding="utf-8")
    return len(data["scanners"])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    print(f"Rendered {render(parser.parse_args().source)} scanner previews")
