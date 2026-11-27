"""テクスチャの色を荒野・西部劇の共通パレットに寄せる（ローポリのパック用。AO や高さは使わない）。

python3 recolor_texture.py <入力> <出力.png> [パレットへの寄せ具合 0〜1 = 0.85] [明るさの段数 = 5] [redrock]

redrock: 彩度の低い灰色の部分を、明るさに応じて赤い岩のグラデーションに置き換える（峡谷の崖・岩用）
"""
import sys
import numpy as np
from PIL import Image, ImageFilter
sys.path.insert(0, __import__("os").path.dirname(__import__("os").path.abspath(__file__)))
from toonify_texture import PAL, to_ycc  # noqa: E402

src, dst = sys.argv[1], sys.argv[2]
w = float(sys.argv[3]) if len(sys.argv) > 3 else 0.85
steps = int(sys.argv[4]) if len(sys.argv) > 4 else 5
redrock = len(sys.argv) > 5 and sys.argv[5] == "redrock"

img = Image.open(src).convert("RGB")
if max(img.size) > 2048:     # VRChat 向けに 2048 までに縮小
    img = img.resize((min(img.size[0], 2048), min(img.size[1], 2048)), Image.LANCZOS)
if max(img.size) > 256:      # 写真的なテクスチャだけ細部をならす（小さなカラーパレット画像はそのまま）
    img = img.filter(ImageFilter.MedianFilter(5))
c = np.asarray(img).astype(np.float32) / 255.0
H, W, _ = c.shape
if redrock:
    RAMP = np.array([[0x5e, 0x2a, 0x20], [0x84, 0x40, 0x2c], [0xa9, 0x5a, 0x3c], [0xc9, 0x7a, 0x52], [0xe0, 0xa1, 0x74], [0xf0, 0xc8, 0xa0]], np.float32) / 255
    mx, mn = c.max(-1), c.min(-1)
    sat = (mx - mn) / (mx + 1e-4)
    lum = 0.299 * c[..., 0] + 0.587 * c[..., 1] + 0.114 * c[..., 2]
    t = np.clip((lum - 0.12) / 0.75, 0, 1) * (len(RAMP) - 1)
    i0 = np.floor(t).astype(int); i1 = np.minimum(i0 + 1, len(RAMP) - 1); f = (t - i0)[..., None]
    rock = RAMP[i0] * (1 - f) + RAMP[i1] * f
    gray = np.clip((0.25 - sat) / 0.15, 0, 1)[..., None]
    c = c * (1 - gray) + rock * gray
flat = c.reshape(-1, 3)
f_ycc, p_ycc = to_ycc(flat), to_ycc(PAL)
idx = np.empty(len(flat), np.int32)
for i in range(0, len(flat), 1 << 18):
    idx[i:i + (1 << 18)] = ((f_ycc[i:i + (1 << 18), None] - p_ycc[None]) ** 2).sum(-1).argmin(1)
out = PAL[idx].reshape(H, W, 3) * w + c * (1 - w)
lum = out.max(-1, keepdims=True) + 1e-4
q = (np.floor(lum * steps) + 0.6) / steps
out = np.clip(out / lum * np.minimum(q, 1.0), 0, 1)
Image.fromarray((out * 255).astype(np.uint8)).save(dst)
print("RECOLOR", dst, (W, H))
