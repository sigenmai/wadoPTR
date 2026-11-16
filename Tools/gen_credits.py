#!/usr/bin/env python3
"""CREDITS.md の Poly Haven の表を polyhaven_manifest.json から作り直す。"""
import json, os, re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
m = json.load(open(os.path.join(ROOT, "Tools", "polyhaven_manifest.json"), encoding="utf-8"))
rows = ["| 素材 | 作者 | 置き場所 |", "|---|---|---|"]
for k, v in sorted(m["models"].items(), key=lambda kv: kv[0].lower()):
    rows.append(f"| {v['title']} (`{k}`) | {', '.join(v['authors'])} | `Assets/WadoPTR/Models/{k}/` |")
for k, v in sorted(m["textures"].items()):
    rows.append(f"| {v['title']} (`{k}`) | {', '.join(v['authors'])} | `Assets/WadoPTR/Textures/{k}/` |")
p = os.path.join(ROOT, "CREDITS.md")
s = open(p, encoding="utf-8").read()
s2 = re.sub(r"\| 素材 \| 作者 \| 置き場所 \|\n\|---\|---\|---\|\n(?:\|.*\n)+", "\n".join(rows) + "\n", s, count=1)
open(p, "w", encoding="utf-8").write(s2)
print("models", len(m["models"]), "textures", len(m["textures"]))
