"""焼き込んだ色・AO・高さから、世界観に合わせたトゥーン調テクスチャを作る。

python3 toonify_texture.py <color.png> <ao.png> <height.png> <out.png>

- 写真っぽい細かいノイズをならす（ぼかし＋中央値）
- 全アセット共通の「荒野・西部劇」パレットに色を寄せる（元の色味の差は少し残す）
- 明るさを段階化（手描きの塗り分け風）
- AO を2段の影にしてくぼみを強調
- 足元 12% を砂色に寄せる（砂ぼこり・地面となじませる）
"""
import sys
import numpy as np
from PIL import Image, ImageFilter

color_p, ao_p, h_p, out_p = sys.argv[1:5]

# 荒野・西部劇の共通パレット（トゥーン向けに彩度控えめ・暖色寄り）
PALETTE_HEX = [
    # 木
    "4a2f22", "6b4530", "8c5f3f", "ad7d52", "cfa06f", "e3c08e",
    # 赤い岩・土
    "5e2a20", "84402c", "a95a3c", "c97a52", "e0a174", "f0c8a0",
    # 砂
    "b88c5c", "d1a873", "e6c493", "f4dfba",
    # 乾いた緑（サボテン・葉）
    "3f4a28", "566335", "707d42", "8f9a55", "b0b676",
    # 灰色の石
    "5c5650", "7a726a", "9a9086", "bdb2a4", "ddd3c3",
    # 布・革・金属（暗）
    "2c2420", "45372e", "64503f", "86705a",
    # 差し色（色あせた赤・青）
    "8e3b2e", "b5584a", "4f6470", "71899a",
]
PAL = np.array([[int(h[i:i + 2], 16) for i in (0, 2, 4)] for h in PALETTE_HEX], dtype=np.float32) / 255.0


def load(p, mode="RGB"):
    return Image.open(p).convert(mode)


col_img = load(color_p)
# ノイズをならす：中央値 → 軽いぼかし
col_img = col_img.filter(ImageFilter.MedianFilter(5)).filter(ImageFilter.GaussianBlur(1.2))
col = np.asarray(col_img).astype(np.float32) / 255.0
ao = np.asarray(load(ao_p, "L")).astype(np.float32) / 255.0
hgt = np.asarray(load(h_p, "L")).astype(np.float32) / 255.0

H, W, _ = col.shape
flat = col.reshape(-1, 3)

# パレットの最も近い色（明るさを重め、色相も見る：簡易的に YCbCr 風の重み付き距離）
def to_ycc(c):
    y = 0.299 * c[..., 0] + 0.587 * c[..., 1] + 0.114 * c[..., 2]
    cb = c[..., 2] - y
    cr = c[..., 0] - y
    return np.stack([y, cb * 1.6, cr * 1.6], -1)

f_ycc = to_ycc(flat)
p_ycc = to_ycc(PAL)
idx = np.empty(len(flat), dtype=np.int32)
chunk = 1 << 18
for i in range(0, len(flat), chunk):
    d = ((f_ycc[i:i + chunk, None, :] - p_ycc[None, :, :]) ** 2).sum(-1)
    idx[i:i + chunk] = d.argmin(1)
mapped = PAL[idx].reshape(H, W, 3)

# 元の色味を 25% 残す（全部同じ色に潰れないように）
out = mapped * 0.75 + col * 0.25

# 足元を砂色に（高さ 0〜12% で徐々に）
sand = np.array([0xd1, 0xa8, 0x73], dtype=np.float32) / 255.0
dust = np.clip(1.0 - hgt / 0.12, 0, 1)[..., None] ** 1.5 * 0.55
out = out * (1 - dust) + sand * dust

# AO を2段の影に
shadow = np.where(ao < 0.55, 0.72, np.where(ao < 0.8, 0.88, 1.0))[..., None]
out = out * shadow

# 明るさを段階化（6段）しつつ色相・彩度は保つ
lum = out.max(-1, keepdims=True) + 1e-4
q = (np.floor(lum * 6) + 0.6) / 6
out = np.clip(out / lum * np.minimum(q, 1.0), 0, 1)

img = Image.fromarray((out * 255).astype(np.uint8))
# 段階化の境界をなじませる
img = img.filter(ImageFilter.ModeFilter(3))
img.save(out_p)
print("TOON", out_p, img.size)
