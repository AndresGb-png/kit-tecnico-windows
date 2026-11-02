using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace RespaldoPC
{
    public class JobResult
    {
        public bool Complete;
        public string Verdict, Detail, ReportPath, Color;
        public List<string> Pending = new List<string>();
    }

    public static class Jobs
    {
        public const string Green = "#1b7f4b", Amber = "#a86200", Red = "#c62828";

        public static string SafeName(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        public static string NewFolder(string dest, SourceInfo source)
        {
            string name = source.IsCurrentWindows ? Environment.MachineName : "Disco_" + Backup.Letter(source.Root);
            return Path.Combine(dest, "Respaldo_" + SafeName(name) + "_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm"));
        }

        // Un respaldo a medias del mismo origen se puede continuar sin volver a copiar lo que ya está.
        public static string FindIncomplete(string dest, SourceInfo source)
        {
            try
            {
                string name = source.IsCurrentWindows ? Environment.MachineName : "Disco_" + Backup.Letter(source.Root);
                return Directory.GetDirectories(dest, "Respaldo_" + SafeName(name) + "_*").OrderByDescending(x => x)
                    .FirstOrDefault(d => Info.Get(Info.Read(d), "estado") == "incompleto");
            }
            catch { return null; }
        }

        public static JobResult RunBackup(SourceInfo source, string root, List<CopyTask> tasks, List<SystemTask> system, CopyEngine engine, Action<string> status)
        {
            N.EnsureDir(root);
            Fs.RemoveCopyTemps(root);
            Backup.WriteInfo(root, source, "incompleto", 0);

            var selected = tasks.Where(t => t.Selected).ToList();
            foreach (var t in selected) t.Dst = Path.Combine(root, t.Rel);
            engine.BytesTotal = selected.Sum(t => t.Size);
            engine.Known = Manifest.Load(root);
            var w = new StreamWriter(Path.Combine(root, Manifest.FileName), true, new UTF8Encoding(false));
            try
            {
                engine.ManifestOut = w;
                engine.Run(selected);
                engine.ManifestOut = null;
            }
            finally
            {
                // Si el destino desapareció, cerrar el archivo también falla: ya está anotado como error.
                try { w.Dispose(); } catch (IOException) { }
            }

            if (!engine.Cancel && engine.Fatal == null)
                foreach (var s in system.Where(x => x.Selected))
                {
                    if (status != null) status("Guardando: " + s.Label);
                    Backup.RunSystem(s, root, source);
                }

            var r = new JobResult();
            int sysFail = system.Count(s => s.Selected && s.Result == St.Fail);
            bool stopped = engine.Cancel || engine.Fatal != null;
            r.Complete = !stopped && engine.FailCount == 0 && sysFail == 0;

            if (engine.Fatal != null)
            {
                r.Verdict = "El respaldo se detuvo antes de terminar";
                r.Detail = engine.Fatal;
                r.Color = Red;
            }
            else if (engine.Cancel)
            {
                r.Verdict = "Respaldo cancelado: no formatees todavía";
                r.Detail = "Lo copiado se conserva. Pulsa «Reintentar» para seguir desde donde quedó.";
                r.Color = Amber;
            }
            else if (!r.Complete)
            {
                r.Verdict = "Faltan " + (engine.FailCount + sysFail) + " elementos por copiar: no formatees todavía";
                r.Detail = "Revisa la lista. Si son archivos en uso, ciérralos y pulsa «Reintentar».";
                r.Color = Red;
            }
            else
            {
                r.Verdict = "Todo lo seleccionado quedó copiado";
                r.Detail = engine.FilesDone.ToString("N0") + " archivos" + (engine.Verify ? " comprobados uno por uno contra el original." : " copiados, sin verificación.");
                r.Color = Green;
            }

            r.Pending.Add("Las contraseñas guardadas en el navegador no se copian. Pide al cliente que active la sincronización de su cuenta o las exporte antes de formatear.");
            r.Pending.Add("Los programas no se copian: hay que reinstalarlos. Anota sus licencias antes de formatear.");
            if (engine.Cloud.Count > 0)
                r.Pending.Add(engine.Cloud.Count + " archivos solo están en la nube. Confirma que el cliente conoce la contraseña de esa cuenta.");
            var skipped = tasks.Where(t => !t.Selected).ToList();
            if (skipped.Count > 0)
                r.Pending.Add("Elegiste no copiar " + skipped.Count + " elementos (" + Fs.Bytes(skipped.Sum(t => t.Size)) + "). Están listados abajo.");

            string label = source.IsCurrentWindows ? Environment.MachineName : source.Label;
            string programs = "";
            try { programs = Report.ProgramsTable(root); } catch (IOException) { }
            string html = Report.Page("Respaldo de " + label, r.Verdict, r.Detail, r.Color, tasks, engine, system, r.Pending, programs);
            r.ReportPath = Path.Combine(root, "informe.html");
            try
            {
                File.WriteAllText(r.ReportPath, html, new UTF8Encoding(true));
                Backup.WriteInfo(root, source, r.Complete ? "completo" : "incompleto", engine.FailCount);
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                // El destino ya no está: el informe se deja donde siempre se puede escribir.
                r.ReportPath = Path.Combine(Path.GetTempPath(), "Informe_respaldo_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
                File.WriteAllText(r.ReportPath, html, new UTF8Encoding(true));
            }
            return r;
        }

        public static JobResult RunRestore(string backup, List<CopyTask> tasks, List<SystemTask> system, CopyEngine engine, Action<string> status)
        {
            var selected = tasks.Where(t => t.Selected).ToList();
            engine.BytesTotal = selected.Sum(t => t.Size);
            engine.Known = Manifest.Load(backup);
            engine.CheckAgainstKnown = true;
            engine.Run(selected);

            if (!engine.Cancel && engine.Fatal == null)
                foreach (var s in system.Where(x => x.Selected))
                {
                    if (status != null) status("Restaurando: " + s.Label);
                    Restore.RunSystem(s, backup);
                }

            var r = new JobResult();
            int sysFail = system.Count(s => s.Selected && s.Result == St.Fail);
            bool stopped = engine.Cancel || engine.Fatal != null;
            r.Complete = !stopped && engine.FailCount == 0 && sysFail == 0;
            if (engine.Fatal != null) { r.Verdict = "La restauración se detuvo antes de terminar"; r.Detail = engine.Fatal; r.Color = Red; }
            else if (engine.Cancel) { r.Verdict = "Restauración cancelada"; r.Detail = "Lo ya restaurado se conserva. Pulsa «Reintentar» para seguir."; r.Color = Amber; }
            else if (!r.Complete) { r.Verdict = "Faltan " + (engine.FailCount + sysFail) + " elementos por restaurar"; r.Detail = "Conserva el respaldo y revisa la lista antes de entregar el equipo."; r.Color = Red; }
            else { r.Verdict = "Restauración terminada"; r.Detail = engine.FilesDone.ToString("N0") + " archivos revisados. Nada del equipo se sobrescribió."; r.Color = Green; }

            r.Pending.Add("Reinstala los programas del cliente. La lista está en este informe.");
            r.Pending.Add("Si el cliente usa Chrome o Edge, inicia sesión en su cuenta para recuperar las contraseñas.");
            if (selected.Any(t => t.Label.StartsWith("Correo de Outlook")))
                r.Pending.Add("Outlook: abre Archivo, Abrir y exportar, Abrir archivo de datos de Outlook, y elige el .pst restaurado.");
            if (selected.Any(t => t.Label == "Favoritos de Firefox"))
                r.Pending.Add("Firefox: abre Marcadores, Administrar marcadores, Restaurar, y elige un archivo de Documentos\\Respaldo de Firefox.");
            if (N.IsDir(Path.Combine(backup, "Sistema", "WiFi")))
                r.Pending.Add("El respaldo contiene claves Wi-Fi legibles. Bórralas cuando termines.");

            r.ReportPath = Path.Combine(Path.GetTempPath(), "Informe_restauracion_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".html");
            try
            {
                string onBackup = Path.Combine(backup, "informe_restauracion.html");
                File.WriteAllText(onBackup, Report.Page("Restauración", r.Verdict, r.Detail, r.Color, tasks, engine, system, r.Pending, Report.ProgramsTable(backup)), new UTF8Encoding(true));
                r.ReportPath = onBackup;
            }
            catch
            {
                File.WriteAllText(r.ReportPath, Report.Page("Restauración", r.Verdict, r.Detail, r.Color, tasks, engine, system, r.Pending, Report.ProgramsTable(backup)), new UTF8Encoding(true));
            }
            return r;
        }
    }
}
