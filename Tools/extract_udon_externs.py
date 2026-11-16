#!/usr/bin/env python3
"""VRChat SDK (com.vrchat.worlds) の DLL から、Udon が公開している extern の一覧を抜き出す。

  python Tools/extract_udon_externs.py <com.vrchat.worlds のフォルダ> > udon_externs.txt
  python Tools/lint_udon.py udon_externs.txt

SDK の zip は https://github.com/vrchat/packages/releases から取得できる (VCC が Packages/ に展開したものでもよい)。
DLL の文字列領域 (UTF-16) にある "型名.__メソッド__引数__戻り値" 形式の名前を拾うだけで、DLL は実行しない。
"""
import glob, os, re, sys

root = sys.argv[1]
out = set()
for f in glob.glob(os.path.join(root, "Editor", "Udon", "External", "*.dll")):
    data = open(f, "rb").read()
    for m in re.finditer(rb"(?:[\x20-\x7e]\x00){8,}", data):
        s = m.group(0).decode("utf-16le")
        if ".__" in s and "__" in s[3:]:
            out.add(s)
print("\n".join(sorted(out)))
