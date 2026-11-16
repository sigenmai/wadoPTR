#!/usr/bin/env python3
"""U# スクリプトの .meta と UdonSharpProgramAsset (.asset) を生成する。

UdonSharp はスクリプト 1 本につき Program Asset が 1 つ必要。Unity を開かずに用意できるよう、
SDK 同梱サンプルと同じ書式で書き出す (中身は初回インポート時に UdonSharp がコンパイルして埋める)。
GUID はパスから決定的に作るので、何度実行しても変わらない。
"""
import hashlib, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCRIPTS = os.path.join(ROOT, "Assets", "WadoPTR", "Scripts")
EDITOR = os.path.join(ROOT, "Assets", "WadoPTR", "Editor")
USHARP_PROGRAM_ASSET_GUID = "c333ccfdd0cbdbc4ca30cef2dd6e6b9b"   # UdonSharpProgramAsset.cs (com.vrchat.worlds 同梱)


def guid(key):
    return hashlib.md5(("wadoptr:" + key).encode()).hexdigest()


def write(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


MONO_META = """fileFormatVersion: 2
guid: {g}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
ASSET_META = """fileFormatVersion: 2
guid: {g}
NativeFormatImporter:
  externalObjects: {{}}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
ASSET = """%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: 0}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {usharp}, type: 3}}
  m_Name: {name}
  m_EditorClassIdentifier: 
  serializedUdonProgramAsset: {{fileID: 0}}
  udonAssembly: 
  assemblyError: 
  sourceCsScript: {{fileID: 11500000, guid: {cs}, type: 3}}
  scriptVersion: 2
  compiledVersion: 0
  behaviourSyncMode: 0
  hasInteractEvent: 0
  scriptID: 0
  serializationData:
    SerializedFormat: 2
    SerializedBytes: 
    ReferencedUnityObjects: []
    SerializedBytesString: 
    Prefab: {{fileID: 0}}
    PrefabModificationsReferencedUnityObjects: []
    PrefabModifications: []
    SerializationNodes: []
"""

for fn in sorted(os.listdir(SCRIPTS)):
    if not fn.endswith(".cs"):
        continue
    name = fn[:-3]
    cs = guid("Scripts/" + fn)
    write(os.path.join(SCRIPTS, fn + ".meta"), MONO_META.format(g=cs))
    write(os.path.join(SCRIPTS, name + ".asset"), ASSET.format(usharp=USHARP_PROGRAM_ASSET_GUID, name=name, cs=cs))
    write(os.path.join(SCRIPTS, name + ".asset.meta"), ASSET_META.format(g=guid("Scripts/" + name + ".asset")))
    print("OK", name)
for fn in sorted(os.listdir(EDITOR)):
    if fn.endswith(".cs"):
        write(os.path.join(EDITOR, fn + ".meta"), MONO_META.format(g=guid("Editor/" + fn)))
