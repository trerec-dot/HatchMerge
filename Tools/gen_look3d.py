# Собирает строки 3D-моделей (Look3D в MergeGame.Gallery.cs) для 30 зверюшек из creatures_extra.py.
# Запуск: python Tools/gen_look3d.py  → печатает C#-строки, их вставляют в конец массива Look3D.
import math, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from creatures_extra import EXTRA

BC, BA = (0.0, -0.08, 0.0), (0.8, 0.78, 0.71)   # центр и полуоси тела


def surf(dx, dy, dz, k=1.0, c=BC, a=BA):
    """Точка на поверхности эллипсоида по направлению (k>1 — чуть снаружи)."""
    t = 1 / math.sqrt((dx / a[0]) ** 2 + (dy / a[1]) ** 2 + (dz / a[2]) ** 2)
    return (c[0] + dx * t * k, c[1] + dy * t * k, c[2] + dz * t * k)


def B(col, p, s, r=0):
    return "%s %s %s %s %s %s %s %s" % ((col,) + tuple(round(v, 3) for v in p) + tuple(round(v, 3) for v in s) + (round(r),))


SPOTS = [(-0.7, 0.5, -0.35), (0.62, 0.62, -0.25), (0.9, -0.1, 0.25), (-0.85, -0.15, 0.35), (0.05, 0.85, 0.45),
         (0.35, 0.25, 0.9), (-0.4, 0.45, 0.8), (0.1, -0.3, 1.0)]


def look(feats):
    f = set(feats.split())
    out, feet = [], not f & {"tentacles", "flippers", "tail_fluke", "shell_spiral"}
    for s in (-1, 1):
        if "ears_round" in f:
            out += [B("D", (0.56 * s, 0.68, 0.05), (0.42, 0.42, 0.26)), B("L", (0.58 * s, 0.68, -0.07), (0.22, 0.22, 0.06))]
        if "ears_mouse" in f:
            out += [B("B", (0.66 * s, 0.66, 0.08), (0.66, 0.66, 0.2)), B("P", (0.67 * s, 0.66, -0.03), (0.42, 0.42, 0.06))]
        if "ears_big" in f:
            out += [B("B", (0.92 * s, 0.24, 0.12), (0.82, 0.82, 0.2)), B("L", (0.94 * s, 0.24, 0.01), (0.52, 0.52, 0.06))]
        if "ears_pointy" in f or "ears_tuft" in f:
            out += [B("B", (0.52 * s, 0.72, 0.05), (0.36, 0.6, 0.24), -25 * s),
                    B("P" if "ears_pointy" in f else "L", (0.52 * s, 0.7, -0.08), (0.18, 0.36, 0.06), -25 * s)]
            if "ears_tuft" in f:
                out.append(B("A", (0.66 * s, 1.03, 0.05), (0.07, 0.26, 0.07), -25 * s))
        if "ears_long" in f:
            out += [B("B", (0.32 * s, 1.0, 0.05), (0.3, 0.84, 0.24), -6 * s), B("L", (0.32 * s, 1.0, -0.07), (0.15, 0.58, 0.06), -6 * s)]
        if "ears_floppy" in f:
            out.append(B("D", (0.78 * s, 0.22, 0.0), (0.3, 0.7, 0.24), 18 * s))
        if "antlers" in f:
            out += [B("G", (0.36 * s, 0.98, 0.1), (0.1, 0.6, 0.1), -18 * s), B("G", (0.56 * s, 1.1, 0.1), (0.08, 0.32, 0.08), -62 * s),
                    B("G", (0.36 * s, 1.3, 0.1), (0.08, 0.24, 0.08), 12 * s)]
        if "antenna" in f:
            out += [B("A", (0.3 * s, 0.88, 0.05), (0.06, 0.5, 0.06), -22 * s), B("A", (0.4 * s, 1.13, 0.05), (0.16, 0.16, 0.16))]
        if "eyebumps" in f:
            out.append(B("B", (0.34 * s, 0.52, -0.08), (0.52, 0.46, 0.46)))
        if "claws" in f:
            out += [B("B", (0.98 * s, 0.05, -0.1), (0.44, 0.42, 0.42)), B("L", (1.1 * s, 0.32, -0.1), (0.17, 0.38, 0.17), -25 * s)]
        if "wings_small" in f:
            out.append(B("L", (0.84 * s, -0.12, 0.08), (0.24, 0.66, 0.36), 20 * s))
        if "wings_bat" in f:
            out += [B("D", (1.08 * s, 0.28, 0.2), (0.9, 0.48, 0.08), 25 * s), B("D", (1.02 * s, -0.02, 0.2), (0.7, 0.36, 0.08), -12 * s)]
        if "flippers" in f:
            out.append(B("D", (0.78 * s, -0.62, -0.12), (0.5, 0.2, 0.32), 25 * s))
        if "whiskers" in f:
            for dy, r in ((0.0, 8), (-0.1, -6)):
                out.append(B("A", (0.56 * s, -0.08 + dy, -0.6), (0.34, 0.025, 0.025), r * s))
        if "mask" in f:
            out.append(B("A", (0.3 * s, 0.09, -0.6), (0.5, 0.32, 0.11)))
    if "mask" in f:
        out.append(B("A", (0, 0.12, -0.63), (0.3, 0.18, 0.1)))
    if "tentacles" in f:
        for x, r in ((-0.54, 14), (-0.18, 4), (0.18, -4), (0.54, -14)):
            out.append(B("D", (x, -0.9, -0.05 + abs(x) * 0.2), (0.24, 0.66, 0.24), r))
    if "shell" in f:
        out.append(B("D", (0, 0.04, 0.3), (1.56, 1.42, 1.06)))
        for d in ((0, 0.75, 0.5), (-0.65, 0.4, 0.6), (0.65, 0.4, 0.6), (0, 0.15, 1), (-0.8, -0.2, 0.5), (0.8, -0.2, 0.5)):
            out.append(B("L", surf(*d, c=(0, 0.04, 0.3), a=(0.78, 0.71, 0.53)), (0.32, 0.28, 0.12)))
    if "shell_spiral" in f:
        out += [B("H", (0, 0.24, 0.6), (1.2, 1.2, 0.9)), B("D", (0, 0.24, 1.0), (0.6, 0.6, 0.22)), B("H", (0, 0.24, 1.08), (0.3, 0.3, 0.14))]
    if "tail_big" in f:
        out += [B("B", (0.2, 0.28, 0.92), (0.72, 1.5, 0.5), -14), B("L", (0.32, 0.8, 1.02), (0.4, 0.5, 0.36), -14)]
    if "tail_striped" in f:
        for k in range(5):
            t = k / 4
            out.append(B("B" if k % 2 == 0 else "A", (0.48 + 0.42 * t, -0.62 + 0.4 * t, 0.58 + 0.18 * t), (0.28, 0.28, 0.28)))
    if "tail_fluke" in f:
        out += [B("B", (0, -0.1, 0.86), (0.34, 0.4, 0.5)), B("B", (-0.24, 0.12, 1.06), (0.52, 0.18, 0.3), 30),
                B("B", (0.24, 0.12, 1.06), (0.52, 0.18, 0.3), -30)]
    if "spikes" in f:
        for a in (70, 40, 10, -20, -50):
            ra = math.radians(a)
            out.append(B("D", surf(0, math.sin(ra), math.cos(ra) * 0.9 + 0.1 if a > 60 else math.cos(ra)), (0.12, 0.38, 0.38)))
    if "wool" in f:
        dirs = [(math.cos(math.radians(a)) * math.cos(math.radians(e)), math.sin(math.radians(e)),
                 math.sin(math.radians(a)) * math.cos(math.radians(e)))
                for e, n, z0 in ((60, 5, 0), (25, 8, 15), (-10, 8, 0), (-40, 6, 25)) for a in [z0 + i * 360 / n for i in range(n)]]
        for d in dirs:
            if d[2] < -0.55 and d[1] < 0.75:        # не закрывать мордочку
                continue
            out.append(B("W", surf(*d, k=0.92), (0.52, 0.5, 0.5)))
    if "cap" in f:
        out.append(B("M", (0, 0.62, 0.05), (1.96, 0.84, 1.76)))
        for d in ((-0.5, 0.7, -0.5), (0.45, 0.8, -0.4), (0, 1, 0), (-0.6, 0.6, 0.5), (0.6, 0.6, 0.5), (0.1, 0.5, -0.85)):
            out.append(B("W", surf(*d, c=(0, 0.62, 0.05), a=(0.98, 0.42, 0.88)), (0.3, 0.14, 0.28)))
    if "fluff_top" in f:
        out += [B("L", (-0.2, 0.74, 0.0), (0.36, 0.36, 0.36)), B("L", (0.2, 0.74, 0.0), (0.36, 0.36, 0.36)), B("L", (0, 0.84, 0.06), (0.4, 0.4, 0.4))]
    if "stripes" in f:
        for x in (-0.2, 0.0, 0.2):
            out.append(B("D", surf(x, 0.8, -0.5), (0.08, 0.26, 0.1), -x * 60))
        for s in (-1, 1):
            for y in (0.15, -0.25):
                out.append(B("D", surf(s, y, -0.25), (0.1, 0.08, 0.32), 0))
    for key, col in (("spots_dark", "A"), ("spots_light", "W")):
        if key in f:
            for d in SPOTS:
                out.append(B(col, surf(*d), (0.22, 0.22, 0.22)))
    if "muzzle" in f:
        out += [B("L", (0, -0.16, -0.63), (0.58, 0.36, 0.22)), B("K", (0, -0.12, -0.75), (0.12, 0.05, 0.04))]
    if "nose" in f:
        out.append(B("K", (0, 0.0, -0.77 if "muzzle" in f else -0.72), (0.13, 0.09, 0.08)))
    if "bignose" in f:
        out.append(B("A", (0, -0.03, -0.72), (0.28, 0.21, 0.14)))
    if "snout" in f:
        out += [B("P", (0, -0.1, -0.7), (0.38, 0.26, 0.2)), B("A", (-0.07, -0.1, -0.8), (0.05, 0.08, 0.04)), B("A", (0.07, -0.1, -0.8), (0.05, 0.08, 0.04))]
    if "trunk" in f:
        out += [B("B", (0, -0.2, -0.76), (0.22, 0.46, 0.22)), B("B", (0.04, -0.44, -0.84), (0.2, 0.24, 0.2))]
    if "beak" in f:
        out.append(B("O", (0, -0.05, -0.72), (0.2, 0.13, 0.2)))
    return "; ".join(out) + (";-" if not feet else "")


if __name__ == "__main__":
    for i, (name, col, egg, feats) in enumerate(EXTRA):
        s = look(feats)
        print("        // %d %s: %s" % (12 + i, name.split("-")[0].lower(), feats.replace(" ", ", ")))
        print('        "%s",' % s)
