using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HorseRace.EditorTools
{
    /// <summary>
    /// 仮の効果音（ui-r2-11）。SfxMath の波形を 16bit モノラルの WAV にして Generated/Audio に書き、取り込んで AudioClip を返す。
    /// 音は「モデル」ではないので「モデルは作るな」の対象外。Phase 2 で本物の音に差し替えるまで、無音の AudioSource を無くすためのもの。
    /// 同じ音はビルド中 1 回だけ書く（キャッシュ）。
    /// </summary>
    public static class SfxBuilder
    {
        public const string GEN_AUDIO = GenUtil.GENERATED + "/Audio";

        static readonly Dictionary<int, AudioClip> cache = new Dictionary<int, AudioClip>();

        public static void ResetCache() => cache.Clear();

        /// <summary>音 kind（SfxMath.TICK など）の AudioClip。書けなければ null（Validate が警告）。</summary>
        public static AudioClip Clip(int kind)
        {
            AudioClip c;
            if (cache.TryGetValue(kind, out c) && c != null) return c;
            string name = SfxMath.Name(kind);
            if (name.Length == 0) return null;
            GenUtil.EnsureFolder(GEN_AUDIO);
            string path = GEN_AUDIO + "/Sfx_" + name + ".wav";
            float[] wave = SfxMath.Generate(kind, SfxMath.SAMPLE_RATE);
            byte[] bytes = Wav(wave, SfxMath.SAMPLE_RATE);
            string full = Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
            bool same = File.Exists(full) && SameBytes(File.ReadAllBytes(full), bytes);
            if (!same)
            {
                File.WriteAllBytes(full, bytes);
                AssetDatabase.ImportAsset(path);
            }
            c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (c == null) GenUtil.Warn("効果音を取り込めませんでした: " + path);
            cache[kind] = c;
            return c;
        }

        /// <summary>16bit PCM・モノラルの WAV（RIFF）。</summary>
        public static byte[] Wav(float[] samples, int rate)
        {
            int dataBytes = samples.Length * 2;
            using (var ms = new MemoryStream(44 + dataBytes))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);              // fmt の長さ
                w.Write((short)1);        // PCM
                w.Write((short)1);        // モノラル
                w.Write(rate);
                w.Write(rate * 2);        // byte rate
                w.Write((short)2);        // block align
                w.Write((short)16);       // bits
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(dataBytes);
                for (int i = 0; i < samples.Length; i++) w.Write((short)SfxMath.ToPcm16(samples[i]));
                w.Flush();
                return ms.ToArray();
            }
        }

        static bool SameBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
