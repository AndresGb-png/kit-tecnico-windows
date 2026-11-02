using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace ChequeadorPC
{
    public class MemSlot
    {
        public string Locator = "", Bank = "", Maker = "", Part = "", Serial = "", Type = "", Form = "";
        public long SizeMb; public int Speed, ConfiguredSpeed;
        public bool Soldered { get { return Form == "Soldada"; } }
    }

    public static class Smbios
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[] buffer, uint size);

        // Lee la tabla del firmware, que sí lista las ranuras vacías (WMI solo da las ocupadas).
        public static List<MemSlot> ReadMemorySlots()
        {
            const uint RSMB = 0x52534D42;
            uint size = GetSystemFirmwareTable(RSMB, 0, null, 0);
            if (size == 0) return null;
            var raw = new byte[size];
            if (GetSystemFirmwareTable(RSMB, 0, raw, size) == 0) return null;

            var slots = new List<MemSlot>();
            int pos = 8, end = Math.Min(raw.Length, 8 + BitConverter.ToInt32(raw, 4));
            while (pos + 4 <= end)
            {
                int type = raw[pos], len = raw[pos + 1];
                if (len < 4 || pos + len > end) break;
                int strStart = pos + len, cursor = strStart;
                while (cursor + 1 < end && !(raw[cursor] == 0 && raw[cursor + 1] == 0)) cursor++;
                if (type == 17 && len >= 0x15) slots.Add(ParseSlot(raw, pos, len, strStart));
                if (type == 127) break;
                pos = cursor + 2;
            }
            return slots;
        }

        static MemSlot ParseSlot(byte[] raw, int p, int len, int strings)
        {
            var m = new MemSlot();
            int size = BitConverter.ToUInt16(raw, p + 0x0C);
            if (size == 0 || size == 0xFFFF) m.SizeMb = 0;
            else if (size == 0x7FFF && len >= 0x20) m.SizeMb = BitConverter.ToUInt32(raw, p + 0x1C);
            else if ((size & 0x8000) != 0) m.SizeMb = (size & 0x7FFF) / 1024;
            else m.SizeMb = size;

            switch (raw[p + 0x0E])
            {
                case 0x09: m.Form = "DIMM"; break;
                case 0x0D: m.Form = "SODIMM"; break;
                case 0x0B: m.Form = "Soldada"; break;
                default: m.Form = ""; break;
            }
            m.Locator = Str(raw, strings, raw[p + 0x10]);
            m.Bank = Str(raw, strings, raw[p + 0x11]);
            switch (raw[p + 0x12])
            {
                case 0x12: m.Type = "DDR"; break;
                case 0x13: m.Type = "DDR2"; break;
                case 0x18: m.Type = "DDR3"; break;
                case 0x1A: m.Type = "DDR4"; break;
                case 0x1B: m.Type = "LPDDR"; break;
                case 0x1C: m.Type = "LPDDR2"; break;
                case 0x1D: m.Type = "LPDDR3"; break;
                case 0x1E: m.Type = "LPDDR4"; break;
                case 0x22: m.Type = "DDR5"; break;
                case 0x23: m.Type = "LPDDR5"; break;
                default: m.Type = ""; break;
            }
            if (len >= 0x17) m.Speed = BitConverter.ToUInt16(raw, p + 0x15);
            if (len >= 0x18) m.Maker = Str(raw, strings, raw[p + 0x17]);
            if (len >= 0x19) m.Serial = Str(raw, strings, raw[p + 0x18]);
            if (len >= 0x1B) m.Part = Str(raw, strings, raw[p + 0x1A]);
            if (len >= 0x22) m.ConfiguredSpeed = BitConverter.ToUInt16(raw, p + 0x20);
            return m;
        }

        static string Str(byte[] raw, int start, int index)
        {
            if (index <= 0) return "";
            int cur = start;
            for (int i = 1; i < index; i++)
            {
                while (cur < raw.Length && raw[cur] != 0) cur++;
                cur++;
                if (cur >= raw.Length) return "";
            }
            int e = cur;
            while (e < raw.Length && raw[e] != 0) e++;
            return Encoding.ASCII.GetString(raw, cur, e - cur).Trim();
        }
    }

    public class NvmeHealth
    {
        public byte CriticalWarning, AvailableSpare, SpareThreshold, PercentUsed;
        public int TemperatureC;
        public long PowerOnHours, UnsafeShutdowns, MediaErrors, PowerCycles;
        public double BytesWritten;
    }

    public static class DiskIo
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle dev, uint code, byte[] inBuf, int inSize, byte[] outBuf, int outSize, out int returned, IntPtr ov);

        const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;

        static SafeFileHandle Open(int index)
        {
            string path = @"\\.\PhysicalDrive" + index;
            var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) h = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            return h;
        }

        // Pide a la unidad NVMe su registro de salud (log page 2 del estándar).
        public static NvmeHealth ReadNvme(int index)
        {
            using (var h = Open(index))
            {
                if (h.IsInvalid) return null;
                const int headerSize = 8 + 40, logSize = 512;
                var buf = new byte[headerSize + logSize];
                Put(buf, 0, 50);        // StorageDeviceProtocolSpecificProperty
                Put(buf, 4, 0);         // PropertyStandardQuery
                Put(buf, 8, 3);         // ProtocolTypeNvme
                Put(buf, 12, 2);        // NVMeDataTypeLogPage
                Put(buf, 16, 2);        // SMART / Health Information
                Put(buf, 24, 40);       // ProtocolDataOffset
                Put(buf, 28, logSize);  // ProtocolDataLength
                int got;
                if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, buf, buf.Length, buf, buf.Length, out got, IntPtr.Zero))
                    return null;
                int offset = 8 + BitConverter.ToInt32(buf, 8 + 16);
                if (got < offset + 180 || offset + logSize > buf.Length) return null;
                var r = new NvmeHealth();
                r.CriticalWarning = buf[offset];
                int kelvin = BitConverter.ToUInt16(buf, offset + 1);
                r.TemperatureC = kelvin > 0 ? kelvin - 273 : 0;
                r.AvailableSpare = buf[offset + 3];
                r.SpareThreshold = buf[offset + 4];
                r.PercentUsed = buf[offset + 5];
                r.BytesWritten = BitConverter.ToInt64(buf, offset + 48) * 512000.0;
                r.PowerCycles = BitConverter.ToInt64(buf, offset + 112);
                r.PowerOnHours = BitConverter.ToInt64(buf, offset + 128);
                r.UnsafeShutdowns = BitConverter.ToInt64(buf, offset + 144);
                r.MediaErrors = BitConverter.ToInt64(buf, offset + 160);
                return r;
            }
        }

        // null = no se sabe. false = sin penalización de búsqueda, es decir, sin cabezales: SSD.
        public static bool? SeekPenalty(int index)
        {
            try
            {
                using (var h = Open(index))
                {
                    if (h.IsInvalid) return null;
                    var input = new byte[12];
                    Put(input, 0, 7);   // StorageDeviceSeekPenaltyProperty
                    var output = new byte[12];
                    int got;
                    if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, input, input.Length, output, output.Length, out got, IntPtr.Zero) || got < 9)
                        return null;
                    return output[8] != 0;
                }
            }
            catch { return null; }
        }

        static void Put(byte[] b, int at, int v) { Array.Copy(BitConverter.GetBytes(v), 0, b, at, 4); }
    }

    public class SmartAttr { public int Id, Current; public long Raw; }

    public static class SmartParser
    {
        // Formato clásico ATA: 2 bytes de versión y luego entradas de 12 bytes.
        public static Dictionary<int, SmartAttr> Parse(byte[] data)
        {
            var map = new Dictionary<int, SmartAttr>();
            if (data == null) return map;
            for (int p = 2; p + 12 <= data.Length; p += 12)
            {
                int id = data[p];
                if (id == 0) continue;
                long raw = 0;
                for (int i = 0; i < 6; i++) raw |= (long)data[p + 5 + i] << (8 * i);
                map[id] = new SmartAttr { Id = id, Current = data[p + 3], Raw = raw };
            }
            return map;
        }
    }

    public static class Power
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Status
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        public static extern bool GetSystemPowerStatus(out Status s);
    }
}
