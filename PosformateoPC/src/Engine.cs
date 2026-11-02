using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace PosformateoPC
{
    public enum Kind { Winget, Local }

    public class App
    {
        public string Id, Name, Group, Hint;
        public Kind Kind = Kind.Winget;
        public bool Selected;
        public object Node;
    }

    public static class Catalog
    {
        const string Base = "Componentes básicos", Web = "Navegadores", Office = "Oficina y PDF", Talk = "Comunicación", Media = "Multimedia y diseño", Tech = "Herramientas técnicas", Cloud = "Nube", Games = "Juegos", Dev = "Desarrollo";

        // Validados uno por uno con --verificar-catalogo. El orden fija el orden de instalación.
        public static List<App> All()
        {
            return new List<App>
            {
                A(Base, "Microsoft.VCRedist.2015+.x64", "Visual C++ 2015+ (64 bits)", "Muchos programas lo necesitan"),
                A(Base, "Microsoft.VCRedist.2015+.x86", "Visual C++ 2015+ (32 bits)", "Muchos programas lo necesitan"),
                A(Base, "Microsoft.DotNet.DesktopRuntime.8", ".NET Desktop Runtime 8"),
                A(Base, "Microsoft.DirectX", "DirectX (componentes antiguos)", "Para juegos y programas gráficos"),
                A(Base, "Oracle.JavaRuntimeEnvironment", "Java 8"),
                A(Web, "Google.Chrome", "Google Chrome"),
                A(Web, "Mozilla.Firefox", "Mozilla Firefox"),
                A(Web, "Brave.Brave", "Brave"),
                A(Office, "TheDocumentFoundation.LibreOffice", "LibreOffice", "Gratis, abre archivos de Office"),
                A(Office, "ONLYOFFICE.DesktopEditors", "ONLYOFFICE"),
                A(Office, "Microsoft.Office", "Microsoft 365", "Requiere licencia del cliente"),
                A(Office, "Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader"),
                A(Office, "SumatraPDF.SumatraPDF", "SumatraPDF"),
                A(Office, "7zip.7zip", "7-Zip"),
                A(Office, "RARLab.WinRAR", "WinRAR", "Licencia de prueba"),
                A(Talk, "Zoom.Zoom", "Zoom"),
                A(Talk, "Microsoft.Teams", "Microsoft Teams"),
                A(Talk, "Telegram.TelegramDesktop", "Telegram"),
                A(Talk, "Discord.Discord", "Discord"),
                A(Media, "VideoLAN.VLC", "VLC"),
                A(Media, "GIMP.GIMP", "GIMP"),
                A(Media, "Inkscape.Inkscape", "Inkscape"),
                A(Media, "dotPDN.PaintDotNet", "paint.net"),
                A(Media, "BlenderFoundation.Blender", "Blender"),
                A(Media, "Audacity.Audacity", "Audacity"),
                A(Media, "HandBrake.HandBrake", "HandBrake"),
                A(Media, "OBSProject.OBSStudio", "OBS Studio"),
                A(Media, "ShareX.ShareX", "ShareX"),
                A(Cloud, "Google.GoogleDrive", "Google Drive"),
                A(Cloud, "Dropbox.Dropbox", "Dropbox"),
                A(Cloud, "Microsoft.OneDrive", "Microsoft OneDrive"),
                A(Tech, "AnyDesk.AnyDesk", "AnyDesk"),
                A(Tech, "TeamViewer.TeamViewer", "TeamViewer"),
                A(Tech, "Malwarebytes.Malwarebytes", "Malwarebytes"),
                A(Tech, "CrystalDewWorld.CrystalDiskInfo", "CrystalDiskInfo"),
                A(Tech, "CPUID.CPU-Z", "CPU-Z"),
                A(Tech, "REALiX.HWiNFO", "HWiNFO"),
                A(Tech, "Rufus.Rufus", "Rufus"),
                A(Tech, "voidtools.Everything", "Everything"),
                A(Tech, "WinSCP.WinSCP", "WinSCP"),
                A(Tech, "PuTTY.PuTTY", "PuTTY"),
                A(Tech, "Notepad++.Notepad++", "Notepad++"),
                A(Tech, "Microsoft.PowerToys", "PowerToys"),
                A(Dev, "Microsoft.VisualStudioCode", "Visual Studio Code"),
                A(Dev, "Git.Git", "Git"),
                A(Dev, "Python.Python.3.12", "Python 3.12"),
                A(Games, "Valve.Steam", "Steam"),
                A(Games, "EpicGames.EpicGamesLauncher", "Epic Games")
            };
        }

        static App A(string group, string id, string name, string hint = null)
        {
            return new App { Group = group, Id = id, Name = name, Hint = hint };
        }

        static readonly string[] Basic = { "Microsoft.VCRedist.2015+.x64", "Microsoft.VCRedist.2015+.x86", "Google.Chrome", "7zip.7zip", "VideoLAN.VLC", "Adobe.Acrobat.Reader.64-bit", "AnyDesk.AnyDesk" };

        public static readonly Dictionary<string, string[]> Profiles = new Dictionary<string, string[]>
        {
            { "Hogar", Basic },
            { "Oficina", Basic.Concat(new[] { "TheDocumentFoundation.LibreOffice", "Zoom.Zoom", "Microsoft.Teams" }).ToArray() },
            { "Diseño", Basic.Concat(new[] { "GIMP.GIMP", "Inkscape.Inkscape", "dotPDN.PaintDotNet", "BlenderFoundation.Blender", "ShareX.ShareX" }).ToArray() },
            { "Juegos", Basic.Concat(new[] { "Microsoft.DirectX", "Valve.Steam", "EpicGames.EpicGamesLauncher", "Discord.Discord" }).ToArray() },
            { "Técnico", new[] { "Google.Chrome", "7zip.7zip", "AnyDesk.AnyDesk", "CrystalDewWorld.CrystalDiskInfo", "CPUID.CPU-Z", "REALiX.HWiNFO", "Rufus.Rufus", "voidtools.Everything", "WinSCP.WinSCP", "PuTTY.PuTTY", "Notepad++.Notepad++" } }
        };
    }

    // Las listas propias se guardan junto al programa, para llevarlas en la USB.
    public static class SavedLists
    {
        static IEnumerable<string> Folders()
        {
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Perfiles");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PosformateoPC", "Perfiles");
        }

        public static Dictionary<string, List<KeyValuePair<string, string>>> Load()
        {
            var map = new Dictionary<string, List<KeyValuePair<string, string>>>();
            foreach (var dir in Folders())
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*.txt"))
                    {
                        var rows = new List<KeyValuePair<string, string>>();
                        foreach (var line in File.ReadAllLines(f, Encoding.UTF8))
                        {
                            var p = line.Split('|');
                            if (p[0].Trim().Length > 0) rows.Add(new KeyValuePair<string, string>(p[0].Trim(), p.Length > 1 ? p[1].Trim() : p[0].Trim()));
                        }
                        map[Path.GetFileNameWithoutExtension(f)] = rows;
                    }
                }
                catch { }
            return map;
        }

        public static string Save(string name, IEnumerable<App> apps)
        {
            string clean = name;
            foreach (var c in Path.GetInvalidFileNameChars()) clean = clean.Replace(c, '_');
            Exception last = null;
            foreach (var dir in Folders())
                try
                {
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, clean + ".txt");
                    File.WriteAllLines(path, apps.Select(a => a.Id + "|" + a.Name), Encoding.UTF8);
                    return path;
                }
                catch (Exception ex) { last = ex; }
            throw last ?? new IOException("No hay carpeta disponible.");
        }
    }

    // Lo que el programa de respaldo anotó del equipo anterior.
    public static class BackupImport
    {
        public static string FindNewest()
        {
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var d in DriveInfo.GetDrives())
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                    foreach (var dir in Directory.GetDirectories(d.RootDirectory.FullName, "Respaldo_*"))
                    {
                        if (!File.Exists(Path.Combine(dir, "Sistema", "winget.json"))) continue;
                        var t = Directory.GetLastWriteTime(dir);
                        if (t > bestTime) { best = dir; bestTime = t; }
                    }
                }
                catch { }
            return best;
        }

        public static List<string> WingetIds(string backup)
        {
            string p = Path.Combine(backup, "Sistema", "winget.json");
            if (!File.Exists(p)) return new List<string>();
            return Regex.Matches(File.ReadAllText(p), "\"PackageIdentifier\"\\s*:\\s*\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<string> ProgramNames(string backup)
        {
            string p = Path.Combine(backup, "Sistema", "programas.tsv");
            if (!File.Exists(p)) return new List<string>();
            return File.ReadAllLines(p, Encoding.UTF8).Select(l => l.Split('\t')[0]).Where(n => n.Length > 0).ToList();
        }
    }

    public class ToolRun
    {
        Process proc;
        public volatile bool Killed;
        public volatile string LastLine = "";

        // Parte por \r y \n: winget actualiza su progreso en la misma línea.
        public int Run(string file, string args, int timeoutMs, out string output, bool window = false)
        {
            output = "";
            Killed = false;
            var psi = new ProcessStartInfo(file, args);
            if (window) psi.UseShellExecute = true;
            else
            {
                psi.UseShellExecute = false; psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
            }
            using (var p = Process.Start(psi))
            {
                proc = p;
                var sb = new StringBuilder();
                Thread reader = null;
                if (!window)
                {
                    reader = new Thread(() =>
                    {
                        try
                        {
                            var line = new StringBuilder();
                            int c;
                            while ((c = p.StandardOutput.Read()) >= 0)
                            {
                                sb.Append((char)c);
                                if (c == '\r' || c == '\n')
                                {
                                    string t = line.ToString().Trim();
                                    if (t.Length > 1) LastLine = t;
                                    line.Clear();
                                }
                                else line.Append((char)c);
                            }
                        }
                        catch { }
                    }) { IsBackground = true };
                    reader.Start();
                    var err = new Thread(() => { try { p.StandardError.ReadToEnd(); } catch { } }) { IsBackground = true };
                    err.Start();
                }
                if (!p.WaitForExit(timeoutMs))
                {
                    Kill();
                    p.WaitForExit(5000);
                    if (reader != null) reader.Join(1000);
                    output = sb.ToString();
                    return -2;
                }
                if (reader != null) reader.Join(3000);
                output = sb.ToString();
                proc = null;
                return p.ExitCode;
            }
        }

        // Mata también al instalador que winget haya lanzado.
        public void Kill()
        {
            Killed = true;
            var p = proc;
            if (p == null) return;
            try
            {
                string o;
                new ToolRun().Run("taskkill", "/T /F /PID " + p.Id, 10000, out o);
            }
            catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
        }
    }

    public static class Winget
    {
        public static string Path_ = "winget";
        public static string Version = "";
        public static bool Available;
        static bool modern = true;

        public static bool Detect()
        {
            var candidates = new List<string>();
            string test = Environment.GetEnvironmentVariable("POSFORMATEO_WINGET");
            if (!string.IsNullOrEmpty(test)) candidates.Add(test);
            else
            {
                candidates.Add("winget");
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe"));
            }
            foreach (var c in candidates)
                try
                {
                    string o;
                    if (new ToolRun().Run(c, "--version", 15000, out o) != 0) continue;
                    var m = Regex.Match(o, @"v?(\d+)\.(\d+)");
                    if (!m.Success) continue;
                    Path_ = c;
                    Version = m.Value;
                    int major = int.Parse(m.Groups[1].Value), minor = int.Parse(m.Groups[2].Value);
                    modern = major > 1 || minor >= 4;
                    Available = true;
                    return true;
                }
                catch { }
            Available = false;
            return false;
        }

        // Solución documentada por Microsoft para un Windows recién instalado.
        public static bool TryRegister()
        {
            string o;
            try
            {
                new ToolRun().Run("powershell", "-NoProfile -NonInteractive -Command \"Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\"", 120000, out o);
            }
            catch { }
            return Detect();
        }

        static string Quiet { get { return modern ? " --disable-interactivity" : ""; } }

        public static string ListArgs(string id) { return "list --exact --id \"" + id + "\" --accept-source-agreements" + Quiet; }
        public static string InstallArgs(string id) { return "install --exact --id \"" + id + "\" --source winget --silent --accept-package-agreements --accept-source-agreements" + Quiet; }
        public static string ShowArgs(string id) { return "show --exact --id \"" + id + "\" --source winget --accept-source-agreements" + Quiet; }
        public static string SearchArgs(string q) { return "search \"" + q.Replace("\"", "") + "\" --source winget --accept-source-agreements" + Quiet; }

        // Toma las columnas de la línea de título, porque los nombres pueden contener espacios.
        public static List<App> ParseSearch(string output)
        {
            var found = new List<App>();
            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int dash = Array.FindIndex(lines, l => l.TrimStart().StartsWith("---"));
            if (dash < 1) return found;
            var cols = Regex.Matches(lines[dash - 1], @"\S+").Cast<Match>().Select(m => m.Index).ToList();
            if (cols.Count < 3) return found;
            for (int i = dash + 1; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.Length <= cols[1]) continue;
                string name = l.Substring(0, cols[1]).Trim();
                string id = l.Substring(cols[1], Math.Min(cols[2], l.Length) - cols[1]).Trim();
                if (id.Length == 0 || id.Contains(" ")) continue;
                found.Add(new App { Id = id, Name = name.Length > 0 ? name : id });
            }
            return found;
        }
    }

    public enum StepState { Pending, Running, Installed, Already, Reboot, Failed, Skipped }

    public class Step
    {
        public App App;
        public volatile StepState State;
        public string Detail = "";
    }

    public class Installer
    {
        public readonly List<Step> Steps;
        public volatile bool Cancel;
        public volatile Step Current;
        public volatile bool SkipRequested;
        readonly object gate = new object();
        ToolRun run;

        public static int TimeoutMs
        {
            get
            {
                int s;
                return int.TryParse(Environment.GetEnvironmentVariable("POSFORMATEO_LIMITE_S"), out s) ? s * 1000 : 30 * 60 * 1000;
            }
        }

        public Installer(IEnumerable<App> apps) { Steps = apps.Select(a => new Step { App = a }).ToList(); }

        public string Line { get { var r = run; return r == null ? "" : r.LastLine; } }

        public void SkipCurrent()
        {
            SkipRequested = true;
            lock (gate) { if (run != null) run.Kill(); }
        }

        public void StopAll()
        {
            Cancel = true;
            SkipCurrent();
        }

        public void Run()
        {
            foreach (var s in Steps.Where(x => x.State == StepState.Pending || x.State == StepState.Failed || x.State == StepState.Skipped))
            {
                s.State = StepState.Pending;
                s.Detail = "";
            }
            foreach (var s in Steps)
            {
                if (Cancel) break;
                if (s.State != StepState.Pending) continue;
                Current = s;
                s.State = StepState.Running;
                SkipRequested = false;
                try
                {
                    if (s.App.Kind == Kind.Local) InstallLocal(s);
                    else InstallWinget(s);
                }
                catch (Exception ex)
                {
                    s.State = StepState.Failed;
                    s.Detail = "No se pudo iniciar: " + ex.Message;
                }
                if (SkipRequested && s.State != StepState.Installed && s.State != StepState.Already && s.State != StepState.Reboot)
                {
                    s.State = Cancel ? StepState.Pending : StepState.Skipped;
                    s.Detail = Cancel ? "" : "Omitido por ti.";
                }
            }
            Current = null;
        }

        int Exec(string file, string args, out string output, bool window = false)
        {
            var r = new ToolRun();
            lock (gate) { run = r; }
            if (SkipRequested) { output = ""; return -3; }
            return r.Run(file, args, TimeoutMs, out output, window);
        }

        void InstallWinget(Step s)
        {
            string o;
            int code = Exec(Winget.Path_, Winget.ListArgs(s.App.Id), out o);
            if (SkipRequested) return;
            if (code == 0 && o.IndexOf(s.App.Id, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                s.State = StepState.Already;
                s.Detail = "Ya estaba instalado.";
                return;
            }

            for (int attempt = 1; ; attempt++)
            {
                code = Exec(Winget.Path_, Winget.InstallArgs(s.App.Id), out o);
                if (SkipRequested) return;
                if ((code == E_DOWNLOAD && attempt < 2) || (code == E_BUSY && attempt < 3) || (code == 1618 && attempt < 3))
                {
                    Thread.Sleep(code == E_DOWNLOAD ? 3000 : 20000);
                    if (SkipRequested) return;
                    continue;
                }
                break;
            }
            Classify(s, code);
        }

        static readonly int E_DOWNLOAD = unchecked((int)0x8A150008), E_BUSY = unchecked((int)0x8A150102);

        public static void Classify(Step s, int code)
        {
            switch (unchecked((uint)code))
            {
                case 0: s.State = StepState.Installed; s.Detail = "Instalado."; return;
                case 0x8A150061: case 0x8A15002B: case 0x8A15010D:
                    s.State = StepState.Already; s.Detail = "Ya estaba instalado."; return;
                case 3010: case 1641: case 0x8A150109:
                    s.State = StepState.Reboot; s.Detail = "Instalado. Hay que reiniciar para terminar."; return;
            }
            s.State = StepState.Failed;
            s.Detail = Reason(code);
        }

        public static string Reason(int code)
        {
            switch (unchecked((uint)code))
            {
                case 0xFFFFFFFE: return "Tardó demasiado y se detuvo. Reintenta o instálalo a mano.";
                case 0x8A150014: return "Ya no existe en el catálogo de winget. Búscalo con otro nombre.";
                case 0x8A150008: return "No se pudo descargar. Revisa la conexión a internet y reintenta.";
                case 0x8A150107: return "Sin conexión a internet. Conéctate y reintenta.";
                case 0x8A150105: return "No queda espacio en el disco.";
                case 0x8A150011: return "El instalador no coincide con el del catálogo, y se bloqueó por seguridad. Reintenta más tarde o descárgalo de la web oficial.";
                case 0x8A150101: case 0x8A150103: return "El programa o uno de sus archivos está en uso. Ciérralo y reintenta.";
                case 0x8A150102: case 1618: return "Otra instalación sigue en curso. Espera y reintenta.";
                case 0x8A15010A: return "Windows pide reiniciar antes de instalarlo. Reinicia y reintenta.";
                case 0x8A15010C: return "Se canceló la instalación.";
                case 0x8A15010F: return "Una directiva de la organización bloquea la instalación.";
                default: return "El instalador terminó con el código " + code + " (0x" + code.ToString("X8") + "). Instálalo a mano si hace falta.";
            }
        }

        void InstallLocal(Step s)
        {
            string o, path = s.App.Id;
            bool msi = path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
            int code = msi ? Exec("msiexec", "/i \"" + path + "\" /qn /norestart", out o) : Exec(path, "", out o, true);
            if (SkipRequested) return;
            Classify(s, code);
            if (!msi && s.State == StepState.Installed) s.Detail = "El instalador terminó.";
        }
    }

    public class Tweak
    {
        public string Key, Label, Hint;
        public bool Selected = true, Done, Failed;
        public string Detail = "";
        public Action<Tweak, string> Apply;
        public object Node;
    }

    public static class Tweaks
    {
        // En pruebas todo se escribe en una clave aparte y no se ejecuta ningún comando del sistema.
        public static bool TestMode { get { return Environment.GetEnvironmentVariable("POSFORMATEO_PRUEBA") == "1"; } }
        public static string Prefix { get { return TestMode ? @"Software\PosformateoPrueba\" : ""; } }
        public const string ComputerIcon = "{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
        const string Adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        public static string NewName;

        public static List<Tweak> Build()
        {
            var list = new List<Tweak>
            {
                new Tweak { Key = "punto", Label = "Crear un punto de restauración antes de empezar", Hint = "Permite volver atrás si algo sale mal", Apply = (t, p) => Restore(t) },
                Reg("ext", "Mostrar las extensiones de archivo", "Ayuda a distinguir un .exe de un documento", Adv, "HideFileExt", 0, true),
                Reg("ocultos", "Mostrar archivos ocultos", null, Adv, "Hidden", 1, false),
                Reg("equipo", "Abrir el Explorador en «Este equipo»", null, Adv, "LaunchTo", 1, true),
                Reg("icono", "Poner el icono «Este equipo» en el Escritorio", null, @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel", ComputerIcon, 0, true),
                Reg("busqueda", "Reducir la búsqueda de la barra de tareas a un icono", null, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", 1, true),
                new Tweak { Key = "energia", Label = "No suspender el equipo mientras esté enchufado", Hint = "Evita que se corten instalaciones largas", Apply = (t, p) => Command(t, "powercfg", "/change standby-timeout-ac 0", "Listo.") },
                new Tweak { Key = "update", Label = "Abrir Windows Update al terminar", Hint = "Para buscar actualizaciones y controladores", Apply = (t, p) => { t.Done = true; t.Detail = "Se abrirá al terminar."; } }
            };
            return list;
        }

        static Tweak Reg(string key, string label, string hint, string path, string name, int value, bool selected)
        {
            return new Tweak
            {
                Key = key, Label = label, Hint = hint, Selected = selected,
                Apply = (t, prefix) =>
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(prefix + path))
                    {
                        object old = k.GetValue(name);
                        k.SetValue(name, value, RegistryValueKind.DWord);
                        t.Detail = "Aplicado. Valor anterior: " + (old ?? "sin definir") + ".";
                    }
                    t.Done = true;
                }
            };
        }

        static void Command(Tweak t, string file, string args, string okText)
        {
            if (TestMode) { t.Done = true; t.Detail = "Simulado en modo de prueba."; return; }
            string o;
            int code = new ToolRun().Run(file, args, 300000, out o);
            t.Done = code == 0;
            t.Failed = code != 0;
            t.Detail = code == 0 ? okText : "No se pudo completar (código " + code + ").";
        }

        static void Restore(Tweak t)
        {
            Command(t, "powershell", "-NoProfile -NonInteractive -Command \"$ErrorActionPreference='Stop'; Enable-ComputerRestore -Drive $env:SystemDrive; Checkpoint-Computer -Description 'Antes de Posformateo PC' -RestorePointType MODIFY_SETTINGS\"", "Punto de restauración listo.");
            if (t.Failed) t.Detail += " Puede estar desactivado por el sistema, requerir administrador o haberse creado otro hace poco.";
        }

        public static bool ValidName(string name)
        {
            return Regex.IsMatch(name, @"^(?!\d+$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,13}[A-Za-z0-9])?$");
        }

        public static Tweak RenameTweak(string name)
        {
            return new Tweak
            {
                Key = "nombre", Label = "Cambiar el nombre del equipo a " + name,
                Apply = (t, p) =>
                {
                    Command(t, "powershell", "-NoProfile -NonInteractive -Command \"Rename-Computer -NewName '" + name + "' -Force -ErrorAction Stop\"", "Nombre cambiado. Se aplica al reiniciar.");
                }
            };
        }

        public static void RunAll(IEnumerable<Tweak> tweaks)
        {
            foreach (var t in tweaks.Where(x => x.Selected))
            {
                t.Done = t.Failed = false;
                try { t.Apply(t, Prefix); }
                catch (Exception ex) { t.Failed = true; t.Detail = "No se pudo aplicar: " + ex.Message; }
            }
        }
    }

    public static class Checks
    {
        public static bool Admin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        public static bool Internet()
        {
            foreach (var ip in new[] { "1.1.1.1", "8.8.8.8" })
                try
                {
                    using (var p = new Ping())
                        if (p.Send(IPAddress.Parse(ip), 2000).Status == IPStatus.Success) return true;
                }
                catch { }
            return false;
        }

        public static List<string> MissingDrivers()
        {
            var list = new List<string>();
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name,DeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode = 28"))
                    foreach (ManagementObject m in s.Get())
                        list.Add((m["Name"] ?? "Dispositivo no reconocido") + " (" + m["DeviceID"] + ")");
            }
            catch { }
            return list;
        }
    }

    public static class Report
    {
        static string E(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        public static string StateWord(StepState s)
        {
            switch (s)
            {
                case StepState.Pending: return "Sin ejecutar";
                case StepState.Running: return "Instalando…";
                case StepState.Installed: return "Instalado";
                case StepState.Already: return "Ya estaba";
                case StepState.Reboot: return "Instalado, requiere reinicio";
                case StepState.Failed: return "Falló";
                default: return "Omitido";
            }
        }

        public static string Verdict(Installer inst, List<Tweak> tweaks, out string detail, out string color)
        {
            int failed = inst.Steps.Count(s => s.State == StepState.Failed) + tweaks.Count(t => t.Selected && t.Failed);
            int notDone = inst.Steps.Count(s => s.State == StepState.Pending || s.State == StepState.Skipped);
            int ok = inst.Steps.Count(s => s.State == StepState.Installed || s.State == StepState.Reboot);
            int had = inst.Steps.Count(s => s.State == StepState.Already);
            detail = "Instalados: " + ok + ". Ya estaban: " + had + "." + (failed > 0 ? " Con fallos: " + failed + "." : "") + (notDone > 0 ? " Sin instalar: " + notDone + "." : "");
            if (failed > 0) { color = "#c62828"; return failed + (failed == 1 ? " elemento falló" : " elementos fallaron") + ": revisa la lista"; }
            if (notDone > 0) { color = "#a86200"; return "Quedaron programas sin instalar"; }
            color = "#1b7f4b";
            return inst.Steps.Any(s => s.State == StepState.Reboot) ? "Equipo listo: falta reiniciar" : "Equipo listo";
        }

        public static string Page(Installer inst, List<Tweak> tweaks, List<string> drivers, List<string> oldPrograms, AppInfoHolder holder)
        {
            string detail, color;
            string verdict = Verdict(inst, tweaks, out detail, out color);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=es><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Instalación de " + E(Environment.MachineName) + "</title><style>");
            sb.Append("body{margin:0;background:#eef1f3;color:#1e2a32;font:15px/1.5 'Segoe UI',system-ui,sans-serif}.band{color:#fff;padding:24px}.band h1{font-size:25px;line-height:1.2;margin:0 0 6px;font-weight:600}.band p{margin:0;max-width:72ch}");
            sb.Append("main{max-width:880px;margin:0 auto;padding:8px 16px 40px}h2{font-size:18px;font-weight:600;margin:26px 0 8px}table{border-collapse:collapse;width:100%;background:#fff;font-size:14px}td,th{padding:6px 10px;border-bottom:1px solid #d5dbe0;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{font-weight:600}.w{overflow-x:auto}li{margin:4px 0;max-width:72ch}");
            sb.Append("@media print{body{background:#fff}.band{-webkit-print-color-adjust:exact;print-color-adjust:exact}}</style>");
            sb.Append("<div class=band style='background:" + color + "'><h1>" + E(verdict) + "</h1><p>" + E(detail) + "</p><p>Equipo " + E(Environment.MachineName) + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".</p></div><main>");

            var pending = new List<string>();
            if (inst.Steps.Any(s => s.State == StepState.Reboot) || tweaks.Any(t => t.Key == "nombre" && t.Done)) pending.Add("Reinicia el equipo.");
            if (tweaks.Any(t => t.Key == "icono" || t.Key == "ext")) pending.Add("Algunos ajustes del Explorador se ven al cerrar sesión o reiniciar.");
            if (drivers.Count > 0) pending.Add(drivers.Count + " dispositivos siguen sin controlador. Busca en Windows Update o en la web del fabricante.");
            if (pending.Count > 0)
            {
                sb.Append("<h2>Qué falta hacer</h2><ul>");
                foreach (var p in pending) sb.Append("<li>" + E(p) + "</li>");
                sb.Append("</ul>");
            }
            sb.Append("<h2>Programas</h2><div class=w><table><tr><th>Programa</th><th>Estado</th><th>Detalle</th></tr>");
            foreach (var s in inst.Steps)
                sb.Append("<tr><td>" + E(s.App.Name) + "</td><td>" + E(StateWord(s.State)) + "</td><td>" + E(s.Detail) + "</td></tr>");
            sb.Append("</table></div>");
            if (tweaks.Any(t => t.Selected))
            {
                sb.Append("<h2>Ajustes</h2><div class=w><table><tr><th>Ajuste</th><th>Resultado</th></tr>");
                foreach (var t in tweaks.Where(x => x.Selected))
                    sb.Append("<tr><td>" + E(t.Label) + "</td><td>" + E(t.Detail.Length > 0 ? t.Detail : "No se aplicó.") + "</td></tr>");
                sb.Append("</table></div>");
            }
            if (drivers.Count > 0)
            {
                sb.Append("<h2>Dispositivos sin controlador</h2><ul>");
                foreach (var d in drivers) sb.Append("<li>" + E(d) + "</li>");
                sb.Append("</ul>");
            }
            if (oldPrograms != null && oldPrograms.Count > 0)
            {
                sb.Append("<h2>Programas del equipo anterior</h2><p>Lista del respaldo, para revisar lo que haya que instalar a mano.</p><ul>");
                foreach (var d in oldPrograms) sb.Append("<li>" + E(d) + "</li>");
                sb.Append("</ul>");
            }
            sb.Append(Comun.Credits.HtmlFooter(holder.Info));
            sb.Append("</main></html>");
            return sb.ToString();
        }

        public static string Save(string html)
        {
            string name = "Instalacion_" + Environment.MachineName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + ".html";
            try
            {
                if (Tweaks.TestMode) throw new InvalidOperationException();
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name);
                File.WriteAllText(path, html, new UTF8Encoding(true));
                return path;
            }
            catch
            {
                string path = Path.Combine(Path.GetTempPath(), name);
                File.WriteAllText(path, html, new UTF8Encoding(true));
                return path;
            }
        }
    }

    public class AppInfoHolder { public Comun.AppInfo Info; }
}
