using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace ChequeadorPC
{
    // Orden de gravedad: el peor estado de una sección es el mayor valor.
    public enum St { Info = 0, Ok = 1, Unknown = 2, Warn = 3, Bad = 4 }

    public class Finding
    {
        public St Status;
        public string Title, Plain, Action, Tech, SectionTitle;
        public Finding(St status, string title, string plain, string action, string tech)
        {
            Status = status; Title = title; Plain = plain; Action = action; Tech = tech;
        }
    }

    public class Section
    {
        public string Key, Title;
        public Action<Section> Body;
        public Ctx Ctx;
        public volatile bool Running;
        readonly object gate = new object();
        readonly List<Finding> items = new List<Finding>();
        bool sealedOff;

        public Section(Ctx ctx, string key, string title, Action<Section> body)
        {
            Ctx = ctx; Key = key; Title = title; Body = body;
        }

        public void Add(St st, string title, string plain, string action = null, string tech = null)
        {
            if (st == St.Unknown && string.IsNullOrEmpty(action))
                action = Ctx.IsAdmin
                    ? "Compruébalo a mano o con otra herramienta. El resto de la revisión no se ve afectado."
                    : "Pulsa «Reabrir como administrador» y repite la revisión. Sin esos permisos Windows oculta este dato.";
            lock (gate)
            {
                if (sealedOff) return;
                items.Add(new Finding(st, title, plain, action, tech) { SectionTitle = Title });
            }
            Ctx.Touch();
        }

        // Un fallo en una comprobación nunca debe tumbar las demás de la sección.
        public void Safe(string title, Action work)
        {
            try { work(); }
            catch (Exception ex)
            {
                bool denied = ex is UnauthorizedAccessException ||
                    (ex is ManagementException && ((ManagementException)ex).ErrorCode == ManagementStatus.AccessDenied);
                Add(St.Unknown, title,
                    denied ? "Windows no permitió leer este dato con los permisos actuales."
                           : "No se pudo leer este dato en este equipo.",
                    null, ex.GetType().Name + ": " + ex.Message);
            }
        }

        public void Seal(string reason)
        {
            lock (gate)
            {
                if (sealedOff) return;
                if (Running && reason != null)
                    items.Add(new Finding(St.Unknown, "Revisión incompleta", reason,
                        "Pulsa «Repetir revisión». Si vuelve a ocurrir, este componente puede estar bloqueando a Windows: anótalo para el técnico.",
                        null) { SectionTitle = Title });
                sealedOff = true;
                Running = false;
            }
            Ctx.Touch();
        }

        public List<Finding> All()
        {
            var list = new List<Finding>();
            lock (gate) list.AddRange(items);
            list.AddRange(Ctx.ManualFor(Key, Title));
            return list;
        }

        public St Worst()
        {
            var all = All();
            if (all.Count == 0) return Running ? St.Info : St.Unknown;
            return all.Max(f => f.Status);
        }
    }

    public class Ctx
    {
        public readonly bool IsAdmin;
        public readonly Lazy<List<Dictionary<string, object>>> Pnp;
        public readonly Lazy<bool> IsPortable;
        int version;
        readonly object manualGate = new object();
        readonly Dictionary<string, List<KeyValuePair<string, Finding>>> manual =
            new Dictionary<string, List<KeyValuePair<string, Finding>>>();

        public Ctx()
        {
            try
            {
                IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { IsAdmin = false; }
            Pnp = new Lazy<List<Dictionary<string, object>>>(() => Wmi.Q(@"root\cimv2", "SELECT * FROM Win32_PnPEntity", 40));
            IsPortable = new Lazy<bool>(DetectPortable);
        }

        public int Version { get { return Thread.VolatileRead(ref version); } }
        public void Touch() { Interlocked.Increment(ref version); }

        static bool DetectPortable()
        {
            try
            {
                foreach (var d in Wmi.Q(@"root\cimv2", "SELECT PCSystemType FROM Win32_ComputerSystem", 15))
                    if (Wmi.L(d, "PCSystemType") == 2) return true;
            }
            catch { }
            try
            {
                int[] kinds = { 8, 9, 10, 11, 12, 14, 18, 21, 30, 31, 32 };
                foreach (var d in Wmi.Q(@"root\cimv2", "SELECT ChassisTypes FROM Win32_SystemEnclosure", 15))
                {
                    var arr = d.ContainsKey("ChassisTypes") ? d["ChassisTypes"] as Array : null;
                    if (arr != null)
                        foreach (var v in arr)
                            if (kinds.Contains(Convert.ToInt32(v))) return true;
                }
            }
            catch { }
            try { return Wmi.Q(@"root\cimv2", "SELECT Name FROM Win32_Battery", 15).Count > 0; }
            catch { return false; }
        }

        // Las pruebas manuales sobreviven a «Repetir revisión».
        public void SetManual(string sectionKey, string testKey, St st, string title, string plain, string action)
        {
            lock (manualGate)
            {
                List<KeyValuePair<string, Finding>> list;
                if (!manual.TryGetValue(sectionKey, out list))
                    manual[sectionKey] = list = new List<KeyValuePair<string, Finding>>();
                list.RemoveAll(p => p.Key == testKey);
                list.Add(new KeyValuePair<string, Finding>(testKey, new Finding(st, title, plain, action, null)));
            }
            Touch();
        }

        public List<Finding> ManualFor(string sectionKey, string sectionTitle)
        {
            lock (manualGate)
            {
                List<KeyValuePair<string, Finding>> list;
                if (!manual.TryGetValue(sectionKey, out list)) return new List<Finding>();
                foreach (var p in list) p.Value.SectionTitle = sectionTitle;
                return list.Select(p => p.Value).ToList();
            }
        }
    }

    // Lanza todas las secciones a la vez, cada una en su hilo y con límite de tiempo.
    public class Engine
    {
        public const int SectionTimeoutSeconds = 90;
        public readonly List<Section> Sections;
        public Engine(Ctx ctx) { Sections = Probes.Build(ctx); }

        public void Start()
        {
            foreach (var s in Sections) s.Running = true;
            foreach (var s in Sections)
            {
                var section = s;
                var t = new Thread(() =>
                {
                    try { section.Body(section); }
                    catch (Exception ex)
                    {
                        section.Add(St.Unknown, "Error en la revisión",
                            "Esta parte no se pudo completar, pero las demás siguen funcionando.", null, ex.ToString());
                    }
                    finally
                    {
                        if (section.All().Count == 0)
                            section.Add(St.Unknown, "Sin datos", "Windows no devolvió información sobre este componente.");
                        section.Seal(null);
                    }
                });
                t.IsBackground = true;
                t.SetApartmentState(ApartmentState.MTA);
                t.Start();
            }
            var dog = new Thread(() =>
            {
                var limit = DateTime.UtcNow.AddSeconds(SectionTimeoutSeconds);
                while (DateTime.UtcNow < limit && !Done) Thread.Sleep(500);
                StopNow("Esta parte tardó demasiado y se dejó de esperar.");
            });
            dog.IsBackground = true;
            dog.Start();
        }

        public bool Done { get { return Sections.All(s => !s.Running); } }

        public void StopNow(string reason)
        {
            foreach (var s in Sections)
                if (s.Running) s.Seal(reason);
        }
    }

    public static class Wmi
    {
        static ManagementObjectSearcher Searcher(string ns, string wql, int seconds)
        {
            var scope = new ManagementScope(ns, new ConnectionOptions
            {
                Timeout = TimeSpan.FromSeconds(seconds),
                EnablePrivileges = true
            });
            var opts = new EnumerationOptions
            {
                Timeout = TimeSpan.FromSeconds(seconds),
                ReturnImmediately = true,
                Rewindable = false
            };
            return new ManagementObjectSearcher(scope, new ObjectQuery(wql), opts);
        }

        static Dictionary<string, object> ToDict(ManagementBaseObject mo)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in mo.Properties)
            {
                try { d[p.Name] = p.Value; } catch { }
            }
            return d;
        }

        public static List<Dictionary<string, object>> Q(string ns, string wql, int seconds = 20)
        {
            var list = new List<Dictionary<string, object>>();
            using (var s = Searcher(ns, wql, seconds))
            using (var r = s.Get())
                foreach (ManagementBaseObject mo in r)
                    using (mo) list.Add(ToDict(mo));
            return list;
        }

        // Igual que Q, pero una clase ausente o sin permiso cuenta como «sin datos».
        public static List<Dictionary<string, object>> QOrEmpty(string ns, string wql, int seconds = 20)
        {
            try { return Q(ns, wql, seconds); }
            catch { return new List<Dictionary<string, object>>(); }
        }

        public static Dictionary<string, object> Related(string ns, string wql, string relatedClass, Func<Dictionary<string, object>, bool> match)
        {
            using (var s = Searcher(ns, wql, 15))
            using (var r = s.Get())
                foreach (ManagementObject mo in r)
                    using (mo)
                    {
                        if (!match(ToDict(mo))) continue;
                        foreach (ManagementBaseObject rel in mo.GetRelated(relatedClass))
                            using (rel) return ToDict(rel);
                    }
            return null;
        }

        public static string S(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v).Trim();
        }

        public static long L(Dictionary<string, object> d, string key, long fallback = -1)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt64(v); } catch { return fallback; }
        }

        public static DateTime? Date(Dictionary<string, object> d, string key)
        {
            var s = S(d, key);
            if (s.Length == 0) return null;
            try { return ManagementDateTimeConverter.ToDateTime(s); } catch { return null; }
        }
    }

    public class EvResult
    {
        public int Count; public DateTime? Latest; public string Sample; public bool Failed;
        public readonly List<string> Messages = new List<string>();
    }

    public static class Ev
    {
        public static EvResult Count(string provider, int[] ids, int days, string log = "System")
        {
            var res = new EvResult();
            try
            {
                string idFilter = ids == null || ids.Length == 0 ? "" :
                    " and (" + string.Join(" or ", ids.Select(i => "EventID=" + i)) + ")";
                string xpath = "*[System[Provider[@Name='" + provider + "']" + idFilter +
                               " and TimeCreated[timediff(@SystemTime) <= " + (days * 86400000L) + "]]]";
                var q = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
                using (var reader = new EventLogReader(q))
                {
                    EventRecord rec;
                    while (res.Count < 500 && (rec = reader.ReadEvent()) != null)
                    {
                        using (rec)
                        {
                            string text = null;
                            if (res.Count < 100) { try { text = rec.FormatDescription(); } catch { } }
                            if (res.Count == 0)
                            {
                                res.Latest = rec.TimeCreated;
                                res.Sample = text;
                            }
                            res.Messages.Add(text ?? "");
                            res.Count++;
                        }
                    }
                }
            }
            catch { res.Failed = true; }
            return res;
        }
    }

    public static class Exec
    {
        // Devuelve null si el programa no existe, falla al abrir o supera el tiempo.
        public static string Run(string file, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = "";
                    var reader = new Thread(() => { try { output = p.StandardOutput.ReadToEnd(); } catch { } });
                    reader.IsBackground = true;
                    reader.Start();
                    var err = new Thread(() => { try { p.StandardError.ReadToEnd(); } catch { } });
                    err.IsBackground = true;
                    err.Start();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return null;
                    }
                    reader.Join(1000);
                    return output;
                }
            }
            catch { return null; }
        }
    }

    public static class Fmt
    {
        public static string Bytes(double b)
        {
            if (b >= 1e12) return (b / 1e12).ToString("0.##") + " TB";
            if (b >= 1e9) return (b / 1e9).ToString("0.#") + " GB";
            if (b >= 1e6) return (b / 1e6).ToString("0") + " MB";
            return (b / 1e3).ToString("0") + " KB";
        }

        public static string GiB(double b) { return (b / 1073741824.0).ToString("0.#") + " GB"; }

        public static string Hours(long h)
        {
            if (h < 48) return h + " horas";
            double years = h / 8760.0;
            return h.ToString("N0") + " horas" + (years >= 0.2 ? " (" + years.ToString("0.#") + " años encendido)" : "");
        }

        public static string StatusWord(St s)
        {
            switch (s)
            {
                case St.Ok: return "Bueno";
                case St.Warn: return "Revisar";
                case St.Bad: return "Falla";
                case St.Unknown: return "Sin comprobar";
                default: return "Información";
            }
        }

        public static string StatusGlyph(St s)
        {
            switch (s)
            {
                case St.Ok: return "✓";
                case St.Warn: return "!";
                case St.Bad: return "✕";
                case St.Unknown: return "?";
                default: return "i";
            }
        }

        public static string Verdict(List<Section> sections, out string detail, out St overall)
        {
            var all = sections.SelectMany(s => s.All()).ToList();
            int bad = all.Count(f => f.Status == St.Bad);
            int warn = all.Count(f => f.Status == St.Warn);
            int unk = all.Count(f => f.Status == St.Unknown);
            var parts = new List<string>();
            if (bad > 0) parts.Add(bad + (bad == 1 ? " falla" : " fallas"));
            if (warn > 0) parts.Add(warn + (warn == 1 ? " punto por revisar" : " puntos por revisar"));
            if (unk > 0) parts.Add(unk + (unk == 1 ? " dato sin comprobar" : " datos sin comprobar"));
            detail = parts.Count == 0 ? "Revisado sin novedades. Las pruebas manuales confirman lo que Windows no puede ver."
                                      : string.Join(", ", parts) + ". Abre «Resumen» para ver qué hacer.";
            overall = bad > 0 ? St.Bad : warn > 0 ? St.Warn : unk > 0 ? St.Unknown : St.Ok;
            if (bad > 0) return "Este equipo tiene problemas que requieren atención";
            if (warn > 0) return "Este equipo funciona, con puntos por revisar";
            if (unk > 0) return "No se encontraron fallas, pero faltan datos";
            return "Este equipo está en buen estado";
        }
    }
}
