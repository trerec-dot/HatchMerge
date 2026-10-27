import numpy as np, wave, os
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Resources", "Audio") + "/"
os.makedirs(OUT, exist_ok=True)
SR = 44100
rng = np.random.RandomState(3)

def hz(m): return 440.0 * 2 ** ((m - 69) / 12)
def t_(dur): return np.arange(int(dur * SR)) / SR

def save(name, x, stereo=False):
    x = np.asarray(x, np.float64)
    peak = np.max(np.abs(x)) + 1e-9
    x = x / peak * 0.89
    if not stereo: x = x[:, None] if x.ndim == 1 else x
    data = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(OUT + name + ".wav", "wb") as w:
        w.setnchannels(data.shape[1]); w.setsampwidth(2); w.setframerate(SR); w.writeframes(data.tobytes())

def env(n, a=0.004, d=0.2, shape=5.0):
    t = np.arange(n) / SR
    return np.minimum(1, t / max(a, 1e-4)) * np.exp(-shape * t / d)

def bell(f, dur, amp=1.0, bright=0.35):
    t = t_(dur)
    s = np.sin(2*np.pi*f*t) + bright*np.sin(2*np.pi*2*f*t)*np.exp(-6*t/dur) + 0.12*np.sin(2*np.pi*3.01*f*t)*np.exp(-9*t/dur)
    return amp * s * env(len(t), 0.003, dur)

def pluck(f, dur, amp=1.0):
    t = t_(dur)
    s = np.sin(2*np.pi*f*t) + 0.5*np.sin(2*np.pi*2*f*t)*np.exp(-10*t) + 0.25*np.sin(2*np.pi*3*f*t)*np.exp(-16*t)
    return amp * s * env(len(t), 0.004, dur, 4.0)

def sweep(f0, f1, dur, amp=1.0, shape=4.0):
    t = t_(dur)
    ph = 2*np.pi*(f0*t + (f1-f0)*t*t/(2*dur))
    return amp * (np.sin(ph) + 0.2*np.sin(2*ph)) * env(len(t), 0.003, dur, shape)

def noise(dur, amp=1.0, lp=0.0):
    x = rng.randn(int(dur*SR))
    if lp > 0:
        y = np.zeros_like(x); a = lp
        for i in range(1, len(x)): y[i] = y[i-1] + a*(x[i]-y[i-1])
        x = y
    return amp * x

def place(buf, sig, t0):
    i = int(t0*SR); j = min(len(buf), i+len(sig))
    if i < len(buf): buf[i:j] += sig[:j-i]

def blank(dur): return np.zeros(int(dur*SR))

# ---------------- SFX
save("click", sweep(1700, 1100, 0.045, 1) * 0.6)
b = blank(0.14); place(b, sweep(500, 900, 0.10), 0); place(b, noise(0.03, 0.15)*env(int(0.03*SR), 0.001, 0.03), 0); save("pick", b)
b = blank(0.18); place(b, sweep(300, 120, 0.14, shape=5), 0); place(b, noise(0.05, 0.4, 0.08)*env(int(0.05*SR), 0.001, 0.05), 0); save("drop", b)
b = blank(0.26)  # bubbly pop
place(b, sweep(260, 1100, 0.16, shape=3), 0); place(b, bell(1500, 0.12, 0.25), 0.07); save("spawn", b)
b = blank(0.32); place(b, sweep(240, 170, 0.14, shape=2), 0); place(b, sweep(200, 140, 0.16, shape=2), 0.13); save("deny", b)
b = blank(0.6); place(b, bell(1318.5, 0.35, 0.8), 0); place(b, bell(1975.5, 0.45, 0.9), 0.08); place(b, noise(0.08, 0.08)*env(int(0.08*SR), 0.001, 0.08), 0); save("buy", b)
b = blank(1.0)
for k, m in enumerate([76, 72, 67, 64]): place(b, bell(hz(m), 0.4, 0.8), k*0.13)
save("stuck", b)

for lvl in range(2, 8):  # merge chimes: higher with level
    base = 60 + lvl*2
    b = blank(0.75)
    place(b, sweep(300 + lvl*40, 900 + lvl*80, 0.08, 0.5), 0)            # whoosh-pop
    for k, iv in enumerate([0, 4, 7, 12]): place(b, bell(hz(base+iv), 0.45, 0.6 if k < 3 else 0.5), 0.05 + k*0.055)
    for k in range(lvl):                                                     # sparkle
        place(b, bell(hz(base+24+rng.randint(0, 7)), 0.12, 0.15), 0.12 + rng.rand()*0.4)
    save(f"merge_{lvl}", b)

b = blank(2.0)  # hatch: crack + fanfare
for k in range(3):
    cr = noise(0.035, 0.9, 0.35) * env(int(0.035*SR), 0.0005, 0.035)
    place(b, cr, k*0.09)
place(b, sweep(200, 1400, 0.25, 0.6, 2), 0.28)
for k, m in enumerate([72, 76, 79, 84]): place(b, bell(hz(m), 0.5, 0.7), 0.42 + k*0.09)
for m in [84, 88, 91]: place(b, bell(hz(m), 1.2, 0.45), 0.80)
for k in range(10): place(b, bell(hz(96 + rng.randint(0, 8)), 0.15, 0.12), 0.85 + rng.rand()*0.8)
save("hatch", b)

# ---------------- MUSIC: seamless loop, 108 bpm, 16 bars, stereo
BPM = 108; beat = 60 / BPM; bars = 16
L = int(bars * 4 * beat * SR)
left = np.zeros(L); right = np.zeros(L)

def put(sig, t0, pan=0.0, amp=1.0):
    i = int(t0 * SR); n = len(sig)
    idx = (np.arange(n) + i) % L          # wrap tail -> seamless loop
    gl = amp * np.sqrt((1 - pan) / 2); gr = amp * np.sqrt((1 + pan) / 2)
    np.add.at(left, idx, sig * gl); np.add.at(right, idx, sig * gr)

# progression (C major, cheerful): C G Am F | C G F G
prog = [(48, [60, 64, 67]), (43, [59, 62, 67]), (45, [57, 60, 64]), (41, [57, 60, 65]),
        (48, [60, 64, 67]), (43, [59, 62, 67]), (41, [57, 60, 65]), (43, [59, 62, 67])]

def pad(notes, dur):
    t = t_(dur); s = np.zeros_like(t)
    for m in notes:
        f = hz(m)
        s += np.sin(2*np.pi*f*t + 0.3*np.sin(2*np.pi*0.3*t)) + 0.15*np.sin(2*np.pi*2.003*f*t)
    a = np.minimum(1, t/0.25) * np.minimum(1, (dur - t)/0.3)
    return s * np.clip(a, 0, 1)

def kick():
    t = t_(0.18); return np.sin(2*np.pi*(60*t + 90*(1-np.exp(-30*t))/30)) * np.exp(-18*t)

def hat():
    return noise(0.04, 1.0) * env(int(0.04*SR), 0.0005, 0.04, 6) * 0.5

def shaker():
    x = noise(0.07, 1.0); x = x - np.convolve(x, np.ones(6)/6, "same")
    return x * env(len(x), 0.01, 0.07, 3)

melA = [(0, 76, 1), (1, 79, 1), (2, 81, .5), (2.5, 79, .5), (3, 76, 1),
        (4, 74, 1), (5, 76, .5), (5.5, 74, .5), (6, 71, 1.5),
        (8, 72, 1), (9, 76, 1), (10, 79, 1), (11, 81, 1),
        (12, 79, .5), (12.5, 77, .5), (13, 76, .5), (13.5, 74, .5), (14, 76, 2)]
melB = [(0, 84, 1), (1, 83, .5), (1.5, 81, .5), (2, 79, 2),
        (4, 79, 1), (5, 81, .5), (5.5, 79, .5), (6, 74, 2),
        (8, 77, 1), (9, 76, .5), (9.5, 74, .5), (10, 72, 1), (11, 74, 1),
        (12, 76, 1), (13, 74, 1), (14, 79, 2)]

for bar in range(bars):
    t0 = bar * 4 * beat
    root, chord = prog[bar % 8]
    section = bar // 8
    put(pad(chord, 4*beat), t0, 0.0, 0.05)
    # bass: root on 1, fifth on 3, bounce
    for (bt, iv, du) in [(0, 0, 1.5), (1.5, 0, .5), (2, 7, 1.0), (3, 12, .5), (3.5, 7, .5)]:
        put(pluck(hz(root + iv - 12 + 12), du*beat*1.1, 1.0), t0 + bt*beat, 0.0, 0.22)
    # arpeggio (marimba-ish)
    arp = [0, 1, 2, 1, 2, 0, 1, 2]
    for k in range(8):
        m = chord[arp[k]] + 12
        put(bell(hz(m), 0.35, 1.0, 0.2), t0 + k*0.5*beat, -0.35 if k % 2 else 0.35, 0.07)
    # drums (lighter in first 2 bars of each section)
    for bt in range(4):
        if bt in (0, 2): put(kick(), t0 + bt*beat, 0, 0.35)
        put(hat(), t0 + (bt + 0.5)*beat, 0.4, 0.10)
        put(shaker(), t0 + bt*beat, -0.4, 0.06)

for section, mel in enumerate([melA, melB]):
    for rep in range(2):
        base = (section * 8 + rep * 4) * 4 * beat
        for (bt, m, du) in mel:
            if rep == 1 and bt >= 12:   # second phrase ends on the tonic
                pass
            put(bell(hz(m), du*beat*1.6, 1.0, 0.45), base + bt*beat, 0.1, 0.16)
            put(bell(hz(m+12), du*beat*0.9, 1.0, 0.2), base + bt*beat + 0.012, -0.2, 0.03)

st = np.stack([left, right], 1)
# gentle glue: soft clip
st = np.tanh(st * 2.2) / np.tanh(2.2)
save("music_loop", st, stereo=True)
print("loop seconds", L / SR)
