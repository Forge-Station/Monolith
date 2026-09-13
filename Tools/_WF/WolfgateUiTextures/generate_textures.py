"""Generates the Wolfgate UI textures for every skin under Resources/Textures/_WF/Interface/<skin>.

Run from the repository root:  python Tools/_WF/WolfgateUiTextures/generate_textures.py
Requires Pillow. Slot and storage icons are recoloured copies of the stock Default theme.
Palettes mirror Content.Client/_WF/Stylesheets/WolfgateSkin.cs; keep the two in sync.
The Anatomy top-bar icon is white and tinted by the menu button, so one copy under
Resources/Textures/_WF/Interface serves every skin.
"""
import math
import os
import shutil
from PIL import Image, ImageDraw

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
DEFAULT = os.path.join(ROOT, "Resources", "Textures", "Interface", "Default")
CLEAR = (0, 0, 0, 0)
WHITE = (255, 255, 255, 255)

# Pastel washed-out ROYGBIV for the retro skin's rainbow rules
RAINBOW = [
    (242, 139, 130), (245, 184, 122), (242, 226, 122), (159, 217, 138),
    (127, 209, 214), (143, 168, 240), (209, 154, 232),
]

SKINS = {
    # Hyper-futurist glass and cyan, chamfered corners
    "Wolfgate": dict(
        rounded=False,
        accent=(70, 215, 255, 255), accent_dim=(42, 143, 176, 255), danger=(255, 77, 94, 255),
        glass=(14, 20, 27, 250), glass_inner=(20, 29, 38, 255),
        edge=(42, 59, 77, 255), edge_soft=(35, 48, 62, 255), edge_dim=(28, 38, 47, 255),
        ink=(8, 12, 17, 245), field=(7, 11, 16, 235), header=(18, 27, 37, 252),
        lineedit_edge=(30, 42, 54, 255), tab_panel=(14, 20, 27, 240), search=(11, 17, 23, 255),
        inv_slot=(30, 42, 54, 243), inv_slot_edge=(20, 28, 36, 255),
        stripe_bg=(11, 17, 24, 235), stripe_fg=(17, 26, 36, 235),
        grabber=(27, 39, 51, 255), checkbox_bg=(11, 17, 23, 255), checkbox_edge=(53, 72, 91, 255),
        shade=(8, 12, 17), slot_bg=(14, 20, 27, 235),
        slot_dark=(58, 80, 102), slot_light=(122, 154, 182),
        storage_dark=(9, 13, 18), storage_light=(150, 172, 194),
    ),
    # Aphelion cassette futurism: amber on charcoal, aged plastic, rounded corners
    "WolfgateRetro": dict(
        rounded=True,
        accent=(242, 165, 74, 255), accent_dim=(168, 112, 46, 255), danger=(224, 100, 90, 255),
        glass=(28, 27, 24, 250), glass_inner=(38, 37, 32, 255),
        edge=(75, 71, 57, 255), edge_soft=(57, 54, 45, 255), edge_dim=(46, 44, 37, 255),
        ink=(20, 20, 18, 245), field=(17, 17, 15, 235), header=(36, 35, 30, 252),
        lineedit_edge=(62, 58, 48, 255), tab_panel=(28, 27, 24, 240), search=(22, 22, 19, 255),
        inv_slot=(48, 46, 39, 243), inv_slot_edge=(34, 32, 27, 255),
        stripe_bg=(24, 23, 20, 235), stripe_fg=(33, 32, 27, 235),
        grabber=(58, 55, 46, 255), checkbox_bg=(22, 22, 19, 255), checkbox_edge=(95, 89, 74, 255),
        shade=(10, 14, 13), slot_bg=(28, 27, 24, 235),
        slot_dark=(96, 86, 64), slot_light=(196, 180, 140),
        storage_dark=(20, 19, 16), storage_light=(206, 190, 150),
    ),
}

# Set per skin by main()
P = SKINS["Wolfgate"]
OUT = ""
STYLE = ""
written = []


def gray(v, a=255):
    return (v, v, v, a)


def inside(x, y, w, h, tl=0, tr=0, br=0, bl=0):
    """True when the pixel is inside a rectangle whose corners are cut by the given sizes: a straight chamfer for
    the futurist skin, a quarter circle for the retro skin."""
    if x < 0 or y < 0 or x >= w or y >= h:
        return False
    corners = (
        (tl, x, y),
        (tr, w - 1 - x, y),
        (br, w - 1 - x, h - 1 - y),
        (bl, x, h - 1 - y),
    )
    for c, dx, dy in corners:
        if c <= 0 or dx >= c or dy >= c:
            continue
        if P["rounded"]:
            ex = c - 0.5 - dx
            ey = c - 0.5 - dy
            if ex * ex + ey * ey > c * c:
                return False
        elif dx + dy < c:
            return False
    return True


def shape(w, h, fill, edge, tl=0, tr=0, br=0, bl=0, inner=None, top=None):
    """Cornered rectangle with a 1px edge, optional 1px inner ring and top highlight row."""
    im = Image.new("RGBA", (w, h), CLEAR)
    px = im.load()
    for y in range(h):
        for x in range(w):
            if not inside(x, y, w, h, tl, tr, br, bl):
                continue
            sides = [(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)]
            if any(not inside(nx, ny, w, h, tl, tr, br, bl) for nx, ny in sides):
                px[x, y] = edge
                continue
            ring = [(x + dx, y + dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)]
            if inner is not None and any(not inside(nx, ny, w, h, tl, tr, br, bl) for nx, ny in ring):
                px[x, y] = inner
                continue
            if top is not None and y == 1:
                px[x, y] = top
                continue
            px[x, y] = fill
    return im


def paint(im, fill, colour, xs, ys):
    """Paints the given colour over fill-coloured pixels inside the x/y ranges."""
    px = im.load()
    for y in ys:
        for x in xs:
            if px[x, y] == fill:
                px[x, y] = colour


def header(accent):
    """Window header: shaped top-left, outlined like the panel, accent bar on the left and line below."""
    im = shape(24, 24, P["header"], P["edge"], tl=7)
    paint(im, P["header"], accent, range(1, 4), range(24))
    paint(im, P["header"], accent, range(24), range(21, 23))
    paint(im, P["header"], P["accent_dim"], range(24), range(1, 2))
    return im


def save(im, *path):
    full = os.path.join(*path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    im.save(full)
    written.append(os.path.relpath(full, OUT).replace(os.sep, "/"))



def sex_icons():
    """Mars / Venus / neuter glyphs for the character creator's sex selector.

    Drawn pure white so the control can tint them; nothing else in the repo has a sex glyph.
    """
    size = 24

    def blank():
        return Image.new("RGBA", (size, size), CLEAR)

    def dot(px, x, y):
        if 0 <= x < size and 0 <= y < size:
            px[x, y] = WHITE

    def stamp(px, x, y):
        # 2px pen, so the glyph reads at 24px
        dot(px, x, y)
        dot(px, x + 1, y)
        dot(px, x, y + 1)
        dot(px, x + 1, y + 1)

    def ring(px, cx, cy, r):
        steps = 180
        for i in range(steps):
            a = 2 * 3.14159265 * i / steps
            stamp(px, int(round(cx + r * math.cos(a))),
                      int(round(cy + r * math.sin(a))))

    def line(px, x0, y0, x1, y1):
        steps = max(abs(x1 - x0), abs(y1 - y0)) * 4 + 1
        for i in range(steps + 1):
            t = i / steps
            stamp(px, int(round(x0 + (x1 - x0) * t)), int(round(y0 + (y1 - y0) * t)))

    # Male: circle low-left with an arrow to the upper right
    im = blank(); px = im.load()
    ring(px, 8, 15, 5)
    line(px, 12, 11, 19, 4)
    line(px, 19, 4, 14, 4)
    line(px, 19, 4, 19, 9)
    save(im, STYLE, "sex_male.png")

    # Female: circle up top with a cross below
    im = blank(); px = im.load()
    ring(px, 11, 8, 5)
    line(px, 11, 13, 11, 21)
    line(px, 7, 18, 15, 18)
    save(im, STYLE, "sex_female.png")

    # Unsexed: the neuter glyph, a circle with a plain stem
    im = blank(); px = im.load()
    ring(px, 11, 9, 5)
    line(px, 11, 14, 11, 21)
    save(im, STYLE, "sex_none.png")


def style_textures():
    accent = P["accent"]
    accent_dim = P["accent_dim"]

    # Window chrome. White textures are tinted by the stylesheet, coloured ones are used as-is.
    save(shape(32, 32, P["glass"], P["edge"], tl=7, br=7, inner=P["glass_inner"]), STYLE, "window_panel.png")
    save(shape(32, 32, gray(115), WHITE, tl=7, br=7), STYLE, "panel.png")
    save(shape(7, 7, P["glass"], P["edge_soft"]), STYLE, "panel_bordered.png")
    save(shape(7, 7, P["glass"][:3] + (245,), P["edge"]), STYLE, "menu_panel.png")
    save(header(accent), STYLE, "window_header.png")
    save(header(P["danger"]), STYLE, "window_header_alert.png")

    # Buttons
    save(shape(24, 24, gray(200), WHITE, tl=5, br=5, top=gray(222)), STYLE, "button.png")
    save(shape(52, 19, gray(200), WHITE, tl=4, br=4, top=gray(222)), STYLE, "button_small.png")
    save(shape(32, 32, gray(200), WHITE, tl=6, br=6, top=gray(222)), STYLE, "pill.png")
    save(shape(32, 32, gray(185), WHITE, tl=6, br=6), STYLE, "pill_bordered.png")

    # Fields and containers
    im = shape(9, 9, P["field"], P["lineedit_edge"])
    paint(im, P["field"], accent_dim, range(1, 8), range(7, 8))
    save(im, STYLE, "lineedit.png")
    save(shape(7, 7, P["tab_panel"], P["edge_soft"]), STYLE, "tab_panel.png")
    save(shape(7, 7, P["ink"], accent[:3] + (150,)), STYLE, "tooltip.png")
    save(shape(7, 7, P["search"], P["edge_dim"]), STYLE, "search_box.png")
    save(shape(5, 5, P["inv_slot"], P["inv_slot_edge"]), STYLE, "inv_slot_background.png")
    save(shape(5, 5, CLEAR, accent), STYLE, "hand_slot_highlight.png")

    # Heading underline with a left tick and a fading tail
    im = Image.new("RGBA", (16, 16), CLEAR)
    px = im.load()
    for x in range(16):
        a = 255 if x < 6 else int(255 - (x - 6) * 20)
        px[x, 14] = accent[:3] + (a,)
        px[x, 15] = accent_dim[:3] + (a,)
    for y in range(6, 16):
        px[0, y] = accent
        px[1, y] = accent
    save(im, STYLE, "heading.png")

    # Diagonal hatch tile
    im = Image.new("RGBA", (32, 32), P["stripe_bg"])
    px = im.load()
    for y in range(32):
        for x in range(32):
            if (x + y) % 12 < 4:
                px[x, y] = P["stripe_fg"]
    save(im, STYLE, "stripeback.png")

    # Sliders
    save(shape(26, 26, WHITE, WHITE, tl=5, br=5), STYLE, "slider_fill.png")
    save(shape(26, 26, CLEAR, WHITE, tl=5, br=5), STYLE, "slider_outline.png")
    save(shape(24, 26, P["grabber"], accent, tl=5, br=5), STYLE, "slider_grabber.png")

    # Check boxes
    box = shape(20, 20, P["checkbox_bg"], P["checkbox_edge"], tl=3, br=3)
    save(box, STYLE, "checkbox_unchecked.png")
    checked = box.copy()
    checked.alpha_composite(shape(8, 8, accent, accent, tl=2, br=2), (6, 6))
    save(checked, STYLE, "checkbox_checked.png")

    # Lobby: left-side shade gradient and the menu entry (accent bar plus faint fill, tinted by the stylesheet)
    im = Image.new("RGBA", (256, 4), CLEAR)
    px = im.load()
    for x in range(256):
        a = int(215 * (1 - x / 255) ** 1.4)
        for y in range(4):
            px[x, y] = P["shade"] + (a,)
    save(im, STYLE, "lobby_shade.png")
    im = Image.new("RGBA", (24, 24), (255, 255, 255, 40))
    px = im.load()
    for y in range(24):
        for x in range(3):
            px[x, y] = WHITE
    save(im, STYLE, "nav_button.png")


def overlay_textures():
    """Lobby overlays: scanlines and a rainbow title rule. The futurist skin gets clear placeholders of the
    same sizes so the shared rules can reference them without drawing anything."""
    if not P["rounded"]:
        save(Image.new("RGBA", (4, 4), CLEAR), STYLE, "scanlines.png")
        save(Image.new("RGBA", (256, 4), CLEAR), STYLE, "title_rule.png")
        return

    # Scanlines: one dark line every four rows, a fainter one in between
    im = Image.new("RGBA", (4, 4), CLEAR)
    px = im.load()
    for x in range(4):
        px[x, 1] = (0, 0, 0, 70)
        px[x, 3] = (0, 0, 0, 28)
    save(im, STYLE, "scanlines.png")

    # Rainbow rule under the lobby title
    im = Image.new("RGBA", (256, 4), CLEAR)
    px = im.load()
    for x in range(256):
        t = x / 255 * (len(RAINBOW) - 1)
        i = min(int(t), len(RAINBOW) - 2)
        r, g, b = lerp(RAINBOW[i], RAINBOW[i + 1], t - i)
        for y in range(4):
            px[x, y] = (r, g, b, 255 if y < 3 else 110)
    save(im, STYLE, "title_rule.png")


def hud_textures():
    accent = P["accent"]
    slot = shape(32, 32, P["slot_bg"], P["edge"], tl=5, br=5, inner=P["glass_inner"])
    save(slot, OUT, "SlotBackground.png")
    save(shape(32, 32, CLEAR, accent, tl=5, br=5, inner=accent[:3] + (200,)), OUT, "slot_highlight.png")
    save(shape(16, 16, P["slot_bg"], P["edge"], bl=4), OUT, "item_status_left.png")
    save(shape(16, 16, P["slot_bg"], P["edge"], br=4), OUT, "item_status_right.png")
    save(shape(16, 16, CLEAR, accent, bl=4), OUT, "item_status_left_highlight.png")
    save(shape(16, 16, CLEAR, accent, br=4), OUT, "item_status_right_highlight.png")
    return slot


def luma(p):
    return 0.299 * p[0] + 0.587 * p[1] + 0.114 * p[2]


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def recolour_slots(slot):
    """Keeps the stock slot icons but draws them on the skin's slot background in its metal tones."""
    default_bg = Image.open(os.path.join(DEFAULT, "SlotBackground.png")).convert("RGBA")
    src_dir = os.path.join(DEFAULT, "Slots")
    for name in sorted(os.listdir(src_dir)):
        if not name.endswith(".png"):
            continue
        src = Image.open(os.path.join(src_dir, name)).convert("RGBA")
        out = slot.copy()
        spx, bpx, opx = src.load(), default_bg.load(), out.load()
        for y in range(32):
            for x in range(32):
                p = spx[x, y]
                if p[3] == 0 or p == bpx[x, y]:
                    continue
                t = max(0.0, min(1.0, (luma(p) - 40) / 30))
                r, g, b = lerp(P["slot_dark"], P["slot_light"], t)
                opx[x, y] = (r, g, b, p[3])
        save(out, OUT, "Slots", name)


def recolour_storage():
    """Shifts the stock storage window pieces to the skin's palette; the red controls are copied."""
    src_dir = os.path.join(DEFAULT, "Storage")
    for name in sorted(os.listdir(src_dir)):
        if not name.endswith(".png"):
            continue
        src_path = os.path.join(src_dir, name)
        if name in ("back.png", "exit.png", "marked_first.png", "marked_second.png"):
            dest = os.path.join(OUT, "Storage", name)
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            shutil.copy(src_path, dest)
            written.append("Storage/" + name)
            continue
        src = Image.open(src_path).convert("RGBA")
        out = Image.new("RGBA", src.size, CLEAR)
        spx, opx = src.load(), out.load()
        for y in range(src.height):
            for x in range(src.width):
                p = spx[x, y]
                if p[3] == 0:
                    continue
                t = luma(p) / 255
                r, g, b = lerp(P["storage_dark"], P["storage_light"], t)
                opx[x, y] = (r, g, b, p[3])
        save(out, OUT, "Storage", name)


def attributions():
    derived = sorted(f for f in written if f.startswith("Slots/") or f.startswith("Storage/"))
    original = sorted(f for f in written if f not in derived)
    lines = [
        "- files: [%s]" % ", ".join('"%s"' % f for f in original),
        '  license: "CC-BY-SA-3.0"',
        '  copyright: "Generated for Wolfgate by Tools/_WF/WolfgateUiTextures/generate_textures.py"',
        '  source: "https://github.com/Aphelion-Moon/Wolfgate"',
        "",
        "- files: [%s]" % ", ".join('"%s"' % f for f in derived),
        '  license: "CC-BY-NC-SA-3.0"',
        '  copyright: "Recoloured from Resources/Textures/Interface/Default (goonstation, modified by PixelTK) by the Wolfgate texture generator"',
        '  source: "https://github.com/goonstation/goonstation/commit/e77d85d9c1d93aa32da7702737ceeac2b56738ac"',
        "",
    ]
    with open(os.path.join(OUT, "attributions.yml"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))


def anatomy_icon():
    """Neutral outline figure for the Anatomy top-bar button: ring head, hollow torso, plain limbs.

    Drawn white at 4x and downsampled to 64px like the stock svg.192dpi icons. The menu button tints it,
    so one copy serves every skin. Writes the texture, its filtering sidecar and the folder's attributions.yml.
    """
    size, scale = 64, 4
    big = size * scale

    def u(v):
        return int(round(v * scale))

    mask = Image.new("L", (big, big), 0)
    draw = ImageDraw.Draw(mask)

    def capsule(x0, y0, x1, y1, width):
        draw.line((u(x0), u(y0), u(x1), u(y1)), fill=255, width=u(width))
        r = u(width) / 2
        for x, y in ((x0, y0), (x1, y1)):
            draw.ellipse((u(x) - r, u(y) - r, u(x) + r, u(y) + r), fill=255)

    # Standing figure with a 4px pen: ring head and outlined torso, then limbs that stay clear of both holes
    draw.ellipse((u(25), u(3), u(39), u(17)), fill=255)
    draw.ellipse((u(29), u(7), u(35), u(13)), fill=0)
    draw.rounded_rectangle((u(23), u(20), u(41), u(40)), radius=u(5), fill=255)
    draw.rounded_rectangle((u(27), u(24), u(37), u(36)), radius=u(2), fill=0)
    capsule(22, 23, 17, 40, 5)
    capsule(42, 23, 47, 40, 5)
    capsule(28, 40, 26, 59, 6)
    capsule(36, 40, 38, 59, 6)

    resample = getattr(Image, "Resampling", Image).LANCZOS
    alpha = mask.resize((size, size), resample)

    im = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    im.putalpha(alpha)

    out_dir = os.path.join(ROOT, "Resources", "Textures", "_WF", "Interface")
    os.makedirs(out_dir, exist_ok=True)
    path = os.path.join(out_dir, "anatomy.svg.192dpi.png")
    im.save(path)
    with open(path + ".yml", "w", encoding="utf-8", newline="\n") as f:
        f.write("sample:\n  filter: true\n")
    lines = [
        '- files: ["anatomy.svg.192dpi.png"]',
        '  license: "CC-BY-SA-3.0"',
        '  copyright: "Generated for Wolfgate by Tools/_WF/WolfgateUiTextures/generate_textures.py"',
        '  source: "https://github.com/Aphelion-Moon/Wolfgate"',
        "",
    ]
    with open(os.path.join(out_dir, "attributions.yml"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print("Anatomy icon written to %s" % path)

def window_icons():
    """White title bar icons for the window pop-out button, tinted by the stylesheet, so one set serves every skin.
    Drawn 4x and downsampled to match the 22px stock close cross."""
    out = os.path.join(ROOT, "Resources", "Textures", "_WF", "Interface", "Window")
    os.makedirs(out, exist_ok=True)
    s, w = 4, 8

    def icon(arrow, head):
        im = Image.new("RGBA", (22 * s, 22 * s), CLEAR)
        d = ImageDraw.Draw(im)
        # Box with the top-right corner open for the arrow
        for line in (((3, 7), (10, 7)), ((3, 7), (3, 19)), ((3, 19), (15, 19)), ((15, 12), (15, 19))):
            d.line([(x * s, y * s) for x, y in line], fill=WHITE, width=w)
        for line in (arrow, *head):
            d.line([(x * s, y * s) for x, y in line], fill=WHITE, width=w)
        return im.resize((22, 22), Image.LANCZOS)

    icon(((9, 13), (19, 3)), (((12, 3), (19, 3)), ((19, 3), (19, 10)))).save(os.path.join(out, "popout.png"))
    icon(((19, 3), (9, 13)), (((9, 6), (9, 13)), ((9, 13), (16, 13)))).save(os.path.join(out, "dock.png"))
    with open(os.path.join(out, "attributions.yml"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join([
            '- files: ["popout.png", "dock.png"]',
            '  license: "CC-BY-SA-3.0"',
            '  copyright: "Generated for Wolfgate by Tools/_WF/WolfgateUiTextures/generate_textures.py"',
            '  source: "https://github.com/Aphelion-Moon/Wolfgate"',
            "",
        ]))
    print("Window icons written to %s" % out)


def main():
    global P, OUT, STYLE, written
    for skin, palette in SKINS.items():
        P = palette
        OUT = os.path.join(ROOT, "Resources", "Textures", "_WF", "Interface", skin)
        STYLE = os.path.join(OUT, "Style")
        written = []
        style_textures()
        sex_icons()
        overlay_textures()
        recolour_slots(hud_textures())
        recolour_storage()
        attributions()
        print("%s textures written to %s (%d files)" % (skin, OUT, len(written)))
    anatomy_icon()
    window_icons()


if __name__ == "__main__":
    main()
