using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ReparadorPC
{
    public enum St { None, Running, Ok, Warn, Fail, Skipped }

    public static class Env
    {
        // En pruebas no se ejecuta ningún comando ni se toca ningún archivo del sistema.
        public static bool TestMode { get { return Environment.GetEnvironmentVariable("REPARADOR_PRUEBA") == "1"; } }

        public static bool Admin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        public static string Windows { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }
        public static string SystemDrive { get { return Path.GetPathRoot(Windows).TrimEnd('\\'); } }
        public static Encoding Oem
        {
            get
            {
                try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
                catch { return Encoding.Default; }
            }
        }

        public static bool RebootPending()
        {
            foreach (var key in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
                                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired" })
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(key))
                        if (k != null) return true;
                }
                catch { }
            return false;
        }
    }

    public class ToolRun
    {
        Process proc;
        public volatile string LastLine = "";

        public int Run(string file, string args, int timeoutMs, Encoding encoding, out string output)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = encoding ?? Env.Oem
            };
            var sb = new StringBuilder();
            using (var p = Process.Start(psi))
            {
                proc = p;
                var reader = new Thread(() =>
                {
                    try
                    {
                        var line = new StringBuilder();
                        int c;
                        while ((c = p.StandardOutput.Read()) >= 0)
                        {
                            if (c == 0) continue;
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
                var err = new Thread(() => { try { sb.Append(p.StandardError.ReadToEnd()); } catch { } }) { IsBackground = true };
                err.Start();
                bool finished = p.WaitForExit(timeoutMs);
                if (!finished) Kill();
                reader.Join(3000);
                err.Join(1000);
                output = sb.ToString();
                proc = null;
                return finished ? p.ExitCode : -2;
            }
        }

        public void Kill()
        {
            var p = proc;
            if (p == null) return;
            try
            {
                var psi = new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id) { UseShellExecute = false, CreateNoWindow = true };
                using (var k = Process.Start(psi)) k.WaitForExit(10000);
            }
            catch { }
        }
    }

    public class Repair
    {
        public string Key, Group, Label, Why, Note, Duration;
        public bool NeedsAdmin = true, NeedsReboot, Selected;
        public volatile St Result;
        public string Detail = "";
        public readonly StringBuilder Log = new StringBuilder();
        public Action<Repair, Ctx> Run;
        public object Node;
    }

    public class Ctx
    {
        public volatile bool Cancel;
        public volatile Repair Current;
        ToolRun tool;
        public string Line { get { var t = tool; return t == null ? "" : t.LastLine; } }

        public void Stop()
        {
            Cancel = true;
            var t = tool;
            if (t != null) t.Kill();
        }

        public int Exec(Repair r, string file, string args, int timeoutMinutes, out string output, Encoding enc = null)
        {
            r.Log.AppendLine("> " + file + " " + args);
            if (Env.TestMode) { output = "(simulado)"; r.Log.AppendLine("(simulado)"); return 0; }
            var t = new ToolRun();
            tool = t;
            int code;
            try { code = t.Run(file, args, timeoutMinutes * 60000, enc, out output); }
            catch (Exception ex)
            {
                output = ex.Message;
                r.Log.AppendLine("No se pudo iniciar: " + ex.Message);
                return -1;
            }
            string shown = Regex.Replace(output ?? "", @"[^\r\n]*\d+(\.\d+)?\s*%[^\r\n]*\r", "");
            if (shown.Length > 6000) shown = "…" + shown.Substring(shown.Length - 6000);
            r.Log.AppendLine(shown.Trim());
            r.Log.AppendLine("Código de salida: " + code);
            return code;
        }

        public bool StopService(Repair r, string name)
        {
            r.Log.AppendLine("Deteniendo servicio " + name);
            if (Env.TestMode) return true;
            try
            {
                using (var s = new ServiceController(name))
                {
                    if (s.Status == ServiceControllerStatus.Stopped) return true;
                    if (s.Status != ServiceControllerStatus.StopPending) s.Stop();
                    s.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                    return true;
                }
            }
            catch (Exception ex)
            {
                r.Log.AppendLine("No se detuvo " + name + ": " + ex.Message);
                return false;
            }
        }

        public bool StartService(Repair r, string name)
        {
            r.Log.AppendLine("Iniciando servicio " + name);
            if (Env.TestMode) return true;
            try
            {
                using (var s = new ServiceController(name))
                {
                    if (s.Status == ServiceControllerStatus.Running) return true;
                    if (s.Status != ServiceControllerStatus.StartPending) s.Start();
                    s.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
                    return true;
                }
            }
            catch (Exception ex)
            {
                r.Log.AppendLine("No se inició " + name + ": " + ex.Message);
                return false;
            }
        }
    }

    public static class Repairs
    {
        const string G_System = "Archivos de Windows", G_Update = "Windows Update", G_Net = "Internet y red", G_Print = "Impresoras", G_Other = "Otros arreglos";

        public static List<Repair> All()
        {
            return new List<Repair>
            {
                new Repair { Key = "punto", Group = "Antes de empezar", Label = "Crear un punto de restauración", Why = "Permite volver atrás si una reparación no sale bien.", Duration = "1 min", Selected = true, Run = RestorePoint },

                new Repair { Key = "dism", Group = G_System, Label = "Reparar la imagen de Windows (DISM)", Why = "Recupera componentes dañados de Windows. Va antes de SFC porque SFC los usa de fuente.", Note = "Puede descargar archivos de Windows Update.", Duration = "10 a 30 min", Run = Dism },
                new Repair { Key = "sfc", Group = G_System, Label = "Reparar archivos del sistema (SFC)", Why = "Revisa los archivos protegidos de Windows y repone los dañados.", Duration = "10 a 20 min", Run = Sfc },
                new Repair { Key = "chkscan", Group = G_System, Label = "Revisar errores del disco sin reiniciar", Why = "Busca errores en el sistema de archivos mientras Windows sigue funcionando.", Duration = "2 a 10 min", Run = ChkScan },
                new Repair { Key = "chkfix", Group = G_System, Label = "Programar la reparación del disco al reiniciar", Why = "Corrige errores del sistema de archivos antes de que Windows arranque.", Note = "El próximo arranque tardará más. Haz una copia si el disco ya da errores.", Duration = "Al reiniciar", NeedsReboot = true, Run = ChkFix },

                new Repair { Key = "wureset", Group = G_Update, Label = "Reiniciar los componentes de Windows Update", Why = "Arregla actualizaciones que se atascan o fallan una y otra vez.", Note = "Vacía el historial visible de actualizaciones. Las carpetas anteriores se conservan con otro nombre.", Duration = "2 min", Run = WuReset },

                new Repair { Key = "dns", Group = G_Net, Label = "Limpiar DNS y renovar la IP", Why = "Arregla páginas que no cargan o direcciones antiguas. Sin riesgo.", Duration = "Menos de 1 min", Run = Dns },
                new Repair { Key = "proxy", Group = G_Net, Label = "Quitar el proxy configurado", Why = "Un proxy dejado por un virus o un programa puede cortar internet.", Note = "No lo uses si la empresa requiere un proxy.", Duration = "Menos de 1 min", Run = Proxy },
                new Repair { Key = "netreset", Group = G_Net, Label = "Restablecer la red de Windows (Winsock e IP)", Why = "Último recurso cuando hay adaptador pero no navega.", Note = "Borra las IP fijas y puede afectar a VPN. Sus datos se guardan en el informe.", Duration = "Al reiniciar", NeedsReboot = true, Run = NetReset },

                new Repair { Key = "spooler", Group = G_Print, Label = "Reiniciar el servicio de impresión", Why = "Arregla impresoras que no responden. Conserva la cola.", Duration = "Menos de 1 min", Run = Spooler },
                new Repair { Key = "spoolclear", Group = G_Print, Label = "Vaciar la cola de impresión atascada", Why = "Elimina los trabajos que bloquean la impresora.", Note = "Se cancelan todos los trabajos pendientes.", Duration = "Menos de 1 min", Run = SpoolClear },

                new Repair { Key = "hora", Group = G_Other, Label = "Sincronizar la hora", Why = "Una hora incorrecta causa errores en páginas seguras y en actualizaciones.", Duration = "Menos de 1 min", Run = TimeSync },
                new Repair { Key = "audio", Group = G_Other, Label = "Reiniciar los servicios de sonido", Why = "Recupera el sonido cuando los servicios se quedan colgados.", Duration = "Menos de 1 min", Run = Audio },
                new Repair { Key = "store", Group = G_Other, Label = "Limpiar la caché de Microsoft Store", Why = "Arregla descargas de la Store que no avanzan. Al terminar abre la Store.", Duration = "1 min", Run = StoreReset },
                new Repair { Key = "explorer", Group = G_Other, Label = "Reiniciar el Explorador de Windows", Why = "Arregla la barra de tareas o el Escritorio colgados.", Note = "Cierra las ventanas de carpetas abiertas.", NeedsAdmin = false, Duration = "Menos de 1 min", Run = Explorer }
            };
        }

        public static readonly Dictionary<string, string[]> Symptoms = new Dictionary<string, string[]>
        {
            { "Errores o pantallazos azules", new[] { "punto", "dism", "sfc", "chkscan" } },
            { "Windows no actualiza", new[] { "punto", "hora", "wureset", "dism" } },
            { "Sin internet", new[] { "dns", "proxy" } },
            { "No imprime", new[] { "spooler", "spoolclear" } },
            { "No suena", new[] { "audio" } },
            { "La Store no funciona", new[] { "hora", "store" } },
            { "Barra de tareas colgada", new[] { "explorer", "sfc" } },
            { "Hora incorrecta", new[] { "hora" } }
        };

        static void Set(Repair r, St st, string detail) { r.Result = st; r.Detail = detail; }

        public static void RunAll(IEnumerable<Repair> repairs, Ctx ctx, bool admin)
        {
            foreach (var r in repairs)
            {
                if (ctx.Cancel) { Set(r, St.Skipped, "Detenido antes de empezar."); continue; }
                r.Log.Clear();
                if (r.NeedsAdmin && !admin && !Env.TestMode) { Set(r, St.Skipped, "Requiere permisos de administrador."); continue; }
                ctx.Current = r;
                r.Result = St.Running;
                r.Detail = "";
                try { r.Run(r, ctx); }
                catch (Exception ex) { Set(r, St.Fail, "No se pudo completar: " + ex.Message); r.Log.AppendLine(ex.ToString()); }
                if (ctx.Cancel && r.Result == St.Running) Set(r, St.Skipped, "Detenido a petición tuya.");
                if (ctx.Cancel && r.Result == St.Fail) Set(r, St.Skipped, "Detenido a petición tuya, antes de terminar.");
            }
            ctx.Current = null;
        }

        static void RestorePoint(Repair r, Ctx c)
        {
            string o;
            int code = c.Exec(r, "powershell", "-NoProfile -NonInteractive -Command \"$ErrorActionPreference='Stop'; Enable-ComputerRestore -Drive $env:SystemDrive; Checkpoint-Computer -Description 'Antes de Reparador PC' -RestorePointType MODIFY_SETTINGS\"", 10, out o);
            if (code == 0) Set(r, St.Ok, "Punto de restauración listo. Si ya se creó otro hoy, Windows reutiliza ese.");
            else Set(r, St.Warn, "No se pudo crear. Puede estar desactivado en este equipo. Las reparaciones siguen sin él.");
        }

        // /English da mensajes fijos, sea cual sea el idioma de Windows.
        static void Dism(Repair r, Ctx c)
        {
            string o;
            int code = c.Exec(r, "dism", "/English /Online /Cleanup-Image /RestoreHealth", 90, out o);
            if (c.Cancel) return;
            if (code == 0) Set(r, St.Ok, "Imagen de Windows revisada: queda sin daños.");
            else if (code == -2) Set(r, St.Fail, "Tardó demasiado y se detuvo. Revisa internet y reintenta.");
            else if (o.Contains("0x800f081f") || o.Contains("0x800f0906") || o.Contains("0x800f0907") || code == 16385)
                Set(r, St.Fail, "No encontró de dónde descargar los archivos. Revisa internet y Windows Update, o repara con una ISO de la misma versión.");
            else if (code == 87) Set(r, St.Fail, "Esta versión de Windows no admite esta reparación.");
            else Set(r, St.Fail, "DISM terminó con el código " + code + ". Los detalles quedan en el informe y en C:\\Windows\\Logs\\DISM\\dism.log.");
        }

        public static void ParseSfc(string[] srLines, out int repaired, out int cannot)
        {
            repaired = srLines.Count(l => l.Contains("Repaired file") || l.Contains("Repairing corrupted file"));
            cannot = srLines.Count(l => l.Contains("Cannot repair member file"));
        }

        static void Sfc(Repair r, Ctx c)
        {
            DateTime start = DateTime.Now.AddMinutes(-1);
            string o;
            int code = c.Exec(r, "sfc", "/scannow", 90, out o, Encoding.Unicode);
            if (c.Cancel) return;
            if (Env.TestMode) { Set(r, St.Ok, "Simulado en modo de prueba."); return; }
            if (code == -2) { Set(r, St.Fail, "Tardó demasiado y se detuvo."); return; }

            // sfc responde en el idioma de Windows; CBS.log está siempre en inglés.
            string[] lines = null;
            try
            {
                string path = Path.Combine(Env.Windows, @"Logs\CBS\CBS.log");
                var found = new List<string>();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs))
                {
                    string l;
                    while ((l = sr.ReadLine()) != null)
                    {
                        if (!l.Contains("[SR]")) continue;
                        DateTime when;
                        if (l.Length > 19 && DateTime.TryParse(l.Substring(0, 19), CultureInfo.InvariantCulture, DateTimeStyles.None, out when) && when >= start)
                            found.Add(l);
                    }
                }
                lines = found.ToArray();
            }
            catch (Exception ex) { r.Log.AppendLine("No se pudo leer CBS.log: " + ex.Message); }

            if (lines == null || lines.Length == 0)
            {
                bool looksOk = code == 0;
                Set(r, looksOk ? St.Ok : St.Warn, looksOk ? "SFC terminó sin informar problemas." : "SFC terminó con el código " + code + ", sin detalle en el registro. Reinicia y vuelve a ejecutarlo.");
                return;
            }
            int repaired, cannot;
            ParseSfc(lines, out repaired, out cannot);
            if (cannot > 0)
                Set(r, St.Fail, "Hay " + cannot + " archivos que SFC no pudo reparar. Ejecuta primero la reparación DISM, reinicia y repite SFC.");
            else if (repaired > 0)
                Set(r, St.Ok, "Encontró y reparó " + repaired + " archivos dañados. Reinicia el equipo.");
            else
                Set(r, St.Ok, "Revisado: no hay archivos del sistema dañados.");
        }

        static void ChkScan(Repair r, Ctx c)
        {
            string o;
            int code = c.Exec(r, "chkdsk", Env.SystemDrive + " /scan", 60, out o);
            if (c.Cancel) return;
            switch (code)
            {
                case 0: Set(r, St.Ok, "El sistema de archivos no tiene errores."); break;
                case 1: Set(r, St.Ok, "Encontró errores y los corrigió."); break;
                case 2: Set(r, St.Ok, "Hizo limpieza menor, sin errores graves."); break;
                case 3: Set(r, St.Warn, "Quedan errores. Programa la reparación del disco al reiniciar."); break;
                default: Set(r, St.Warn, "chkdsk terminó con el código " + code + ". Revisa el detalle en el informe."); break;
            }
        }

        static void ChkFix(Repair r, Ctx c)
        {
            string o;
            int code = c.Exec(r, "cmd", "/c echo S|chkdsk " + Env.SystemDrive + " /f & echo Y|chkdsk " + Env.SystemDrive + " /f", 5, out o);
            if (c.Cancel) return;
            string dirty;
            int q = c.Exec(r, "chkntfs", Env.SystemDrive, 2, out dirty);
            if (Env.TestMode || code == 0 || q == 0) Set(r, St.Ok, "Reparación programada. Se hará en el próximo reinicio.");
            else Set(r, St.Fail, "No se pudo programar (código " + code + ").");
        }

        static readonly string[] WuServices = { "wuauserv", "bits", "cryptsvc", "msiserver" };

        // Pase lo que pase, los servicios vuelven a iniciarse.
        static void WuReset(Repair r, Ctx c)
        {
            if (Env.RebootPending() && !Env.TestMode)
            {
                Set(r, St.Warn, "Windows tiene un reinicio pendiente. Reinicia primero: tocar estas carpetas ahora podría dañar la actualización en curso.");
                return;
            }
            var problems = new List<string>();
            foreach (var s in WuServices)
                if (!c.StopService(r, s)) problems.Add(s);
            try
            {
                if (problems.Count > 0)
                {
                    Set(r, St.Fail, "No se pudieron detener estos servicios: " + string.Join(", ", problems) + ". Reinicia el equipo y reintenta.");
                    return;
                }
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string[] folders = { Path.Combine(Env.Windows, "SoftwareDistribution"), Path.Combine(Env.Windows, @"System32\catroot2") };
                foreach (var f in folders)
                {
                    string target = f + ".anterior_" + stamp;
                    r.Log.AppendLine("Renombrando " + f + " a " + target);
                    if (Env.TestMode) continue;
                    if (!Directory.Exists(f)) continue;
                    try { Directory.Move(f, target); }
                    catch (Exception ex)
                    {
                        Set(r, St.Fail, "No se pudo renombrar " + f + ": " + ex.Message + " Reinicia el equipo y reintenta.");
                        return;
                    }
                }
                Set(r, St.Ok, "Listo. Busca actualizaciones de nuevo en Windows Update. Si todo va bien, puedes borrar las carpetas «.anterior_» de C:\\Windows.");
            }
            finally
            {
                foreach (var s in new[] { "cryptsvc", "bits", "wuauserv" })
                    if (!c.StartService(r, s) && r.Result == St.Ok)
                    {
                        Set(r, St.Warn, "Reparado, pero el servicio " + s + " no volvió a iniciarse. Reinicia el equipo.");
                    }
            }
        }

        static void Dns(Repair r, Ctx c)
        {
            string o;
            int a = c.Exec(r, "ipconfig", "/flushdns", 2, out o);
            if (c.Cancel) return;
            int b = c.Exec(r, "ipconfig", "/renew", 3, out o);
            if (a != 0) Set(r, St.Fail, "No se pudo limpiar la caché de DNS (código " + a + ").");
            else if (b != 0) Set(r, St.Warn, "DNS limpio, pero no se renovó la IP. Es normal con IP fija o sin cable conectado.");
            else Set(r, St.Ok, "DNS limpio e IP renovada.");
        }

        const string ProxyKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        static string ProxyRoot { get { return Env.TestMode ? @"Software\ReparadorPrueba\" : ""; } }

        static void Proxy(Repair r, Ctx c)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(ProxyRoot + ProxyKey))
            {
                object enabled = k.GetValue("ProxyEnable");
                string server = Convert.ToString(k.GetValue("ProxyServer") ?? "");
                r.Log.AppendLine("Antes: activado=" + (enabled ?? "sin definir") + ", servidor=" + server);
                k.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                string o;
                int code = c.Exec(r, "netsh", "winhttp reset proxy", 2, out o);
                string was = Convert.ToInt32(enabled ?? 0) != 0 ? "Se quitó el proxy " + server + "." : "No había proxy del usuario activo.";
                Set(r, code == 0 ? St.Ok : St.Warn, was + (code == 0 ? "" : " No se pudo reiniciar el proxy del sistema (código " + code + ")."));
            }
        }

        static void NetReset(Repair r, Ctx c)
        {
            string o;
            c.Exec(r, "ipconfig", "/all", 1, out o);
            int a = c.Exec(r, "netsh", "winsock reset", 2, out o);
            if (c.Cancel) return;
            int b = c.Exec(r, "netsh", "int ip reset", 2, out o);
            if (a == 0 && b == 0) Set(r, St.Ok, "Red restablecida. Reinicia el equipo. Si tenía IP fija, vuelve a ponerla: está en este informe.");
            else if (a == 0 || b == 0) Set(r, St.Warn, "Se restableció solo una parte. Reinicia y comprueba la conexión.");
            else Set(r, St.Fail, "No se pudo restablecer. Hazlo desde Configuración, Red, Restablecer red.");
        }

        static void Spooler(Repair r, Ctx c)
        {
            bool down = c.StopService(r, "Spooler");
            bool up = c.StartService(r, "Spooler");
            if (down && up) Set(r, St.Ok, "Servicio de impresión reiniciado.");
            else Set(r, St.Fail, "El servicio de impresión no se reinició. Reinicia el equipo.");
        }

        static void SpoolClear(Repair r, Ctx c)
        {
            bool down = c.StopService(r, "Spooler");
            int removed = 0, stuck = 0;
            try
            {
                if (down && !Env.TestMode)
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\PRINTERS");
                    if (Directory.Exists(dir))
                        foreach (var f in Directory.GetFiles(dir))
                            try { File.Delete(f); removed++; }
                            catch { stuck++; }
                }
            }
            finally
            {
                bool up = c.StartService(r, "Spooler");
                if (!down || !up) Set(r, St.Fail, "El servicio de impresión no se reinició. Reinicia el equipo y reintenta.");
                else if (stuck > 0) Set(r, St.Warn, "Quedaron " + stuck + " archivos sin borrar. Reinicia el equipo y reintenta.");
                else Set(r, St.Ok, removed == 0 ? "La cola ya estaba vacía." : "Cola vaciada: " + removed + " archivos eliminados.");
            }
        }

        static void TimeSync(Repair r, Ctx c)
        {
            if (!c.StartService(r, "w32time")) { Set(r, St.Fail, "No se pudo iniciar el servicio de hora."); return; }
            string o;
            int code = c.Exec(r, "w32tm", "/resync /force", 2, out o);
            if (code != 0)
            {
                c.Exec(r, "w32tm", "/config /update", 1, out o);
                Thread.Sleep(Env.TestMode ? 0 : 2000);
                code = c.Exec(r, "w32tm", "/resync /force", 2, out o);
            }
            Set(r, code == 0 ? St.Ok : St.Warn, code == 0 ? "Hora sincronizada." : "No se pudo sincronizar. Revisa internet, o ajusta la hora en Configuración.");
        }

        static void Audio(Repair r, Ctx c)
        {
            bool a = c.StopService(r, "Audiosrv");
            bool b = c.StopService(r, "AudioEndpointBuilder");
            bool d = c.StartService(r, "AudioEndpointBuilder");
            bool e = c.StartService(r, "Audiosrv");
            if (a && b && d && e) Set(r, St.Ok, "Servicios de sonido reiniciados.");
            else Set(r, St.Fail, "Algún servicio de sonido no se reinició. Reinicia el equipo.");
        }

        static void StoreReset(Repair r, Ctx c)
        {
            string o;
            int code = c.Exec(r, "wsreset.exe", "", 3, out o);
            if (c.Cancel) return;
            if (code == 0) Set(r, St.Ok, "Caché limpiada. Windows abrirá la Store.");
            else if (code == -2) Set(r, St.Warn, "Sigue en proceso, o se abrió la Store. Comprueba si ya funciona.");
            else Set(r, St.Warn, "No se pudo limpiar (código " + code + "). Este Windows puede no tener Store.");
        }

        static void Explorer(Repair r, Ctx c)
        {
            r.Log.AppendLine("Reiniciando explorer.exe");
            if (Env.TestMode) { Set(r, St.Ok, "Simulado en modo de prueba."); return; }
            string o;
            var old = new HashSet<int>(Process.GetProcessesByName("explorer").Select(p => p.Id));
            c.Exec(r, "taskkill", "/F /IM explorer.exe", 1, out o);

            // Un proceso que se está cerrando aún aparece en la lista: no debe contar como reiniciado.
            for (int i = 0; i < 20 && NewShells(old) == 0 && Process.GetProcessesByName("explorer").Any(p => old.Contains(p.Id)); i++) Thread.Sleep(250);

            // Windows a veces lo abre solo, con los permisos normales del usuario.
            if (!WaitForShell(old, 5000))
            {
                string shell = Path.Combine(Env.Windows, "explorer.exe");
                r.Log.AppendLine("Windows no lo abrió solo; se abre desde el programa.");
                // Abierto desde aquí heredaría los permisos de administrador: se abre sin ellos.
                if (Env.Admin()) c.Exec(r, "runas", "/trustlevel:0x20000 \"" + shell + "\"", 1, out o);
                else Process.Start(shell);
                if (!WaitForShell(old, 6000))
                {
                    r.Log.AppendLine("Último recurso: se abre directamente.");
                    Process.Start(shell);
                    WaitForShell(old, 6000);
                }
            }

            // Confirma que sigue abierto pasados unos segundos.
            Thread.Sleep(3000);
            if (NewShells(old) > 0) Set(r, St.Ok, "Explorador reiniciado.");
            else Set(r, St.Fail, "El Explorador no volvió a abrirse. Pulsa Ctrl+Mayús+Esc, Ejecutar nueva tarea, y escribe explorer.");
        }

        static int NewShells(HashSet<int> old)
        {
            return Process.GetProcessesByName("explorer").Count(p => !old.Contains(p.Id));
        }

        static bool WaitForShell(HashSet<int> old, int ms)
        {
            for (int waited = 0; waited < ms; waited += 250)
            {
                if (NewShells(old) > 0) return true;
                Thread.Sleep(250);
            }
            return NewShells(old) > 0;
        }
    }

    public class Check
    {
        public string Label, Detail = "", Tech = "";
        public St Result = St.None;
        public string[] Suggest = new string[0];
    }

    public static class Diagnosis
    {
        public static List<Check> Run(Action<string> status, bool admin)
        {
            var list = new List<Check>();
            Action<string, Action<Check>> add = (label, work) =>
            {
                var c = new Check { Label = label };
                if (status != null) status(label);
                try { work(c); }
                catch (Exception ex) { c.Result = St.Skipped; c.Detail = "No se pudo comprobar en este equipo."; c.Tech = ex.Message; }
                list.Add(c);
            };

            add("Reinicio pendiente", c =>
            {
                bool p = Env.RebootPending();
                c.Result = p ? St.Warn : St.Ok;
                c.Detail = p ? "Windows espera un reinicio. Reinicia antes de reparar." : "No hay reinicios pendientes.";
            });
            add("Espacio libre", c =>
            {
                var d = new DriveInfo(Env.SystemDrive);
                double pct = 100.0 * d.AvailableFreeSpace / d.TotalSize;
                c.Result = pct < 8 ? St.Warn : St.Ok;
                c.Detail = (d.AvailableFreeSpace / 1e9).ToString("0.#") + " GB libres (" + pct.ToString("0") + "%)." + (pct < 8 ? " Libera espacio: las reparaciones lo necesitan." : "");
            });
            add("Imagen de Windows", c =>
            {
                if (!admin) { c.Result = St.Skipped; c.Detail = "Requiere administrador."; return; }
                string o;
                int code = new ToolRun().Run("dism", "/English /Online /Cleanup-Image /CheckHealth", 180000, null, out o);
                c.Tech = o;
                if (o.Contains("No component store corruption detected")) { c.Result = St.Ok; c.Detail = "Windows no ha registrado daños."; }
                else if (o.Contains("repairable") && !o.Contains("not repairable")) { c.Result = St.Warn; c.Detail = "Tiene daños que se pueden reparar."; c.Suggest = new[] { "punto", "dism", "sfc" }; }
                else if (o.Contains("not repairable")) { c.Result = St.Fail; c.Detail = "Tiene daños que no se pueden reparar automáticamente. Conviene reinstalar Windows conservando los archivos."; }
                else { c.Result = St.Skipped; c.Detail = "No se pudo comprobar (código " + code + ")."; }
            });
            add("Disco del sistema", c =>
            {
                using (var s = new ManagementObjectSearcher("SELECT VolumeDirty FROM Win32_LogicalDisk WHERE DeviceID='" + Env.SystemDrive + "'"))
                    foreach (ManagementObject m in s.Get())
                    {
                        bool dirty = Convert.ToBoolean(m["VolumeDirty"] ?? false);
                        c.Result = dirty ? St.Warn : St.Ok;
                        c.Detail = dirty ? "Windows lo marcó para revisar por errores." : "Sin marca de errores.";
                        if (dirty) c.Suggest = new[] { "chkscan", "chkfix" };
                    }
            });
            add("Windows Update", c =>
            {
                var w = ServiceState("wuauserv");
                int failures = 0;
                try
                {
                    var q = new System.Diagnostics.Eventing.Reader.EventLogQuery("System", System.Diagnostics.Eventing.Reader.PathType.LogName,
                        "*[System[Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and (EventID=20) and TimeCreated[timediff(@SystemTime) <= 1209600000]]]");
                    using (var rd = new System.Diagnostics.Eventing.Reader.EventLogReader(q))
                    {
                        System.Diagnostics.Eventing.Reader.EventRecord e;
                        while (failures < 50 && (e = rd.ReadEvent()) != null) { e.Dispose(); failures++; }
                    }
                }
                catch { }
                if (w == "Disabled") { c.Result = St.Warn; c.Detail = "El servicio está desactivado. Actívalo en Servicios si el equipo debe actualizarse."; }
                else if (failures >= 2) { c.Result = St.Warn; c.Detail = failures + " actualizaciones fallaron en 14 días."; c.Suggest = new[] { "punto", "hora", "wureset", "dism" }; }
                else { c.Result = St.Ok; c.Detail = failures == 1 ? "Una actualización falló en 14 días, sin repetición." : "Sin fallos en 14 días."; }
            });
            add("Internet", c =>
            {
                bool ping = false;
                foreach (var ip in new[] { "1.1.1.1", "8.8.8.8" })
                    try { using (var p = new Ping()) if (p.Send(IPAddress.Parse(ip), 2000).Status == IPStatus.Success) { ping = true; break; } } catch { }
                bool dns = false;
                try { dns = Dns.GetHostAddresses("www.microsoft.com").Length > 0; } catch { }
                if (ping && dns) { c.Result = St.Ok; c.Detail = "Conexión y DNS funcionan."; }
                else if (ping) { c.Result = St.Warn; c.Detail = "Hay conexión, pero el DNS no responde."; c.Suggest = new[] { "dns" }; }
                else { c.Result = St.Warn; c.Detail = "Sin conexión. Revisa el cable o el Wi-Fi."; c.Suggest = new[] { "dns", "proxy" }; }
            });
            add("Proxy", c =>
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    bool on = k != null && Convert.ToInt32(k.GetValue("ProxyEnable") ?? 0) != 0;
                    c.Result = on ? St.Warn : St.Ok;
                    c.Detail = on ? "Hay un proxy activo: " + k.GetValue("ProxyServer") + ". Comprueba que el cliente lo necesite." : "Sin proxy.";
                    if (on) c.Suggest = new[] { "proxy" };
                }
            });
            add("Hora del equipo", c =>
            {
                string o;
                new ToolRun().Run("w32tm", "/stripchart /computer:time.windows.com /samples:1 /dataonly", 12000, null, out o);
                c.Tech = o;
                var m = Regex.Match(o, @"([+-]\d+[.,]\d+)s");
                if (!m.Success) { c.Result = St.Skipped; c.Detail = "No se pudo comparar con el servidor de hora."; return; }
                double off = Math.Abs(double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture));
                c.Result = off > 120 ? St.Warn : St.Ok;
                c.Detail = off > 120 ? "La hora está desfasada " + ((int)off) + " segundos." : "La hora es correcta.";
                if (off > 120) c.Suggest = new[] { "hora" };
            });
            add("Impresión", c =>
            {
                string st = ServiceState("Spooler");
                int files = 0;
                try { files = Directory.GetFiles(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"spool\PRINTERS")).Length; } catch { }
                if (st != "Running" && st != "Disabled") { c.Result = St.Warn; c.Detail = "El servicio de impresión está detenido."; c.Suggest = new[] { "spooler" }; }
                else if (st == "Disabled") { c.Result = St.Skipped; c.Detail = "El servicio de impresión está desactivado."; }
                else if (files > 0) { c.Result = St.Warn; c.Detail = "Hay " + (files / 2 + files % 2) + " trabajos en cola. Si la impresora no avanza, vacía la cola."; }
                else { c.Result = St.Ok; c.Detail = "Servicio activo y sin trabajos en cola."; }
            });
            add("Sonido", c =>
            {
                string st = ServiceState("Audiosrv");
                c.Result = st == "Running" ? St.Ok : St.Warn;
                c.Detail = st == "Running" ? "El servicio de sonido está activo." : "El servicio de sonido no está activo.";
                if (st != "Running") c.Suggest = new[] { "audio" };
            });
            return list;
        }

        static string ServiceState(string name)
        {
            using (var s = new ServiceController(name))
            {
                if (s.StartType == ServiceStartMode.Disabled) return "Disabled";
                return s.Status == ServiceControllerStatus.Running ? "Running" : "Stopped";
            }
        }
    }

    public static class Report
    {
        static string E(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        public static string Word(St s)
        {
            switch (s)
            {
                case St.Ok: return "Bien";
                case St.Warn: return "Revisar";
                case St.Fail: return "Falló";
                case St.Skipped: return "Sin hacer";
                case St.Running: return "En curso…";
                default: return "Pendiente";
            }
        }

        public static string Verdict(List<Repair> done, out string detail, out string color)
        {
            int ok = done.Count(r => r.Result == St.Ok), warn = done.Count(r => r.Result == St.Warn), fail = done.Count(r => r.Result == St.Fail), skip = done.Count(r => r.Result == St.Skipped);
            detail = "Bien: " + ok + ". Por revisar: " + warn + ". Con fallos: " + fail + (skip > 0 ? ". Sin hacer: " + skip : "") + ".";
            bool reboot = done.Any(r => r.NeedsReboot && r.Result == St.Ok);
            if (fail > 0) { color = "#c62828"; return fail == 1 ? "Una reparación falló" : fail + " reparaciones fallaron"; }
            if (warn > 0 || skip > 0) { color = "#a86200"; return "Terminado, con puntos por revisar"; }
            color = "#1b7f4b";
            return reboot ? "Reparado: falta reiniciar" : "Reparaciones terminadas";
        }

        public static string Page(List<Repair> done, List<Check> before, List<Check> after, Comun.AppInfo info)
        {
            string detail, color;
            string verdict = Verdict(done, out detail, out color);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=es><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Reparación de " + E(Environment.MachineName) + "</title><style>");
            sb.Append("body{margin:0;background:#eef1f3;color:#1e2a32;font:15px/1.5 'Segoe UI',system-ui,sans-serif}.band{color:#fff;padding:24px}.band h1{font-size:25px;line-height:1.2;margin:0 0 6px;font-weight:600}.band p{margin:0;max-width:72ch}");
            sb.Append("main{max-width:880px;margin:0 auto;padding:8px 16px 40px}h2{font-size:18px;font-weight:600;margin:26px 0 8px}table{border-collapse:collapse;width:100%;background:#fff;font-size:14px}td,th{padding:6px 10px;border-bottom:1px solid #d5dbe0;text-align:left;vertical-align:top;overflow-wrap:anywhere}th{font-weight:600}.w{overflow-x:auto}details{margin-top:4px;font-size:13px}pre{white-space:pre-wrap;word-break:break-word;font:12.5px Consolas,monospace;margin:4px 0}");
            sb.Append("@media print{body{background:#fff}.band{-webkit-print-color-adjust:exact;print-color-adjust:exact}}</style>");
            sb.Append("<div class=band style='background:" + color + "'><h1>" + E(verdict) + "</h1><p>" + E(detail) + "</p><p>Equipo " + E(Environment.MachineName) + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ".</p></div><main>");
            sb.Append("<h2>Reparaciones</h2><div class=w><table><tr><th>Reparación</th><th>Resultado</th><th>Detalle</th></tr>");
            foreach (var r in done)
            {
                sb.Append("<tr><td>" + E(r.Label) + "</td><td>" + E(Word(r.Result)) + "</td><td>" + E(r.Detail));
                if (r.Log.Length > 0) sb.Append("<details><summary>Datos técnicos</summary><pre>" + E(r.Log.ToString()) + "</pre></details>");
                sb.Append("</td></tr>");
            }
            sb.Append("</table></div>");
            if (before != null && before.Count > 0)
            {
                sb.Append("<h2>Antes y después</h2><div class=w><table><tr><th>Revisión</th><th>Antes</th><th>Después</th></tr>");
                foreach (var b in before)
                {
                    var a = after == null ? null : after.FirstOrDefault(x => x.Label == b.Label);
                    sb.Append("<tr><td>" + E(b.Label) + "</td><td>" + E(Word(b.Result) + ": " + b.Detail) + "</td><td>" + E(a == null ? "Sin comprobar." : Word(a.Result) + ": " + a.Detail) + "</td></tr>");
                }
                sb.Append("</table></div>");
            }
            sb.Append(Comun.Credits.HtmlFooter(info));
            sb.Append("</main></html>");
            return sb.ToString();
        }

        public static string Save(string html)
        {
            string name = "Reparacion_" + Environment.MachineName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + ".html";
            try
            {
                if (Env.TestMode) throw new InvalidOperationException();
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
}
