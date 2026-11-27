"""手配書モデル用の「選択カード」テクスチャを作る（ロビーの壁に貼る能力・乗り物カード）。

python3 make_wanted_cards.py <元テクスチャ WantedTexture.png> <馬シルエット.png> <ラクダシルエット.png> <出力フォルダ>

- 元テクスチャ（1536×1024）の配置：左の正方形（u 0〜0.666）＝紙の上 2/3、
  右の細い帯（u 0.766〜1）＝紙の下 1/3 を 90° 回転したもの。
  縦長キャンバス 1024×1436（実際の見た目）で描き、この配置に変換して保存する
- 上部の「WANTED」の見出しは元の手配書から切り出して使う。2行目（DEAD OR ALIVE）は消して書き換える
- 中央は墨の線画（能力）または乗り物のシルエット、下に日本語の名前と懸賞金
"""
import sys, os, math, random
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

src, sil_horse, sil_camel, out = sys.argv[1:5]
os.makedirs(out, exist_ok=True)
W, H = 1024, 1436
INK = (52, 34, 22)
FONT = "/usr/share/fonts/opentype/ipafont-gothic/ipag.ttf"

TOP_H = 955            # 紙の上 2/3 の高さ（px）。下 1/3 = H - TOP_H
STRIP_X = int(0.766 * 1536)


def tex_to_portrait(tex):
    top = tex.crop((0, 0, 1024, 1024)).resize((W, TOP_H), Image.LANCZOS)
    strip = tex.crop((STRIP_X, 0, 1536, 1024)).rotate(-90, expand=True).resize((W, H - TOP_H), Image.LANCZOS)
    img = Image.new("RGB", (W, H))
    img.paste(top, (0, 0))
    img.paste(strip, (0, TOP_H))
    return img


def portrait_to_tex(img):
    tex = Image.new("RGB", (1536, 1024))
    tex.paste(img.resize((1536, 1024)), (0, 0))                       # 使われない隙間も紙の色で埋める
    tex.paste(img.crop((0, 0, W, TOP_H)).resize((1024, 1024), Image.LANCZOS), (0, 0))
    strip = img.crop((0, TOP_H, W, H)).rotate(90, expand=True).resize((1536 - STRIP_X, 1024), Image.LANCZOS)
    tex.paste(strip, (STRIP_X, 0))
    return tex


orig = Image.open(src).convert("RGB")
portrait = tex_to_portrait(orig)                        # 紙に貼られたときの見た目
header = portrait.crop((0, 0, W, 200))                  # 「WANTED」の文字だけ（DEAD OR ALIVE は含めない）
rng = np.random.default_rng(7)


def parchment():
    """元の紙の色味に合わせた羊皮紙：平均色＋ムラ＋縁の焼け"""
    px = np.asarray(portrait, dtype=np.float32).reshape(-1, 3)
    bright = px[px.sum(1) > np.percentile(px.sum(1), 55)]          # インクを除いた紙の部分だけの色
    base = np.ones((H, W, 3), np.float32) * np.median(bright, axis=0)
    noise = Image.fromarray((rng.random((H // 8, W // 8)) * 255).astype(np.uint8)).resize((W, H), Image.BICUBIC)
    n = (np.asarray(noise, dtype=np.float32) / 255 - 0.5)[..., None] * 26
    yy, xx = np.mgrid[0:H, 0:W]
    edge = np.minimum.reduce([xx, yy, W - 1 - xx, H - 1 - yy]).astype(np.float32)
    burn = np.clip(1 - edge / 90, 0, 1)[..., None] ** 2 * 70
    img = np.clip(base + n - burn, 0, 255).astype(np.uint8)
    return Image.fromarray(img)


def ink_line(d, pts, w):
    """筆っぽく太さを揺らした線"""
    for (a, b) in zip(pts, pts[1:]):
        d.line([a, b], fill=INK, width=max(2, int(w * random.uniform(0.75, 1.2))))


def draw_dust(d, cx, cy):
    random.seed(1)
    for k in range(4):                                   # 渦巻く砂煙
        r0 = 60 + k * 55
        pts = [(cx + (r0 + t * 1.6) * math.cos(t / 22 + k), cy + (r0 + t * 1.6) * 0.55 * math.sin(t / 22 + k)) for t in range(0, 220, 4)]
        ink_line(d, pts, 9 - k)
    for _ in range(140):                                 # 砂粒
        a, r = random.uniform(0, 6.28), random.uniform(60, 330)
        x, y = cx + r * math.cos(a), cy + r * 0.55 * math.sin(a)
        s = random.uniform(2, 7)
        d.ellipse([x - s, y - s, x + s, y + s], fill=INK)


def draw_lasso(d, cx, cy):
    random.seed(2)
    d.ellipse([cx - 230, cy - 170, cx + 230, cy + 60], outline=INK, width=14)   # 輪
    d.ellipse([cx - 205, cy - 148, cx + 205, cy + 38], outline=INK, width=4)
    pts = [(cx + 190, cy + 10)]
    for t in range(1, 40):                                # 手元へ伸びる縄
        pts.append((cx + 190 + t * 6, cy + 10 + 120 * math.sin(t / 9) + t * 5))
    ink_line(d, pts, 12)
    d.rectangle([cx + 150, cy - 10, cx + 200, cy + 30], fill=INK)               # 結び目


def draw_wall(d, cx, cy):
    random.seed(3)
    top = [(cx - 300, cy + 170)]
    for i in range(1, 12):
        top.append((cx - 300 + i * 55, cy - random.uniform(80, 220)))
    top.append((cx + 300, cy + 170))
    d.polygon(top, outline=INK, width=12)
    for i in range(9):                                    # ひび
        x = cx - 250 + i * 60
        pts = [(x, cy + 160)]
        for j in range(4):
            pts.append((pts[-1][0] + random.uniform(-25, 25), pts[-1][1] - random.uniform(30, 60)))
        ink_line(d, pts, 5)
    d.line([(cx - 360, cy + 172), (cx + 360, cy + 172)], fill=INK, width=10)    # 地面


def draw_ball(d, cx, cy):
    r = 170
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=INK)
    d.ellipse([cx - r + 40, cy - r + 30, cx - r + 120, cy - r + 90], fill=(120, 96, 70))  # 光
    for a in range(0, 360, 45):                           # 鋲
        x, y = cx + 120 * math.cos(math.radians(a)), cy + 120 * math.sin(math.radians(a))
        d.ellipse([x - 9, y - 9, x + 9, y + 9], fill=(95, 74, 55))
    for i in range(5):                                    # 動きの線
        y = cy - 120 + i * 60
        d.line([(cx - r - 60 - i * 15, y), (cx - r - 220 - i * 15, y)], fill=INK, width=8)


def paste_silhouette(img, path, cx, cy, w):
    s = Image.open(path).convert("RGBA")
    bb = s.getbbox()
    s = s.crop(bb)
    h = int(s.height * w / s.width)
    s = s.resize((w, h), Image.LANCZOS)
    a = np.asarray(s)[..., 3]
    ink = Image.new("RGBA", s.size, INK + (0,))
    ink.putalpha(Image.fromarray(a))
    img.paste(ink, (cx - w // 2, cy - h // 2), ink)


CARDS = [
    ("Dust", "砂塵", "すなけむり", draw_dust),
    ("Lasso", "投げ縄", "なげなわ", draw_lasso),
    ("Wall", "岩壁", "がんぺき", draw_wall),
    ("Ball", "鉄球", "てっきゅう", draw_ball),
    ("Horse", "馬", "うま", sil_horse),
    ("Camel", "ラクダ", "らくだ", sil_camel),
]

f_name = ImageFont.truetype(FONT, 150)
f_kana = ImageFont.truetype(FONT, 54)
f_small = ImageFont.truetype(FONT, 48)

for key, name, kana, art in CARDS:
    img = parchment()
    # 見出しの帯は下端をぼかして紙になじませる
    mask = Image.new("L", header.size, 255)
    md = ImageDraw.Draw(mask)
    for i in range(40):
        md.line([(0, header.height - 1 - i), (W, header.height - 1 - i)], fill=int(255 * i / 40))
    img.paste(header, (0, 0), mask)
    d = ImageDraw.Draw(img)
    cx, cy = W // 2, int(H * 0.47)
    d.text((W // 2, int(H * 0.17)), "～ この腕を求む ～", font=f_small, fill=INK, anchor="mm")
    d.line([(90, int(H * 0.2)), (W - 90, int(H * 0.2))], fill=INK, width=4)
    if callable(art):
        art(d, cx, cy)
    else:
        paste_silhouette(img, art, cx, cy, 700)
        d = ImageDraw.Draw(img)
    d.line([(90, int(H * 0.68)), (W - 90, int(H * 0.68))], fill=INK, width=4)
    # 名前（太らせるため少しずらして重ね書き）
    for dx, dy in ((0, 0), (2, 0), (0, 2), (2, 2)):
        d.text((W // 2 + dx, int(H * 0.76) + dy), name, font=f_name, fill=INK, anchor="mm")
    d.text((W // 2, int(H * 0.84)), kana, font=f_kana, fill=INK, anchor="mm")
    d.text((W // 2, int(H * 0.92)), "REWARD  $500", font=f_small, fill=INK, anchor="mm")
    # 印刷のかすれ
    img = img.filter(ImageFilter.GaussianBlur(0.6))
    portrait_to_tex(img).save(os.path.join(out, f"Wanted_{key}.png"))
    img.resize((W // 2, H // 2)).save(os.path.join(out, f"_preview_{key}.png"))

orig.save(os.path.join(out, "Wanted_Original.png"))
portrait.resize((W // 2, H // 2)).save(os.path.join(out, "_preview_Original.png"))
print("CARDS", [c[0] for c in CARDS])
