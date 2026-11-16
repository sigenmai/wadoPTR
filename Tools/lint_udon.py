#!/usr/bin/env python3
"""Unity を開かずにできる範囲の検査。

  python Tools/lint_udon.py [udon_externs.txt]     (一覧は Tools/extract_udon_externs.py で作る)

1) tree-sitter で全 .cs の構文を検査 (ERROR / MISSING ノードが無いこと)
2) U# スクリプトで使っている「型.メンバー」「値.メンバー」を拾い、
   - 自作クラスのメンバー
   - Udon が公開している extern (VRChat SDK の DLL から抜き出した一覧。第 1 引数)
   のどちらかに名前があることを確かめる。
3) U# が受け付けない構文 (ジェネリック型, interface, try/catch, List<T> など) が無いこと
要: pip install tree-sitter tree-sitter-c-sharp
これは本物のコンパイルの代わりにはならない。名前の綴りと公開範囲の取りこぼしを早めに見つけるためのもの。
"""
import os, re, sys
import tree_sitter_c_sharp as tscs
from tree_sitter import Language, Parser

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, "Assets", "WadoPTR", "Scripts")
EDITOR = os.path.join(ROOT, "Assets", "WadoPTR", "Editor")
parser = Parser(Language(tscs.language()))
errors = 0


def walk(n):
    yield n
    for c in n.children:
        yield from walk(c)


def check_syntax(path):
    global errors
    src = open(path, "rb").read()
    tree = parser.parse(src)
    bad = [n for n in walk(tree.root_node) if n.type == "ERROR" or n.is_missing]
    for n in bad[:5]:
        errors += 1
        print(f"SYNTAX {os.path.basename(path)}:{n.start_point[0] + 1}: {src[n.start_byte:n.end_byte][:60]!r}")
    return src, tree


files = [os.path.join(SCRIPTS, f) for f in sorted(os.listdir(SCRIPTS)) if f.endswith(".cs")]
editor_files = [os.path.join(EDITOR, f) for f in sorted(os.listdir(EDITOR)) if f.endswith(".cs")]
parsed = {f: check_syntax(f) for f in files}
for f in editor_files:
    check_syntax(f)

# 自作メンバー
own = set()
own_types = set()
for f, (src, tree) in parsed.items():
    for n in walk(tree.root_node):
        if n.type == "class_declaration":
            own_types.add(n.child_by_field_name("name").text.decode())
        if n.type == "method_declaration":
            own.add(n.child_by_field_name("name").text.decode())
        if n.type == "variable_declarator":
            own.add(n.child_by_field_name("name").text.decode() if n.child_by_field_name("name") else n.children[0].text.decode())

externs = set()
if len(sys.argv) > 1:
    for line in open(sys.argv[1], encoding="utf-8"):
        m = re.match(r"^[A-Za-z0-9_]+\.__(?:get_|set_|op_)?([A-Za-z0-9]+)", line)
        if m:
            externs.add(m.group(1))
    externs.add("ctor")

# 名前空間・型名としてだけ現れるもの
TYPE_NAMES = {"Math", "DateTime", "TrackingDataType", "TrackingData", "PickupHand"}

# U# が扱えないもの
FORBIDDEN = {"interface_declaration": "interface", "try_statement": "try/catch", "lambda_expression": "lambda",
             "struct_declaration": "struct", "anonymous_method_expression": "delegate", "yield_statement": "yield",
             "await_expression": "await", "query_expression": "LINQ", "delegate_declaration": "delegate"}
for f, (src, tree) in parsed.items():
    base = os.path.basename(f)
    for n in walk(tree.root_node):
        if n.type in FORBIDDEN:
            errors += 1
            print(f"UNSUPPORTED {base}:{n.start_point[0] + 1}: {FORBIDDEN[n.type]}")
        if n.type == "generic_name":
            name = n.children[0].text.decode()
            if name not in ("GetComponent", "GetComponentInChildren", "GetComponentsInChildren", "GetComponents"):
                errors += 1
                print(f"UNSUPPORTED {base}:{n.start_point[0] + 1}: generic {n.text.decode()}")
        if n.type == "member_access_expression" and externs:
            name_node = n.child_by_field_name("name")
            name = name_node.text.decode()
            if name_node.type == "generic_name":
                name = name_node.children[0].text.decode()
            if name in own or name in externs or name in TYPE_NAMES:
                continue
            # 列挙値・定数 (TrackingDataType.Head など) は extern にならない
            parent_txt = n.child_by_field_name("expression").text.decode()
            if re.search(r"(TrackingDataType|BehaviourSyncMode|PickupHand|WadoPart|WadoGame|WadoBody)$", parent_txt):
                continue
            errors += 1
            print(f"NOT-EXPOSED? {base}:{n.start_point[0] + 1}: {n.text.decode()[:70]}")
        if n.type == "invocation_expression" and externs:
            fn = n.child_by_field_name("function")
            if fn.type == "identifier":
                name = fn.text.decode()
                if name not in own and name not in ("Instantiate", "GetComponent", "SendCustomEvent", "SendCustomEventDelayedSeconds", "RequestSerialization", "typeof", "nameof"):
                    errors += 1
                    print(f"UNKNOWN-CALL {base}:{n.start_point[0] + 1}: {name}")

# メソッドのオーバーロード (U# では避ける)
for f, (src, tree) in parsed.items():
    seen = {}
    for n in walk(tree.root_node):
        if n.type == "method_declaration":
            name = n.child_by_field_name("name").text.decode()
            if name in seen:
                errors += 1
                print(f"OVERLOAD {os.path.basename(f)}:{n.start_point[0] + 1}: {name}")
            seen[name] = 1

print("files:", len(files), "+ editor", len(editor_files), "| own members:", len(own), "| extern names:", len(externs), "| problems:", errors)
sys.exit(1 if errors else 0)
