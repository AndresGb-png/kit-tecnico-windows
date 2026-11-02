using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FichaServicio
{
    [DataContract]
    public class Item
    {
        [DataMember] public string Description = "";
        [DataMember] public decimal Quantity = 1;
        [DataMember] public decimal UnitPrice;
        public decimal Total { get { return Quantity * UnitPrice; } }
    }

    [DataContract]
    public class Attachment
    {
        [DataMember] public string Kind = "";   // antes, despues u otro
        [DataMember] public string File = "";
        [DataMember] public string Verdict = "";
        [DataMember] public string Detail = "";
        [DataMember] public string Date = "";
    }

    [DataContract]
    public class HistoryEntry
    {
        [DataMember] public string Date = "";
        [DataMember] public string Text = "";
    }

    [DataContract]
    public class Ficha
    {
        [DataMember] public string Number = "";
        [DataMember] public string Status = Statuses.Received;
        [DataMember] public string ReceivedOn = "";
        [DataMember] public string DeliveredOn = "";

        [DataMember] public string ClientName = "";
        [DataMember] public string ClientPhone = "";
        [DataMember] public string ClientEmail = "";
        [DataMember] public string ClientDocument = "";
        [DataMember] public string ClientAddress = "";

        [DataMember] public string DeviceType = "";
        [DataMember] public string DeviceBrandModel = "";
        [DataMember] public string DeviceSerial = "";
        [DataMember] public string DeviceSpecs = "";
        [DataMember] public string Accessories = "";
        [DataMember] public string Condition = "";
        [DataMember] public string Problem = "";

        [DataMember] public string Diagnosis = "";
        [DataMember] public string WorkDone = "";
        [DataMember] public string Notes = "";
        [DataMember] public List<Item> Items = new List<Item>();
        [DataMember] public decimal Labor;
        [DataMember] public decimal Discount;
        [DataMember] public decimal TaxPercent;
        [DataMember] public decimal Advance;
        [DataMember] public int WarrantyDays = 30;
        [DataMember] public List<Attachment> Attachments = new List<Attachment>();
        [DataMember] public List<HistoryEntry> History = new List<HistoryEntry>();

        public decimal PartsTotal { get { return Items.Sum(i => i.Total); } }
        public decimal Subtotal { get { return Math.Max(0, PartsTotal + Labor - Discount); } }
        public decimal Tax { get { return Math.Round(Subtotal * TaxPercent / 100m, 2, MidpointRounding.AwayFromZero); } }
        public decimal Total { get { return Subtotal + Tax; } }
        public decimal Balance { get { return Total - Advance; } }

        public DateTime? WarrantyEnd
        {
            get
            {
                DateTime d;
                if (WarrantyDays <= 0 || !DateTime.TryParseExact(DeliveredOn, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return null;
                return d.Date.AddDays(WarrantyDays);
            }
        }

        public string WarrantyText
        {
            get
            {
                var end = WarrantyEnd;
                if (end == null) return "";
                return (end.Value >= DateTime.Today ? "Vigente hasta " : "Venció el ") + end.Value.ToString("yyyy-MM-dd");
            }
        }

        public Attachment Report(string kind) { return Attachments.LastOrDefault(a => a.Kind == kind); }

        public void Normalize()
        {
            if (Items == null) Items = new List<Item>();
            if (Attachments == null) Attachments = new List<Attachment>();
            if (History == null) History = new List<HistoryEntry>();
            foreach (var f in GetType().GetFields())
                if (f.FieldType == typeof(string) && f.GetValue(this) == null) f.SetValue(this, "");
        }
    }

    public static class Statuses
    {
        public const string Received = "Recibido", Delivered = "Entregado";
        public static readonly string[] All = { Received, "En diagnóstico", "Esperando repuesto", "En reparación", "Listo para entregar", Delivered, "Cancelado" };
    }

    [DataContract]
    public class Settings
    {
        [DataMember] public string BusinessName = "";
        [DataMember] public string TechnicianName = "";
        [DataMember] public string Phone = "";
        [DataMember] public string Email = "";
        [DataMember] public string Address = "";
        [DataMember] public string BusinessId = "";
        [DataMember] public string LogoFile = "";
        [DataMember] public string Currency = "$";
        [DataMember] public int Decimals = 2;
        [DataMember] public decimal TaxPercent;
        [DataMember] public int WarrantyDays = 30;
        [DataMember] public string PageSize = "Carta";
        [DataMember] public string ReceptionTerms = "El cliente confirma haber entregado el equipo con los accesorios y el estado aquí descritos. Se recomienda conservar una copia de seguridad de su información.";
        [DataMember] public string DeliveryTerms = "La garantía cubre únicamente el trabajo y los repuestos indicados en este comprobante.";
    }

    public static class Json
    {
        public static string Write<T>(T value)
        {
            var s = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream())
            {
                s.WriteObject(ms, value);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static T Read<T>(string text)
        {
            var s = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                return (T)s.ReadObject(ms);
        }
    }

    public class Store
    {
        public readonly string Root;
        public readonly List<string> Unreadable = new List<string>();
        public Settings Settings = new Settings();

        string FichasDir { get { return Path.Combine(Root, "fichas"); } }
        public string AttachmentsDir(string number) { return Path.Combine(Root, "adjuntos", number); }
        string SettingsPath { get { return Path.Combine(Root, "ajustes.json"); } }

        Store(string root) { Root = root; }

        // Junto al programa si se puede (USB); si no, en Documentos.
        public static Store Open()
        {
            var candidates = new List<string>();
            string forced = Environment.GetEnvironmentVariable("FICHAS_DATOS");
            if (!string.IsNullOrEmpty(forced)) candidates.Add(forced);
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fichas de servicio"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Fichas de servicio"));
            candidates.Add(Path.Combine(Path.GetTempPath(), "Fichas de servicio"));
            Exception last = null;
            foreach (var c in candidates)
                try
                {
                    Directory.CreateDirectory(Path.Combine(c, "fichas"));
                    string probe = Path.Combine(c, ".prueba_" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(probe, "x");
                    File.Delete(probe);
                    var store = new Store(c);
                    store.LoadSettings();
                    return store;
                }
                catch (Exception ex) { last = ex; }
            throw new IOException("No hay ninguna carpeta donde guardar las fichas.", last);
        }

        void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath)) Settings = Json.Read<Settings>(File.ReadAllText(SettingsPath, Encoding.UTF8)) ?? new Settings();
            }
            catch { Settings = new Settings(); }
            foreach (var f in typeof(Settings).GetFields())
                if (f.FieldType == typeof(string) && f.GetValue(Settings) == null) f.SetValue(Settings, "");
            if (Settings.Currency.Length == 0) Settings.Currency = "$";
        }

        public void SaveSettings() { WriteSafe(SettingsPath, Json.Write(Settings)); }

        // Escribe aparte y sustituye al final: un corte de luz nunca deja la ficha a medias.
        static void WriteSafe(string path, string text)
        {
            string temp = path + ".tmp";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }

        public List<Ficha> LoadAll()
        {
            Unreadable.Clear();
            var list = new List<Ficha>();
            foreach (var f in Directory.GetFiles(FichasDir, "*.json"))
            {
                Ficha x = null;
                try { x = Json.Read<Ficha>(File.ReadAllText(f, Encoding.UTF8)); }
                catch { }
                if (x == null && File.Exists(f + ".bak"))
                    try { x = Json.Read<Ficha>(File.ReadAllText(f + ".bak", Encoding.UTF8)); }
                    catch { }
                if (x == null || string.IsNullOrEmpty(x.Number)) { Unreadable.Add(Path.GetFileName(f)); continue; }
                x.Normalize();
                list.Add(x);
            }
            return list.OrderByDescending(x => x.ReceivedOn).ThenByDescending(x => x.Number).ToList();
        }

        // Reserva el número creando el archivo: dos ventanas abiertas no pueden repetirlo.
        public Ficha NewFicha()
        {
            string year = DateTime.Now.ToString("yyyy");
            int max = 0;
            foreach (var f in Directory.GetFiles(FichasDir, "F-" + year + "-*.json"))
            {
                var m = Regex.Match(Path.GetFileName(f), @"^F-\d{4}-(\d+)\.json$");
                int n;
                if (m.Success && int.TryParse(m.Groups[1].Value, out n) && n > max) max = n;
            }
            for (int i = max + 1; i < max + 1000; i++)
            {
                string number = "F-" + year + "-" + i.ToString("0000");
                string path = Path.Combine(FichasDir, number + ".json");
                try
                {
                    using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                }
                catch (IOException) { continue; }
                var x = new Ficha
                {
                    Number = number, ReceivedOn = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    WarrantyDays = Settings.WarrantyDays, TaxPercent = Settings.TaxPercent
                };
                x.History.Add(new HistoryEntry { Date = x.ReceivedOn, Text = "Ficha creada" });
                Save(x);
                return x;
            }
            throw new IOException("No se pudo reservar un número de ficha.");
        }

        public void Save(Ficha x) { WriteSafe(Path.Combine(FichasDir, x.Number + ".json"), Json.Write(x)); }

        public string AddAttachment(Ficha x, string source, string kind)
        {
            string dir = AttachmentsDir(x.Number);
            Directory.CreateDirectory(dir);
            string name = kind + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + Path.GetExtension(source);
            string target = Path.Combine(dir, name);
            File.Copy(source, target, true);
            return name;
        }

        public string AttachmentPath(Ficha x, Attachment a) { return Path.Combine(AttachmentsDir(x.Number), a.File); }

        public int BackupTo(string folder)
        {
            string target = Path.Combine(folder, "Fichas_copia_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm"));
            int count = 0;
            foreach (var f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = f.Substring(Root.TrimEnd('\\').Length + 1);
                string dst = Path.Combine(target, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(f, dst, true);
                count++;
            }
            return count;
        }
    }

    public static class Money
    {
        // Acepta 1.500,50 / 1,500.50 / 1500 / 1.500 (tres cifras tras el separador son miles).
        public static bool TryParse(string text, out decimal value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return true;
            string s = Regex.Replace(text, @"[^\d,.\-]", "");
            if (s.Length == 0 || s == "-") return false;
            int comma = s.LastIndexOf(','), dot = s.LastIndexOf('.');
            int last = Math.Max(comma, dot);
            if (comma >= 0 && dot >= 0)
            {
                char dec = s[last];
                s = s.Replace(dec == ',' ? "." : ",", "").Replace(',', '.');
            }
            else if (last >= 0)
            {
                char sep = s[last];
                int digitsAfter = s.Length - last - 1;
                int count = s.Count(c => c == sep);
                if (count > 1 || digitsAfter == 3) s = s.Replace(sep.ToString(), "");
                else s = s.Replace(',', '.');
            }
            return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        public static string Show(decimal v, Settings s)
        {
            string fmt = s.Decimals <= 0 ? "#,##0" : "#,##0.00";
            return (v < 0 ? "-" : "") + s.Currency + (s.Currency.Length > 1 ? " " : "") + Math.Abs(v).ToString(fmt, CultureInfo.CurrentCulture);
        }

        public static string Plain(decimal v, Settings s)
        {
            return v.ToString(s.Decimals <= 0 ? "#,##0" : "#,##0.00", CultureInfo.CurrentCulture);
        }
    }

    public class CompareRow { public string Title, Before, After, Change; }

    public class ReportData
    {
        public string Program = "", Verdict = "", Detail = "";
        public List<KeyValuePair<string, string>> Findings = new List<KeyValuePair<string, string>>();
    }

    public static class Reports
    {
        static string T(string html) { return WebUtility.HtmlDecode(Regex.Replace(html ?? "", "<[^>]+>", " ")).Trim(); }

        public static ReportData Parse(string path)
        {
            var html = File.ReadAllText(path, Encoding.UTF8);
            var r = new ReportData();
            r.Program = T(Regex.Match(html, "<title>(.*?)</title>", RegexOptions.Singleline).Groups[1].Value);
            r.Verdict = T(Regex.Match(html, "<h1>(.*?)</h1>", RegexOptions.Singleline).Groups[1].Value);
            r.Detail = T(Regex.Match(html, "</h1>\\s*<p>(.*?)</p>", RegexOptions.Singleline).Groups[1].Value);
            var seen = new Dictionary<string, int>();
            foreach (Match m in Regex.Matches(html, "<span class=w[^>]*>(.*?)</span><b>(.*?)</b>", RegexOptions.Singleline))
            {
                string word = Regex.Replace(T(m.Groups[1].Value), @"^\S+\s+", "");
                string title = T(m.Groups[2].Value);
                int n;
                seen.TryGetValue(title, out n);
                seen[title] = n + 1;
                r.Findings.Add(new KeyValuePair<string, string>(n == 0 ? title : title + " (" + (n + 1) + ")", word));
            }
            return r;
        }

        static int Rank(string word)
        {
            switch (word)
            {
                case "Falla": return 3;
                case "Revisar": return 2;
                case "Sin comprobar": return 1;
                default: return 0;
            }
        }

        // Solo lo que cambió: una lista con todo lo igual no le dice nada al cliente.
        public static List<CompareRow> Compare(ReportData before, ReportData after)
        {
            var rows = new List<CompareRow>();
            var a = new Dictionary<string, string>();
            foreach (var f in after.Findings) a[f.Key] = f.Value;
            var b = new Dictionary<string, string>();
            foreach (var f in before.Findings) b[f.Key] = f.Value;
            foreach (var f in before.Findings)
            {
                string now;
                if (!a.TryGetValue(f.Key, out now) || now == f.Value) continue;
                rows.Add(new CompareRow
                {
                    Title = f.Key, Before = f.Value, After = now,
                    Change = Rank(now) < Rank(f.Value) ? "Mejoró" : Rank(now) > Rank(f.Value) ? "Empeoró" : "Cambió"
                });
            }
            foreach (var f in after.Findings)
                if (!b.ContainsKey(f.Key) && Rank(f.Value) >= 2)
                    rows.Add(new CompareRow { Title = f.Key, Before = "No aparecía", After = f.Value, Change = "Nuevo" });
            return rows.OrderBy(r => r.Change == "Empeoró" || r.Change == "Nuevo" ? 0 : r.Change == "Mejoró" ? 1 : 2).ToList();
        }
    }

    public static class ThisPc
    {
        public static void Read(Ficha x)
        {
            var parts = new List<string>();
            Func<string, string, string> first = (wql, prop) =>
            {
                try
                {
                    using (var s = new ManagementObjectSearcher(wql))
                        foreach (ManagementObject m in s.Get())
                            return Convert.ToString(m[prop]).Trim();
                }
                catch { }
                return "";
            };
            string brand = first("SELECT Manufacturer FROM Win32_ComputerSystem", "Manufacturer");
            string model = first("SELECT Model FROM Win32_ComputerSystem", "Model");
            string serial = first("SELECT IdentifyingNumber FROM Win32_ComputerSystemProduct", "IdentifyingNumber");
            string type = first("SELECT PCSystemType FROM Win32_ComputerSystem", "PCSystemType");
            string cpu = first("SELECT Name FROM Win32_Processor", "Name");
            string ram = first("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem", "TotalPhysicalMemory");
            string os = first("SELECT Caption FROM Win32_OperatingSystem", "Caption");
            string[] junk = { "to be filled by o.e.m.", "default string", "system serial number", "system product name", "system manufacturer", "none", "unknown", "o.e.m." };
            Func<string, string> clean = v => junk.Contains(v.ToLowerInvariant()) ? "" : v;
            string full = (clean(brand) + " " + clean(model)).Trim();
            if (full.Length > 0) x.DeviceBrandModel = full;
            if (clean(serial).Length > 0) x.DeviceSerial = clean(serial);
            x.DeviceType = type == "2" ? "Portátil" : "Escritorio";
            if (cpu.Length > 0) parts.Add(cpu);
            // Los módulos dan los GB instalados; el total visible descuenta lo que reserva la gráfica.
            long bytes = 0;
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory"))
                    foreach (ManagementObject m in s.Get()) bytes += Convert.ToInt64(m["Capacity"]);
            }
            catch { bytes = 0; }
            if (bytes <= 0) long.TryParse(ram, out bytes);
            if (bytes > 0) parts.Add(Math.Round(bytes / 1073741824.0) + " GB de RAM");
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Model,Size FROM Win32_DiskDrive"))
                    foreach (ManagementObject m in s.Get())
                    {
                        long size;
                        if (long.TryParse(Convert.ToString(m["Size"]), out size) && size > 0) parts.Add("Disco " + Math.Round(size / 1e9) + " GB");
                    }
            }
            catch { }
            if (os.Length > 0) parts.Add(os);
            x.DeviceSpecs = string.Join(", ", parts);
        }
    }

    public static class Csv
    {
        // Sin el apóstrofo, Excel ejecutaría como fórmula un texto que empiece por = + - @.
        public static string Cell(string s)
        {
            s = s ?? "";
            if (s.Length > 0 && "=+-@\t\r".IndexOf(s[0]) >= 0) s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
        }

        public static void Export(string path, IEnumerable<Ficha> fichas, Settings st)
        {
            string sep = CultureInfo.CurrentCulture.TextInfo.ListSeparator;
            var sb = new StringBuilder();
            string[] head = { "Número", "Recibido", "Estado", "Entregado", "Cliente", "Teléfono", "Correo", "Documento", "Equipo", "Serie", "Problema", "Trabajo", "Total", "Abono", "Saldo", "Garantía" };
            sb.AppendLine(string.Join(sep, head.Select(Cell)));
            foreach (var x in fichas)
                sb.AppendLine(string.Join(sep, new[]
                {
                    Cell(x.Number), Cell(x.ReceivedOn), Cell(x.Status), Cell(x.DeliveredOn), Cell(x.ClientName), Cell(x.ClientPhone), Cell(x.ClientEmail), Cell(x.ClientDocument),
                    Cell(x.DeviceBrandModel), Cell(x.DeviceSerial), Cell(x.Problem), Cell(x.WorkDone), Cell(Money.Plain(x.Total, st)), Cell(Money.Plain(x.Advance, st)), Cell(Money.Plain(x.Balance, st)), Cell(x.WarrantyText)
                }));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
    }

    public static class Chequeador
    {
        public static string Find()
        {
            string p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChequeadorPC.exe");
            string forced = Environment.GetEnvironmentVariable("FICHAS_CHEQUEADOR");
            if (!string.IsNullOrEmpty(forced)) p = forced;
            return File.Exists(p) ? p : null;
        }

        // Pide administrador para que el informe sea completo; si se rechaza, lo hace con menos datos.
        public static string Run(string exe)
        {
            string output = Path.Combine(Path.GetTempPath(), "Diagnostico_" + Guid.NewGuid().ToString("N") + ".html");
            string args = "--reporte \"" + output + "\"";
            Process p = null;
            try { p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }); }
            catch (System.ComponentModel.Win32Exception) { }
            if (p == null) p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true });
            using (p)
                if (!p.WaitForExit(180000)) return null;
            return File.Exists(output) ? output : null;
        }
    }
}
