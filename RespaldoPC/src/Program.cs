using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Respaldo y migración")]
[assembly: System.Reflection.AssemblyProduct("Respaldo y migración")]
[assembly: System.Reflection.AssemblyVersion("1.0.1.0")]

namespace RespaldoPC
{
    static class Program
    {
        public static readonly Comun.AppInfo Info = new Comun.AppInfo
        {
            Name = "Respaldo y migración",
            Version = "1.0.1",
            Description = "Guarda los datos del cliente antes de formatear, comprueba cada copia y los devuelve a un Windows limpio sin sobrescribir nada.",
            Uses = new[]
            {
                "Copia propia de archivos con rutas largas y comprobación SHA-256",
                "Herramientas de Windows: netsh para Wi-Fi, DISM y pnputil para controladores, PrintBrm para impresoras e ipconfig para datos de red",
                "winget, si el equipo lo tiene, para anotar los programas instalados",
                "Información del sistema de Windows (WMI) y registro de Windows para la lista de programas"
            },
            Privacy = "Los archivos solo se copian entre los discos que tú eliges, sin enviarlos a ningún servidor. winget puede consultar su catálogo en internet al anotar los programas. Si incluyes las redes Wi-Fi, sus claves quedan legibles dentro del respaldo: guárdalo en un lugar seguro."
        };

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);
            // Permiten leer carpetas de otros usuarios y de discos de otro Windows.
            N.EnablePrivilege("SeBackupPrivilege");
            N.EnablePrivilege("SeRestorePrivilege");

            int at = Array.FindIndex(args, a => a.Equals("--respaldar", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 2 < args.Length) return CliBackup(CleanArg(args[at + 1]), CleanArg(args[at + 2]));
            at = Array.FindIndex(args, a => a.Equals("--restaurar", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 2 < args.Length) return CliRestore(args[at + 1], args[at + 2]);

            at = Array.FindIndex(args, a => a.Equals("--configuracion", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < args.Length) return CliSystem(args[at + 1]);
            at = Array.FindIndex(args, a => a.Equals("--restaurar-wifi", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < args.Length) return CliWifi(args[at + 1]);
            at = Array.FindIndex(args, a => a.Equals("--restaurar-impresoras", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < args.Length) return CliRestoreTask(args[at + 1], "impresoras", "impresoras_restauradas.txt");

            bool noElevate = args.Any(a => a.Equals("--no-elevar", StringComparison.OrdinalIgnoreCase));
            if (!Admin.Is() && !noElevate && Relaunch("--no-elevar")) return 0;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                LogCrash(e.Exception);
                try
                {
                    MessageBox.Show("Ocurrió un error en esta pantalla, pero el programa sigue abierto. Lo ya copiado no se pierde.\n\nDetalle: " + CrashPath(), "Respaldo y migración", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch { }
            };
            try { Application.Run(new MainForm()); }
            catch (Exception ex)
            {
                LogCrash(ex);
                try { MessageBox.Show("La ventana no pudo abrirse.\n\nDetalle: " + CrashPath(), "Respaldo y migración"); } catch { }
            }
            Environment.Exit(0);
            return 0;
        }

        // Una ruta como "Y:\" llega como Y:" porque Windows lee \" como comilla escapada.
        static string CleanArg(string a)
        {
            string s = a.Trim().Trim('"').Trim();
            return s.Length == 2 && s[1] == ':' ? s + "\\" : s;
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

        // Sin ventana: la raíz es tratada como un disco de otro Windows. Útil para scripts.
        static int CliBackup(string root, string dest)
        {
            try
            {
                var src = new SourceInfo { Root = root, Label = root, IsCurrentWindows = false };
                var tasks = Backup.PlanFiles(src);
                foreach (var t in tasks) Backup.Measure(t, null);
                var system = Backup.PlanSystem(src, false);
                string folder = Jobs.FindIncomplete(dest, src) ?? Jobs.NewFolder(dest, src);
                using (var engine = new CopyEngine())
                    return Jobs.RunBackup(src, folder, tasks, system, engine, null).Complete ? 0 : 1;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        static int CliRestore(string backup, string root)
        {
            try
            {
                var tasks = new List<CopyTask>();
                foreach (var u in Restore.BackupUsers(backup))
                    tasks.AddRange(Restore.PlanUser(backup, u, root.TrimEnd('\\') + "\\Users\\" + u));
                tasks.AddRange(Restore.PlanOthers(backup, root, false));
                foreach (var t in tasks) Backup.Measure(t, null);
                using (var engine = new CopyEngine())
                    return Jobs.RunRestore(backup, tasks, new List<SystemTask>(), engine, null).Complete ? 0 : 1;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        // Solo la configuración de este Windows, sin copiar archivos. Deja el resultado en configuracion.txt.
        static int CliSystem(string dest)
        {
            try
            {
                var src = Backup.Sources().First(s => s.IsCurrentWindows);
                var system = Backup.PlanSystem(src, Admin.Is());
                N.EnsureDir(dest);
                foreach (var t in system) { t.Selected = true; Backup.RunSystem(t, dest, src); }
                Backup.WriteInfo(dest, src, "completo", 0);
                File.WriteAllLines(Path.Combine(dest, "configuracion.txt"), system.Select(t => t.Key + "\t" + t.Result + "\t" + t.Detail), System.Text.Encoding.UTF8);
                return system.Any(t => t.Result == St.Fail) ? 1 : 0;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        static int CliWifi(string backup) { return CliRestoreTask(backup, "wifi", "wifi_restaurado.txt"); }

        static int CliRestoreTask(string backup, string key, string resultFile)
        {
            try
            {
                var t = new SystemTask { Key = key, Label = key };
                Restore.RunSystem(t, backup);
                File.WriteAllText(Path.Combine(backup, resultFile), t.Result + "\t" + t.Detail, System.Text.Encoding.UTF8);
                return t.Result == St.Fail ? 1 : 0;
            }
            catch (Exception ex) { LogCrash(ex); return 2; }
        }

        public static string CrashPath() { return Path.Combine(Path.GetTempPath(), "RespaldoPC_errores.log"); }

        public static void LogCrash(Exception ex)
        {
            try { File.AppendAllText(CrashPath(), DateTime.Now + "\r\n" + ex + "\r\n\r\n"); } catch { }
        }
    }
}
