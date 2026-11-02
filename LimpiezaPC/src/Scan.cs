using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LimpiezaPC
{
    public enum Sev { Normal, Review, Suspicious }

    public enum ItemKind { Startup, Task, Service, Extension, Shortcut, System, Defender }

    public class Item
    {
        public string Id = "", Group = "", Name = "", Location = "", Command = "", Path = "", Signer = "", SigStatus = "";
        public ItemKind Kind;
        public Sev Severity;
        public bool Disabled, CanDisable;
        public string Advice = "";
        public readonly List<string> Reasons = new List<string>();
        // Datos para desactivar: hive, clave del valor, nombre, carpeta de StartupApproved
        public RegistryKey Hive;
        public string RegKey = "", RegValue = "", Approved = "";

        public void Flag(Sev s, string reason)
        {
            if (s > Severity) Severity = s;
            if (!Reasons.Contains(reason)) Reasons.Add(reason);
        }
    }

    public static class Env
    {
        public static bool Admin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
        public static string Windows { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
    }

    public static class Cmd
    {
        public static int Run(string file, string args, int timeoutMs, out string output)
        {
            output = "";
            var psi = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                string got = "";
                var a = new System.Threading.Thread(() => { try { got = p.StandardOutput.ReadToEnd(); } catch { } }) { IsBackground = true };
                var b = new System.Threading.Thread(() => { try { p.StandardError.ReadToEnd(); } catch { } }) { IsBackground = true };
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
    }

    // Pruebas puras sobre rutas y comandos: no dependen del equipo y se pueden comprobar sin ventana.
    public static class Rules
    {
        static readonly Regex BadArgs = new Regex(
            @"(?:^|\s)-e(nc|ncodedcommand|c)?\s+[A-Za-z0-9+/=]{16,}|frombase64string|downloadstring|downloadfile|invoke-webrequest|invoke-expression|\biex\b|\biwr\s|start-bitstransfer|-w(indowstyle)?\s+hidden|certutil.*-urlcache|bitsadmin.*/transfer|mshta(\.exe)?""?\s+[""']?(https?:|javascript:|vbscript:)|regsvr32.*scrobj|rundll32.*javascript:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly string[] SystemNames = { "svchost.exe", "lsass.exe", "csrss.exe", "winlogon.exe", "smss.exe", "services.exe", "taskhostw.exe", "dllhost.exe", "spoolsv.exe", "conhost.exe", "wininit.exe", "explorer.exe", "rundll32.exe" };
        static readonly string[] Interpreters = { "powershell.exe", "pwsh.exe", "cmd.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe" };

        public static string Expand(string raw)
        {
            string s = (raw ?? "").Trim();
            s = Environment.ExpandEnvironmentVariables(s);
            if (s.StartsWith(@"\??\")) s = s.Substring(4);
            if (s.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) s = Env.Windows + s.Substring(11);
            else if (s.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase)) s = Env.Windows + @"\" + s;
            return s;
        }

        // Separa el ejecutable de los argumentos, también si la ruta lleva espacios y no tiene comillas.
        public static void SplitCommand(string raw, out string exe, out string args)
        {
            string s = Expand(raw);
            exe = ""; args = "";
            if (s.Length == 0) return;
            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 0) { exe = s.Substring(1, end - 1); args = s.Substring(end + 1).Trim(); }
                else exe = s.Trim('"');
            }
            else
            {
                exe = s;
                int from = 0;
                bool found = false;
                while (true)
                {
                    int sp = s.IndexOf(' ', from);
                    string head = sp < 0 ? s : s.Substring(0, sp);
                    if (FileThere(head))
                    {
                        exe = head; args = sp < 0 ? "" : s.Substring(sp + 1).Trim();
                        found = true;
                        break;
                    }
                    if (sp < 0) break;
                    from = sp + 1;
                }
                if (!found)
                {
                    // No existe: se decide por la extensión, o por el primer espacio.
                    var m = Regex.Match(s, @"^(.+?\.(exe|com|bat|cmd|vbs|js|ps1|dll|scr))(\s|$)", RegexOptions.IgnoreCase);
                    if (m.Success) { exe = m.Groups[1].Value; args = s.Substring(m.Length).Trim(); }
                    else
                    {
                        int sp = s.IndexOf(' ');
                        if (sp > 0) { exe = s.Substring(0, sp); args = s.Substring(sp + 1).Trim(); }
                    }
                }
            }
            exe = Resolve(exe);
        }

        static bool FileThere(string p)
        {
            try { return p.Contains("\\") && (File.Exists(p) || File.Exists(p + ".exe")); } catch { return false; }
        }

        public static string Resolve(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return exe;
            try
            {
                if (File.Exists(exe)) return exe;
                if (File.Exists(exe + ".exe")) return exe + ".exe";
                if (Path.IsPathRooted(exe)) return exe;
                string name = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || exe.Contains(".") ? exe : exe + ".exe";
                foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Concat(new[] { Environment.GetFolderPath(Environment.SpecialFolder.System), Env.Windows }))
                {
                    if (dir.Length == 0) continue;
                    string c;
                    try { c = Path.Combine(dir.Trim(), name); } catch { continue; }
                    if (File.Exists(c)) return c;
                }
            }
            catch { }
            return exe;
        }

        public static bool IsTempLike(string path)
        {
            string p = (path ?? "").ToLowerInvariant();
            return p.Contains(@"\appdata\local\temp\") || p.Contains(@"\windows\temp\") || p.Contains(@"\users\public\") || p.Contains(@"\$recycle.bin\") || p.Contains(@"\downloads\") || p.Contains(@"\descargas\");
        }

        public static bool IsLooseData(string path)
        {
            string p = (path ?? "").ToLowerInvariant();
            return Regex.IsMatch(p, @"^[a-z]:\\programdata\\[^\\]+$") || Regex.IsMatch(p, @"\\appdata\\(roaming|local|locallow)\\[^\\]+$");
        }

        public static bool InWindows(string path)
        {
            return (path ?? "").StartsWith(Env.Windows + "\\", StringComparison.OrdinalIgnoreCase);
        }

        public static bool BadCommand(string text)
        {
            string t = Environment.ExpandEnvironmentVariables(text ?? "");
            // Windows oculta sus propios scripts de mantenimiento: ocultarse solo no basta si el script es suyo.
            var scripts = Regex.Matches(t, @"[a-z]:\\[^""'\s]+\.ps1", RegexOptions.IgnoreCase).Cast<Match>().ToList();
            if (scripts.Count > 0 && scripts.All(m => InWindows(m.Value)))
                t = Regex.Replace(t, @"-w(indowstyle)?\s+hidden", "", RegexOptions.IgnoreCase);
            return BadArgs.IsMatch(t);
        }

        public static void Judge(Item it, Dictionary<string, Signature> sigs)
        {
            string exe, args;
            SplitCommand(it.Command, out exe, out args);
            if (string.IsNullOrEmpty(it.Path)) it.Path = exe;
            string file = "";
            try { file = Path.GetFileName(it.Path ?? "").ToLowerInvariant(); } catch { }

            Signature sig = null;
            if (!string.IsNullOrEmpty(it.Path)) sigs.TryGetValue(it.Path.ToLowerInvariant(), out sig);
            if (sig != null) { it.SigStatus = sig.Status; it.Signer = sig.Signer; }

            bool exists = false;
            try { exists = !string.IsNullOrEmpty(it.Path) && File.Exists(it.Path); } catch { }
            bool valid = sig != null && sig.Status == "Valid";
            bool microsoft = valid && it.Signer.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;

            if (BadCommand(it.Command))
                it.Flag(Sev.Suspicious, "El comando se oculta o descarga algo de internet, algo habitual en programas maliciosos.");
            if (!exists && !string.IsNullOrEmpty(it.Path))
                it.Flag(Sev.Review, "El archivo ya no existe: suele ser un resto de un programa desinstalado.");
            if (sig != null && (sig.Status == "HashMismatch" || sig.Status == "NotTrusted"))
                it.Flag(Sev.Suspicious, "Su firma digital no es válida: el archivo pudo haber sido alterado.");
            if (SystemNames.Contains(file) && exists && !InWindows(it.Path))
                it.Flag(Sev.Suspicious, "Se llama como un archivo de Windows, pero está fuera de la carpeta de Windows.");
            if (exists && IsTempLike(it.Path))
                it.Flag(valid ? Sev.Review : Sev.Suspicious, "Arranca desde una carpeta temporal o de descargas, donde un programa legítimo no suele instalarse.");
            if (exists && !valid && IsLooseData(it.Path))
                it.Flag(Sev.Suspicious, "Es un archivo sin firma suelto en una carpeta de datos del usuario.");
            if (exists && sig != null && sig.Status == "NotSigned" && it.Severity == Sev.Normal)
            {
                string low = it.Path.ToLowerInvariant();
                if (low.Contains(@"\appdata\")) it.Flag(Sev.Review, "No tiene firma digital y está en la carpeta de datos del usuario.");
                else it.Flag(Sev.Review, "No tiene firma digital, por lo que no se puede confirmar quién lo publica.");
            }

            // Un intérprete firmado por Microsoft no dice nada: lo que importa es lo que ejecuta.
            if (Interpreters.Contains(file))
            {
                it.Advice = "Es un programa de Windows usado para ejecutar otra cosa. Revisa el comando completo.";
                string target = Regex.Match(args, @"[A-Za-z]:\\[^""'\s,]+\.(ps1|vbs|js|jse|wsf|hta|bat|cmd|dll|scr|exe)", RegexOptions.IgnoreCase).Value;
                if (target.Length > 0 && IsTempLike(target))
                    it.Flag(Sev.Suspicious, "Ejecuta un archivo desde una carpeta temporal o de descargas.");
                else if (!microsoft || !InWindows(it.Path) || (target.Length > 0 && !InWindows(target)))
                    it.Flag(Sev.Review, "Usa un intérprete de comandos para ejecutar otro archivo.");
                if (Regex.IsMatch(args, @"https?://", RegexOptions.IgnoreCase))
                    it.Flag(Sev.Suspicious, "El intérprete de comandos abre una dirección de internet.");
            }
        }
    }

    public class Signature { public string Status = "", Signer = ""; }

    public static class Signatures
    {
        // Get-AuthenticodeSignature reconoce también los archivos firmados por catálogo, como los de Windows.
        public static Dictionary<string, Signature> Load(IEnumerable<string> paths)
        {
            var map = new Dictionary<string, Signature>(StringComparer.OrdinalIgnoreCase);
            var list = paths.Where(p => { try { return !string.IsNullOrEmpty(p) && File.Exists(p); } catch { return false; } }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0) return map;
            string baseName = Path.Combine(Path.GetTempPath(), "limpieza_" + Guid.NewGuid().ToString("N"));
            string inFile = baseName + ".in", outFile = baseName + ".out", script = baseName + ".ps1";
            try
            {
                File.WriteAllLines(inFile, list, new UTF8Encoding(false));
                File.WriteAllText(script,
                    "$r=New-Object Collections.ArrayList\n" +
                    "foreach($p in [IO.File]::ReadAllLines('" + inFile + "')){try{$s=Get-AuthenticodeSignature -LiteralPath $p -ErrorAction Stop;$n='';if($s.SignerCertificate){$n=$s.SignerCertificate.Subject};[void]$r.Add($p+\"`t\"+$s.Status+\"`t\"+$n)}catch{[void]$r.Add($p+\"`tError`t\")}}\n" +
                    "[IO.File]::WriteAllLines('" + outFile + "',[string[]]$r.ToArray(),(New-Object Text.UTF8Encoding($false)))\n", new UTF8Encoding(true));
                string o;
                Cmd.Run("powershell", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\"", 240000, out o);
                if (File.Exists(outFile))
                    foreach (var line in File.ReadAllLines(outFile, Encoding.UTF8))
                    {
                        var c = line.Split('\t');
                        if (c.Length < 2) continue;
                        map[c[0]] = new Signature { Status = c[1], Signer = c.Length > 2 ? Friendly(c[2]) : "" };
                    }
            }
            catch { }
            finally
            {
                foreach (var f in new[] { inFile, outFile, script }) try { File.Delete(f); } catch { }
            }
            return map;
        }

        static string Friendly(string subject)
        {
            var m = Regex.Match(subject ?? "", @"CN=(""[^""]+""|[^,]+)");
            return m.Success ? m.Groups[1].Value.Trim('"') : subject ?? "";
        }
    }

    public class ScanResult
    {
        public readonly List<Item> Items = new List<Item>();
        public readonly List<string> Gaps = new List<string>();
        public bool Admin;
    }

    public static class Scanner
    {
        const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run", RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            Wow = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        public static ScanResult Run(Action<string> status)
        {
            var r = new ScanResult { Admin = Env.Admin() };
            Action<string, Action> step = (name, work) =>
            {
                if (status != null) status(name);
                try { work(); }
                catch (Exception ex) { r.Gaps.Add(name + ": no se pudo revisar (" + ex.Message + ")"); }
            };
            step("Programas de inicio", () => Startup(r));
            step("Tareas programadas", () => Tasks(r));
            step("Servicios", () => Services(r));
            step("Inicio de sesión de Windows", () => WinlogonAndHijacks(r));
            step("Eventos WMI ocultos", () => WmiPersistence(r));
            step("Extensiones de navegadores", () => Extensions(r));
            step("Accesos directos de navegadores", () => Shortcuts(r));
            step("Archivo hosts y proxy", () => Network(r));
            step("Windows Defender", () => Defender(r));

            if (status != null) status("Comprobando firmas digitales");
            var targets = new List<Item>();
            foreach (var it in r.Items.Where(i => i.Command.Length > 0))
            {
                string exe, args;
                Rules.SplitCommand(it.Command, out exe, out args);
                it.Path = exe;
                targets.Add(it);
            }
            var sigs = Signatures.Load(targets.Select(i => i.Path));
            if (sigs.Count == 0 && targets.Count > 0) r.Gaps.Add("Firmas digitales: no se pudieron comprobar en este equipo.");
            foreach (var it in targets)
                try { Rules.Judge(it, sigs); }
                catch (Exception ex) { r.Gaps.Add(it.Name + ": " + ex.Message); }

            if (!r.Admin) r.Gaps.Add("Sin administrador no se ven las tareas de otros usuarios ni las exclusiones de Defender.");
            return r;
        }

        static void Startup(ScanResult r)
        {
            ReadRun(r, Registry.CurrentUser, "HKCU", RunPath, "Run", "Inicio de este usuario");
            ReadRun(r, Registry.CurrentUser, "HKCU", RunOncePath, null, "Inicio de este usuario (una vez)");
            ReadRun(r, Registry.LocalMachine, "HKLM", RunPath, "Run", "Inicio de todos los usuarios");
            ReadRun(r, Registry.LocalMachine, "HKLM", Wow, "Run32", "Inicio de todos los usuarios (32 bits)");
            ReadRun(r, Registry.LocalMachine, "HKLM", RunOncePath, null, "Inicio de todos los usuarios (una vez)");

            var dirs = new[]
            {
                new { Dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup), Hive = Registry.CurrentUser, Label = "Carpeta Inicio de este usuario" },
                new { Dir = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Hive = Registry.LocalMachine, Label = "Carpeta Inicio de todos los usuarios" }
            };
            foreach (var d in dirs)
            {
                if (string.IsNullOrEmpty(d.Dir) || !Directory.Exists(d.Dir)) continue;
                foreach (var f in Directory.GetFiles(d.Dir))
                {
                    if (Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    string target = f;
                    string args = "";
                    if (f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) ReadLink(f, out target, out args);
                    var it = new Item
                    {
                        Id = "startup|folder|" + f, Kind = ItemKind.Startup, Group = "Inicio de Windows", Name = Path.GetFileName(f), Location = d.Label,
                        Command = "\"" + target + "\"" + (args.Length > 0 ? " " + args : ""),
                        Hive = d.Hive, RegValue = Path.GetFileName(f), Approved = "StartupFolder", CanDisable = true
                    };
                    it.Disabled = IsApprovedOff(d.Hive, "StartupFolder", it.RegValue);
                    r.Items.Add(it);
                }
            }
        }

        static void ReadRun(ScanResult r, RegistryKey hive, string hiveName, string path, string approved, string label)
        {
            using (var k = hive.OpenSubKey(path))
            {
                if (k == null) return;
                foreach (var name in k.GetValueNames())
                {
                    if (name.Length == 0) continue;
                    string cmd = Convert.ToString(k.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames));
                    var it = new Item
                    {
                        Id = "startup|" + hiveName + "|" + path + "|" + name, Kind = ItemKind.Startup, Group = "Inicio de Windows", Name = name, Location = label, Command = cmd,
                        Hive = hive, RegKey = path, RegValue = name, Approved = approved ?? "", CanDisable = true
                    };
                    if (approved != null) it.Disabled = IsApprovedOff(hive, approved, name);
                    r.Items.Add(it);
                }
            }
        }

        public static bool IsApprovedOff(RegistryKey hive, string approved, string name)
        {
            using (var k = hive.OpenSubKey(ApprovedBase + approved))
            {
                var b = k == null ? null : k.GetValue(name) as byte[];
                return b != null && b.Length > 0 && (b[0] & 1) == 1;
            }
        }

        public static void ReadLink(string lnk, out string target, out string args)
        {
            target = lnk; args = "";
            object shell = null, link = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                link = shell.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                target = Convert.ToString(link.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, link, null));
                args = Convert.ToString(link.GetType().InvokeMember("Arguments", System.Reflection.BindingFlags.GetProperty, null, link, null));
                if (string.IsNullOrEmpty(target)) target = lnk;
            }
            catch { }
            finally
            {
                if (link != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(link);
                if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
        }

        static void Tasks(ScanResult r)
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            service.Connect();
            var pending = new Stack<dynamic>();
            pending.Push(service.GetFolder("\\"));
            while (pending.Count > 0)
            {
                dynamic folder = pending.Pop();
                foreach (dynamic sub in folder.GetFolders(0)) pending.Push(sub);
                foreach (dynamic task in folder.GetTasks(1))
                {
                    string path = task.Path, xml;
                    try { xml = task.Xml; } catch { continue; }
                    var cmds = Regex.Matches(xml, @"<Exec>\s*<Command>(.*?)</Command>(?:\s*<Arguments>(.*?)</Arguments>)?", RegexOptions.Singleline);
                    foreach (Match m in cmds)
                    {
                        string cmd = WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
                        string arg = WebUtility.HtmlDecode(m.Groups[2].Value).Trim();
                        string exeOnly = Rules.Expand(cmd);
                        var it = new Item
                        {
                            Id = "task|" + path + "|" + cmd, Kind = ItemKind.Task, Group = "Tareas programadas", Name = path.TrimStart('\\'), Location = "Programador de tareas",
                            Command = (exeOnly.Contains(" ") && !exeOnly.StartsWith("\"") ? "\"" + exeOnly + "\"" : exeOnly) + (arg.Length > 0 ? " " + arg : ""),
                            RegKey = path, CanDisable = true, Disabled = !(bool)task.Enabled
                        };
                        r.Items.Add(it);
                    }
                }
            }
        }

        static void Services(ScanResult r)
        {
            using (var s = new ManagementObjectSearcher("SELECT Name,DisplayName,PathName,StartMode,State FROM Win32_Service"))
                foreach (ManagementObject m in s.Get())
                {
                    string name = Convert.ToString(m["Name"]);
                    string path = Convert.ToString(m["PathName"]);
                    if (path.Length == 0) continue;
                    r.Items.Add(new Item
                    {
                        Id = "service|" + name, Kind = ItemKind.Service, Group = "Servicios", Name = Convert.ToString(m["DisplayName"]), Location = "Servicio " + name + ", inicio " + m["StartMode"] + ", " + m["State"],
                        Command = path, RegKey = name, RegValue = Convert.ToString(m["StartMode"]), CanDisable = true,
                        Disabled = "Disabled".Equals(Convert.ToString(m["StartMode"]), StringComparison.OrdinalIgnoreCase)
                    });
                }
        }

        static void WinlogonAndHijacks(ScanResult r)
        {
            const string wl = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            using (var k = Registry.LocalMachine.OpenSubKey(wl))
                if (k != null)
                {
                    string shell = Convert.ToString(k.GetValue("Shell", "explorer.exe")).Trim();
                    string init = Convert.ToString(k.GetValue("Userinit", "")).Trim().TrimEnd(',');
                    if (!shell.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                        Manual(r, "Winlogon Shell", "Inicio de sesión", shell, Sev.Suspicious, "Windows abre otro programa como escritorio, en lugar de explorer.exe.", "Restablece el valor Shell a explorer.exe, tras comprobar que no sea un kiosco o programa de la empresa.");
                    if (init.Length > 0 && !Regex.IsMatch(init, @"^([a-z]:\\windows\\system32\\)?userinit(\.exe)?$", RegexOptions.IgnoreCase))
                        Manual(r, "Winlogon Userinit", "Inicio de sesión", init, Sev.Suspicious, "Se añadió otro programa al inicio de sesión de Windows.", "Restablece Userinit a C:\\Windows\\system32\\userinit.exe, seguido de una coma.");
                }

            foreach (var view in new[] { @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows" })
                using (var k = Registry.LocalMachine.OpenSubKey(view))
                {
                    string dlls = k == null ? "" : Convert.ToString(k.GetValue("AppInit_DLLs", "")).Trim();
                    if (dlls.Length > 0)
                        Manual(r, "AppInit_DLLs", view, dlls, Sev.Suspicious, "Una DLL se carga dentro de los programas de Windows.", "Identifica la DLL. Si no pertenece a un programa conocido, vacía este valor.");
                }

            using (var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"))
                if (root != null)
                    foreach (var name in root.GetSubKeyNames())
                    {
                        // Algunas claves solo las puede leer el sistema: no deben impedir revisar las demás.
                        string dbg = "";
                        try
                        {
                            using (var k = root.OpenSubKey(name))
                                dbg = k == null ? "" : Convert.ToString(k.GetValue("Debugger", ""));
                        }
                        catch (System.Security.SecurityException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }
                        if (dbg.Length > 0)
                            Manual(r, "Se redirige " + name, "Opciones de ejecución de imagen", dbg, Sev.Suspicious, "Al abrir " + name + ", Windows ejecuta otro programa.", "Si no lo configuraste tú, borra el valor Debugger. Es una técnica común para bloquear antivirus o abrir accesos ocultos.");
                    }
        }

        static void Manual(ScanResult r, string name, string location, string value, Sev sev, string reason, string advice)
        {
            var it = new Item { Id = "system|" + location + "|" + name, Kind = ItemKind.System, Group = "Sistema", Name = name, Location = location, Advice = advice };
            it.Path = "";
            it.Reasons.Add(reason);
            it.Severity = sev;
            it.RegValue = value;
            r.Items.Add(it);
        }

        static void WmiPersistence(ScanResult r)
        {
            foreach (var cls in new[] { "CommandLineEventConsumer", "ActiveScriptEventConsumer" })
            {
                try
                {
                    using (var s = new ManagementObjectSearcher(@"root\subscription", "SELECT * FROM " + cls))
                        foreach (ManagementObject m in s.Get())
                        {
                            string name = Convert.ToString(m["Name"]);
                            string value = cls.StartsWith("Command") ? Convert.ToString(m["CommandLineTemplate"]) : Convert.ToString(m["ScriptText"]);
                            if (name.Equals("BVTConsumer", StringComparison.OrdinalIgnoreCase)) continue;
                            Manual(r, "Evento WMI: " + name, "Suscripciones WMI", value, Sev.Suspicious, "Un evento oculto de Windows ejecuta un comando. Pocos programas legítimos lo usan.", "Revísalo con Autoruns de Sysinternals antes de borrarlo.");
                        }
                }
                catch (ManagementException) { }
            }
        }

        static void Extensions(ScanResult r)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var browsers = new[] { new[] { "Chrome", @"Google\Chrome\User Data", @"SOFTWARE\Policies\Google\Chrome" }, new[] { "Edge", @"Microsoft\Edge\User Data", @"SOFTWARE\Policies\Microsoft\Edge" }, new[] { "Brave", @"BraveSoftware\Brave-Browser\User Data", @"SOFTWARE\Policies\BraveSoftware\Brave" } };
            foreach (var b in browsers)
            {
                string data = Path.Combine(local, b[1]);
                if (Directory.Exists(data))
                    foreach (var profile in Directory.GetDirectories(data))
                    {
                        string ext = Path.Combine(profile, "Extensions");
                        if (!Directory.Exists(ext)) continue;
                        foreach (var id in Directory.GetDirectories(ext))
                        {
                            string idName = Path.GetFileName(id);
                            if (idName == "Temp") continue;
                            var version = Directory.GetDirectories(id).OrderByDescending(x => x).FirstOrDefault();
                            if (version == null) continue;
                            string man = Path.Combine(version, "manifest.json");
                            if (!File.Exists(man)) continue;
                            string json = File.ReadAllText(man, Encoding.UTF8);
                            string name = ExtName(json, version);
                            var it = new Item { Id = "ext|" + b[0] + "|" + Path.GetFileName(profile) + "|" + idName, Kind = ItemKind.Extension, Group = "Extensiones de navegadores", Name = name, Location = b[0] + ", perfil " + Path.GetFileName(profile), Path = version };
                            it.RegValue = idName;
                            string update = Regex.Match(json, "\"update_url\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
                            bool store = update.Contains("clients2.google.com") || update.Contains("edge.microsoft.com");
                            bool wide = json.Contains("<all_urls>") || json.Contains("*://*/*") || json.Contains("http://*/*") || json.Contains("https://*/*");
                            if (update.Length > 0 && !store) it.Flag(Sev.Suspicious, "Se actualiza desde fuera de la tienda oficial: " + update);
                            else if (update.Length == 0) it.Flag(Sev.Review, "No viene de la tienda oficial o se instaló a mano.");
                            if (wide && it.Severity == Sev.Normal) it.Flag(Sev.Review, "Puede leer y cambiar lo que ves en todas las páginas. Confirma que el cliente la reconoce.");
                            it.Advice = "Para quitarla, abre " + b[0] + " y entra en la página de extensiones.";
                            r.Items.Add(it);
                        }
                    }
                using (var k = Registry.LocalMachine.OpenSubKey(b[2] + @"\ExtensionInstallForcelist"))
                    if (k != null)
                        foreach (var v in k.GetValueNames())
                            Manual(r, b[0] + ": extensión impuesta", "Directivas del navegador", Convert.ToString(k.GetValue(v)), Sev.Suspicious, "Una directiva instala esta extensión a la fuerza y no deja quitarla.", "En un equipo que no sea de empresa, borra esta directiva del registro y luego quita la extensión.");
            }

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string ff = Path.Combine(roaming, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ff))
                foreach (var profile in Directory.GetDirectories(ff))
                {
                    string f = Path.Combine(profile, "extensions.json");
                    if (!File.Exists(f)) continue;
                    string json = File.ReadAllText(f, Encoding.UTF8);
                    foreach (Match m in Regex.Matches(json, "\"id\":\"([^\"]+)\".{0,2500}?\"location\":\"app-profile\"", RegexOptions.Singleline))
                    {
                        string body = m.Value;
                        string name = Regex.Match(body, "\"name\":\"([^\"]+)\"").Groups[1].Value;
                        var it = new Item { Id = "ext|firefox|" + m.Groups[1].Value, Kind = ItemKind.Extension, Group = "Extensiones de navegadores", Name = name.Length > 0 ? name : m.Groups[1].Value, Location = "Firefox, " + Path.GetFileName(profile), Advice = "Para quitarla, abre Firefox y entra en Complementos." };
                        r.Items.Add(it);
                    }
                }
        }

        static string ExtName(string json, string dir)
        {
            string name = Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            var msg = Regex.Match(name, "^__MSG_(.+)__$");
            if (msg.Success)
                foreach (var loc in new[] { "es", "es_419", "en", "en_US" })
                    try
                    {
                        string f = Path.Combine(dir, "_locales", loc, "messages.json");
                        if (!File.Exists(f)) continue;
                        var v = Regex.Match(File.ReadAllText(f, Encoding.UTF8), "\"" + Regex.Escape(msg.Groups[1].Value) + "\"\\s*:\\s*\\{.*?\"message\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        if (v.Success) return v.Groups[1].Value;
                    }
                    catch { }
            return name.Length > 0 ? name : Path.GetFileName(Path.GetDirectoryName(dir));
        }

        static void Shortcuts(ScanResult r)
        {
            string[] browsers = { "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "iexplore.exe" };
            var dirs = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar")
            };
            foreach (var d in dirs.Where(x => !string.IsNullOrEmpty(x) && Directory.Exists(x)))
                foreach (var f in Directory.GetFiles(d, "*.lnk"))
                {
                    string t, a;
                    ReadLink(f, out t, out a);
                    string file = "";
                    try { file = Path.GetFileName(t).ToLowerInvariant(); } catch { }
                    if (!browsers.Contains(file)) continue;
                    if (Regex.IsMatch(a, @"https?://|--load-extension|--proxy-server", RegexOptions.IgnoreCase))
                    {
                        var it = new Item { Id = "lnk|" + f, Kind = ItemKind.Shortcut, Group = "Accesos directos", Name = Path.GetFileName(f), Location = f, RegValue = a, Path = t, Advice = "Abre las propiedades del acceso directo y quita lo añadido después de la ruta del navegador, o créalo de nuevo." };
                        it.Flag(Sev.Suspicious, "El acceso directo del navegador abre una página o cambia su configuración: " + a);
                        r.Items.Add(it);
                    }
                }
        }

        static void Network(ScanResult r)
        {
            string hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            if (File.Exists(hosts))
            {
                var bad = new List<string>();
                foreach (var raw in File.ReadAllLines(hosts))
                {
                    string l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#")) continue;
                    if (Regex.IsMatch(l, @"^(127\.0\.0\.1|::1)\s+localhost\b", RegexOptions.IgnoreCase)) continue;
                    bad.Add(l);
                }
                if (bad.Count > 0)
                {
                    bool critical = bad.Any(b => Regex.IsMatch(b, @"microsoft|windowsupdate|google|bing|facebook|banco|bank|paypal|kaspersky|avast|eset|mcafee|symantec|norton|malwarebytes", RegexOptions.IgnoreCase));
                    Manual(r, "Archivo hosts modificado", hosts, string.Join("\n", bad), critical ? Sev.Suspicious : Sev.Review, critical ? "Redirige o bloquea páginas conocidas, de seguridad o de bancos." : bad.Count + " direcciones añadidas a mano o por un programa.", "Abre el archivo como administrador y borra las líneas que no reconozcas.");
                }
            }
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                if (k != null)
                {
                    bool on = Convert.ToInt32(k.GetValue("ProxyEnable") ?? 0) != 0;
                    string server = Convert.ToString(k.GetValue("ProxyServer") ?? ""), pac = Convert.ToString(k.GetValue("AutoConfigURL") ?? "");
                    if (on) Manual(r, "Proxy activo", "Configuración de internet", server, Sev.Review, "El tráfico web pasa por otro equipo.", "Si el cliente no usa un proxy de empresa, quítalo en Configuración, Red, Proxy, o con ReparadorPC.");
                    if (pac.Length > 0) Manual(r, "Script de proxy", "Configuración de internet", pac, Sev.Suspicious, "Un script externo decide por dónde pasa el tráfico web.", "Si no es de la empresa, quítalo en Configuración, Red, Proxy.");
                }
        }

        static void Defender(ScanResult r)
        {
            var av = new List<string>();
            bool activeOther = false;
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName,productState FROM AntiVirusProduct"))
                    foreach (ManagementObject m in s.Get())
                    {
                        string n = Convert.ToString(m["displayName"]);
                        int st = Convert.ToInt32(m["productState"]);
                        bool on = ((st >> 12) & 0xF) == 1;
                        av.Add(n + (on ? " (activo)" : " (inactivo)"));
                        if (on && n.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) < 0) activeOther = true;
                    }
            }
            catch { }
            string o;
            int code = Cmd.Run("powershell", "-NoProfile -NonInteractive -Command \"$s=Get-MpComputerStatus;$p=Get-MpPreference;'RT='+$s.RealTimeProtectionEnabled;'AV='+$s.AntivirusEnabled;'AGE='+$s.AntivirusSignatureAge;'EXP='+(($p.ExclusionPath+$p.ExclusionProcess+$p.ExclusionExtension)-join'|')\"", 30000, out o);
            var line = new Func<string, string>(key => Regex.Match(o ?? "", "(?m)^" + key + "=(.*)$").Groups[1].Value.Trim());
            string rt = line("RT"), age = line("AGE"), exp = line("EXP");
            if (code != 0 || rt.Length == 0)
            {
                if (av.Count > 0) Manual(r, "Antivirus", "Centro de seguridad", string.Join(", ", av), Sev.Normal, "Antivirus registrados en Windows.", "");
                else r.Gaps.Add("Windows Defender: no se pudo leer su estado.");
                return;
            }
            if (rt.Equals("False", StringComparison.OrdinalIgnoreCase) && !activeOther)
                Manual(r, "Protección en tiempo real apagada", "Windows Defender", string.Join(", ", av), Sev.Suspicious, "No hay ningún antivirus activo. Algunos virus apagan la protección.", "Actívala en Seguridad de Windows.");
            int days;
            if (int.TryParse(age, out days) && days > 14 && !activeOther)
                Manual(r, "Antivirus desactualizado", "Windows Defender", days + " días", Sev.Review, "Las definiciones de virus tienen " + days + " días.", "Actualízalas en Seguridad de Windows antes de analizar.");
            // Sin administrador, Windows oculta la lista con un texto en vez de las rutas.
            if (exp.Length > 0 && !exp.Contains("N/A") && !exp.StartsWith("{") && !exp.Contains("Must be"))
                Manual(r, "Exclusiones de Defender", "Windows Defender", exp.Replace("|", "\n"), Sev.Review, "Defender no analiza estas rutas. Un virus puede añadirse a sí mismo aquí.", "Quita las que no reconozcas en Seguridad de Windows, Exclusiones.");
        }
    }

    public class Change
    {
        public string Id = "", Name = "", Kind = "", A = "", B = "", Old = "", Date = "";
    }

    // Nada se borra: cada cambio se anota aquí para poder deshacerlo.
    public static class Actions
    {
        const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

        static string LogPath
        {
            get
            {
                string forced = Environment.GetEnvironmentVariable("LIMPIEZA_DATOS");
                string dir = !string.IsNullOrEmpty(forced) ? forced : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LimpiezaPC");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "cambios_" + Environment.UserName + ".tsv");
            }
        }

        public static List<Change> Load()
        {
            var list = new List<Change>();
            try
            {
                if (!File.Exists(LogPath)) return list;
                foreach (var l in File.ReadAllLines(LogPath, Encoding.UTF8))
                {
                    var c = l.Split('\t');
                    if (c.Length >= 7) list.Add(new Change { Id = D(c[0]), Name = D(c[1]), Kind = c[2], A = D(c[3]), B = D(c[4]), Old = D(c[5]), Date = c[6] });
                }
            }
            catch { }
            return list;
        }

        static string E(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }
        static string D(string s) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch { return ""; } }

        static void Save(List<Change> list)
        {
            File.WriteAllLines(LogPath, list.Select(c => string.Join("\t", E(c.Id), E(c.Name), c.Kind, E(c.A), E(c.B), E(c.Old), c.Date)), new UTF8Encoding(false));
        }

        static void Record(Change c)
        {
            var list = Load();
            list.RemoveAll(x => x.Id == c.Id);
            c.Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            list.Add(c);
            Save(list);
        }

        public static string Disable(Item it)
        {
            switch (it.Kind)
            {
                case ItemKind.Startup: return DisableStartup(it);
                case ItemKind.Task: return SetTask(it, false);
                case ItemKind.Service: return DisableService(it);
                default: throw new InvalidOperationException("Esto se corrige a mano.");
            }
        }

        static string DisableStartup(Item it)
        {
            string hive = it.Hive == Registry.LocalMachine ? "HKLM" : "HKCU";
            if (it.Approved.Length > 0)
            {
                // Es lo mismo que hace el Administrador de tareas: la entrada sigue ahí, sin ejecutarse.
                using (var k = it.Hive.CreateSubKey(ApprovedBase + it.Approved))
                {
                    var old = k.GetValue(it.RegValue) as byte[];
                    var off = new byte[12];
                    off[0] = 3;
                    BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(off, 4);
                    k.SetValue(it.RegValue, off, RegistryValueKind.Binary);
                    Record(new Change { Id = it.Id, Name = it.Name, Kind = "approved", A = hive + "|" + it.Approved, B = it.RegValue, Old = old == null ? "" : Convert.ToBase64String(old) });
                }
                return "Desactivado. Ya no arrancará con Windows; puedes deshacerlo.";
            }
            // RunOnce no admite desactivar: se saca el valor, que queda guardado para devolverlo.
            using (var k = it.Hive.OpenSubKey(it.RegKey, true))
            {
                string value = Convert.ToString(k.GetValue(it.RegValue, "", RegistryValueOptions.DoNotExpandEnvironmentNames));
                k.DeleteValue(it.RegValue, false);
                Record(new Change { Id = it.Id, Name = it.Name, Kind = "runonce", A = hive + "|" + it.RegKey, B = it.RegValue, Old = value });
            }
            return "Quitado del inicio. Queda guardado por si quieres devolverlo.";
        }

        static string SetTask(Item it, bool enabled)
        {
            dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            service.Connect();
            int cut = it.RegKey.LastIndexOf('\\');
            dynamic folder = service.GetFolder(cut <= 0 ? "\\" : it.RegKey.Substring(0, cut));
            dynamic task = folder.GetTask(it.RegKey.Substring(cut + 1));
            task.Enabled = enabled;
            if (!enabled) Record(new Change { Id = it.Id, Name = it.Name, Kind = "task", A = it.RegKey });
            return enabled ? "Tarea activada." : "Tarea desactivada. Puedes deshacerlo.";
        }

        static string DisableService(Item it)
        {
            if (!Env.Admin()) throw new UnauthorizedAccessException("Desactivar un servicio requiere administrador.");
            string o;
            int code = Cmd.Run("sc", "config \"" + it.RegKey + "\" start= disabled", 30000, out o);
            if (code != 0) throw new InvalidOperationException("Windows no permitió cambiarlo (código " + code + ").");
            Record(new Change { Id = it.Id, Name = it.Name, Kind = "service", A = it.RegKey, Old = it.RegValue });
            try
            {
                using (var s = new ServiceController(it.RegKey))
                    if (s.Status != ServiceControllerStatus.Stopped && s.CanStop)
                    {
                        s.Stop();
                        s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
            }
            catch { return "Desactivado para el próximo arranque, pero sigue en marcha hasta reiniciar."; }
            return "Servicio detenido y desactivado. Puedes deshacerlo.";
        }

        public static string Undo(Change c)
        {
            switch (c.Kind)
            {
                case "approved":
                    var pa = c.A.Split('|');
                    var hive = pa[0] == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                    using (var k = hive.OpenSubKey(ApprovedBase + pa[1], true))
                    {
                        if (k != null)
                        {
                            if (c.Old.Length > 0) k.SetValue(c.B, Convert.FromBase64String(c.Old), RegistryValueKind.Binary);
                            else k.DeleteValue(c.B, false);
                        }
                    }
                    break;
                case "runonce":
                    var pr = c.A.Split('|');
                    using (var k = (pr[0] == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser).OpenSubKey(pr[1], true))
                        k.SetValue(c.B, c.Old, RegistryValueKind.ExpandString);
                    break;
                case "task":
                    SetTask(new Item { RegKey = c.A }, true);
                    break;
                case "service":
                    if (!Env.Admin()) throw new UnauthorizedAccessException("Requiere administrador.");
                    string mode = c.Old.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? "auto" : c.Old.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? "disabled" : "demand";
                    string o;
                    int code = Cmd.Run("sc", "config \"" + c.A + "\" start= " + mode, 30000, out o);
                    if (code != 0) throw new InvalidOperationException("Windows no permitió cambiarlo (código " + code + ").");
                    break;
            }
            var list = Load();
            list.RemoveAll(x => x.Id == c.Id);
            Save(list);
            return "Restaurado: " + c.Name;
        }
    }

    public static class Tools
    {
        public static string Sha256(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        public static string FindMpCmdRun()
        {
            try
            {
                string plat = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows Defender\Platform");
                if (Directory.Exists(plat))
                {
                    var best = Directory.GetDirectories(plat).OrderByDescending(x => x).Select(x => Path.Combine(x, "MpCmdRun.exe")).FirstOrDefault(File.Exists);
                    if (best != null) return best;
                }
            }
            catch { }
            string pf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");
            return File.Exists(pf) ? pf : null;
        }

        // Devuelve el resultado en lenguaje claro. full = análisis completo.
        public static string DefenderScan(bool full, Func<bool> cancelled, out bool threats)
        {
            threats = false;
            string exe = FindMpCmdRun();
            if (exe == null) return "Este equipo no tiene Windows Defender disponible. Usa el antivirus que tenga instalado.";
            var psi = new ProcessStartInfo(exe, "-Scan -ScanType " + (full ? "2" : "1")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var p = Process.Start(psi))
            {
                var a = new System.Threading.Thread(() => { try { p.StandardOutput.ReadToEnd(); } catch { } }) { IsBackground = true };
                var b = new System.Threading.Thread(() => { try { p.StandardError.ReadToEnd(); } catch { } }) { IsBackground = true };
                a.Start(); b.Start();
                while (!p.WaitForExit(1000))
                    if (cancelled != null && cancelled())
                    {
                        string o;
                        try { Cmd.Run(exe, "-Scan -Cancel", 20000, out o); } catch { }
                        try { if (!p.WaitForExit(5000)) p.Kill(); } catch { }
                        return "Análisis cancelado.";
                    }
                switch (p.ExitCode)
                {
                    case 0: return "Windows Defender terminó y no encontró amenazas.";
                    case 2:
                        threats = true;
                        return "Windows Defender encontró amenazas. Ábrelas en Seguridad de Windows, Protección contra virus y amenazas, Historial de protección.";
                    default: return "Windows Defender no pudo completar el análisis (código " + p.ExitCode + "). Puede estar desactivado por otro antivirus.";
                }
            }
        }
    }

    public static class Report
    {
        static string E(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        public static string Word(Sev s) { return s == Sev.Suspicious ? "Sospechoso" : s == Sev.Review ? "Revisar" : "Normal"; }

        public static string Verdict(ScanResult r, out string detail, out string color)
        {
            int sus = r.Items.Count(i => i.Severity == Sev.Suspicious && !i.Disabled), rev = r.Items.Count(i => i.Severity == Sev.Review && !i.Disabled);
            detail = sus + " sospechosos, " + rev + " para revisar, de " + r.Items.Count + " elementos revisados." + (r.Gaps.Count > 0 ? " Partes sin revisar del todo: " + r.Gaps.Count + "." : "");
            if (sus > 0) { color = "#c62828"; return sus == 1 ? "Hay un elemento sospechoso" : "Hay " + sus + " elementos sospechosos"; }
            if (rev > 0) { color = "#a86200"; return rev == 1 ? "Hay un elemento por revisar" : "Hay " + rev + " elementos por revisar"; }
            color = "#1b7f4b";
            // No se afirma que el equipo esté limpio: esta revisión no reemplaza a un antivirus.
            return "No se encontró nada sospechoso en el arranque";
        }

        public static string Page(ScanResult r, string defender, Comun.AppInfo info)
        {
            string detail, color;
            string verdict = Verdict(r, out detail, out color);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=es><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Revisión de " + E(Environment.MachineName) + "</title><style>");
            sb.Append("body{margin:0;background:#eef1f3;color:#1e2a32;font:15px/1.5 'Segoe UI',system-ui,sans-serif}.band{color:#fff;padding:24px}.band h1{font-size:25px;line-height:1.2;margin:0 0 6px;font-weight:600}.band p{margin:0;max-width:72ch}");
            sb.Append("main{max-width:900px;margin:0 auto;padding:8px 16px 40px}h2{font-size:18px;font-weight:600;margin:26px 0 8px}table{border-collapse:collapse;width:100%;background:#fff;font-size:14px}td,th{padding:6px 10px;border-bottom:1px solid #d5dbe0;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{font-weight:600}.w{overflow-x:auto}code{font:12.5px Consolas,monospace}li{max-width:72ch;margin:4px 0}");
            sb.Append("@media print{body{background:#fff}.band{-webkit-print-color-adjust:exact;print-color-adjust:exact}}</style>");
            sb.Append("<div class=band style='background:" + color + "'><h1>" + E(verdict) + "</h1><p>" + E(detail) + "</p><p>Equipo " + E(Environment.MachineName) + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".</p></div><main>");
            sb.Append("<p>Esta revisión busca señales en lo que arranca con Windows. No sustituye a un antivirus: «sospechoso» no prueba que haya un virus, y «normal» no prueba que no lo haya.</p>");
            if (!string.IsNullOrEmpty(defender)) sb.Append("<h2>Análisis con Windows Defender</h2><p>" + E(defender) + "</p>");
            foreach (var sev in new[] { Sev.Suspicious, Sev.Review })
            {
                var rows = r.Items.Where(i => i.Severity == sev).ToList();
                if (rows.Count == 0) continue;
                sb.Append("<h2>" + (sev == Sev.Suspicious ? "Sospechosos" : "Para revisar") + " (" + rows.Count + ")</h2><div class=w><table><tr><th>Elemento</th><th>Motivo</th><th>Detalle</th></tr>");
                foreach (var i in rows)
                    sb.Append("<tr><td>" + E(i.Name) + (i.Disabled ? " (desactivado)" : "") + "<br><small>" + E(i.Group) + "</small></td><td>" + E(string.Join(" ", i.Reasons)) + (i.Advice.Length > 0 ? "<br><b>Qué hacer:</b> " + E(i.Advice) : "") + "</td><td><code>" + E(i.Command.Length > 0 ? i.Command : i.RegValue) + "</code>" + (i.Signer.Length > 0 ? "<br>Firma: " + E(i.Signer) : "") + "</td></tr>");
                sb.Append("</table></div>");
            }
            if (r.Gaps.Count > 0)
            {
                sb.Append("<h2>Lo que no se pudo revisar</h2><ul>");
                foreach (var g in r.Gaps) sb.Append("<li>" + E(g) + "</li>");
                sb.Append("</ul>");
            }
            sb.Append(Comun.Credits.HtmlFooter(info));
            sb.Append("</main></html>");
            return sb.ToString();
        }
    }
}
