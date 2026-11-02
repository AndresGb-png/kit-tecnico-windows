using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Win32;

namespace ChequeadorPC
{
    public static class Probes
    {
        const string CIM = @"root\cimv2", ROOTWMI = @"root\wmi", STORAGE = @"root\microsoft\windows\storage";

        public static List<Section> Build(Ctx c)
        {
            return new List<Section>
            {
                new Section(c, "equipo", "Equipo y placa base", Board),
                new Section(c, "cpu", "Procesador y temperatura", Cpu),
                new Section(c, "ram", "Memoria RAM y ranuras", Ram),
                new Section(c, "discos", "Discos", Disks),
                new Section(c, "video", "Gráfica y pantalla", Video),
                new Section(c, "sonido", "Sonido y micrófono", Sound),
                new Section(c, "camara", "Cámara", Camera),
                new Section(c, "entrada", "Teclado, ratón y táctil", Input),
                new Section(c, "puertos", "Puertos y conectores", Ports),
                new Section(c, "red", "Red, Wi-Fi y Bluetooth", Network),
                new Section(c, "bateria", "Batería y cargador", Battery),
                new Section(c, "so", "Sistema operativo", Os),
                new Section(c, "dispositivos", "Otros dispositivos", Devices)
            };
        }

        // ---------- utilidades ----------

        static string Clean(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            string t = s.Trim();
            string low = t.ToLowerInvariant();
            string[] junk = { "to be filled by o.e.m.", "default string", "not specified", "system serial number",
                              "none", "unknown", "o.e.m.", "not applicable", "no asset tag" };
            return junk.Contains(low) ? "" : t;
        }

        static string Join(params string[] parts) { return string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p))); }

        static string PnpErrorText(long code)
        {
            switch (code)
            {
                case 1: return "no está bien configurado";
                case 3: return "su controlador puede estar dañado o faltó memoria";
                case 10: return "no pudo iniciarse";
                case 12: return "no encuentra recursos libres";
                case 14: return "necesita reiniciar el equipo";
                case 18: return "hay que reinstalar su controlador";
                case 19: return "su configuración en Windows está dañada";
                case 21: return "Windows lo está quitando";
                case 22: return "está desactivado";
                case 24: return "no está presente o no funciona";
                case 28: return "no tiene controlador instalado";
                case 29: return "está desactivado en el firmware";
                case 31: return "Windows no pudo cargar su controlador";
                case 32: return "su controlador está desactivado";
                case 37: return "su controlador no pudo iniciarse";
                case 39: return "su controlador falta o está dañado";
                case 41: return "su controlador cargó, pero Windows no encuentra el hardware";
                case 43: return "Windows lo detuvo porque informó de un problema";
                case 45: return "no está conectado ahora";
                case 47: return "se preparó para extraerlo; desconéctalo y vuelve a conectarlo";
                case 48: return "su controlador está bloqueado por incompatible";
                case 52: return "Windows no pudo verificar la firma de su controlador";
                default: return "informa un error (código " + code + ")";
            }
        }

        static string PnpTech(Dictionary<string, object> d)
        {
            return Join("ID:", Wmi.S(d, "DeviceID")) + "\nCódigo de error: " + Wmi.L(d, "ConfigManagerErrorCode", 0);
        }

        static IEnumerable<Dictionary<string, object>> PnpByName(Ctx c, params string[] words)
        {
            return c.Pnp.Value.Where(d =>
            {
                string n = Wmi.S(d, "Name").ToLowerInvariant();
                return words.Any(w => n.Contains(w));
            });
        }

        static IEnumerable<Dictionary<string, object>> PnpByClass(Ctx c, params string[] classes)
        {
            return c.Pnp.Value.Where(d =>
                classes.Contains(Wmi.S(d, "PNPClass"), StringComparer.OrdinalIgnoreCase) ||
                classes.Contains(Wmi.S(d, "Service"), StringComparer.OrdinalIgnoreCase));
        }

        // Añade un hallazgo por dispositivo PnP, explicando el error si lo hay.
        static void PnpFinding(Section s, Dictionary<string, object> d, string okPlain)
        {
            long err = Wmi.L(d, "ConfigManagerErrorCode", 0);
            string name = Wmi.S(d, "Name");
            if (err == 0)
                s.Add(St.Ok, name, okPlain, null, PnpTech(d));
            else
                s.Add(err == 22 || err == 45 ? St.Warn : St.Bad, name,
                    "Este dispositivo " + PnpErrorText(err) + ".",
                    err == 22 ? "Actívalo en el Administrador de dispositivos si lo necesitas."
                              : "Abre el Administrador de dispositivos, reinstala o actualiza su controlador y reinicia. Si sigue igual, puede ser una falla física.",
                    PnpTech(d));
        }

        static bool PrivacyDenied(string capability)
        {
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using (var k = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + capability))
                        if (k != null && string.Equals(k.GetValue("Value") as string, "Deny", StringComparison.OrdinalIgnoreCase))
                            return true;
                }
                catch { }
            }
            return false;
        }

        // ---------- equipo y placa ----------

        static void Board(Section s)
        {
            s.Safe("Equipo", () =>
            {
                var cs = Wmi.Q(CIM, "SELECT Manufacturer,Model,Name,PCSystemType FROM Win32_ComputerSystem").FirstOrDefault();
                var pr = Wmi.QOrEmpty(CIM, "SELECT IdentifyingNumber FROM Win32_ComputerSystemProduct").FirstOrDefault();
                string serial = Clean(Wmi.S(pr, "IdentifyingNumber"));
                s.Add(St.Info, Join(Clean(Wmi.S(cs, "Manufacturer")), Clean(Wmi.S(cs, "Model"))).Length > 0
                        ? Join(Clean(Wmi.S(cs, "Manufacturer")), Clean(Wmi.S(cs, "Model"))) : "Equipo de marca no identificada",
                    (s.Ctx.IsPortable.Value ? "Equipo portátil" : "Equipo de escritorio") + " llamado " + Wmi.S(cs, "Name") + ".",
                    null, serial.Length > 0 ? "Número de serie: " + serial : null);
            });

            s.Safe("Placa base", () =>
            {
                var b = Wmi.Q(CIM, "SELECT Manufacturer,Product,Version,SerialNumber,Status FROM Win32_BaseBoard").FirstOrDefault();
                string name = Join(Clean(Wmi.S(b, "Manufacturer")), Clean(Wmi.S(b, "Product")));
                string status = Wmi.S(b, "Status");
                bool bad = status.Length > 0 && !status.Equals("OK", StringComparison.OrdinalIgnoreCase);
                s.Add(bad ? St.Warn : St.Ok, "Placa base" + (name.Length > 0 ? ": " + name : ""),
                    bad ? "La placa informa el estado «" + status + "»." :
                          "La placa responde. Los errores físicos graves aparecerían abajo, en «Errores de hardware».",
                    bad ? "Pide a un técnico que revise la placa." : null,
                    "Versión: " + Wmi.S(b, "Version") + "\nSerie: " + Wmi.S(b, "SerialNumber"));
            });

            s.Safe("Errores de hardware", () =>
            {
                var whea = Ev.Count("Microsoft-Windows-WHEA-Logger", null, 90);
                if (whea.Failed)
                    s.Add(St.Unknown, "Errores de hardware", "No se pudo leer el registro de eventos de Windows.");
                else if (whea.Count == 0)
                    s.Add(St.Ok, "Errores de hardware", "El procesador, la memoria y la placa no informaron errores en los últimos 90 días.");
                else
                    s.Add(whea.Count >= 10 ? St.Bad : St.Warn, "Errores de hardware",
                        whea.Count + " errores físicos registrados en 90 días (procesador, memoria o bus PCIe).",
                        "Revisa la RAM y la temperatura y actualiza la BIOS. Si siguen apareciendo, lleva el equipo a un técnico con este informe.",
                        "Último: " + whea.Latest + "\n" + whea.Sample);
            });

            s.Safe("BIOS", () =>
            {
                var b = Wmi.Q(CIM, "SELECT Manufacturer,SMBIOSBIOSVersion,ReleaseDate,SMBIOSMajorVersion,SMBIOSMinorVersion FROM Win32_BIOS").FirstOrDefault();
                var date = Wmi.Date(b, "ReleaseDate");
                string secure = "desconocido";
                try
                {
                    using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
                    {
                        var v = k == null ? null : k.GetValue("UEFISecureBootEnabled");
                        secure = v == null ? "no disponible (arranque antiguo o no compatible)" : Convert.ToInt32(v) == 1 ? "activado" : "desactivado";
                    }
                }
                catch { }
                string age = date.HasValue ? "Es de " + date.Value.ToString("MMMM 'de' yyyy") + "." : "Su fecha no está disponible.";
                s.Add(St.Info, "BIOS / UEFI", age + " Arranque seguro: " + secure + ".", null,
                    Join(Wmi.S(b, "Manufacturer"), Wmi.S(b, "SMBIOSBIOSVersion")) + "\nSMBIOS " + Wmi.S(b, "SMBIOSMajorVersion") + "." + Wmi.S(b, "SMBIOSMinorVersion"));
            });

            s.Safe("Chip de seguridad TPM", () =>
            {
                var t = Wmi.Q(@"root\cimv2\security\microsofttpm", "SELECT IsEnabled_InitialValue,SpecVersion FROM Win32_Tpm").FirstOrDefault();
                if (t == null)
                    s.Add(St.Info, "Chip de seguridad TPM", "No se encontró. Windows 11 lo necesita.", "Puede estar apagado en la BIOS; búscalo como TPM, PTT o fTPM.");
                else
                    s.Add(St.Ok, "Chip de seguridad TPM", "Presente y detectado.", null, "Versión: " + Wmi.S(t, "SpecVersion"));
            });

            s.Safe("Ventiladores", () =>
            {
                var fans = Wmi.QOrEmpty(CIM, "SELECT Name,Status,ActiveCooling FROM Win32_Fan");
                if (fans.Count == 0)
                    s.Add(St.Info, "Ventiladores", "Este equipo no informa a Windows de sus ventiladores.", "Compruébalo de oído: bajo carga deberían sonar. Una temperatura alta también apunta a ellos.");
                else
                    foreach (var f in fans)
                    {
                        string st = Wmi.S(f, "Status");
                        bool ok = st.Length == 0 || st.Equals("OK", StringComparison.OrdinalIgnoreCase);
                        s.Add(ok ? St.Ok : St.Warn, Wmi.S(f, "Name"), ok ? "Sin errores informados." : "Estado informado: " + st + ".",
                            ok ? null : "Limpia el polvo y revisa que gire.");
                    }
            });
        }

        // ---------- procesador ----------

        static void Cpu(Section s)
        {
            s.Safe("Procesador", () =>
            {
                foreach (var p in Wmi.Q(CIM, "SELECT Name,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed,CurrentClockSpeed,LoadPercentage,Status,SocketDesignation,VirtualizationFirmwareEnabled FROM Win32_Processor"))
                {
                    string st = Wmi.S(p, "Status");
                    bool ok = st.Length == 0 || st.Equals("OK", StringComparison.OrdinalIgnoreCase);
                    long load = Wmi.L(p, "LoadPercentage");
                    s.Add(ok ? St.Ok : St.Bad, Wmi.S(p, "Name"),
                        ok ? Wmi.L(p, "NumberOfCores") + " núcleos, " + Wmi.L(p, "NumberOfLogicalProcessors") + " hilos" +
                             (load >= 0 ? ", en uso al " + load + "%." : ".")
                           : "El procesador informa el estado «" + st + "».",
                        ok ? null : "Consulta a un técnico.",
                        "Velocidad máxima: " + Wmi.L(p, "MaxClockSpeed") + " MHz, actual: " + Wmi.L(p, "CurrentClockSpeed") + " MHz\n" +
                        "Zócalo: " + Wmi.S(p, "SocketDesignation") + "\nVirtualización activa: " + Wmi.S(p, "VirtualizationFirmwareEnabled"));
                }
            });
            s.Safe("Temperatura", () => Temperature(s));
        }

        static void AddTemp(Section s, string title, double c, string note, string tech)
        {
            string val = c.ToString("0") + " °C";
            if (c >= 95)
                s.Add(St.Bad, title, val + ": demasiado caliente. El equipo puede apagarse o dañarse.",
                    "Apágalo y límpialo por dentro. Hay que revisar el ventilador y cambiar la pasta térmica.", tech);
            else if (c >= 80)
                s.Add(St.Warn, title, val + ": caliente. Es normal solo bajo mucha carga." + note,
                    "Si el equipo está en reposo, limpia el polvo y comprueba que el ventilador gire.", tech);
            else
                s.Add(St.Ok, title, val + ": normal." + note, null, tech);
        }

        static void Temperature(Section s)
        {
            // 1. Si hay un monitor abierto, tiene la lectura real del sensor del procesador.
            foreach (var ns in new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" })
            {
                var sensors = Wmi.QOrEmpty(ns, "SELECT Name,Value,Identifier FROM Sensor WHERE SensorType='Temperature'", 5)
                    .Where(d => Wmi.S(d, "Identifier").ToLowerInvariant().Contains("cpu")).ToList();
                if (sensors.Count == 0) continue;
                var best = sensors.OrderByDescending(d => Wmi.S(d, "Name").ToLowerInvariant().Contains("package") ? 1 : 0)
                    .ThenByDescending(d => Convert.ToDouble(d["Value"])).First();
                AddTemp(s, "Temperatura del procesador", Convert.ToDouble(best["Value"]), "",
                    "Fuente: " + ns + " / " + Wmi.S(best, "Name"));
                return;
            }

            // 2. Sensores de zona térmica del firmware.
            bool denied = false;
            var zones = new List<KeyValuePair<string, double>>();
            try
            {
                foreach (var z in Wmi.Q(ROOTWMI, "SELECT InstanceName,CurrentTemperature FROM MSAcpi_ThermalZoneTemperature", 8))
                {
                    double t = Wmi.L(z, "CurrentTemperature", 0) / 10.0 - 273.15;
                    if (t > 5 && t < 115) zones.Add(new KeyValuePair<string, double>(Wmi.S(z, "InstanceName"), t));
                }
            }
            catch (System.Management.ManagementException ex)
            {
                denied = ex.ErrorCode == System.Management.ManagementStatus.AccessDenied;
            }

            if (zones.Count > 0)
            {
                var hot = zones.OrderByDescending(z => z.Value).First();
                AddTemp(s, "Temperatura interna", hot.Value,
                    " Es el sensor térmico de la placa, muy cerca del procesador pero no siempre igual a él.",
                    "Fuente: ACPI\n" + string.Join("\n", zones.Select(z => z.Key + " = " + z.Value.ToString("0.0") + " °C")));
                return;
            }

            if (denied)
                s.Add(St.Unknown, "Temperatura del procesador", "Windows solo entrega este dato con permisos de administrador.");
            else
                s.Add(St.Unknown, "Temperatura del procesador",
                    "El fabricante de este equipo no expone el sensor a Windows de forma directa.",
                    "Abre LibreHardwareMonitor, deja la opción de WMI activa y repite la revisión: este programa leerá de ahí la temperatura. También puedes verla en la BIOS.");
        }

        // ---------- memoria ----------

        static void Ram(Section s)
        {
            long visibleKb = -1;
            s.Safe("Memoria total", () =>
            {
                var os = Wmi.Q(CIM, "SELECT TotalVisibleMemorySize,FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
                visibleKb = Wmi.L(os, "TotalVisibleMemorySize");
                long freeKb = Wmi.L(os, "FreePhysicalMemory");
                double used = visibleKb > 0 ? 100.0 * (visibleKb - freeKb) / visibleKb : 0;
                s.Add(used >= 90 ? St.Warn : St.Ok, "Memoria en uso",
                    Fmt.GiB(visibleKb * 1024.0) + " utilizables, " + used.ToString("0") + "% ocupada ahora mismo.",
                    used >= 90 ? "Cierra programas. Si es habitual, al equipo le conviene más memoria." : null);
            });

            s.Safe("Ranuras de memoria", () => RamSlots(s, visibleKb));

            s.Safe("Prueba rápida de memoria", () => QuickRamTest(s));

            s.Safe("Diagnóstico de memoria de Windows", () =>
            {
                var pass = Ev.Count("Microsoft-Windows-MemoryDiagnostics-Results", new[] { 1201 }, 3650);
                var fail = Ev.Count("Microsoft-Windows-MemoryDiagnostics-Results", new[] { 1202 }, 3650);
                if (pass.Count + fail.Count == 0)
                    s.Add(St.Info, "Prueba completa de memoria", "Nunca se ha ejecutado o no quedó registro.",
                        "Es la única forma fiable de detectar un módulo dañado. Usa el botón «Prueba completa de memoria»; requiere reiniciar y tarda unos 20 minutos.");
                else
                {
                    bool failed = fail.Count > 0 && (!pass.Latest.HasValue || fail.Latest > pass.Latest);
                    var r = failed ? fail : pass;
                    s.Add(failed ? St.Bad : St.Ok, "Prueba completa de memoria",
                        (failed ? "La última prueba de Windows encontró errores." : "La última prueba de Windows no encontró errores.") +
                        " Fecha: " + r.Latest + ".",
                        failed ? "Prueba los módulos uno a uno para localizar el dañado y cámbialo." : null, r.Sample);
                }
            });
        }

        static void RamSlots(Section s, long visibleKb)
        {
            List<MemSlot> slots = null;
            try { slots = Smbios.ReadMemorySlots(); } catch { }

            if (slots == null || slots.Count == 0)
            {
                // Sin tabla del firmware, WMI solo sabe de ranuras ocupadas.
                slots = new List<MemSlot>();
                foreach (var m in Wmi.Q(CIM, "SELECT DeviceLocator,BankLabel,Capacity,Speed,Manufacturer,PartNumber FROM Win32_PhysicalMemory"))
                    slots.Add(new MemSlot
                    {
                        Locator = Wmi.S(m, "DeviceLocator"), Bank = Wmi.S(m, "BankLabel"),
                        SizeMb = Wmi.L(m, "Capacity", 0) / 1048576, Speed = (int)Wmi.L(m, "Speed", 0),
                        Maker = Wmi.S(m, "Manufacturer"), Part = Wmi.S(m, "PartNumber")
                    });
                long declared = Wmi.L(Wmi.QOrEmpty(CIM, "SELECT MemoryDevices FROM Win32_PhysicalMemoryArray").FirstOrDefault(), "MemoryDevices", slots.Count);
                for (long i = slots.Count; i < declared; i++)
                    slots.Add(new MemSlot { Locator = "Ranura " + (i + 1), SizeMb = 0 });
            }

            if (slots.Count == 0)
            {
                s.Add(St.Unknown, "Ranuras de memoria", "El firmware no entrega el detalle de las ranuras (común en máquinas virtuales).",
                    "Abre el equipo y revísalas a la vista.");
                return;
            }

            foreach (var m in slots)
            {
                string where = (Join(m.Bank, m.Locator)).Trim();
                if (where.Length == 0) where = "Ranura";
                if (m.SizeMb > 0)
                {
                    string desc = Fmt.GiB(m.SizeMb * 1048576.0) + (m.Type.Length > 0 ? " " + m.Type : "") +
                                  (m.Speed > 0 ? " a " + (m.ConfiguredSpeed > 0 ? m.ConfiguredSpeed : m.Speed) + " MHz" : "");
                    s.Add(St.Ok, where + ": módulo detectado",
                        desc + (m.Soldered ? ", soldada a la placa." : ". La ranura responde y el módulo se reconoce."), null,
                        "Fabricante: " + m.Maker + "\nModelo: " + m.Part + "\nSerie: " + m.Serial + "\nFormato: " + m.Form +
                        "\nVelocidad nominal: " + m.Speed + " MHz, configurada: " + m.ConfiguredSpeed + " MHz");
                }
                else
                    s.Add(St.Info, where + ": sin módulo detectado",
                        "Está vacía, o tiene un módulo o una ranura que no responde: Windows no puede distinguirlos.",
                        "Si hay un módulo puesto, límpialo y cámbialo de ranura. Si en otra ranura funciona, esta está dañada.");
            }

            long installed = slots.Sum(m => m.SizeMb);
            if (visibleKb > 0 && installed > 0)
            {
                double visibleMb = visibleKb / 1024.0;
                if (visibleMb < installed * 0.80)
                    s.Add(St.Warn, "Memoria incompleta",
                        Fmt.GiB(installed * 1048576.0) + " instalados pero solo " + Fmt.GiB(visibleMb * 1048576.0) + " utilizables.",
                        "Revisa la gráfica integrada en la BIOS (reserva memoria) y que el Windows sea de 64 bits. Si no, un módulo puede estar fallando.");
            }
            var sizes = slots.Where(m => m.SizeMb > 0).Select(m => m.SizeMb).Distinct().Count();
            if (sizes > 1)
                s.Add(St.Info, "Módulos de distinto tamaño", "Funciona, pero puede rendir menos que con módulos iguales.");
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr VirtualAlloc(IntPtr addr, UIntPtr size, uint type, uint protect);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualFree(IntPtr addr, UIntPtr size, uint type);

        static unsafe void QuickRamTest(Section s)
        {
            var info = Wmi.Q(CIM, "SELECT FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault();
            long freeBytes = Wmi.L(info, "FreePhysicalMemory", 0) * 1024;
            long size = Math.Min(512L * 1048576, freeBytes / 3);
            if (IntPtr.Size == 4) size = Math.Min(size, 256L * 1048576);
            if (size < 32 * 1048576)
            {
                s.Add(St.Unknown, "Prueba rápida de memoria", "Hay muy poca memoria libre para hacerla sin afectar al equipo.",
                    "Cierra programas y repite la revisión.");
                return;
            }
            IntPtr mem = VirtualAlloc(IntPtr.Zero, new UIntPtr((ulong)size), 0x3000, 0x04);
            if (mem == IntPtr.Zero)
            {
                s.Add(St.Unknown, "Prueba rápida de memoria", "Windows no entregó memoria para la prueba.", "Cierra programas y repite la revisión.");
                return;
            }
            long errors = 0;
            try
            {
                ulong[] patterns = { 0x5555555555555555UL, 0xAAAAAAAAAAAAAAAAUL, 0xFFFFFFFFFFFFFFFFUL, 0UL };
                ulong* start = (ulong*)mem.ToPointer();
                long count = size / 8;
                foreach (ulong pat in patterns)
                {
                    for (long i = 0; i < count; i++) start[i] = pat ^ (ulong)i;
                    for (long i = 0; i < count; i++) if (start[i] != (pat ^ (ulong)i)) errors++;
                }
            }
            finally { VirtualFree(mem, UIntPtr.Zero, 0x8000); }
            string mb = (size / 1048576) + " MB";
            if (errors > 0)
                s.Add(St.Bad, "Prueba rápida de memoria", errors + " errores al escribir y leer " + mb + ". La memoria RAM está fallando.",
                    "Haz la «Prueba completa de memoria» y prueba los módulos uno a uno.");
            else
                s.Add(St.Ok, "Prueba rápida de memoria",
                    mb + " escritos y leídos sin errores. Solo prueba la memoria libre, así que no descarta un módulo dañado.");
        }

        // ---------- discos ----------

        static void Disks(Section s)
        {
            List<Dictionary<string, object>> drives = null;
            s.Safe("Discos", () => { drives = Wmi.Q(CIM, "SELECT Index,Model,Size,InterfaceType,MediaType,Status,PNPDeviceID,SerialNumber,FirmwareRevision FROM Win32_DiskDrive"); });
            if (drives == null) return;

            if (drives.Count == 0)
                s.Add(St.Bad, "Discos", "Windows no detecta ningún disco físico.", "Revisa el cable o la conexión del disco. Si sigue sin aparecer, entra en la BIOS y mira si lo detecta.");

            var msft = Wmi.QOrEmpty(STORAGE, "SELECT * FROM MSFT_PhysicalDisk");
            var predict = Wmi.QOrEmpty(ROOTWMI, "SELECT InstanceName,PredictFailure FROM MSStorageDriver_FailurePredictStatus");
            var smart = Wmi.QOrEmpty(ROOTWMI, "SELECT InstanceName,VendorSpecific FROM MSStorageDriver_FailurePredictData");

            foreach (var d in drives.OrderBy(x => Wmi.L(x, "Index")))
            {
                var drive = d;
                string title = "Disco " + Wmi.L(d, "Index") + ": " + Wmi.S(d, "Model");
                s.Safe(title, () => OneDisk(s, drive, title, msft, predict, smart));
            }

            s.Safe("Unidades", () =>
            {
                foreach (var v in Wmi.Q(CIM, "SELECT DeviceID,VolumeName,FileSystem,Size,FreeSpace,VolumeDirty FROM Win32_LogicalDisk WHERE DriveType=3"))
                {
                    double size = Wmi.L(v, "Size", 0), free = Wmi.L(v, "FreeSpace", 0);
                    if (size <= 0) continue;
                    double pct = free / size * 100;
                    bool dirty = Wmi.S(v, "VolumeDirty").Equals("True", StringComparison.OrdinalIgnoreCase);
                    St st = pct < 5 ? St.Bad : pct < 12 || dirty ? St.Warn : St.Ok;
                    s.Add(st, "Unidad " + Wmi.S(v, "DeviceID") + " " + Wmi.S(v, "VolumeName"),
                        Fmt.Bytes(free) + " libres de " + Fmt.Bytes(size) + " (" + pct.ToString("0") + "% libre)." +
                        (dirty ? " Windows la marcó para revisar por errores." : ""),
                        dirty ? "Ejecuta «chkdsk " + Wmi.S(v, "DeviceID") + " /f» y reinicia." : pct < 12 ? "Libera espacio: con el disco casi lleno Windows se vuelve lento e inestable." : null,
                        "Formato: " + Wmi.S(v, "FileSystem"));
                }
            });

            s.Safe("Errores de lectura", () =>
            {
                var e = Ev.Count("disk", new[] { 7, 11, 51, 52, 153 }, 30);
                var n = Ev.Count("Ntfs", new[] { 55, 98, 130 }, 30);
                // Un error de un disco que ya no está (memoria USB retirada) no es culpa de los actuales.
                var present = new HashSet<string>(drives.Select(x => Wmi.L(x, "Index").ToString()));
                int gone = e.Messages.Count(t =>
                {
                    var m = Regex.Match(t, @"Harddisk(\d+)", RegexOptions.IgnoreCase);
                    return m.Success && !present.Contains(m.Groups[1].Value);
                });
                int total = e.Count - gone + n.Count;
                string tech = "disk: " + e.Count + " (de discos ya desconectados: " + gone + "), Ntfs: " + n.Count + "\n" + (e.Sample ?? n.Sample);
                if (e.Failed && n.Failed) s.Add(St.Unknown, "Errores de lectura", "No se pudo leer el registro de eventos de Windows.");
                else if (total > 0)
                    s.Add(total >= 10 ? St.Bad : St.Warn, "Errores de lectura y escritura",
                        total + (total == 1 ? " error de disco registrado" : " errores de disco registrados") + " en 30 días.",
                        "Haz una copia de seguridad ahora. Revisa el cable y, si los errores siguen, cambia el disco.", tech);
                else if (gone > 0)
                    s.Add(St.Info, "Errores de lectura y escritura",
                        "Los discos actuales no tienen errores. Hay " + gone + (gone == 1 ? " error" : " errores") + " de una unidad que ya no está conectada, como una memoria USB.",
                        "Si vuelves a conectar esa unidad, cópiale los datos importantes: puede estar fallando.", tech);
                else s.Add(St.Ok, "Errores de lectura y escritura", "Sin errores registrados en 30 días.");
            });
        }

        static void OneDisk(Section s, Dictionary<string, object> d, string title,
            List<Dictionary<string, object>> msft, List<Dictionary<string, object>> predict, List<Dictionary<string, object>> smart)
        {
            int index = (int)Wmi.L(d, "Index");
            string pnp = Wmi.S(d, "PNPDeviceID");
            var m = msft.FirstOrDefault(x => Wmi.S(x, "DeviceId") == index.ToString());
            long bus = Wmi.L(m, "BusType", -1), media = Wmi.L(m, "MediaType", 0);
            string iface = Wmi.S(d, "InterfaceType");
            string model = Wmi.S(d, "Model");
            string modelLow = model.ToLowerInvariant();

            bool isNvme = bus == 17 || modelLow.Contains("nvme") || pnp.ToUpperInvariant().Contains("NVME");
            bool isUsb = bus == 7 || iface.Equals("USB", StringComparison.OrdinalIgnoreCase);
            bool? penalty = DiskIo.SeekPenalty(index);
            bool isSsd = isNvme || media == 4 || (media == 0 && penalty == false) || modelLow.Contains("ssd");
            bool isHdd = !isSsd && (media == 3 || penalty == true);

            string kind = isNvme ? "SSD NVMe (M.2 o PCIe)" :
                          isSsd ? (isUsb ? "SSD externo USB" : "SSD SATA") :
                          isHdd ? (isUsb ? "Disco duro externo USB" : "Disco duro HDD") :
                          isUsb ? "Unidad USB externa" : "Disco de tipo no identificado";
            string plain = kind + " de " + Fmt.Bytes(Wmi.L(d, "Size", 0)) + ".";

            var tech = new StringBuilder();
            tech.AppendLine("Interfaz: " + iface + ", bus: " + bus + ", medio: " + media);
            tech.AppendLine("Serie: " + Wmi.S(d, "SerialNumber") + ", firmware: " + Wmi.S(d, "FirmwareRevision"));

            St st = St.Ok;
            string action = null;
            int? health = null;
            bool haveDeep = false;
            Action<St, string> raise = (ns, act) =>
            {
                if (ns > st) st = ns;
                if (act != null && action == null) action = act;
            };
            const string backup = "Haz una copia de seguridad cuanto antes y planea cambiar el disco.";

            // Estado general según Windows.
            long hs = Wmi.L(m, "HealthStatus", -1);
            if (hs == 2) { raise(St.Bad, backup); plain += " Windows lo marca como defectuoso."; }
            else if (hs == 1) { raise(St.Warn, backup); plain += " Windows avisa de un posible problema."; }
            string status = Wmi.S(d, "Status");
            if (status.Length > 0 && !status.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                raise(status.Equals("Pred Fail", StringComparison.OrdinalIgnoreCase) ? St.Bad : St.Warn, backup);
                plain += " Estado informado: " + status + ".";
            }

            var pred = predict.FirstOrDefault(x => Wmi.S(x, "InstanceName").StartsWith(pnp, StringComparison.OrdinalIgnoreCase));
            if (pred != null && Wmi.S(pred, "PredictFailure").Equals("True", StringComparison.OrdinalIgnoreCase))
            {
                raise(St.Bad, backup);
                plain += " El propio disco predice que va a fallar.";
            }

            // NVMe: registro de salud del estándar.
            NvmeHealth nv = null;
            if (isNvme) { try { nv = DiskIo.ReadNvme(index); } catch { } }
            if (nv != null)
            {
                haveDeep = true;
                health = Math.Max(0, 100 - nv.PercentUsed);
                if (nv.CriticalWarning != 0) { raise(St.Bad, backup); plain += " El disco activó una alerta crítica."; }
                if (nv.AvailableSpare < nv.SpareThreshold) { raise(St.Bad, backup); plain += " Se quedó sin celdas de reserva."; }
                if (nv.MediaErrors > 0) { raise(St.Warn, backup); plain += " Tiene " + nv.MediaErrors + " errores de datos."; }
                if (nv.PowerOnHours > 0) plain += " Encendido: " + Fmt.Hours(nv.PowerOnHours) + ".";
                if (nv.TemperatureC > 0)
                {
                    plain += " Temperatura: " + nv.TemperatureC + " °C.";
                    if (nv.TemperatureC >= 75) raise(St.Warn, "Mejora la ventilación del equipo: el disco está muy caliente.");
                }
                tech.AppendLine("Alerta crítica: 0x" + nv.CriticalWarning.ToString("X2") + ", usado: " + nv.PercentUsed + "%");
                tech.AppendLine("Reserva disponible: " + nv.AvailableSpare + "% (umbral " + nv.SpareThreshold + "%)");
                tech.AppendLine("Datos escritos: " + Fmt.Bytes(nv.BytesWritten) + ", ciclos: " + nv.PowerCycles);
                tech.AppendLine("Apagados bruscos: " + nv.UnsafeShutdowns + ", errores de medio: " + nv.MediaErrors);
            }

            // SATA: atributos SMART clásicos.
            var data = smart.FirstOrDefault(x => Wmi.S(x, "InstanceName").StartsWith(pnp, StringComparison.OrdinalIgnoreCase));
            if (nv == null && data != null && data.ContainsKey("VendorSpecific") && data["VendorSpecific"] is byte[])
            {
                var attrs = SmartParser.Parse((byte[])data["VendorSpecific"]);
                if (attrs.Count > 0)
                {
                    haveDeep = true;
                    Func<int, long> raw = id => attrs.ContainsKey(id) ? attrs[id].Raw : 0;
                    long realloc = raw(5), pending = raw(197), uncorrect = raw(198);
                    int score = 100;
                    if (realloc > 0) { score -= (int)Math.Min(40, 5 + realloc / 2); raise(St.Warn, backup); plain += " Tiene " + realloc + " sectores dañados ya reemplazados."; }
                    if (pending > 0) { score -= (int)Math.Min(50, 20 + pending * 5); raise(St.Bad, backup); plain += " Tiene " + pending + " sectores inestables."; }
                    if (uncorrect > 0) { score -= (int)Math.Min(40, 20 + uncorrect); raise(St.Bad, backup); plain += " Tiene " + uncorrect + " sectores irrecuperables."; }

                    health = Math.Max(0, score);
                    if (isSsd)
                        foreach (int id in new[] { 231, 233, 177, 202, 169, 173 })
                            if (attrs.ContainsKey(id) && attrs[id].Current >= 1 && attrs[id].Current <= 100)
                            {
                                health = Math.Min(health.Value, attrs[id].Current);
                                break;
                            }

                    long hours = raw(9) & 0xFFFFFFFF;
                    if (hours > 0) plain += " Encendido: " + Fmt.Hours(hours) + ".";
                    int temp = attrs.ContainsKey(194) ? (int)(attrs[194].Raw & 0xFF) : attrs.ContainsKey(190) ? (int)(attrs[190].Raw & 0xFF) : 0;
                    if (temp > 0 && temp < 100)
                    {
                        plain += " Temperatura: " + temp + " °C.";
                        if (temp >= 60) raise(St.Warn, "Mejora la ventilación: el disco está muy caliente.");
                    }
                    tech.AppendLine("SMART: " + string.Join(" ", attrs.Values.OrderBy(a => a.Id)
                        .Where(a => new[] { 5, 9, 12, 177, 187, 190, 194, 197, 198, 199, 202, 231, 233 }.Contains(a.Id))
                        .Select(a => a.Id + "=" + a.Raw + "(" + a.Current + ")")));
                }
            }

            // Respaldo de Windows 8+ cuando no se pudo leer directamente.
            if (!haveDeep && m != null)
            {
                try
                {
                    string objId = Wmi.S(m, "ObjectId");
                    var rel = Wmi.Related(STORAGE, "SELECT * FROM MSFT_PhysicalDisk", "MSFT_StorageReliabilityCounter", x => Wmi.S(x, "ObjectId") == objId);
                    if (rel != null)
                    {
                        long wear = Wmi.L(rel, "Wear", -1), temp = Wmi.L(rel, "Temperature", 0), poh = Wmi.L(rel, "PowerOnHours", 0);
                        if (wear >= 0 && wear <= 100) { health = 100 - (int)wear; haveDeep = true; }
                        if (poh > 0) { plain += " Encendido: " + Fmt.Hours(poh) + "."; haveDeep = true; }
                        if (temp > 0) plain += " Temperatura: " + temp + " °C.";
                    }
                }
                catch { }
            }

            if (health.HasValue)
            {
                plain += " Salud" + (isSsd ? "" : " estimada") + ": " + health + "%.";
                if (health <= 10) raise(St.Bad, backup);
                else if (health <= 40) raise(St.Warn, backup);
            }

            if (!haveDeep && st == St.Ok)
            {
                if (isUsb)
                {
                    s.Add(St.Info, title, plain + " Windows no informa de fallas, pero las cajas USB suelen ocultar el porcentaje de salud.",
                        "Para ver el porcentaje, conéctalo directamente por dentro del equipo.", tech.ToString().Trim());
                    return;
                }
                s.Add(St.Unknown, title, plain + " Windows no informa de fallas, pero no se pudo leer el porcentaje de salud.", null, tech.ToString().Trim());
                return;
            }
            s.Add(st, title, plain, action, tech.ToString().Trim());
        }

        // ---------- gráfica y pantalla ----------

        static void Video(Section s)
        {
            s.Safe("Tarjeta gráfica", () =>
            {
                var cards = Wmi.Q(CIM, "SELECT Name,Status,ConfigManagerErrorCode,DriverVersion,DriverDate,AdapterRAM,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate,PNPDeviceID FROM Win32_VideoController");
                if (cards.Count == 0)
                    s.Add(St.Bad, "Tarjeta gráfica", "Windows no detecta ninguna tarjeta gráfica.", "Reinstala el controlador. Si es una tarjeta dedicada, comprueba que esté bien insertada.");
                foreach (var v in cards)
                {
                    long err = Wmi.L(v, "ConfigManagerErrorCode", 0);
                    string name = Wmi.S(v, "Name");
                    bool basic = name.ToLowerInvariant().Contains("basic display") || name.ToLowerInvariant().Contains("básico");
                    long w = Wmi.L(v, "CurrentHorizontalResolution", 0), h = Wmi.L(v, "CurrentVerticalResolution", 0);
                    string plain = w > 0 ? "Funciona a " + w + "×" + h + " y " + Wmi.L(v, "CurrentRefreshRate", 0) + " Hz." : "Detectada, sin pantalla activa ahora mismo.";
                    var date = Wmi.Date(v, "DriverDate");
                    string tech = "Controlador: " + Wmi.S(v, "DriverVersion") + (date.HasValue ? " (" + date.Value.ToString("yyyy-MM-dd") + ")" : "") + "\nID: " + Wmi.S(v, "PNPDeviceID");
                    if (err != 0)
                        s.Add(err == 22 ? St.Warn : St.Bad, name, "Este dispositivo " + PnpErrorText(err) + ".", "Reinstala el controlador desde la web del fabricante. Si sigue igual, la tarjeta puede estar dañada.", tech);
                    else if (basic)
                        s.Add(St.Warn, name, "Falta el controlador real: la imagen funciona, pero sin aceleración.", "Instala el controlador desde la web del fabricante o Windows Update.", tech);
                    else
                        s.Add(St.Ok, name, plain, null, tech);
                }
            });

            s.Safe("Temperatura gráfica", () =>
            {
                string smi = null;
                foreach (var candidate in new[] { "nvidia-smi", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"NVIDIA Corporation\NVSMI\nvidia-smi.exe") })
                {
                    smi = Exec.Run(candidate, "--query-gpu=name,temperature.gpu,utilization.gpu --format=csv,noheader,nounits", 6000);
                    if (!string.IsNullOrWhiteSpace(smi)) break;
                }
                if (string.IsNullOrWhiteSpace(smi)) return;
                foreach (var line in smi.Split('\n').Where(l => l.Contains(",")))
                {
                    var p = line.Split(',').Select(x => x.Trim()).ToArray();
                    double t;
                    if (p.Length >= 2 && double.TryParse(p[1], out t))
                        s.Add(t >= 90 ? St.Bad : t >= 80 ? St.Warn : St.Ok, "Temperatura de " + p[0], t.ToString("0") + " °C.",
                            t >= 80 ? "Limpia los ventiladores de la tarjeta." : null);
                }
            });

            s.Safe("Pantallas", () =>
            {
                var conn = Wmi.QOrEmpty(ROOTWMI, "SELECT InstanceName,VideoOutputTechnology FROM WmiMonitorConnectionParams", 8);
                if (conn.Count == 0)
                    s.Add(St.Info, "Pantallas", "Windows no entrega datos de los monitores en este equipo.", "Haz la prueba de pantalla para revisarla a la vista.");
                foreach (var c in conn)
                    s.Add(St.Ok, "Pantalla conectada por " + OutputName(Wmi.L(c, "VideoOutputTechnology")), "Windows la detecta correctamente.", null, Wmi.S(c, "InstanceName"));
            });
        }

        public static string OutputName(long t)
        {
            switch (t)
            {
                case 0: return "VGA";
                case 4: return "DVI";
                case 5: return "HDMI";
                case 6: return "LVDS (pantalla integrada)";
                case 10: return "DisplayPort";
                case 11: return "DisplayPort integrado (pantalla interna)";
                case 15: return "Miracast (inalámbrica)";
                case 2147483648L: return "conexión interna (pantalla integrada)";
                default: return "otra conexión (" + t + ")";
            }
        }

        // ---------- sonido ----------

        static List<KeyValuePair<string, int>> AudioEndpoints(string flow)
        {
            var list = new List<KeyValuePair<string, int>>();
            using (var root = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\" + flow))
            {
                if (root == null) return list;
                foreach (var id in root.GetSubKeyNames())
                {
                    using (var k = root.OpenSubKey(id))
                    using (var p = k == null ? null : k.OpenSubKey("Properties"))
                    {
                        if (k == null) continue;
                        int state = Convert.ToInt32(k.GetValue("DeviceState", 0));
                        string name = p == null ? "" : Convert.ToString(p.GetValue("{a45c254e-df1c-4efd-8020-67d146a850e0},2", ""));
                        string adapter = p == null ? "" : Convert.ToString(p.GetValue("{b3f8fa53-0004-438e-9003-51a46e139bfc},6", ""));
                        list.Add(new KeyValuePair<string, int>(Join(name, adapter.Length > 0 ? "(" + adapter + ")" : ""), state));
                    }
                }
            }
            return list;
        }

        static void Sound(Section s)
        {
            s.Safe("Tarjeta de sonido", () =>
            {
                var devs = Wmi.Q(CIM, "SELECT Name,Status,ConfigManagerErrorCode,DeviceID FROM Win32_SoundDevice");
                if (devs.Count == 0)
                    s.Add(St.Bad, "Tarjeta de sonido", "Windows no detecta ningún dispositivo de sonido.", "Instala el controlador de audio. Si está activo y no aparece, revisa que no esté apagado en la BIOS.");
                foreach (var d in devs) PnpFinding(s, d, "Detectado y sin errores.");
            });

            s.Safe("Servicio de audio", () =>
            {
                var svc = Wmi.Q(CIM, "SELECT State FROM Win32_Service WHERE Name='Audiosrv'").FirstOrDefault();
                if (svc != null && !Wmi.S(svc, "State").Equals("Running", StringComparison.OrdinalIgnoreCase))
                    s.Add(St.Bad, "Servicio de audio de Windows", "Está detenido, así que el equipo no sonará aunque el hardware esté bien.", "Abre «Servicios», busca «Audio de Windows» e inícialo.");
            });

            s.Safe("Salidas de sonido", () =>
            {
                var o = AudioEndpoints("Render");
                var active = o.Where(e => e.Value == 1).ToList();
                if (active.Count == 0)
                    s.Add(St.Warn, "Altavoces y auriculares", "No hay ninguna salida de sonido activa.", "Conecta unos auriculares o abre Configuración de sonido y activa una salida.");
                else
                    s.Add(St.Ok, "Altavoces y auriculares", active.Count + " salida(s) activa(s). Para confirmar que suenan, usa «Probar altavoces».", null, string.Join("\n", active.Select(e => e.Key)));
            });

            s.Safe("Entradas de sonido", () =>
            {
                var i = AudioEndpoints("Capture");
                var active = i.Where(e => e.Value == 1).ToList();
                if (active.Count == 0)
                    s.Add(s.Ctx.IsPortable.Value ? St.Warn : St.Info, "Micrófono", "No hay ningún micrófono activo.",
                        s.Ctx.IsPortable.Value ? "Actívalo en Configuración de sonido. Si no aparece, revisa el controlador de audio." : "En equipos de escritorio es normal si no conectaste uno.");
                else
                    s.Add(St.Ok, "Micrófono", active.Count + " entrada(s) activa(s). Para confirmar que captan voz, usa «Probar micrófono».", null, string.Join("\n", active.Select(e => e.Key)));
                if (PrivacyDenied("microphone"))
                    s.Add(St.Warn, "Permiso del micrófono", "La privacidad de Windows bloquea el micrófono.", "Actívalo en Configuración, Privacidad, Micrófono.");
            });
        }

        // ---------- cámara ----------

        static void Camera(Section s)
        {
            s.Safe("Cámara", () =>
            {
                var cams = PnpByClass(s.Ctx, "Camera").ToList();
                cams.AddRange(PnpByClass(s.Ctx, "Image").Where(d => !Wmi.S(d, "Name").ToLowerInvariant().Contains("scan") && !Wmi.S(d, "Name").ToLowerInvariant().Contains("print")));
                bool portable = s.Ctx.IsPortable.Value;
                if (cams.Count == 0)
                {
                    if (portable)
                        s.Add(St.Warn, "Cámara", "Este portátil no muestra ninguna cámara.",
                            "Busca una tecla o tapa de cámara y mira si está apagada en la BIOS. Si sigue sin aparecer, el cable o la cámara pueden estar dañados.");
                    else
                        s.Add(St.Info, "Cámara", "No hay ninguna cámara conectada. Es normal en un equipo de escritorio.");
                    return;
                }
                foreach (var c in cams)
                    PnpFinding(s, c, "Windows la reconoce. Para confirmar que da imagen, usa «Probar cámara»; solo se enciende si lo pides.");
                if (PrivacyDenied("webcam"))
                    s.Add(St.Warn, "Permiso de la cámara", "La privacidad de Windows bloquea la cámara.", "Actívala en Configuración, Privacidad, Cámara.");
            });
        }

        // ---------- entrada ----------

        static void Input(Section s)
        {
            bool portable = s.Ctx.IsPortable.Value;
            s.Safe("Teclado", () =>
            {
                var k = Wmi.Q(CIM, "SELECT Name,Description,Status,ConfigManagerErrorCode,DeviceID FROM Win32_Keyboard");
                if (k.Count == 0)
                    s.Add(St.Bad, "Teclado", "Windows no detecta ningún teclado.", portable ? "Conecta uno por USB para comprobar el equipo. El interno puede tener el cable suelto." : "Conéctalo a otro USB o prueba con otro teclado.");
                foreach (var d in k)
                {
                    long err = Wmi.L(d, "ConfigManagerErrorCode", 0);
                    string name = Wmi.S(d, "Description").Length > 0 ? Wmi.S(d, "Description") : Wmi.S(d, "Name");
                    if (err == 0)
                        s.Add(St.Ok, name, "Detectado. Para revisar tecla por tecla, usa «Probar teclado».", null, Wmi.S(d, "DeviceID"));
                    else
                        s.Add(St.Bad, name, "Este teclado " + PnpErrorText(err) + ".", "Reinstala el controlador o prueba con otro teclado.", Wmi.S(d, "DeviceID"));
                }
            });

            s.Safe("Ratón y touchpad", () =>
            {
                var m = Wmi.Q(CIM, "SELECT Name,Manufacturer,Status,ConfigManagerErrorCode,DeviceID,NumberOfButtons FROM Win32_PointingDevice");
                if (m.Count == 0)
                    s.Add(portable ? St.Bad : St.Warn, "Ratón o touchpad", "Windows no detecta ninguno.", "Conecta un ratón por USB. En un portátil, revisa la tecla que apaga el touchpad.");
                foreach (var d in m)
                {
                    long err = Wmi.L(d, "ConfigManagerErrorCode", 0);
                    if (err == 0)
                        s.Add(St.Ok, Wmi.S(d, "Name"), "Detectado. Para revisar botones y movimiento, usa «Probar ratón».", null, Wmi.S(d, "DeviceID"));
                    else
                        s.Add(St.Bad, Wmi.S(d, "Name"), "Este dispositivo " + PnpErrorText(err) + ".", "Reinstala su controlador.", Wmi.S(d, "DeviceID"));
                }
            });

            s.Safe("Pantalla táctil", () =>
            {
                foreach (var d in PnpByName(s.Ctx, "touch screen", "pantalla táctil", "touchscreen", "digitalizador"))
                    PnpFinding(s, d, "Detectada.");
            });
        }

        // ---------- puertos ----------

        static string PortTypeName(long t, string text)
        {
            string low = (text ?? "").ToLowerInvariant();
            if (low.Contains("hdmi")) return "HDMI";
            if (low.Contains("displayport") || low.Contains(" dp")) return "DisplayPort";
            if (low.Contains("thunderbolt")) return "Thunderbolt";
            if (low.Contains("type-c") || low.Contains("type c") || low.Contains("usb-c")) return "USB-C";
            if (low.Contains("usb")) return "USB";
            if (low.Contains("lan") || low.Contains("rj45") || low.Contains("network")) return "Red RJ45";
            if (low.Contains("audio") || low.Contains("mic") || low.Contains("line") || low.Contains("headphone") || low.Contains("speaker")) return "Audio";
            if (low.Contains("vga") || low.Contains("video")) return "Vídeo VGA";
            if (low.Contains("dvi")) return "DVI";
            if (low.Contains("sata")) return "SATA interno";
            switch (t)
            {
                case 16: return "USB";
                case 31: return "Red RJ45";
                case 15: return "PS/2";
                case 14: return "Ratón";
                case 13: return "Teclado";
                case 17: return "FireWire";
                case 9: return "Serie";
                case 1: case 2: case 3: return "Paralelo";
                case 28: return "Vídeo";
                case 29: return "Audio";
                default: return "";
            }
        }

        static void Ports(Section s)
        {
            s.Add(St.Info, "Cómo leer esta sección",
                "Windows ve los controladores de los puertos, no cada agujero físico. Un puerto solo se confirma cuando le conectas algo.",
                "Pulsa «Probar puertos» y conecta un dispositivo en cada uno: el programa te dirá lo que detecta.");

            s.Safe("Lista de puertos del fabricante", () =>
            {
                var groups = new SortedDictionary<string, int>();
                foreach (var p in Wmi.QOrEmpty(CIM, "SELECT InternalReferenceDesignator,ExternalReferenceDesignator,PortType FROM Win32_PortConnector"))
                {
                    string name = PortTypeName(Wmi.L(p, "PortType"), Wmi.S(p, "ExternalReferenceDesignator") + " " + Wmi.S(p, "InternalReferenceDesignator"));
                    if (name.Length == 0 || name.Contains("interno")) continue;
                    int n; groups.TryGetValue(name, out n); groups[name] = n + 1;
                }
                if (groups.Count > 0)
                    s.Add(St.Info, "Puertos que declara el fabricante", string.Join(", ", groups.Select(g => g.Value + " " + g.Key)) + ". Esta lista puede ser incompleta.");
                else
                    s.Add(St.Info, "Puertos que declara el fabricante", "Este equipo no publica la lista. Cuéntalos a la vista.");
            });

            s.Safe("Puertos USB", () =>
            {
                var ctrl = Wmi.Q(CIM, "SELECT Name,Status,ConfigManagerErrorCode,DeviceID FROM Win32_USBController");
                if (ctrl.Count == 0)
                    s.Add(St.Bad, "Puertos USB", "No se detecta ningún controlador USB.", "Revisa que el USB no esté apagado en la BIOS y reinstala los controladores de la placa.");
                foreach (var c in ctrl)
                {
                    string n = Wmi.S(c, "Name"), low = n.ToLowerInvariant();
                    string ver = low.Contains("3.2") ? "USB 3.2" : low.Contains("3.1") ? "USB 3.1" : low.Contains("3.0") || low.Contains("3.10") || low.Contains("xhci") || low.Contains("extensible") ? "USB 3.x" :
                                 low.Contains("enhanced") || low.Contains("ehci") || low.Contains("2.0") ? "USB 2.0" : "USB";
                    long err = Wmi.L(c, "ConfigManagerErrorCode", 0);
                    if (err == 0) s.Add(St.Ok, "Controlador " + ver, "Funciona. Los puertos que dependen de él deberían responder.", null, n + "\n" + Wmi.S(c, "DeviceID"));
                    else s.Add(St.Bad, "Controlador " + ver, "Este controlador " + PnpErrorText(err) + ". Sus puertos pueden no funcionar.", "Reinstala el controlador de la placa o del chipset.", n + "\n" + Wmi.S(c, "DeviceID"));
                }
                var devices = s.Ctx.Pnp.Value.Where(d => Wmi.S(d, "DeviceID").StartsWith(@"USB\VID", StringComparison.OrdinalIgnoreCase) && !Wmi.S(d, "DeviceID").Contains("&MI_")).ToList();
                var failing = devices.Where(d => Wmi.L(d, "ConfigManagerErrorCode", 0) != 0).ToList();
                s.Add(St.Info, "Dispositivos USB conectados", devices.Count + " conectados ahora mismo.", null, string.Join("\n", devices.Select(d => Wmi.S(d, "Name"))));
                foreach (var f in failing)
                    s.Add(St.Bad, "Dispositivo USB: " + Wmi.S(f, "Name"), "Este dispositivo " + PnpErrorText(Wmi.L(f, "ConfigManagerErrorCode", 0)) + ".",
                        "Conéctalo a otro puerto. Si falla en cualquier puerto, es el dispositivo; si solo en este, es el puerto.", PnpTech(f));
            });

            s.Safe("Puertos de vídeo", () =>
            {
                var conn = Wmi.QOrEmpty(ROOTWMI, "SELECT VideoOutputTechnology FROM WmiMonitorConnectionParams", 8);
                var ext = conn.Select(c => Wmi.L(c, "VideoOutputTechnology")).Where(t => t != 2147483648L && t != 6 && t != 11).ToList();
                foreach (var t in ext)
                    s.Add(St.Ok, "Salida " + OutputName(t), "Funciona: hay una pantalla conectada y detectada por este puerto.");
                bool hdmiAudio = s.Ctx.Pnp.Value.Any(d =>
                {
                    string n = Wmi.S(d, "Name").ToLowerInvariant();
                    return n.Contains("hdmi") || n.Contains("high definition audio") && n.Contains("display");
                });
                if (ext.Count == 0)
                    s.Add(St.Info, "HDMI, DisplayPort y VGA", "Sin pantalla externa conectada ahora, así que no se pueden confirmar estos puertos." + (hdmiAudio ? " La gráfica sí tiene soporte HDMI activo." : ""),
                        "Conecta un monitor o TV con «Probar puertos» abierto y el programa confirmará si responde.");
            });

            s.Safe("Puerto de red RJ45", () =>
            {
                var phys = Wmi.QOrEmpty(CIM, "SELECT Name,AdapterTypeID,NetConnectionID,NetConnectionStatus,PhysicalAdapter,Speed,ConfigManagerErrorCode FROM Win32_NetworkAdapter WHERE PhysicalAdapter=TRUE");
                var eth = phys.Where(a =>
                {
                    string n = Wmi.S(a, "Name").ToLowerInvariant();
                    return Wmi.L(a, "AdapterTypeID") == 0 && !n.Contains("wi-fi") && !n.Contains("wifi") && !n.Contains("wireless") && !n.Contains("802.11") &&
                           !n.Contains("bluetooth") && !n.Contains("virtual") && !n.Contains("vpn");
                }).ToList();
                if (eth.Count == 0)
                {
                    s.Add(St.Info, "Puerto de red RJ45", "No se detecta adaptador de cable. Muchos portátiles modernos no lo traen.",
                        "Si este equipo tiene el conector, instala el controlador de red o revisa que no esté apagado en la BIOS.");
                    return;
                }
                foreach (var a in eth)
                {
                    long err = Wmi.L(a, "ConfigManagerErrorCode", 0);
                    long status = Wmi.L(a, "NetConnectionStatus");
                    string name = Wmi.S(a, "Name");
                    if (err != 0)
                        s.Add(err == 22 ? St.Warn : St.Bad, "RJ45: " + name, "Este adaptador " + PnpErrorText(err) + ".", "Actívalo o reinstala su controlador.");
                    else if (status == 2)
                        s.Add(St.Ok, "RJ45: " + name, "Funciona: cable conectado a " + (Wmi.L(a, "Speed", 0) / 1000000) + " Mbps.");
                    else
                        s.Add(St.Info, "RJ45: " + name, "El adaptador está bien, pero no hay cable conectado, así que el conector físico no se puede confirmar.",
                            "Conecta un cable de red con «Probar puertos» abierto.");
                }
            });

            s.Safe("Otros puertos", () =>
            {
                foreach (var d in PnpByName(s.Ctx, "thunderbolt", "sd host", "card reader", "lector de tarjetas", "sdxc", "sdhc", "memory card"))
                    PnpFinding(s, d, "Detectado y sin errores.");
                foreach (var p in Wmi.QOrEmpty(CIM, "SELECT Name,DeviceID FROM Win32_SerialPort"))
                    s.Add(St.Ok, "Puerto serie: " + Wmi.S(p, "Name"), "Detectado.");
                foreach (var p in Wmi.QOrEmpty(CIM, "SELECT Name,DeviceID FROM Win32_ParallelPort"))
                    s.Add(St.Ok, "Puerto paralelo: " + Wmi.S(p, "Name"), "Detectado.");
                foreach (var p in Wmi.QOrEmpty(CIM, "SELECT Description,CurrentUsage,SlotDesignation FROM Win32_SystemSlot"))
                    s.Add(St.Info, "Ranura de expansión: " + Wmi.S(p, "SlotDesignation"),
                        Wmi.L(p, "CurrentUsage") == 4 ? "Ocupada." : Wmi.L(p, "CurrentUsage") == 3 ? "Libre." : "Estado no informado.");
            });
        }

        // ---------- red ----------

        static bool Ping(IPAddress ip, int timeoutMs, out long ms)
        {
            ms = -1;
            try
            {
                using (var p = new Ping())
                {
                    var r = p.Send(ip, timeoutMs);
                    if (r.Status == IPStatus.Success) { ms = r.RoundtripTime; return true; }
                }
            }
            catch { }
            return false;
        }

        static void Network(Section s)
        {
            IPAddress gateway = null;
            s.Safe("Adaptadores de red", () =>
            {
                var nics = NetworkInterface.GetAllNetworkInterfaces().Where(n =>
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel).ToList();
                var real = nics.Where(n =>
                {
                    string d = n.Description.ToLowerInvariant();
                    return !d.Contains("virtual") && !d.Contains("vpn") && !d.Contains("hyper-v") && !d.Contains("wan miniport") && !d.Contains("pseudo") && !d.Contains("filter") && !d.Contains("wsl") && !d.Contains("bluetooth");
                }).ToList();
                if (real.Count == 0)
                    s.Add(St.Bad, "Adaptadores de red", "Windows no ve ningún adaptador de red.", "Instala los controladores de red o revisa que no estén apagados en la BIOS.");
                foreach (var n in real)
                {
                    bool wifi = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                    bool up = n.OperationalStatus == OperationalStatus.Up;
                    string kind = wifi ? "Wi-Fi" : n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? (n.Description.ToLowerInvariant().Contains("bluetooth") ? "Bluetooth" : "Cable") : "Red";
                    string ips = "";
                    try
                    {
                        var props = n.GetIPProperties();
                        ips = string.Join(", ", props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).Select(a => a.Address.ToString()));
                        if (up && gateway == null)
                        {
                            var g = props.GatewayAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.Equals(IPAddress.Any));
                            if (g != null) gateway = g.Address;
                        }
                    }
                    catch { }
                    string signal = "";
                    if (wifi && up)
                    {
                        var m = Regex.Match(Exec.Run("netsh", "wlan show interfaces", 6000) ?? "", @"(\d{1,3})\s*%");
                        if (m.Success) signal = " Señal: " + m.Groups[1].Value + "%.";
                    }
                    s.Add(up ? St.Ok : St.Info, kind + ": " + n.Description,
                        up ? "Conectado a " + (n.Speed / 1000000) + " Mbps." + signal : "El adaptador está bien, pero no está conectado ahora.",
                        null, "Nombre: " + n.Name + "\nMAC: " + n.GetPhysicalAddress() + "\nIP: " + ips);
                }
                if (s.Ctx.IsPortable.Value && !real.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211))
                    s.Add(St.Warn, "Wi-Fi", "Este portátil no muestra adaptador Wi-Fi.", "Instala su controlador y revisa el modo avión o la tecla de Wi-Fi. Si no aparece, la tarjeta puede estar dañada.");
            });

            s.Safe("Conexión a internet", () =>
            {
                long ms;
                if (gateway != null)
                {
                    if (Ping(gateway, 1500, out ms))
                        s.Add(St.Ok, "Conexión con el router", "Responde en " + ms + " ms.", null, "Puerta de enlace: " + gateway);
                    else
                        s.Add(St.Info, "Conexión con el router", "El router no respondió. Algunos bloquean esta prueba, así que no es necesariamente una falla.", null, "Puerta de enlace: " + gateway);
                }
                bool online = Ping(IPAddress.Parse("1.1.1.1"), 2000, out ms) || Ping(IPAddress.Parse("8.8.8.8"), 2000, out ms);
                if (online)
                    s.Add(St.Ok, "Internet", "Hay conexión (" + ms + " ms).");
                else
                    s.Add(St.Info, "Internet", "Sin conexión a internet ahora. Esto no indica una falla del hardware.", "Comprueba el router o la red Wi-Fi si esperabas tener conexión.");
            });

            s.Safe("Bluetooth", () =>
            {
                var bt = PnpByClass(s.Ctx, "Bluetooth").Where(d =>
                {
                    string id = Wmi.S(d, "DeviceID").ToUpperInvariant();
                    return id.StartsWith(@"USB\") || id.StartsWith(@"PCI\");
                }).ToList();
                if (bt.Count == 0)
                {
                    bt = PnpByName(s.Ctx, "bluetooth").Where(d => Wmi.S(d, "PNPClass").Equals("Bluetooth", StringComparison.OrdinalIgnoreCase)).Take(1).ToList();
                }
                if (bt.Count == 0)
                    s.Add(St.Info, "Bluetooth", "No se detecta. Algunos equipos no lo traen o está apagado.", "Si debería tenerlo, instala su controlador.");
                foreach (var d in bt) PnpFinding(s, d, "Detectado y sin errores.");
            });
        }

        // ---------- batería ----------

        static void Battery(Section s)
        {
            bool has = false;
            s.Safe("Batería", () =>
            {
                var bats = Wmi.QOrEmpty(CIM, "SELECT Name,EstimatedChargeRemaining,BatteryStatus,Status,DeviceID FROM Win32_Battery");
                has = bats.Count > 0;
                if (!has)
                {
                    if (s.Ctx.IsPortable.Value)
                        s.Add(St.Bad, "Batería", "Este portátil no detecta ninguna batería.", "Comprueba que esté bien conectada. Si lo está, la batería o su conector pueden haber fallado.");
                    else
                        s.Add(St.Info, "Batería", "Este equipo no usa batería. Es normal en uno de escritorio.");
                    return;
                }
                Power.Status ps;
                bool ac = Power.GetSystemPowerStatus(out ps) && ps.ACLineStatus == 1;
                foreach (var b in bats)
                {
                    long pct = Wmi.L(b, "EstimatedChargeRemaining");
                    s.Add(St.Ok, "Carga actual", (pct >= 0 ? pct + "%" : "Porcentaje no disponible") + (ac ? ", enchufado al cargador." : ", funcionando con batería."),
                        null, Wmi.S(b, "Name") + "\nID: " + Wmi.S(b, "DeviceID"));
                }
                s.Add(ac ? St.Ok : St.Info, "Cargador", ac ? "Conectado y detectado." : "No está conectado ahora, así que no se pudo comprobar.", ac ? null : "Enchúfalo y repite la revisión.");
            });
            if (!has) return;

            s.Safe("Desgaste de la batería", () =>
            {
                long design = 0, full = 0, cycles = 0;
                foreach (var d in Wmi.QOrEmpty(ROOTWMI, "SELECT DesignedCapacity FROM BatteryStaticData", 8)) design += Math.Max(0, Wmi.L(d, "DesignedCapacity", 0));
                foreach (var d in Wmi.QOrEmpty(ROOTWMI, "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", 8)) full += Math.Max(0, Wmi.L(d, "FullChargedCapacity", 0));
                foreach (var d in Wmi.QOrEmpty(ROOTWMI, "SELECT CycleCount FROM BatteryCycleCount", 8)) cycles = Math.Max(cycles, Wmi.L(d, "CycleCount", 0));

                if (design <= 0 || full <= 0)
                {
                    // Plan B: el informe que genera Windows, independiente de los permisos de WMI.
                    string xmlPath = Path.Combine(Path.GetTempPath(), "chequeador_bateria_" + Guid.NewGuid().ToString("N") + ".xml");
                    try
                    {
                        Exec.Run("powercfg", "/batteryreport /xml /output \"" + xmlPath + "\"", 20000);
                        if (File.Exists(xmlPath))
                        {
                            var doc = new XmlDocument();
                            doc.Load(xmlPath);
                            foreach (XmlNode node in doc.GetElementsByTagName("*"))
                            {
                                long v;
                                if (!long.TryParse(node.InnerText, out v)) continue;
                                if (node.LocalName == "DesignCapacity") design += v;
                                else if (node.LocalName == "FullChargeCapacity") full += v;
                                else if (node.LocalName == "CycleCount") cycles = Math.Max(cycles, v);
                            }
                        }
                    }
                    catch { }
                    finally { try { File.Delete(xmlPath); } catch { } }
                }

                if (design <= 0 || full <= 0)
                {
                    s.Add(St.Unknown, "Desgaste de la batería", "La batería no informa su capacidad a Windows.");
                    return;
                }
                int health = (int)Math.Min(100, Math.Round(100.0 * full / design));
                St st = health < 40 ? St.Bad : health < 70 ? St.Warn : St.Ok;
                s.Add(st, "Salud de la batería", "Conserva el " + health + "% de su capacidad original." + (cycles > 0 ? " Ha hecho " + cycles + " ciclos de carga." : ""),
                    st == St.Bad ? "Conviene cambiarla: durará poco y puede apagarse de repente." : st == St.Warn ? "Dura bastante menos que nueva. Planea cambiarla si la autonomía ya no te alcanza." : null,
                    "Capacidad de fábrica: " + design + " mWh\nCapacidad actual: " + full + " mWh");
            });
        }

        // ---------- sistema operativo ----------

        static void Os(Section s)
        {
            s.Safe("Windows", () =>
            {
                var o = Wmi.Q(CIM, "SELECT Caption,Version,BuildNumber,OSArchitecture,InstallDate,LastBootUpTime FROM Win32_OperatingSystem").FirstOrDefault();
                int build; int.TryParse(Wmi.S(o, "BuildNumber"), out build);
                var boot = Wmi.Date(o, "LastBootUpTime");
                var inst = Wmi.Date(o, "InstallDate");
                string tech = "Versión: " + Wmi.S(o, "Version") + ", compilación " + build +
                              (inst.HasValue ? "\nInstalado: " + inst.Value.ToString("yyyy-MM-dd") : "") + (boot.HasValue ? "\nÚltimo arranque: " + boot : "");
                string plain = Wmi.S(o, "Caption") + " de " + Wmi.S(o, "OSArchitecture") + ".";

                DateTime? eol = EndOfSupport(build);
                if (eol.HasValue && DateTime.Now > eol.Value)
                    s.Add(St.Warn, "Versión de Windows", plain + " Terminó su soporte en " + eol.Value.ToString("MMMM 'de' yyyy") + ", según el calendario de Microsoft.",
                        "Sin actualizaciones de seguridad el equipo queda expuesto. Actualiza a una versión con soporte o contrata las actualizaciones extendidas si ya las tienes disponibles.", tech);
                else
                    s.Add(St.Ok, "Versión de Windows", plain + " Sigue con soporte.", null, tech);

                if (boot.HasValue && (DateTime.Now - boot.Value).TotalDays > 30)
                    s.Add(St.Info, "Tiempo encendido", "Lleva " + (int)(DateTime.Now - boot.Value).TotalDays + " días sin reiniciarse.", "Reiniciar de vez en cuando evita errores y aplica actualizaciones.");
            });

            s.Safe("Activación", () =>
            {
                var l = Wmi.Q(CIM, "SELECT Name,LicenseStatus FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f'", 30);
                bool active = l.Any(x => Wmi.L(x, "LicenseStatus") == 1);
                if (l.Count == 0)
                    s.Add(St.Warn, "Activación de Windows", "No hay ninguna licencia instalada.", "Actívalo en Configuración, Activación.");
                else
                    s.Add(active ? St.Ok : St.Warn, "Activación de Windows", active ? "Windows está activado." : "Windows no está activado.",
                        active ? null : "Actívalo en Configuración, Activación.", string.Join("\n", l.Select(x => Wmi.S(x, "Name"))));
            });

            s.Safe("Actualizaciones", () =>
            {
                DateTime last = DateTime.MinValue;
                foreach (var h in Wmi.Q(CIM, "SELECT HotFixID,InstalledOn FROM Win32_QuickFixEngineering", 30))
                {
                    DateTime dt;
                    string v = Wmi.S(h, "InstalledOn");
                    if (DateTime.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt) ||
                        DateTime.TryParse(v, out dt))
                        if (dt > last) last = dt;
                }
                if (last == DateTime.MinValue)
                    s.Add(St.Unknown, "Actualizaciones", "No se encontró la fecha de la última actualización.", "Mírala en Configuración, Windows Update.");
                else
                {
                    int days = (int)(DateTime.Now - last).TotalDays;
                    s.Add(days > 90 ? St.Warn : St.Ok, "Actualizaciones", "La última se instaló hace " + Math.Max(0, days) + " días (" + last.ToString("yyyy-MM-dd") + ").",
                        days > 90 ? "Abre Windows Update y busca actualizaciones." : null);
                }
                bool pending = false;
                foreach (var key in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
                                            @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired" })
                    using (var k = Registry.LocalMachine.OpenSubKey(key))
                        if (k != null) pending = true;
                if (pending)
                    s.Add(St.Info, "Reinicio pendiente", "Windows espera un reinicio para terminar de actualizarse.", "Reinicia cuando puedas.");
            });

            s.Safe("Antivirus", () =>
            {
                var av = Wmi.Q(@"root\SecurityCenter2", "SELECT displayName,productState FROM AntiVirusProduct", 10);
                if (av.Count == 0)
                    s.Add(St.Warn, "Antivirus", "No se encontró ningún antivirus registrado.", "Activa Seguridad de Windows.");
                else
                {
                    var on = av.Where(a => ((Wmi.L(a, "productState", 0) >> 12) & 0xF) == 1).ToList();
                    if (on.Count > 0)
                        s.Add(St.Ok, "Antivirus", string.Join(", ", on.Select(a => Wmi.S(a, "displayName"))) + " está activo.");
                    else
                        s.Add(St.Warn, "Antivirus", "Hay antivirus instalado, pero ninguno está activo.", "Actívalo en Seguridad de Windows.", string.Join("\n", av.Select(a => Wmi.S(a, "displayName"))));
                }
            });

            s.Safe("Firewall", () =>
            {
                const string basePath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";
                bool off = false, read = false;
                foreach (var prof in new[] { "StandardProfile", "PublicProfile" })
                    using (var k = Registry.LocalMachine.OpenSubKey(basePath + prof))
                    {
                        if (k == null) continue;
                        read = true;
                        if (Convert.ToInt32(k.GetValue("EnableFirewall", 1)) == 0) off = true;
                    }
                if (read)
                    s.Add(off ? St.Warn : St.Ok, "Firewall de Windows", off ? "Está apagado en al menos un perfil de red." : "Está activo.", off ? "Actívalo en Seguridad de Windows, salvo que uses otro firewall." : null);
            });

            s.Safe("Estabilidad", () =>
            {
                var blue = Ev.Count("Microsoft-Windows-WER-SystemErrorReporting", new[] { 1001 }, 90);
                var hard = Ev.Count("Microsoft-Windows-Kernel-Power", new[] { 41 }, 90);
                if (blue.Failed && hard.Failed) { s.Add(St.Unknown, "Estabilidad", "No se pudo leer el registro de eventos de Windows."); return; }
                const string fix = "Abre un Símbolo del sistema como administrador y ejecuta «sfc /scannow» y «DISM /Online /Cleanup-Image /RestoreHealth». Revisa también RAM, disco y temperatura.";
                if (blue.Count > 0)
                    s.Add(blue.Count >= 3 ? St.Bad : St.Warn, "Pantallazos azules", blue.Count + " en los últimos 90 días.", fix, "Último: " + blue.Latest + "\n" + blue.Sample);
                else
                    s.Add(St.Ok, "Pantallazos azules", "Ninguno en los últimos 90 días.");
                if (hard.Count > 0)
                    s.Add(hard.Count >= 5 ? St.Warn : St.Info, "Apagados inesperados", hard.Count + " en los últimos 90 días (cortes de luz, cuelgues o apagado forzado).",
                        hard.Count >= 5 ? "Si no fuiste tú, revisa la fuente o el cargador y la temperatura." : null, "Último: " + hard.Latest);
            });
        }

        static DateTime? EndOfSupport(int build)
        {
            if (build <= 0) return null;
            if (build < 7601) return new DateTime(2017, 4, 11);
            if (build <= 7601) return new DateTime(2020, 1, 14);
            if (build <= 9600) return new DateTime(2023, 1, 10);
            if (build < 22000) return new DateTime(2025, 10, 14);
            if (build == 22000) return new DateTime(2024, 10, 8);
            if (build == 22621) return new DateTime(2025, 10, 14);
            if (build == 22631) return new DateTime(2026, 11, 10);
            if (build == 26100) return new DateTime(2026, 10, 13);
            if (build == 26200) return new DateTime(2027, 10, 12);
            return null;
        }

        // ---------- catch-all ----------

        static void Devices(Section s)
        {
            s.Safe("Dispositivos con problemas", () =>
            {
                var all = s.Ctx.Pnp.Value;
                var bad = all.Where(d => Wmi.L(d, "ConfigManagerErrorCode", 0) != 0 && Wmi.L(d, "ConfigManagerErrorCode", 0) != 45).ToList();
                if (bad.Count == 0)
                    s.Add(St.Ok, "Todos los dispositivos", all.Count + " dispositivos revisados, ninguno con errores.");
                else
                    foreach (var d in bad)
                    {
                        long code = Wmi.L(d, "ConfigManagerErrorCode", 0);
                        string name = Wmi.S(d, "Name");
                        if (name.Length == 0) name = "Dispositivo no reconocido";
                        string hw = "";
                        var ids = d.ContainsKey("HardwareID") ? d["HardwareID"] as string[] : null;
                        if (ids != null && ids.Length > 0) hw = ids[0];
                        string action = code == 28
                            ? "Busca en internet su identificador" + (hw.Length > 0 ? " (" + hw + ")" : "") + " para saber qué es e instala su controlador. Windows Update también puede encontrarlo."
                            : code == 22 ? "Actívalo en el Administrador de dispositivos si lo necesitas."
                            : "Abre el Administrador de dispositivos, reinstálalo y reinicia. Si sigue igual, puede ser una falla física.";
                        s.Add(code == 22 ? St.Info : code == 43 || code == 10 ? St.Bad : St.Warn, name, "Este dispositivo " + PnpErrorText(code) + ".", action, PnpTech(d) + (hw.Length > 0 ? "\nHardware: " + hw : ""));
                    }
            });

            s.Safe("Unidad óptica", () =>
            {
                foreach (var d in Wmi.QOrEmpty(CIM, "SELECT Name,Status FROM Win32_CDROMDrive"))
                    s.Add(St.Ok, "Unidad óptica: " + Wmi.S(d, "Name"), "Detectada.");
            });

            s.Safe("Lector de huellas", () =>
            {
                foreach (var d in PnpByClass(s.Ctx, "Biometric")) PnpFinding(s, d, "Detectado y sin errores.");
            });
        }
    }
}
