"""Writes the planetside cargo hub maps; --check fails when a written map is stale.

    python Tools/_WF/PlanetCargoHubs/hubs.py            write the maps
    python Tools/_WF/PlanetCargoHubs/hubs.py --check    fail when a written map differs

Each hub faces south: an apron with the road socket, two or three landing berths side by side, a loading apron with
sell pallets and a pallet console at the head of each berth, and a roofed warehouse and office across the north.
Content.Server/_WF/PlanetCargoHubs/README.md has the layout rules the tests hold the maps to.
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools" / "_WF" / "PlanetPois"))
from map_author import PoiMap  # noqa: E402

OUT = ROOT / "Resources" / "Maps" / "_WF" / "PlanetCargoHubs"

BERTH_W, BERTH_D = 24, 36
APRON_S = 2            # rows below the berths, with the road socket
HEAD = 6               # loading apron rows above the berths
BUILDING = 14          # warehouse and office depth
WALL = "WallConcrete"
NUMBERS = ["CMSemioticOne", "CMSemioticTwo", "CMSemioticThree"]


def hub(slug: str, berths: int, gap: int, chute: str, vendor: str) -> tuple[PoiMap, dict]:
    width = 2 + berths * BERTH_W + (berths - 1) * gap
    height = APRON_S + BERTH_D + HEAD + BUILDING
    m = PoiMap(slug, width, height, "", "", ground="RMCPlanetHybrisaCement")
    pad_top = APRON_S + BERTH_D            # first apron row above the pads
    base = pad_top + HEAD                  # the building's south wall
    xs = [1 + k * (BERTH_W + gap) for k in range(berths)]

    # Berths: an asphalt pad edged in warning lines, a floodlight at each corner and halfway, a number and a landing
    # zone sign on pillars at its head, and a marker the tests and the system read the pad from.
    for k, x0 in enumerate(xs):
        m.rect(x0, APRON_S, BERTH_W, BERTH_D, "RMCFloorAsphalt")
        m.entity("WFCargoHubBerth", x0, APRON_S)
        for x in range(x0, x0 + BERTH_W):
            m.decals.append(("WarnLineS", x - 0, APRON_S, 0))
            m.decals.append(("WarnLineN", x, pad_top - 1, 0))
        for y in range(APRON_S, pad_top):
            m.decals.append(("WarnLineW", x0, y, 0))
            m.decals.append(("WarnLineE", x0 + BERTH_W - 1, y, 0))
        for y in (APRON_S, APRON_S + BERTH_D // 2, pad_top - 1):
            m.entity("RMCSmallFloodlight", x0 - 1, y)
            m.entity("RMCSmallFloodlight", x0 + BERTH_W, y)
        # Blinking strobes beside the floodlights at the berth's open end, where ships come in.
        m.entity("WFCargoHubBerthStrobe", x0 - 1, APRON_S + 1)
        m.entity("WFCargoHubBerthStrobe", x0 + BERTH_W, APRON_S + 1)
        m.entity(WALL, x0 + 1, pad_top)
        m.entity(NUMBERS[k], x0 + 1, pad_top)
        m.entity(WALL, x0 + BERTH_W - 2, pad_top)
        m.entity("CMSemioticLandingzone", x0 + BERTH_W - 2, pad_top)

        # Loading bay at the berth's head: six sell pallets and the high-market console that sells them.
        for px in range(x0 + 10, x0 + 13):
            for py in (pad_top + 1, pad_top + 2):
                m.entity("CargoPalletSell", px, py)
        m.entity("ComputerPalletConsoleNFHighMarket", x0 + 14, pad_top + 3)
        m.decals.append(("LoadingArea", x0 + 11, pad_top + 3, 0))

    # The building: concrete walls, a warehouse with a power room in its corner, and an office with the depot's vendor
    # and turn-in chute. Doors open onto the loading apron at each berth and at the office.
    top = height - 1
    office = width - 15
    m.rect(0, base, width, BUILDING, "FloorSteel")
    m.rect(office + 1, base + 1, width - office - 2, BUILDING - 2, "FloorWood")
    m.roof(0, base, width, BUILDING)
    doors = {(x0 + 11, base) for x0 in xs} | {(x0 + 12, base) for x0 in xs} | {(width - 8, base), (office, base + 6)}
    for x in range(width):
        for y in range(base, height):
            edge = x in (0, width - 1) or y in (base, top)
            if not (edge or x == office):
                continue
            m.entity("MetalDoor" if (x, y) in doors else WALL, x, y)

    power_x, power_y = 6, base + 7
    for y in range(power_y, top):
        m.entity(WALL, power_x, y)
    for x in range(1, power_x):
        m.entity("MetalDoor" if x == 3 else WALL, x, power_y)

    # Power: RTG -> HV -> substation -> MV -> APC -> LV along the south wall, out to each console.
    rtg, sub, apc = (2, top - 2), (4, top - 2), (4, top - 4)
    m.entity("GeneratorRTG", *rtg)
    m.entity("SubstationBasic", *sub)
    m.entity("APCBasic", *apc)
    for x in range(rtg[0], sub[0] + 1):
        m.entity("CableHV", x, rtg[1])
    for y in range(apc[1], sub[1] + 1):
        m.entity("CableMV", sub[0], y)
    lv = {(apc[0], y) for y in range(base + 1, apc[1] + 1)}
    lv |= {(x, base + 1) for x in range(1, width - 1)}
    for x0 in xs:
        lv |= {(x0 + 14, y) for y in range(pad_top + 3, base + 1)}
    for x, y in sorted(lv):
        m.entity("CableApcExtension", x, y)

    m.entity(vendor, width - 4, base + 1)
    m.entity(chute, width - 6, base + 1)
    m.entity("SignCargo", width - 10, base)
    m.entity("TableReinforced", width - 5, base + 5)
    m.entity("TableReinforced", width - 4, base + 5)
    m.entity("ComfyChair", width - 4, base + 6)
    m.entity("WaterCooler", width - 2, top - 1)
    m.entity("PottedPlantRandom", office + 1, top - 1)
    for x in range(office + 3, width - 3, 4):
        m.entity("AlwaysPoweredLightLED", x, top - 2)

    for y in (base + 4, base + 9):
        for x in range(power_x + 3, office - 2):
            if (x - power_x) % 7:
                m.entity("Rack", x, y)
    for x in (power_x + 3, office - 4):
        m.entity("CrateGenericSteel", x, base + 7)
    for x in range(power_x + 4, office - 1, 8):
        m.entity("AlwaysPoweredLightLED", x, top - 2)

    socket_x = xs[0] + BERTH_W + gap // 2
    m.socket(socket_x, 0, "South")
    # The depot's beacon mast stands between the first two berths' heads, over the loading apron.
    m.entity("WFCargoHubBeacon", socket_x, pad_top + 2)
    ox, oy = width // 2, height // 2
    entry = {"footprint": f"{-ox},{-oy},{width - ox},{height - oy}", "socket": f"{socket_x - ox},{-oy}"}
    return m, entry


def maps():
    return [
        hub("cargo_hub_twin", 2, 4, "CargoChuteDepot", "VendingMachineTraderDepot"),
        hub("cargo_hub_triple", 3, 3, "CargoChuteDepotAlt", "VendingMachineTraderDepotAlt"),
    ]


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    stale = []
    for m, entry in maps():
        path = OUT / f"{m.slug}.yml"
        text = m.serialize()
        if args.check:
            if not path.exists() or path.read_text(encoding="utf-8") != text:
                stale.append(path)
            continue
        OUT.mkdir(parents=True, exist_ok=True)
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write(text)
        print(f"{path.relative_to(ROOT)}: footprint {entry['footprint']}, socket {entry['socket']} South")
    if stale:
        print("Stale hub maps: " + ", ".join(str(p.relative_to(ROOT)) for p in stale))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
