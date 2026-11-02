using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Limpieza de virus y arranque")]
[assembly: System.Reflection.AssemblyProduct("Limpieza de virus y arranque")]
[assembly: System.Reflection.AssemblyVersion("1.0.1.0")]

namespace LimpiezaPC
{
    static class Program
    {
        public static readonly Comun.AppInfo Info = new Comun.AppInfo
        {
            Name = "Limpieza de virus y arranque",
            Version = "1.0.1",
            Description = "Revisa lo que arranca con Windows y señala lo sospechoso. Permite desactivarlo sin borrar nada y analizar el equipo con Windows Defender. No sustituye a un antivirus.",
            Uses = new[]
            {
                "Registro de Windows, carpetas de Inicio, Programador de tareas y servicios",
                "Firmas digitales de los archivos, comprobadas con PowerShell",
                "Windows Defender para el análisis de virus",
                "Información del sistema de Windows (WMI) para eventos ocultos y antivirus instalados"
            },
            Privacy = "Este programa no envía tus datos a ningún servidor. Solo si pulsas «Buscar en VirusTotal» se abre ese sitio con la huella del archivo, sin subir el archivo. Los cambios se anotan en este equipo para poder deshacerlos."
        };

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);

            int at = Array.FindIndex(args, a => a.Equals("--analizar", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 2 < args.Length) return CliScan(args[at + 1], args[at + 2]);
            at = Array.FindIndex(args, a => a.Equals("--desactivar", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 2 < args.Length) return CliDisable(args[at + 1], args[at + 2]);
            at = Array.FindIndex(args, a => a.Equals("--deshacer", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 2 < args.Length) return CliUndo(args[at + 1], args[at + 2]);
            at = Array.FindIndex(args, a => a.Equals("--defender-rapido", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < args.Length) return CliDefender(args[at + 1]);

            bool noElevate = args.Any(a => a.Equals("--no-elevar", StringComparison.OrdinalIgnoreCase));
            if (!Env.Admin() && !noElevate && Relaunch("--no-elevar")) return 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                LogCrash(e.Exception);
                try { MessageBox.Show("Ocurrió un error en esta pantalla, pero el programa sigue abierto.\n\nDetalle: " + CrashPath(), "Limpieza de virus y arranque", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                catch { }
            };
            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                LogCrash(ex);
                try { MessageBox.Show("La ventana no pudo abrirse.\n\nDetalle: " + CrashPath(), "Limpieza de virus y arranque"); } catch { }
            }
            Environment.Exit(0);
            return 0;
        }

        public static bool Relaunch(string arg)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, arg) { UseShellExecute = true, Verb = "runas" });
                return true;
            }
            catch (Win32Exception) { return false; }
            catch (Exception ex) { LogCrash(ex); return false; }
        }

        static void Line(StringBuilder sb, Item i)
        {
            sb.AppendLine(string.Join("\t", new[] { Report.Word(i.Severity), i.Disabled ? "apagado" : "activo", i.Group, i.Name, i.Id, i.Path, i.SigStatus, i.Signer, string.Join(" ", i.Reasons), i.Command.Replace("\t", " ") }));
        }

        // --analizar informe.html resultados.tsv
        static int CliScan(string html, string tsv)
        {
            try
            {
                var r = Scanner.Run(null);
                File.WriteAllText(html, Report.Page(r, "", Info), new UTF8Encoding(true));
                var sb = new StringBuilder();
                foreach (var i in r.Items.OrderByDescending(x => x.Severity)) Line(sb, i);
                foreach (var g in r.Gaps) sb.AppendLine("HUECO\t" + g);
                File.WriteAllText(tsv, sb.ToString(), new UTF8Encoding(true));
                return r.Items.Any(i => i.Severity == Sev.Suspicious) ? 1 : 0;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        static Item FindById(string fragment)
        {
            var r = Scanner.Run(null);
            return r.Items.FirstOrDefault(i => i.Id.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static int CliDisable(string fragment, string output)
        {
            try
            {
                var i = FindById(fragment);
                File.WriteAllText(output, i == null ? "NO ENCONTRADO" : Actions.Disable(i), new UTF8Encoding(true));
                return i == null ? 2 : 0;
            }
            catch (Exception ex) { LogCrash(ex); File.WriteAllText(output, "ERROR " + ex.Message, new UTF8Encoding(true)); return 2; }
        }

        static int CliUndo(string fragment, string output)
        {
            try
            {
                var c = Actions.Load().FirstOrDefault(x => x.Id.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
                File.WriteAllText(output, c == null ? "NO ENCONTRADO" : Actions.Undo(c), new UTF8Encoding(true));
                return c == null ? 2 : 0;
            }
            catch (Exception ex) { LogCrash(ex); File.WriteAllText(output, "ERROR " + ex.Message, new UTF8Encoding(true)); return 2; }
        }

        static int CliDefender(string output)
        {
            try
            {
                bool threats;
                File.WriteAllText(output, Tools.DefenderScan(false, null, out threats), new UTF8Encoding(true));
                return threats ? 1 : 0;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        public static string CrashPath() { return Path.Combine(Path.GetTempPath(), "LimpiezaPC_errores.log"); }

        public static void LogCrash(Exception ex)
        {
            try { File.AppendAllText(CrashPath(), DateTime.Now + "\r\n" + ex + "\r\n\r\n"); } catch { }
        }
    }
}
