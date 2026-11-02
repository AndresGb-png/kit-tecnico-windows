using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using FormsTimer = System.Windows.Forms.Timer;

namespace ChequeadorPC
{
    public class TestDef
    {
        public string Label;
        public Action<Form, Ctx> Run;
        public TestDef(string label, Action<Form, Ctx> run) { Label = label; Run = run; }
    }

    public static class TestCatalog
    {
        public static List<TestDef> For(string key)
        {
            var list = new List<TestDef>();
            switch (key)
            {
                case "video": list.Add(new TestDef("Probar pantalla", (f, c) => ScreenTest.Run(f, c))); break;
                case "sonido":
                    list.Add(new TestDef("Probar altavoces", (f, c) => Show(f, new SpeakerTest(c))));
                    list.Add(new TestDef("Probar micrófono", (f, c) => Show(f, new MicTest(c))));
                    break;
                case "camara": list.Add(new TestDef("Probar cámara", (f, c) => CameraTest.Launch(c))); break;
                case "entrada":
                    list.Add(new TestDef("Probar teclado", (f, c) => Show(f, new KeyboardTest(c))));
                    list.Add(new TestDef("Probar ratón", (f, c) => Show(f, new MouseTest(c))));
                    break;
                case "puertos": case "red": list.Add(new TestDef("Probar puertos", (f, c) => Show(f, new PortsTest(c)))); break;
                case "ram": list.Add(new TestDef("Prueba completa de memoria", MemoryDiag)); break;
            }
            return list;
        }

        static void Show(Form owner, Form test)
        {
            using (test) test.ShowDialog(owner);
        }

        static void MemoryDiag(Form owner, Ctx c)
        {
            var answer = MessageBox.Show(owner,
                "Windows abrirá su herramienta de memoria y te dejará elegir entre reiniciar ahora o en el próximo arranque. Tarda unos 20 minutos. Guarda tu trabajo antes.\n\n¿Abrir la herramienta?",
                "Prueba completa de memoria", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            try { Process.Start("mdsched.exe"); }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "No se pudo abrir: " + ex.Message + "\n\nBusca «Diagnóstico de memoria de Windows» en el menú Inicio, o arranca desde un USB con MemTest86.", "Prueba completa de memoria");
            }
        }
    }

    // Base común: siempre hay tres salidas, incluida «No pude probarlo».
    public class TestForm : Form
    {
        protected readonly Ctx ctx;
        protected readonly Panel Body = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly string section, testKey, testName;
        protected string BadAction = "Consulta a un técnico con este informe.";
        protected string OkNote = "Confirmado en la prueba manual.";
        protected Func<string> ExtraNote = () => "";
        bool answered;
        protected readonly Label Hint = new Label { Dock = DockStyle.Top, AutoSize = false, ForeColor = Ui.Ink, Padding = new Padding(Ui.D(16), Ui.D(10), Ui.D(16), Ui.D(4)) };

        public TestForm(Ctx c, string title, string sectionKey, string key, string okText, string badText)
        {
            ctx = c; section = sectionKey; testKey = key; testName = title;
            Text = title;
            Font = Ui.Body;
            BackColor = Color.White;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(Ui.D(720), area.Width), Math.Min(Ui.D(520), area.Height));
            Comun.WindowFit.Apply(this);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Page, Padding = new Padding(Ui.D(8)) };
            bar.Controls.Add(Ui.Btn(okText, true, (s, e) => Answer(St.Ok)));
            bar.Controls.Add(Ui.Btn(badText, false, (s, e) => Answer(St.Bad)));
            bar.Controls.Add(Ui.Btn("No pude probarlo", false, (s, e) => Answer(St.Unknown)));
            Hint.Height = Ui.D(64);
            Controls.Add(Body);
            Controls.Add(Hint);
            Controls.Add(bar);
        }

        protected void Answer(St st)
        {
            answered = true;
            string extra = "";
            try { extra = ExtraNote() ?? ""; } catch { }
            string plain = st == St.Ok ? OkNote : st == St.Bad ? "La persona que hizo la prueba indicó que falla." : "La prueba no se pudo hacer.";
            string action = st == St.Bad ? BadAction : st == St.Unknown ? "Repite esta prueba cuando puedas." : null;
            ctx.SetManual(section, testKey, st, "Prueba manual: " + testName, (plain + " " + extra).Trim(), action);
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            // Cerrar con la X no cuenta como resultado: no se inventa un veredicto.
            if (!answered) ctx.Touch();
        }

        protected override bool ProcessDialogKey(Keys k)
        {
            if (k == Keys.Escape) { Close(); return true; }
            return base.ProcessDialogKey(k);
        }
    }

    // ---------- altavoces ----------

    public class SpeakerTest : TestForm
    {
        [DllImport("winmm.dll", SetLastError = true)]
        static extern bool PlaySound(IntPtr sound, IntPtr mod, uint flags);
        const uint SND_MEMORY = 4, SND_ASYNC = 1, SND_NODEFAULT = 2;
        IntPtr current = IntPtr.Zero;
        readonly Label status = new Label { AutoSize = true, ForeColor = Ui.Muted };

        public SpeakerTest(Ctx c) : base(c, "Altavoces", "sonido", "altavoces", "Se oyó bien en ambos lados", "No se oye o suena mal")
        {
            Hint.Text = "Sube el volumen y pulsa cada botón. El sonido debe salir solo por el lado indicado.";
            BadAction = "Revisa el volumen y la salida seleccionada en Windows, prueba con auriculares y reinstala el controlador de audio. Si los auriculares suenan y los altavoces no, los altavoces están dañados.";
            var row = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(Ui.D(12)) };
            row.Controls.Add(Ui.Btn("Sonar a la izquierda", true, (s, e) => Play(true, false)));
            row.Controls.Add(Ui.Btn("Sonar a la derecha", true, (s, e) => Play(false, true)));
            row.Controls.Add(Ui.Btn("Sonar en ambos", true, (s, e) => Play(true, true)));
            status.Location = new Point(Ui.D(16), Ui.D(70));
            Body.Controls.Add(status);
            Body.Controls.Add(row);
        }

        void Play(bool left, bool right)
        {
            try
            {
                PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
                if (current != IntPtr.Zero) { Marshal.FreeHGlobal(current); current = IntPtr.Zero; }
                var wav = Tone(left, right);
                current = Marshal.AllocHGlobal(wav.Length);
                Marshal.Copy(wav, 0, current, wav.Length);
                bool ok = PlaySound(current, IntPtr.Zero, SND_MEMORY | SND_ASYNC | SND_NODEFAULT);
                status.Text = ok ? "Sonando…" : "Windows no pudo reproducir el sonido: puede no haber una salida activa.";
            }
            catch (Exception ex) { status.Text = "No se pudo reproducir: " + ex.Message; }
        }

        static byte[] Tone(bool left, bool right)
        {
            const int rate = 44100, samples = rate;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int dataLen = samples * 4;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
                for (int i = 0; i < samples; i++)
                {
                    double env = Math.Min(1.0, Math.Min(i, samples - i) / 2000.0);
                    short v = (short)(Math.Sin(2 * Math.PI * 523.25 * i / rate) * 14000 * env);
                    w.Write(left ? v : (short)0);
                    w.Write(right ? v : (short)0);
                }
                return ms.ToArray();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { PlaySound(IntPtr.Zero, IntPtr.Zero, 0); } catch { }
            if (current != IntPtr.Zero) Marshal.FreeHGlobal(current);
            current = IntPtr.Zero;
            base.OnFormClosed(e);
        }
    }

    // ---------- micrófono ----------

    public class MicTest : TestForm
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct WaveFormat { public ushort Tag, Channels; public uint Rate, AvgBytes; public ushort Align, Bits, Extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct WaveHdr { public IntPtr Data; public uint Length, Recorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved; }

        [DllImport("winmm.dll")] static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, uint device, ref WaveFormat fmt, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);

        const int BufBytes = 4410;
        IntPtr dev = IntPtr.Zero, hdr = IntPtr.Zero, buf = IntPtr.Zero;
        readonly FormsTimer timer = new FormsTimer { Interval = 60 };
        readonly Panel meter = new Panel { BackColor = Ui.Line };
        readonly Panel fill = new Panel { BackColor = Ui.Of(St.Ok), Width = 0 };
        readonly Label status = new Label { AutoSize = false, ForeColor = Ui.Ink };
        int peakSeen;

        public MicTest(Ctx c) : base(c, "Micrófono", "sonido", "microfono", "La barra se movió con mi voz", "La barra no se mueve")
        {
            Hint.Text = "Habla o da una palmada cerca del equipo. La barra debe moverse. Nada se graba ni se guarda.";
            BadAction = "Revisa el permiso de micrófono en Privacidad, que no esté silenciado y que el micrófono correcto esté seleccionado. Prueba otro micrófono para descartar una falla física.";
            ExtraNote = () => "Nivel máximo medido: " + (peakSeen * 100 / 32768) + "%.";
            meter.SetBounds(Ui.D(16), Ui.D(20), Ui.D(460), Ui.D(28));
            fill.SetBounds(0, 0, 0, Ui.D(28));
            meter.Controls.Add(fill);
            status.SetBounds(Ui.D(16), Ui.D(60), Ui.D(640), Ui.D(70));
            Body.Controls.Add(meter);
            Body.Controls.Add(status);
            timer.Tick += (s, e) => Poll();
            Shown += (s, e) => Begin();
        }

        void Begin()
        {
            try
            {
                if (waveInGetNumDevs() == 0)
                {
                    status.Text = "Windows no tiene ningún micrófono disponible. Conecta uno o elige «No pude probarlo».";
                    return;
                }
                var fmt = new WaveFormat { Tag = 1, Channels = 1, Rate = 22050, Bits = 16, Align = 2, AvgBytes = 44100, Extra = 0 };
                int rc = waveInOpen(out dev, 0xFFFFFFFF, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
                if (rc != 0)
                {
                    dev = IntPtr.Zero;
                    status.Text = "El micrófono no se pudo abrir (código " + rc + "). Puede estar bloqueado por Privacidad o en uso por otro programa.";
                    return;
                }
                buf = Marshal.AllocHGlobal(BufBytes);
                int size = Marshal.SizeOf(typeof(WaveHdr));
                hdr = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(new WaveHdr { Data = buf, Length = BufBytes }, hdr, false);
                waveInPrepareHeader(dev, hdr, size);
                waveInAddBuffer(dev, hdr, size);
                waveInStart(dev);
                status.Text = "Escuchando…";
                timer.Start();
            }
            catch (Exception ex) { status.Text = "No se pudo iniciar la prueba: " + ex.Message; }
        }

        void Poll()
        {
            try
            {
                var h = (WaveHdr)Marshal.PtrToStructure(hdr, typeof(WaveHdr));
                if ((h.Flags & 1) == 0) return;
                int peak = 0;
                for (int i = 0; i + 1 < h.Recorded; i += 2)
                {
                    int v = Math.Abs((int)Marshal.ReadInt16(buf, i));
                    if (v > peak) peak = v;
                }
                if (peak > peakSeen) peakSeen = peak;
                fill.Width = Math.Min(meter.Width, (int)(Math.Sqrt(peak / 32768.0) * meter.Width));
                int size = Marshal.SizeOf(typeof(WaveHdr));
                h.Flags &= ~1u;
                Marshal.StructureToPtr(h, hdr, false);
                waveInAddBuffer(dev, hdr, size);
                if (peakSeen > 800) status.Text = "Se está captando sonido.";
            }
            catch { timer.Stop(); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            try
            {
                if (dev != IntPtr.Zero)
                {
                    waveInReset(dev);
                    if (hdr != IntPtr.Zero) waveInUnprepareHeader(dev, hdr, Marshal.SizeOf(typeof(WaveHdr)));
                    waveInClose(dev);
                }
            }
            catch { }
            if (hdr != IntPtr.Zero) Marshal.FreeHGlobal(hdr);
            if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
            hdr = buf = dev = IntPtr.Zero;
            base.OnFormClosed(e);
        }
    }

    // ---------- cámara ----------

    public class CameraTest : TestForm
    {
        [DllImport("avicap32.dll", CharSet = CharSet.Ansi)]
        static extern IntPtr capCreateCaptureWindowA(string name, int style, int x, int y, int w, int h, IntPtr parent, int id);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Ansi, EntryPoint = "SendMessageA")] static extern IntPtr SendMessageStr(IntPtr h, int msg, IntPtr w, string l);
        [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int h2, bool repaint);
        const int CAP = 1024, CONNECT = CAP + 10, DISCONNECT = CAP + 11, SAVEDIB = CAP + 25, PREVIEW = CAP + 50, RATE = CAP + 52, SCALE = CAP + 53, GRAB = CAP + 60;

        IntPtr cap = IntPtr.Zero;
        bool connected;
        readonly Panel screen = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0x20, 0x26, 0x2B) };
        readonly Label status = new Label { Dock = DockStyle.Bottom, AutoSize = false, ForeColor = Ui.Ink, Padding = new Padding(Ui.D(12), Ui.D(4), Ui.D(12), Ui.D(4)) };
        readonly FormsTimer check = new FormsTimer { Interval = 1800 };
        string brightness = "";

        // Va en su propio hilo: si el controlador se cuelga, el programa principal sigue respondiendo.
        public static void Launch(Ctx c)
        {
            var t = new Thread(() =>
            {
                try { Application.Run(new CameraTest(c)); }
                catch (Exception ex)
                {
                    MessageBox.Show("La prueba de cámara no pudo iniciarse: " + ex.Message + "\n\nPrueba con la aplicación Cámara de Windows.", "Cámara");
                }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        public CameraTest(Ctx c) : base(c, "Cámara", "camara", "camara", "Se ve la imagen", "No se ve o se ve mal")
        {
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Hint.Text = "Se debe ver lo que tiene delante. La imagen no se guarda. Si no aparece, abre la aplicación Cámara de Windows para comparar.";
            BadAction = "Revisa la tapa o la tecla de la cámara, el permiso en Privacidad y su controlador. Si tampoco funciona en la aplicación Cámara, puede estar dañada o desconectada.";
            ExtraNote = () => brightness;
            status.Height = Ui.D(56);
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(Ui.D(8), 0, Ui.D(8), 0) };
            row.Controls.Add(Ui.Btn("Abrir la aplicación Cámara de Windows", false, (s, e) =>
            {
                Stop();
                status.Text = "Cámara liberada para la aplicación de Windows. Cuando termines, indica abajo el resultado.";
                try { Process.Start("microsoft.windows.camera:"); }
                catch { status.Text = "Este Windows no tiene la aplicación Cámara. Indica abajo que no pudiste probarlo."; }
            }));
            Body.Controls.Add(screen);
            Body.Controls.Add(status);
            Body.Controls.Add(row);
            Shown += (s, e) => StartCam();
            screen.Resize += (s, e) => { if (cap != IntPtr.Zero) MoveWindow(cap, 0, 0, screen.ClientSize.Width, screen.ClientSize.Height, true); };
            check.Tick += (s, e) => { check.Stop(); Analyze(); };
        }

        void StartCam()
        {
            try
            {
                status.Text = "Encendiendo la cámara…";
                status.Refresh();
                cap = capCreateCaptureWindowA("cam", 0x50000000, 0, 0, screen.ClientSize.Width, screen.ClientSize.Height, screen.Handle, 1);
                if (cap == IntPtr.Zero) throw new InvalidOperationException("Windows no creó la ventana de captura.");
                for (int i = 0; i < 4 && !connected; i++)
                    connected = SendMessage(cap, CONNECT, (IntPtr)i, IntPtr.Zero) != IntPtr.Zero;
                if (!connected)
                {
                    status.Text = "La cámara no respondió a este método. Puede estar en uso, bloqueada por Privacidad, o ser de un tipo moderno que solo funciona con la aplicación Cámara: ábrela con el botón para comprobarlo.";
                    return;
                }
                SendMessage(cap, SCALE, (IntPtr)1, IntPtr.Zero);
                SendMessage(cap, RATE, (IntPtr)33, IntPtr.Zero);
                SendMessage(cap, PREVIEW, (IntPtr)1, IntPtr.Zero);
                status.Text = "Cámara encendida.";
                check.Start();
            }
            catch (Exception ex)
            {
                status.Text = "La prueba automática no está disponible en este equipo (" + ex.Message + "). Usa el botón para abrir la aplicación Cámara.";
            }
        }

        void Analyze()
        {
            string temp = Path.Combine(Path.GetTempPath(), "chequeador_cam_" + Guid.NewGuid().ToString("N") + ".bmp");
            try
            {
                SendMessage(cap, GRAB, IntPtr.Zero, IntPtr.Zero);
                SendMessageStr(cap, SAVEDIB, IntPtr.Zero, temp);
                SendMessage(cap, PREVIEW, (IntPtr)1, IntPtr.Zero);
                if (!File.Exists(temp)) return;
                double sum = 0; int n = 0;
                using (var b = new Bitmap(temp))
                    for (int y = 0; y < b.Height; y += Math.Max(1, b.Height / 40))
                        for (int x = 0; x < b.Width; x += Math.Max(1, b.Width / 40))
                        {
                            sum += b.GetPixel(x, y).GetBrightness();
                            n++;
                        }
                double avg = n > 0 ? sum / n : 0;
                if (avg < 0.03)
                {
                    brightness = "La imagen salió casi negra.";
                    status.Text = "Cámara encendida, pero la imagen está casi negra. Comprueba que no tenga la tapa de privacidad puesta.";
                }
                else
                {
                    brightness = "Imagen recibida correctamente.";
                    status.Text = "La cámara está enviando imagen.";
                }
            }
            catch { }
            finally { try { File.Delete(temp); } catch { } }
        }

        void Stop()
        {
            check.Stop();
            try
            {
                if (cap != IntPtr.Zero)
                {
                    SendMessage(cap, PREVIEW, IntPtr.Zero, IntPtr.Zero);
                    if (connected) SendMessage(cap, DISCONNECT, IntPtr.Zero, IntPtr.Zero);
                    DestroyWindow(cap);
                }
            }
            catch { }
            cap = IntPtr.Zero;
            connected = false;
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { Stop(); base.OnFormClosed(e); }
    }

    // ---------- teclado ----------

    public class KeyboardTest : TestForm, IMessageFilter
    {
        class K { public string L; public int Code; public float W; public RectangleF R; public K(string l, int c, float w = 1f) { L = l; Code = c; W = w; } }
        readonly List<List<K>> rows;
        readonly HashSet<int> pressed = new HashSet<int>();
        readonly Panel canvas = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly Label info = new Label { Dock = DockStyle.Bottom, Height = Ui.D(28), ForeColor = Ui.Muted, Padding = new Padding(Ui.D(16), 0, 0, 0) };
        int other;

        static K[] R(params K[] k) { return k; }

        public KeyboardTest(Ctx c) : base(c, "Teclado", "entrada", "teclado", "Todas funcionan", "Alguna tecla falla")
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(Ui.D(1040), area.Width), Math.Min(Ui.D(560), area.Height));
            Hint.Text = "Pulsa cada tecla: se pondrá verde. Las que sigan grises no se detectaron. La tecla Windows y algunas teclas Fn no se pueden capturar siempre.";
            BadAction = "Limpia el teclado. Si las mismas teclas siguen sin responder, hay que cambiarlo, o conectar uno USB para seguir trabajando.";
            ExtraNote = () => pressed.Count + " teclas detectadas.";
            rows = new List<List<K>>
            {
                R(new K("Esc",1),new K("F1",59),new K("F2",60),new K("F3",61),new K("F4",62),new K("F5",63),new K("F6",64),new K("F7",65),new K("F8",66),new K("F9",67),new K("F10",68),new K("F11",87),new K("F12",88),new K("ImpPt",311),new K("Bloq",70),new K("Pausa",69+1000)).ToList(),
                R(new K("|",41),new K("1",2),new K("2",3),new K("3",4),new K("4",5),new K("5",6),new K("6",7),new K("7",8),new K("8",9),new K("9",10),new K("0",11),new K("'",12),new K("¿",13),new K("Borrar",14,2f),new K("Ins",338),new K("Inicio",327),new K("RePág",329)).ToList(),
                R(new K("Tab",15,1.5f),new K("Q",16),new K("W",17),new K("E",18),new K("R",19),new K("T",20),new K("Y",21),new K("U",22),new K("I",23),new K("O",24),new K("P",25),new K("´",26),new K("+",27),new K("}",43,1.5f),new K("Supr",339),new K("Fin",335),new K("AvPág",337)).ToList(),
                R(new K("Mayús",58,1.75f),new K("A",30),new K("S",31),new K("D",32),new K("F",33),new K("G",34),new K("H",35),new K("J",36),new K("K",37),new K("L",38),new K("Ñ",39),new K("{",40),new K("Enter",28,2.25f)).ToList(),
                R(new K("Shift",42,1.25f),new K("<",86),new K("Z",44),new K("X",45),new K("C",46),new K("V",47),new K("B",48),new K("N",49),new K("M",50),new K(",",51),new K(".",52),new K("-",53),new K("Shift",54,2.75f),new K("↑",328)).ToList(),
                R(new K("Ctrl",29,1.25f),new K("Win",347,1.25f),new K("Alt",56,1.25f),new K("Espacio",57,6.25f),new K("AltGr",312,1.25f),new K("Menú",349),new K("Ctrl",285,1.25f),new K("←",331),new K("↓",336),new K("→",333)).ToList()
            };
            canvas.Paint += PaintKeys;
            Body.Controls.Add(canvas);
            Body.Controls.Add(info);
            Load += (s, e) => Application.AddMessageFilter(this);
            FormClosed += (s, e) => Application.RemoveMessageFilter(this);
        }

        // Las teclas se capturan antes que los botones, para que Espacio o Enter no pulsen nada.
        public bool PreFilterMessage(ref Message m)
        {
            const int KEYDOWN = 0x100, KEYUP = 0x101, SYSDOWN = 0x104, SYSUP = 0x105;
            if (m.Msg != KEYDOWN && m.Msg != SYSDOWN && m.Msg != KEYUP && m.Msg != SYSUP) return false;
            long lp = m.LParam.ToInt64();
            int scan = (int)((lp >> 16) & 0xFF);
            bool ext = ((lp >> 24) & 1) != 0;
            int code = scan + (ext ? 256 : 0);
            int vk = m.WParam.ToInt32();
            if (vk == 0x13) code = 1069;                // Pausa
            else if (vk == 0x90) code = 1069 + 1;       // Bloq Num
            else if (vk == 0x2C) code = 311;            // Impr Pant solo envía la subida
            if (m.Msg == KEYDOWN || m.Msg == SYSDOWN || vk == 0x2C)
            {
                if (pressed.Add(code))
                {
                    if (!rows.Any(r => r.Any(k => k.Code == code))) other++;
                    info.Text = pressed.Count + " teclas detectadas" + (other > 0 ? ", " + other + " de ellas fuera de este dibujo (teclado numérico o multimedia)." : ".");
                    canvas.Invalidate();
                }
            }
            return true;
        }

        void PaintKeys(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            float pad = Ui.D(12), gap = Ui.D(4);
            float units = rows.Max(r => r.Sum(k => k.W));
            float unit = (canvas.ClientSize.Width - pad * 2) / units;
            float h = Math.Min(unit, (canvas.ClientSize.Height - pad) / rows.Count);
            float y = pad / 2;
            using (var line = new Pen(Ui.Line))
                foreach (var row in rows)
                {
                    float x = pad;
                    foreach (var k in row)
                    {
                        var r = new RectangleF(x, y, k.W * unit - gap, h - gap);
                        bool on = pressed.Contains(k.Code) || (k.Code == 1069 && pressed.Contains(1069));
                        using (var b = new SolidBrush(on ? Ui.Of(St.Ok) : Ui.Page)) g.FillRectangle(b, r);
                        g.DrawRectangle(line, r.X, r.Y, r.Width, r.Height);
                        TextRenderer.DrawText(g, k.L, Ui.Small, Rectangle.Round(r), on ? Color.White : Ui.Ink,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                        x += k.W * unit;
                    }
                    y += h;
                }
        }
    }

    public class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    }

    // ---------- ratón ----------

    public class MouseTest : TestForm
    {
        readonly Panel area = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Ui.Page };
        readonly HashSet<string> done = new HashSet<string>();
        Point last;
        double traveled;

        public MouseTest(Ctx c) : base(c, "Ratón o touchpad", "entrada", "raton", "Todo responde", "Algo no responde")
        {
            Hint.Text = "Dentro del recuadro gris: mueve el puntero, haz clic izquierdo y derecho, y gira la rueda o desliza dos dedos.";
            BadAction = "Limpia el touchpad o prueba otro ratón. En un portátil, revisa la tecla que lo desactiva y su controlador.";
            ExtraNote = () => "Detectado: " + (done.Count == 0 ? "nada" : string.Join(", ", done)) + ".";
            area.MouseMove += (s, e) =>
            {
                if (!last.IsEmpty) traveled += Math.Abs(e.X - last.X) + Math.Abs(e.Y - last.Y);
                last = e.Location;
                if (traveled > Ui.D(300)) Mark("movimiento");
            };
            area.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) Mark("clic izquierdo");
                else if (e.Button == MouseButtons.Right) Mark("clic derecho");
                else if (e.Button == MouseButtons.Middle) Mark("clic central");
                else Mark("botón extra");
            };
            area.MouseEnter += (s, e) => area.Focus();
            MouseWheel += (s, e) => Mark("rueda");
            area.Paint += (s, e) =>
            {
                int y = Ui.D(16);
                foreach (var it in new[] { "movimiento", "clic izquierdo", "clic derecho", "rueda" })
                {
                    bool on = done.Contains(it);
                    Ui.Badge(e.Graphics, on ? St.Ok : St.Unknown, Ui.D(16), y, Ui.D(22));
                    TextRenderer.DrawText(e.Graphics, char.ToUpper(it[0]) + it.Substring(1) + (on ? ": detectado" : ": pendiente"), Ui.Body, new Point(Ui.D(46), y + Ui.D(1)), Ui.Ink);
                    y += Ui.D(34);
                }
            };
            Body.Controls.Add(area);
        }

        void Mark(string what) { if (done.Add(what)) area.Invalidate(); }
    }

    // ---------- puertos en vivo ----------

    public class PortsTest : TestForm
    {
        readonly ListBox log = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false };
        readonly FormsTimer tick = new FormsTimer { Interval = 2000 };
        Dictionary<string, string> usb, monitors, nets;
        volatile bool scanning;
        int usbEvents, videoEvents, netEvents;

        public PortsTest(Ctx c) : base(c, "Puertos", "puertos", "puertos", "Los puertos probados responden", "Algún puerto no responde")
        {
            var a = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(Ui.D(760), a.Width), Math.Min(Ui.D(560), a.Height));
            Hint.Text = "Conecta y desconecta un USB en cada puerto, un monitor o TV en HDMI o DisplayPort, y un cable de red en RJ45. Lo que se detecte aparecerá aquí en unos segundos.";
            BadAction = "Prueba otro dispositivo y otro cable en ese puerto. Si con varios no responde, el puerto está dañado. Anota cuál es para el técnico.";
            ExtraNote = () => "Eventos detectados: USB " + usbEvents + ", vídeo " + videoEvents + ", red " + netEvents + ".";
            Body.Padding = new Padding(Ui.D(16), Ui.D(4), Ui.D(16), Ui.D(4));
            Body.Controls.Add(log);
            log.Items.Add("Leyendo el estado inicial…");
            tick.Tick += (s, e) => Scan();
            Shown += (s, e) => { Scan(); tick.Start(); };
            FormClosed += (s, e) => tick.Stop();
        }

        void Scan()
        {
            if (scanning) return;
            scanning = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Dictionary<string, string> u = null, m = null, n = null;
                try
                {
                    u = new Dictionary<string, string>();
                    foreach (var d in Wmi.Q(@"root\cimv2", "SELECT Name,DeviceID FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID%'", 6))
                    {
                        string id = Wmi.S(d, "DeviceID");
                        if (!id.Contains("&MI_")) u[id] = Wmi.S(d, "Name");
                    }
                }
                catch { u = null; }
                try
                {
                    m = new Dictionary<string, string>();
                    foreach (var d in Wmi.QOrEmpty(@"root\wmi", "SELECT InstanceName,VideoOutputTechnology FROM WmiMonitorConnectionParams", 5))
                        m[Wmi.S(d, "InstanceName")] = Probes.OutputName(Wmi.L(d, "VideoOutputTechnology"));
                }
                catch { m = null; }
                try
                {
                    n = new Dictionary<string, string>();
                    foreach (var i in NetworkInterface.GetAllNetworkInterfaces())
                        if (i.NetworkInterfaceType == NetworkInterfaceType.Ethernet && i.OperationalStatus == OperationalStatus.Up &&
                            !i.Description.ToLowerInvariant().Contains("virtual") && !i.Description.ToLowerInvariant().Contains("bluetooth"))
                            n[i.Id] = i.Description + ", " + (i.Speed / 1000000) + " Mbps";
                }
                catch { n = null; }
                try { if (IsHandleCreated) BeginInvoke((Action)(() => Apply(u, m, n))); }
                catch { }
                scanning = false;
            });
        }

        void Apply(Dictionary<string, string> u, Dictionary<string, string> m, Dictionary<string, string> n)
        {
            bool first = usb == null && monitors == null && nets == null;
            if (first)
            {
                log.Items.Clear();
                log.Items.Add("Listo: conecta algo en un puerto.");
                log.Items.Add("Conectados ahora. USB: " + (u != null ? u.Count : 0) + ", pantallas: " + (m != null ? m.Count : 0) + ", cables de red: " + (n != null ? n.Count : 0) + ".");
            }
            else
            {
                Diff(usb, u, "USB", ref usbEvents);
                Diff(monitors, m, "Pantalla", ref videoEvents);
                Diff(nets, n, "Cable de red", ref netEvents);
            }
            if (u != null) usb = u;
            if (m != null) monitors = m;
            if (n != null) nets = n;
        }

        void Diff(Dictionary<string, string> before, Dictionary<string, string> now, string kind, ref int counter)
        {
            if (before == null || now == null) return;
            foreach (var k in now.Keys.Except(before.Keys))
            {
                counter++;
                Log(kind + " conectado: " + now[k]);
            }
            foreach (var k in before.Keys.Except(now.Keys))
            {
                counter++;
                Log(kind + " desconectado: " + before[k]);
            }
        }

        void Log(string text)
        {
            log.Items.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + text);
            log.TopIndex = Math.Max(0, log.Items.Count - 1);
        }
    }

    // ---------- pantalla ----------

    public static class ScreenTest
    {
        public static void Run(Form owner, Ctx c)
        {
            Color[] colors = { Color.White, Color.Black, Color.Red, Color.Lime, Color.Blue };
            using (var f = new Form { FormBorderStyle = FormBorderStyle.None, WindowState = FormWindowState.Maximized, TopMost = true, BackColor = colors[0], Cursor = Cursors.Hand, KeyPreview = true, ShowInTaskbar = false })
            {
                int i = 0;
                var tip = new Label
                {
                    AutoSize = true, BackColor = Color.FromArgb(0x20, 0x26, 0x2B), ForeColor = Color.White, Font = Ui.Body,
                    Padding = new Padding(Ui.D(10)), Location = new Point(Ui.D(24), Ui.D(24)),
                    Text = "Busca puntos muertos, manchas o líneas en cada color.\nHaz clic o pulsa Espacio para cambiar de color. Esc para salir."
                };
                f.Controls.Add(tip);
                Action next = () =>
                {
                    tip.Visible = false;
                    i++;
                    if (i >= colors.Length) f.Close();
                    else f.BackColor = colors[i];
                };
                f.Click += (s, e) => next();
                tip.Click += (s, e) => next();
                f.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape) f.Close();
                    else if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) next();
                };
                f.ShowDialog(owner);
            }
            var answer = MessageBox.Show(owner, "¿La pantalla se vio bien en todos los colores, sin puntos ni líneas?\n\nSí: se ve bien.  No: tiene defectos.  Cancelar: no pude probarla.",
                "Prueba de pantalla", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes)
                c.SetManual("video", "pantalla", St.Ok, "Prueba manual: Pantalla", "Se vio bien en todos los colores.", null);
            else if (answer == DialogResult.No)
                c.SetManual("video", "pantalla", St.Bad, "Prueba manual: Pantalla", "Se vieron puntos, manchas o líneas.",
                    "Conecta un monitor externo: si ahí se ve bien, el daño está en el panel; si no, en la gráfica o el cable.");
            else
                c.SetManual("video", "pantalla", St.Unknown, "Prueba manual: Pantalla", "La prueba no se pudo hacer.", "Repite esta prueba cuando puedas.");
        }
    }
}
