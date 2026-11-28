using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace HorseRace.Tests
{
    /// <summary>
    /// Udon は TextMeshPro の継承メンバー（transform / gameObject / GetComponent）を公開していない
    /// （U# のエラー「Method is not exposed to Udon: 'rankText.transform'」、オーナーの Console 2026-11-28）。
    /// ((Component)x).transform のように Component に変えてから使う。
    /// </summary>
    public class UdonExposureTests
    {
        static string ScriptsDir()
        {
            string d = TestContext.CurrentContext.TestDirectory;
            while (d != null && !Directory.Exists(Path.Combine(d, "Assets", "HorseRace", "Scripts"))) d = Path.GetDirectoryName(d);
            Assert.That(d, Is.Not.Null, "Assets/HorseRace/Scripts が見つからない");
            return Path.Combine(d, "Assets", "HorseRace", "Scripts");
        }

        [Test]
        public void NoInheritedComponentMembersOnTextMeshPro()
        {
            var bad = new System.Collections.Generic.List<string>();
            foreach (string f in Directory.GetFiles(ScriptsDir(), "*.cs", SearchOption.AllDirectories))
            {
                string s = File.ReadAllText(f);
                foreach (Match d in Regex.Matches(s, @"\b(?:TextMeshProUGUI|TextMeshPro|TMP_Text)(?:\[\])?\s+(\w+)\s*[;=]"))
                {
                    string n = d.Groups[1].Value;
                    foreach (Match m in Regex.Matches(s, @"(?<![\w(])" + n + @"(\[[^\]]*\])?\s*\.\s*(transform|gameObject|GetComponent\w*)\b"))
                        bad.Add(Path.GetFileName(f) + ": " + m.Value);
                }
            }
            Assert.That(bad, Is.Empty, string.Join("\n", bad));
        }
    }
}
