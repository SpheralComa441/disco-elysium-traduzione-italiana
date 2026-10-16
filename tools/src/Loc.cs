// Funzioni di localizzazione: lettura/scrittura dei LanguageSourceAsset (I2 Localization)
// e del database dei dialoghi (Pixel Crushers Dialogue System).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DE
{
    public class Term { public string Key; public int Type; public string Text; }

    public class Lockit
    {
        public string Name; public long PathID; public List<Term> Terms = new List<Term>();
        public Dictionary<string, Term> ByKey = new Dictionary<string, Term>();
    }

    // Etichetta di una skill nella scheda personaggio (oggetti CharsheetSkillLabels<Lingua> in resources.assets)
    public class SkillLabel { public int Id; public string Text; public float[] F = new float[9]; }

    public class SkillLabels
    {
        public byte[] Head; public string Name; public List<SkillLabel> Items = new List<SkillLabel>();

        // layout: m_GameObject(12) m_Enabled(4) m_Script(12) m_Name(string) int n { int id; string text; float[9] }
        public static SkillLabels Parse(byte[] d)
        {
            SkillLabels s = new SkillLabels();
            BinReader r = new BinReader(d, false);
            s.Head = r.Bytes(28);
            int nl = r.I32(); s.Name = Encoding.UTF8.GetString(r.Bytes(nl)); r.Align(4);
            int n = r.I32();
            if (n < 0 || n > 200) throw new InvalidDataException("CharsheetSkillLabels: conteggio inatteso " + n);
            for (int i = 0; i < n; i++)
            {
                SkillLabel l = new SkillLabel(); l.Id = r.I32();
                int tl = r.I32(); l.Text = Encoding.UTF8.GetString(r.Bytes(tl)); r.Align(4);
                for (int k = 0; k < 9; k++) l.F[k] = r.F32();
                s.Items.Add(l);
            }
            if (r.P != d.Length) throw new InvalidDataException("CharsheetSkillLabels: dati residui inattesi");
            return s;
        }

        public byte[] Build()
        {
            BinWriter w = new BinWriter(false);
            w.Bytes(Head);
            byte[] nb = Encoding.UTF8.GetBytes(Name); w.I32(nb.Length); w.Bytes(nb); w.Align(4);
            w.I32(Items.Count);
            foreach (SkillLabel l in Items)
            {
                w.I32(l.Id);
                byte[] tb = Encoding.UTF8.GetBytes(l.Text); w.I32(tb.Length); w.Bytes(tb); w.Align(4);
                for (int k = 0; k < 9; k++) w.F32(l.F[k]);
            }
            return w.ToArray();
        }
    }

    public static class Loc
    {
        public static string Esc(string s)
        {
            if (s.IndexOfAny(new char[] { '\\', '\n', '\r', '\t' }) < 0) return s;
            return s.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        public static string Unesc(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    sb.Append(n == 'n' ? '\n' : n == 'r' ? '\r' : n == 't' ? '\t' : n);
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public static bool IsLanguageSource(SerializedFile sf, SObject o)
        {
            SType t = sf.Types[o.TypeID];
            if (t.ClassID != 114 || t.Nodes == null) return false;
            foreach (TypeNode n in t.Nodes) if (n.Level == 1 && n.Name == "mSource") return true;
            return false;
        }

        public static List<Lockit> ReadLockits(SerializedFile sf)
        {
            List<Lockit> res = new List<Lockit>();
            foreach (SObject o in sf.Objects)
            {
                if (!IsLanguageSource(sf, o)) continue;
                OMap m = (OMap)sf.Read(o);
                Lockit l = new Lockit(); l.Name = m.S("m_Name"); l.PathID = o.PathID;
                foreach (object to in m.M("mSource").L("mTerms"))
                {
                    OMap tm = (OMap)to;
                    Term t = new Term(); t.Key = tm.S("Term"); t.Type = (int)tm["TermType"];
                    List<object> langs = tm.L("Languages");
                    t.Text = langs.Count > 0 ? (string)langs[0] : null;
                    l.Terms.Add(t);
                    l.ByKey[t.Key] = t;
                }
                res.Add(l);
            }
            return res;
        }

        public static List<Lockit> ReadLockits(string bundlePath)
        {
            Bundle b = Bundle.Load(bundlePath);
            return ReadLockits(SerializedFile.Load(b.FindSerialized().Data));
        }

        // "GeneralLockitSpanish" -> "General"
        public static string Kind(string lockitName)
        {
            int i = lockitName.IndexOf("Lockit", StringComparison.Ordinal);
            return i > 0 ? lockitName.Substring(0, i) : lockitName;
        }

        public static void WriteTsv(string path, IEnumerable<string[]> rows)
        {
            using (StreamWriter w = new StreamWriter(path, false, new UTF8Encoding(false)))
                foreach (string[] r in rows)
                {
                    for (int i = 0; i < r.Length; i++) { if (i > 0) w.Write('\t'); w.Write(Esc(r[i] ?? "")); }
                    w.Write('\n');
                }
        }

        public static List<string[]> ReadTsv(string path)
        {
            List<string[]> rows = new List<string[]>();
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.Length == 0) continue;
                string[] p = line.Split('\t');
                for (int i = 0; i < p.Length; i++) p[i] = Unesc(p[i]);
                rows.Add(p);
            }
            return rows;
        }

        public static string FieldOf(OMap o, string title)
        {
            foreach (object fo in o.L("fields")) { OMap f = (OMap)fo; if (f.S("title") == title) return f.S("value"); }
            return null;
        }

        // Testo inglese dei dialoghi dal database Pixel Crushers: chiave I2 -> {testo, attore, conversazione}
        public static Dictionary<string, string[]> ReadDialogueEnglish(string dialogueBundle)
        {
            SerializedFile sf = SerializedFile.Load(Bundle.Load(dialogueBundle).FindSerialized().Data);
            OMap db = null;
            foreach (SObject o in sf.Objects) if (sf.Types[o.TypeID].ClassID == 114) db = (OMap)sf.Read(o);
            Dictionary<string, string> actorName = new Dictionary<string, string>();
            foreach (object ao in db.L("actors")) { OMap a = (OMap)ao; actorName[((int)a["id"]).ToString()] = FieldOf(a, "Name"); }
            Dictionary<string, string[]> res = new Dictionary<string, string[]>();
            foreach (object co in db.L("conversations"))
            {
                OMap c = (OMap)co; string title = FieldOf(c, "Title") ?? "";
                foreach (object eo in c.L("dialogueEntries"))
                {
                    OMap e = (OMap)eo; string id = null, actor = "";
                    List<KeyValuePair<string, string>> texts = new List<KeyValuePair<string, string>>();
                    foreach (object fo in e.L("fields"))
                    {
                        OMap f = (OMap)fo; string t = f.S("title"); string v = f.S("value");
                        if (t == "Articy Id") id = v;
                        else if (t == "Actor") { string n; if (actorName.TryGetValue(v, out n)) actor = n; }
                        else if (t == "Dialogue Text" || t.StartsWith("Alternate", StringComparison.Ordinal) || t.StartsWith("tooltip", StringComparison.Ordinal))
                            texts.Add(new KeyValuePair<string, string>(t, v));
                    }
                    if (id == null) continue;
                    foreach (KeyValuePair<string, string> kv in texts)
                        if (kv.Value.Length > 0) res[kv.Key + "/" + id] = new string[] { kv.Value, actor, title };
                }
            }
            return res;
        }

        static readonly Regex HexId = new Regex("0x[0-9A-Fa-f]{16}");
        static readonly Regex Digits = new Regex("[0-9]+");

        public static string Pattern(string key)
        {
            return Digits.Replace(HexId.Replace(key, "<ID>"), "#");
        }
    }
}
