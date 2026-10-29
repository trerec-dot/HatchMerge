import math, os
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Art") + "/"
os.makedirs(OUT, exist_ok=True)
INK = (59, 42, 74)

def hexc(c): return tuple(c)
def mix(a, b, t): return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))

# ---------------------------------------------------------------- eggs
PALETTE = [None, (246, 214, 168), (158, 216, 198), (142, 197, 242), (245, 167, 200),
           (199, 166, 242), (255, 201, 120), (255, 140, 120),
           (120, 215, 235), (255, 225, 110), (175, 230, 140), (230, 150, 240)]

def egg_r(nx, ny):
    k = 1.0 - 0.16 * ny
    ex = nx / (0.74 * k); ey = ny / 0.94
    return np.sqrt(ex * ex + ey * ey)

def make_egg(level, N=256, ss=3):
    M = N * ss
    ys, xs = np.mgrid[0:M, 0:M].astype(np.float32)
    nx = (xs + 0.5) / M * 2 - 1
    ny = 1 - (ys + 0.5) / M * 2
    r = egg_r(nx, ny)
    inside = np.clip((1 - r) * 60, 0, 1)
    base = np.array(PALETTE[level], np.float32) / 255
    # approximate ellipsoid normal
    nz = np.sqrt(np.clip(1 - r * r, 0, 1))
    L = np.array([-0.45, 0.55, 0.70]); L /= np.linalg.norm(L)
    gx = nx / (r + 1e-6) * (1 - nz); gy = ny / (r + 1e-6) * (1 - nz)
    lam = np.clip(gx * L[0] + gy * L[1] + nz * L[2], 0, 1)
    shade = 0.70 + 0.40 * lam
    col = base[None, None, :] * shade[..., None]
    # warm bounce light at the bottom
    bounce = np.clip((-ny - 0.45) * 2.0, 0, 1) * np.clip(r - 0.6, 0, 1) * 0.25
    col = col + bounce[..., None] * np.array([1.0, 0.95, 0.85])
    # rim darkening
    rim = np.clip((r - 0.86) * 7, 0, 1)
    col = col * (1 - 0.18 * rim[..., None])
    # specks
    dark = base * 0.62
    for (sx, sy, sr) in [(0.30, 0.10, 0.075), (0.18, -0.22, 0.055), (-0.20, -0.42, 0.05), (0.42, -0.08, 0.035), (-0.05, 0.30, 0.03)]:
        d = np.sqrt((nx - sx) ** 2 + (ny - sy) ** 2)
        a = np.clip((sr - d) * M / 3, 0, 1) * 0.55
        col = col * (1 - a[..., None]) + dark * a[..., None]
    # gloss
    gx2 = (nx + 0.30) / 0.15; gy2 = (ny - 0.40) / 0.24
    rot = math.radians(-25)
    u = gx2 * math.cos(rot) - gy2 * math.sin(rot); v = gx2 * math.sin(rot) + gy2 * math.cos(rot)
    gd = np.sqrt(u * u + v * v)
    ga = np.clip((1 - gd) * 3, 0, 1) * 0.75
    col = col * (1 - ga[..., None]) + ga[..., None]
    sp = np.sqrt(((nx + 0.12) / 0.05) ** 2 + ((ny - 0.62) / 0.05) ** 2)
    sa = np.clip((1 - sp) * 3, 0, 1) * 0.8
    col = col * (1 - sa[..., None]) + sa[..., None]
    rgba = np.dstack([np.clip(col, 0, 1), inside])
    img = Image.fromarray((rgba * 255).astype(np.uint8), "RGBA").resize((N, N), Image.LANCZOS)
    return img

for lv in range(1, len(PALETTE)):
    make_egg(lv).save(OUT + f"egg_{lv}.png")

# ---------------------------------------------------------------- helpers (PIL, supersampled)
S = 4
def layer(n): return Image.new("RGBA", (n, n), (0, 0, 0, 0))

def shape_mask(n, fn):
    m = Image.new("L", (n, n), 0); fn(ImageDraw.Draw(m)); return m

def paint(img, mask, color, alpha=255):
    c = Image.new("RGBA", img.size, tuple(color) + (alpha,))
    a = Image.new("L", img.size, 0); a.paste(mask, (0, 0))
    if alpha < 255:
        a = a.point(lambda p: p * alpha // 255)
    c.putalpha(a); img.alpha_composite(c)

def shaded(img, mask, color, light=(-0.45, -0.55), strength=0.32):
    """paint color, then add soft light/shadow gradient inside mask"""
    paint(img, mask, color)
    n = img.size[0]
    bbox = mask.getbbox()
    if not bbox: return
    x0, y0, x1, y1 = bbox
    ys, xs = np.mgrid[0:n, 0:n].astype(np.float32)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    w, h = max(1, (x1 - x0) / 2), max(1, (y1 - y0) / 2)
    u = (xs - cx) / w; v = (ys - cy) / h
    t = -(u * light[0] + v * light[1])  # >0 lit side
    m = np.asarray(mask, np.float32) / 255
    lit = np.clip(t, 0, 1) * m * strength
    shd = np.clip(-t, 0, 1) * m * strength * 0.9
    hi = np.zeros((n, n, 4), np.float32); hi[..., :3] = 255; hi[..., 3] = lit * 255
    lo = np.zeros((n, n, 4), np.float32); lo[..., :3] = np.array(INK) * 0.6; lo[..., 3] = shd * 255
    img.alpha_composite(Image.fromarray(hi.astype(np.uint8), "RGBA"))
    img.alpha_composite(Image.fromarray(lo.astype(np.uint8), "RGBA"))

def ell(cx, cy, rx, ry):
    return lambda d: d.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=255)

def poly(pts):
    return lambda d: d.polygon(pts, fill=255)

def rotated_ellipse_mask(n, cx, cy, rx, ry, deg):
    big = Image.new("L", (n, n), 0)
    ImageDraw.Draw(big).ellipse([n / 2 - rx, n / 2 - ry, n / 2 + rx, n / 2 + ry], fill=255)
    big = big.rotate(deg, resample=Image.BICUBIC)
    m = Image.new("L", (n, n), 0); m.paste(big, (int(cx - n / 2), int(cy - n / 2)))
    return m

def outline_of(mask, px):
    return mask.filter(ImageFilter.MaxFilter(px * 2 + 1))

def finish(img, N):
    return img.resize((N, N), Image.LANCZOS)

# ---------------------------------------------------------------- creatures
CREATURES = [
    ("cloud", (142, 197, 242)), ("dragon", (199, 166, 242)), ("jelly", (245, 167, 200)),
    ("fox", (255, 168, 100)), ("cat", (158, 216, 198)), ("owl", (255, 209, 102)),
    ("bunny", (250, 200, 215)), ("unicorn", (190, 160, 240)),
    ("penguin", (236, 240, 250)), ("hedgehog", (218, 182, 140)), ("panda", (246, 246, 250)), ("phoenix", (255, 150, 95)),
]
NAVY, CHAR, FLAME_R, FLAME_Y = (74, 86, 130), (66, 58, 80), (235, 85, 70), (255, 205, 90)

def creature(idx, N=256):
    kind, body = CREATURES[idx]
    n = N * S
    img = layer(n)
    dark = mix(body, INK, 0.28)
    light = mix(body, (255, 255, 255), 0.55)
    line = mix(body, INK, 0.55)
    cx, cy, R = n * 0.5, n * 0.58, n * 0.30
    back = []   # (mask, color) drawn behind body
    front = []  # drawn over body

    if kind == "cloud":
        for (dx, dy, rr) in [(-0.62, -0.55, 0.42), (0.0, -0.85, 0.5), (0.6, -0.6, 0.44), (-0.95, 0.0, 0.34), (0.95, 0.0, 0.34)]:
            back.append((shape_mask(n, ell(cx + dx * R, cy + dy * R, rr * R, rr * R)), body))
    elif kind == "dragon":
        for s in (-1, 1):
            back.append((shape_mask(n, poly([(cx + s * 0.38 * R, cy - 0.78 * R), (cx + s * 0.62 * R, cy - 1.32 * R), (cx + s * 0.72 * R, cy - 0.62 * R)])), light))
            back.append((shape_mask(n, poly([(cx + s * 0.85 * R, cy - 0.10 * R), (cx + s * 1.45 * R, cy - 0.55 * R), (cx + s * 1.30 * R, cy + 0.05 * R), (cx + s * 1.40 * R, cy + 0.30 * R), (cx + s * 0.95 * R, cy + 0.30 * R)])), dark))
        for i, dx in enumerate((-0.18, 0.0, 0.18)):
            back.append((shape_mask(n, poly([(cx + (dx - 0.1) * R, cy - 0.92 * R), (cx + dx * R, cy - 1.18 * R), (cx + (dx + 0.1) * R, cy - 0.92 * R)])), (255, 190, 120)))
    elif kind == "jelly":
        for i, dx in enumerate((-0.55, -0.18, 0.18, 0.55)):
            m = Image.new("L", (n, n), 0); d = ImageDraw.Draw(m)
            pts = []
            for k in range(40):
                t = k / 39
                pts.append((cx + dx * R + math.sin(t * 7 + i) * 0.09 * R, cy + 0.55 * R + t * 0.75 * R))
            d.line(pts, fill=255, width=int(0.16 * R), joint="curve")
            d.ellipse([pts[-1][0] - 0.08 * R, pts[-1][1] - 0.08 * R, pts[-1][0] + 0.08 * R, pts[-1][1] + 0.08 * R], fill=255)
            back.append((m, dark))
    elif kind in ("fox", "cat"):
        for s in (-1, 1):
            outer = [(cx + s * 0.25 * R, cy - 0.85 * R), (cx + s * 0.80 * R, cy - 1.35 * R if kind == "fox" else cy - 1.2 * R), (cx + s * 0.92 * R, cy - 0.35 * R)]
            inner = [(cx + s * 0.38 * R, cy - 0.80 * R), (cx + s * 0.76 * R, cy - 1.12 * R if kind == "fox" else cy - 1.0 * R), (cx + s * 0.82 * R, cy - 0.50 * R)]
            back.append((shape_mask(n, poly(outer)), body))
            back.append((shape_mask(n, poly(inner)), (90, 60, 70) if kind == "fox" else (245, 160, 185)))
    elif kind == "owl":
        for s in (-1, 1):
            back.append((shape_mask(n, poly([(cx + s * 0.35 * R, cy - 0.85 * R), (cx + s * 0.85 * R, cy - 1.25 * R), (cx + s * 0.85 * R, cy - 0.45 * R)])), dark))
    elif kind == "bunny":
        for s in (-1, 1):
            back.append((rotated_ellipse_mask(n, cx + s * 0.42 * R, cy - 1.15 * R, 0.24 * R, 0.62 * R, -s * 12), body))
            back.append((rotated_ellipse_mask(n, cx + s * 0.42 * R, cy - 1.12 * R, 0.12 * R, 0.45 * R, -s * 12), (245, 150, 180)))
    elif kind == "unicorn":
        for (dx, dy, rr, c) in [(-0.75, -0.65, 0.3, (255, 170, 210)), (-0.45, -0.92, 0.3, (150, 210, 255)), (0.5, -0.92, 0.28, (255, 225, 140)), (0.78, -0.62, 0.28, (255, 170, 210))]:
            back.append((shape_mask(n, ell(cx + dx * R, cy + dy * R, rr * R, rr * R)), c))
        for s in (-1, 1):
            back.append((shape_mask(n, poly([(cx + s * 0.35 * R, cy - 0.80 * R), (cx + s * 0.62 * R, cy - 1.18 * R), (cx + s * 0.80 * R, cy - 0.62 * R)])), body))

    elif kind == "penguin":
        back.append((shape_mask(n, ell(cx, cy - 0.10 * R, 1.06 * R, 1.02 * R)), NAVY))      # тёмная «шапочка» и спинка
        for s in (-1, 1):
            back.append((rotated_ellipse_mask(n, cx + s * 0.98 * R, cy + 0.22 * R, 0.16 * R, 0.42 * R, s * 25), NAVY))
            back.append((shape_mask(n, ell(cx + s * 0.34 * R, cy + 0.98 * R, 0.24 * R, 0.11 * R)), (255, 170, 70)))
    elif kind == "hedgehog":
        spike = mix(body, (90, 60, 45), 0.6)
        for k in range(11):
            a = math.radians(-200 + k * 22)
            bx, by = cx + math.cos(a) * 0.82 * R, cy + math.sin(a) * 0.80 * R
            tx, ty = cx + math.cos(a) * 1.32 * R, cy + math.sin(a) * 1.28 * R
            px, py = -math.sin(a) * 0.2 * R, math.cos(a) * 0.2 * R
            back.append((shape_mask(n, poly([(bx - px, by - py), (tx, ty), (bx + px, by + py)])), spike))
    elif kind == "panda":
        for s in (-1, 1):
            back.append((shape_mask(n, ell(cx + s * 0.66 * R, cy - 0.74 * R, 0.30 * R, 0.30 * R)), CHAR))
    elif kind == "phoenix":
        for (dx, h, w, c, rot) in [(-0.30, 1.30, 0.20, FLAME_R, 18), (0.30, 1.30, 0.20, FLAME_R, -18), (0.0, 1.50, 0.24, FLAME_Y, 0)]:
            back.append((rotated_ellipse_mask(n, cx + dx * R, cy - (h - 0.25) * R, w * R, 0.42 * R, rot), c))
        for s in (-1, 1):
            back.append((rotated_ellipse_mask(n, cx + s * 1.0 * R, cy + 0.05 * R, 0.18 * R, 0.46 * R, s * 40), FLAME_Y))

    # ground shadow
    sh = shape_mask(n, ell(cx, cy + R * 1.02, R * 0.8, R * 0.14)).filter(ImageFilter.GaussianBlur(n * 0.012))
    paint(img, sh, INK, 60)

    body_mask = shape_mask(n, ell(cx, cy, R * 1.0, R * 0.96))
    all_back = Image.new("L", (n, n), 0)
    for m, _ in back: all_back = Image.fromarray(np.maximum(np.asarray(all_back), np.asarray(m)))
    silhouette = Image.fromarray(np.maximum(np.asarray(all_back), np.asarray(body_mask)))
    paint(img, outline_of(silhouette, int(n * 0.012)), line)
    for m, c in back: shaded(img, m, c, strength=0.22)
    shaded(img, body_mask, body)

    # belly
    paint(img, shape_mask(n, ell(cx, cy + 0.42 * R, 0.52 * R, 0.40 * R)), (255, 255, 255), 105)

    # species details on the body
    if kind == "owl":
        for s in (-1, 1):
            paint(img, shape_mask(n, ell(cx + s * 0.34 * R, cy - 0.05 * R, 0.30 * R, 0.30 * R)), (255, 248, 230))
        paint(img, shape_mask(n, poly([(cx - 0.09 * R, cy + 0.12 * R), (cx + 0.09 * R, cy + 0.12 * R), (cx, cy + 0.30 * R)])), (240, 130, 60))
        # donut frosting band with sprinkles
        fm = shape_mask(n, lambda d: d.chord([cx - R, cy - R * 0.96, cx + R, cy + R * 0.96], 200, 340, fill=255))
        paint(img, fm, (245, 140, 175))
        rng = np.random.RandomState(7)
        for k in range(14):
            a = math.radians(205 + k * 9.5); rr = R * (0.62 + 0.18 * rng.rand())
            px, py = cx + math.cos(a) * rr, cy + math.sin(a) * rr * 0.96
            ang = rng.rand() * 180
            paint(img, rotated_ellipse_mask(n, px, py, 0.025 * R, 0.075 * R, ang), [(255, 255, 255), (120, 200, 255), (255, 230, 120), (140, 220, 170)][k % 4])
    if kind == "fox":
        for s in (-1, 1):
            paint(img, shape_mask(n, ell(cx + s * 0.30 * R, cy + 0.18 * R, 0.34 * R, 0.24 * R)), (255, 245, 235))
    if kind == "cat":
        for s in (-1, 1):
            for k, dy in enumerate((-0.04, 0.06)):
                m = Image.new("L", (n, n), 0)
                ImageDraw.Draw(m).line([(cx + s * 0.42 * R, cy + 0.12 * R + dy * R), (cx + s * 0.82 * R, cy + 0.06 * R + dy * 1.8 * R)], fill=255, width=int(0.025 * R))
                paint(img, m, line, 200)
        # yarn stripes on belly
        for k in range(3):
            m = Image.new("L", (n, n), 0)
            ImageDraw.Draw(m).arc([cx - 0.5 * R, cy + (0.15 + k * 0.12) * R, cx + 0.5 * R, cy + (0.75 + k * 0.12) * R], 200, 340, fill=255, width=int(0.035 * R))
            paint(img, m, dark, 90)
    if kind == "dragon":
        for s in (-1, 1):
            paint(img, shape_mask(n, ell(cx + s * 0.08 * R, cy + 0.20 * R, 0.025 * R, 0.02 * R)), line)
    if kind == "jelly":
        for (dx, dy, rr) in [(-0.45, -0.55, 0.08), (0.30, -0.70, 0.06), (0.55, -0.35, 0.05)]:
            paint(img, shape_mask(n, ell(cx + dx * R, cy + dy * R, rr * R, rr * R)), (255, 255, 255), 140)
    if kind == "unicorn":
        horn = [(cx - 0.12 * R, cy - 0.82 * R), (cx + 0.12 * R, cy - 0.82 * R), (cx, cy - 1.45 * R)]
        hm = shape_mask(n, poly(horn))
        paint(img, outline_of(hm, int(n * 0.008)), (200, 140, 40))
        shaded(img, hm, (255, 217, 115), strength=0.3)
        for k in range(3):
            y = cy - (0.98 + k * 0.15) * R
            m = Image.new("L", (n, n), 0)
            ImageDraw.Draw(m).line([(cx - 0.1 * R, y + 0.04 * R), (cx + 0.1 * R, y - 0.04 * R)], fill=255, width=int(0.03 * R))
            paint(img, m, (220, 160, 60), 200)

    if kind == "penguin":
        paint(img, shape_mask(n, poly([(cx - 0.12 * R, cy + 0.10 * R), (cx + 0.12 * R, cy + 0.10 * R), (cx, cy + 0.30 * R)])), (255, 170, 70))
    if kind == "hedgehog":
        paint(img, shape_mask(n, ell(cx, cy + 0.12 * R, 0.62 * R, 0.52 * R)), (250, 232, 205))
        paint(img, shape_mask(n, ell(cx, cy + 0.12 * R, 0.07 * R, 0.055 * R)), INK)
    if kind == "panda":
        for s in (-1, 1):
            paint(img, rotated_ellipse_mask(n, cx + s * 0.34 * R, cy - 0.02 * R, 0.19 * R, 0.25 * R, -s * 25), CHAR)
            paint(img, shape_mask(n, ell(cx + s * 0.62 * R, cy + 0.62 * R, 0.24 * R, 0.30 * R)), CHAR)
    if kind == "phoenix":
        paint(img, shape_mask(n, poly([(cx - 0.11 * R, cy + 0.10 * R), (cx + 0.11 * R, cy + 0.10 * R), (cx, cy + 0.28 * R)])), (255, 200, 80))
        for (dx, dy, rr) in [(-0.6, -0.5, 0.05), (0.55, -0.6, 0.04), (0.2, -0.75, 0.035)]:
            paint(img, shape_mask(n, ell(cx + dx * R, cy + dy * R, rr * R, rr * R)), (255, 240, 170), 180)

    # face
    ey = cy - 0.02 * R
    for s in (-1, 1):
        ex = cx + s * 0.34 * R
        paint(img, shape_mask(n, ell(ex, ey, 0.11 * R, 0.14 * R)), INK)
        paint(img, shape_mask(n, ell(ex - 0.035 * R, ey - 0.05 * R, 0.04 * R, 0.045 * R)), (255, 255, 255))
        paint(img, shape_mask(n, ell(ex + 0.04 * R, ey + 0.05 * R, 0.018 * R, 0.018 * R)), (255, 255, 255), 200)
        paint(img, shape_mask(n, ell(cx + s * 0.58 * R, cy + 0.20 * R, 0.13 * R, 0.08 * R)), (245, 120, 150), 120)
    m = Image.new("L", (n, n), 0)
    ImageDraw.Draw(m).arc([cx - 0.13 * R, cy + 0.02 * R, cx + 0.13 * R, cy + 0.26 * R], 20, 160, fill=255, width=int(0.045 * R))
    if kind not in ("owl", "penguin", "phoenix"): paint(img, m, INK)
    return finish(img, N)

for i in range(len(CREATURES)):
    creature(i).save(OUT + f"creature_{i}.png")

# ---------------------------------------------------------------- UI bits
def coin(N=128):
    n = N * S; img = layer(n); c = n / 2
    paint(img, shape_mask(n, ell(c, c + n * 0.03, n * 0.46, n * 0.46)), (200, 130, 20))
    shaded(img, shape_mask(n, ell(c, c, n * 0.46, n * 0.46)), (247, 205, 80), strength=0.35)
    shaded(img, shape_mask(n, ell(c, c, n * 0.34, n * 0.34)), (242, 176, 40), light=(0.45, 0.55), strength=0.25)
    # star
    pts = []
    for k in range(10):
        a = -math.pi / 2 + k * math.pi / 5; r = n * (0.20 if k % 2 == 0 else 0.09)
        pts.append((c + math.cos(a) * r, c + math.sin(a) * r))
    paint(img, shape_mask(n, poly(pts)), (255, 236, 160))
    paint(img, rotated_ellipse_mask(n, c - n * 0.2, c - n * 0.22, n * 0.08, n * 0.04, 40), (255, 255, 255), 170)
    return finish(img, N)

def gem(N=128):
    n = N * S; img = layer(n); c = n / 2
    top, mid, bot = c - n * 0.40, c - n * 0.12, c + n * 0.42
    l, r = c - n * 0.44, c + n * 0.44
    il, ir = c - n * 0.22, c + n * 0.22
    full = [(il, top), (ir, top), (r, mid), (c, bot), (l, mid)]
    paint(img, outline_of(shape_mask(n, poly(full)), int(n * 0.012)), (40, 110, 170))
    facets = [
        ([(il, top), (ir, top), (c + n * 0.12, mid), (c - n * 0.12, mid)], (150, 220, 250)),
        ([(il, top), (c - n * 0.12, mid), (l, mid)], (110, 200, 245)),
        ([(ir, top), (r, mid), (c + n * 0.12, mid)], (80, 170, 230)),
        ([(l, mid), (c - n * 0.12, mid), (c, bot)], (90, 185, 238)),
        ([(c - n * 0.12, mid), (c + n * 0.12, mid), (c, bot)], (70, 160, 225)),
        ([(c + n * 0.12, mid), (r, mid), (c, bot)], (50, 140, 210)),
    ]
    for pts, col in facets: paint(img, shape_mask(n, poly(pts)), col)
    paint(img, rotated_ellipse_mask(n, c - n * 0.13, c - n * 0.26, n * 0.07, n * 0.035, 20), (255, 255, 255), 220)
    return finish(img, N)

def panel(N=128, rad=48):  # 9-slice rounded rect, white
    ys, xs = np.mgrid[0:N, 0:N].astype(np.float32)
    qx = np.abs(xs + 0.5 - N / 2) - (N / 2 - rad); qy = np.abs(ys + 0.5 - N / 2) - (N / 2 - rad)
    d = np.sqrt(np.maximum(qx, 0) ** 2 + np.maximum(qy, 0) ** 2) + np.minimum(np.maximum(qx, qy), 0) - rad
    a = np.clip(0.5 - d, 0, 1)
    rgba = np.dstack([np.ones_like(a), np.ones_like(a), np.ones_like(a), a])
    return Image.fromarray((rgba * 255).astype(np.uint8), "RGBA")

def circle(N=64):
    ys, xs = np.mgrid[0:N, 0:N].astype(np.float32)
    r = np.sqrt(((xs + 0.5) / N * 2 - 1) ** 2 + ((ys + 0.5) / N * 2 - 1) ** 2)
    a = np.clip((1 - r) * N / 2, 0, 1)
    return Image.fromarray((np.dstack([np.ones_like(a)] * 3 + [a]) * 255).astype(np.uint8), "RGBA")

def ring(N=256, ss=3):
    M = N * ss
    ys, xs = np.mgrid[0:M, 0:M].astype(np.float32)
    nx = (xs + 0.5) / M * 2 - 1; ny = 1 - (ys + 0.5) / M * 2
    r = egg_r(nx, ny)
    ang = np.arctan2(ny, nx) / (2 * np.pi)
    dash = (np.mod(ang * 16, 1) < 0.58).astype(np.float32)
    a = np.clip((0.045 - np.abs(r - 0.9)) * M / 3, 0, 1) * dash
    im = Image.fromarray((np.dstack([np.ones_like(a)] * 3 + [a]) * 255).astype(np.uint8), "RGBA")
    return im.resize((N, N), Image.LANCZOS)

def star(N=64):
    n = N * S; img = layer(n); c = n / 2
    pts = []
    for k in range(8):
        a = -math.pi / 2 + k * math.pi / 4; r = n * (0.48 if k % 2 == 0 else 0.13)
        pts.append((c + math.cos(a) * r, c + math.sin(a) * r))
    paint(img, shape_mask(n, poly(pts)), (255, 255, 255))
    return finish(img, N)

coin().save(OUT + "coin.png")
gem().save(OUT + "gem.png")
panel().save(OUT + "panel.png")
circle().save(OUT + "circle.png")
ring().save(OUT + "ring.png")
star().save(OUT + "star.png")

# ---------------------------------------------------------------- app icon
def icon(N=1024):
    ys, xs = np.mgrid[0:N, 0:N].astype(np.float32)
    t = (ys / N)[..., None]
    top = np.array([255, 236, 205], np.float32); bot = np.array([255, 205, 160], np.float32)
    bg = top * (1 - t) + bot * t
    d = np.sqrt((xs - N * 0.5) ** 2 + (ys - N * 0.45) ** 2) / N
    glow = np.clip(0.55 - d, 0, 1)[..., None] * 120
    bg = np.clip(bg + glow, 0, 255)
    img = Image.fromarray(np.dstack([bg, np.full((N, N), 255, np.float32)]).astype(np.uint8), "RGBA")
    # nest
    nest = layer(N)
    ImageDraw.Draw(nest).ellipse([N * 0.18, N * 0.66, N * 0.82, N * 0.86], fill=(196, 140, 80, 255))
    for k in range(18):
        a0 = 190 + k * 9
        ImageDraw.Draw(nest).arc([N * 0.17, N * 0.62 + (k % 3) * 8, N * 0.83, N * 0.88], a0, a0 + 40, fill=(150, 100, 50, 255), width=10)
    img.alpha_composite(nest)
    egg = make_egg(5, N=600, ss=2)
    # crack zigzag
    dr = ImageDraw.Draw(egg)
    pts = [(120, 250), (190, 215), (240, 270), (300, 220), (360, 275), (420, 225), (480, 260)]
    dr.line(pts, fill=(90, 60, 120, 255), width=14, joint="curve")
    img.alpha_composite(egg, (int(N * 0.5 - 300), int(N * 0.40 - 300)))
    # peeking eyes
    for s in (-1, 1):
        ex, ey = N * 0.5 + s * 70, N * 0.30
        ImageDraw.Draw(img).ellipse([ex - 26, ey - 32, ex + 26, ey + 32], fill=INK + (255,))
        ImageDraw.Draw(img).ellipse([ex - 14, ey - 22, ex + 2, ey - 6], fill=(255, 255, 255, 255))
    # sparkles
    sp = star(160)
    for (x, y, s) in [(0.16, 0.18, 1.0), (0.82, 0.24, 0.8), (0.86, 0.60, 0.6), (0.12, 0.55, 0.55)]:
        st = sp.resize((int(160 * s), int(160 * s)), Image.LANCZOS)
        img.alpha_composite(st, (int(N * x - st.size[0] / 2), int(N * y - st.size[1] / 2)))
    return img.convert("RGB")

icon().save(OUT + "app_icon.png")

# ---------------------------------------------------------------- preview sheet
sheet = Image.new("RGBA", (8 * 140, 3 * 150), (255, 243, 224, 255))
for i in range(1, 8): sheet.alpha_composite(make_egg(i, 128, 2), (10 + (i - 1) * 140, 10))
for i in range(8): sheet.alpha_composite(Image.open(OUT + f"creature_{i}.png").resize((136, 136), Image.LANCZOS), (2 + i * 140, 150))
for j, nm in enumerate(["coin", "gem", "star", "ring"]):
    im = Image.open(OUT + nm + ".png").convert("RGBA").resize((100, 100), Image.LANCZOS)
    if nm in ("star", "ring"):
        tint = Image.new("RGBA", im.size, (201, 60, 32, 255)); tint.putalpha(im.getchannel("A")); im = tint
    sheet.alpha_composite(im, (20 + j * 140, 320))
sheet.alpha_composite(Image.open(OUT + "app_icon.png").convert("RGBA").resize((130, 130)), (8 * 140 - 150, 310))
sheet.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "preview_sheet.png"))
print("done")
