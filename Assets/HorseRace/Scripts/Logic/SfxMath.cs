using UnityEngine;

namespace HorseRace
{
    /// <summary>
    /// 仮の効果音の波形（ui-r2-11）。エディタの SfxBuilder がこの値で 16bit WAV を書き、AudioSource に付ける。
    /// 本物の音（Phase 2）に差し替えるまでの「無音よりまし」な合成音：サイン波の倍音・ノイズに包絡を掛けるだけ。
    /// Static only（UdonSharp-safe：ジェネリック・LINQ なし、乱数は添字のハッシュで決定的）。
    /// </summary>
    public class SfxMath : UdonSharp.UdonSharpBehaviour
    {
        public const int SAMPLE_RATE = 22050;
        /// <summary>正規化の目標の山（≤ 0.9、テストで確かめる）。</summary>
        public const float PEAK = 0.85f;

        public const int TICK = 0;     // 時計・CP：30ms 2kHz
        public const int CHIME = 1;    // 使えるようになった：300ms 3 倍音
        public const int PEN = 2;      // 台帳のサイン：250ms 帯域ノイズ
        public const int RUSTLE = 3;   // 手配書・電報の紙：200ms ノイズ
        public const int BELL = 4;     // 教会・号砲の鐘：1.5s 非整数倍音
        public const int WHOOSH = 5;   // 抜かれた・砂塵・投げ縄：400ms ふくらむノイズ
        public const int SHOT = 6;     // 銃声・号砲の空砲：120ms ノイズの破裂
        public const int CLICK = 7;    // 鞍の調整・空撃ち・号砲の撃鉄：20ms
        public const int STING = 8;    // ゴール・表彰・練習の周回：0.8s 上がる 3 音
        public const int THUD = 9;     // 岩壁・鉄球・カカシ：220ms 下がる低音
        public const int BREATH = 10;  // 馬の息（ループ）：1.2s ゆっくりふくらむノイズ
        public const int SPARKLE = 11; // 遺物：400ms 高い倍音のきらめき
        public const int KIND_COUNT = 12;

        public static string Name(int kind)
        {
            if (kind == TICK) return "Tick";
            if (kind == CHIME) return "Chime";
            if (kind == PEN) return "Pen";
            if (kind == RUSTLE) return "Rustle";
            if (kind == BELL) return "Bell";
            if (kind == WHOOSH) return "Whoosh";
            if (kind == SHOT) return "Shot";
            if (kind == CLICK) return "Click";
            if (kind == STING) return "Sting";
            if (kind == THUD) return "Thud";
            if (kind == BREATH) return "Breath";
            if (kind == SPARKLE) return "Sparkle";
            return "";
        }

        /// <summary>長さ（秒）。</summary>
        public static float Seconds(int kind)
        {
            if (kind == TICK) return 0.03f;
            if (kind == CHIME) return 0.3f;
            if (kind == PEN) return 0.25f;
            if (kind == RUSTLE) return 0.2f;
            if (kind == BELL) return 1.5f;
            if (kind == WHOOSH) return 0.4f;
            if (kind == SHOT) return 0.12f;
            if (kind == CLICK) return 0.02f;
            if (kind == STING) return 0.8f;
            if (kind == THUD) return 0.22f;
            if (kind == BREATH) return 1.2f;
            if (kind == SPARKLE) return 0.4f;
            return 0f;
        }

        public static int SampleCount(float seconds, int rate)
        {
            if (seconds <= 0f || rate <= 0) return 0;
            return Mathf.RoundToInt(seconds * rate);
        }

        /// <summary>決定的なノイズ（-1..1）。i = サンプルの添字、seed = 音ごと。</summary>
        public static float Noise(int i, int seed)
        {
            int h = i * 374761393 + seed * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            h = h ^ (h >> 16);
            return ((h & 0xFFFF) / 32767.5f) - 1f;
        }

        /// <summary>立ち上がり attack 秒・指数の減衰 decay（1/秒）、最後の 5ms は 0 へ（プチッと切れない）。</summary>
        public static float Envelope(float t, float length, float attack, float decay)
        {
            if (t < 0f || t > length) return 0f;
            float a = attack > 0f ? Mathf.Clamp01(t / attack) : 1f;
            float d = Mathf.Exp(-decay * t);
            float tail = Mathf.Clamp01((length - t) / 0.005f);
            return a * d * tail;
        }

        /// <summary>音 kind の波形（-PEAK..PEAK に正規化）。</summary>
        public static float[] Generate(int kind, int rate)
        {
            float len = Seconds(kind);
            int n = SampleCount(len, rate);
            float[] buf = new float[n];
            if (n == 0) return buf;
            float lp = 0f, lp2 = 0f;
            float twoPi = 2f * Mathf.PI;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float v = 0f;
                if (kind == TICK)
                    v = Mathf.Sin(twoPi * 2000f * t) * Envelope(t, len, 0.001f, 120f);
                else if (kind == CHIME)
                    v = (Mathf.Sin(twoPi * 880f * t) + 0.5f * Mathf.Sin(twoPi * 1320f * t) + 0.3f * Mathf.Sin(twoPi * 1760f * t)) * Envelope(t, len, 0.004f, 9f);
                else if (kind == PEN)
                {
                    // 帯域ノイズ（2 段のローパスの差）＋ 18Hz の揺れ（ペン先の引っかき）
                    float x = Noise(i, 3);
                    lp += 0.35f * (x - lp);
                    lp2 += 0.05f * (lp - lp2);
                    float wob = 0.6f + 0.4f * Mathf.Sin(twoPi * 18f * t);
                    v = (lp - lp2) * wob * Envelope(t, len, 0.02f, 4f);
                }
                else if (kind == RUSTLE)
                {
                    float x = Noise(i, 7);
                    lp += 0.5f * (x - lp);
                    v = (x - lp) * Envelope(t, len, 0.01f, 10f);
                }
                else if (kind == BELL)
                {
                    float f = 330f;
                    v = Mathf.Sin(twoPi * f * t) * Mathf.Exp(-1.6f * t)
                        + 0.6f * Mathf.Sin(twoPi * f * 2.76f * t) * Mathf.Exp(-2.6f * t)
                        + 0.35f * Mathf.Sin(twoPi * f * 5.40f * t) * Mathf.Exp(-4f * t)
                        + 0.2f * Mathf.Sin(twoPi * f * 8.93f * t) * Mathf.Exp(-6f * t);
                    v *= Envelope(t, len, 0.002f, 0f);
                }
                else if (kind == WHOOSH)
                {
                    float x = Noise(i, 11);
                    float k = 0.05f + 0.3f * Mathf.Sin(Mathf.PI * t / len);   // 真ん中で明るく
                    lp += k * (x - lp);
                    v = lp * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / len));
                }
                else if (kind == SHOT)
                {
                    float x = Noise(i, 13);
                    lp += 0.6f * (x - lp);
                    v = (0.7f * lp + 0.3f * Mathf.Sin(twoPi * 90f * t)) * Envelope(t, len, 0.0005f, 35f);
                }
                else if (kind == CLICK)
                    v = (0.6f * Noise(i, 17) + 0.4f * Mathf.Sin(twoPi * 3200f * t)) * Envelope(t, len, 0.0005f, 250f);
                else if (kind == STING)
                {
                    // 3 音（ド・ミ・ソ）を 0.12 秒ずつずらして重ねる
                    for (int k = 0; k < 3; k++)
                    {
                        float t0 = t - k * 0.12f;
                        if (t0 < 0f) continue;
                        float f = 523.25f * (k == 0 ? 1f : (k == 1 ? 1.26f : 1.5f));
                        v += (Mathf.Sin(twoPi * f * t0) + 0.3f * Mathf.Sin(twoPi * 2f * f * t0)) * Envelope(t0, len - k * 0.12f, 0.005f, 4f);
                    }
                }
                else if (kind == THUD)
                {
                    float f = 110f * Mathf.Exp(-6f * t) + 45f;
                    v = (Mathf.Sin(twoPi * f * t) + 0.2f * Noise(i, 19)) * Envelope(t, len, 0.002f, 16f);
                }
                else if (kind == BREATH)
                {
                    float x = Noise(i, 23);
                    lp += 0.12f * (x - lp);
                    // 吸って吐く：前半ふくらみ・後半しぼむ（ループの継ぎ目は 0）
                    v = lp * Mathf.Sin(Mathf.PI * t / len);
                }
                else if (kind == SPARKLE)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        float t0 = t - k * 0.06f;
                        if (t0 < 0f) continue;
                        float f = 1568f * (1f + 0.25f * k);
                        v += Mathf.Sin(twoPi * f * t0) * Envelope(t0, len - k * 0.06f, 0.002f, 14f);
                    }
                }
                buf[i] = v;
            }
            Normalize(buf, PEAK);
            return buf;
        }

        /// <summary>山（|最大|）を peak にそろえる。全部 0 ならそのまま。</summary>
        public static void Normalize(float[] buf, float peak)
        {
            float m = Peak(buf);
            if (m < 0.000001f) return;
            float k = peak / m;
            for (int i = 0; i < buf.Length; i++) buf[i] *= k;
        }

        public static float Peak(float[] buf)
        {
            float m = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float a = buf[i] < 0f ? -buf[i] : buf[i];
                if (a > m) m = a;
            }
            return m;
        }

        /// <summary>-1..1 を 16bit の整数に（はみ出しは切る）。</summary>
        public static int ToPcm16(float v)
        {
            if (v > 1f) v = 1f;
            if (v < -1f) v = -1f;
            return Mathf.RoundToInt(v * 32767f);
        }
    }
}
