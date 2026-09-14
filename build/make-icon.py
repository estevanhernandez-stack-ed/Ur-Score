"""Draw Ur Score's icon.png in the RoRoRo plugin family shape.

The family is Ur Task's icon: a navy rounded tile with transparent corners, an isometric slab
with magenta sides, a top face edged in a cyan-to-magenta gradient, and one white glyph on the
top face. Ur MCP shares the shape but its PNG was flattened to RGB, so its corners are white;
Ur Task's RGBA file is the reference and its colours and radius were sampled, not guessed.

Ur Score's glyph is three rising bars crossed by a threshold line: a score over time, and the
line a metric alert fires against.

Drawn at 4x and downsampled, which is the whole antialiasing strategy.

    python build/make-icon.py              # writes icon.png at the repo root
    python build/make-icon.py --preview P  # also writes a 256px + 48px comparison sheet to P
"""
from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw

SIZE = 256
SS = 4                      # supersample factor
S = SIZE * SS

# Sampled from rororo-ur-task/icon.png, plus the brand tokens in 626labs-design.
NAVY = (15, 31, 49, 255)          # --brand-navy-deep #0f1f31, the tile
FACE = (19, 40, 64, 255)          # top face fill
SIDE_LEFT = (184, 31, 102, 255)   # shaded magenta
SIDE_RIGHT = (242, 47, 137, 255)  # --brand-magenta #f22f89
CYAN = (23, 212, 250)             # --brand-cyan #17d4fa
MAGENTA = (242, 47, 137)
WHITE = (238, 246, 251, 255)
RADIUS = 52                       # Ur Task's tile corner

# Slab geometry, in 256px units.
CX = 128
TOP, RIGHT, BOTTOM, LEFT = (CX, 76), (210, 110), (CX, 144), (46, 110)
DEPTH = 52
EDGE = 4


def u(v: float) -> int:
    return round(v * SS)


def pt(p: tuple[float, float], dy: float = 0) -> tuple[int, int]:
    return (u(p[0]), u(p[1] + dy))


def lerp(a, b, t):
    return tuple(round(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)


def draw_icon(threshold: bool = True) -> Image.Image:
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=u(RADIUS), fill=NAVY)

    # Sides first, so the top face and its edge sit over their seams.
    d.polygon([pt(LEFT), pt(BOTTOM), pt(BOTTOM, DEPTH), pt(LEFT, DEPTH)], fill=SIDE_LEFT)
    d.polygon([pt(BOTTOM), pt(RIGHT), pt(RIGHT, DEPTH), pt(BOTTOM, DEPTH)], fill=SIDE_RIGHT)
    d.polygon([pt(TOP), pt(RIGHT), pt(BOTTOM), pt(LEFT)], fill=FACE)

    # Gradient edge: colour by x across the face, cyan on the left to magenta on the right,
    # laid down as short overlapping segments with round caps.
    ring = [TOP, RIGHT, BOTTOM, LEFT, TOP]
    steps = 60
    for a, b in zip(ring, ring[1:]):
        for i in range(steps):
            t0, t1 = i / steps, (i + 1) / steps
            p0 = (a[0] + (b[0] - a[0]) * t0, a[1] + (b[1] - a[1]) * t0)
            p1 = (a[0] + (b[0] - a[0]) * t1, a[1] + (b[1] - a[1]) * t1)
            mid_x = (p0[0] + p1[0]) / 2
            colour = lerp(CYAN, MAGENTA, (mid_x - LEFT[0]) / (RIGHT[0] - LEFT[0]))
            d.line([pt(p0), pt(p1)], fill=colour, width=u(EDGE))
            r = u(EDGE) / 2
            d.ellipse((u(p1[0]) - r, u(p1[1]) - r, u(p1[0]) + r, u(p1[1]) + r), fill=colour)

    # Glyph: three rising bars on one baseline, centred on the face.
    # Sized to the face's clearance: the tall bar's top stays about 4px inside the top-right edge
    # stroke and the baseline about 3px inside the bottom edges. Any larger touches the gradient.
    bar_w, gap, base = 13, 7, 128
    heights = (15, 25, 35)
    total = bar_w * 3 + gap * 2
    x0 = CX - total / 2

    if threshold:
        # Behind the bars, so it reads through the gaps and past both ends. Without it, three
        # rising bars read as a phone's signal-strength icon.
        y = base - 19
        d.rounded_rectangle((u(x0 - 9), u(y - 1.5), u(x0 + total + 9), u(y + 1.5)),
                            radius=u(1.5), fill=CYAN + (255,))

    for i, h in enumerate(heights):
        left = x0 + i * (bar_w + gap)
        d.rounded_rectangle((u(left), u(base - h), u(left + bar_w), u(base)),
                            radius=u(2.5), fill=WHITE)

    return img.resize((SIZE, SIZE), Image.LANCZOS)


def preview(path: Path) -> None:
    """Both variants at full size and at 48px, which is roughly how the Plugins list shows it."""
    pad = 24
    sheet = Image.new('RGBA', (pad * 3 + SIZE * 2, pad * 3 + SIZE + 48), (9, 16, 35, 255))
    for col, variant in enumerate((True, False)):
        icon = draw_icon(threshold=variant)
        x = pad + col * (SIZE + pad)
        sheet.alpha_composite(icon, (x, pad))
        sheet.alpha_composite(icon.resize((48, 48), Image.LANCZOS), (x, pad * 2 + SIZE))
    sheet.save(path)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument('--preview', type=Path)
    ap.add_argument('--no-threshold', action='store_true')
    a = ap.parse_args()

    out = Path(__file__).resolve().parent.parent / 'icon.png'
    icon = draw_icon(threshold=not a.no_threshold)
    icon.save(out)
    print(f'wrote {out}')

    # The same art as the EXE and window icon. Windows picks the nearest size for the title bar,
    # taskbar and Alt+Tab, so every size it asks for is in the file rather than scaled at runtime.
    ico = out.with_suffix('.ico')
    icon.save(ico, sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (256, 256)])
    print(f'wrote {ico}')
    if a.preview:
        preview(a.preview)
        print(f'wrote {a.preview}')


if __name__ == '__main__':
    main()
