"""
Thornspire: a thorny display face derived from Metamorphous (SIL OFL 1.1).

Pipeline per glyph (outlines in font units, UPM 2048):
  1. flatten TrueType contours to polygons (nonzero-ish: clockwise = ink, ccw = counters)
  2. calligraphic embolden: Minkowski sum with a slanted broad-nib segment (thick verticals, thin horizontals)
  3. thorns: stretch every sharp convex tip outward along its bisector, tapering the neighbourhood
  4. crown spikes on a few letters (A apex, t, d, ...)
  5. simplify, orient for TrueType, write glyf / hmtx
"""
import math, os, sys, json
HERE = os.path.dirname(os.path.abspath(__file__))
import numpy as np
from fontTools.ttLib import TTFont
from fontTools.pens.basePen import BasePen
from fontTools.pens.ttGlyphPen import TTGlyphPen
from shapely.geometry import Polygon, MultiPolygon, LineString, Point
from shapely.ops import unary_union
from shapely import affinity
import unicodedata

P = dict(
    nib=150.0,          # broad-nib length (font units) -> extra stem weight
    nib_angle=14.0,     # degrees; tilt of the nib (0 = pure horizontal emboldening)
    nib_steps=36,
    base_grow=6.0,      # small uniform grow after the nib
    tip_angle=70.0,     # single convex corners sharper than this become thorns
    corner_angle=140.0, # corner detection threshold (flat stroke ends = two ~90 degree corners)
    cap_max=150.0,       # max width of a flat stroke end that is pinched into a thorn
    tip_len=250.0,      # max thorn extension
    tip_radius=140.0,   # arc radius over which the stretch tapers
    tip_probe=55.0,     # arc distance used to measure a vertex's angle (thin stroke ends read as tips)
    crown_h=460.0,      # crown spike height
    simplify=1.2,
    barb_len=120.0,     # mid-stem thorn on capitals with a left stem
    waist=34.0,         # stem pinch at mid-height (each side)
    stem_span=90.0,     # an edge must stay vertical this far along the ring to count as a stem
)
if len(sys.argv) > 1:
    P.update(json.loads(sys.argv[1]))

# Crown spikes: glyph -> (height scale, lean in units). Applied at the topmost stem of the glyph.
CROWNS = {'A': (1.0, -60.0), 't': (0.8, 0.0), 'd': (0.55, 60.0)}


class FlattenPen(BasePen):
    def __init__(self, glyphSet, step=12.0):
        super().__init__(glyphSet)
        self.rings, self.cur, self.step = [], None, step

    def _moveTo(self, p):
        self.cur = [p]

    def _lineTo(self, p):
        self.cur.append(p)

    def _qCurveToOne(self, p1, p2):
        p0 = self.cur[-1]
        n = max(2, int(math.dist(p0, p1) + math.dist(p1, p2)) // int(self.step))
        for i in range(1, n + 1):
            t = i / n
            a = (1 - t) ** 2; b = 2 * (1 - t) * t; c = t * t
            self.cur.append((a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1]))

    def _curveToOne(self, p1, p2, p3):
        p0 = self.cur[-1]
        n = max(3, int(math.dist(p0, p1) + math.dist(p1, p2) + math.dist(p2, p3)) // int(self.step))
        for i in range(1, n + 1):
            t = i / n; u = 1 - t
            self.cur.append((u**3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t**3 * p3[0],
                             u**3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t**3 * p3[1]))

    def _closePath(self):
        if self.cur and len(self.cur) >= 3:
            self.rings.append(self.cur)
        self.cur = None

    _endPath = _closePath


def glyph_polygon(glyphSet, name):
    pen = FlattenPen(glyphSet)
    glyphSet[name].draw(pen)
    polys = []
    for r in pen.rings:
        poly = Polygon(r).buffer(0)
        if not poly.is_empty and poly.area > 0:
            polys.append(poly)
    if not polys:
        return None
    # Even-odd by nesting depth: a ring inside an odd number of other rings is a counter.
    geom = None
    order = sorted(range(len(polys)), key=lambda k: -polys[k].area)
    for k in order:
        p = polys[k]
        depth = sum(1 for m in order if m != k and polys[m].area > p.area and polys[m].contains(p.representative_point()))
        if geom is None:
            geom = p
        elif depth % 2 == 0:
            geom = geom.union(p)
        else:
            geom = geom.difference(p)
    return geom


def nib_embolden(geom):
    L, a = P['nib'], math.radians(P['nib_angle'])
    dx, dy = math.cos(a), math.sin(a)
    parts = []
    n = P['nib_steps']
    for i in range(n + 1):
        t = -0.5 + i / n
        parts.append(affinity.translate(geom, t * L * dx, t * L * dy))
    g = unary_union(parts)
    # close the tiny stair-steps left by the discrete sweep, then grow slightly (mitred = keeps points sharp)
    g = g.buffer(3, join_style=2, mitre_limit=5).buffer(-3, join_style=2, mitre_limit=5)
    if P['base_grow']:
        g = g.buffer(P['base_grow'], join_style=2, mitre_limit=5)
    return g.simplify(1.5, preserve_topology=True)


def _arc_positions(pts):
    seg = np.linalg.norm(np.diff(pts, axis=0, append=pts[:1]), axis=1)
    s = np.concatenate([[0], np.cumsum(seg)[:-1]])
    return s, seg.sum()


def _point_at(pts, s_arr, total, s):
    s = s % total
    i = np.searchsorted(s_arr, s, side='right') - 1
    j = (i + 1) % len(pts)
    seglen = (s_arr[j] if j else total) - s_arr[i]
    t = 0 if seglen <= 1e-9 else (s - s_arr[i]) / seglen
    return pts[i] + (pts[j] - pts[i]) * t


def _angles(pts, s_arr, total, probe, ccw):
    n = len(pts)
    ang = np.full(n, 180.0); bis = np.zeros((n, 2))
    for i in range(n):
        p = pts[i]
        a = _point_at(pts, s_arr, total, s_arr[i] - probe) - p
        b = _point_at(pts, s_arr, total, s_arr[i] + probe) - p
        la, lb = np.linalg.norm(a), np.linalg.norm(b)
        if la < 1e-6 or lb < 1e-6:
            continue
        a /= la; b /= lb
        cross = a[0] * b[1] - a[1] * b[0]
        convex = (cross < 0) if ccw else (cross > 0)
        theta = math.degrees(math.acos(max(-1, min(1, float(a @ b)))))
        ang[i] = theta if convex else 360 - theta
        d = -(a + b); ln = np.linalg.norm(d)
        bis[i] = d / ln if ln > 1e-6 else 0
    return ang, bis


def _local_minima(vals, thresh, win):
    n = len(vals); out = []
    for i in range(n):
        if vals[i] >= thresh:
            continue
        if all(vals[i] <= vals[(i + k) % n] for k in range(-win, win + 1)):
            if not out or i - out[-1] > win:
                out.append(i)
    return out


def _bezier(p0, c, p2, steps=14):
    out = []
    for k in range(steps + 1):
        t = k / steps
        out.append(((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * c[0] + t * t * p2[0],
                    (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * c[1] + t * t * p2[1]))
    return out


def ring_thorns(coords, strength=1.0, seed=0, allow_tips=True):
    """Explicit thorn polygons for every flat stroke end (and very sharp tip) of an outer ring."""
    pts = np.array(coords[:-1], dtype=float)
    n = len(pts)
    if n < 4:
        return []
    dense = []
    for i in range(n):
        a, b = pts[i], pts[(i + 1) % n]
        k = max(1, int(np.linalg.norm(b - a) // 6))
        for t in range(k):
            dense.append(a + (b - a) * (t / k))
    pts = np.array(dense); n = len(pts)
    s_arr, total = _arc_positions(pts)
    if total < 150:
        return []
    ccw = Polygon(pts).exterior.is_ccw
    ang, bis = _angles(pts, s_arr, total, 9.0, ccw)
    corners = _local_minima(ang, P['corner_angle'], 2)
    thorns, used = [], set()
    idx = 0
    for k, i in enumerate(corners):
        if len(corners) < 2 or i in used:
            continue
        j = corners[(k + 1) % len(corners)]
        gap = (s_arr[j] - s_arr[i]) % total
        if not (0 < gap < P['cap_max'] and 140 <= ang[i] + ang[j] <= 230):
            continue
        d = bis[i] + bis[j]; ln = np.linalg.norm(d)
        if ln < 1e-6:
            continue
        d /= ln
        c1, c2 = pts[i], pts[j]
        m = (c1 + c2) / 2
        half = np.linalg.norm(c2 - c1) / 2
        L = P['tip_len'] * strength * (0.65 + 0.7 * TitleHash(seed * 131 + idx)) * min(1.0, 0.45 + half / 45.0)
        idx += 1
        tip = m + d * L
        # Fins on a baseline / top line stay flat on that side and sweep from the other.
        flat = None
        if abs(d[1]) < 0.6:
            for c in (c1, c2):
                other = c2 if c is c1 else c1
                if abs(c[1] - round(c[1] / 1.0)) < 1e9 and c[1] <= other[1] - 8 and abs(c[1]) < 20:
                    flat = c
                elif c[1] >= other[1] + 8 and c[1] > 900:
                    flat = c
        if flat is not None:
            tip = np.array([tip[0], flat[1]])
        inset = -d * 45
        ctrl1 = m + d * (L * 0.3) + (c1 - m) * 0.35
        ctrl2 = m + d * (L * 0.3) + (c2 - m) * 0.35
        side1 = [tuple(c1), tuple(tip)] if flat is not None and flat is c1 else _bezier(tuple(c1), tuple(ctrl1), tuple(tip))
        side2 = [tuple(tip), tuple(c2)] if flat is not None and flat is c2 else _bezier(tuple(tip), tuple(ctrl2), tuple(c2))
        poly = [tuple(c1 + inset)] + side1 + side2[1:] + [tuple(c2 + inset)]
        pg = Polygon(poly).buffer(0)
        if not pg.is_empty:
            thorns.append(pg)
        used.update((i, j))
    if allow_tips:
        for i in corners:
            if i in used or ang[i] >= P['tip_angle']:
                continue
            p = pts[i]
            a = _point_at(pts, s_arr, total, s_arr[i] - 40)
            b = _point_at(pts, s_arr, total, s_arr[i] + 40)
            L = P['tip_len'] * 0.6 * strength * (0.7 + 0.6 * TitleHash(seed * 17 + i))
            tip = p + bis[i] * L
            mid = (a + b) / 2
            poly = [tuple(a)] + _bezier(tuple(a), tuple(p + bis[i] * L * 0.3), tuple(tip))[1:] + \
                   _bezier(tuple(tip), tuple(p + bis[i] * L * 0.3), tuple(b))[1:] + [tuple(mid)]
            pg = Polygon(poly).buffer(0)
            if not pg.is_empty:
                thorns.append(pg)
    return thorns


def TitleHash(k):
    k = (k * 2654435761) & 0xFFFFFFFF
    k ^= k >> 15
    return (k & 0xFFFF) / 65535.0


def waist_ring(coords, is_hole, top, bottom):
    """Concave, flared stems: pull long straight vertical edges toward the stroke centre mid-height."""
    pts = np.array(coords[:-1], dtype=float)
    n0 = len(pts)
    if n0 < 4 or P['waist'] <= 0:
        return coords
    dense = []
    for i in range(n0):
        a, b = pts[i], pts[(i + 1) % n0]
        k = max(1, int(np.linalg.norm(b - a) // 8))
        for t in range(k):
            dense.append(a + (b - a) * (t / k))
    pts = np.array(dense); n = len(pts)
    s_arr, total = _arc_positions(pts)
    tang = np.roll(pts, -1, axis=0) - np.roll(pts, 1, axis=0)
    tl = np.linalg.norm(tang, axis=1); tl[tl < 1e-9] = 1
    tang /= tl[:, None]
    vertical = np.abs(tang[:, 1]) > math.cos(math.radians(14))
    # a vertex is on a stem if the edge stays vertical for +-stem_span along the ring
    span = int(P['stem_span'] // 8)
    stem = np.zeros(n, bool)
    for i in range(n):
        if vertical[i] and all(vertical[(i + k) % n] for k in range(-span, span + 1, 2)):
            stem[i] = True
    # smooth the mask so the pinch eases in
    sw = 2 * span
    w = np.convolve(np.concatenate([stem[-sw:], stem, stem[:sw]]).astype(float), np.ones(2 * sw + 1) / (2 * sw + 1), 'same')[sw:sw + n]
    w = np.clip(w * 1.6, 0, 1)
    ccw = Polygon(pts).exterior.is_ccw
    # inward normal of the ink: for a ccw ring the interior is on the left of the tangent
    left = np.stack([-tang[:, 1], tang[:, 0]], axis=1)
    inward = left if ccw else -left
    if is_hole:
        inward = -inward  # counters: ink is outside the ring
    y = pts[:, 1]
    prof = np.where(y >= 0, np.sin(np.pi * np.clip(y / max(top, 1), 0, 1)),
                    np.sin(np.pi * np.clip(y / min(bottom, -1), 0, 1)))
    out = pts + inward * (P['waist'] * w * prof)[:, None]
    return [tuple(p) for p in out] + [tuple(out[0])]


def waist(geom, top, bottom):
    polys = [geom] if isinstance(geom, Polygon) else list(geom.geoms)
    res = []
    for p in polys:
        newp = Polygon(waist_ring(list(p.exterior.coords), False, top, bottom)).buffer(0)
        for hole in p.interiors:
            newp = newp.difference(Polygon(waist_ring(list(hole.coords), True, top, bottom)).buffer(0))
        res.append(newp)
    return unary_union(res)


def thornify(geom, strength=1.0, seed=0, allow_tips=True):
    polys = [geom] if isinstance(geom, Polygon) else list(geom.geoms)
    extra = []
    for p in polys:
        extra += ring_thorns(list(p.exterior.coords), strength, seed, allow_tips)
    if not extra:
        return geom
    g = unary_union([geom] + extra)
    # re-open counters that a thorn may have bridged
    for p in polys:
        for hole in p.interiors:
            g = g.difference(Polygon(hole))
    return g


def crown(geom, scale, lean):
    if scale <= 0:
        return geom
    minx, miny, maxx, maxy = geom.bounds
    band = geom.intersection(Polygon([(minx - 10, maxy - 170), (maxx + 10, maxy - 170), (maxx + 10, maxy + 10), (minx - 10, maxy + 10)]))
    if band.is_empty:
        return geom
    parts = [band] if isinstance(band, Polygon) else list(band.geoms)
    top = max(parts, key=lambda g: g.bounds[3])
    bx0, by0, bx1, by1 = top.bounds
    xc = (bx0 + bx1) / 2
    half = min(95.0, max(60.0, (bx1 - bx0) * 0.4))
    H = P['crown_h'] * scale
    base_y = by1 - 150
    tip = (xc + lean, by1 + H)
    # needle thorn with concave sides (bows in toward the axis)
    pts = []
    for k in range(0, 17):
        t = k / 16
        p0, c = (xc - half, base_y), (xc - half * 0.12 + lean * 0.2, base_y + H * 0.22)
        pts.append(((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * c[0] + t * t * tip[0],
                    (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * c[1] + t * t * tip[1]))
    for k in range(1, 17):
        t = k / 16
        p2, c = (xc + half, base_y), (xc + half * 0.12 + lean * 0.2, base_y + H * 0.22)
        pts.append(((1 - t) ** 2 * tip[0] + 2 * (1 - t) * t * c[0] + t * t * p2[0],
                    (1 - t) ** 2 * tip[1] + 2 * (1 - t) * t * c[1] + t * t * p2[1]))
    pts.append((xc, base_y - 60))
    spike = Polygon(pts).buffer(0)
    return unary_union([geom, spike])


BARBS = set('BDEFHKLMNPR')


def barb(geom):
    """Small thorn pointing out-and-down from the left stem at mid height (logo 'R' / 'M' style)."""
    minx, miny, maxx, maxy = geom.bounds
    h = maxy - max(miny, 0)
    def left_edge(y):
        seg = geom.intersection(LineString([(minx - 50, y), (maxx + 50, y)]))
        if seg.is_empty:
            return None
        return seg.bounds[0]
    ys = [h * 0.38, h * 0.48, h * 0.58]
    xs = [left_edge(y) for y in ys]
    if any(x is None for x in xs) or max(xs) - min(xs) > 30:
        return geom
    x, y = xs[1], ys[1]
    L = P['barb_len']
    tip = (x - L, y - L * 0.45)
    base_top, base_bot = (x + 30, y + 45), (x + 30, y - 35)
    pts = [base_top, (x, y + 45)] + _bezier((x, y + 45), (x - L * 0.25, y + 5), tip)[1:] + \
          _bezier(tip, (x - L * 0.2, y - 25), (x, y - 35))[1:] + [base_bot]
    return unary_union([geom, Polygon(pts).buffer(0)])


def to_glyph(geom, glyphSet):
    pen = TTGlyphPen(glyphSet)
    polys = [] if geom is None or geom.is_empty else ([geom] if isinstance(geom, Polygon) else [g for g in geom.geoms if isinstance(g, Polygon)])
    for p in polys:
        p = p.simplify(P['simplify'], preserve_topology=True)
        if p.is_empty:
            continue
        rings = [(p.exterior, False)] + [(h, True) for h in p.interiors]
        for ring, is_hole in rings:
            c = list(ring.coords)[:-1]
            if len(c) < 3:
                continue
            ccw = ring.is_ccw
            # TrueType: ink clockwise, counters counter-clockwise
            if (not is_hole and ccw) or (is_hole and not ccw):
                c = c[::-1]
            c = [(int(round(x)), int(round(y))) for x, y in c]
            # drop consecutive duplicates after rounding
            cc = [c[0]] + [q for k, q in enumerate(c[1:], 1) if q != c[k - 1]]
            if len(cc) >= 3:
                pen.moveTo(cc[0])
                for q in cc[1:]:
                    pen.lineTo(q)
                pen.closePath()
    return pen.glyph()


def build(src, dst, family='Thornspire', only=None):
    font = TTFont(src)
    gs = font.getGlyphSet()
    cmap = font.getBestCmap()
    rev = {}
    for cp, g in cmap.items():
        rev.setdefault(g, cp)
    L, a = P['nib'], math.radians(P['nib_angle'])
    grow_x = L * math.cos(a) / 2 + P['base_grow']
    glyf, hmtx = font['glyf'], font['hmtx']
    new = {}
    names = font.getGlyphOrder() if only is None else only
    for name in names:
        adv, lsb = hmtx[name]
        geom = glyph_polygon(gs, name)
        if geom is None:
            new[name] = (None, adv + 2 * grow_x)
            continue
        cp = rev.get(name)
        cat = unicodedata.category(chr(cp)) if cp else 'Lo'
        geom = nib_embolden(geom)
        if cat[0] in 'LN':
            gb = geom.bounds
            geom = waist(geom, gb[3], gb[1])
        seed = sum(ord(c) for c in name)
        if cat[0] == 'L':
            geom = thornify(geom, 1.0, seed)
        elif cat[0] == 'N':
            geom = thornify(geom, 0.6, seed, allow_tips=False)
        elif cat[0] in 'SP':
            geom = thornify(geom, 0.35, seed, allow_tips=False)
        base = chr(cp) if cp else ''
        if base in BARBS:
            geom = barb(geom)
        if base in CROWNS:
            geom = crown(geom, *CROWNS[base])
        geom = affinity.translate(geom, grow_x, 0)
        new[name] = (geom, adv + 2 * grow_x)
    for name, (geom, adv) in new.items():
        g = to_glyph(geom, gs) if geom is not None else TTGlyphPen(gs).glyph()
        glyf[name] = g
        g.recalcBounds(glyf)
        hmtx[name] = (int(round(adv)), getattr(g, 'xMin', 0))
    # drop hinting (outlines changed)
    for t in ('fpgm', 'prep', 'cvt ', 'FFTM'):
        if t in font:
            del font[t]
    for name in font.getGlyphOrder():
        g = glyf[name]
        if hasattr(g, 'program'):
            from fontTools.ttLib.tables import ttProgram
            g.program = ttProgram.Program(); g.program.fromBytecode(b'')
    if 'gasp' in font:
        font['gasp'].gaspRange = {0xFFFF: 0x000F}
    # metrics / names
    font['OS/2'].usWeightClass = 400
    font['OS/2'].xAvgCharWidth = int(np.mean([hmtx[n][0] for n in font.getGlyphOrder() if hmtx[n][0] > 0]))
    ymax = max(getattr(glyf[n], 'yMax', 0) for n in font.getGlyphOrder())
    ymin = min(getattr(glyf[n], 'yMin', 0) for n in font.getGlyphOrder())
    font['OS/2'].usWinAscent = max(font['OS/2'].usWinAscent, ymax)
    font['OS/2'].usWinDescent = max(font['OS/2'].usWinDescent, -ymin)
    # hhea keeps the base line spacing; spikes may rise above it (win metrics cover them so nothing clips).
    name = font['name']
    copyright_ = ("Copyright (c) 2011-2012 by Sorkin Type Co (www.sorkintype.com), with Reserved Font Name "
                  "\"Metamorphous\". Thornspire modifications copyright (c) 2026 jon.")
    vals = {0: copyright_, 1: family, 2: 'Regular', 3: f'{family}-Regular;2026', 4: f'{family} Regular',
            5: 'Version 1.000', 6: f'{family}-Regular', 16: family, 17: 'Regular',
            13: 'This Font Software is licensed under the SIL Open Font License, Version 1.1. '
                'This license is available with a FAQ at: https://openfontlicense.org',
            14: 'https://openfontlicense.org'}
    name.names = [r for r in name.names if r.nameID not in vals and r.nameID not in (7, 8, 9, 10, 11, 12)]
    for nid, v in vals.items():
        name.setName(v, nid, 3, 1, 0x409)
        name.setName(v, nid, 1, 0, 0)
    font['head'].fontRevision = 1.0
    font['post'].underlineThickness = int(font['post'].underlineThickness * 1.5)
    font.save(dst)
    return dst


if __name__ == '__main__':
    out = build(os.path.join(HERE, 'Metamorphous-Regular.ttf'), os.path.join(HERE, '..', 'Thornspire-Regular.ttf'))
    print('wrote', out)
