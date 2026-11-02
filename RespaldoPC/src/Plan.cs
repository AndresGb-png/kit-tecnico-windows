using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using Microsoft.Win32;

namespace RespaldoPC
{
    public class SystemTask
    {
        public string Key, Label, Hint;
        public bool Selected = true;
        public St Result = St.None;
        public string Detail = "";
        public object Node;
    }

    public enum St { None, Ok, Warn, Fail }

    public class SourceInfo
    {
        public string Root, Label;
        public bool IsCurrentWindows;
        public override string ToString() { return Label; }
    }

    public static class Backup
    {
        const string G_System = "Configuración del equipo";

        static readonly HashSet<string> SkipUsers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Default", "Default User", "All Users", "defaultuser0", "WDAGUtilityAccount", "desktop.ini", "Todos los usuarios", "Usuario predeterminado", "Administrador predeterminado" };
        static readonly HashSet<string> SkipRoot = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Windows", "Windows.old", "Program Files", "Program Files (x86)", "ProgramData", "Users", "Usuarios", "Documents and Settings", "PerfLogs", "Recovery", "MSOCache",
              "$Recycle.Bin", "$WinREAgent", "$Windows.~BT", "$Windows.~WS", "$SysReset", "System Volume Information", "Config.Msi", "Boot", "EFI", "OneDriveTemp", "Intel", "AMD", "NVIDIA", "Drivers", "inetpub" };
        static readonly string[] DataExt = { ".pst", ".mdb", ".accdb", ".dbf", ".fdb", ".gdb", ".qbw", ".qbb", ".mdf", ".ldf", ".sdf", ".kdbx" };

        public static readonly Dictionary<string, string> Friendly = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Desktop", "Escritorio" }, { "Documents", "Documentos" }, { "Downloads", "Descargas" }, { "Pictures", "Imágenes" },
            { "Music", "Música" }, { "Videos", "Vídeos" }, { "Favorites", "Favoritos de Windows" }, { "Contacts", "Contactos" }, { "Saved Games", "Partidas guardadas" }
        };

        public static List<SourceInfo> Sources()
        {
            var list = new List<SourceInfo>();
            string sys = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                    string root = d.RootDirectory.FullName;
                    if (!N.IsDir(root + "Users")) continue;
                    bool current = root.Equals(sys, StringComparison.OrdinalIgnoreCase);
                    list.Add(new SourceInfo
                    {
                        Root = root, IsCurrentWindows = current,
                        Label = current ? "Este Windows (" + root.TrimEnd('\\') + ")"
                                        : "Otro disco conectado (" + root.TrimEnd('\\') + (d.VolumeLabel.Length > 0 ? " " + d.VolumeLabel : "") + ")"
                    });
                }
                catch { }
            }
            return list.OrderByDescending(s => s.IsCurrentWindows).ToList();
        }

        public static List<CopyTask> PlanFiles(SourceInfo source)
        {
            var tasks = new List<CopyTask>();
            string users = source.Root.TrimEnd('\\') + "\\Users";
            var dirs = new List<string>();
            Fs.Walk1(users, dirs);
            foreach (var profile in dirs.OrderBy(x => x))
            {
                string name = Path.GetFileName(profile);
                if (SkipUsers.Contains(name)) continue;
                PlanProfile(tasks, profile, name);
            }
            PlanOutside(tasks, source);
            return tasks;
        }

        static void PlanProfile(List<CopyTask> tasks, string profile, string user)
        {
            string group = "Usuario " + user, relRoot = "Perfiles\\" + user;
            var subdirs = new List<string>();
            var files = new List<string>();
            Fs.Walk1(profile, subdirs, files);
            foreach (var dir in subdirs.OrderBy(d => Friendly.ContainsKey(Path.GetFileName(d)) ? 0 : 1).ThenBy(d => d))
            {
                string name = Path.GetFileName(dir);
                if (name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;
                string label;
                Friendly.TryGetValue(name, out label);
                bool cloud = name.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase);
                tasks.Add(new CopyTask
                {
                    Group = group, Label = label ?? name, Src = dir, Dst = relRoot + "\\" + name, Rel = relRoot + "\\" + name,
                    Hint = cloud ? "Lo que solo está en la nube no se descarga; queda anotado en el informe." : null
                });
            }
            foreach (var f in files)
            {
                string n = Path.GetFileName(f).ToLowerInvariant();
                if (n.StartsWith("ntuser") || n == "desktop.ini" || n.EndsWith(".dat") || n.EndsWith(".log1") || n.EndsWith(".log2")) continue;
                tasks.Add(new CopyTask { Group = group, Label = Path.GetFileName(f), Src = f, IsFile = true, Dst = relRoot + "\\" + Path.GetFileName(f), Rel = relRoot + "\\" + Path.GetFileName(f) });
            }

            // De AppData solo lo que el cliente va a echar en falta; el resto es caché o se regenera.
            string local = profile + "\\AppData\\Local", roaming = profile + "\\AppData\\Roaming";
            foreach (var b in new[] { new[] { "Google Chrome", @"Google\Chrome\User Data" }, new[] { "Microsoft Edge", @"Microsoft\Edge\User Data" }, new[] { "Brave", @"BraveSoftware\Brave-Browser\User Data" } })
            {
                string data = local + "\\" + b[1];
                if (!N.IsDir(data)) continue;
                var profiles = new List<string>();
                Fs.Walk1(data, profiles);
                foreach (var p in profiles)
                {
                    string book = p + "\\Bookmarks";
                    if (!N.Exists(book)) continue;
                    string pn = Path.GetFileName(p);
                    tasks.Add(new CopyTask
                    {
                        Group = group, Label = "Favoritos de " + b[0] + (pn == "Default" ? "" : " (" + pn + ")"), Src = book, IsFile = true,
                        Dst = relRoot + "\\AppData\\Local\\" + b[1] + "\\" + pn + "\\Bookmarks", Rel = relRoot + "\\AppData\\Local\\" + b[1] + "\\" + pn + "\\Bookmarks"
                    });
                }
            }
            AddIfDir(tasks, group, relRoot, profile, @"AppData\Roaming\Mozilla\Firefox\Profiles", "Favoritos de Firefox", "bookmarkbackups");
            AddIfDir(tasks, group, relRoot, profile, @"AppData\Roaming\Microsoft\Signatures", "Firmas de Outlook", null);
            AddIfDir(tasks, group, relRoot, profile, @"AppData\Roaming\Thunderbird", "Correo de Thunderbird", null);
            AddIfDir(tasks, group, relRoot, profile, @"AppData\Local\Packages\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe\LocalState", "Notas adhesivas", null);
            string outlook = local + "\\Microsoft\\Outlook";
            if (N.IsDir(outlook))
            {
                var outFiles = new List<string>();
                Fs.Walk1(outlook, new List<string>(), outFiles);
                foreach (var pst in outFiles.Where(x => x.EndsWith(".pst", StringComparison.OrdinalIgnoreCase)))
                {
                    string rel = relRoot + "\\AppData\\Local\\Microsoft\\Outlook\\" + Path.GetFileName(pst);
                    tasks.Add(new CopyTask { Group = group, Label = "Correo de Outlook: " + Path.GetFileName(pst), Src = pst, IsFile = true, Dst = rel, Rel = rel });
                }
            }
        }

        static void AddIfDir(List<CopyTask> tasks, string group, string relRoot, string profile, string sub, string label, string onlyChild)
        {
            string full = profile + "\\" + sub;
            if (!N.IsDir(full)) return;
            if (onlyChild == null)
            {
                tasks.Add(new CopyTask { Group = group, Label = label, Src = full, Dst = relRoot + "\\" + sub, Rel = relRoot + "\\" + sub });
                return;
            }
            var kids = new List<string>();
            Fs.Walk1(full, kids);
            foreach (var k in kids)
                if (N.IsDir(k + "\\" + onlyChild))
                {
                    string rel = relRoot + "\\" + sub + "\\" + Path.GetFileName(k) + "\\" + onlyChild;
                    tasks.Add(new CopyTask { Group = group, Label = label, Src = k + "\\" + onlyChild, Dst = rel, Rel = rel });
                }
        }

        // Lo que más se olvida: carpetas en la raíz del disco y bases de datos dentro de programas.
        static void PlanOutside(List<CopyTask> tasks, SourceInfo source)
        {
            const string group = "Fuera de los perfiles";
            string root = source.Root.TrimEnd('\\'), letter = Letter(source.Root);
            var dirs = new List<string>();
            var files = new List<string>();
            Fs.Walk1(root + "\\", dirs, files);
            foreach (var d in dirs.OrderBy(x => x))
            {
                string n = Path.GetFileName(d);
                if (SkipRoot.Contains(n) || n.StartsWith("$")) continue;
                string rel = "Otros\\" + letter + "\\" + n;
                tasks.Add(new CopyTask { Group = group, Label = root + "\\" + n, Src = d, Dst = rel, Rel = rel, Hint = "Carpeta en la raíz del disco" });
            }
            foreach (var pd in new[] { "ProgramData", "Program Files", "Program Files (x86)" })
            {
                string dir = root + "\\" + pd;
                if (!N.IsDir(dir)) continue;
                var found = new List<FileEntry>();
                Fs.Walk(dir, f =>
                {
                    if (f.Size >= 1048576 && DataExt.Contains(Path.GetExtension(f.Name).ToLowerInvariant())) found.Add(f);
                }, null, null, () => found.Count > 200);
                foreach (var f in found)
                {
                    string inner = f.Full.Substring(root.Length).TrimStart('\\');
                    string rel = "Otros\\" + letter + "\\" + inner;
                    tasks.Add(new CopyTask { Group = group, Label = "Posible base de datos: " + f.Full, Src = f.Full, IsFile = true, Dst = rel, Rel = rel, Hint = "Dentro de una carpeta de programa" });
                }
            }
        }

        public static string Letter(string root)
        {
            return root.Length >= 2 && root[1] == ':' ? root.Substring(0, 1).ToUpperInvariant() : "RAIZ";
        }

        public static void Measure(CopyTask t, Func<bool> stop)
        {
            long size = 0; int count = 0;
            if (t.IsFile)
            {
                long s, w; uint a;
                if (N.Stat(t.Src, out s, out w, out a)) { size = s; count = 1; }
            }
            else
                Fs.Walk(t.Src, f => { if (!f.CloudOnly) { size += f.Size; count++; } t.Size = size; t.Count = count; }, null, null, stop);
            t.Size = size; t.Count = count;
        }

        public static List<SystemTask> PlanSystem(SourceInfo source, bool admin)
        {
            var list = new List<SystemTask>();
            list.Add(new SystemTask { Key = "programas", Label = "Lista de programas instalados", Hint = "Para saber qué reinstalar" });
            if (!source.IsCurrentWindows)
            {
                list[0].Hint = "Aproximada: de otro disco solo se pueden leer los nombres de las carpetas";
                return list;
            }
            list.Add(new SystemTask { Key = "wifi", Label = "Redes Wi-Fi con sus claves", Hint = "Las claves quedan legibles dentro del respaldo" });
            list.Add(new SystemTask { Key = "red", Label = "Datos de red (IP, unidades de red, archivo hosts)", Hint = "Para IP fijas y carpetas compartidas" });
            list.Add(new SystemTask { Key = "impresoras", Label = "Impresoras", Hint = admin ? "Lista y copia de configuración" : "Solo la lista: la copia requiere administrador" });
            list.Add(new SystemTask { Key = "drivers", Label = "Controladores (drivers)", Hint = admin ? "Pueden ser varios GB. Sirven para este mismo modelo" : "Requiere administrador", Selected = admin });
            return list;
        }

        public static void RunSystem(SystemTask t, string backupRoot, SourceInfo source)
        {
            string dir = Path.Combine(backupRoot, "Sistema");
            Directory.CreateDirectory(dir);
            try
            {
                switch (t.Key)
                {
                    case "programas": Programs(t, dir, source); break;
                    case "wifi": Wifi(t, dir); break;
                    case "red": Net(t, dir); break;
                    case "impresoras": Printers(t, dir); break;
                    case "drivers": Drivers(t, dir); break;
                }
            }
            catch (Exception ex)
            {
                t.Result = St.Fail;
                t.Detail = "No se pudo completar: " + ex.Message;
            }
        }

        static void Programs(SystemTask t, string dir, SourceInfo source)
        {
            var rows = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (source.IsCurrentWindows)
            {
                foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                    foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                        using (var root = hive.OpenSubKey(path))
                        {
                            if (root == null) continue;
                            foreach (var name in root.GetSubKeyNames())
                                using (var k = root.OpenSubKey(name))
                                {
                                    if (k == null) continue;
                                    string shown = k.GetValue("DisplayName") as string;
                                    if (string.IsNullOrEmpty(shown) || k.GetValue("SystemComponent") as int? == 1 || k.GetValue("ParentKeyName") != null) continue;
                                    rows[shown] = shown + "\t" + k.GetValue("DisplayVersion") + "\t" + k.GetValue("Publisher");
                                }
                        }
                string output;
                Exec.Run("winget", "export -o \"" + Path.Combine(dir, "winget.json") + "\" --accept-source-agreements --disable-interactivity", 120000, out output);
            }
            else
            {
                foreach (var pf in new[] { "Program Files", "Program Files (x86)" })
                {
                    var dirs = new List<string>();
                    string p = source.Root.TrimEnd('\\') + "\\" + pf;
                    if (N.IsDir(p)) Fs.Walk1(p, dirs);
                    foreach (var d in dirs)
                    {
                        string n = Path.GetFileName(d);
                        if (!n.StartsWith("Common Files", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("Windows", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("Microsoft.NET", StringComparison.OrdinalIgnoreCase) && !n.Equals("Internet Explorer", StringComparison.OrdinalIgnoreCase))
                            rows[n] = n + "\t\t";
                    }
                }
            }
            File.WriteAllLines(Path.Combine(dir, "programas.tsv"), rows.Values, Encoding.UTF8);
            t.Result = rows.Count > 0 ? St.Ok : St.Warn;
            t.Detail = rows.Count + " programas anotados.";
        }

        static void Wifi(SystemTask t, string dir)
        {
            string sub = Path.Combine(dir, "WiFi");
            Directory.CreateDirectory(sub);
            string output;
            Exec.Run("netsh", "wlan export profile key=clear folder=\"" + sub + "\"", 30000, out output);
            int n = Directory.GetFiles(sub, "*.xml").Length;
            if (n == 0) { t.Result = St.Warn; t.Detail = "No hay redes guardadas o el servicio de Wi-Fi no está activo."; return; }
            bool keys = Directory.GetFiles(sub, "*.xml").Any(f => File.ReadAllText(f).Contains("keyMaterial"));
            t.Result = keys || Admin.Is() ? St.Ok : St.Warn;
            t.Detail = n + " redes guardadas." + (Admin.Is() ? " Las claves quedan legibles: guarda este respaldo en un lugar seguro." : " Sin administrador no se copiaron las claves.");
        }

        static void Net(SystemTask t, string dir)
        {
            var sb = new StringBuilder();
            string output;
            Exec.Run("ipconfig", "/all", 15000, out output);
            sb.AppendLine(output);
            sb.AppendLine("--- Unidades de red ---");
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name,ProviderName FROM Win32_MappedLogicalDisk"))
                    foreach (ManagementObject m in s.Get())
                        sb.AppendLine(m["Name"] + " = " + m["ProviderName"]);
            }
            catch { sb.AppendLine("No se pudieron leer."); }
            File.WriteAllText(Path.Combine(dir, "red.txt"), sb.ToString(), Encoding.UTF8);
            string hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            if (File.Exists(hosts)) File.Copy(hosts, Path.Combine(dir, "hosts.txt"), true);
            t.Result = St.Ok;
            t.Detail = "Anotados en Sistema\\red.txt.";
        }

        static void Printers(SystemTask t, string dir)
        {
            var names = new List<string>();
            using (var s = new ManagementObjectSearcher("SELECT Name,PortName,DriverName FROM Win32_Printer"))
                foreach (ManagementObject m in s.Get())
                    names.Add(m["Name"] + "\t" + m["PortName"] + "\t" + m["DriverName"]);
            File.WriteAllLines(Path.Combine(dir, "impresoras.tsv"), names, Encoding.UTF8);
            string brm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\tools\PrintBrm.exe");
            bool full = false;
            string why = !Admin.Is() ? "requiere administrador" : !File.Exists(brm) ? "esta edición de Windows no incluye PrintBrm" : "";
            if (why.Length == 0)
            {
                string output, file = Path.Combine(dir, "impresoras.printerExport");
                // PrintBrm guarda las comillas como parte de la ruta y devuelve 0 aunque falle:
                // se le pasa solo el nombre desde su carpeta y se comprueba el archivo creado.
                int code = Exec.Run(brm, "-B -F impresoras.printerExport", 300000, out output, dir);
                File.WriteAllText(Path.Combine(dir, "impresoras.log"), "Código: " + code + "\r\n" + output, Encoding.UTF8);
                full = code == 0 && File.Exists(file);
                if (!full)
                {
                    var err = System.Text.RegularExpressions.Regex.Match(output ?? "", @"0x[0-9a-fA-F]{8}");
                    why = "PrintBrm informó " + (err.Success ? "el error " + err.Value : "el código " + code);
                }
            }
            t.Result = full ? St.Ok : St.Warn;
            t.Detail = names.Count + " impresoras anotadas." + (full ? " Configuración copiada." : " La configuración no se copió (" + why + "); habrá que instalarlas a mano.");
        }

        static void Drivers(SystemTask t, string dir)
        {
            if (!Admin.Is()) { t.Result = St.Fail; t.Detail = "Copiar controladores requiere administrador."; return; }
            string sub = Path.Combine(dir, "Drivers");
            Directory.CreateDirectory(sub);
            string output;
            int code = Exec.Run("dism", "/online /export-driver /destination:\"" + sub + "\"", 1200000, out output);
            int n = Directory.GetFiles(sub, "*.inf", SearchOption.AllDirectories).Length;
            if (code == 0 && n > 0) { t.Result = St.Ok; t.Detail = n + " controladores copiados."; }
            else if (n > 0) { t.Result = St.Warn; t.Detail = "Se copiaron " + n + " controladores, pero el proceso no terminó bien."; }
            else { t.Result = St.Fail; t.Detail = "No se pudieron copiar (código " + code + "). Descárgalos de la web del fabricante."; }
        }

        public static void WriteInfo(string backupRoot, SourceInfo source, string state, long fails)
        {
            string model = "";
            if (source.IsCurrentWindows)
                try
                {
                    using (var s = new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem"))
                        foreach (ManagementObject m in s.Get()) model = (m["Manufacturer"] + " " + m["Model"]).Trim();
                }
                catch { }
            var lines = new List<string>
            {
                "equipo=" + (source.IsCurrentWindows ? Environment.MachineName : "Disco " + source.Root.TrimEnd('\\')),
                "modelo=" + model,
                "fecha=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                "origen=" + source.Root,
                "estado=" + state,
                "fallidos=" + fails
            };
            File.WriteAllLines(Path.Combine(backupRoot, Info.FileName), lines, Encoding.UTF8);
        }
    }

    public static class Info
    {
        public const string FileName = "respaldo.ini";

        public static Dictionary<string, string> Read(string backupRoot)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string p = Path.Combine(backupRoot, FileName);
            if (!File.Exists(p)) return null;
            foreach (var l in File.ReadAllLines(p, Encoding.UTF8))
            {
                int i = l.IndexOf('=');
                if (i > 0) d[l.Substring(0, i)] = l.Substring(i + 1);
            }
            return d;
        }

        public static string Get(Dictionary<string, string> d, string key)
        {
            string v;
            return d != null && d.TryGetValue(key, out v) ? v : "";
        }
    }

    public class RestoreUser
    {
        public string BackupName, Target;
    }

    public static class Restore
    {
        static readonly Dictionary<string, Func<string>> Known = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "Desktop", () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
            { "Documents", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
            { "Pictures", () => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) },
            { "Music", () => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) },
            { "Videos", () => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) }
        };

        public static List<string> BackupUsers(string backup)
        {
            var dirs = new List<string>();
            string p = Path.Combine(backup, "Perfiles");
            if (N.IsDir(p)) Fs.Walk1(p, dirs);
            return dirs.Select(Path.GetFileName).OrderBy(x => x).ToList();
        }

        public static List<string> TargetUsers(string systemRoot)
        {
            var dirs = new List<string>();
            string p = systemRoot.TrimEnd('\\') + "\\Users";
            if (N.IsDir(p)) Fs.Walk1(p, dirs);
            var skip = new[] { "Default", "Default User", "All Users", "defaultuser0", "WDAGUtilityAccount", "Todos los usuarios", "Usuario predeterminado" };
            return dirs.Where(d => !skip.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)).OrderBy(x => x).ToList();
        }

        // Si el destino es el usuario actual, respeta carpetas redirigidas (por ejemplo a OneDrive).
        static string TargetFolder(string userDir, string name)
        {
            string current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Func<string> f;
            if (userDir.Equals(current, StringComparison.OrdinalIgnoreCase) && Known.TryGetValue(name, out f))
            {
                string p = f();
                if (!string.IsNullOrEmpty(p)) return p;
            }
            return userDir + "\\" + name;
        }

        public static List<CopyTask> PlanUser(string backup, string user, string targetDir)
        {
            var tasks = new List<CopyTask>();
            string group = "Usuario " + user, baseRel = "Perfiles\\" + user, profile = Path.Combine(backup, "Perfiles", user);
            var dirs = new List<string>();
            var files = new List<string>();
            Fs.Walk1(profile, dirs, files);
            foreach (var d in dirs.OrderBy(x => x))
            {
                string name = Path.GetFileName(d);
                if (name.Equals("AppData", StringComparison.OrdinalIgnoreCase)) continue;
                bool cloud = name.StartsWith("OneDrive", StringComparison.OrdinalIgnoreCase);
                string shown;
                if (!Backup.Friendly.TryGetValue(name, out shown)) shown = name;
                tasks.Add(new CopyTask
                {
                    Group = group, Label = shown, Src = d, Dst = TargetFolder(targetDir, name), Rel = baseRel + "\\" + name, Policy = Policy.KeepBoth,
                    Selected = !cloud, Hint = cloud ? "OneDrive lo descargará solo al iniciar sesión" : null
                });
            }
            foreach (var f in files)
                tasks.Add(new CopyTask { Group = group, Label = Path.GetFileName(f), Src = f, IsFile = true, Dst = targetDir + "\\" + Path.GetFileName(f), Rel = baseRel + "\\" + Path.GetFileName(f), Policy = Policy.KeepBoth });

            string appdata = Path.Combine(profile, "AppData");
            if (!N.IsDir(appdata)) return tasks;
            var all = new List<string>();
            Fs.Walk(appdata, e => all.Add(e.Full), null, null, null);
            foreach (var book in all.Where(x => Path.GetFileName(x).Equals("Bookmarks", StringComparison.OrdinalIgnoreCase)))
            {
                string inner = book.Substring(profile.Length).TrimStart('\\');
                string browser = inner.Contains("Chrome") ? "Chrome" : inner.Contains("Edge") ? "Edge" : inner.Contains("Brave") ? "Brave" : "navegador";
                tasks.Add(new CopyTask { Group = group, Label = "Favoritos de " + browser, Src = book, IsFile = true, Dst = targetDir + "\\" + inner, Rel = baseRel + "\\" + inner, Policy = Policy.ReplaceKeepOld, Hint = "Cierra " + browser + " antes de restaurar" });
            }
            AddMirror(tasks, group, baseRel, profile, targetDir, @"AppData\Roaming\Microsoft\Signatures", "Firmas de Outlook", Policy.KeepBoth);
            AddMirror(tasks, group, baseRel, profile, targetDir, @"AppData\Roaming\Thunderbird", "Correo de Thunderbird", Policy.KeepBoth);
            AddMirror(tasks, group, baseRel, profile, targetDir, @"AppData\Local\Packages\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe\LocalState", "Notas adhesivas", Policy.ReplaceKeepOld);
            foreach (var pst in all.Where(x => x.EndsWith(".pst", StringComparison.OrdinalIgnoreCase)))
            {
                string inner = pst.Substring(profile.Length).TrimStart('\\');
                tasks.Add(new CopyTask { Group = group, Label = "Correo de Outlook: " + Path.GetFileName(pst), Src = pst, IsFile = true, Dst = targetDir + "\\" + inner, Rel = baseRel + "\\" + inner, Policy = Policy.KeepBoth });
            }
            // Firefox crea carpetas de perfil con nombre aleatorio: no hay ruta fija donde devolverlo.
            string ff = Path.Combine(appdata, @"Roaming\Mozilla\Firefox\Profiles");
            if (N.IsDir(ff))
                tasks.Add(new CopyTask { Group = group, Label = "Favoritos de Firefox", Src = ff, Dst = TargetFolder(targetDir, "Documents") + "\\Respaldo de Firefox", Rel = baseRel + "\\AppData\\Roaming\\Mozilla\\Firefox\\Profiles", Policy = Policy.KeepBoth, Hint = "Se deja en Documentos para importarlo desde Firefox" });
            return tasks;
        }

        static void AddMirror(List<CopyTask> tasks, string group, string baseRel, string profile, string targetDir, string sub, string label, Policy policy)
        {
            string src = profile + "\\" + sub;
            if (N.IsDir(src))
                tasks.Add(new CopyTask { Group = group, Label = label, Src = src, Dst = targetDir + "\\" + sub, Rel = baseRel + "\\" + sub, Policy = policy });
        }

        public static List<CopyTask> PlanOthers(string backup, string systemRoot, bool useDriveLetters = true)
        {
            var tasks = new List<CopyTask>();
            string others = Path.Combine(backup, "Otros");
            if (!N.IsDir(others)) return tasks;
            var letters = new List<string>();
            Fs.Walk1(others, letters);
            foreach (var l in letters)
            {
                string letter = Path.GetFileName(l);
                string driveRoot = useDriveLetters && letter.Length == 1 && N.IsDir(letter + @":\") ? letter + @":\" : systemRoot;
                var dirs = new List<string>();
                var files = new List<string>();
                Fs.Walk1(l, dirs, files);
                foreach (var d in dirs)
                {
                    string n = Path.GetFileName(d);
                    bool inProgram = n.StartsWith("Program", StringComparison.OrdinalIgnoreCase);
                    tasks.Add(new CopyTask
                    {
                        Group = "Fuera de los perfiles", Label = driveRoot.TrimEnd('\\') + "\\" + n, Src = d, Dst = driveRoot.TrimEnd('\\') + "\\" + n,
                        Rel = "Otros\\" + letter + "\\" + n, Policy = Policy.KeepBoth, Selected = !inProgram,
                        Hint = inProgram ? "Instala primero el programa; luego elige si devolver sus datos" : null
                    });
                }
            }
            return tasks;
        }

        public static List<SystemTask> PlanSystem(string backup, string model)
        {
            var list = new List<SystemTask>();
            string sys = Path.Combine(backup, "Sistema");
            if (Directory.Exists(Path.Combine(sys, "WiFi")) && Directory.GetFiles(Path.Combine(sys, "WiFi"), "*.xml").Length > 0)
                list.Add(new SystemTask { Key = "wifi", Label = "Redes Wi-Fi", Hint = Directory.GetFiles(Path.Combine(sys, "WiFi"), "*.xml").Length + " redes" });
            if (Directory.Exists(Path.Combine(sys, "Drivers")) && Directory.GetFiles(Path.Combine(sys, "Drivers"), "*.inf", SearchOption.AllDirectories).Length > 0)
            {
                bool same = false;
                string here = CurrentModel();
                same = here.Length > 0 && here.Equals(model, StringComparison.OrdinalIgnoreCase);
                list.Add(new SystemTask { Key = "drivers", Label = "Controladores", Selected = same, Hint = same ? "Mismo modelo de equipo" : "El respaldo es de otro modelo: mejor no instalarlos" });
            }
            if (File.Exists(Path.Combine(sys, "impresoras.printerExport")))
                list.Add(new SystemTask { Key = "impresoras", Label = "Impresoras", Selected = false, Hint = "Opcional: también instala sus controladores antiguos" });
            if (File.Exists(Path.Combine(sys, "programas.tsv")))
                list.Add(new SystemTask { Key = "programas", Label = "Lista de programas para reinstalar", Hint = "Se deja en el Escritorio" });
            return list;
        }

        static string CurrentModel()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem"))
                    foreach (ManagementObject m in s.Get()) return (m["Manufacturer"] + " " + m["Model"]).Trim();
            }
            catch { }
            return "";
        }

        public static void RunSystem(SystemTask t, string backup)
        {
            string sys = Path.Combine(backup, "Sistema");
            string output;
            try
            {
                switch (t.Key)
                {
                    case "wifi":
                        var files = Directory.GetFiles(Path.Combine(sys, "WiFi"), "*.xml");
                        int ok = 0;
                        foreach (var f in files)
                            if (Exec.Run("netsh", "wlan add profile filename=\"" + f + "\" user=all", 20000, out output) == 0) ok++;
                        t.Result = ok == files.Length ? St.Ok : ok > 0 ? St.Warn : St.Fail;
                        t.Detail = ok + " de " + files.Length + " redes añadidas." + (ok == 0 ? " Comprueba que el adaptador Wi-Fi tenga controlador y reintenta." : "");
                        break;
                    case "drivers":
                        int code = Exec.Run("pnputil", "/add-driver \"" + Path.Combine(sys, "Drivers") + "\\*.inf\" /subdirs /install", 1200000, out output);
                        t.Result = code == 0 || code == 3010 ? St.Ok : St.Warn;
                        t.Detail = code == 3010 ? "Instalados. Reinicia el equipo." : code == 0 ? "Instalados." : "Algunos no se instalaron (código " + code + "). Normalmente es porque el equipo ya tiene uno igual o más nuevo.";
                        break;
                    case "impresoras":
                        string brm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\tools\PrintBrm.exe");
                        int pc = Exec.Run(brm, "-R -F impresoras.printerExport", 300000, out output, sys);
                        var printErr = System.Text.RegularExpressions.Regex.Match(output ?? "", @"0x[0-9a-fA-F]{8}");
                        bool printOk = pc == 0 && !printErr.Success;
                        t.Result = printOk ? St.Ok : St.Fail;
                        t.Detail = printOk ? "Restauradas." : "No se pudieron restaurar (" + (printErr.Success ? "error " + printErr.Value : "código " + pc) + "). Instálalas a mano; la lista está en el informe.";
                        break;
                    case "programas":
                        string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Programas que tenía el equipo.html");
                        File.WriteAllText(target, Report.ProgramsPage(backup), new UTF8Encoding(true));
                        t.Result = St.Ok;
                        t.Detail = "Lista guardada en el Escritorio.";
                        break;
                }
            }
            catch (Exception ex)
            {
                t.Result = St.Fail;
                t.Detail = "No se pudo completar: " + ex.Message;
            }
        }
    }

    public static class Report
    {
        static string E(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        const string Css = "body{margin:0;background:#eef1f3;color:#1e2a32;font:15px/1.5 'Segoe UI',system-ui,sans-serif}.band{color:#fff;padding:24px}.band h1{font-size:25px;line-height:1.2;margin:0 0 6px;font-weight:600}.band p{margin:0;max-width:72ch}" +
            "main{max-width:880px;margin:0 auto;padding:8px 16px 40px}h2{font-size:18px;font-weight:600;margin:26px 0 8px}table{border-collapse:collapse;width:100%;background:#fff;font-size:14px}td,th{padding:6px 10px;border-bottom:1px solid #d5dbe0;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{font-weight:600}td.n{text-align:right;white-space:nowrap}.w{overflow-x:auto}li{margin:4px 0;max-width:72ch}" +
            "@media print{body{background:#fff}.band{-webkit-print-color-adjust:exact;print-color-adjust:exact}}";

        public static string Page(string title, string verdict, string detail, string color, List<CopyTask> tasks, CopyEngine engine, List<SystemTask> system, List<string> pending, string extraHtml)
        {
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=es><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>" + E(title) + "</title><style>" + Css + "</style>");
            sb.Append("<div class=band style='background:" + color + "'><h1>" + E(verdict) + "</h1><p>" + E(detail) + "</p><p>" + E(title) + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".</p></div><main>");

            if (pending != null && pending.Count > 0)
            {
                sb.Append("<h2>Qué falta hacer a mano</h2><ul>");
                foreach (var p in pending) sb.Append("<li>" + E(p) + "</li>");
                sb.Append("</ul>");
            }
            if (engine.Failed.Count > 0)
            {
                sb.Append("<h2>Archivos que no se copiaron (" + engine.Failed.Count + ")</h2><div class=w><table><tr><th>Archivo</th><th>Motivo</th></tr>");
                foreach (var f in engine.Failed) sb.Append("<tr><td>" + E(f.Path) + "</td><td>" + E(f.Reason) + "</td></tr>");
                sb.Append("</table></div>");
            }
            sb.Append("<h2>Lo que se copió</h2><div class=w><table><tr><th>Parte</th><th>Copiados</th><th>Ya estaban</th><th>Fallidos</th><th>Solo en la nube</th></tr>");
            foreach (var t in tasks.Where(x => x.Selected))
            {
                TaskStat s;
                engine.Stats.TryGetValue(t, out s);
                if (s == null) s = new TaskStat();
                sb.Append("<tr><td>" + E(t.Group + ": " + t.Label) + "</td><td class=n>" + s.Copied + "</td><td class=n>" + s.Skipped + "</td><td class=n>" + s.Failed + "</td><td class=n>" + s.Cloud + "</td></tr>");
            }
            sb.Append("</table></div>");

            if (system != null && system.Any(x => x.Selected))
            {
                sb.Append("<h2>Configuración del equipo</h2><div class=w><table>");
                foreach (var t in system.Where(x => x.Selected))
                    sb.Append("<tr><td>" + E(t.Label) + "</td><td>" + E(t.Detail.Length > 0 ? t.Detail : "No se ejecutó.") + "</td></tr>");
                sb.Append("</table></div>");
            }
            var skipped = tasks.Where(x => !x.Selected).ToList();
            if (skipped.Count > 0)
            {
                sb.Append("<h2>Lo que elegiste no copiar</h2><ul>");
                foreach (var t in skipped) sb.Append("<li>" + E(t.Group + ": " + t.Label) + " (" + Fs.Bytes(t.Size) + ")</li>");
                sb.Append("</ul>");
            }
            if (engine.Cloud.Count > 0)
            {
                sb.Append("<h2>Archivos que solo están en la nube (" + engine.Cloud.Count + ")</h2><p>Siguen en la cuenta del cliente y no se descargaron.</p><div class=w><table>");
                foreach (var f in engine.Cloud.Take(500)) sb.Append("<tr><td>" + E(f.Path) + "</td></tr>");
                sb.Append("</table></div>");
            }
            sb.Append(extraHtml ?? "");
            sb.Append(Comun.Credits.HtmlFooter(Program.Info));
            sb.Append("</main></html>");
            return sb.ToString();
        }

        public static string ProgramsTable(string backup)
        {
            string p = Path.Combine(backup, "Sistema", "programas.tsv");
            if (!File.Exists(p)) return "";
            var sb = new StringBuilder("<h2>Programas que tenía el equipo</h2><div class=w><table><tr><th>Programa</th><th>Versión</th><th>Fabricante</th></tr>");
            foreach (var l in File.ReadAllLines(p, Encoding.UTF8))
            {
                var c = l.Split('\t');
                sb.Append("<tr><td>" + E(c[0]) + "</td><td>" + E(c.Length > 1 ? c[1] : "") + "</td><td>" + E(c.Length > 2 ? c[2] : "") + "</td></tr>");
            }
            sb.Append("</table></div>");
            return sb.ToString();
        }

        public static string ProgramsPage(string backup)
        {
            return "<!doctype html><html lang=es><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Programas del equipo</title><style>" + Css + "</style><main>" + ProgramsTable(backup) + "</main></html>";
        }
    }
}
