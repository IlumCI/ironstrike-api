#!/usr/bin/env python3
"""Draws the API's icon library in the style of IRONSTRIKE's own skill icons.

    python3 art/icons/make_icons.py          # writes IronstrikeApi/Assets/Icons/*.png and art/icons/sheet.png

The game's icons are white silhouettes (the UI tints them gold on a dark diamond): bold solid shapes
with small knife-cut nicks along their edges, emblems cut out as negative space, four-point sparkles,
orbit rings, speed lines, cycle arrows. Every icon here is drawn from primitives on a 1024 px canvas,
roughened, then scaled down to 256 px, so the set stays consistent and can be regenerated.

Needs Pillow. The PNGs are committed, so building the API does not.
"""
import math
import os
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 1024                 # working canvas
OUT = 256                # shipped size
HERE = os.path.dirname(os.path.abspath(__file__))
DEST = os.path.normpath(os.path.join(HERE, "..", "..", "IronstrikeApi", "Assets", "Icons"))


# ---------------------------------------------------------------------- canvas helpers

class Ink:
    """A white-on-transparent drawing: add shapes, cut shapes out."""

    def __init__(self):
        self.m = Image.new("L", (S, S), 0)
        self.d = ImageDraw.Draw(self.m)

    def layer(self):
        return Ink()

    def add(self, other):
        self.m = ImageChops.lighter(self.m, other.m)
        self.d = ImageDraw.Draw(self.m)

    def cut(self, other):
        self.m = ImageChops.subtract(self.m, other.m)
        self.d = ImageDraw.Draw(self.m)

    # -- primitives (fill=255 draws, fill=0 erases)
    def poly(self, pts, fill=255):
        self.d.polygon([(float(x), float(y)) for x, y in pts], fill=fill)

    def circle(self, cx, cy, r, fill=255):
        self.d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=fill)

    def ring(self, cx, cy, r, w, fill=255):
        self.d.ellipse([cx - r, cy - r, cx + r, cy + r], outline=fill, width=int(w))

    def arc(self, cx, cy, r, a0, a1, w, fill=255):
        self.d.arc([cx - r, cy - r, cx + r, cy + r], a0, a1, fill=fill, width=int(w))

    def line(self, pts, w, fill=255):
        self.d.line([(float(x), float(y)) for x, y in pts], fill=fill, width=int(w), joint="curve")
        for x, y in (pts[0], pts[-1]):
            self.circle(x, y, w / 2 - 1, fill)

    def rect(self, x0, y0, x1, y1, fill=255):
        self.d.rectangle([x0, y0, x1, y1], fill=fill)


def rot(pts, cx, cy, deg):
    a = math.radians(deg)
    c, s = math.cos(a), math.sin(a)
    return [(cx + (x - cx) * c - (y - cy) * s, cy + (x - cx) * s + (y - cy) * c) for x, y in pts]


def star_pts(cx, cy, r_out, r_in, n, start=-90):
    pts = []
    for i in range(n * 2):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(start + i * 180 / n)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def sparkle(ink, cx, cy, r, fill=255):
    """The game's four-point sparkle."""
    ink.poly(star_pts(cx, cy, r, r * 0.22, 4), fill)


def heart_pts(cx, cy, w, h, steps=90):
    pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        x = 16 * math.sin(t) ** 3
        y = -(13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t))
        pts.append((cx + x * w / 32, cy + y * h / 34))
    return pts


def shield_pts(cx, top, w, h):
    """Heater shield: slightly dipped top, straight sides for half the height, then to a point."""
    side = h * 0.5
    pts = [(cx - w / 2 + w * i / 20, top + h * 0.04 * math.sin(math.pi * i / 20)) for i in range(21)]
    pts.append((cx + w / 2, top + side))
    for i in range(1, 21):                       # gently bowed lower edge to the point
        u = i / 20
        x = cx + w / 2 * (1 - u)
        y = top + side + (h - side) * u
        bow = math.sin(u * math.pi) * w * 0.07
        pts.append((x + bow, y))
    left = [(2 * cx - x, y) for x, y in reversed(pts[21:])]
    return pts + left


def sword(ink, x0, y0, x1, y1, w, fill=255):
    """A sword from pommel (x0,y0) to tip (x1,y1)."""
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    px, py = -uy, ux
    hilt = 0.24
    gx, gy = x0 + ux * L * hilt, y0 + uy * L * hilt
    blade = [(gx + px * w / 2, gy + py * w / 2), (x1 - ux * w * 1.4 + px * w / 2, y1 - uy * w * 1.4 + py * w / 2),
             (x1, y1), (x1 - ux * w * 1.4 - px * w / 2, y1 - uy * w * 1.4 - py * w / 2), (gx - px * w / 2, gy - py * w / 2)]
    ink.poly(blade, fill)
    guard = w * 2.0
    ink.line([(gx + px * guard, gy + py * guard), (gx - px * guard, gy - py * guard)], w * 0.75, fill)
    ink.line([(x0 + ux * w * 0.5, y0 + uy * w * 0.5), (gx, gy)], w * 0.6, fill)
    ink.circle(x0, y0, w * 0.6, fill)


def arrow(ink, x0, y0, x1, y1, w, head=2.6, fill=255, fletch=True):
    dx, dy = x1 - x0, y1 - y0
    L = math.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    px, py = -uy, ux
    hl = w * head * 1.5
    bx, by = x1 - ux * hl, y1 - uy * hl
    ink.line([(x0, y0), (bx, by)], w, fill)
    ink.poly([(x1, y1), (bx + px * w * head, by + py * w * head), (bx + ux * hl * 0.25, by + uy * hl * 0.25),
              (bx - px * w * head, by - py * w * head)], fill)
    if fletch:
        for k in (0, 1):
            fx, fy = x0 + ux * w * (1.2 + k * 1.8), y0 + uy * w * (1.2 + k * 1.8)
            for sgn in (1, -1):
                ink.poly([(fx, fy), (fx - ux * w * 1.6 + sgn * px * w * 1.7, fy - uy * w * 1.6 + sgn * py * w * 1.7),
                          (fx - ux * w * 2.6 + sgn * px * w * 1.7, fy - uy * w * 2.6 + sgn * py * w * 1.7),
                          (fx - ux * w, fy - uy * w)], fill)


def cycle_arrow(ink, cx, cy, r, w, a0=-60, a1=250, fill=255, ccw=False):
    """The game's ring with an arrowhead (Combo, Regeneration)."""
    ink.arc(cx, cy, r, a0, a1, w, fill)
    a = math.radians(a1 if not ccw else a0)
    ex, ey = cx + r * math.cos(a), cy + r * math.sin(a)
    tx, ty = -math.sin(a), math.cos(a)            # tangent (clockwise)
    if ccw:
        tx, ty = -tx, -ty
    nx, ny = math.cos(a), math.sin(a)
    hw = w * 1.6
    ink.poly([(ex + tx * w * 2.4, ey + ty * w * 2.4), (ex + nx * hw, ey + ny * hw), (ex - nx * hw, ey - ny * hw)], fill)


def flame_pts(cx, base, w, h, tongues=3, seed=1):
    rnd = random.Random(seed)
    pts = [(cx - w / 2, base)]
    for i in range(tongues * 2 + 1):
        t = i / (tongues * 2)
        x = cx - w / 2 + w * t
        if i % 2 == 1:
            y = base - h * (0.55 + 0.45 * math.sin(math.pi * t)) - rnd.uniform(-0.04, 0.04) * h
            x += rnd.uniform(-0.05, 0.05) * w
        else:
            y = base - h * (0.25 + 0.3 * math.sin(math.pi * t))
        pts.append((x, y))
    pts.append((cx + w / 2, base))
    pts += [(cx + w / 2 * math.cos(math.pi * k / 16), base + w * 0.32 * math.sin(math.pi * k / 16)) for k in range(17)]
    return pts


def droplet_pts(cx, cy, r, tip=1.9, steps=80):
    pts = []
    for i in range(steps):
        a = math.pi * 2 * i / steps - math.pi / 2
        k = max(0.0, math.sin(a + math.pi / 2)) ** 3  # sharpen toward the top
        rr = r * (1 + (tip - 1) * (1 - k) * (math.cos(a) < 0.0) * 0)
        x = cx + r * math.cos(a)
        y = cy + r * math.sin(a)
        if math.sin(a) < 0:  # upper half: pull to a point
            f = -math.sin(a)
            x = cx + (x - cx) * (1 - f) ** 0.9
            y = cy - r * (1 + (tip - 1) * f) * f - (1 - f) * 0
        pts.append((x, y))
    return pts


def gear_pts(cx, cy, r, teeth=8, depth=0.18):
    pts = []
    n = teeth * 4
    for i in range(n):
        a = 2 * math.pi * i / n
        rr = r if (i % 4) in (1, 2) else r * (1 - depth)
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    return pts


def crescent(ink, cx, cy, r, off, fill=255):
    m = ink.layer()
    m.circle(cx, cy, r)
    m.circle(cx + off, cy - off * 0.35, r * 0.88, 0)
    if fill:
        ink.add(m)
    else:
        ink.cut(m)


def speed_lines(ink, x0, x1, ys, w):
    for i, y in enumerate(ys):
        ink.line([(x0 + (i % 2) * 40, y), (x1, y)], w)


# ---------------------------------------------------------------------- the hand-cut look

def roughen(mask, seed, nicks=26, size=0.022):
    """Small triangular knife-cuts along the outline, like the game's icons."""
    rnd = random.Random(seed)
    edge = mask.filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 64 else 0)
    px = edge.load()
    pts = [(x, y) for y in range(0, S, 4) for x in range(0, S, 4) if px[x, y]]
    if not pts:
        return mask
    cut = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(cut)
    for _ in range(nicks):
        x, y = rnd.choice(pts)
        r = S * size * rnd.uniform(0.6, 1.3)
        a = rnd.uniform(0, 2 * math.pi)
        tri = [(x + r * math.cos(a), y + r * math.sin(a)),
               (x + r * 0.5 * math.cos(a + 2.4), y + r * 0.5 * math.sin(a + 2.4)),
               (x + r * 0.5 * math.cos(a - 2.4), y + r * 0.5 * math.sin(a - 2.4))]
        d.polygon(tri, fill=255)
    return ImageChops.subtract(mask, cut)


def finish(ink, seed, nicks=26):
    m = roughen(ink.m, seed, nicks)
    bb = m.getbbox()
    out = Image.new("RGBA", (S, S), (255, 255, 255, 0))
    out.putalpha(m)
    if bb:  # center the glyph, leaving a margin like the game's sprites
        glyph = out.crop(bb)
        side = max(glyph.width, glyph.height)
        pad = int(side * 0.06)
        sq = Image.new("RGBA", (side + 2 * pad, side + 2 * pad), (255, 255, 255, 0))
        sq.paste(glyph, ((sq.width - glyph.width) // 2, (sq.height - glyph.height) // 2), glyph)
        out = sq
    return out.resize((OUT, OUT), Image.LANCZOS)


# ---------------------------------------------------------------------- the icons

ICONS = {}


def icon(name, nicks=26):
    def reg(fn):
        ICONS[name] = (fn, nicks)
        return fn
    return reg


C = S / 2

# ---- magic schools: one strong emblem each, like the game's own schools

@icon("school_blood")
def _(k):
    k.poly(droplet_pts(C, 590, 250, tip=1.85))
    cut = k.layer()
    cut.poly(heart_pts(C, 640, 230, 210), 255)
    k.cut(cut)
    k.poly(heart_pts(C, 650, 150, 140))           # a heart inside the drop
    for x in (C - 110, C + 110):
        k.poly([(x - 26, 800), (x + 26, 800), (x, 900)])   # two dripping fangs


@icon("school_void")
def _(k):
    k.ring(C, C, 330, 54)
    k.circle(C, C, 205)
    k.circle(C + 60, C - 45, 165, 0)                # an eclipse: the moon eats the sun
    o = k.layer()
    o.d.ellipse([C - 470, C - 110, C + 470, C + 110], outline=255, width=34)
    k.add(o)
    sparkle(k, C + 300, C - 300, 95)
    sparkle(k, C - 330, C + 290, 60)


@icon("school_tide")
def _(k):
    for i, y in enumerate((420, 560, 700)):
        pts = []
        for xx in range(110, 915, 10):
            t = (xx - 110) / 805
            pts.append((xx, y + 60 * math.sin(t * math.pi * 3 + i)))
        k.line(pts, 70 - i * 10)
    crest = []
    for a in range(0, 271, 6):
        r = 190 - a * 0.45
        crest.append((C + 120 + r * math.cos(math.radians(a - 200)), 330 + r * math.sin(math.radians(a - 200))))
    k.line(crest, 66)
    sparkle(k, 250, 230, 70)


@icon("school_clockwork")
def _(k):
    k.poly(gear_pts(C, C, 400, teeth=10))
    k.circle(C, C, 285, 0)
    k.circle(C, C, 235)
    # an hourglass cut through the middle
    hg = k.layer()
    hg.poly([(C - 130, C - 170), (C + 130, C - 170), (C + 18, C), (C + 130, C + 170), (C - 130, C + 170), (C - 18, C)])
    k.cut(hg)
    k.poly([(C - 70, C + 130), (C + 70, C + 130), (C, C + 40)])   # sand


@icon("school_bone")
def _(k):
    k.circle(C, 400, 255)
    k.rect(C - 150, 520, C + 150, 700)
    k.circle(C - 100, 410, 78, 0)                   # eyes
    k.circle(C + 100, 410, 78, 0)
    k.poly([(C, 500), (C - 38, 575), (C + 38, 575)], 0)
    for i in range(-2, 3):
        k.rect(C + i * 56 - 16, 640, C + i * 56 + 16, 700, 0)    # teeth
    for sgn in (1, -1):                              # crossed bones behind
        b = k.layer()
        b.line([(C - sgn * 400, 880), (C + sgn * 400, 560)], 70)
        for x, y in ((C - sgn * 400, 880), (C + sgn * 400, 560)):
            b.circle(x - 30, y, 52)
            b.circle(x + 30, y, 52)
        k.add(b)


@icon("school_time")
def _(k):
    k.ring(C, C, 420, 46)
    a = math.radians(-40)
    k.poly([(C + 420 * math.cos(a) - 10, C + 420 * math.sin(a) - 80), (C + 420 * math.cos(a) + 95, C + 420 * math.sin(a) + 10),
            (C + 420 * math.cos(a) - 50, C + 420 * math.sin(a) + 70)])
    k.ring(C, C, 420, 120, 0) if False else None
    k.rect(C - 210, 230, C + 210, 280)
    k.rect(C - 210, 744, C + 210, 794)
    k.poly([(C - 170, 280), (C + 170, 280), (C + 30, 500), (C + 30, 524), (C + 170, 744), (C - 170, 744), (C - 30, 524), (C - 30, 500)])
    k.poly([(C - 110, 320), (C + 110, 320), (C + 14, 470), (C - 14, 470)], 0)
    k.poly([(C - 14, 560), (C + 14, 560), (C + 110, 704), (C - 110, 704)], 0)
    k.poly([(C - 75, 704), (C + 75, 704), (C, 610)])
    k.rect(C - 7, 470, C + 7, 600)

@icon("school_beast")
def _(k):
    for i in range(3):
        x0, x1 = 230 + i * 210, 330 + i * 210
        pts = []
        for t in range(0, 101, 5):
            u = t / 100
            pts.append((x0 + (x1 - x0) * u + 70 * math.sin(u * math.pi), 130 + 760 * u))
        w = [int(10 + 80 * math.sin(math.pi * (j / (len(pts) - 1))) ) for j in range(len(pts))]
        for j in range(len(pts) - 1):
            k.line([pts[j], pts[j + 1]], max(12, w[j]))

@icon("school_echo")
def _(k):
    # a war horn, its call spreading out from the bell
    pts = []
    for t in range(0, 101, 2):
        u = t / 100
        x = 140 + 430 * u
        y = 820 - 300 * u ** 1.4
        w = 28 + 200 * u ** 2.2
        pts.append((x, y, w))
    upper = [(x - w * 0.35, y - w) for x, y, w in pts]
    lower = [(x + w * 0.35, y + w) for x, y, w in reversed(pts)]
    k.poly(upper + lower)
    k.circle(140, 820, 38)
    k.rect(260, 700, 290, 830, 0)
    for i, r in enumerate((150, 260, 370)):
        k.arc(560, 520, r, -75, 15, 44 - i * 8)
    sparkle(k, 230, 330, 70)

# ---- spells: smaller glyphs, like the game's spell icons

@icon("spell_blood_lance")
def _(k):
    arrow(k, 160, 860, 860, 160, 70, head=2.7, fletch=False)
    k.poly(droplet_pts(330, 560, 90, tip=1.8))
    k.poly(droplet_pts(560, 820, 70, tip=1.8))


@icon("spell_hemorrhage")
def _(k):
    k.poly(heart_pts(C, 540, 760, 660))
    crack = k.layer()
    crack.line([(C - 40, 300), (C + 50, 450), (C - 60, 560), (C + 40, 680), (C - 10, 860)], 44)
    k.cut(crack)
    for x, y, r in ((300, 900, 40), (720, 880, 32)):
        k.poly(droplet_pts(x, y, r, tip=1.8))


@icon("spell_void_rift")
def _(k):
    k.d.ellipse([C - 410, C - 190, C + 410, C + 190], fill=255)
    k.d.ellipse([C - 330, C - 110, C + 330, C + 110], fill=0)
    k.poly([(C, C - 150), (C + 60, C), (C, C + 150), (C - 60, C)])
    sparkle(k, C - 300, C - 300, 80)
    sparkle(k, C + 320, C + 280, 110)


@icon("spell_singularity")
def _(k):
    pts = []
    for i in range(360 * 3):
        a = math.radians(i)
        r = 40 + i * 0.36
        pts.append((C + r * math.cos(a), C + r * math.sin(a)))
    k.line(pts, 54)
    k.circle(C, C, 70)
    for a in (40, 160, 280):
        sparkle(k, C + 440 * math.cos(math.radians(a)), C + 440 * math.sin(math.radians(a)), 60)


@icon("spell_tidal_surge")
def _(k):
    # the classic breaking wave: the face sweeps up from the left and curls over into a spiral lip
    face = [(110, 900), (110, 700)]
    for t in range(0, 101, 2):
        u = t / 100
        face.append((110 + 420 * u, 700 - 470 * math.sin(u * math.pi / 2)))
    spiral = []
    for i in range(0, 300, 6):                    # the lip curls clockwise inward
        a = math.radians(-90 + i)
        r = 250 - i * 0.55
        spiral.append((560 + r * math.cos(a), 480 + r * math.sin(a)))
    k.poly(face + spiral + [(640, 600), (760, 720), (914, 760), (914, 900)])
    hole = k.layer()
    hole.circle(570, 470, 95)
    k.cut(hole)
    for i, x in enumerate((240, 400, 560, 720)):  # foam
        k.circle(x, 860 - (i % 2) * 20, 26, 0)
    sparkle(k, 860, 210, 72)

@icon("spell_whirlpool")
def _(k):
    for i in range(3):
        a0 = i * 120
        pts = []
        for t in range(0, 241, 4):
            a = math.radians(a0 + t)
            r = 420 - t * 1.4
            pts.append((C + r * math.cos(a), C + r * 0.62 * math.sin(a)))
        k.line(pts, 58 - i * 6)
    k.circle(C, C, 50)


@icon("spell_overclock")
def _(k):
    k.poly(gear_pts(600, C, 300, teeth=8))
    k.circle(600, C, 120, 0)
    speed_lines(k, 90, 340, (330, 450, 570, 690), 40)
    k.poly([(620, 330), (540, 530), (640, 530), (580, 700), (760, 470), (650, 470), (720, 330)], 0)


@icon("spell_bone_spear")
def _(k):
    k.poly([(880, 140), (820, 420), (740, 350), (600, 290)])
    k.poly([(880, 140), (600, 290), (690, 330)])
    k.line([(700, 330), (230, 830)], 84)
    for t in (0.3, 0.55, 0.8):
        x, y = 700 + (230 - 700) * t, 330 + (830 - 330) * t
        k.circle(x, y, 68)
    k.circle(190, 840, 72)
    k.circle(240, 890, 72)

@icon("spell_rewind")
def _(k):
    cycle_arrow(k, C, C, 380, 70, a0=-40, a1=250, ccw=True)
    k.line([(C, C), (C, 300)], 54)
    k.line([(C, C), (C + 150, C + 90)], 54)
    k.circle(C, C, 50)


@icon("spell_savage_pounce")
def _(k):
    for i in range(3):
        pts = []
        for t in range(0, 101, 5):
            u = t / 100
            pts.append((260 + i * 200 + 140 * u, 160 + 700 * u - 90 * math.sin(u * math.pi)))
        for j in range(len(pts) - 1):
            u = j / (len(pts) - 1)
            k.line([pts[j], pts[j + 1]], max(10, int(96 * math.sin(math.pi * u))))
    speed_lines(k, 60, 200, (360, 520, 680), 34)

@icon("spell_shriek")
def _(k):
    # a wailing spirit: hooded head, gaping mouth, the cry spreading out
    hood = [(380, 200), (520, 300), (560, 520), (520, 760), (430, 900), (260, 900), (180, 760), (160, 520), (220, 300)]
    k.poly(hood)
    k.d.ellipse([255, 380, 345, 470], fill=0)
    k.d.ellipse([400, 380, 490, 470], fill=0)
    k.d.ellipse([290, 560, 460, 790], fill=0)
    for i, r in enumerate((180, 300, 420)):
        k.arc(470, 640, r, -45, 30, 50 - i * 8)

# ---- skills

@icon("skill_bloodlust")
def _(k):
    k.poly(heart_pts(C, 500, 780, 680))
    for x in (C - 120, C + 120):               # two fangs cut into the heart
        k.poly([(x - 60, 330), (x + 60, 330), (x + 10, 650), (x - 10, 650)], 0)
    for x in (C - 120, C + 120):
        k.poly(droplet_pts(x, 820, 48, tip=1.9))

@icon("skill_second_wind")
def _(k):
    cycle_arrow(k, C, C, 360, 66, a0=-70, a1=230)
    f = k.layer()
    f.poly([(C - 30, 760), (C + 150, 260), (C + 60, 300), (C - 20, 540), (C - 60, 500), (C - 90, 720)])
    k.add(f)


@icon("skill_momentum")
def _(k):
    for i in range(3):
        x = 360 + i * 170
        k.poly([(x, 230), (x + 230, C), (x, 794), (x - 110, 794), (x + 120, C), (x - 110, 230)])
    speed_lines(k, 80, 260, (380, C, 644), 44)


@icon("skill_thorns")
def _(k):
    sp = shield_pts(C, 230, 560, 650)
    k.poly(sp)
    n = len(sp)
    for i in range(3, n - 3, max(1, n // 11)):  # thorns along the rim
        x, y = sp[i]
        cx, cy = C, 520
        a = math.atan2(y - cy, x - cx)
        k.poly([(x + 34 * math.cos(a + 1.6), y + 34 * math.sin(a + 1.6)), (x + 34 * math.cos(a - 1.6), y + 34 * math.sin(a - 1.6)),
                (x + 120 * math.cos(a), y + 120 * math.sin(a))])
    k.poly(star_pts(C, 500, 150, 60, 5), 0)

@icon("skill_overcharge")
def _(k):
    k.ring(C, C, 380, 56)
    k.poly([(C + 70, 160), (C - 170, 560), (C - 10, 560), (C - 90, 870), (C + 190, 440), (C + 30, 440), (C + 140, 160)])
    sparkle(k, C + 380, C - 380, 70)


@icon("skill_executioner")
def _(k):
    k.line([(230, 900), (720, 230)], 60)
    head = []
    for a in range(-70, 71, 5):
        r = 330
        head.append((720 + r * math.cos(math.radians(a)) * 0.9, 300 + r * math.sin(math.radians(a))))
    head = [(560, 110), (650, 160)] + [(720 + 300 * math.cos(math.radians(a)), 300 + 300 * math.sin(math.radians(a)))
                                      for a in range(-80, 81, 5)] + [(650, 470), (560, 520), (610, 300)]
    k.poly(head)
    k.circle(700, 300, 46, 0)

@icon("skill_vampiric_strike")
def _(k):
    sword(k, 180, 860, 840, 190, 64)
    k.poly(droplet_pts(760, 640, 95, tip=1.8))
    k.poly(droplet_pts(870, 820, 60, tip=1.8))


@icon("skill_ricochet")
def _(k):
    k.line([(140, 300), (C, 760)], 46)
    arrow(k, C, 760, 880, 260, 46, head=2.8, fletch=False)
    k.ring(C, 820, 70, 26)
    sparkle(k, 180, 820, 70)


@icon("skill_fortify")
def _(k):
    k.rect(260, 340, 764, 900)
    for i in range(4):
        x = 260 + i * 168
        k.rect(x, 230, x + 100, 340)
    k.d.pieslice([400, 560, 624, 784], 180, 360, fill=0)
    k.rect(400, 672, 624, 900, 0)


@icon("skill_frenzy")
def _(k):
    sword(k, 200, 860, 820, 220, 56)
    sword(k, 824, 860, 204, 220, 56)
    for a in (-120, -60, 0):
        sparkle(k, C + 420 * math.cos(math.radians(a)), 480 + 420 * math.sin(math.radians(a)), 55)


@icon("skill_last_stand")
def _(k):
    k.poly(shield_pts(C, 170, 620, 760))
    crack = k.layer()
    crack.line([(C - 20, 150), (C + 40, 330), (C - 50, 480), (C + 30, 640), (C - 10, 940)], 34)
    k.cut(crack)
    k.poly(star_pts(C + 230, 270, 120, 46, 5))


@icon("skill_hunters_focus")
def _(k):
    k.ring(C, C, 330, 46)
    for a in (0, 90, 180, 270):
        x, y = C + 330 * math.cos(math.radians(a)), C + 330 * math.sin(math.radians(a))
        k.line([(x, y), (C + 450 * math.cos(math.radians(a)), C + 450 * math.sin(math.radians(a)))], 46)
    k.d.ellipse([C - 210, C - 120, C + 210, C + 120], fill=255)
    k.circle(C, C, 82, 0)
    k.circle(C, C, 34)


# ---- the example spell pack's schools and spells

def cloud(k, cx, cy, w, fill=255):
    """A storm cloud: overlapping puffs on a flat bottom."""
    for dx, dy, r in ((-0.30, 0.05, 0.22), (-0.05, -0.12, 0.30), (0.25, -0.02, 0.24), (0.42, 0.10, 0.15), (-0.45, 0.14, 0.13)):
        k.circle(cx + dx * w, cy + dy * w, r * w, fill)
    k.rect(cx - 0.52 * w, cy + 0.02 * w, cx + 0.52 * w, cy + 0.24 * w, fill)


def bolt_pts(x0, y0, x1, y1, w):
    """A lightning bolt from (x0,y0) down to (x1,y1)."""
    mx, my = (x0 + x1) / 2, (y0 + y1) / 2
    return [(x0 - w * 0.6, y0), (x0 + w * 0.9, y0), (mx + w * 0.5, my - w * 0.2), (mx + w * 1.6, my - w * 0.2),
            (x1, y1), (mx - w * 0.3, my + w * 0.5), (mx - w * 1.4, my + w * 0.5)]


def bomb_pts(cx, top, w, h):
    """An aerial bomb nose-down: rounded body, pointed nose, fins at the top."""
    pts = []
    for a in range(0, 181, 6):
        t = math.radians(a)
        pts.append((cx + w / 2 * math.cos(t), top + h * 0.62 + h * 0.38 * math.sin(t) ** 0.8))
    pts = [(cx + w / 2, top + h * 0.25)] + pts + [(cx - w / 2, top + h * 0.25)]
    return [(cx, top)] + pts


def skull(k, cx, cy, r, fill=255):
    k.circle(cx, cy, r, fill)
    k.rect(cx - r * 0.58, cy + r * 0.45, cx + r * 0.58, cy + r * 1.05, fill)
    k.circle(cx - r * 0.38, cy + r * 0.05, r * 0.28, 0)
    k.circle(cx + r * 0.38, cy + r * 0.05, r * 0.28, 0)
    k.poly([(cx, cy + r * 0.38), (cx - r * 0.14, cy + r * 0.62), (cx + r * 0.14, cy + r * 0.62)], 0)
    for i in (-1, 0, 1):
        k.rect(cx + i * r * 0.28 - r * 0.07, cy + r * 0.82, cx + i * r * 0.28 + r * 0.07, cy + r * 1.05, 0)


@icon("school_tempest")
def _(k):
    cloud(k, C, 360, 760)
    b = k.layer()
    b.poly(bolt_pts(C + 10, 330, C - 60, 960, 70))
    gap = k.layer()
    gap.poly(bolt_pts(C + 10, 300, C - 60, 990, 104))
    k.cut(gap)                                       # a dark outline around the bolt
    k.add(b)
    sparkle(k, 820, 690, 80)
    sparkle(k, 210, 720, 55)


@icon("school_necromancy")
def _(k):
    k.ring(C, C, 430, 40)
    for a in range(0, 360, 60):
        x, y = C + 430 * math.cos(math.radians(a - 90)), C + 430 * math.sin(math.radians(a - 90))
        k.poly([(x, y - 62), (x + 40, y), (x, y + 62), (x - 40, y)])
        k.poly([(x, y - 30), (x + 18, y), (x, y + 30), (x - 18, y)], 0)
    skull(k, C, 450, 205)
    k.poly(flame_pts(C, 260, 230, 300, tongues=3, seed=7))      # a soul flame rising


@icon("school_mind")
def _(k):
    eye = k.layer()
    eye.d.ellipse([C - 380, C - 170, C + 380, C + 210], fill=255)
    k.add(eye)
    k.circle(C, C + 20, 150, 0)
    k.circle(C, C + 20, 92)
    k.circle(C + 34, C - 14, 30, 0)
    for i, r in enumerate((270, 360, 450)):
        k.arc(C, C + 40, r, 230, 310, 40 - i * 6)               # thought waves above
    sparkle(k, C, 120, 70)


@icon("school_war")
def _(k):
    # an aerial bomb falling nose-first, fins up, onto a burst
    k.d.ellipse([C - 150, 250, C + 150, 760], fill=255)
    k.poly([(C - 120, 640), (C + 120, 640), (C, 860)])
    k.rect(C - 40, 150, C + 40, 300)
    for sgn in (-1, 1):
        k.poly([(C + sgn * 30, 140), (C + sgn * 190, 90), (C + sgn * 190, 250), (C + sgn * 60, 320)])
    k.rect(C - 160, 430, C + 160, 470, 0)
    speed_lines(k, 170, 300, (260, 360, 460), 34)
    speed_lines(k, 724, 854, (260, 360, 460), 34)
    k.poly(star_pts(C, 900, 170, 60, 8))

@icon("school_plague")
def _(k):
    k.circle(C, C, 120)
    for a in (-90, 30, 150):
        x, y = C + 230 * math.cos(math.radians(a)), C + 230 * math.sin(math.radians(a))
        k.circle(x, y, 210)
        k.circle(x + 70 * math.cos(math.radians(a)), y + 70 * math.sin(math.radians(a)), 150, 0)
    k.circle(C, C, 70, 0)
    k.ring(C, C, 330, 40)
    hole = k.layer()
    for a in (-90, 30, 150):
        hole.line([(C, C), (C + 140 * math.cos(math.radians(a)), C + 140 * math.sin(math.radians(a)))], 34)
    k.cut(hole)


@icon("spell_thunderstorm")
def _(k):
    cloud(k, C, 300, 820)
    for x0, x1, y1 in ((C - 250, C - 300, 900), (C + 20, C - 40, 970), (C + 280, C + 230, 880)):
        gap = k.layer()
        gap.poly(bolt_pts(x0 + 20, 330, x1, y1 + 20, 74))
        k.cut(gap)
        k.poly(bolt_pts(x0 + 20, 380, x1, y1, 50))

@icon("spell_chain_lightning")
def _(k):
    nodes = [(170, 330), (512, 700), (860, 300)]
    for (x0, y0), (x1, y1) in zip(nodes, nodes[1:]):
        dx, dy = x1 - x0, y1 - y0
        L = math.hypot(dx, dy)
        ux, uy = dx / L, dy / L
        nx, ny = -uy, ux
        pts = []
        for t, o in ((0.0, 0), (0.38, 70), (0.46, -40), (0.62, 60), (0.70, -30), (1.0, 0)):
            pts.append((x0 + dx * t + nx * o, y0 + dy * t + ny * o))
        k.line(pts, 58)
    for x, y in nodes:
        k.circle(x, y, 120)
        k.circle(x, y, 58, 0)
        k.circle(x, y, 26)
    sparkle(k, 512, 220, 110)

@icon("spell_orbital_laser")
def _(k):
    k.d.ellipse([C - 260, 90, C + 260, 230], outline=255, width=40)     # the orbit
    k.circle(C, 160, 70)
    k.poly([(C - 60, 210), (C + 60, 210), (C + 120, 800), (C - 120, 800)])   # the beam
    k.poly([(C - 22, 230), (C + 22, 230), (C + 44, 780), (C - 44, 780)], 0)
    k.poly(star_pts(C, 830, 220, 70, 8))
    k.rect(140, 900, 884, 940)


@icon("spell_raise_dead")
def _(k):
    k.d.rounded_rectangle([C - 230, 430, C + 230, 920], radius=200, fill=255)
    k.rect(C - 26, 520, C + 26, 800, 0)
    k.rect(C - 110, 590, C + 110, 640, 0)
    k.rect(150, 900, 874, 950)
    arrow(k, C, 400, C, 80, 70, head=3.0, fletch=False)
    sparkle(k, 240, 260, 70)
    sparkle(k, 790, 230, 55)


@icon("spell_life_drain")
def _(k):
    k.poly(droplet_pts(240, 640, 175, tip=1.8))
    k.poly(heart_pts(760, 600, 430, 390))
    k.poly(heart_pts(760, 610, 210, 190), 0)
    pts = [(500 + 300 * math.cos(math.radians(a)), 470 - 260 * math.sin(math.radians(a))) for a in range(185, 0, -5)]
    k.line(pts, 62)
    ex, ey = pts[-1]
    k.poly([(ex + 95, ey - 30), (ex - 70, ey - 40), (ex + 10, ey + 120)])

@icon("spell_kinetic_ward")
def _(k):
    k.poly(shield_pts(C + 120, 200, 520, 640))
    k.poly(shield_pts(C + 120, 300, 300, 400), 0)
    for i, r in enumerate((260, 360, 460)):
        k.arc(C + 120, 520, r, 150, 210, 44 - i * 6)


@icon("spell_telekinesis")
def _(k):
    rock = [(C - 230, 300), (C - 90, 170), (C + 140, 190), (C + 260, 320), (C + 200, 470), (C - 40, 520), (C - 230, 440)]
    k.poly(rock)
    k.line([(C - 90, 260), (C + 20, 330), (C + 150, 300)], 28, 0)
    for i, y in enumerate((640, 750, 860)):
        w = 300 - i * 70
        pts = [(C - w + t * 2 * w / 30, y + 26 * math.sin(t / 30 * math.pi * 4)) for t in range(31)]
        k.line(pts, 40)
    sparkle(k, 190, 600, 60)
    sparkle(k, 840, 560, 60)


@icon("spell_flak_shot")
def _(k):
    for deg in (-26, 0, 26):
        shell = [(C - 52, 760), (C + 52, 760), (C + 52, 470), (C, 330), (C - 52, 470)]
        k.poly(rot(shell, C, 900, deg))
        band = [(C - 54, 690), (C + 54, 690), (C + 54, 720), (C - 54, 720)]
        k.poly(rot(band, C, 900, deg), 0)
    k.poly(star_pts(C, 170, 150, 60, 8))
    k.circle(C, 170, 40, 0)


@icon("spell_air_strike")
def _(k):
    k.ring(C, 700, 230, 40)
    k.line([(C, 420), (C, 520)], 36)
    k.line([(C, 880), (C, 980)], 30)
    k.line([(C - 330, 700), (C - 250, 700)], 36)
    k.line([(C + 250, 700), (C + 330, 700)], 36)
    k.circle(C, 700, 50)
    for x, top in ((C - 250, 90), (C, 40), (C + 250, 90)):
        k.poly(bomb_pts(x, top + 300, 110, -260))
        k.line([(x, top - 20), (x, top + 20)], 24)


@icon("spell_plague_cloud")
def _(k):
    cloud(k, C, 330, 800)
    for x, y in ((C - 230, 720), (C, 820), (C + 230, 720)):
        k.poly(droplet_pts(x, y, 80, tip=1.9))
    k.circle(C - 120, 360, 60, 0)
    k.circle(C + 110, 320, 60, 0)
    k.circle(C - 10, 430, 60, 0)

@icon("spell_pestilence")
def _(k):
    for sgn in (-1, 1):
        w = k.layer()
        w.d.ellipse([C + sgn * 40 - 210 + sgn * 170, 170, C + sgn * 40 + 210 + sgn * 170, 520], fill=255)
        w.d.ellipse([C + sgn * 40 - 140 + sgn * 170, 240, C + sgn * 40 + 140 + sgn * 170, 450], fill=0)
        k.add(w)
    k.d.ellipse([C - 110, 360, C + 110, 860], fill=255)        # body
    k.circle(C, 300, 105)                                        # head
    k.circle(C - 46, 285, 30, 0)
    k.circle(C + 46, 285, 30, 0)
    for i in range(3):
        k.rect(C - 110, 520 + i * 100, C + 110, 545 + i * 100, 0)
    for sgn in (-1, 1):
        for y, dx in ((470, 230), (580, 260), (690, 230)):
            k.line([(C + sgn * 100, y), (C + sgn * dx, y + 60), (C + sgn * (dx + 40), y + 150)], 26)


def main():
    os.makedirs(DEST, exist_ok=True)
    made = []
    for i, (name, (fn, nicks)) in enumerate(ICONS.items()):
        ink = Ink()
        fn(ink)
        img = finish(ink, seed=1000 + i, nicks=nicks)
        img.save(os.path.join(DEST, name + ".png"))
        made.append((name, img))
    # preview: gold on the card's dark diamond, as the game shows them
    cell, cols = 200, 8
    rows = (len(made) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * (cell + 24)), (24, 18, 22, 255))
    d = ImageDraw.Draw(sheet)
    for i, (name, img) in enumerate(made):
        x, y = (i % cols) * cell, (i // cols) * (cell + 24)
        dia = [(x + cell / 2, y + 8), (x + cell - 8, y + cell / 2), (x + cell / 2, y + cell - 8), (x + 8, y + cell / 2)]
        d.polygon(dia, fill=(52, 30, 30, 255), outline=(120, 90, 70, 255))
        g = Image.new("RGBA", img.size, (245, 180, 66, 255))
        g.putalpha(img.getchannel("A"))
        g = g.resize((112, 112), Image.LANCZOS)
        sheet.paste(g, (int(x + cell / 2 - 56), int(y + cell / 2 - 56)), g)
        d.text((x + 6, y + cell + 4), name, fill=(220, 210, 200, 255))
    sheet.save(os.path.join(HERE, "sheet.png"))
    print(f"{len(made)} icons -> {DEST}")


if __name__ == "__main__":
    main()
