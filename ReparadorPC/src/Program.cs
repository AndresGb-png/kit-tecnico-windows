using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Reparador de Windows")]
[assembly: System.Reflection.AssemblyProduct("Reparador de Windows")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace ReparadorPC
{
    static class Program
    {
        public static readonly Comun.AppInfo Info = new Comun.AppInfo
        {
            Name = "Reparador de Windows",
            Version = "1.0.0",
            Description = "Revisa el estado de Windows, propone las reparaciones habituales, las ejecuta una por una y deja un informe de antes y después.",
            Uses = new[]
            {
                "Herramientas de Windows: SFC, DISM, chkdsk, netsh, ipconfig, w32tm y wsreset",
                "Control de servicios de Windows para actualizaciones, impresión, sonido y hora",
                "PowerShell para el punto de restauración",
                "Registro de eventos e información del sistema (WMI) para el diagnóstico"
            },
            Privacy = "Este programa no envía tus datos a ningún servidor. Para revisar la conexión hace ping a 1.1.1.1 y 8.8.8.8, consulta www.microsoft.com y time.windows.com, y DISM puede descargar archivos de Windows Update."
        };

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);

            int at = Array.FindIndex(args, a => a.Equals("--reparar", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 2 < args.Length) return Cli(args[at + 1], args[at + 2]);
            at = Array.FindIndex(args, a => a.Equals("--diagnostico", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < args.Length) return Cli("", args[at + 1]);

            bool noElevate = args.Any(a => a.Equals("--no-elevar", StringComparison.OrdinalIgnoreCase));
            if (!Env.Admin() && !noElevate && Relaunch("--no-elevar")) return 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                LogCrash(e.Exception);
                try { MessageBox.Show("Ocurrió un error en esta pantalla, pero el programa sigue abierto.\n\nDetalle: " + CrashPath(), "Reparador de Windows", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                catch { }
            };
            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                LogCrash(ex);
                try { MessageBox.Show("La ventana no pudo abrirse.\n\nDetalle: " + CrashPath(), "Reparador de Windows"); } catch { }
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

        // Sin ventana: --diagnostico informe.html o --reparar clave1,clave2 informe.html
        static int Cli(string keys, string report)
        {
            try
            {
                bool admin = Env.Admin();
                var before = Diagnosis.Run(null, admin);
                var wanted = keys.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToList();
                var chosen = Repairs.All().Where(r => wanted.Contains(r.Key)).ToList();
                Repairs.RunAll(chosen, new Ctx(), admin);
                var after = chosen.Count > 0 ? Diagnosis.Run(null, admin) : null;
                File.WriteAllText(report, Report.Page(chosen, before, after, Info), new UTF8Encoding(true));
                return chosen.Any(r => r.Result == St.Fail) ? 1 : 0;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        public static string CrashPath() { return Path.Combine(Path.GetTempPath(), "ReparadorPC_errores.log"); }

        public static void LogCrash(Exception ex)
        {
            try { File.AppendAllText(CrashPath(), DateTime.Now + "\r\n" + ex + "\r\n\r\n"); } catch { }
        }
    }
}
