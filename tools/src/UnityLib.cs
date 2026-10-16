// Lettore/scrittore minimale di AssetBundle Unity (UnityFS) e SerializedFile con TypeTree.
// Scritto per C# 5 (csc.exe del .NET Framework 4.x, presente in ogni Windows 10/11).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DE
{
    public class BinReader
    {
        public byte[] B; public int P; public bool Big;
        public BinReader(byte[] b, bool big) { B = b; P = 0; Big = big; }
        public byte U8() { return B[P++]; }
        public sbyte I8() { return (sbyte)B[P++]; }
        public ushort U16()
        {
            ushort v = Big ? (ushort)((B[P] << 8) | B[P + 1]) : (ushort)(B[P] | (B[P + 1] << 8));
            P += 2; return v;
        }
        public short I16() { return (short)U16(); }
        public uint U32()
        {
            uint v = Big
                ? ((uint)B[P] << 24) | ((uint)B[P + 1] << 16) | ((uint)B[P + 2] << 8) | B[P + 3]
                : ((uint)B[P + 3] << 24) | ((uint)B[P + 2] << 16) | ((uint)B[P + 1] << 8) | B[P];
            P += 4; return v;
        }
        public int I32() { return (int)U32(); }
        public ulong U64() { ulong a = U32(), b = U32(); return Big ? (a << 32) | b : (b << 32) | a; }
        public long I64() { return (long)U64(); }
        public float F32() { return BitConverter.ToSingle(BitConverter.GetBytes(U32()), 0); }
        public double F64() { return BitConverter.Int64BitsToDouble((long)U64()); }
        public string CStr()
        {
            int s = P; while (B[P] != 0) P++;
            string r = Encoding.UTF8.GetString(B, s, P - s); P++; return r;
        }
        public byte[] Bytes(int n) { byte[] r = new byte[n]; Buffer.BlockCopy(B, P, r, 0, n); P += n; return r; }
        public void Align(int n) { int m = P % n; if (m != 0) P += n - m; }
    }

    public class BinWriter
    {
        public MemoryStream S = new MemoryStream(); public bool Big;
        public BinWriter(bool big) { Big = big; }
        public long Pos { get { return S.Position; } }
        public void U8(byte v) { S.WriteByte(v); }
        public void U16(ushort v)
        {
            if (Big) { S.WriteByte((byte)(v >> 8)); S.WriteByte((byte)v); }
            else { S.WriteByte((byte)v); S.WriteByte((byte)(v >> 8)); }
        }
        public void U32(uint v)
        {
            if (Big) { S.WriteByte((byte)(v >> 24)); S.WriteByte((byte)(v >> 16)); S.WriteByte((byte)(v >> 8)); S.WriteByte((byte)v); }
            else { S.WriteByte((byte)v); S.WriteByte((byte)(v >> 8)); S.WriteByte((byte)(v >> 16)); S.WriteByte((byte)(v >> 24)); }
        }
        public void I32(int v) { U32((uint)v); }
        public void U64(ulong v)
        {
            if (Big) { U32((uint)(v >> 32)); U32((uint)v); } else { U32((uint)v); U32((uint)(v >> 32)); }
        }
        public void I64(long v) { U64((ulong)v); }
        public void F32(float v) { U32(BitConverter.ToUInt32(BitConverter.GetBytes(v), 0)); }
        public void F64(double v) { U64((ulong)BitConverter.DoubleToInt64Bits(v)); }
        public void CStr(string s) { byte[] b = Encoding.UTF8.GetBytes(s); S.Write(b, 0, b.Length); S.WriteByte(0); }
        public void Bytes(byte[] b) { S.Write(b, 0, b.Length); }
        public void Align(int n) { while (S.Position % n != 0) S.WriteByte(0); }
        public byte[] ToArray() { return S.ToArray(); }
    }

    public static class Lz4
    {
        public static byte[] Decode(byte[] src, int srcOff, int srcLen, int dstLen)
        {
            byte[] dst = new byte[dstLen];
            int s = srcOff, end = srcOff + srcLen, d = 0;
            while (s < end)
            {
                int token = src[s++];
                int lit = token >> 4;
                if (lit == 15) { int b; do { b = src[s++]; lit += b; } while (b == 255); }
                Buffer.BlockCopy(src, s, dst, d, lit); s += lit; d += lit;
                if (s >= end) break;
                int off = src[s] | (src[s + 1] << 8); s += 2;
                int ml = token & 15;
                if (ml == 15) { int b; do { b = src[s++]; ml += b; } while (b == 255); }
                ml += 4;
                int m = d - off;
                if (m < 0) throw new InvalidDataException("LZ4: offset non valido");
                for (int k = 0; k < ml; k++) dst[d++] = dst[m++];
            }
            if (d != dstLen) throw new InvalidDataException("LZ4: dimensione decompressa inattesa (" + d + " != " + dstLen + ")");
            return dst;
        }

        // Compressore LZ4 greedy con hash table: output valido per qualunque decoder LZ4 (formato block).
        public static byte[] Encode(byte[] src, int srcOff, int srcLen)
        {
            MemoryStream o = new MemoryStream(srcLen / 2 + 64);
            int end = srcOff + srcLen;
            int mfLimit = end - 12;      // nessun match può iniziare negli ultimi 12 byte
            int matchEndLimit = end - 5; // gli ultimi 5 byte sono sempre letterali
            int[] table = new int[1 << 16];
            for (int i = 0; i < table.Length; i++) table[i] = -1;
            int anchor = srcOff, p = srcOff;
            while (p < mfLimit)
            {
                uint seq = (uint)(src[p] | (src[p + 1] << 8) | (src[p + 2] << 16) | (src[p + 3] << 24));
                int h = (int)((seq * 2654435761u) >> 16);
                int cand = table[h];
                table[h] = p;
                if (cand >= 0 && p - cand <= 65535 &&
                    src[cand] == src[p] && src[cand + 1] == src[p + 1] && src[cand + 2] == src[p + 2] && src[cand + 3] == src[p + 3])
                {
                    int ml = 4;
                    while (p + ml < matchEndLimit && src[cand + ml] == src[p + ml]) ml++;
                    EmitSeq(o, src, anchor, p - anchor, p - cand, ml);
                    p += ml; anchor = p;
                }
                else p++;
            }
            EmitSeq(o, src, anchor, end - anchor, 0, 0);
            return o.ToArray();
        }

        static void EmitSeq(MemoryStream o, byte[] src, int litStart, int litLen, int offset, int matchLen)
        {
            int ml = matchLen == 0 ? 0 : matchLen - 4;
            int token = (Math.Min(litLen, 15) << 4) | Math.Min(ml, 15);
            o.WriteByte((byte)token);
            if (litLen >= 15) { int r = litLen - 15; while (r >= 255) { o.WriteByte(255); r -= 255; } o.WriteByte((byte)r); }
            o.Write(src, litStart, litLen);
            if (matchLen == 0) return;
            o.WriteByte((byte)offset); o.WriteByte((byte)(offset >> 8));
            if (ml >= 15) { int r = ml - 15; while (r >= 255) { o.WriteByte(255); r -= 255; } o.WriteByte((byte)r); }
        }
    }

    // Decoder LZMA (stream "raw" con 5 byte di proprietà in testa, come nei blocchi UnityFS).
    public class Lzma
    {
        const int NumStates = 12, PosBitsMax = 4, EndPosModelIndex = 14, NumFullDistances = 1 << (EndPosModelIndex >> 1);
        byte[] src; int sp, sEnd; uint range, code;

        void Normalize() { if (range < (1u << 24)) { range <<= 8; code = (code << 8) | (sp < sEnd ? src[sp] : (byte)0); sp++; } }

        int Bit(ushort[] p, int i)
        {
            uint v = p[i]; uint bound = (range >> 11) * v; int sym;
            if (code < bound) { v += ((1u << 11) - v) >> 5; range = bound; sym = 0; }
            else { v -= v >> 5; code -= bound; range -= bound; sym = 1; }
            p[i] = (ushort)v; Normalize(); return sym;
        }

        uint DirectBits(int n)
        {
            uint res = 0;
            do
            {
                range >>= 1; code -= range; uint t = 0 - (code >> 31); code += range & t;
                Normalize(); res = (res << 1) + t + 1;
            } while (--n > 0);
            return res;
        }

        int Tree(ushort[] p, int off, int bits)
        {
            int m = 1; for (int i = 0; i < bits; i++) m = (m << 1) + Bit(p, off + m);
            return m - (1 << bits);
        }

        int TreeRev(ushort[] p, int off, int bits)
        {
            int m = 1, sym = 0;
            for (int i = 0; i < bits; i++) { int b = Bit(p, off + m); m = (m << 1) + b; sym |= b << i; }
            return sym;
        }

        static ushort[] Probs(int n) { ushort[] p = new ushort[n]; for (int i = 0; i < n; i++) p[i] = 1024; return p; }

        // layout: [0]=choice [1]=choice2 [2..)=low (16*8) poi mid (16*8) poi high (256)
        int Len(ushort[] p, int posState)
        {
            if (Bit(p, 0) == 0) return Tree(p, 2 + posState * 8, 3);
            if (Bit(p, 1) == 0) return 8 + Tree(p, 2 + 128 + posState * 8, 3);
            return 16 + Tree(p, 2 + 256, 8);
        }

        public static byte[] Decode(byte[] src, int off, int len, int outSize)
        {
            return new Lzma().Run(src, off, len, outSize);
        }

        byte[] Run(byte[] s, int off, int len, int outSize)
        {
            src = s; sp = off; sEnd = off + len;
            int d = src[sp++];
            if (d >= 9 * 5 * 5) throw new InvalidDataException("LZMA: proprietà non valide");
            int lc = d % 9; d /= 9; int pb = d / 5; int lp = d % 5;
            sp += 4; // dimensione dizionario: l'intero output fa da dizionario
            byte[] o = new byte[outSize]; int pos = 0;
            ushort[] lit = Probs(0x300 << (lc + lp));
            ushort[] isMatch = Probs(NumStates << PosBitsMax), isRep = Probs(NumStates), isRepG0 = Probs(NumStates),
                isRepG1 = Probs(NumStates), isRepG2 = Probs(NumStates), isRep0Long = Probs(NumStates << PosBitsMax);
            ushort[] posSlot = Probs(4 * 64), posDec = Probs(1 + NumFullDistances - EndPosModelIndex), align = Probs(16);
            ushort[] lenDec = Probs(2 + 256 + 256), repLenDec = Probs(2 + 256 + 256);
            range = 0xFFFFFFFF; code = 0;
            sp++; for (int i = 0; i < 4; i++) code = (code << 8) | src[sp++];
            uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0; int state = 0;
            int pbMask = (1 << pb) - 1, lpMask = (1 << lp) - 1;
            while (pos < outSize)
            {
                int posState = pos & pbMask;
                if (Bit(isMatch, (state << PosBitsMax) + posState) == 0)
                {
                    int prev = pos > 0 ? o[pos - 1] : 0;
                    int lo = 0x300 * (((pos & lpMask) << lc) + (prev >> (8 - lc)));
                    int sym = 1;
                    if (state >= 7)
                    {
                        int mb = o[pos - rep0 - 1];
                        do
                        {
                            int mbit = (mb >> 7) & 1; mb <<= 1;
                            int b = Bit(lit, lo + ((1 + mbit) << 8) + sym);
                            sym = (sym << 1) | b;
                            if (mbit != b) break;
                        } while (sym < 0x100);
                    }
                    while (sym < 0x100) sym = (sym << 1) | Bit(lit, lo + sym);
                    o[pos++] = (byte)sym;
                    state = state < 4 ? 0 : (state < 10 ? state - 3 : state - 6);
                    continue;
                }
                int ln;
                if (Bit(isRep, state) != 0)
                {
                    if (pos == 0) throw new InvalidDataException("LZMA: dati corrotti");
                    if (Bit(isRepG0, state) == 0)
                    {
                        if (Bit(isRep0Long, (state << PosBitsMax) + posState) == 0)
                        {
                            state = state < 7 ? 9 : 11;
                            o[pos] = o[pos - rep0 - 1]; pos++;
                            continue;
                        }
                    }
                    else
                    {
                        uint dist;
                        if (Bit(isRepG1, state) == 0) dist = rep1;
                        else
                        {
                            if (Bit(isRepG2, state) == 0) dist = rep2; else { dist = rep3; rep3 = rep2; }
                            rep2 = rep1;
                        }
                        rep1 = rep0; rep0 = dist;
                    }
                    ln = Len(repLenDec, posState);
                    state = state < 7 ? 8 : 11;
                }
                else
                {
                    rep3 = rep2; rep2 = rep1; rep1 = rep0;
                    ln = Len(lenDec, posState);
                    state = state < 7 ? 7 : 10;
                    int lenState = ln > 3 ? 3 : ln;
                    int slot = Tree(posSlot, lenState * 64, 6);
                    if (slot < 4) rep0 = (uint)slot;
                    else
                    {
                        int nd = (slot >> 1) - 1;
                        uint dist = (uint)(2 | (slot & 1)) << nd;
                        if (slot < EndPosModelIndex) dist += (uint)TreeRev(posDec, (int)dist - slot, nd);
                        else { dist += DirectBits(nd - 4) << 4; dist += (uint)TreeRev(align, 0, 4); }
                        rep0 = dist;
                    }
                    if (rep0 == 0xFFFFFFFF) break; // marcatore di fine
                    if (rep0 >= (uint)pos) throw new InvalidDataException("LZMA: distanza non valida");
                }
                ln += 2;
                if (pos + ln > outSize) throw new InvalidDataException("LZMA: output oltre la dimensione attesa");
                int from = pos - (int)rep0 - 1;
                for (int k = 0; k < ln; k++) o[pos++] = o[from++];
            }
            if (pos != outSize) throw new InvalidDataException("LZMA: dimensione decompressa inattesa");
            return o;
        }
    }

    public class BundleNode { public long Offset; public long Size; public uint Flags; public string Path; public byte[] Data; }
    public class BundleBlock { public uint USize, CSize; public ushort Flags; }

    public class Bundle
    {
        public string Signature; public uint Version; public string UnityVersion, UnityRevision; public uint Flags;
        public List<BundleNode> Nodes = new List<BundleNode>();
        public List<BundleBlock> Blocks = new List<BundleBlock>();

        static byte[] Decompress(byte[] src, int off, int len, int uSize, uint comp)
        {
            switch (comp)
            {
                case 0: { byte[] r = new byte[len]; Buffer.BlockCopy(src, off, r, 0, len); return r; }
                case 1: return Lzma.Decode(src, off, len, uSize);
                case 2: case 3: return Lz4.Decode(src, off, len, uSize);
                default: throw new NotSupportedException("Compressione bundle non supportata: " + comp);
            }
        }

        public static Bundle Load(string path) { return Load(File.ReadAllBytes(path)); }

        public static Bundle Load(byte[] f)
        {
            BinReader r = new BinReader(f, true);
            Bundle b = new Bundle();
            b.Signature = r.CStr();
            if (b.Signature != "UnityFS") throw new InvalidDataException("Non è un bundle UnityFS");
            b.Version = r.U32(); b.UnityVersion = r.CStr(); b.UnityRevision = r.CStr();
            long size = r.I64(); uint cSize = r.U32(); uint uSize = r.U32(); b.Flags = r.U32();
            if (b.Version >= 7) r.Align(16);
            byte[] bi;
            if ((b.Flags & 0x80) != 0) bi = Decompress(f, f.Length - (int)cSize, (int)cSize, (int)uSize, b.Flags & 0x3F);
            else { bi = Decompress(f, r.P, (int)cSize, (int)uSize, b.Flags & 0x3F); r.P += (int)cSize; }
            BinReader br = new BinReader(bi, true);
            br.Bytes(16);
            int nb = br.I32();
            for (int i = 0; i < nb; i++)
            {
                BundleBlock k = new BundleBlock(); k.USize = br.U32(); k.CSize = br.U32(); k.Flags = br.U16(); b.Blocks.Add(k);
            }
            int nn = br.I32();
            for (int i = 0; i < nn; i++)
            {
                BundleNode n = new BundleNode(); n.Offset = br.I64(); n.Size = br.I64(); n.Flags = br.U32(); n.Path = br.CStr(); b.Nodes.Add(n);
            }
            if ((b.Flags & 0x200) != 0) r.Align(16);
            long total = 0; foreach (BundleBlock k in b.Blocks) total += k.USize;
            byte[] all = new byte[total]; int w = 0;
            foreach (BundleBlock k in b.Blocks)
            {
                uint comp = (uint)(k.Flags & 0x3F);
                if (comp == 0) Buffer.BlockCopy(f, r.P, all, w, (int)k.CSize);
                else { byte[] u = Decompress(f, r.P, (int)k.CSize, (int)k.USize, comp); Buffer.BlockCopy(u, 0, all, w, u.Length); }
                r.P += (int)k.CSize; w += (int)k.USize;
            }
            foreach (BundleNode n in b.Nodes)
            {
                n.Data = new byte[n.Size]; Buffer.BlockCopy(all, (int)n.Offset, n.Data, 0, (int)n.Size);
            }
            return b;
        }

        // Scrive il bundle. compress=true: blocchi LZ4 da 128 KiB come fa Unity; false: non compresso.
        public byte[] Save(bool compress)
        {
            const int Chunk = 0x20000;
            long total = 0; foreach (BundleNode n in Nodes) total += n.Data.Length;
            byte[] all = new byte[total]; long off = 0;
            foreach (BundleNode n in Nodes)
            {
                n.Offset = off; n.Size = n.Data.Length;
                Buffer.BlockCopy(n.Data, 0, all, (int)off, n.Data.Length); off += n.Data.Length;
            }
            MemoryStream blockData = new MemoryStream();
            List<BundleBlock> blocks = new List<BundleBlock>();
            for (int p = 0; p < all.Length; p += Chunk)
            {
                int len = Math.Min(Chunk, all.Length - p);
                BundleBlock k = new BundleBlock(); k.USize = (uint)len;
                byte[] c = compress ? Lz4.Encode(all, p, len) : null;
                if (c != null && c.Length < len) { k.CSize = (uint)c.Length; k.Flags = 2; blockData.Write(c, 0, c.Length); }
                else { k.CSize = (uint)len; k.Flags = 0; blockData.Write(all, p, len); }
                blocks.Add(k);
            }
            BinWriter bi = new BinWriter(true);
            bi.Bytes(new byte[16]);
            bi.I32(blocks.Count);
            foreach (BundleBlock k in blocks) { bi.U32(k.USize); bi.U32(k.CSize); bi.U16(k.Flags); }
            bi.I32(Nodes.Count);
            foreach (BundleNode n in Nodes) { bi.I64(n.Offset); bi.I64(n.Size); bi.U32(n.Flags); bi.CStr(n.Path); }
            byte[] biRaw = bi.ToArray();
            byte[] biOut = biRaw; uint biComp = 0;
            if (compress) { byte[] c = Lz4.Encode(biRaw, 0, biRaw.Length); if (c.Length < biRaw.Length) { biOut = c; biComp = 2; } }

            uint flags = 0x40 | biComp; // info blocchi+directory combinati, subito dopo l'header
            BinWriter w = new BinWriter(true);
            w.CStr(Signature); w.U32(Version); w.CStr(UnityVersion); w.CStr(UnityRevision);
            long sizePos = w.Pos;
            w.I64(0); w.U32((uint)biOut.Length); w.U32((uint)biRaw.Length); w.U32(flags);
            if (Version >= 7) w.Align(16);
            w.Bytes(biOut);
            byte[] bd = blockData.ToArray();
            w.Bytes(bd);
            byte[] res = w.ToArray();
            long sz = res.Length;
            for (int i = 0; i < 8; i++) res[sizePos + i] = (byte)(sz >> (8 * (7 - i)));
            Blocks = blocks; Flags = flags;
            return res;
        }

        // Scrive il bundle non compresso con un blocco per nodo (flag 0x40), lo stesso schema usato da UABE:
        // è il formato dei file della patch originale, che il gioco carica senza problemi da anni.
        public byte[] SavePlain()
        {
            long off = 0;
            foreach (BundleNode n in Nodes) { n.Offset = off; n.Size = n.Data.Length; off += n.Data.Length; }
            BinWriter bi = new BinWriter(true);
            bi.Bytes(new byte[16]);
            bi.I32(Nodes.Count);
            foreach (BundleNode n in Nodes) { bi.U32((uint)n.Data.Length); bi.U32((uint)n.Data.Length); bi.U16(0x40); }
            bi.I32(Nodes.Count);
            foreach (BundleNode n in Nodes) { bi.I64(n.Offset); bi.I64(n.Size); bi.U32(n.Flags); bi.CStr(n.Path); }
            byte[] biRaw = bi.ToArray();
            BinWriter w = new BinWriter(true);
            w.CStr(Signature); w.U32(Version); w.CStr(UnityVersion); w.CStr(UnityRevision);
            long sizePos = w.Pos;
            w.I64(0); w.U32((uint)biRaw.Length); w.U32((uint)biRaw.Length); w.U32(0x40);
            if (Version >= 7) w.Align(16);
            w.Bytes(biRaw);
            foreach (BundleNode n in Nodes) w.Bytes(n.Data);
            byte[] res = w.ToArray();
            long sz = res.Length;
            for (int i = 0; i < 8; i++) res[sizePos + i] = (byte)(sz >> (8 * (7 - i)));
            return res;
        }

        public BundleNode FindSerialized()
        {
            foreach (BundleNode n in Nodes) if ((n.Flags & 4) != 0) return n;
            foreach (BundleNode n in Nodes) if (!n.Path.EndsWith(".resS") && !n.Path.EndsWith(".resource")) return n;
            return null;
        }
        public BundleNode FindNode(string name)
        {
            foreach (BundleNode n in Nodes) if (n.Path == name) return n;
            return null;
        }
    }

    public class TypeNode
    {
        public ushort Version; public byte Level; public byte TypeFlags; public uint TypeOff, NameOff;
        public int ByteSize, Index, MetaFlag; public ulong RefTypeHash; public string Type, Name;
    }

    public class SType
    {
        public int ClassID; public bool IsStripped; public short ScriptTypeIndex = -1;
        public byte[] ScriptID, OldTypeHash; public List<TypeNode> Nodes; public int[] TypeDeps;
        public string KlassName, NameSpace, AsmName;
    }

    public class SObject
    {
        public long PathID; public long ByteStart; public uint ByteSize; public int TypeID;
        public byte[] NewData; // se non null sostituisce i dati originali al salvataggio
    }

    public class SExternal { public string Temp; public byte[] Guid; public int Type; public string PathName; }

    // Mappa ordinata per i campi di un oggetto
    public class OMap
    {
        public List<string> Keys = new List<string>();
        public Dictionary<string, object> D = new Dictionary<string, object>();
        public object this[string k]
        {
            get { return D[k]; }
            set { if (!D.ContainsKey(k)) Keys.Add(k); D[k] = value; }
        }
        public bool Has(string k) { return D.ContainsKey(k); }
        public OMap M(string k) { return (OMap)D[k]; }
        public List<object> L(string k) { return (List<object>)D[k]; }
        public string S(string k) { return (string)D[k]; }
    }

    public class SerializedFile
    {
        public byte[] Raw;
        public uint Version; public byte Endian; public uint MetadataSize; public long FileSize, DataOffset;
        public string UnityVersion; public int Platform; public bool EnableTypeTree;
        public List<SType> Types = new List<SType>();
        public List<SType> RefTypes = new List<SType>();
        public List<SObject> Objects = new List<SObject>();
        public List<SExternal> Externals = new List<SExternal>();
        public int ObjTablePos, ObjTableEnd, HeaderEnd, MetaEnd;

        static string commonBlob;
        static readonly string[] CommonStrings = new string[] {
            "AABB","AnimationClip","AnimationCurve","AnimationState","Array","Base","BitField","bitset","bool","char",
            "ColorRGBA","Component","data","deque","double","dynamic_array","FastPropertyName","first","float","Font",
            "GameObject","Generic Mono","GradientNEW","GUID","GUIStyle","int","list","long long","map","Matrix4x4f",
            "MdFour","MonoBehaviour","MonoScript","m_ByteSize","m_Curve","m_EditorClassIdentifier","m_EditorHideFlags",
            "m_Enabled","m_ExtensionPtr","m_GameObject","m_Index","m_IsArray","m_IsStatic","m_MetaFlag","m_Name",
            "m_ObjectHideFlags","m_PrefabInternal","m_PrefabParentObject","m_Script","m_StaticEditorFlags","m_Type",
            "m_Version","Object","pair","PPtr<Component>","PPtr<GameObject>","PPtr<Material>","PPtr<MonoBehaviour>",
            "PPtr<MonoScript>","PPtr<Object>","PPtr<Prefab>","PPtr<Sprite>","PPtr<TextAsset>","PPtr<Texture>",
            "PPtr<Texture2D>","PPtr<Transform>","Prefab","Quaternionf","Rectf","RectInt","RectOffset","second","set",
            "short","size","SInt16","SInt32","SInt64","SInt8","staticvector","string","TextAsset","TextMesh","Texture",
            "Texture2D","Transform","TypelessData","UInt16","UInt32","UInt64","UInt8","unsigned int",
            "unsigned long long","unsigned short","vector","Vector2f","Vector3f","Vector4f","m_ScriptingClassIdentifier",
            "Gradient","Type*","int2_storage","int3_storage","BoundsInt","m_CorrespondingSourceObject",
            "m_PrefabInstance","m_PrefabAsset","FileSize","Hash128" };

        static string CommonStr(uint off)
        {
            if (commonBlob == null)
            {
                StringBuilder sb = new StringBuilder();
                foreach (string s in CommonStrings) { sb.Append(s); sb.Append('\0'); }
                commonBlob = sb.ToString();
                if (commonBlob.IndexOf("string\0", StringComparison.Ordinal) != 840 || commonBlob.IndexOf("Hash128\0", StringComparison.Ordinal) != 1161)
                    throw new Exception("Tabella stringhe comuni Unity non allineata");
            }
            int e = commonBlob.IndexOf('\0', (int)off);
            return commonBlob.Substring((int)off, e - (int)off);
        }

        static string StrAt(byte[] buf, uint off)
        {
            if ((off & 0x80000000u) != 0) return CommonStr(off & 0x7FFFFFFFu);
            int e = (int)off; while (buf[e] != 0) e++;
            return Encoding.UTF8.GetString(buf, (int)off, e - (int)off);
        }

        SType ReadType(BinReader r, bool isRef)
        {
            SType t = new SType();
            t.ClassID = r.I32();
            t.IsStripped = r.U8() != 0;
            t.ScriptTypeIndex = r.I16();
            if ((isRef && t.ScriptTypeIndex >= 0) || t.ClassID == 114) t.ScriptID = r.Bytes(16);
            t.OldTypeHash = r.Bytes(16);
            if (EnableTypeTree)
            {
                int nc = r.I32(); int sb = r.I32();
                t.Nodes = new List<TypeNode>(nc);
                for (int i = 0; i < nc; i++)
                {
                    TypeNode n = new TypeNode();
                    n.Version = r.U16(); n.Level = r.U8(); n.TypeFlags = r.U8(); n.TypeOff = r.U32(); n.NameOff = r.U32();
                    n.ByteSize = r.I32(); n.Index = r.I32(); n.MetaFlag = r.I32();
                    if (Version >= 19) n.RefTypeHash = r.U64();
                    t.Nodes.Add(n);
                }
                byte[] buf = r.Bytes(sb);
                foreach (TypeNode n in t.Nodes) { n.Type = StrAt(buf, n.TypeOff); n.Name = StrAt(buf, n.NameOff); }
                if (Version >= 21)
                {
                    if (isRef) { t.KlassName = r.CStr(); t.NameSpace = r.CStr(); t.AsmName = r.CStr(); }
                    else { int c = r.I32(); t.TypeDeps = new int[c]; for (int i = 0; i < c; i++) t.TypeDeps[i] = r.I32(); }
                }
            }
            return t;
        }

        public static SerializedFile Load(byte[] data)
        {
            SerializedFile f = new SerializedFile(); f.Raw = data;
            BinReader r = new BinReader(data, true);
            f.MetadataSize = r.U32(); f.FileSize = r.U32(); f.Version = r.U32(); f.DataOffset = r.U32();
            if (f.Version < 17) throw new NotSupportedException("SerializedFile versione " + f.Version + " non supportata");
            f.Endian = r.U8(); r.Bytes(3);
            if (f.Version >= 22) { f.MetadataSize = r.U32(); f.FileSize = r.I64(); f.DataOffset = r.I64(); r.I64(); }
            f.HeaderEnd = r.P;
            r.Big = f.Endian != 0;
            f.UnityVersion = r.CStr(); f.Platform = r.I32(); f.EnableTypeTree = r.U8() != 0;
            int tc = r.I32();
            for (int i = 0; i < tc; i++) f.Types.Add(f.ReadType(r, false));
            f.ObjTablePos = r.P;
            int oc = r.I32();
            for (int i = 0; i < oc; i++)
            {
                r.Align(4);
                SObject o = new SObject();
                o.PathID = r.I64();
                o.ByteStart = (f.Version >= 22 ? r.I64() : r.U32()) + f.DataOffset;
                o.ByteSize = r.U32(); o.TypeID = r.I32();
                f.Objects.Add(o);
            }
            f.ObjTableEnd = r.P;
            int sc = r.I32();
            for (int i = 0; i < sc; i++) { r.I32(); r.Align(4); r.I64(); }
            int ec = r.I32();
            for (int i = 0; i < ec; i++)
            {
                SExternal e = new SExternal(); e.Temp = r.CStr(); e.Guid = r.Bytes(16); e.Type = r.I32(); e.PathName = r.CStr(); f.Externals.Add(e);
            }
            if (f.Version >= 20) { int rc = r.I32(); for (int i = 0; i < rc; i++) f.RefTypes.Add(f.ReadType(r, true)); }
            r.CStr();
            f.MetaEnd = r.P;
            return f;
        }

        public byte[] GetData(SObject o)
        {
            if (o.NewData != null) return o.NewData;
            byte[] d = new byte[o.ByteSize]; Buffer.BlockCopy(Raw, (int)o.ByteStart, d, 0, (int)o.ByteSize); return d;
        }

        public SObject Find(long pathID)
        {
            foreach (SObject o in Objects) if (o.PathID == pathID) return o;
            return null;
        }

        public object Read(SObject o)
        {
            SType t = Types[o.TypeID];
            if (t.Nodes == null || t.Nodes.Count == 0) throw new InvalidOperationException("Nessun TypeTree per l'oggetto " + o.PathID);
            BinReader r = new BinReader(GetData(o), Endian != 0);
            int i = 0;
            object v = TypeTreeIO.Read(t.Nodes, ref i, r);
            if (r.P != r.B.Length) throw new InvalidDataException("Oggetto " + o.PathID + ": letti " + r.P + " byte su " + r.B.Length);
            return v;
        }

        public void Write(SObject o, object value)
        {
            SType t = Types[o.TypeID];
            BinWriter w = new BinWriter(Endian != 0);
            int i = 0;
            TypeTreeIO.Write(t.Nodes, ref i, w, value);
            o.NewData = w.ToArray();
        }

        // Ricostruisce il file: metadati invariati (stessa dimensione), tabella oggetti e area dati rigenerate.
        public byte[] Save()
        {
            if (Version < 22) throw new NotSupportedException("Salvataggio supportato solo per SerializedFile v22+");
            bool big = Endian != 0;
            MemoryStream data = new MemoryStream();
            long[] starts = new long[Objects.Count]; uint[] sizes = new uint[Objects.Count];
            for (int i = 0; i < Objects.Count; i++)
            {
                while (data.Position % 8 != 0) data.WriteByte(0);
                byte[] d = GetData(Objects[i]);
                starts[i] = data.Position; sizes[i] = (uint)d.Length;
                data.Write(d, 0, d.Length);
            }
            BinWriter t = new BinWriter(big);
            t.I32(Objects.Count);
            int basePos = ObjTablePos + 4;
            for (int i = 0; i < Objects.Count; i++)
            {
                while ((basePos + (t.Pos - 4)) % 4 != 0) t.U8(0);
                t.I64(Objects[i].PathID); t.I64(starts[i]); t.U32(sizes[i]); t.I32(Objects[i].TypeID);
            }
            byte[] table = t.ToArray();
            if (table.Length != ObjTableEnd - ObjTablePos) throw new Exception("Dimensione tabella oggetti cambiata");
            byte[] dd = data.ToArray();
            long newSize = DataOffset + dd.Length;
            byte[] res = new byte[newSize];
            Buffer.BlockCopy(Raw, 0, res, 0, (int)DataOffset);
            Buffer.BlockCopy(table, 0, res, ObjTablePos, table.Length);
            Buffer.BlockCopy(dd, 0, res, (int)DataOffset, dd.Length);
            // header v22: [0..4) meta32 [4..8) size32 [8..12) ver [12..16) off32 [16] endian [20..24) metaSize [24..32) fileSize (big endian)
            for (int i = 0; i < 8; i++) res[24 + i] = (byte)(newSize >> (8 * (7 - i)));
            return res;
        }
    }

    public static class SerializedFileExt
    {
        // Salvataggio "minimo" per i file .assets del gioco: gli oggetti modificati vengono accodati in fondo
        // e si aggiornano solo la loro voce in tabella e la dimensione nel header. Tutto il resto resta identico.
        public static byte[] SaveAppend(SerializedFile f)
        {
            if (f.Version < 22) throw new NotSupportedException("Salvataggio supportato solo per SerializedFile v22+");
            bool big = f.Endian != 0;
            MemoryStream ms = new MemoryStream(f.Raw.Length + 65536);
            ms.Write(f.Raw, 0, f.Raw.Length);
            // posizione di ogni voce: count(4) poi per oggetto [allineamento a 4] pathID(8) offset(8) size(4) typeID(4)
            int p = f.ObjTablePos + 4;
            foreach (SObject o in f.Objects)
            {
                if (p % 4 != 0) p += 4 - p % 4;
                if (o.NewData != null)
                {
                    while (ms.Length % 16 != 0) ms.WriteByte(0);
                    long start = ms.Length - f.DataOffset;
                    ms.Position = ms.Length; ms.Write(o.NewData, 0, o.NewData.Length);
                    BinWriter e = new BinWriter(big); e.I64(start); e.U32((uint)o.NewData.Length);
                    byte[] eb = e.ToArray();
                    ms.Position = p + 8; ms.Write(eb, 0, eb.Length);
                }
                p += 24;
            }
            if (p != f.ObjTableEnd) throw new Exception("Tabella oggetti: struttura inattesa");
            byte[] res = ms.ToArray();
            long newSize = res.Length;
            for (int i = 0; i < 8; i++) res[24 + i] = (byte)(newSize >> (8 * (7 - i)));
            return res;
        }
    }

    public class Typeless { public byte[] Data; public Typeless(byte[] d) { Data = d; } }

    public static class TypeTreeIO
    {
        static int SubtreeEnd(List<TypeNode> n, int i)
        {
            int lv = n[i].Level; int j = i + 1;
            while (j < n.Count && n[j].Level > lv) j++;
            return j;
        }

        public static object Read(List<TypeNode> n, ref int i, BinReader r)
        {
            TypeNode node = n[i];
            bool align = (node.MetaFlag & 0x4000) != 0;
            object val;
            switch (node.Type)
            {
                case "SInt8": val = r.I8(); i++; break;
                case "UInt8": case "char": val = r.U8(); i++; break;
                case "bool": val = r.U8() != 0; i++; break;
                case "short": case "SInt16": val = r.I16(); i++; break;
                case "UInt16": case "unsigned short": val = r.U16(); i++; break;
                case "int": case "SInt32": val = r.I32(); i++; break;
                case "UInt32": case "unsigned int": case "Type*": val = r.U32(); i++; break;
                case "long long": case "SInt64": val = r.I64(); i++; break;
                case "UInt64": case "unsigned long long": case "FileSize": val = r.U64(); i++; break;
                case "float": val = r.F32(); i++; break;
                case "double": val = r.F64(); i++; break;
                case "string":
                    {
                        int len = r.I32(); val = Encoding.UTF8.GetString(r.B, r.P, len); r.P += len; r.Align(4);
                        i = SubtreeEnd(n, i); break;
                    }
                case "TypelessData":
                    {
                        int len = r.I32(); val = new Typeless(r.Bytes(len)); i = SubtreeEnd(n, i); break;
                    }
                default:
                    if (i + 1 < n.Count && n[i + 1].Type == "Array" && n[i + 1].Level == node.Level + 1)
                    {
                        if ((n[i + 1].MetaFlag & 0x4000) != 0) align = true;
                        int size = r.I32();
                        int elem = i + 3; int end = SubtreeEnd(n, i);
                        string et = n[elem].Type;
                        if (elem + 1 == end && (et == "UInt8" || et == "char" || et == "SInt8"))
                            val = r.Bytes(size);
                        else
                        {
                            List<object> list = new List<object>(size);
                            for (int k = 0; k < size; k++) { int j = elem; list.Add(Read(n, ref j, r)); }
                            val = list;
                        }
                        i = end;
                    }
                    else
                    {
                        OMap m = new OMap(); int end = SubtreeEnd(n, i); int j = i + 1;
                        while (j < end) { string name = n[j].Name; m[name] = Read(n, ref j, r); }
                        val = m; i = end;
                    }
                    break;
            }
            if (align) r.Align(4);
            return val;
        }

        public static void Write(List<TypeNode> n, ref int i, BinWriter w, object v)
        {
            TypeNode node = n[i];
            bool align = (node.MetaFlag & 0x4000) != 0;
            switch (node.Type)
            {
                case "SInt8": w.U8((byte)(sbyte)v); i++; break;
                case "UInt8": case "char": w.U8((byte)v); i++; break;
                case "bool": w.U8((bool)v ? (byte)1 : (byte)0); i++; break;
                case "short": case "SInt16": w.U16((ushort)(short)v); i++; break;
                case "UInt16": case "unsigned short": w.U16((ushort)v); i++; break;
                case "int": case "SInt32": w.I32((int)v); i++; break;
                case "UInt32": case "unsigned int": case "Type*": w.U32((uint)v); i++; break;
                case "long long": case "SInt64": w.I64((long)v); i++; break;
                case "UInt64": case "unsigned long long": case "FileSize": w.U64((ulong)v); i++; break;
                case "float": w.F32((float)v); i++; break;
                case "double": w.F64((double)v); i++; break;
                case "string":
                    {
                        byte[] b = Encoding.UTF8.GetBytes((string)v); w.I32(b.Length); w.Bytes(b); w.Align(4);
                        i = SubtreeEnd(n, i); break;
                    }
                case "TypelessData":
                    {
                        byte[] b = ((Typeless)v).Data; w.I32(b.Length); w.Bytes(b); i = SubtreeEnd(n, i); break;
                    }
                default:
                    if (i + 1 < n.Count && n[i + 1].Type == "Array" && n[i + 1].Level == node.Level + 1)
                    {
                        if ((n[i + 1].MetaFlag & 0x4000) != 0) align = true;
                        int elem = i + 3; int end = SubtreeEnd(n, i);
                        byte[] raw = v as byte[];
                        if (raw != null) { w.I32(raw.Length); w.Bytes(raw); }
                        else
                        {
                            List<object> list = (List<object>)v;
                            w.I32(list.Count);
                            foreach (object e in list) { int j = elem; Write(n, ref j, w, e); }
                        }
                        i = end;
                    }
                    else
                    {
                        OMap m = (OMap)v; int end = SubtreeEnd(n, i); int j = i + 1;
                        while (j < end) { string name = n[j].Name; Write(n, ref j, w, m[name]); }
                        i = end;
                    }
                    break;
            }
            if (align) w.Align(4);
        }
    }

    public static class Json
    {
        public static string Esc(string s)
        {
            StringBuilder sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // maxList/maxStr > 0 troncano liste e stringhe per i dump esplorativi
        public static void Write(TextWriter w, object v, int indent, int maxList, int maxStr)
        {
            string pad = new string(' ', indent * 1);
            if (v == null) { w.Write("null"); return; }
            OMap m = v as OMap;
            if (m != null)
            {
                w.Write("{");
                for (int i = 0; i < m.Keys.Count; i++)
                {
                    w.Write(i == 0 ? "\n" : ",\n"); w.Write(pad); w.Write(' '); w.Write(Esc(m.Keys[i])); w.Write(": ");
                    Write(w, m.D[m.Keys[i]], indent + 1, maxList, maxStr);
                }
                w.Write("\n"); w.Write(pad); w.Write("}"); return;
            }
            List<object> l = v as List<object>;
            if (l != null)
            {
                w.Write("[");
                int cnt = maxList > 0 ? Math.Min(maxList, l.Count) : l.Count;
                for (int i = 0; i < cnt; i++)
                {
                    w.Write(i == 0 ? "\n" : ",\n"); w.Write(pad); w.Write(' ');
                    Write(w, l[i], indent + 1, maxList, maxStr);
                }
                if (cnt < l.Count) { w.Write(",\n"); w.Write(pad); w.Write(" \"... (" + l.Count + " elementi in totale)\""); }
                w.Write("\n"); w.Write(pad); w.Write("]"); return;
            }
            string s = v as string;
            if (s != null)
            {
                if (maxStr > 0 && s.Length > maxStr) s = s.Substring(0, maxStr) + "…(" + s.Length + ")";
                w.Write(Esc(s)); return;
            }
            byte[] b = v as byte[];
            if (b != null) { w.Write("\"<" + b.Length + " byte>\""); return; }
            Typeless tl = v as Typeless;
            if (tl != null) { w.Write("\"<typeless " + tl.Data.Length + " byte>\""); return; }
            if (v is bool) { w.Write((bool)v ? "true" : "false"); return; }
            if (v is float) { w.Write(((float)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is double) { w.Write(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            w.Write(Convert.ToString(v, CultureInfo.InvariantCulture));
        }
    }
}
