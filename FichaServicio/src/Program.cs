using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Ficha de servicio")]
[assembly: System.Reflection.AssemblyProduct("Ficha de servicio")]
[assembly: System.Reflection.AssemblyVersion("1.0.1.0")]

namespace FichaServicio
{
    static class Program
    {
        public static readonly Comun.AppInfo Info = new Comun.AppInfo
        {
            Name = "Ficha de servicio",
            Version = "1.0.1",
            Description = "Registra clientes, equipos, trabajos, costos y garantías, y genera comprobantes en PDF con el diagnóstico de antes y después.",
            Uses = new[]
            {
                "Archivos JSON guardados junto al programa o en Documentos",
                "Generador de PDF propio, sin programas adicionales",
                "ChequeadorPC.exe, si está en la misma carpeta, para el antes y el después",
                "Información del sistema de Windows (WMI) para leer los datos del equipo"
            },
            Privacy = "No se envía nada a ningún servidor. Los datos de tus clientes quedan en esta carpeta sin cifrar: guárdala en un lugar seguro, sobre todo si es una USB, y no anotes contraseñas en las fichas."
        };

        public static Store Data;

        [STAThread]
        static int Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception);

            int at = Array.FindIndex(args, a => a.Equals("--autoprueba", StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < args.Length) return SelfTest.Run(args[at + 1]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                LogCrash(e.Exception);
                try { MessageBox.Show("Ocurrió un error en esta pantalla, pero el programa sigue abierto. Lo guardado no se pierde.\n\nDetalle: " + CrashPath(), "Ficha de servicio", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                catch { }
            };
            try
            {
                Data = Store.Open();
                Application.Run(new MainForm(Data));
            }
            catch (Exception ex)
            {
                LogCrash(ex);
                try { MessageBox.Show("El programa no pudo abrirse: " + ex.Message + "\n\nDetalle: " + CrashPath(), "Ficha de servicio"); } catch { }
            }
            Environment.Exit(0);
            return 0;
        }

        public static List<CompareRow> CompareOf(Ficha x, Store store)
        {
            var a = x.Report("antes");
            var b = x.Report("despues");
            if (a == null || b == null) return null;
            try { return Reports.Compare(Reports.Parse(store.AttachmentPath(x, a)), Reports.Parse(store.AttachmentPath(x, b))); }
            catch { return null; }
        }

        public static void BuildPdf(Ficha x, Store store, string which, string path)
        {
            if (which == "recepcion") { Docs.Reception(x, store.Settings, store.Root, path); return; }
            var a = x.Report("antes");
            var b = x.Report("despues");
            Docs.Delivery(x, store.Settings, store.Root, path, CompareOf(x, store), a == null ? null : a.Verdict, b == null ? null : b.Verdict);
        }

        public static string CrashPath() { return Path.Combine(Path.GetTempPath(), "FichaServicio_errores.log"); }

        public static void LogCrash(Exception ex)
        {
            try { File.AppendAllText(CrashPath(), DateTime.Now + "\r\n" + ex + "\r\n\r\n"); } catch { }
        }
    }

    // Comprueba cálculos, datos y PDF sin ventana. Deja el resultado en un archivo.
    static class SelfTest
    {
        static StringBuilder log = new StringBuilder();
        static int failed;

        static void Check(string name, bool ok, string extra = "")
        {
            log.AppendLine((ok ? "OK    " : "FALLO ") + name + (extra.Length > 0 ? "  [" + extra + "]" : ""));
            if (!ok) failed++;
        }

        static void M(string text, decimal expected)
        {
            decimal v;
            bool ok = Money.TryParse(text, out v);
            Check("dinero " + text, ok && v == expected, v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        static void SamplePdfs(Store store, string dir, string before, string after)
        {
            store.Settings.BusinessName = "Servicio Técnico Ñandú & Hijos (prueba)";
            store.Settings.TechnicianName = "Técnico de prueba";
            store.Settings.Phone = "+57 300 000 0000";
            store.Settings.Email = "contacto@ejemplo.test";
            store.Settings.Address = "Calle de prueba 123";
            string logo = Environment.GetEnvironmentVariable("FICHAS_LOGO");
            if (!string.IsNullOrEmpty(logo)) store.Settings.LogoFile = logo;

            var x = store.NewFicha();
            x.ClientName = "María José Ñáñez (\"Pepa\") \\ prueba";
            x.ClientPhone = "300 123 4567"; x.ClientDocument = "CC 1.234.567"; x.ClientEmail = "cliente@ejemplo.test"; x.ClientAddress = "Avenida Larga número 45 con calle 12, edificio Las Palmas, apartamento 301";
            x.DeviceType = "Portátil"; x.DeviceBrandModel = "Acer Aspire A515-44"; x.DeviceSerial = "NXHW4AL00000000000000000000000000000000000000000000000000000000000000000000000000000000000";
            x.DeviceSpecs = "AMD Ryzen 5 4500U, 8 GB de RAM, Disco 512 GB, Microsoft Windows 10 Pro";
            x.Accessories = "Cargador original, funda negra";
            x.Condition = "Rayones en la tapa.\nFalta la tecla F5.";
            x.Problem = string.Join(" ", Enumerable.Repeat("El equipo se apaga solo, se calienta y va muy lento al abrir programas; además la batería dura poco.", 6));
            x.Diagnosis = "Ventilador obstruido, pasta térmica seca y batería al 58 % de su capacidad.";
            x.WorkDone = "Limpieza interna, cambio de pasta térmica, batería nueva.\nInstalación de controladores — con raya larga — y símbolos: € ¿? ¡! ° ñ Ñ á é í ó ú ü. Emoji sin soporte: 😀.";
            x.Notes = "Se recomienda cambiar a Windows 11.";
            for (int i = 1; i <= 34; i++)
                x.Items.Add(new Item { Description = i == 3 ? string.Join(" ", Enumerable.Repeat("Repuesto con una descripción muy larga", 8)) : "Repuesto número " + i, Quantity = i % 3 == 0 ? 1.5m : 1, UnitPrice = 12345.67m * i });
            x.Labor = 80000; x.Discount = 10000; x.TaxPercent = 19; x.Advance = 50000; x.WarrantyDays = 90;
            x.Status = Statuses.Delivered; x.DeliveredOn = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            if (!string.IsNullOrEmpty(before) && File.Exists(before) && File.Exists(after))
            {
                string a = store.AddAttachment(x, before, "antes");
                System.Threading.Thread.Sleep(1100);
                string b = store.AddAttachment(x, after, "despues");
                x.Attachments.Add(new Attachment { Kind = "antes", File = a, Verdict = Reports.Parse(before).Verdict });
                x.Attachments.Add(new Attachment { Kind = "despues", File = b, Verdict = Reports.Parse(after).Verdict });
            }
            store.Save(x);
            Program.BuildPdf(x, store, "recepcion", Path.Combine(dir, "muestra_recepcion.pdf"));
            Program.BuildPdf(x, store, "servicio", Path.Combine(dir, "muestra_servicio.pdf"));

            var small = store.NewFicha();
            small.ClientName = "Cliente mínimo";
            store.Settings.LogoFile = ""; store.Settings.BusinessName = ""; store.Settings.TechnicianName = "";
            store.Settings.PageSize = "A4";
            Program.BuildPdf(small, store, "recepcion", Path.Combine(dir, "muestra_minima_recepcion.pdf"));
            Program.BuildPdf(small, store, "servicio", Path.Combine(dir, "muestra_minima_servicio.pdf"));
            Check("PDF creados", new[] { "muestra_recepcion", "muestra_servicio", "muestra_minima_recepcion", "muestra_minima_servicio" }.All(n => new FileInfo(Path.Combine(dir, n + ".pdf")).Length > 500));
        }

        public static int Run(string output)
        {
            string dir = Environment.GetEnvironmentVariable("FICHAS_DATOS");
            if (string.IsNullOrEmpty(dir)) { File.WriteAllText(output, "Define FICHAS_DATOS"); return 2; }
            try
            {
                M("1500", 1500); M("1.500", 1500); M("1,500", 1500); M("1.500,50", 1500.50m); M("1,500.50", 1500.50m); M("12,5", 12.5m); M("12.5", 12.5m);
                M("1.000.000", 1000000); M("1,000,000", 1000000); M("$ 25.000", 25000); M("", 0); M("0,99", 0.99m); M("-5", -5);
                decimal bad; Check("dinero inválido", !Money.TryParse("abc", out bad));

                var t = new Ficha { Labor = 50000, Discount = 10000, TaxPercent = 19, Advance = 20000 };
                t.Items.Add(new Item { Description = "SSD", Quantity = 2, UnitPrice = 100000.50m });
                Check("repuestos", t.PartsTotal == 200001m);
                Check("subtotal", t.Subtotal == 240001m);
                Check("impuesto", t.Tax == 45600.19m, t.Tax.ToString());
                Check("total", t.Total == 285601.19m);
                Check("saldo", t.Balance == 265601.19m);
                Check("descuento no da negativo", new Ficha { Discount = 100 }.Total == 0);

                t.DeliveredOn = DateTime.Now.AddDays(-10).ToString("yyyy-MM-dd HH:mm");
                t.WarrantyDays = 30;
                Check("garantía vigente", t.WarrantyEnd >= DateTime.Today && t.WarrantyText.StartsWith("Vigente"));
                t.WarrantyDays = 5;
                Check("garantía vencida", t.WarrantyText.StartsWith("Venció"));
                t.WarrantyDays = 0;
                Check("sin garantía", t.WarrantyText == "");

                Check("csv fórmula", Csv.Cell("=1+1") == "\"'=1+1\"");
                Check("csv comillas", Csv.Cell("a\"b") == "\"a\"\"b\"");

                var store = Store.Open();
                Check("carpeta de datos", store.Root.TrimEnd('\\').Equals(dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase), store.Root);
                var a = store.NewFicha();
                var b = store.NewFicha();
                Check("números distintos", a.Number != b.Number, a.Number + " " + b.Number);
                a.ClientName = "José Ñáñez (\"Pepe\") €";
                a.Problem = "Línea 1\nLínea 2";
                a.Items.Add(new Item { Description = "Disco", Quantity = 1.5m, UnitPrice = 10.25m });
                store.Save(a);
                var loaded = store.LoadAll().First(x => x.Number == a.Number);
                Check("guardar y leer", loaded.ClientName == a.ClientName && loaded.Problem == a.Problem && loaded.Items[0].UnitPrice == 10.25m && loaded.Items[0].Quantity == 1.5m);

                // Una ficha dañada no debe impedir leer las demás; si hay copia, se usa.
                File.WriteAllText(Path.Combine(dir, "fichas", "F-1999-0001.json"), "{dañado");
                var list = store.LoadAll();
                Check("ficha dañada avisada", store.Unreadable.Contains("F-1999-0001.json") && list.Any(x => x.Number == a.Number));
                File.Delete(Path.Combine(dir, "fichas", "F-1999-0001.json"));
                string fa = Path.Combine(dir, "fichas", a.Number + ".json");
                a.ClientName = "Segunda versión";
                store.Save(a);
                File.WriteAllText(fa, "{cortado");
                var recovered = store.LoadAll().FirstOrDefault(x => x.Number == a.Number);
                Check("recupera desde copia", recovered != null && recovered.ClientName.StartsWith("José"), recovered == null ? "" : recovered.ClientName);
                a.ClientName = "José Ñáñez"; store.Save(a);

                var old = Environment.GetEnvironmentVariable("FICHAS_ANTES");
                var newer = Environment.GetEnvironmentVariable("FICHAS_DESPUES");
                if (!string.IsNullOrEmpty(old) && File.Exists(old) && File.Exists(newer))
                {
                    var ra = Reports.Parse(old);
                    var rb = Reports.Parse(newer);
                    Check("informe leído", ra.Verdict.Length > 0 && ra.Findings.Count > 5, ra.Findings.Count + " hallazgos: " + ra.Verdict);
                    var cmp = Reports.Compare(ra, rb);
                    log.AppendLine("Comparación: " + cmp.Count + " cambios");
                    foreach (var c in cmp.Take(12)) log.AppendLine("   " + c.Title + ": " + c.Before + " > " + c.After + " = " + c.Change);
                }

                var w = Pdf.Wrap("palabra " + new string('x', 400) + " fin", 10, false, 200);
                Check("texto largo se corta", w.All(l => Pdf.Width(l, 10, false) <= 200.5f), w.Count + " líneas");
                SamplePdfs(store, dir, old, newer);
                File.WriteAllText(output, log.ToString(), new UTF8Encoding(true));
                return failed == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                log.AppendLine("EXCEPCIÓN " + ex);
                File.WriteAllText(output, log.ToString(), new UTF8Encoding(true));
                return 2;
            }
        }
    }
}
