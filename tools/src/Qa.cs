// Controlli automatici di qualità sulla traduzione italiana.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DE
{
    public static class Qa
    {
        static readonly Regex Placeholder = new Regex(@"\{[0-9A-Za-z_]+\}");
        static readonly Regex Tag = new Regex(@"</?[a-zA-Z][^>]*>");
        static readonly Regex Word = new Regex(@"[A-Za-zÀ-ÿ']+");
        static readonly Regex Number = new Regex(@"\d+");
        static readonly HashSet<string> EsWords = new HashSet<string>(new string[] {
            "el","los","las","una","unos","unas","que","qué","y","pero","porque","cuando","donde","dónde","muy","más","también",
            "está","están","estoy","eres","soy","tiene","tienes","tengo","hay","para","con","sin","sobre","hasta","desde","ahora",
            "aquí","allí","eso","esto","este","esta","ese","esa","nada","algo","todo","todos","usted","ustedes","él","ella","ellos",
            "nosotros","mí","ti","sí","es","del","al","lo","le","les","su","sus","mi","mis","tu","tus","fue","ser","hacer","puede",
            "puedes","quiero","quieres","bien","solo","sólo","entonces","pues","así","cómo","quién","cuál","hombre","mujer","años" });
        static readonly HashSet<string> EnWords = new HashSet<string>(new string[] {
            "the","and","you","your","is","are","was","were","this","that","it","of","to","in","with","for","on","not","have","has",
            "what","he","she","they","we","his","her","there","be","from","at","but","if","or","an","as","by","do","don't","it's",
            "i'm","like","just","about","would","could","should","will","can","no","yes","me","my","him","them","who","how","why",
            "when","where","then","than","so","up","out","one","all","been","had","did","into","some","any","get","got","know" });
        static readonly HashSet<string> ItWords = new HashSet<string>(new string[] {
            "il","lo","la","i","gli","le","un","uno","una","che","e","ma","perché","quando","dove","molto","più","anche","è","sono",
            "sei","ha","hai","ho","c'è","per","con","senza","su","fino","da","adesso","ora","qui","lì","questo","questa","quello",
            "quella","niente","qualcosa","tutto","tutti","lui","lei","loro","noi","me","te","sì","del","della","dei","delle","al",
            "alla","nel","nella","suo","sua","mio","mia","tuo","tua","non","si","di","a","in","come","chi","cosa","se","o","ci",
            "ne","mi","ti","vi","era","essere","fare","può","puoi","voglio","vuoi","bene","solo","allora","così","uomo","donna","anni" });

        static void Count(string text, out int es, out int en, out int it, out int total)
        {
            es = en = it = total = 0;
            foreach (Match m in Word.Matches(text))
            {
                string w = m.Value.ToLowerInvariant(); total++;
                if (EsWords.Contains(w)) es++; if (EnWords.Contains(w)) en++; if (ItWords.Contains(w)) it++;
            }
        }

        static string Multiset(MatchCollection mc)
        {
            List<string> l = new List<string>(); foreach (Match m in mc) l.Add(m.Value.ToLowerInvariant());
            l.Sort(StringComparer.Ordinal); return string.Join(" ", l.ToArray());
        }

        static int CountChar(string s, char c) { int n = 0; foreach (char x in s) if (x == c) n++; return n; }

        static bool HasLetters(string s, int min)
        {
            int n = 0; foreach (char c in s) if (char.IsLetter(c) && ++n >= min) return true; return false;
        }

        // Mostra come un termine inglese è stato reso in italiano: esempi + conteggio di candidati italiani
        public static int Gloss(string workDir, string enPattern, string[] itCandidates, int samples)
        {
            Regex re = new Regex(enPattern, RegexOptions.IgnoreCase);
            int hits = 0; int[] cnt = new int[itCandidates.Length]; List<string[]> ex = new List<string[]>();
            foreach (string kind in new string[] { "General", "Dialogues" })
                foreach (string[] r in Loc.ReadTsv(Path.Combine(workDir, "master_" + kind + ".tsv")))
                {
                    if (!re.IsMatch(r[2])) continue;
                    hits++;
                    for (int i = 0; i < itCandidates.Length; i++)
                        if (r[4].IndexOf(itCandidates[i], StringComparison.OrdinalIgnoreCase) >= 0) cnt[i]++;
                    if (r[2].Length < 160) ex.Add(r);
                }
            Console.WriteLine("/{0}/ : {1} righe", enPattern, hits);
            for (int i = 0; i < itCandidates.Length; i++) Console.WriteLine("   '{0}': {1}", itCandidates[i], cnt[i]);
            Random rnd = new Random(11);
            for (int i = 0; i < samples && ex.Count > 0; i++)
            {
                int k = rnd.Next(ex.Count); string[] r = ex[k]; ex.RemoveAt(k);
                Console.WriteLine("   EN: {0}\n   IT: {1}", r[2].Replace("\n", "\\n"), r[4].Replace("\n", "\\n"));
            }
            return 0;
        }

        // Elenco unificato delle stringhe da sistemare a mano: n, categorie, chiave, attore, conversazione, EN, ES, IT attuale
        public static int Todo(string workDir, string outFile)
        {
            string[] use = new string[] { "MANCANTE", "VUOTA", "SPAGNOLO_IDENTICO", "INGLESE_IDENTICO", "SEMBRA_SPAGNOLO", "SEMBRA_INGLESE",
                "SEGNAPOSTO", "ASTERISCHI", "DOPPI_SPAZI", "PUNTEGGIATURA_SPAGNOLA", "SPAZI_ESTREMI", "ESCAPE_LETTERALI", "VIRGOLETTE",
                "PARENTESI_QUADRE", "NUMERI" };
            Dictionary<string, List<string>> catsByKey = new Dictionary<string, List<string>>();
            foreach (string c in use)
                foreach (string[] r in Loc.ReadTsv(Path.Combine(workDir, "qa", c + ".tsv")))
                {
                    string id = r[0] + "\t" + r[1]; List<string> l;
                    if (!catsByKey.TryGetValue(id, out l)) catsByKey[id] = l = new List<string>();
                    l.Add(c);
                }
            List<string[]> rows = new List<string[]>(); int n = 0;
            foreach (string kind in new string[] { "General", "Dialogues" })
                foreach (string[] r in Loc.ReadTsv(Path.Combine(workDir, "master_" + kind + ".tsv")))
                {
                    List<string> l;
                    if (!catsByKey.TryGetValue(kind + "\t" + r[0], out l)) continue;
                    string en = r[2], es = r[3], it = r[4];
                    string itShown = it == es ? "=ES" : it == en ? "=EN" : it;
                    bool needEs = en == "<<NO_EN>>" || l.Contains("MANCANTE");
                    rows.Add(new string[] { (++n).ToString(), string.Join(",", l.ToArray()), kind, r[0], r.Length > 5 ? r[5] : "", r.Length > 6 ? r[6] : "",
                        en, needEs ? es : "", itShown });
                }
            Loc.WriteTsv(outFile, rows);
            Console.WriteLine("{0} stringhe da rivedere -> {1}", rows.Count, outFile);
            return 0;
        }

        public static int Run(string workDir, string itColumnFile)
        {
            Dictionary<string, List<string[]>> cats = new Dictionary<string, List<string[]>>();
            List<string> order = new List<string>(new string[] {
                "MANCANTE", "VUOTA", "SPAGNOLO_IDENTICO", "INGLESE_IDENTICO", "SEMBRA_SPAGNOLO", "SEMBRA_INGLESE", "SEGNAPOSTO", "TAG",
                "ASTERISCHI", "A_CAPO", "MOJIBAKE", "DOPPI_SPAZI", "SPAZIO_PUNTEGGIATURA", "LUNGHEZZA", "PUNTEGGIATURA_SPAGNOLA", "SPAZI_ESTREMI" });
            foreach (string c in order) cats[c] = new List<string[]>();
            foreach (string c in new string[] { "ESCAPE_LETTERALI", "VIRGOLETTE", "PARENTESI_QUADRE", "NUMERI" }) cats[c] = new List<string[]>();
            int total = 0;
            foreach (string kind in new string[] { "General", "Dialogues" })
            {
                // se indicata una cartella di traduzione, l'italiano viene preso da italiano_<Tipo>.tsv
                Dictionary<string, string> final = null;
                if (itColumnFile != null)
                {
                    final = new Dictionary<string, string>();
                    foreach (string[] fr in Loc.ReadTsv(Path.Combine(itColumnFile, "italiano_" + kind + ".tsv"))) final[fr[0]] = fr.Length > 1 ? fr[1] : "";
                }
                foreach (string[] r in Loc.ReadTsv(Path.Combine(workDir, "master_" + kind + ".tsv")))
                {
                    string key = r[0], en = r[2], es = r[3], it = r[4];
                    if (final != null) { string fv; it = final.TryGetValue(key, out fv) ? fv : "<<NO_IT>>"; }
                    total++;
                    string[] row = new string[] { kind, key, en, es, it };
                    bool noEn = en == "<<NO_EN>>";
                    if (it == "<<NO_IT>>") { cats["MANCANTE"].Add(row); continue; }
                    string src = noEn ? es : en;
                    if (src.Trim().Length == 0 && es.Trim().Length == 0) continue;
                    if (it.Trim().Length == 0) { if (es.Trim().Length > 0) cats["VUOTA"].Add(row); continue; }
                    bool isMeta = key.StartsWith("Conversation/", StringComparison.Ordinal) && (key.EndsWith("/Title", StringComparison.Ordinal));
                    if (it == es && es != en && HasLetters(it, 3)) { cats["SPAGNOLO_IDENTICO"].Add(row); continue; }
                    if (!noEn && it == en && es != en && HasLetters(it, 3) && !isMeta) { cats["INGLESE_IDENTICO"].Add(row); continue; }
                    int ces, cen, cit, ct; Count(it, out ces, out cen, out cit, out ct);
                    if (ct >= 4 && ces > cit && ces * 4 >= ct && ces > cen) cats["SEMBRA_SPAGNOLO"].Add(row);
                    else if (ct >= 4 && cen > cit && cen * 4 >= ct && !isMeta) cats["SEMBRA_INGLESE"].Add(row);
                    if (!noEn)
                    {
                        if (Multiset(Placeholder.Matches(en)) != Multiset(Placeholder.Matches(it))) cats["SEGNAPOSTO"].Add(row);
                        if (Multiset(Tag.Matches(en)) != Multiset(Tag.Matches(it))) cats["TAG"].Add(row);
                        if (CountChar(en, '*') % 2 == 0 && CountChar(it, '*') % 2 != 0) cats["ASTERISCHI"].Add(row);
                        if (CountChar(en, '\n') != CountChar(it, '\n') && kind == "General") cats["A_CAPO"].Add(row);
                        if (en.Length >= 30 && (it.Length < en.Length * 0.4 || it.Length > en.Length * 2.6)) cats["LUNGHEZZA"].Add(row);
                    }
                    if (it.IndexOf('�') >= 0 || it.Contains("Ã") && (it.Contains("Ã¨") || it.Contains("Ã ") || it.Contains("Ã©") || it.Contains("Ã²") || it.Contains("Ã¹")) || it.Contains("â€"))
                        cats["MOJIBAKE"].Add(row);
                    if (it.Contains("  ") && !src.Contains("  ")) cats["DOPPI_SPAZI"].Add(row);
                    if (Regex.IsMatch(it, @"[a-zà-ù] [,;:!?](\s|$)") || Regex.IsMatch(it, @"[a-zà-ù] \.(\s|$)")) cats["SPAZIO_PUNTEGGIATURA"].Add(row);
                    if (it.IndexOf('¿') >= 0 || it.IndexOf('¡') >= 0) cats["PUNTEGGIATURA_SPAGNOLA"].Add(row);
                    if ((it != it.Trim()) != (src != src.Trim())) cats["SPAZI_ESTREMI"].Add(row);
                    if (Regex.IsMatch(it, @"\\u[0-9a-fA-F]{4}|\\[nrt]|&[a-z]+;|&#\d+;") && !Regex.IsMatch(src, @"\\u[0-9a-fA-F]{4}|\\[nrt]|&[a-z]+;|&#\d+;"))
                        cats["ESCAPE_LETTERALI"].Add(row);
                    if (!noEn)
                    {
                        if (CountChar(en, '"') % 2 == 0 && CountChar(it, '"') % 2 != 0) cats["VIRGOLETTE"].Add(row);
                        if (CountChar(en, '[') != CountChar(it, '[') || CountChar(en, ']') != CountChar(it, ']')) cats["PARENTESI_QUADRE"].Add(row);
                        if (kind == "General" && !key.StartsWith("Conversation/", StringComparison.Ordinal)
                            && Multiset(Number.Matches(en)) != Multiset(Number.Matches(it))) cats["NUMERI"].Add(row);
                    }
                }
            }
            order.AddRange(new string[] { "ESCAPE_LETTERALI", "VIRGOLETTE", "PARENTESI_QUADRE", "NUMERI" });
            string outDir = Path.Combine(workDir, itColumnFile != null ? "qa_finale" : "qa");
            Directory.CreateDirectory(outDir);
            Console.WriteLine("stringhe esaminate: {0}", total);
            foreach (string c in order)
            {
                Loc.WriteTsv(Path.Combine(outDir, c + ".tsv"), cats[c]);
                int g = 0; foreach (string[] r in cats[c]) if (r[0] == "General") g++;
                Console.WriteLine("  {0,-24} {1,6}   (General {2}, Dialogues {3})", c, cats[c].Count, g, cats[c].Count - g);
            }
            return 0;
        }
    }
}
