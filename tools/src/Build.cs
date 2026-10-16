// Comandi di costruzione della patch: unione delle correzioni, testo italiano finale, payload, bundle immagini.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DE
{
    public static class Build
    {
        static int CountChar(string s, char c) { int n = 0; foreach (char x in s) if (x == c) n++; return n; }
        static readonly Regex Placeholder = new Regex(@"\{[0-9A-Za-z_]+\}");

        static string Multiset(MatchCollection mc)
        {
            List<string> l = new List<string>(); foreach (Match m in mc) l.Add(m.Value);
            l.Sort(StringComparer.Ordinal); return string.Join(" ", l.ToArray());
        }

        // lotti "n<TAB>testo" (n = riga di da_rivedere.tsv) -> correzioni.tsv "tipo<TAB>chiave<TAB>testo", con controlli sul testo
        public static int FixMerge(string todoFile, string lottiDir, string outFile)
        {
            Dictionary<string, string[]> todo = new Dictionary<string, string[]>();
            foreach (string[] r in Loc.ReadTsv(todoFile)) todo[r[0]] = r;
            List<string[]> outRows = new List<string[]>(); Dictionary<string, bool> seen = new Dictionary<string, bool>();
            int warn = 0;
            string[] files = Directory.GetFiles(lottiDir, "*.tsv"); Array.Sort(files, StringComparer.Ordinal);
            foreach (string f in files)
                foreach (string[] r in Loc.ReadTsv(f))
                {
                    if (r.Length < 2) { Console.WriteLine("RIGA NON VALIDA in {0}: {1}", Path.GetFileName(f), r[0]); warn++; continue; }
                    string[] t;
                    if (!todo.TryGetValue(r[0], out t)) { Console.WriteLine("NUMERO SCONOSCIUTO {0} in {1}", r[0], Path.GetFileName(f)); warn++; continue; }
                    if (seen.ContainsKey(r[0])) { Console.WriteLine("DUPLICATO {0} in {1}", r[0], Path.GetFileName(f)); warn++; continue; }
                    seen[r[0]] = true;
                    string en = t[6], it = r[1];
                    List<string> p = new List<string>();
                    if (en != "<<NO_EN>>")
                    {
                        if (CountChar(en, '*') % 2 != CountChar(it, '*') % 2) p.Add("asterischi");
                        if (CountChar(en, '"') % 2 == 0 && CountChar(it, '"') % 2 != 0) p.Add("virgolette");
                        if (CountChar(en, '[') != CountChar(it, '[') || CountChar(en, ']') != CountChar(it, ']')) p.Add("quadre");
                        if (CountChar(en, '(') != CountChar(it, '(') || CountChar(en, ')') != CountChar(it, ')')) p.Add("tonde");
                        if (Multiset(Placeholder.Matches(en)) != Multiset(Placeholder.Matches(it))) p.Add("segnaposto");
                        if (CountChar(en, '\n') != CountChar(it, '\n')) p.Add("a-capo");
                    }
                    if (it != it.Trim()) p.Add("spazi-estremi");
                    if (it.Contains("  ")) p.Add("doppio-spazio");
                    if (p.Count > 0) { Console.WriteLine("CONTROLLA {0} [{1}]\n   EN: {2}\n   IT: {3}", r[0], string.Join(",", p.ToArray()), en, it); warn++; }
                    outRows.Add(new string[] { t[2], t[3], it });
                }
            Loc.WriteTsv(outFile, outRows);
            Console.WriteLine("{0} correzioni -> {1} ({2} da controllare)", outRows.Count, outFile, warn);
            return 0;
        }

        // master (EN/ES/IT vecchia) + correzioni -> italiano_<Tipo>.tsv (chiave, italiano, inglese di riferimento)
        public static int MkItalian(string workDir, string tradDir)
        {
            Dictionary<string, string> fix = new Dictionary<string, string>();
            string cf = Path.Combine(tradDir, "correzioni.tsv");
            if (File.Exists(cf)) foreach (string[] r in Loc.ReadTsv(cf)) fix[r[0] + "\t" + r[1]] = r.Length > 2 ? r[2] : "";
            // revisioni manuali per chiave (tipo<TAB>chiave<TAB>testo): hanno la precedenza sulle correzioni automatiche
            string revDir = Path.Combine(tradDir, "revisioni");
            if (Directory.Exists(revDir))
            {
                string[] files = Directory.GetFiles(revDir, "*.tsv"); Array.Sort(files, StringComparer.Ordinal);
                foreach (string f in files)
                    foreach (string[] r in Loc.ReadTsv(f))
                    {
                        if (r.Length < 3) { Console.WriteLine("RIGA NON VALIDA in {0}: {1}", Path.GetFileName(f), r[0]); continue; }
                        fix[r[0] + "\t" + r[1]] = r[2];
                    }
            }
            int used = 0;
            foreach (string kind in new string[] { "General", "Dialogues" })
            {
                List<string[]> rows = new List<string[]>(); int fromFix = 0, fromOld = 0, missing = 0;
                foreach (string[] r in Loc.ReadTsv(Path.Combine(workDir, "master_" + kind + ".tsv")))
                {
                    string key = r[0], en = r[2], it = r[4], f;
                    if (fix.TryGetValue(kind + "\t" + key, out f)) { it = f; fromFix++; }
                    else if (it == "<<NO_IT>>") { missing++; continue; }
                    else fromOld++;
                    rows.Add(new string[] { key, it, en == "<<NO_EN>>" ? "" : en });
                }
                used += fromFix;
                string o = Path.Combine(tradDir, "italiano_" + kind + ".tsv");
                Loc.WriteTsv(o, rows);
                Console.WriteLine("{0}: {1} stringhe ({2} dalla patch originale, {3} corrette/nuove, {4} senza traduzione) -> {5}", kind, rows.Count, fromOld, fromFix, missing, o);
            }
            if (used != fix.Count) Console.WriteLine("ATTENZIONE: {0} correzioni non corrispondono a chiavi del gioco", fix.Count - used);
            return 0;
        }

        // Copia del bundle immagini italiano con l'identità del bundle "images_dyslexic/spanish" del gioco
        public static int MkDyslexic(string italianImagesBundle, string gameDyslexicBundle, string outFile)
        {
            Bundle src = Bundle.Load(italianImagesBundle), dys = Bundle.Load(gameDyslexicBundle);
            BundleNode sNode = src.FindSerialized(), dNode = dys.FindSerialized();
            string oldCab = sNode.Path, newCab = dNode.Path;
            SerializedFile dsf = SerializedFile.Load(dNode.Data);
            string abName = null, abBundleName = null;
            foreach (SObject o in dsf.Objects)
                if (dsf.Types[o.TypeID].ClassID == 142) { OMap m = (OMap)dsf.Read(o); abName = m.S("m_Name"); abBundleName = m.S("m_AssetBundleName"); }
            if (abName == null) throw new Exception("AssetBundle non trovato nel bundle dislessia");
            SerializedFile sf = SerializedFile.Load(sNode.Data);
            int streamRefs = 0;
            foreach (SObject o in sf.Objects)
            {
                int cid = sf.Types[o.TypeID].ClassID;
                if (cid == 142)
                {
                    OMap m = (OMap)sf.Read(o);
                    Console.WriteLine("AssetBundle: '{0}' / '{1}' -> '{2}' / '{3}'", m.S("m_Name"), m.S("m_AssetBundleName"), abName, abBundleName);
                    m["m_Name"] = abName; m["m_AssetBundleName"] = abBundleName;
                    sf.Write(o, m);
                }
                else if (cid == 28)
                {
                    OMap m = (OMap)sf.Read(o); OMap sd = m.M("m_StreamData");
                    string p = sd.S("path");
                    if (p.Length > 0) { sd["path"] = p.Replace(oldCab, newCab); sf.Write(o, m); streamRefs++; }
                }
            }
            sNode.Data = sf.Save();
            foreach (BundleNode n in src.Nodes) n.Path = n.Path.Replace(oldCab, newCab);
            File.WriteAllBytes(outFile, src.SavePlain());
            Console.WriteLine("{0} -> {1}; riferimenti a dati esterni aggiornati: {2}; scritto {3}", oldCab, newCab, streamRefs, outFile);
            return 0;
        }

        // Scrive la cartella dei dati distribuita accanto al patcher: tutto in chiaro, nessun archivio.
        // fileSpecs: "nome_nella_cartella=percorso_nel_gioco=file_sorgente"
        public static int MkPayload(string tradDir, string version, string outDir, string[] fileSpecs)
        {
            UTF8Encoding utf8 = new UTF8Encoding(false);
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "versione.txt"), version + "\n", utf8);
            foreach (string kind in new string[] { "General", "Dialogues" })
            {
                StringBuilder sb = new StringBuilder(); int n = 0;
                foreach (string[] r in Loc.ReadTsv(Path.Combine(tradDir, "italiano_" + kind + ".tsv")))
                {
                    sb.Append(Loc.Esc(r[0])).Append('\t').Append(Loc.Esc(r.Length > 1 ? r[1] : "")).Append('\n'); n++;
                }
                File.WriteAllText(Path.Combine(outDir, kind.ToLowerInvariant() + ".tsv"), sb.ToString(), utf8);
                Console.WriteLine("{0}: {1} stringhe", kind, n);
            }
            File.Copy(Path.Combine(tradDir, "etichette_skill.tsv"), Path.Combine(outDir, "skills.tsv"));
            File.Copy(Path.Combine(tradDir, "nomi_lingue.tsv"), Path.Combine(outDir, "langnames.tsv"));
            StringBuilder list = new StringBuilder();
            foreach (string spec in fileSpecs)
            {
                string[] s = spec.Split('=');
                File.Copy(s[2], Path.Combine(outDir, s[0]));
                list.Append(s[0]).Append('\t').Append(s[1].Replace('\\', '/')).Append('\n');
                Console.WriteLine("file {0} -> {1}", s[0], s[1]);
            }
            File.WriteAllText(Path.Combine(outDir, "file.tsv"), list.ToString(), utf8);
            long total = 0; foreach (string f in Directory.GetFiles(outDir)) total += new FileInfo(f).Length;
            Payload.LoadFolder(outDir); // verifica che la cartella si rilegga
            Console.WriteLine("dati: {0:N0} byte -> {1}", total, outDir);
            return 0;
        }

        // Verifica che i caratteri usati nel testo italiano esistano nei font TextMeshPro del gioco
        public static int Glyphs(string fontsBundle, string tradDir)
        {
            Dictionary<int, int> used = new Dictionary<int, int>(); Dictionary<int, string> example = new Dictionary<int, string>();
            foreach (string kind in new string[] { "General", "Dialogues" })
                foreach (string[] r in Loc.ReadTsv(Path.Combine(tradDir, "italiano_" + kind + ".tsv")))
                {
                    if (r.Length < 2) continue;
                    foreach (char c in r[1])
                    {
                        if (c < 32 || char.IsSurrogate(c)) continue;
                        int n; used.TryGetValue(c, out n); used[c] = n + 1;
                        if (!example.ContainsKey(c)) example[c] = r[0];
                    }
                }
            foreach (string[] r in Loc.ReadTsv(Path.Combine(tradDir, "etichette_skill.tsv")))
                foreach (char c in r[1]) { if (c < 32) continue; int n; used.TryGetValue(c, out n); used[c] = n + 1; if (!example.ContainsKey(c)) example[c] = "etichette_skill"; }
            Console.WriteLine("caratteri distinti nel testo italiano: {0}", used.Count);
            SerializedFile sf = SerializedFile.Load(Bundle.Load(fontsBundle).FindSerialized().Data);
            foreach (SObject o in sf.Objects)
            {
                if (sf.Types[o.TypeID].ClassID != 114) continue;
                OMap m = (OMap)sf.Read(o);
                if (!m.Has("m_CharacterTable")) continue;
                HashSet<int> have = new HashSet<int>();
                foreach (object co in m.L("m_CharacterTable")) have.Add((int)(uint)((OMap)co)["m_Unicode"]);
                StringBuilder miss = new StringBuilder(); int missCount = 0, missUses = 0;
                foreach (KeyValuePair<int, int> kv in used)
                {
                    if (have.Contains(kv.Key)) continue;
                    missCount++; missUses += kv.Value;
                    miss.AppendFormat(" '{0}'(U+{1:X4} x{2})", (char)kv.Key, kv.Key, kv.Value);
                }
                Console.WriteLine("{0,-28} glifi={1,-5} fallback={2} mancanti={3} ({4} occorrenze):{5}", m.S("m_Name"), have.Count,
                    m.Has("m_FallbackFontAssetTable") ? m.L("m_FallbackFontAssetTable").Count : -1, missCount, missUses, miss);
            }
            return 0;
        }

        // ---- icona del patcher (disegnata qui: nessuna immagine del gioco) ----

        static System.Drawing.Bitmap DrawIcon(int s)
        {
            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(System.Drawing.Color.Transparent);
                float r = s * 0.17f, d = r * 2, w = s - 1;
                using (System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    p.AddArc(0, 0, d, d, 180, 90); p.AddArc(w - d, 0, d, d, 270, 90); p.AddArc(w - d, w - d, d, d, 0, 90); p.AddArc(0, w - d, d, d, 90, 90);
                    p.CloseFigure();
                    using (System.Drawing.Drawing2D.LinearGradientBrush b = new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(0, 0, s, s),
                        System.Drawing.Color.FromArgb(0x2A, 0x2E, 0x33), System.Drawing.Color.FromArgb(0x0E, 0x10, 0x11), 65f)) g.FillPath(b, p);
                    g.SetClip(p);
                    System.Drawing.Color[] four = new System.Drawing.Color[] {
                        System.Drawing.Color.FromArgb(0x5B, 0xC0, 0xD6), System.Drawing.Color.FromArgb(0x7A, 0x5C, 0xD0),
                        System.Drawing.Color.FromArgb(0xCB, 0x45, 0x6B), System.Drawing.Color.FromArgb(0xE2, 0xB5, 0x34) };
                    float sh = Math.Max(2f, (float)Math.Round(s * 0.15f)), seg = s / 4f;
                    for (int i = 0; i < 4; i++)
                        using (System.Drawing.SolidBrush b = new System.Drawing.SolidBrush(four[i])) g.FillRectangle(b, i * seg, s - sh, seg + 1, sh);
                    g.ResetClip();
                    using (System.Drawing.Font f = new System.Drawing.Font("Segoe UI", s * 0.50f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
                    using (System.Drawing.StringFormat sf = new System.Drawing.StringFormat(System.Drawing.StringFormat.GenericTypographic))
                    using (System.Drawing.SolidBrush b = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xE9, 0xE4, 0xD6)))
                    {
                        sf.Alignment = System.Drawing.StringAlignment.Center; sf.LineAlignment = System.Drawing.StringAlignment.Center;
                        g.DrawString("IT", f, b, new System.Drawing.RectangleF(0, -s * 0.03f, s, s - sh), sf);
                    }
                }
            }
            return bmp;
        }

        public static int MkIcon(string outFile)
        {
            int[] sizes = new int[] { 16, 24, 32, 48, 64, 256 };
            List<byte[]> images = new List<byte[]>();
            foreach (int s in sizes)
                using (System.Drawing.Bitmap bmp = DrawIcon(s))
                {
                    if (s == 256)
                    {
                        using (MemoryStream ms = new MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); images.Add(ms.ToArray()); }
                        continue;
                    }
                    // formato DIB: intestazione da 40 byte (altezza doppia), pixel BGRA dal basso verso l'alto, maschera AND vuota
                    int maskRow = ((s + 31) / 32) * 4;
                    using (MemoryStream ms = new MemoryStream())
                    using (BinaryWriter w = new BinaryWriter(ms))
                    {
                        w.Write(40); w.Write(s); w.Write(s * 2); w.Write((short)1); w.Write((short)32); w.Write(0); w.Write(s * s * 4 + maskRow * s);
                        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                        for (int y = s - 1; y >= 0; y--)
                            for (int x = 0; x < s; x++) w.Write(bmp.GetPixel(x, y).ToArgb());
                        w.Write(new byte[maskRow * s]);
                        w.Flush(); images.Add(ms.ToArray());
                    }
                }
            using (FileStream fs = File.Create(outFile))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32); w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
            }
            using (System.Drawing.Bitmap big = DrawIcon(256)) big.Save(Path.ChangeExtension(outFile, ".png"), System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine("icona -> {0}", outFile);
            return 0;
        }

        public static int TestInstall(string payloadDir, string gameDir, bool uninstall)
        {
            Payload p = Payload.LoadFolder(payloadDir);
            PatchEngine e = new PatchEngine(); e.Log = delegate(string s) { Console.WriteLine(s); };
            if (uninstall) e.Uninstall(gameDir); else e.Install(gameDir, p);
            InstallStatus st = PatchEngine.GetStatus(gameDir);
            Console.WriteLine("stato: installata={0} parziale={1} versione={2}", st.Installed, st.Partial, st.Version);
            return 0;
        }
    }
}
