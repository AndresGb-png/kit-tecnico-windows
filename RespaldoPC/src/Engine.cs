using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace RespaldoPC
{
    public static class N
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, SHARE_ALL = 7, OPEN_EXISTING = 3, CREATE_ALWAYS = 2;
        public const uint BACKUP_SEMANTICS = 0x02000000, SEQUENTIAL = 0x08000000, NO_BUFFERING = 0x20000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int n, out int read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadFile")]
        public static extern bool ReadFilePtr(SafeFileHandle h, IntPtr buf, int n, out int read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, byte[] buf, int n, out int written, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FlushFileBuffers(SafeFileHandle h);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetFileTime(SafeFileHandle h, ref long created, ref long accessed, ref long written);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateDirectoryW")]
        public static extern bool CreateDirectory(string path, IntPtr sec);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "DeleteFileW")]
        public static extern bool DeleteFile(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "MoveFileExW")]
        public static extern bool MoveFileEx(string from, string to, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SetFileAttributesW")]
        public static extern bool SetFileAttributes(string path, uint attrs);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFileAttributesExW")]
        public static extern bool GetFileAttributesEx(string path, int level, out AttrData data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindFirstFileW")]
        public static extern IntPtr FindFirstFile(string path, out FindData data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindNextFileW")]
        public static extern bool FindNextFile(IntPtr h, out FindData data);
        [DllImport("kernel32.dll")] public static extern bool FindClose(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetDiskFreeSpaceExW")]
        public static extern bool GetDiskFreeSpaceEx(string path, out long free, out long total, out long totalFree);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumePathNameW")]
        public static extern bool GetVolumePathName(string path, StringBuilder volume, int len);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeInformationW")]
        public static extern bool GetVolumeInformation(string root, StringBuilder name, int nameLen, out uint serial, out uint maxLen, out uint flags, StringBuilder fs, int fsLen);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, IntPtr inBuf, int inSize, byte[] outBuf, int outSize, out int ret, IntPtr ov);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr OpenThread(uint access, bool inherit, uint id);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool CancelSynchronousIo(IntPtr thread);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr VirtualAlloc(IntPtr a, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool VirtualFree(IntPtr a, UIntPtr size, uint type);
        [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr p, uint access, out IntPtr token);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LookupPrivilegeValueW")] static extern bool LookupPrivilegeValue(string sys, string name, out long luid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPriv state, int len, IntPtr prev, IntPtr ret);

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct TokenPriv { public int Count; public long Luid; public int Attr; }

        // Pack = 4: las fechas de Windows no están alineadas a 8 bytes en esta estructura.
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct AttrData { public uint Attrs; public long Created, Accessed, Written; public uint SizeHigh, SizeLow; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct FindData
        {
            public uint Attrs;
            public uint CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh, SizeHigh, SizeLow, Reserved0, Reserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string Alt;
        }

        public static void EnablePrivilege(string name)
        {
            try
            {
                IntPtr token;
                if (!OpenProcessToken(GetCurrentProcess(), 0x28, out token)) return;
                long luid;
                if (LookupPrivilegeValue(null, name, out luid))
                {
                    var tp = new TokenPriv { Count = 1, Luid = luid, Attr = 2 };
                    AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                }
                CloseHandle(token);
            }
            catch { }
        }

        // Prefijo \\?\ para superar el límite clásico de 260 caracteres.
        public static string LP(string path)
        {
            string p = path.Replace('/', '\\');
            if (p.StartsWith(@"\\?\")) return p;
            if (p.Length > 3) p = p.TrimEnd('\\');
            if (p.StartsWith(@"\\")) return @"\\?\UNC\" + p.Substring(2);
            return @"\\?\" + p;
        }

        public static bool Stat(string path, out long size, out long written, out uint attrs)
        {
            AttrData d;
            if (GetFileAttributesEx(LP(path), 0, out d))
            {
                size = ((long)d.SizeHigh << 32) | d.SizeLow;
                written = d.Written;
                attrs = d.Attrs;
                return true;
            }
            size = written = 0; attrs = 0;
            return false;
        }

        public static bool Exists(string path) { long a, b; uint c; return Stat(path, out a, out b, out c); }

        public static bool IsDir(string path) { long a, b; uint c; return Stat(path, out a, out b, out c) && (c & 0x10) != 0; }

        // Path.GetDirectoryName rechaza rutas de más de 260 caracteres, justo las que hay que soportar.
        public static string Parent(string path)
        {
            string p = path.TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            if (i <= 0) return null;
            return i == 2 && p[1] == ':' ? p.Substring(0, 3) : p.Substring(0, i);
        }

        public static void EnsureDir(string path)
        {
            string lp = LP(path);
            if (CreateDirectory(lp, IntPtr.Zero)) return;
            int err = Marshal.GetLastWin32Error();
            if (err == 183) return;
            if (err == 3)
            {
                string parent = Parent(path);
                if (string.IsNullOrEmpty(parent) || parent == path) throw new Win32Exception(err);
                EnsureDir(parent);
                if (CreateDirectory(lp, IntPtr.Zero) || Marshal.GetLastWin32Error() == 183) return;
                err = Marshal.GetLastWin32Error();
            }
            throw new Win32Exception(err);
        }

        public static string Reason(int code)
        {
            switch (code)
            {
                case 5: return "Sin permiso para leerlo. Abre el programa como administrador.";
                case 2: case 3: return "El archivo ya no existe o cambió de lugar.";
                case 32: case 33: return "Otro programa lo tiene abierto. Ciérralo y pulsa «Reintentar».";
                case 23: return "El disco tiene un sector dañado en este archivo.";
                case 1117: return "El disco dio un error físico al leerlo.";
                case 995: case 1460: return "El disco dejó de responder al leerlo: posible daño físico.";
                case 21: case 1167: return "La unidad se desconectó.";
                case 112: case 39: return "No queda espacio en el destino.";
                case 206: return "La ruta es demasiado larga para el destino.";
                case 87: return "El destino no admite este archivo. Si es FAT32, tiene un límite de 4 GB.";
                case 223: return "El archivo supera el tamaño máximo del destino (FAT32 admite hasta 4 GB).";
                default: return new Win32Exception(code).Message + " (código " + code + ")";
            }
        }
    }

    public class FileEntry
    {
        public string Full, Name;
        public long Size, Written;
        public uint Attrs;
        public bool CloudOnly { get { return (Attrs & (0x400000u | 0x40000u | 0x1000u)) != 0; } }
    }

    public static class Fs
    {
        static readonly HashSet<string> SkipFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "thumbs.db", "ehthumbs.db", "ehthumbs_vista.db" };
        static readonly HashSet<string> SkipDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "$RECYCLE.BIN", "System Volume Information", "RECYCLER" };

        public static bool IsLink(uint attrs, uint tag)
        {
            // Junction y symlink: seguirlos causaría bucles o copias duplicadas. Los de OneDrive sí se recorren.
            return (attrs & 0x400) != 0 && (tag == 0xA0000003 || tag == 0xA000000C);
        }

        // Recorre sin recursión para aguantar árboles muy profundos.
        public static void Walk(string root, Action<FileEntry> onFile, Action<string> onDir, Action<string, int> onError, Func<bool> stop)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                if (stop != null && stop()) return;
                string dir = pending.Pop();
                if (onDir != null) onDir(dir);
                N.FindData d;
                IntPtr h = N.FindFirstFile(N.LP(dir).TrimEnd('\\') + @"\*", out d);
                if (h == new IntPtr(-1))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err != 2 && err != 18 && onError != null) onError(dir, err);
                    continue;
                }
                try
                {
                    do
                    {
                        if (d.Name == "." || d.Name == "..") continue;
                        string full = dir.TrimEnd('\\') + "\\" + d.Name;
                        if ((d.Attrs & 0x10) != 0)
                        {
                            if (!IsLink(d.Attrs, d.Reserved0) && !SkipDirs.Contains(d.Name)) pending.Push(full);
                        }
                        else if (!IsLink(d.Attrs, d.Reserved0) && !SkipFiles.Contains(d.Name) && !d.Name.StartsWith("~$") && !IsCopyTemp(d.Name))
                        {
                            onFile(new FileEntry
                            {
                                Full = full, Name = d.Name, Attrs = d.Attrs,
                                Size = ((long)d.SizeHigh << 32) | d.SizeLow,
                                Written = ((long)d.WrittenHigh << 32) | d.WrittenLow
                            });
                        }
                    } while (N.FindNextFile(h, out d));
                }
                finally { N.FindClose(h); }
            }
        }

        // Temporal de una copia cortada a medias: nunca debe contarse como dato ni restaurarse.
        public static bool IsCopyTemp(string name)
        {
            return name.StartsWith("~rpc", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
        }

        public static void RemoveCopyTemps(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var dirs = new List<string>();
                var files = new List<string>();
                Walk1(pending.Pop(), dirs, files);
                foreach (var f in files)
                    if (IsCopyTemp(Path.GetFileName(f))) N.DeleteFile(N.LP(f));
                foreach (var d in dirs) pending.Push(d);
            }
        }

        // Lista un solo nivel, sin seguir enlaces. Devuelve vacío si no se puede leer.
        public static void Walk1(string dir, List<string> dirs, List<string> files = null)
        {
            N.FindData d;
            IntPtr h = N.FindFirstFile(N.LP(dir).TrimEnd('\\') + @"\*", out d);
            if (h == new IntPtr(-1)) return;
            try
            {
                do
                {
                    if (d.Name == "." || d.Name == "..") continue;
                    if (IsLink(d.Attrs, d.Reserved0)) continue;
                    string full = dir.TrimEnd('\\') + "\\" + d.Name;
                    if ((d.Attrs & 0x10) != 0) dirs.Add(full);
                    else if (files != null) files.Add(full);
                } while (N.FindNextFile(h, out d));
            }
            finally { N.FindClose(h); }
        }

        public static string Bytes(double b)
        {
            if (b >= 1e12) return (b / 1e12).ToString("0.##") + " TB";
            if (b >= 1e9) return (b / 1e9).ToString("0.#") + " GB";
            if (b >= 1e6) return (b / 1e6).ToString("0") + " MB";
            if (b >= 1e3) return (b / 1e3).ToString("0") + " KB";
            return b.ToString("0") + " bytes";
        }

        public static int PhysicalDisk(string path)
        {
            try
            {
                if (path.StartsWith(@"\\") || path.Length < 2 || path[1] != ':') return -1;
                using (var h = N.CreateFile(@"\\.\" + path.Substring(0, 2), 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) return -1;
                    var buf = new byte[12];
                    int ret;
                    if (!N.DeviceIoControl(h, 0x2D1080, IntPtr.Zero, 0, buf, 12, out ret, IntPtr.Zero)) return -1;
                    return BitConverter.ToInt32(buf, 4);
                }
            }
            catch { return -1; }
        }

        public static string FileSystem(string path)
        {
            try
            {
                var vol = new StringBuilder(300);
                if (!N.GetVolumePathName(path, vol, vol.Capacity)) return "";
                var fs = new StringBuilder(32);
                uint a, b, c;
                return N.GetVolumeInformation(vol.ToString(), null, 0, out a, out b, out c, fs, fs.Capacity) ? fs.ToString() : "";
            }
            catch { return ""; }
        }

        public static long FreeSpace(string path)
        {
            long free, t, tf;
            return N.GetDiskFreeSpaceEx(path, out free, out t, out tf) ? free : -1;
        }
    }

    public enum Policy { Mirror, KeepBoth, ReplaceKeepOld }

    public class CopyTask
    {
        public string Group, Label, Src, Dst, Rel;
        public bool IsFile;
        public Policy Policy = Policy.Mirror;
        public long Size;
        public int Count;
        public bool Selected = true;
        public string Hint;
        public object Node;
    }

    public class Issue { public string Group, Path, Reason; }

    public class TaskStat { public int Copied, Skipped, Failed, Cloud; public long Bytes; }

    public class ManifestEntry { public string Status, Sha; public long Size, Written; public string Rel, Source; }

    public static class Manifest
    {
        public const string FileName = "archivos.tsv";

        public static Dictionary<string, ManifestEntry> Load(string backupRoot)
        {
            var map = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(backupRoot, FileName);
            if (!File.Exists(path)) return map;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var p = line.Split('\t');
                if (p.Length < 6) continue;
                long size, written;
                long.TryParse(p[1], out size);
                long.TryParse(p[2], out written);
                map[p[4]] = new ManifestEntry { Status = p[0], Size = size, Written = written, Sha = p[3], Rel = p[4], Source = p[5] };
            }
            return map;
        }
    }

    // Copia archivo por archivo: un fallo se anota y se sigue con el resto.
    public class CopyEngine : IDisposable
    {
        const int BufSize = 1 << 20;
        const int StallMs = 60000;

        public volatile bool Cancel;
        public volatile string Current = "";
        public volatile string Fatal;
        public long BytesDone, BytesTotal, FilesDone, FailCount;
        public readonly List<Issue> Failed = new List<Issue>();
        public readonly List<Issue> Cloud = new List<Issue>();
        public readonly Dictionary<CopyTask, TaskStat> Stats = new Dictionary<CopyTask, TaskStat>();
        public bool Verify = true;
        public StreamWriter ManifestOut;
        public Dictionary<string, ManifestEntry> Known = new Dictionary<string, ManifestEntry>(StringComparer.OrdinalIgnoreCase);
        public bool CheckAgainstKnown;

        readonly byte[] buffer = new byte[BufSize];
        readonly IntPtr aligned;
        readonly object gate = new object();
        readonly HashSet<string> made = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int lastTick;
        volatile bool readingSource;
        IntPtr workerHandle = IntPtr.Zero;
        Timer watchdog;

        public CopyEngine()
        {
            aligned = N.VirtualAlloc(IntPtr.Zero, new UIntPtr((uint)BufSize), 0x3000, 0x04);
        }

        public void Dispose()
        {
            if (watchdog != null) watchdog.Dispose();
            if (workerHandle != IntPtr.Zero) N.CloseHandle(workerHandle);
            if (aligned != IntPtr.Zero) N.VirtualFree(aligned, UIntPtr.Zero, 0x8000);
        }

        class FatalDest : Exception { public FatalDest(string m) : base(m) { } }

        public void Run(IEnumerable<CopyTask> tasks)
        {
            workerHandle = N.OpenThread(1, false, N.GetCurrentThreadId());
            lastTick = Environment.TickCount;
            // Un disco dañado puede quedarse colgado en una lectura: se aborta esa lectura y se sigue.
            watchdog = new Timer(_ =>
            {
                if (readingSource && unchecked(Environment.TickCount - lastTick) > StallMs && workerHandle != IntPtr.Zero)
                    N.CancelSynchronousIo(workerHandle);
            }, null, 5000, 5000);
            N.SetThreadExecutionState(0x80000001);
            try
            {
                foreach (var t in tasks)
                {
                    if (Cancel || Fatal != null) break;
                    RunTask(t);
                }
            }
            finally
            {
                N.SetThreadExecutionState(0x80000000);
                try { if (ManifestOut != null) ManifestOut.Flush(); }
                catch (IOException) { if (Fatal == null) Fatal = "El destino se desconectó o no permite escribir. Revísalo y pulsa «Reintentar»."; }
            }
        }

        void RunTask(CopyTask t)
        {
            TaskStat stat;
            lock (gate)
            {
                if (!Stats.TryGetValue(t, out stat)) Stats[t] = stat = new TaskStat();
            }
            try
            {
                if (t.IsFile)
                {
                    long size, written; uint attrs;
                    if (N.Stat(t.Src, out size, out written, out attrs))
                        OneFile(t, stat, new FileEntry { Full = t.Src, Name = Path.GetFileName(t.Src), Size = size, Written = written, Attrs = attrs }, t.Dst, t.Rel);
                    return;
                }
                string root = t.Src.TrimEnd('\\');
                Fs.Walk(root, f =>
                {
                    string inner = f.Full.Substring(root.Length).TrimStart('\\');
                    OneFile(t, stat, f, t.Dst.TrimEnd('\\') + "\\" + inner, (t.Rel ?? "") + "\\" + inner);
                }, d =>
                {
                    string inner = d.Substring(root.Length).TrimStart('\\');
                    string target = inner.Length == 0 ? t.Dst : t.Dst.TrimEnd('\\') + "\\" + inner;
                    try { MakeDir(target); } catch (Win32Exception ex) { DestError(ex.NativeErrorCode, target); }
                }, (dir, err) => AddFail(t, stat, dir, N.Reason(err)), () => Cancel || Fatal != null);
            }
            catch (FatalDest ex) { Fatal = ex.Message; }
        }

        void MakeDir(string path)
        {
            if (made.Contains(path)) return;
            N.EnsureDir(path);
            made.Add(path);
        }

        void DestError(int code, string path)
        {
            if (code == 112 || code == 39)
                throw new FatalDest("Se acabó el espacio en el destino. Libera espacio o usa otra unidad y pulsa «Reintentar».");
            // Al retirar un disco, Windows suele responder «ruta no encontrada» en vez de un error claro.
            bool gone = (code == 3 || code == 2) && path.Length > 2 && path[1] == ':' && !N.IsDir(path.Substring(0, 3));
            if (gone || code == 21 || code == 1167 || code == 55 || code == 64 || code == 19)
                throw new FatalDest("El destino se desconectó o no permite escribir. Revísalo y pulsa «Reintentar».");
        }

        void AddFail(CopyTask t, TaskStat stat, string path, string reason)
        {
            lock (gate)
            {
                Failed.Add(new Issue { Group = t.Group, Path = path, Reason = reason });
                stat.Failed++;
            }
            Interlocked.Increment(ref FailCount);
        }

        void Log(string status, FileEntry f, string sha, string rel)
        {
            if (ManifestOut == null || string.IsNullOrEmpty(rel)) return;
            try
            {
                ManifestOut.WriteLine(status + "\t" + f.Size + "\t" + f.Written + "\t" + sha + "\t" + rel + "\t" + f.Full);
                ManifestOut.Flush();
            }
            catch (IOException)
            {
                throw new FatalDest("El destino se desconectó o no permite escribir. Revísalo y pulsa «Reintentar».");
            }
        }

        void OneFile(CopyTask t, TaskStat stat, FileEntry f, string dst, string rel)
        {
            Current = f.Full;
            lastTick = Environment.TickCount;

            if (f.CloudOnly)
            {
                lock (gate)
                {
                    Cloud.Add(new Issue { Group = t.Group, Path = f.Full, Reason = "Solo está en la nube; no ocupa espacio en este disco." });
                    stat.Cloud++;
                }
                Log("NUBE", f, "", rel);
                return;
            }

            long dSize, dWritten; uint dAttrs;
            if (N.Stat(dst, out dSize, out dWritten, out dAttrs))
            {
                if (dSize == f.Size && dWritten == f.Written)
                {
                    // Ya estaba: reanudar un respaldo, o restaurar algo que el equipo ya tiene.
                    ManifestEntry known;
                    Log("OK", f, Known.TryGetValue(rel ?? "", out known) ? known.Sha : "", rel);
                    stat.Skipped++;
                    Interlocked.Increment(ref FilesDone);
                    Interlocked.Add(ref BytesDone, f.Size);
                    return;
                }
                if (t.Policy == Policy.KeepBoth)
                {
                    string free = FreeName(dst, f);
                    if (free == null)
                    {
                        // Una restauración anterior ya dejó esta misma versión al lado.
                        stat.Skipped++;
                        Interlocked.Increment(ref FilesDone);
                        Interlocked.Add(ref BytesDone, f.Size);
                        return;
                    }
                    dst = free;
                }
                else if (t.Policy == Policy.ReplaceKeepOld)
                {
                    string old = dst + ".antes_del_respaldo";
                    N.MoveFileEx(N.LP(dst), N.LP(old), 1);
                }
            }

            int attempts = 0;
            while (true)
            {
                attempts++;
                string sha; string error; int code;
                if (TryCopy(f, dst, out sha, out error, out code))
                {
                    ManifestEntry known;
                    if (CheckAgainstKnown && Known.TryGetValue(rel ?? "", out known) && known.Sha.Length > 0 && !known.Sha.Equals(sha, StringComparison.OrdinalIgnoreCase))
                    {
                        N.DeleteFile(N.LP(dst));
                        AddFail(t, stat, f.Full, "Este archivo cambió o se dañó dentro del respaldo.");
                        return;
                    }
                    Log("OK", f, sha, rel);
                    stat.Copied++;
                    stat.Bytes += f.Size;
                    Interlocked.Increment(ref FilesDone);
                    return;
                }
                bool retry = attempts < 3 && (code == 32 || code == 33 || code == -1);
                if (retry)
                {
                    Thread.Sleep(800);
                    continue;
                }
                AddFail(t, stat, f.Full, error);
                Log("FALLO", f, "", rel);
                Interlocked.Add(ref BytesDone, f.Size);
                Interlocked.Increment(ref FilesDone);
                return;
            }
        }

        // Devuelve un nombre libre, o null si una copia idéntica ya está guardada con ese sufijo.
        static string FreeName(string path, FileEntry f)
        {
            string dir = N.Parent(path), stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
            for (int i = 1; i < 1000; i++)
            {
                string candidate = dir + "\\" + stem + (i == 1 ? " (respaldo)" : " (respaldo " + i + ")") + ext;
                long size, written; uint attrs;
                if (!N.Stat(candidate, out size, out written, out attrs)) return candidate;
                if (size == f.Size && written == f.Written) return null;
            }
            return dir + "\\" + stem + " (" + Guid.NewGuid().ToString("N") + ")" + ext;
        }

        // Se escribe en un temporal y se renombra al final: un corte nunca deja un archivo a medias con su nombre real.
        bool TryCopy(FileEntry f, string dst, out string sha, out string error, out int code)
        {
            sha = ""; error = null; code = 0;
            long startBytes = Interlocked.Read(ref BytesDone), wrote = 0;
            string dir = N.Parent(dst);
            string temp = dir + "\\~rpc" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".tmp";
            bool ok = false;
            try
            {
                try { MakeDir(dir); }
                catch (Win32Exception ex) { DestError(ex.NativeErrorCode, dir); code = ex.NativeErrorCode; error = N.Reason(code); return false; }

                using (var src = N.CreateFile(N.LP(f.Full), N.GENERIC_READ, N.SHARE_ALL, IntPtr.Zero, N.OPEN_EXISTING, N.BACKUP_SEMANTICS | N.SEQUENTIAL, IntPtr.Zero))
                {
                    if (src.IsInvalid) { code = Marshal.GetLastWin32Error(); error = N.Reason(code); return false; }
                    using (var dest = N.CreateFile(N.LP(temp), N.GENERIC_WRITE, 1, IntPtr.Zero, N.CREATE_ALWAYS, N.SEQUENTIAL, IntPtr.Zero))
                    {
                        if (dest.IsInvalid)
                        {
                            code = Marshal.GetLastWin32Error();
                            DestError(code, temp);
                            error = "No se pudo escribir en el destino: " + N.Reason(code);
                            return false;
                        }
                        using (var hash = SHA256.Create())
                        {
                            while (true)
                            {
                                int read, written;
                                lastTick = Environment.TickCount;
                                readingSource = true;
                                bool rok = N.ReadFile(src, buffer, BufSize, out read, IntPtr.Zero);
                                int rerr = rok ? 0 : Marshal.GetLastWin32Error();
                                readingSource = false;
                                if (!rok) { code = rerr; error = N.Reason(code); return false; }
                                if (read == 0) break;
                                hash.TransformBlock(buffer, 0, read, null, 0);
                                if (!N.WriteFile(dest, buffer, read, out written, IntPtr.Zero) || written != read)
                                {
                                    code = Marshal.GetLastWin32Error();
                                    DestError(code, temp);
                                    error = "No se pudo escribir en el destino: " + N.Reason(code);
                                    return false;
                                }
                                wrote += read;
                                Interlocked.Add(ref BytesDone, read);
                                if (Cancel) { code = 995; error = "Se canceló antes de terminar este archivo."; return false; }
                            }
                            hash.TransformFinalBlock(new byte[0], 0, 0);
                            sha = BitConverter.ToString(hash.Hash).Replace("-", "");
                        }
                        N.FlushFileBuffers(dest);
                        long created = f.Written, accessed = f.Written, modified = f.Written;
                        N.SetFileTime(dest, ref created, ref accessed, ref modified);
                    }
                }

                if (Verify)
                {
                    string again = HashFile(temp);
                    if (again == null || !again.Equals(sha, StringComparison.OrdinalIgnoreCase))
                    {
                        code = -1;
                        error = "La copia no coincide con el original al verificarla.";
                        return false;
                    }
                }

                if (!N.MoveFileEx(N.LP(temp), N.LP(dst), 1))
                {
                    code = Marshal.GetLastWin32Error();
                    error = "No se pudo poner el nombre final: " + N.Reason(code);
                    return false;
                }
                uint keep = f.Attrs & 0x27;
                if (keep != 0) N.SetFileAttributes(N.LP(dst), keep);
                ok = true;
                return true;
            }
            finally
            {
                if (!ok)
                {
                    N.DeleteFile(N.LP(temp));
                    Interlocked.Add(ref BytesDone, -wrote);
                }
            }
        }

        // Relee sin caché de Windows, para comprobar lo que de verdad quedó en el disco.
        string HashFile(string path)
        {
            string r = HashWith(path, true);
            return r ?? HashWith(path, false);
        }

        string HashWith(string path, bool unbuffered)
        {
            uint flags = N.SEQUENTIAL | (unbuffered ? N.NO_BUFFERING : 0);
            using (var h = N.CreateFile(N.LP(path), N.GENERIC_READ, 1, IntPtr.Zero, N.OPEN_EXISTING, flags, IntPtr.Zero))
            {
                if (h.IsInvalid) return null;
                using (var hash = SHA256.Create())
                {
                    while (true)
                    {
                        int read;
                        lastTick = Environment.TickCount;
                        if (!N.ReadFilePtr(h, aligned, BufSize, out read, IntPtr.Zero)) return null;
                        if (read == 0) break;
                        Marshal.Copy(aligned, buffer, 0, read);
                        hash.TransformBlock(buffer, 0, read, null, 0);
                    }
                    hash.TransformFinalBlock(new byte[0], 0, 0);
                    return BitConverter.ToString(hash.Hash).Replace("-", "");
                }
            }
        }
    }

    public static class Exec
    {
        public static int Run(string file, string args, int timeoutMs, out string output, string workingDir = null)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                if (workingDir != null) psi.WorkingDirectory = workingDir;
                using (var p = Process.Start(psi))
                {
                    string got = "";
                    var a = new Thread(() => { try { got = p.StandardOutput.ReadToEnd(); } catch { } }) { IsBackground = true };
                    var b = new Thread(() => { try { p.StandardError.ReadToEnd(); } catch { } }) { IsBackground = true };
                    a.Start(); b.Start();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return -2;
                    }
                    a.Join(1500);
                    output = got;
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }
    }

    public static class Admin
    {
        public static bool Is()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }
}
