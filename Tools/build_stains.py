#!/usr/bin/env python3
"""床や台に増えていく汚れのテクスチャ (アルファ付き PNG) を生成する。オリジナル。  要: pillow numpy"""
import os
import numpy as np
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "WadoPTR", "Textures", "stains")
os.makedirs(OUT, exist_ok=True)
N = 256
for idx in range(4):
    rng = np.random.default_rng(100 + idx)
    a = np.zeros((N, N), dtype=np.float32)
    yy, xx = np.mgrid[0:N, 0:N]
    stretch = 1.0 if idx < 3 else 3.2          # 3 番は引きずった跡
    for _ in range(26 if idx < 3 else 40):
        cx, cy = rng.normal(N / 2, N / 7 * (stretch if idx == 3 else 1)), rng.normal(N / 2, N / 7 / (stretch if idx == 3 else 1))
        r = abs(rng.normal(N / 9, N / 16)) + 4
        d = ((xx - cx) ** 2 + ((yy - cy) * (stretch if idx == 3 else 1)) ** 2) / (r * r)
        a = np.maximum(a, np.clip(1.25 - d, 0, 1))
    for _ in range(60):                         # 飛沫
        cx, cy = rng.uniform(8, N - 8), rng.uniform(8, N - 8)
        r = rng.uniform(1.0, 4.5)
        d = ((xx - cx) ** 2 + (yy - cy) ** 2) / (r * r)
        a = np.maximum(a, np.clip(1.1 - d, 0, 1) * 0.9)
    edge = np.minimum.reduce([xx, yy, N - 1 - xx, N - 1 - yy]) / 18.0
    a *= np.clip(edge, 0, 1)
    al = Image.fromarray((np.clip(a, 0, 1) * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.6))
    al = al.point(lambda v: int(min(255, v * 1.25) * 0.86))
    shade = rng.uniform(0.75, 1.0, (N, N)).astype(np.float32)
    shade = np.asarray(Image.fromarray((shade * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(6)), dtype=np.float32) / 255
    rgb = np.stack([70 * shade, 8 * shade, 10 * shade], axis=-1).astype(np.uint8)
    im = Image.fromarray(rgb, "RGB").convert("RGBA")
    im.putalpha(al)
    im.save(os.path.join(OUT, f"stain{idx}.png"), optimize=True)
    print("OK stain", idx)
