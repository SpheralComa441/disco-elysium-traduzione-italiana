// Strumento da riga di comando per esplorare ed elaborare i bundle di Disco Elysium.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DE
{
    public static class DeTool
    {
        static string ClassName(int id)
        {
            switch (id)
            {
                case 1: return "GameObject"; case 4: return "Transform"; case 21: return "Material"; case 28: return "Texture2D";
                case 48: return "Shader"; case 49: return "TextAsset"; case 114: return "MonoBehaviour"; case 115: return "MonoScript";
                case 128: return "Font"; case 142: return "AssetBundle"; case 213: return "Sprite"; case 224: return "RectTransform";
                case 687078895: return "SpriteAtlas"; default: return "Class" + id;
            }
        }

        // Carica un bundle oppure un file .assets "nudo"
        static SerializedFile LoadAny(string path, out Bundle bundle)
        {
            byte[] f = File.ReadAllBytes(path);
            if (f.Length > 7 && Encoding.ASCII.GetString(f, 0, 7) == "UnityFS")
            {
                bundle = Bundle.Load(f);
                return SerializedFile.Load(bundle.FindSerialized().Data);
            }
            bundle = null;
            return SerializedFile.Load(f);
        }

        static string NameOf(SerializedFile sf, SObject o)
        {
            SType t = sf.Types[o.TypeID];
            if (t.Nodes == null || t.Nodes.Count == 0) return "";
            try
            {
                OMap m = sf.Read(o) as OMap;
                if (m != null && m.Has("m_Name")) return m.S("m_Name");
            }
            catch (Exception e) { return "<errore: " + e.Message + ">"; }
            return "";
        }

        static int Info(string path, bool names)
        {
            Bundle b; SerializedFile sf = LoadAny(path, out b);
            if (b != null)
            {
                Console.WriteLine("UnityFS v{0} {1} {2} flags=0x{3:X}", b.Version, b.UnityVersion, b.UnityRevision, b.Flags);
                Dictionary<int, int> bf = new Dictionary<int, int>();
                foreach (BundleBlock k in b.Blocks) { int c; bf.TryGetValue(k.Flags, out c); bf[k.Flags] = c + 1; }
                foreach (KeyValuePair<int, int> kv in bf) Console.WriteLine("  blocchi flags=0x{0:X}: {1}", kv.Key, kv.Value);
                foreach (BundleNode n in b.Nodes) Console.WriteLine("  nodo {0}  offset={3} size={1}  flags=0x{2:X}", n.Path, n.Size, n.Flags, n.Offset);
                if (b.Blocks.Count <= 4) foreach (BundleBlock k in b.Blocks) Console.WriteLine("  blocco u={0} c={1} flags=0x{2:X}", k.USize, k.CSize, k.Flags);
            }
            Console.WriteLine("SerializedFile v{0} unity={1} platform={2} typeTree={3} dataOffset={4} oggetti={5}",
                sf.Version, sf.UnityVersion, sf.Platform, sf.EnableTypeTree, sf.DataOffset, sf.Objects.Count);
            foreach (SExternal e in sf.Externals) Console.WriteLine("  esterno: {0}", e.PathName);
            Dictionary<int, int> cc = new Dictionary<int, int>();
            foreach (SObject o in sf.Objects) { int cid = sf.Types[o.TypeID].ClassID; int c; cc.TryGetValue(cid, out c); cc[cid] = c + 1; }
            foreach (KeyValuePair<int, int> kv in cc) Console.WriteLine("  {0,-16} x{1}", ClassName(kv.Key), kv.Value);
            if (names)
                foreach (SObject o in sf.Objects)
                    Console.WriteLine("{0,22}  {1,-14} {2,10}  {3}", o.PathID, ClassName(sf.Types[o.TypeID].ClassID), o.ByteSize, NameOf(sf, o));
            return 0;
        }

        static int Dump(string path, long pathID, string outPath, int maxList, int maxStr)
        {
            Bundle b; SerializedFile sf = LoadAny(path, out b);
            SObject o = sf.Find(pathID);
            if (o == null) { Console.Error.WriteLine("PathID non trovato"); return 1; }
            object v = sf.Read(o);
            using (StreamWriter w = new StreamWriter(outPath, false, new UTF8Encoding(false)))
                Json.Write(w, v, 0, maxList, maxStr);
            Console.WriteLine("scritto " + outPath);
            return 0;
        }

        static int TypeTreeDump(string path, long pathID)
        {
            Bundle b; SerializedFile sf = LoadAny(path, out b);
            SObject o = sf.Find(pathID);
            foreach (TypeNode n in sf.Types[o.TypeID].Nodes)
                Console.WriteLine("{0}{1} {2}  (size={3}, meta=0x{4:X})", new string(' ', n.Level * 2), n.Type, n.Name, n.ByteSize, n.MetaFlag);
            return 0;
        }

        // Verifica che lettura+riscrittura di ogni oggetto e dell'intero bundle riproduca dati identici
        static int RoundTrip(string path)
        {
            Bundle b; SerializedFile sf = LoadAny(path, out b);
            int bad = 0, ok = 0, skipped = 0;
            foreach (SObject o in sf.Objects)
            {
                SType t = sf.Types[o.TypeID];
                if (t.Nodes == null || t.Nodes.Count == 0) { skipped++; continue; }
                byte[] orig = sf.GetData(o);
                object v = sf.Read(o);
                sf.Write(o, v);
                byte[] again = o.NewData; o.NewData = null;
                bool same = orig.Length == again.Length;
                for (int i = 0; same && i < orig.Length; i++) if (orig[i] != again[i]) same = false;
                if (same) ok++; else { bad++; Console.WriteLine("DIVERSO: pathID {0} ({1}) {2} -> {3} byte", o.PathID, ClassName(t.ClassID), orig.Length, again.Length); }
            }
            Console.WriteLine("oggetti identici={0} diversi={1} senza typetree={2}", ok, bad, skipped);
            if (b != null)
            {
                // ricostruzione completa: file serializzato + bundle (compresso e non), poi rilettura
                byte[] sfNew = sf.Save();
                BundleNode node = b.FindSerialized();
                byte[] origNode = node.Data;
                node.Data = sfNew;
                foreach (bool comp in new bool[] { false, true })
                {
                    byte[] saved = b.Save(comp);
                    Bundle b2 = Bundle.Load(saved);
                    SerializedFile sf2 = SerializedFile.Load(b2.FindSerialized().Data);
                    int diff = 0;
                    for (int i = 0; i < sf.Objects.Count; i++)
                    {
                        byte[] x = sf.GetData(sf.Objects[i]); byte[] y = sf2.GetData(sf2.Objects[i]);
                        bool same = x.Length == y.Length && sf.Objects[i].PathID == sf2.Objects[i].PathID;
                        for (int k = 0; same && k < x.Length; k++) if (x[k] != y[k]) same = false;
                        if (!same) diff++;
                    }
                    Console.WriteLine("bundle {0}: {1} byte, oggetti diversi dopo rilettura={2}", comp ? "LZ4" : "non compresso", saved.Length, diff);
                    bad += diff;
                }
                node.Data = origNode;
            }
            return bad == 0 ? 0 : 2;
        }

        // Esporta ogni lockit in <outDir>/<tag>_<Kind>.tsv (chiave, tipo, testo)
        static int I2Tsv(string path, string outDir, string tag)
        {
            Directory.CreateDirectory(outDir);
            foreach (Lockit l in Loc.ReadLockits(path))
            {
                List<string[]> rows = new List<string[]>();
                foreach (Term t in l.Terms) rows.Add(new string[] { t.Key, t.Type.ToString(), t.Text });
                string f = Path.Combine(outDir, tag + "_" + Loc.Kind(l.Name) + ".tsv");
                Loc.WriteTsv(f, rows);
                Console.WriteLine("{0}: {1} termini -> {2}", l.Name, l.Terms.Count, f);
            }
            return 0;
        }

        static int I2Patterns(string path)
        {
            foreach (Lockit l in Loc.ReadLockits(path))
            {
                Console.WriteLine("=== {0} ({1} termini)", l.Name, l.Terms.Count);
                Dictionary<string, int> cnt = new Dictionary<string, int>(); Dictionary<string, Term> ex = new Dictionary<string, Term>();
                foreach (Term t in l.Terms)
                {
                    string p = Loc.Pattern(t.Key); int c; cnt.TryGetValue(p, out c); cnt[p] = c + 1;
                    if (!ex.ContainsKey(p)) ex[p] = t;
                }
                List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(cnt);
                list.Sort(delegate(KeyValuePair<string, int> x, KeyValuePair<string, int> y) { return y.Value.CompareTo(x.Value); });
                int shown = 0;
                foreach (KeyValuePair<string, int> kv in list)
                {
                    if (kv.Value < 3 && ++shown > 40) continue;
                    string s = ex[kv.Key].Text ?? ""; if (s.Length > 70) s = s.Substring(0, 70) + "…";
                    Console.WriteLine("{0,6}  {1,-50} es: {2} => {3}", kv.Value, kv.Key, ex[kv.Key].Key, s.Replace("\n", "\\n"));
                }
            }
            return 0;
        }

        static string Field(OMap o, string title)
        {
            foreach (object fo in o.L("fields")) { OMap f = (OMap)fo; if (f.S("title") == title) return f.S("value"); }
            return null;
        }

        // Riassume i campi presenti nel database dei dialoghi: titolo, quante volte è valorizzato, esempio
        static int DbFields(string path)
        {
            Bundle b; SerializedFile sf = LoadAny(path, out b);
            OMap db = null;
            foreach (SObject o in sf.Objects) if (sf.Types[o.TypeID].ClassID == 114) db = (OMap)sf.Read(o);
            string[] groups = new string[] { "actors", "items", "variables", "conversations" };
            foreach (string g in groups)
            {
                List<object> list = db.L(g);
                Console.WriteLine("=== {0}: {1}", g, list.Count);
                if (g == "variables") continue;
                FieldSummary(list);
            }
            List<object> entries = new List<object>();
            foreach (object c in db.L("conversations")) entries.AddRange(((OMap)c).L("dialogueEntries"));
            Console.WriteLine("=== dialogueEntries: {0}", entries.Count);
            FieldSummary(entries);
            return 0;
        }

        static void FieldSummary(List<object> list)
        {
            Dictionary<string, int> total = new Dictionary<string, int>(), filled = new Dictionary<string, int>();
            Dictionary<string, string> ex = new Dictionary<string, string>(); List<string> order = new List<string>();
            foreach (object o in list)
                foreach (object fo in ((OMap)o).L("fields"))
                {
                    OMap f = (OMap)fo; string t = Loc.Pattern(f.S("title")); string v = f.S("value");
                    if (!total.ContainsKey(t)) { total[t] = 0; filled[t] = 0; order.Add(t); }
                    total[t]++;
                    if (v.Length > 0) { filled[t]++; if (!ex.ContainsKey(t) || (ex[t].Length < 12 && v.Length > ex[t].Length)) ex[t] = v; }
                }
            foreach (string t in order)
            {
                string s = ex.ContainsKey(t) ? ex[t] : ""; if (s.Length > 90) s = s.Substring(0, 90) + "…";
                Console.WriteLine("  {0,-34} tot={1,-7} pieni={2,-7} es: {3}", t, total[t], filled[t], s.Replace("\n", "\\n"));
            }
        }

        // Legge un oggetto di un file senza TypeTree usando il TypeTree di un oggetto "donatore" della stessa classe
        static object ReadWithDonor(string assetsPath, long pathID, List<TypeNode> nodes)
        {
            SerializedFile sf = SerializedFile.Load(File.ReadAllBytes(assetsPath));
            SObject o = sf.Find(pathID);
            if (o == null) throw new Exception("pathID non trovato: " + pathID);
            BinReader r = new BinReader(sf.GetData(o), sf.Endian != 0);
            int i = 0; object v = TypeTreeIO.Read(nodes, ref i, r);
            if (r.P != r.B.Length) Console.Error.WriteLine("ATTENZIONE: letti {0} byte su {1}", r.P, r.B.Length);
            return v;
        }

        static List<TypeNode> LanguageSourceNodes(string donorBundle)
        {
            SerializedFile d = SerializedFile.Load(Bundle.Load(donorBundle).FindSerialized().Data);
            foreach (SObject o in d.Objects) if (Loc.IsLanguageSource(d, o)) return d.Types[o.TypeID].Nodes;
            throw new Exception("Nessun LanguageSourceAsset nel donatore");
        }

        // Esporta in TSV la sorgente I2 inglese incorporata in resources.assets (una colonna per lingua)
        static int I2TsvRaw(string assetsPath, long pathID, string donorBundle, string outTsv)
        {
            OMap m = (OMap)ReadWithDonor(assetsPath, pathID, LanguageSourceNodes(donorBundle));
            OMap src = m.M("mSource");
            StringBuilder langs = new StringBuilder();
            foreach (object lo in src.L("mLanguages")) { OMap l = (OMap)lo; langs.Append(l.S("Name") + "/" + l.S("Code") + " "); }
            Console.WriteLine("{0}: lingue = {1}", m.S("m_Name"), langs);
            List<string[]> rows = new List<string[]>();
            foreach (object to in src.L("mTerms"))
            {
                OMap t = (OMap)to; List<object> ls = t.L("Languages");
                string[] row = new string[2 + ls.Count]; row[0] = t.S("Term"); row[1] = ((int)t["TermType"]).ToString();
                for (int i = 0; i < ls.Count; i++) row[2 + i] = (string)ls[i];
                rows.Add(row);
            }
            Loc.WriteTsv(outTsv, rows);
            Console.WriteLine("{0} termini -> {1}", rows.Count, outTsv);
            return 0;
        }

        static string FindDialogueBundle(string gameDir)
        {
            string[] f = Directory.GetFiles(Path.Combine(gameDir, @"disco_Data\StreamingAssets\aa\StandaloneWindows64"), "dialoguebundle_assets_all_*.bundle");
            if (f.Length != 1) throw new Exception("dialoguebundle non trovato (o più di uno)");
            return f[0];
        }

        static long FindMono(SerializedFile sf, string name)
        {
            foreach (SObject o in sf.Objects)
                if (sf.Types[o.TypeID].ClassID == 114 && MonoName(sf, o) == name) return o.PathID;
            throw new Exception("MonoBehaviour non trovato: " + name);
        }

        // Tabella unica: chiave, tipo, EN, ES, IT(vecchia patch), attore, conversazione
        static int Master(string gameDir, string patchLockit, string outDir)
        {
            Directory.CreateDirectory(outDir);
            string lockitEs = Path.Combine(gameDir, @"disco_Data\StreamingAssets\AssetBundles\Windows\lockits\spanish");
            string resources = Path.Combine(gameDir, @"disco_Data\resources.assets");
            List<TypeNode> lsNodes = LanguageSourceNodes(lockitEs);
            SerializedFile rs = SerializedFile.Load(File.ReadAllBytes(resources));
            Dictionary<string, string> enGeneral = new Dictionary<string, string>();
            {
                SObject o = rs.Find(FindMono(rs, "GeneralLockitEnglish"));
                BinReader r = new BinReader(rs.GetData(o), rs.Endian != 0); int i = 0;
                OMap m = (OMap)TypeTreeIO.Read(lsNodes, ref i, r);
                foreach (object to in m.M("mSource").L("mTerms"))
                {
                    OMap t = (OMap)to; List<object> ls = t.L("Languages");
                    enGeneral[t.S("Term")] = ls.Count > 0 ? (string)ls[0] : "";
                }
            }
            Dictionary<string, string[]> enDlg = Loc.ReadDialogueEnglish(FindDialogueBundle(gameDir));
            Dictionary<string, Lockit> it = new Dictionary<string, Lockit>();
            foreach (Lockit l in Loc.ReadLockits(patchLockit)) it[Loc.Kind(l.Name)] = l;
            foreach (Lockit es in Loc.ReadLockits(lockitEs))
            {
                string kind = Loc.Kind(es.Name);
                if (kind != "General" && kind != "Dialogues") continue;
                Lockit itl = it[kind];
                List<string[]> rows = new List<string[]>(); int noEn = 0, noIt = 0;
                foreach (Term t in es.Terms)
                {
                    string en = null, actor = "", conv = "";
                    if (kind == "General") enGeneral.TryGetValue(t.Key, out en);
                    else { string[] v; if (enDlg.TryGetValue(t.Key, out v)) { en = v[0]; actor = v[1]; conv = v[2]; } }
                    if (en == null) noEn++;
                    Term ti; itl.ByKey.TryGetValue(t.Key, out ti);
                    if (ti == null) noIt++;
                    rows.Add(new string[] { t.Key, t.Type.ToString(), en ?? "<<NO_EN>>", t.Text ?? "", ti == null ? "<<NO_IT>>" : (ti.Text ?? ""), actor, conv });
                }
                string f = Path.Combine(outDir, "master_" + kind + ".tsv");
                Loc.WriteTsv(f, rows);
                Console.WriteLine("{0}: {1} termini, senza EN={2}, senza IT={3} -> {4}", kind, rows.Count, noEn, noIt, f);
            }
            return 0;
        }

        // Nome di un MonoBehaviour senza TypeTree: m_GameObject(12) m_Enabled(4) m_Script(12) m_Name(string)
        static string MonoName(SerializedFile sf, SObject o)
        {
            if (o.ByteSize < 32) return null;
            BinReader r = new BinReader(sf.Raw, sf.Endian != 0); r.P = (int)o.ByteStart + 28;
            int len = r.I32();
            if (len < 0 || len > 200 || len > o.ByteSize - 32) return null;
            return Encoding.UTF8.GetString(sf.Raw, r.P, len);
        }

        // Elenca i MonoBehaviour di un file .assets il cui nome contiene il filtro
        static int MonoList(string assetsPath, string filter)
        {
            SerializedFile sf = SerializedFile.Load(File.ReadAllBytes(assetsPath));
            foreach (SObject o in sf.Objects)
            {
                if (sf.Types[o.TypeID].ClassID != 114) continue;
                string n = MonoName(sf, o);
                if (n != null && n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    Console.WriteLine("{0,10}  {1,10}  {2}", o.PathID, o.ByteSize, n);
            }
            return 0;
        }

        // Confronta due file (bundle o .assets) oggetto per oggetto
        static int CmpAssets(string pa, string pb)
        {
            Bundle ba, bb; SerializedFile a = LoadAny(pa, out ba), b = LoadAny(pb, out bb);
            Console.WriteLine("A: {0} oggetti, fileSize header={1}, reale={2}, dataOffset={3}", a.Objects.Count, a.FileSize, a.Raw.Length, a.DataOffset);
            Console.WriteLine("B: {0} oggetti, fileSize header={1}, reale={2}, dataOffset={3}", b.Objects.Count, b.FileSize, b.Raw.Length, b.DataOffset);
            bool metaSame = a.DataOffset == b.DataOffset;
            if (metaSame)
                for (int i = 48; i < a.ObjTablePos; i++) if (a.Raw[i] != b.Raw[i]) { metaSame = false; break; }
            if (metaSame)
                for (int i = a.ObjTableEnd; i < a.MetaEnd; i++) if (a.Raw[i] != b.Raw[i]) { metaSame = false; break; }
            Console.WriteLine("metadati (tipi, riferimenti esterni) identici: {0}", metaSame);
            int diff = 0, same = 0, missing = 0;
            foreach (SObject o in a.Objects)
            {
                SObject p = b.Find(o.PathID);
                if (p == null) { missing++; continue; }
                byte[] x = a.GetData(o), y = b.GetData(p);
                bool eq = x.Length == y.Length && o.TypeID == p.TypeID;
                for (int i = 0; eq && i < x.Length; i++) if (x[i] != y[i]) eq = false;
                if (eq) same++;
                else { diff++; Console.WriteLine("  diverso: pathID {0} ({1}) {2} -> {3} byte  offset {4} -> {5}", o.PathID, ClassName(a.Types[o.TypeID].ClassID), x.Length, y.Length, o.ByteStart, p.ByteStart); }
                if (p.ByteStart + p.ByteSize > b.Raw.Length) Console.WriteLine("  FUORI DAL FILE: pathID {0}", p.PathID);
            }
            Console.WriteLine("identici={0} diversi={1} mancanti in B={2}", same, diff, missing);
            return 0;
        }

        // Mostra le etichette skill della scheda personaggio per le lingue indicate
        static int SkillLabelsDump(string resources, string[] langs)
        {
            SerializedFile sf = SerializedFile.Load(File.ReadAllBytes(resources));
            foreach (string lang in langs)
            {
                SObject o = sf.Find(FindMono(sf, "CharsheetSkillLabels" + lang));
                byte[] d = sf.GetData(o);
                SkillLabels s = SkillLabels.Parse(d);
                byte[] again = s.Build(); bool same = again.Length == d.Length;
                for (int i = 0; same && i < d.Length; i++) if (d[i] != again[i]) same = false;
                Console.WriteLine("=== {0} ({1} etichette, round-trip {2})", s.Name, s.Items.Count, same ? "OK" : "DIVERSO");
                foreach (SkillLabel l in s.Items)
                {
                    StringBuilder f = new StringBuilder();
                    foreach (float x in l.F) f.Append(x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(' ');
                    Console.WriteLine("  {0,2} {1,-28} len={2,-2} {3}", l.Id, l.Text.Replace("\n", "|"), l.Text.Replace("\n", "").Length, f);
                }
            }
            return 0;
        }

        // Elenca texture e sprite di un bundle con formato, dimensioni e origine dei dati
        static int TexList(string path)
        {
            Bundle b; SerializedFile sf = LoadAny(path, out b);
            Dictionary<long, string> texName = new Dictionary<long, string>();
            foreach (SObject o in sf.Objects)
            {
                if (sf.Types[o.TypeID].ClassID != 28) continue;
                OMap m = (OMap)sf.Read(o);
                OMap sd = m.M("m_StreamData");
                texName[o.PathID] = m.S("m_Name");
                Console.WriteLine("TEX {0,-52} {1,4}x{2,-4} fmt={3,-3} mips={4} inline={5} stream=({6},{7}) {8}",
                    m.S("m_Name"), m["m_Width"], m["m_Height"], m["m_TextureFormat"], m["m_MipCount"],
                    ((Typeless)m["image data"]).Data.Length, sd["offset"], sd["size"], sd.S("path"));
            }
            foreach (SObject o in sf.Objects)
            {
                if (sf.Types[o.TypeID].ClassID != 213) continue;
                OMap m = (OMap)sf.Read(o);
                OMap r = m.M("m_Rect"); OMap rd = m.M("m_RD"); OMap tr = rd.M("textureRect");
                long tex = (long)rd.M("texture")["m_PathID"]; string tn; texName.TryGetValue(tex, out tn);
                Console.WriteLine("SPR {0,-44} rect=({1},{2},{3},{4}) texRect=({5},{6},{7},{8}) tex={9} atlasTags={10} ppu={11}",
                    m.S("m_Name"), r["x"], r["y"], r["width"], r["height"], tr["x"], tr["y"], tr["width"], tr["height"], tn ?? tex.ToString(),
                    m.L("m_AtlasTags").Count, m["m_PixelsToUnits"]);
            }
            return 0;
        }

        static int HexDump(string assetsPath, long pathID, int max)
        {
            SerializedFile sf = SerializedFile.Load(File.ReadAllBytes(assetsPath));
            SObject o = sf.Find(pathID);
            byte[] d = sf.GetData(o);
            SType t = sf.Types[o.TypeID];
            Console.WriteLine("pathID {0} class {1} size {2} scriptTypeIndex {3}", pathID, t.ClassID, d.Length, t.ScriptTypeIndex);
            for (int i = 0; i < d.Length && i < max; i += 16)
            {
                StringBuilder hx = new StringBuilder(), tx = new StringBuilder();
                for (int k = i; k < i + 16 && k < d.Length; k++) { hx.Append(d[k].ToString("X2")).Append(' '); tx.Append(d[k] >= 32 && d[k] < 127 ? (char)d[k] : '.'); }
                Console.WriteLine("{0:X5}  {1,-48} {2}", i, hx, tx);
            }
            return 0;
        }

        static int IndexOf(byte[] hay, byte[] needle, int start)
        {
            int end = hay.Length - needle.Length; byte f = needle[0];
            for (int i = start; i <= end; i++)
            {
                if (hay[i] != f) continue;
                int k = 1; while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }

        // Cerca una stringa UTF-8 nei file indicati (bundle decompressi o .assets nudi) e dice in quale oggetto cade
        static int Grep(string needle, string[] files, int maxHits)
        {
            byte[] nb = Encoding.UTF8.GetBytes(needle);
            foreach (string file in files)
            {
                byte[] f;
                try { f = File.ReadAllBytes(file); } catch (Exception e) { Console.WriteLine("{0}: {1}", file, e.Message); continue; }
                List<KeyValuePair<string, byte[]>> parts = new List<KeyValuePair<string, byte[]>>();
                if (f.Length > 7 && Encoding.ASCII.GetString(f, 0, 7) == "UnityFS")
                {
                    try { foreach (BundleNode n in Bundle.Load(f).Nodes) parts.Add(new KeyValuePair<string, byte[]>(n.Path, n.Data)); }
                    catch (Exception e) { Console.WriteLine("{0}: {1}", file, e.Message); continue; }
                }
                else parts.Add(new KeyValuePair<string, byte[]>("", f));
                foreach (KeyValuePair<string, byte[]> part in parts)
                {
                    byte[] d = part.Value; int pos = 0, hits = 0; SerializedFile sf = null; bool tried = false;
                    while ((pos = IndexOf(d, nb, pos)) >= 0)
                    {
                        if (++hits > maxHits) { Console.WriteLine("  ... altri risultati omessi"); break; }
                        if (!tried) { tried = true; try { sf = SerializedFile.Load(d); } catch { sf = null; } }
                        string where = "";
                        if (sf != null)
                            foreach (SObject o in sf.Objects)
                                if (pos >= o.ByteStart && pos < o.ByteStart + o.ByteSize)
                                { where = string.Format(" [pathID {0} {1} size={2}]", o.PathID, ClassName(sf.Types[o.TypeID].ClassID), o.ByteSize); break; }
                        int a0 = Math.Max(0, pos - 40), a1 = Math.Min(d.Length, pos + nb.Length + 60);
                        StringBuilder ctx = new StringBuilder();
                        foreach (char c in Encoding.UTF8.GetString(d, a0, a1 - a0)) ctx.Append(c < 32 || c == 0xFFFD ? '·' : c);
                        Console.WriteLine("{0} {1} @{2}{3}: {4}", Path.GetFileName(file), part.Key, pos, where, ctx);
                        pos += nb.Length;
                    }
                }
            }
            return 0;
        }

        public static int Main(string[] a)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                if (a.Length >= 5 && a[0] == "i2tsvraw") return I2TsvRaw(a[1], long.Parse(a[2]), a[3], a[4]);
                if (a.Length >= 4 && a[0] == "master") return Master(a[1], a[2], a[3]);
                if (a.Length >= 2 && a[0] == "qa") return Qa.Run(a[1], a.Length > 2 ? a[2] : null);
                if (a.Length >= 4 && a[0] == "fixmerge") return Build.FixMerge(a[1], a[2], a[3]);
                if (a.Length >= 3 && a[0] == "mkitalian") return Build.MkItalian(a[1], a[2]);
                if (a.Length >= 4 && a[0] == "mkdyslexic") return Build.MkDyslexic(a[1], a[2], a[3]);
                if (a.Length >= 4 && a[0] == "mkpayload")
                {
                    string[] specs = new string[a.Length - 4]; Array.Copy(a, 4, specs, 0, specs.Length);
                    return Build.MkPayload(a[1], a[2], a[3], specs);
                }
                if (a.Length >= 3 && a[0] == "glyphs") return Build.Glyphs(a[1], a[2]);
                if (a.Length >= 2 && a[0] == "mkicon") return Build.MkIcon(a[1]);
                if (a.Length >= 3 && a[0] == "testinstall") return Build.TestInstall(a[1], a[2], false);
                if (a.Length >= 3 && a[0] == "testuninstall") return Build.TestInstall(a[1], a[2], true);
                if (a.Length >= 3 && a[0] == "todo") return Qa.Todo(a[1], a[2]);
                if (a.Length >= 3 && a[0] == "gloss")
                {
                    string[] cand = a.Length > 3 && a[3].Length > 0 ? a[3].Split('|') : new string[0];
                    return Qa.Gloss(a[1], a[2], cand, a.Length > 4 ? int.Parse(a[4]) : 3);
                }
                if (a.Length >= 2 && a[0] == "texlist") return TexList(a[1]);
                if (a.Length >= 3 && a[0] == "cmp") return CmpAssets(a[1], a[2]);
                if (a.Length >= 3 && a[0] == "skilllabels") return SkillLabelsDump(a[1], a[2].Split(','));
                if (a.Length >= 3 && a[0] == "monolist") return MonoList(a[1], a[2]);
                if (a.Length >= 3 && a[0] == "hexdump") return HexDump(a[1], long.Parse(a[2]), a.Length > 3 ? int.Parse(a[3]) : 4096);
                if (a.Length >= 3 && a[0] == "grep")
                {
                    List<string> files = new List<string>();
                    for (int i = 2; i < a.Length; i++)
                    {
                        string dir = Path.GetDirectoryName(a[i]); string pat = Path.GetFileName(a[i]);
                        if (pat.IndexOf('*') >= 0) files.AddRange(Directory.GetFiles(dir, pat)); else files.Add(a[i]);
                    }
                    return Grep(a[1], files.ToArray(), 6);
                }
                if (a.Length >= 4 && a[0] == "i2tsv") return I2Tsv(a[1], a[2], a[3]);
                if (a.Length >= 2 && a[0] == "i2patterns") return I2Patterns(a[1]);
                if (a.Length >= 2 && a[0] == "dbfields") return DbFields(a[1]);
                if (a.Length >= 2 && a[0] == "info") return Info(a[1], a.Length > 2 && a[2] == "-n");
                if (a.Length >= 4 && a[0] == "dump")
                    return Dump(a[1], long.Parse(a[2]), a[3], a.Length > 4 ? int.Parse(a[4]) : 0, a.Length > 5 ? int.Parse(a[5]) : 0);
                if (a.Length >= 3 && a[0] == "typetree") return TypeTreeDump(a[1], long.Parse(a[2]));
                if (a.Length >= 2 && a[0] == "roundtrip") return RoundTrip(a[1]);
                Console.Error.WriteLine("uso: detool info <file> [-n] | dump <file> <pathID> <out.json> [maxList] [maxStr] | typetree <file> <pathID> | roundtrip <file>");
                return 64;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERRORE: " + e);
                return 1;
            }
        }
    }
}
