#!/usr/bin/env python3
"""効果音を Assets/WadoPTR/Audio/ に生成する。

  python Tools/build_audio.py <rpg_sound_pack_dir> <impsplat_dir> <extreme_splatter_dir>

- CC0 素材(OpenGameArt)は切り出し・正規化して ogg 化
- 環境音・館内チャイム・計量器の電子音などは ffmpeg の信号源から合成(本リポジトリのオリジナル)
要: ffmpeg
"""
import os, subprocess, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "Assets", "WadoPTR", "Audio")
os.makedirs(OUT, exist_ok=True)
rpg, wet, splat = sys.argv[1:4]


def ff(args, name):
    cmd = ["ffmpeg", "-v", "error", "-y"] + args + ["-ac", "1", "-ar", "44100", "-c:a", "libvorbis", "-q:a", "4",
                                                   os.path.join(OUT, name + ".ogg")]
    subprocess.check_call(cmd)
    print("OK", name, flush=True)


def clip(src, name, dur=None, start=0.0, gain=0.0, fade=0.05, extra=""):
    af = f"volume={gain}dB"
    if extra:
        af = extra + "," + af
    a = ["-ss", str(start), "-i", src]
    if dur:
        a += ["-t", str(dur)]
        af += f",afade=t=out:st={max(0.0, dur - fade)}:d={fade}"
    ff(a + ["-af", af], name)


def synth(expr_inputs, flt, name, dur):
    a = []
    for e in expr_inputs:
        a += ["-f", "lavfi", "-t", str(dur), "-i", e]
    ff(a + ["-filter_complex", flt, "-map", "[o]"], name)


# ---- CC0: 8 Wet Squish / Slurp Impacts (OpenGameArt) ----
for i in range(1, 5):
    clip(os.path.join(wet, f"impactsplat0{i}.mp3.flac"), f"chop{i}", dur=0.55, gain=-2)
clip(os.path.join(wet, "impactsplat05.mp3.flac"), "sever1", dur=0.9, gain=0)
clip(os.path.join(wet, "impactsplat07.mp3.flac"), "sever2", dur=0.9, gain=0)
clip(os.path.join(wet, "impactsplat08.mp3.flac"), "plop", dur=0.6, gain=-6)
# ---- CC0: 2 Wooden Squish Splatter Sequences ----
clip(os.path.join(splat, "crack11.mp3.flac"), "crack", dur=1.1, gain=-3)
# ---- CC0: RPG Sound Pack ----
clip(os.path.join(rpg, "battle", "swing.wav"), "swing1", gain=-6)
clip(os.path.join(rpg, "battle", "swing2.wav"), "swing2", gain=-6)
clip(os.path.join(rpg, "battle", "sword-unsheathe.wav"), "blade_take", gain=-8)
clip(os.path.join(rpg, "inventory", "coin.wav"), "coin1", gain=-4)
clip(os.path.join(rpg, "inventory", "coin2.wav"), "coin2", gain=-4)
clip(os.path.join(rpg, "inventory", "coin3.wav"), "coin3", gain=-4)
clip(os.path.join(rpg, "inventory", "cloth-heavy.wav"), "cloth", gain=-6)
clip(os.path.join(rpg, "inventory", "metal-small1.wav"), "metal1", gain=-8)
clip(os.path.join(rpg, "world", "door.wav"), "hatch", gain=-3, extra="asetrate=44100*0.7,aresample=44100")

# ---- 合成音 ----
# 冷蔵機の唸り (ループ)
synth(["anoisesrc=color=brown:amplitude=0.6:seed=7", "sine=frequency=50", "sine=frequency=100"],
      "[0:a]lowpass=f=220,volume=0.9[n];[1:a]volume=0.20[h1];[2:a]volume=0.07,tremolo=f=0.3:d=0.5[h2];"
      "[n][h1][h2]amix=inputs=3:normalize=0,volume=1.6[o]", "amb_cooler", 12)
# 路地のざわめき: 遠い換気扇と雨だれ、くぐもった街の低音 (ループ)
synth(["anoisesrc=color=pink:amplitude=0.5:seed=21", "anoisesrc=color=brown:amplitude=0.7:seed=22", "sine=frequency=72"],
      "[0:a]highpass=f=1800,lowpass=f=7000,tremolo=f=0.13:d=0.5,volume=0.22[rain];[1:a]lowpass=f=160,volume=1.1[rumble];"
      "[2:a]tremolo=f=0.21:d=0.7,volume=0.1[hum];[rain][rumble][hum]amix=inputs=3:normalize=0,volume=1.5[o]", "amb_alley", 14)
# 蛍光灯のジー音 (ループ)
synth(["sine=frequency=100", "sine=frequency=200", "sine=frequency=4100", "anoisesrc=color=white:amplitude=0.05:seed=3"],
      "[0:a]volume=0.25[a];[1:a]volume=0.12[b];[2:a]volume=0.012[c];[3:a]highpass=f=5000,volume=0.6[d];"
      "[a][b][c][d]amix=inputs=4:normalize=0[o]", "amb_buzz", 6)
# コンベア (ループ)
synth(["anoisesrc=color=pink:amplitude=0.5:seed=11", "sine=frequency=38"],
      "[0:a]bandpass=f=300:w=200,tremolo=f=7:d=0.6,volume=1.2[n];[1:a]volume=0.3[h];[n][h]amix=inputs=2:normalize=0[o]",
      "amb_conveyor", 8)
# 計量器の電子音
synth(["sine=frequency=1960"], "[0:a]afade=t=in:d=0.005,afade=t=out:st=0.07:d=0.03,volume=0.5[o]", "beep", 0.1)
synth(["sine=frequency=1960", "sine=frequency=2610"],
      "[0:a]afade=t=out:st=0.06:d=0.03,apad=pad_dur=0.22[a];[1:a]adelay=110,afade=t=out:st=0.2:d=0.1[b];"
      "[a][b]amix=inputs=2:normalize=0,volume=0.5[o]", "beep_ok", 0.32)
synth(["aevalsrc='0.4*(lt(mod(t*160,1),0.5)*2-1)'"], "[0:a]lowpass=f=900,afade=t=out:st=0.22:d=0.06[o]", "buzzer", 0.3)
# 館内放送チャイム (上行 4 音 / 下行 4 音)
def chime(freqs, name):
    ins, parts = [], []
    for i, f in enumerate(freqs):
        ins.append(f"sine=frequency={f}")
        parts.append(f"[{i}:a]afade=t=in:d=0.01,afade=t=out:st=0.05:d=1.1,adelay={int(i * 480)}[c{i}]")
    mix = "".join(f"[c{i}]" for i in range(len(freqs)))
    flt = ";".join(parts) + f";{mix}amix=inputs={len(freqs)}:normalize=0,aecho=0.6:0.5:90:0.35,volume=0.45[o]"
    a = []
    for e in ins:
        a += ["-f", "lavfi", "-t", "1.25", "-i", e]
    ff(a + ["-filter_complex", flt, "-map", "[o]", "-t", "3.4"], name)
chime([523.25, 659.25, 783.99, 1046.5], "chime_up")
chime([1046.5, 783.99, 659.25, 523.25], "chime_down")
# 伝票プリンタ
synth(["anoisesrc=color=white:amplitude=0.5:seed=5"],
      "[0:a]bandpass=f=2600:w=1800,tremolo=f=38:d=0.95,afade=t=out:st=0.6:d=0.1,volume=0.5[o]", "printer", 0.7)
# 水滴
synth(["aevalsrc='0.5*sin(2*PI*(900+2600*exp(-t*38))*t)*exp(-t*26)'"], "[0:a]aecho=0.7:0.6:70:0.3[o]", "drip", 0.5)
# 電話の呼出音 (国内風: 400Hz を 16Hz で変調, 1 秒)
synth(["sine=frequency=400"], "[0:a]tremolo=f=16:d=0.9,afade=t=out:st=0.9:d=0.08,volume=0.5[o]", "phone_ring", 1.0)
# シャッター/搬入機構の駆動音
synth(["anoisesrc=color=brown:amplitude=0.7:seed=2", "sine=frequency=62"],
      "[0:a]bandpass=f=500:w=400,tremolo=f=11:d=0.5[n];[1:a]volume=0.4[h];[n][h]amix=inputs=2:normalize=0,"
      "afade=t=in:d=0.15,afade=t=out:st=1.5:d=0.3,volume=1.3[o]", "motor", 1.8)
