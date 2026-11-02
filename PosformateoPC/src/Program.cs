using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Instalador posformateo")]
[assembly: System.Reflection.AssemblyProduct("Instalador posformateo")]
[assembly: System.Reflection.AssemblyVersion("1.0.1.0")]

namespace PosformateoPC
{
    static class Program
    {
        public static readonly Comun.AppInfo Info = new Comun.AppInfo
        {
            Name = "Instalador posformateo",
            Version = "1.0.1",
            Description = "Instala programas por perfiles, aplica ajustes básicos y deja un informe de lo que quedó instalado en un Windows recién formateado.",
            Uses = new[]
            {
                "winget, el gestor de paquetes de Microsoft, para descargar e instalar",
                "msiexec para instaladores .msi de una carpeta local",
                "Registro de Windows para los ajustes del Explorador",
                "PowerShell para el punto de restauración y el nombre del equipo, y powercfg para la energía",
                "Información del sistema de Windows (WMI) para detectar controladores que faltan"
            },
            Privacy = "Este programa no envía tus datos a ningún servidor. winget se conecta a internet para consultar su catálogo y descargar los programas desde sus fabricantes. Cada programa tiene su propia licencia, que se acepta al instalarlo."
        };

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);

            int at = Array.FindIndex(args, a => a.Equals("--verificar-catalogo", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < args.Length) return VerifyCatalog(args[at + 1]);
            at = Array.FindIndex(args, a => a.Equals("--instalar", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 2 < args.Length) return CliInstall(args[at + 1], args[at + 2]);
            at = Array.FindIndex(args, a => a.Equals("--ajuste", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 2 < args.Length) return CliTweak(args[at + 1], args[at + 2]);

            bool noElevate = args.Any(a => a.Equals("--no-elevar", StringComparison.OrdinalIgnoreCase));
            if (!Checks.Admin() && !noElevate && Relaunch("--no-elevar")) return 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                LogCrash(e.Exception);
                try { MessageBox.Show("Ocurrió un error en esta pantalla, pero el programa sigue abierto.\n\nDetalle: " + CrashPath(), "Instalador posformateo", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                catch { }
            };
            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                LogCrash(ex);
                try { MessageBox.Show("La ventana no pudo abrirse.\n\nDetalle: " + CrashPath(), "Instalador posformateo"); } catch { }
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

        // Comprueba contra winget que cada identificador del catálogo sigue existiendo.
        static int VerifyCatalog(string output)
        {
            try
            {
                if (!Winget.Detect()) { File.WriteAllText(output, "winget no disponible\r\n"); return 2; }
                var sb = new StringBuilder();
                int bad = 0;
                foreach (var a in Catalog.All())
                {
                    string o;
                    int code = new ToolRun().Run(Winget.Path_, Winget.ShowArgs(a.Id), 60000, out o);
                    if (code != 0) bad++;
                    sb.AppendLine((code == 0 ? "OK    " : "FALTA ") + a.Id);
                }
                File.WriteAllText(output, sb.ToString(), new UTF8Encoding(true));
                return bad == 0 ? 0 : 1;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        // Sin ventana, para listas fijas: --instalar id1,id2 informe.html
        static int CliInstall(string ids, string report)
        {
            try
            {
                if (!Winget.Detect()) return 2;
                var catalog = Catalog.All();
                var apps = ids.Split(',').Select(i => i.Trim()).Where(i => i.Length > 0).Select(i =>
                    catalog.FirstOrDefault(c => c.Id.Equals(i, StringComparison.OrdinalIgnoreCase)) ??
                    new App { Id = i, Name = i, Group = "", Kind = i.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ? Kind.Local : Kind.Winget }).ToList();
                var inst = new Installer(apps);
                var tweaks = Tweaks.Build();
                foreach (var t in tweaks) t.Selected = Tweaks.TestMode && (t.Key == "ext" || t.Key == "equipo" || t.Key == "icono");
                Tweaks.RunAll(tweaks);
                inst.Run();
                File.WriteAllText(report, Report.Page(inst, tweaks, Checks.MissingDrivers(), null, new AppInfoHolder { Info = Info }), new UTF8Encoding(true));
                return inst.Steps.Any(s => s.State == StepState.Failed || s.State == StepState.Pending) ? 1 : 0;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        // Aplica un ajuste de verdad y anota el resultado: --ajuste clave resultado.txt
        static int CliTweak(string key, string output)
        {
            try
            {
                var t = Tweaks.Build().FirstOrDefault(x => x.Key == key);
                if (t == null) return 2;
                Tweaks.RunAll(new[] { t });
                File.WriteAllText(output, (t.Failed ? "FALLO " : "OK ") + t.Detail, new UTF8Encoding(true));
                return t.Failed ? 1 : 0;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        public static string CrashPath() { return Path.Combine(Path.GetTempPath(), "PosformateoPC_errores.log"); }

        public static void LogCrash(Exception ex)
        {
            try { File.AppendAllText(CrashPath(), DateTime.Now + "\r\n" + ex + "\r\n\r\n"); } catch { }
        }
    }
}
