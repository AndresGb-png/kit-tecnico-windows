using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Chequeador de PC")]
[assembly: System.Reflection.AssemblyProduct("Chequeador de PC")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace ChequeadorPC
{
    static class Program
    {
        public static readonly Comun.AppInfo Info = new Comun.AppInfo
        {
            Name = "Chequeador de PC",
            Version = "1.0.0",
            Description = "Revisa el hardware y el sistema del equipo y explica cada resultado en lenguaje sencillo, con datos técnicos para el técnico.",
            Uses = new[]
            {
                "Información del sistema de Windows (WMI) y registro de eventos",
                "Tablas del firmware (SMBIOS) para las ranuras de memoria",
                "Datos de salud de los discos (SMART y NVMe)",
                "Herramientas de Windows: netsh, ping y powercfg; nvidia-smi si el equipo lo tiene",
                "Sonido y cámara de Windows, solo durante las pruebas que tú abres"
            },
            Privacy = "Todo se analiza en este equipo y no se envía nada a ningún servidor. Para probar internet solo hace ping al router, a 1.1.1.1 y a 8.8.8.8. La cámara y el micrófono se activan únicamente en sus pruebas, y no se graba nada."
        };

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);

            int at = Array.FindIndex(args, a => a.Equals("--reporte", StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
                return Headless(at + 1 < args.Length ? args[at + 1] : null);

            var ctx = new Ctx();
            bool noElevate = args.Any(a => a.Equals("--no-elevar", StringComparison.OrdinalIgnoreCase));
            if (!ctx.IsAdmin && !noElevate && Relaunch("--no-elevar"))
                return 0;

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    // Un error de la ventana no debe cerrar el programa ni perder la revisión.
                    LogCrash(e.Exception);
                    try
                    {
                        MessageBox.Show("Ocurrió un error en esta pantalla, pero el programa sigue funcionando.\n\nDetalle guardado en:\n" + CrashPath(),
                            "Chequeador de PC", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    catch { }
                };
                Application.Run(new MainForm(ctx));
            }
            catch (Exception ex)
            {
                // Si la ventana no puede abrirse, el informe sigue siendo posible.
                LogCrash(ex);
                string path = null;
                try
                {
                    path = Path.Combine(Path.GetTempPath(), "Diagnostico_PC.html");
                    Headless(path);
                    Process.Start(path);
                }
                catch { }
                try
                {
                    MessageBox.Show("La ventana del programa no pudo abrirse. Se generó el informe igualmente:\n" + path, "Chequeador de PC");
                }
                catch { }
            }
            Environment.Exit(0);
            return 0;
        }

        // Devuelve true si se abrió la copia con permisos. Rechazar el aviso no es un error:
        // el programa continúa con permisos limitados.
        public static bool Relaunch(string arg)
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, arg) { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception) { return false; }
            catch (Exception ex) { LogCrash(ex); return false; }
        }

        static int Headless(string path)
        {
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Path.GetTempPath(), "Diagnostico_PC.html");
            var ctx = new Ctx();
            var engine = new Engine(ctx);
            engine.Start();
            while (!engine.Done) Thread.Sleep(200);
            File.WriteAllText(path, Report.Html(engine.Sections, ctx), new UTF8Encoding(true));
            return 0;
        }

        public static string CrashPath() { return Path.Combine(Path.GetTempPath(), "ChequeadorPC_errores.log"); }

        static void LogCrash(Exception ex)
        {
            try { File.AppendAllText(CrashPath(), DateTime.Now + "\r\n" + ex + "\r\n\r\n"); } catch { }
        }
    }
}
