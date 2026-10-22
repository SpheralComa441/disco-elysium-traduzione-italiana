// Motore della patch: contenitore dei dati (Payload) e logica di installazione/ripristino (PatchEngine).
// Usato sia dagli strumenti di sviluppo sia dal patcher con interfaccia grafica.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DE
{
    // I dati della traduzione: una cartella di file in chiaro accanto al programma.
    //   versione.txt, general.tsv, dialogues.tsv (chiave<TAB>testo), skills.tsv, langnames.tsv,
    //   file.tsv (nome del file nella cartella<TAB>percorso di destinazione nel gioco) e i file elencati.
    public class Payload
    {
        public const string FolderName = "dati";
        public static readonly string[] Required = new string[] { "versione.txt", "general.tsv", "dialogues.tsv", "skills.tsv", "langnames.tsv", "file.tsv" };
        public Dictionary<string, byte[]> Entries = new Dictionary<string, byte[]>();
        public List<string> Order = new List<string>();

        public void Set(string name, byte[] data) { if (!Entries.ContainsKey(name)) Order.Add(name); Entries[name] = data; }
        public string Text(string name) { return Encoding.UTF8.GetString(Entries[name]).TrimStart('﻿'); }
        public bool Has(string name) { return Entries.ContainsKey(name); }

        public static Payload LoadFolder(string dir)
        {
            if (!Directory.Exists(dir)) throw new PatchException("Cartella dei dati non trovata: " + dir);
            Payload p = new Payload();
            foreach (string n in Required)
            {
                string f = Path.Combine(dir, n);
                if (!File.Exists(f)) throw new PatchException("File mancante nella cartella dei dati: " + n);
                p.Set(n == "versione.txt" ? "version" : n, File.ReadAllBytes(f));
            }
            // file.tsv: nome nella cartella dei dati <TAB> destinazione nel gioco (con le barre "/")
            foreach (string raw in p.Text("file.tsv").Split('\n'))
            {
                string[] r = raw.TrimEnd('\r').Split('\t');
                if (r.Length < 2 || r[0].Length == 0) continue;
                string name = r[0], rel = r[1].Replace('/', '\\');
                // la destinazione deve stare dentro i dati del gioco: niente percorsi assoluti o risalite di cartella
                if (name.IndexOfAny(new char[] { '\\', '/', ':' }) >= 0 || Path.IsPathRooted(rel) || rel.Contains("..") || rel.Contains(":") ||
                    !rel.StartsWith(@"disco_Data\StreamingAssets\", StringComparison.OrdinalIgnoreCase))
                    throw new PatchException("Voce non valida in file.tsv: " + raw.Trim());
                string f = Path.Combine(dir, name);
                if (!File.Exists(f)) throw new PatchException("File mancante nella cartella dei dati: " + name);
                p.Set("file:" + rel, File.ReadAllBytes(f));
            }
            return p;
        }

        // righe "chiave<TAB>testo" con escape \n \t \\
        public Dictionary<string, string> Strings(string name)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string raw in Text(name).Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int t = line.IndexOf('\t');
                if (t <= 0) continue;
                d[Loc.Unesc(line.Substring(0, t))] = Loc.Unesc(line.Substring(t + 1));
            }
            return d;
        }

        public List<string[]> Rows(string name)
        {
            List<string[]> rows = new List<string[]>();
            foreach (string raw in Text(name).Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                string[] p = line.Split('\t');
                for (int i = 0; i < p.Length; i++) p[i] = Loc.Unesc(p[i]);
                rows.Add(p);
            }
            return rows;
        }
    }

    public class PatchException : Exception { public PatchException(string m) : base(m) { } }

    public class InstallStatus
    {
        public bool Installed;         // i file correnti sono quelli scritti dalla patch
        public bool Partial;           // patch registrata ma almeno un file è stato cambiato (es. aggiornamento del gioco)
        public string Version = "";
        public string Date = "";
    }

    public class PatchEngine
    {
        // I percorsi interni (queste costanti e file.tsv) usano disco_Data. Unity però chiama la cartella dei dati
        // come l'eseguibile: "disco_Data" su Steam e GOG, "Disco Elysium_Data" su Epic. RealRel li adatta.
        public const string DataDirDefault = "disco_Data";
        public const string LockitRel = @"disco_Data\StreamingAssets\AssetBundles\Windows\lockits\spanish";
        public const string ResourcesRel = @"disco_Data\resources.assets";
        public const string BackupDirName = "_backup_traduzione_ITA";
        const string StateName = "stato.txt";
        const string FilePrefix = "file:";

        public Action<string> Log = delegate { };

        // Nome della cartella dei dati del gioco (disco_Data, Disco Elysium_Data...), null se la cartella non contiene il gioco
        public static string DataDirName(string gameDir)
        {
            try
            {
                if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir)) return null;
                List<string> names = new List<string>(); names.Add(DataDirDefault);
                foreach (string d in Directory.GetDirectories(gameDir, "*_Data")) names.Add(Path.GetFileName(d));
                foreach (string n in names)
                    if (File.Exists(Path.Combine(gameDir, RealRel(LockitRel, n))) && File.Exists(Path.Combine(gameDir, RealRel(ResourcesRel, n)))) return n;
            }
            catch { }
            return null;
        }

        static string RealRel(string rel, string dataDir) { return dataDir + rel.Substring(DataDirDefault.Length); }

        public static bool IsGameDir(string dir) { return DataDirName(dir) != null; }

        static string Sha(byte[] data)
        {
            using (SHA256 s = SHA256.Create())
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in s.ComputeHash(data)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static string ShaFile(string path)
        {
            using (SHA256 s = SHA256.Create())
            using (FileStream f = File.OpenRead(path))
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte b in s.ComputeHash(f)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static Dictionary<string, string> ReadState(string gameDir)
        {
            Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string f = Path.Combine(Path.Combine(gameDir, BackupDirName), StateName);
            if (!File.Exists(f)) return d;
            foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
            {
                int t = line.IndexOf('\t');
                if (t > 0) d[line.Substring(0, t)] = line.Substring(t + 1);
            }
            return d;
        }

        static void WriteState(string gameDir, Dictionary<string, string> d)
        {
            List<string> lines = new List<string>();
            foreach (KeyValuePair<string, string> kv in d) lines.Add(kv.Key + "\t" + kv.Value);
            File.WriteAllLines(Path.Combine(Path.Combine(gameDir, BackupDirName), StateName), lines.ToArray(), new UTF8Encoding(false));
        }

        // File da scrivere: i due modificati sul posto più quelli forniti interi. Un file fornito viene saltato
        // se nel gioco manca la sua cartella (versione del gioco che non ha quella funzione).
        List<string> Targets(string gameDir, string dataDir, Payload p)
        {
            List<string> t = new List<string>(); t.Add(LockitRel); t.Add(ResourcesRel);
            foreach (string n in p.Order)
            {
                if (!n.StartsWith(FilePrefix, StringComparison.Ordinal)) continue;
                string rel = n.Substring(FilePrefix.Length);
                if (Directory.Exists(Path.GetDirectoryName(Path.Combine(gameDir, RealRel(rel, dataDir))))) t.Add(rel);
                else Log("  Saltato (cartella assente in questa versione del gioco): " + rel);
            }
            return t;
        }

        public static InstallStatus GetStatus(string gameDir)
        {
            InstallStatus st = new InstallStatus();
            Dictionary<string, string> state = ReadState(gameDir);
            if (state.Count == 0) return st;
            state.TryGetValue("@versione", out st.Version); state.TryGetValue("@data", out st.Date);
            int ok = 0, total = 0;
            foreach (KeyValuePair<string, string> kv in state)
            {
                if (kv.Key.StartsWith("@", StringComparison.Ordinal)) continue;
                total++;
                string f = Path.Combine(gameDir, kv.Key);
                if (File.Exists(f) && ShaFile(f) == kv.Value) ok++;
            }
            st.Installed = total > 0 && ok == total;
            st.Partial = total > 0 && ok < total;
            return st;
        }

        // ---- trasformazioni in memoria ----

        byte[] PatchLockit(byte[] original, Payload p, out List<TypeNode> languageSourceNodes)
        {
            Bundle b = Bundle.Load(original);
            BundleNode node = b.FindSerialized();
            SerializedFile sf = SerializedFile.Load(node.Data);
            Dictionary<string, Dictionary<string, string>> tables = new Dictionary<string, Dictionary<string, string>>();
            tables["General"] = p.Strings("general.tsv"); tables["Dialogues"] = p.Strings("dialogues.tsv");
            languageSourceNodes = null;
            int replaced = 0, kept = 0, sources = 0;
            foreach (SObject o in sf.Objects)
            {
                if (!Loc.IsLanguageSource(sf, o)) continue;
                languageSourceNodes = sf.Types[o.TypeID].Nodes;
                OMap m = (OMap)sf.Read(o);
                Dictionary<string, string> table;
                if (!tables.TryGetValue(Loc.Kind(m.S("m_Name")), out table)) continue;
                sources++;
                foreach (object to in m.M("mSource").L("mTerms"))
                {
                    OMap t = (OMap)to; string it;
                    List<object> langs = t.L("Languages");
                    if (langs.Count > 0 && table.TryGetValue(t.S("Term"), out it)) { langs[0] = it; replaced++; }
                    else kept++;
                }
                sf.Write(o, m);
            }
            if (sources != 2 || replaced < 50000)
                throw new PatchException("Il file di lingua del gioco ha una struttura inattesa (sorgenti: " + sources + ", stringhe sostituite: " + replaced + ").");
            Log("  Testi: " + replaced + " stringhe tradotte" + (kept > 0 ? ", " + kept + " non presenti nella patch (restano come nel gioco)" : ""));
            node.Data = sf.Save();
            byte[] result = b.SavePlain();
            // verifica: il file prodotto deve rileggersi correttamente
            int check = 0;
            foreach (Lockit l in Loc.ReadLockits(SerializedFile.Load(Bundle.Load(result).FindSerialized().Data))) check += l.Terms.Count;
            if (check < replaced) throw new PatchException("Verifica del file di lingua fallita.");
            return result;
        }

        byte[] PatchResources(byte[] original, Payload p, List<TypeNode> lsNodes)
        {
            SerializedFile sf = SerializedFile.Load(original);
            bool big = sf.Endian != 0;
            SObject skills = null, names = null;
            foreach (SObject o in sf.Objects)
            {
                if (sf.Types[o.TypeID].ClassID != 114 || o.ByteSize < 40 || o.ByteSize > 200000) continue;
                string n = MonoName(sf, o);
                if (n == "CharsheetSkillLabelsSpanish") skills = o;
                else if (n == "LanguagesNamesLockit") names = o;
            }
            if (skills == null) throw new PatchException("Etichette delle skill non trovate in resources.assets.");

            // etichette delle skill nella scheda personaggio
            Dictionary<int, string[]> rows = new Dictionary<int, string[]>();
            foreach (string[] r in p.Rows("skills.tsv")) rows[int.Parse(r[0], CultureInfo.InvariantCulture)] = r;
            SkillLabels sl = SkillLabels.Parse(sf.GetData(skills));
            int done = 0;
            foreach (SkillLabel l in sl.Items)
            {
                string[] r;
                if (!rows.TryGetValue(l.Id, out r)) continue;
                l.Text = r[1];
                for (int k = 0; k < 9; k++) l.F[k] = float.Parse(r[2 + k], CultureInfo.InvariantCulture);
                done++;
            }
            if (done < 20) throw new PatchException("Etichette delle skill: struttura inattesa.");
            skills.NewData = sl.Build();
            SkillLabels.Parse(skills.NewData);
            Log("  Scheda personaggio: " + done + " etichette skill tradotte");

            // nome della lingua nel menu (Español -> Italiano)
            if (names != null && lsNodes != null)
            {
                try
                {
                    BinReader br = new BinReader(sf.GetData(names), big); int i = 0;
                    OMap m = (OMap)TypeTreeIO.Read(lsNodes, ref i, br);
                    if (br.P != br.B.Length) throw new InvalidDataException("lunghezza inattesa");
                    OMap src = m.M("mSource");
                    Dictionary<string, int> langIndex = new Dictionary<string, int>();
                    List<object> langs = src.L("mLanguages");
                    for (int k = 0; k < langs.Count; k++) langIndex[((OMap)langs[k]).S("Name")] = k;
                    int changed = 0;
                    foreach (string[] r in p.Rows("langnames.tsv"))
                    {
                        int idx;
                        if (!langIndex.TryGetValue(r[1], out idx)) continue;
                        foreach (object to in src.L("mTerms"))
                        {
                            OMap t = (OMap)to;
                            if (t.S("Term") != r[0]) continue;
                            List<object> ls = t.L("Languages");
                            if (idx < ls.Count) { ls[idx] = r[2]; changed++; }
                        }
                    }
                    if (changed > 0)
                    {
                        BinWriter bw = new BinWriter(big); int j = 0;
                        TypeTreeIO.Write(lsNodes, ref j, bw, m);
                        names.NewData = bw.ToArray();
                        Log("  Menu: la lingua ora compare come \"Italiano\"");
                    }
                }
                catch (Exception e) { Log("  Nota: nome della lingua nel menu non modificato (" + e.Message + "). Resterà \"Español\"."); }
            }
            byte[] result = SerializedFileExt.SaveAppend(sf);
            // verifica: rilettura delle etichette dal file prodotto
            SerializedFile chk = SerializedFile.Load(result);
            SkillLabels.Parse(chk.GetData(chk.Find(skills.PathID)));
            return result;
        }

        // Nome di un MonoBehaviour senza TypeTree: m_GameObject(12) m_Enabled(4) m_Script(12) m_Name(string)
        public static string MonoName(SerializedFile sf, SObject o)
        {
            if (o.ByteSize < 32) return null;
            BinReader r = new BinReader(sf.Raw, sf.Endian != 0); r.P = (int)o.ByteStart + 28;
            int len = r.I32();
            if (len < 0 || len > 200 || len > o.ByteSize - 32) return null;
            return Encoding.UTF8.GetString(sf.Raw, r.P, len);
        }

        // ---- installazione ----

        public void Install(string gameDir, Payload p)
        {
            string dataDir = DataDirName(gameDir);
            if (dataDir == null) throw new PatchException("La cartella indicata non contiene Disco Elysium - The Final Cut.");
            if (dataDir != DataDirDefault) Log("Cartella dei dati del gioco: " + dataDir);
            string backupDir = Path.Combine(gameDir, BackupDirName);
            Dictionary<string, string> state = ReadState(gameDir);
            List<string> targets = Targets(gameDir, dataDir, p);

            // Per ogni file: se quello attuale è stato scritto da noi si riparte dall'originale salvato,
            // altrimenti quello attuale È l'originale (prima installazione o gioco aggiornato).
            // Dati e risultati sono indicizzati con il percorso interno; su disco e nello stato si usa quello reale.
            Dictionary<string, byte[]> originals = new Dictionary<string, byte[]>();
            Dictionary<string, bool> needBackup = new Dictionary<string, bool>();
            foreach (string rel in targets)
            {
                string real = RealRel(rel, dataDir);
                string cur = Path.Combine(gameDir, real), bak = Path.Combine(backupDir, real);
                if (!File.Exists(cur)) { needBackup[rel] = false; continue; }
                byte[] data = File.ReadAllBytes(cur);
                string old;
                bool ours = state.TryGetValue(real, out old) && old == Sha(data);
                if (ours && File.Exists(bak)) { originals[rel] = File.ReadAllBytes(bak); needBackup[rel] = false; }
                else { originals[rel] = data; needBackup[rel] = !ours; }
            }

            Log("Preparazione dei file...");
            Dictionary<string, byte[]> output = new Dictionary<string, byte[]>();
            List<TypeNode> lsNodes;
            output[LockitRel] = PatchLockit(originals[LockitRel], p, out lsNodes);
            output[ResourcesRel] = PatchResources(originals[ResourcesRel], p, lsNodes);
            foreach (string rel in targets)
                if (!output.ContainsKey(rel)) output[rel] = p.Entries[FilePrefix + rel];
            Log("  Immagini: " + (output.Count - 2) + " file");

            Log("Copia di sicurezza dei file originali in \"" + BackupDirName + "\"...");
            Directory.CreateDirectory(backupDir);
            foreach (string rel in targets)
            {
                bool nb;
                if (!needBackup.TryGetValue(rel, out nb) || !nb) continue;
                string bak = Path.Combine(backupDir, RealRel(rel, dataDir));
                Directory.CreateDirectory(Path.GetDirectoryName(bak));
                File.WriteAllBytes(bak, originals[rel]);
            }

            Log("Scrittura dei file tradotti...");
            Dictionary<string, string> newState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            newState["@versione"] = p.Has("version") ? p.Text("version").Trim() : "?";
            newState["@data"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            foreach (string rel in targets)
            {
                string real = RealRel(rel, dataDir);
                string dst = Path.Combine(gameDir, real), tmp = dst + ".ita_tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.WriteAllBytes(tmp, output[rel]);
                if (File.Exists(dst)) { File.SetAttributes(dst, FileAttributes.Normal); File.Delete(dst); }
                File.Move(tmp, dst);
                newState[real] = Sha(output[rel]);
                // lo stato viene salvato a ogni file: se qualcosa si interrompe, ciò che è già scritto resta ripristinabile
                WriteState(gameDir, newState);
            }
            Log("Fatto.");
        }

        public void Uninstall(string gameDir)
        {
            string backupDir = Path.Combine(gameDir, BackupDirName);
            Dictionary<string, string> state = ReadState(gameDir);
            if (state.Count == 0) throw new PatchException("La traduzione non risulta installata in questa cartella.");
            int restored = 0, skipped = 0;
            foreach (KeyValuePair<string, string> kv in state)
            {
                if (kv.Key.StartsWith("@", StringComparison.Ordinal)) continue;
                string cur = Path.Combine(gameDir, kv.Key), bak = Path.Combine(backupDir, kv.Key);
                bool ours = File.Exists(cur) && ShaFile(cur) == kv.Value;
                if (!ours) { Log("  " + kv.Key + ": modificato dopo l'installazione (aggiornamento del gioco?), lasciato com'è"); skipped++; continue; }
                if (!File.Exists(bak))
                {
                    // file che prima della patch non esisteva: lo abbiamo creato noi, quindi si elimina
                    File.SetAttributes(cur, FileAttributes.Normal); File.Delete(cur); restored++; continue;
                }
                File.SetAttributes(cur, FileAttributes.Normal);
                File.Copy(bak, cur, true);
                restored++;
            }
            Directory.Delete(backupDir, true);
            Log("File originali ripristinati: " + restored + (skipped > 0 ? " (non toccati: " + skipped + ")" : ""));
        }
    }
}
