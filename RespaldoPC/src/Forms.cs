using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FormsTimer = System.Windows.Forms.Timer;

namespace RespaldoPC
{
    public static class Ui
    {
        public static readonly Color Ink = Color.FromArgb(0x1E, 0x2A, 0x32), Muted = Color.FromArgb(0x54, 0x62, 0x6D),
            Page = Color.FromArgb(0xEE, 0xF1, 0xF3), Slate = Color.FromArgb(0x2B, 0x4A, 0x5E);
        public static Font Body = new Font("Segoe UI", 10f), Bold = new Font("Segoe UI Semibold", 11.5f), H1 = new Font("Segoe UI Semibold", 18f), H2 = new Font("Segoe UI Semibold", 14f), Small = new Font("Segoe UI", 9f);
        public static float Scale = 1f;
        public static int D(int v) { return (int)Math.Round(v * Scale); }

        public static Button Btn(string text, bool primary, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat, Font = Body,
                Padding = new Padding(D(8), D(3), D(8), D(3)), Margin = new Padding(D(4)), UseVisualStyleBackColor = false,
                BackColor = primary ? Slate : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = primary ? Slate : Color.FromArgb(0x9A, 0xA6, 0xB0);
            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static Label Text(string text, Font font = null, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Ink, Margin = new Padding(D(4), D(6), D(4), D(2)) };
        }
    }

    public class MainForm : Form
    {
        readonly Panel band = new Panel { Dock = DockStyle.Top };
        readonly Label title = new Label { AutoEllipsis = true, ForeColor = Color.White, Font = Ui.H1, BackColor = Color.Transparent };
        readonly Label subtitle = new Label { AutoEllipsis = true, ForeColor = Color.White, Font = Ui.Body, BackColor = Color.Transparent };
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly FormsTimer timer = new FormsTimer { Interval = 300 };
        EventHandler timerHandler;
        bool busy;
        readonly bool admin;

        public MainForm()
        {
            admin = Admin.Is();
            using (var g = CreateGraphics()) Ui.Scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Respaldo y migración";
            Font = Ui.Body;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(Ui.D(720), area.Width), Math.Min(Ui.D(500), area.Height));
            Size = new Size(Math.Min(Ui.D(980), area.Width), Math.Min(Ui.D(720), area.Height));
            StartPosition = FormStartPosition.CenterScreen;
            Comun.WindowFit.Apply(this);

            band.Height = Ui.D(88);
            band.BackColor = Ui.Slate;
            band.Controls.Add(title);
            band.Controls.Add(subtitle);
            band.Resize += (s, e) =>
            {
                int pad = Ui.D(20), w = band.ClientSize.Width - pad * 2;
                title.SetBounds(pad, Ui.D(10), w, Ui.D(42));
                subtitle.SetBounds(pad, Ui.D(54), w, Ui.D(26));
            };
            Controls.Add(content);
            if (!admin) Controls.Add(PermissionBar());
            Controls.Add(band);
            FormClosing += (s, e) =>
            {
                if (busy && MessageBox.Show(this, "Hay una copia en curso. Si cierras, lo copiado se conserva y podrás continuar después.\n\n¿Cerrar?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    e.Cancel = true;
            };
            FormClosed += (s, e) => Environment.Exit(0);
            ShowHome();
        }

        Control PermissionBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(0xFB, 0xEE, 0xD5), Padding = new Padding(Ui.D(12), Ui.D(2), Ui.D(12), Ui.D(2)) };
            bar.Controls.Add(new Label { Text = "Sin administrador solo se puede copiar tu propio usuario, sin controladores ni claves Wi-Fi.", AutoSize = true, ForeColor = Ui.Ink, Margin = new Padding(Ui.D(4), Ui.D(9), Ui.D(4), Ui.D(4)) });
            bar.Controls.Add(Ui.Btn("Reabrir como administrador", false, (s, e) =>
            {
                if (busy) { MessageBox.Show(this, "Espera a que termine la copia.", Text); return; }
                if (Program.Relaunch("--no-elevar")) Environment.Exit(0);
                else MessageBox.Show(this, "No se concedió el permiso. Puedes seguir con lo disponible.", Text);
            }));
            return bar;
        }

        void Header(string t, string s, Color? c = null)
        {
            title.Text = t;
            subtitle.Text = s;
            band.BackColor = c ?? Ui.Slate;
        }

        void Page(Control c)
        {
            if (timerHandler != null) { timer.Tick -= timerHandler; timerHandler = null; }
            timer.Stop();
            content.SuspendLayout();
            foreach (Control old in content.Controls.Cast<Control>().ToList()) { content.Controls.Remove(old); old.Dispose(); }
            c.Dock = DockStyle.Fill;
            content.Controls.Add(c);
            content.ResumeLayout();
        }

        void Tick(EventHandler h)
        {
            timerHandler = h;
            timer.Tick += h;
            timer.Start();
        }

        // ---------- inicio ----------

        void ShowHome()
        {
            Header("Respaldo y migración", "Guarda lo del cliente antes de formatear y devuélvelo al Windows limpio.");
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(Ui.D(24), Ui.D(20), Ui.D(24), Ui.D(10)) };
            Action<string, string, Action> option = (name, desc, go) =>
            {
                var b = Ui.Btn(name, true, (s, e) => go());
                b.Font = Ui.Bold;
                b.Margin = new Padding(Ui.D(4), Ui.D(12), Ui.D(4), Ui.D(4));
                flow.Controls.Add(b);
                var l = Ui.Text(desc, null, Ui.Muted);
                l.MaximumSize = new Size(Ui.D(720), 0);
                flow.Controls.Add(l);
            };
            option("Respaldar", "Copia a un disco externo los archivos, favoritos, redes Wi-Fi y controladores, y la lista de programas. También funciona con un disco de otro equipo que ya no arranca, conectado a este.", ShowBackup);
            option("Restaurar", "Devuelve un respaldo al Windows limpio. Nunca sobrescribe: si un archivo distinto ya existe, conserva los dos.", ShowRestore);
            var note = Ui.Text("Esto no clona el disco. Windows y los programas no se copian: solo lo que es del cliente.", Ui.Small, Ui.Muted);
            note.Margin = new Padding(Ui.D(4), Ui.D(24), Ui.D(4), 0);
            flow.Controls.Add(note);
            var creditsButton = Ui.Btn("Créditos", false, (s, e) => ShowCredits());
            creditsButton.Margin = new Padding(Ui.D(4), Ui.D(20), Ui.D(4), Ui.D(4));
            flow.Controls.Add(creditsButton);
            Page(flow);
        }

        void ShowCredits()
        {
            Header("Créditos", "Autor y sobre el programa.");
            var host = new Panel();
            var panel = new Comun.CreditsPanel(Program.Info, Ui.Ink, Ui.Muted) { Dock = DockStyle.Fill };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Ui.D(12), Ui.D(4), Ui.D(12), Ui.D(8)) };
            bar.Controls.Add(Ui.Btn("Volver", true, (s, e) => ShowHome()));
            host.Controls.Add(panel);
            host.Controls.Add(bar);
            Page(host);
        }

        // ---------- árbol compartido ----------

        TreeView NewTree()
        {
            var tree = new TreeView { CheckBoxes = true, BorderStyle = BorderStyle.FixedSingle, HideSelection = true, ShowLines = false, ItemHeight = Ui.D(24) };
            tree.AfterCheck += (s, e) =>
            {
                if (e.Action == TreeViewAction.Unknown) return;
                Apply(e.Node);
                foreach (TreeNode child in e.Node.Nodes) { child.Checked = e.Node.Checked; Apply(child); }
            };
            return tree;
        }

        static void Apply(TreeNode n)
        {
            var c = n.Tag as CopyTask;
            if (c != null) c.Selected = n.Checked;
            var st = n.Tag as SystemTask;
            if (st != null) st.Selected = n.Checked;
        }

        static string TaskText(CopyTask t, bool measuring)
        {
            string size = t.Count > 0 || !measuring ? Fs.Bytes(t.Size) + ", " + t.Count.ToString("N0") + (t.Count == 1 ? " archivo" : " archivos") : "calculando…";
            return t.Label + " (" + size + ")" + (t.Hint != null ? ". " + t.Hint : "");
        }

        void FillTree(TreeView tree, List<CopyTask> tasks, List<SystemTask> system, string systemGroup)
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            foreach (var g in tasks.GroupBy(t => t.Group))
            {
                var parent = tree.Nodes.Add(g.Key);
                parent.Checked = g.Any(t => t.Selected);
                foreach (var t in g)
                {
                    var n = parent.Nodes.Add(TaskText(t, true));
                    n.Tag = t; n.Checked = t.Selected; t.Node = n;
                }
                parent.Expand();
            }
            if (system.Count > 0)
            {
                var parent = tree.Nodes.Add(systemGroup);
                parent.Checked = true;
                foreach (var s in system)
                {
                    var n = parent.Nodes.Add(s.Label + (s.Hint != null ? ". " + s.Hint : ""));
                    n.Tag = s; n.Checked = s.Selected;
                }
                parent.Expand();
            }
            tree.EndUpdate();
        }

        static void RefreshTree(List<CopyTask> tasks, bool measuring)
        {
            foreach (var t in tasks)
            {
                var n = t.Node as TreeNode;
                if (n == null) continue;
                string text = TaskText(t, measuring);
                if (n.Text != text) n.Text = text;
            }
        }

        void StartMeasure(List<CopyTask> tasks, Func<bool> stale, Action done)
        {
            var thread = new Thread(() =>
            {
                foreach (var t in tasks)
                {
                    if (stale()) return;
                    try { Backup.Measure(t, stale); } catch { }
                }
                if (!stale())
                    try { BeginInvoke(done); } catch { }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        // ---------- respaldo ----------

        void ShowBackup()
        {
            Header("Respaldar", "Elige de dónde, a dónde y qué copiar.");
            var layout = new TableLayoutPanel { ColumnCount = 3, RowCount = 5, Padding = new Padding(Ui.D(16), Ui.D(10), Ui.D(16), Ui.D(6)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 2; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var source = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            var dest = new TextBox { Dock = DockStyle.Fill };
            var pick = Ui.Btn("Elegir…", false, (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Elige dónde guardar el respaldo. Usa un disco distinto al que vas a formatear." })
                    if (d.ShowDialog(this) == DialogResult.OK) dest.Text = d.SelectedPath;
            });
            var tree = NewTree();
            tree.Dock = DockStyle.Fill;
            var summary = new Label { AutoSize = true, ForeColor = Ui.Ink, Margin = new Padding(Ui.D(4), Ui.D(6), Ui.D(4), Ui.D(2)) };
            var verify = new CheckBox { Text = "Verificar cada archivo después de copiarlo (recomendado)", Checked = true, AutoSize = true };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var start = Ui.Btn("Iniciar respaldo", true, null);
            start.Enabled = false;
            buttons.Controls.Add(start);
            buttons.Controls.Add(Ui.Btn("Volver", false, (s, e) => ShowHome()));
            buttons.Controls.Add(verify);
            verify.Margin = new Padding(Ui.D(4), Ui.D(9), Ui.D(16), 0);

            layout.Controls.Add(Ui.Text("Copiar desde"), 0, 0);
            layout.Controls.Add(source, 1, 0);
            layout.SetColumnSpan(source, 2);
            layout.Controls.Add(Ui.Text("Guardar en"), 0, 1);
            layout.Controls.Add(dest, 1, 1);
            layout.Controls.Add(pick, 2, 1);
            layout.Controls.Add(tree, 0, 2);
            layout.SetColumnSpan(tree, 3);
            layout.Controls.Add(summary, 0, 3);
            layout.SetColumnSpan(summary, 3);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 3);
            Page(layout);

            List<CopyTask> tasks = new List<CopyTask>();
            List<SystemTask> system = new List<SystemTask>();
            int generation = 0;
            bool measuring = false;
            string choose = null;

            Action update = () =>
            {
                long total = tasks.Where(t => t.Selected).Sum(t => t.Size);
                long free = dest.Text.Trim().Length > 0 ? Fs.FreeSpace(dest.Text.Trim()) : -1;
                summary.Text = "Seleccionado: " + Fs.Bytes(total) + (measuring ? " hasta ahora, calculando…" : ".") + (free >= 0 ? " Libres en el destino: " + Fs.Bytes(free) + "." : "");
                start.Enabled = !measuring && dest.Text.Trim().Length > 0 && tasks.Any(t => t.Selected);
            };
            Tick((s, e) => { if (measuring) RefreshTree(tasks, true); update(); });

            source.SelectedIndexChanged += (s, e) =>
            {
                var src = source.SelectedItem as SourceInfo;
                if (src == null) return;
                int gen = ++generation;
                measuring = true;
                tree.Nodes.Clear();
                tree.Nodes.Add("Buscando qué hay para respaldar…");
                var worker = new Thread(() =>
                {
                    List<CopyTask> t = null;
                    string error = null;
                    try { t = Backup.PlanFiles(src); } catch (Exception ex) { error = ex.Message; }
                    var sys = Backup.PlanSystem(src, admin);
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (gen != generation) return;
                            if (t == null)
                            {
                                measuring = false;
                                tree.Nodes.Clear();
                                tree.Nodes.Add("No se pudo leer este disco: " + error);
                                return;
                            }
                            tasks = t; system = sys;
                            FillTree(tree, tasks, system, "Configuración del equipo");
                            StartMeasure(tasks, () => gen != generation, () =>
                            {
                                measuring = false;
                                RefreshTree(tasks, false);
                            });
                        }));
                    }
                    catch { }
                });
                worker.IsBackground = true;
                worker.Start();
            };

            var sources = new List<SourceInfo>();
            try { sources = Backup.Sources(); } catch { }
            if (sources.Count == 0)
            {
                tree.Nodes.Add("No se encontró ningún disco con carpeta de usuarios. Conecta el disco y vuelve a entrar.");
            }
            foreach (var x in sources) source.Items.Add(x);
            if (source.Items.Count > 0) source.SelectedIndex = 0;

            start.Click += (s, e) =>
            {
                var src = (SourceInfo)source.SelectedItem;
                choose = Prepare(src, dest.Text.Trim(), tasks);
                if (choose != null) RunBackupPage(src, choose, tasks, system, verify.Checked);
            };
        }

        // Devuelve la carpeta del respaldo, o null si la persona decide no seguir.
        string Prepare(SourceInfo src, string dest, List<CopyTask> tasks)
        {
            try { N.EnsureDir(dest); }
            catch { MessageBox.Show(this, "No se puede usar esa carpeta de destino. Elige otra.", Text); return null; }

            int a = Fs.PhysicalDisk(src.Root), b = Fs.PhysicalDisk(dest);
            if (a >= 0 && a == b &&
                MessageBox.Show(this, "El destino está en el MISMO disco físico que el origen.\n\nSi formateas ese disco o falla, perderás también el respaldo.\n\n¿Seguir de todos modos?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return null;

            string resume = Jobs.FindIncomplete(dest, src);
            long total = tasks.Where(t => t.Selected).Sum(t => t.Size), free = Fs.FreeSpace(dest);
            if (resume == null && free >= 0 && free < total)
            {
                MessageBox.Show(this, "No cabe. Hacen falta " + Fs.Bytes(total) + " y hay " + Fs.Bytes(free) + " libres en el destino.\n\nUsa otro disco o quita elementos de la lista.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            if (Fs.FileSystem(dest).StartsWith("FAT", StringComparison.OrdinalIgnoreCase) && !Fs.FileSystem(dest).Equals("exFAT", StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this, "El destino es FAT32: los archivos de 4 GB o más no se podrán copiar y quedarán en la lista de fallos.\n\n¿Seguir?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return null;

            var ps = SystemInformation.PowerStatus;
            if (ps.PowerLineStatus == PowerLineStatus.Offline && ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery &&
                MessageBox.Show(this, "El equipo está funcionando con batería. Conviene enchufarlo antes de una copia larga.\n\n¿Seguir?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return null;

            if (src.IsCurrentWindows)
            {
                var open = OpenApps();
                if (open.Count > 0 &&
                    MessageBox.Show(this, "Estos programas están abiertos y pueden bloquear archivos:\n\n" + string.Join(", ", open) + "\n\nCiérralos antes de seguir, o sus archivos en uso quedarán en la lista de fallos.\n\n¿Seguir?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    return null;
            }

            if (resume != null)
            {
                var answer = MessageBox.Show(this, "Hay un respaldo sin terminar en este destino:\n" + Path.GetFileName(resume) + "\n\n¿Continuarlo? Solo se copiará lo que falta.\n\nSí: continuar.  No: crear uno nuevo.", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return null;
                if (answer == DialogResult.Yes) return resume;
                if (free >= 0 && free < total)
                {
                    MessageBox.Show(this, "Un respaldo nuevo no cabe: hacen falta " + Fs.Bytes(total) + " y hay " + Fs.Bytes(free) + " libres.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
            }
            return Jobs.NewFolder(dest, src);
        }

        static List<string> OpenApps()
        {
            var known = new Dictionary<string, string> { { "outlook", "Outlook" }, { "chrome", "Chrome" }, { "msedge", "Edge" }, { "firefox", "Firefox" }, { "brave", "Brave" }, { "thunderbird", "Thunderbird" }, { "winword", "Word" }, { "excel", "Excel" }, { "powerpnt", "PowerPoint" } };
            var open = new List<string>();
            foreach (var k in known)
                try { if (Process.GetProcessesByName(k.Key).Length > 0) open.Add(k.Value); } catch { }
            return open;
        }

        void RunBackupPage(SourceInfo src, string root, List<CopyTask> tasks, List<SystemTask> system, bool verify)
        {
            var engine = new CopyEngine { Verify = verify };
            RunPage("Respaldando…", "No desconectes el disco de destino. Puedes cancelar y continuar después.", engine, status =>
                Jobs.RunBackup(src, root, tasks, system, engine, status),
                r => ResultPage(r, engine, root, () => RunBackupPage(src, root, tasks, system, verify), false));
        }

        // ---------- progreso ----------

        void RunPage(string head, string sub, CopyEngine engine, Func<Action<string>, JobResult> work, Action<JobResult> finished)
        {
            Header(head, sub);
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Ui.D(24), Ui.D(24), Ui.D(24), Ui.D(10)) };
            var percent = Ui.Text("0%", Ui.H1);
            var bar = new ProgressBar { Width = Ui.D(700), Height = Ui.D(22), Maximum = 1000, Margin = new Padding(Ui.D(4)) };
            var counts = Ui.Text("Preparando…");
            var current = new Label { AutoSize = false, Width = Ui.D(700), Height = Ui.D(44), ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(Ui.D(4)), AutoEllipsis = true };
            var cancel = Ui.Btn("Cancelar", false, null);
            cancel.Click += (s, e) => { engine.Cancel = true; cancel.Enabled = false; cancel.Text = "Terminando el archivo actual…"; };
            flow.Controls.AddRange(new Control[] { percent, bar, counts, current, cancel });
            flow.Resize += (s, e) =>
            {
                int w = Math.Max(Ui.D(300), flow.ClientSize.Width - Ui.D(60));
                bar.Width = w; current.Width = w;
            };
            Page(flow);

            string status = null;
            busy = true;
            var clock = Stopwatch.StartNew();
            JobResult result = null;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try { result = work(s => status = s); }
                catch (Exception ex) { failure = ex; }
            });
            thread.IsBackground = true;
            thread.Start();

            Tick((s, e) =>
            {
                long done = Interlocked.Read(ref engine.BytesDone), total = engine.BytesTotal;
                double frac = total > 0 ? Math.Min(1.0, (double)done / total) : 0;
                percent.Text = (frac * 100).ToString("0") + "%";
                bar.Value = (int)(frac * 1000);
                string eta = "";
                if (frac > 0.01 && frac < 1)
                {
                    var left = TimeSpan.FromSeconds(clock.Elapsed.TotalSeconds * (1 - frac) / frac);
                    eta = ". Quedan unos " + (left.TotalMinutes >= 1 ? (int)left.TotalMinutes + " min" : (int)left.TotalSeconds + " s");
                }
                long fails = Interlocked.Read(ref engine.FailCount);
                counts.Text = Interlocked.Read(ref engine.FilesDone).ToString("N0") + " archivos, " + Fs.Bytes(done) + " de " + Fs.Bytes(total) + eta + (fails > 0 ? ". Con problemas: " + fails : "") + ".";
                current.Text = status ?? engine.Current;

                if (thread.IsAlive) return;
                busy = false;
                if (failure != null)
                {
                    Program.LogCrash(failure);
                    finished(new JobResult { Verdict = "La operación no pudo terminar", Detail = failure.Message + ". Revisa el destino y pulsa «Reintentar».", Color = Jobs.Red });
                }
                else finished(result);
            });
        }

        // ---------- resultado ----------

        void ResultPage(JobResult r, CopyEngine engine, string folder, Action retry, bool isRestore)
        {
            Header(r.Verdict, r.Detail, ColorTranslator.FromHtml(r.Color));
            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.D(16), Ui.D(10), Ui.D(16), Ui.D(6)) };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.Controls.Add(Ui.Text(engine.Failed.Count > 0 ? "Esto no se copió:" : "Qué falta hacer a mano:", Ui.H2), 0, 0);

            var list = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, HorizontalScrollbar = true };
            foreach (var f in engine.Failed.Take(2000)) list.Items.Add(f.Reason + "   " + f.Path);
            if (engine.Failed.Count == 0)
                foreach (var p in r.Pending) list.Items.Add(p);
            layout.Controls.Add(list, 0, 1);

            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            bar.Controls.Add(Ui.Btn("Volver al inicio", !r.Complete ? false : true, (s, e) => ShowHome()));
            if (!r.Complete) bar.Controls.Add(Ui.Btn("Reintentar", true, (s, e) => retry()));
            if (!string.IsNullOrEmpty(r.ReportPath))
                bar.Controls.Add(Ui.Btn("Abrir informe", false, (s, e) => Open(r.ReportPath)));
            if (!string.IsNullOrEmpty(folder))
                bar.Controls.Add(Ui.Btn("Abrir carpeta", false, (s, e) => Open(folder)));
            string wifi = string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, "Sistema", "WiFi");
            if (isRestore && wifi != null && Directory.Exists(wifi))
                bar.Controls.Add(Ui.Btn("Borrar claves Wi-Fi del respaldo", false, (s, e) =>
                {
                    if (MessageBox.Show(this, "Se borrarán del respaldo las claves Wi-Fi guardadas. Las redes que ya restauraste siguen en este equipo.\n\n¿Borrar?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    try
                    {
                        Directory.Delete(wifi, true);
                        ((Button)s).Enabled = false;
                        ((Button)s).Text = "Claves Wi-Fi borradas";
                    }
                    catch (Exception ex) { MessageBox.Show(this, "No se pudieron borrar: " + ex.Message, Text); }
                }));
            layout.Controls.Add(bar, 0, 2);
            Page(layout);
        }

        void Open(string path)
        {
            try { Process.Start(path); }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo abrir: " + ex.Message + "\n\n" + path, Text); }
        }

        // ---------- restauración ----------

        const string ChooseUser = "(elige un usuario)";

        void ShowRestore()
        {
            Header("Restaurar", "Elige el respaldo y a qué usuario de este Windows devolver cada perfil.");
            var layout = new TableLayoutPanel { ColumnCount = 3, RowCount = 5, Padding = new Padding(Ui.D(16), Ui.D(10), Ui.D(16), Ui.D(6)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var folder = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.White };
            var mapping = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var tree = NewTree();
            tree.Dock = DockStyle.Fill;
            tree.Nodes.Add("Elige la carpeta de un respaldo para ver qué contiene.");
            var summary = new Label { AutoSize = true, ForeColor = Ui.Ink, Margin = new Padding(Ui.D(4), Ui.D(6), Ui.D(4), Ui.D(2)) };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var start = Ui.Btn("Iniciar restauración", true, null);
            start.Enabled = false;
            buttons.Controls.Add(start);
            buttons.Controls.Add(Ui.Btn("Volver", false, (s, e) => ShowHome()));

            string systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            var targets = Restore.TargetUsers(systemRoot);
            string me = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var combos = new Dictionary<string, ComboBox>();
            List<CopyTask> tasks = new List<CopyTask>();
            List<SystemTask> system = new List<SystemTask>();
            bool measuring = false;
            int generation = 0;
            string backup = null;

            Action rebuild = () =>
            {
                if (backup == null) return;
                int gen = ++generation;
                tasks = new List<CopyTask>();
                foreach (var c in combos)
                {
                    string target = c.Value.SelectedItem as string;
                    if (target != null && !target.StartsWith("(")) tasks.AddRange(Restore.PlanUser(backup, c.Key, target));
                }
                tasks.AddRange(Restore.PlanOthers(backup, systemRoot));
                system = Restore.PlanSystem(backup, Info.Get(Info.Read(backup), "modelo"));
                FillTree(tree, tasks, system, "Configuración del equipo");
                measuring = true;
                StartMeasure(tasks, () => gen != generation, () => { measuring = false; RefreshTree(tasks, false); });
            };

            Action<string> load = path =>
            {
                var info = Info.Read(path);
                if (info == null)
                {
                    MessageBox.Show(this, "Esa carpeta no es un respaldo de este programa: no contiene «" + Info.FileName + "».\n\nElige la carpeta que empieza por «Respaldo_».", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                backup = path;
                folder.Text = path;
                mapping.Controls.Clear();
                combos.Clear();
                bool incomplete = Info.Get(info, "estado") != "completo";
                var head = Ui.Text("Respaldo de " + Info.Get(info, "equipo") + " (" + Info.Get(info, "fecha") + ")" + (incomplete ? ". Quedó INCOMPLETO: puede faltar información." : "."), null, incomplete ? Color.FromArgb(0xC6, 0x28, 0x28) : Ui.Ink);
                mapping.Controls.Add(head);
                int unmatched = 0;
                foreach (var u in Restore.BackupUsers(path))
                {
                    var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
                    row.Controls.Add(Ui.Text("Usuario " + u + " del respaldo pasa a"));
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.D(320), Margin = new Padding(Ui.D(4)) };
                    combo.Items.AddRange(targets.Cast<object>().ToArray());
                    combo.Items.Add("(no restaurar)");
                    combo.Items.Add(ChooseUser);
                    var same = targets.FirstOrDefault(t => Path.GetFileName(t).Equals(u, StringComparison.OrdinalIgnoreCase));
                    string mine = targets.FirstOrDefault(t => t.Equals(me, StringComparison.OrdinalIgnoreCase));
                    // Sin coincidencia, solo el primero va al usuario actual: mezclar a varias personas debe ser una decisión.
                    combo.SelectedItem = same ?? (unmatched++ == 0 && mine != null ? mine : ChooseUser);
                    combo.SelectedIndexChanged += (s, e) => rebuild();
                    row.Controls.Add(combo);
                    mapping.Controls.Add(row);
                    combos[u] = combo;
                }
                rebuild();
            };

            var pick = Ui.Btn("Elegir…", false, (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Elige la carpeta del respaldo (empieza por «Respaldo_»)." })
                    if (d.ShowDialog(this) == DialogResult.OK) load(d.SelectedPath);
            });

            layout.Controls.Add(Ui.Text("Respaldo"), 0, 0);
            layout.Controls.Add(folder, 1, 0);
            layout.Controls.Add(pick, 2, 0);
            layout.Controls.Add(mapping, 0, 1);
            layout.SetColumnSpan(mapping, 3);
            layout.Controls.Add(tree, 0, 2);
            layout.SetColumnSpan(tree, 3);
            layout.Controls.Add(summary, 0, 3);
            layout.SetColumnSpan(summary, 3);
            layout.Controls.Add(buttons, 0, 4);
            layout.SetColumnSpan(buttons, 3);
            Page(layout);

            Tick((s, e) =>
            {
                if (measuring) RefreshTree(tasks, true);
                long total = tasks.Where(t => t.Selected).Sum(t => t.Size);
                summary.Text = backup == null ? "" : "Seleccionado: " + Fs.Bytes(total) + (measuring ? " hasta ahora, calculando…" : ".") + " Libres en este disco: " + Fs.Bytes(Math.Max(0, Fs.FreeSpace(systemRoot))) + ".";
                bool undecided = combos.Values.Any(c => ChooseUser.Equals(c.SelectedItem));
                if (undecided) summary.Text += " Elige a qué usuario pasa cada perfil.";
                start.Enabled = backup != null && !measuring && !undecided && (tasks.Any(t => t.Selected) || system.Any(x => x.Selected));
            });

            string newest = FindNewestBackup();
            if (newest != null) load(newest);

            start.Click += (s, e) =>
            {
                long total = tasks.Where(t => t.Selected).Sum(t => t.Size), free = Fs.FreeSpace(systemRoot);
                if (free >= 0 && free < total)
                {
                    MessageBox.Show(this, "No cabe: hacen falta " + Fs.Bytes(total) + " y hay " + Fs.Bytes(free) + " libres.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                var open = OpenApps();
                if (open.Count > 0 &&
                    MessageBox.Show(this, "Estos programas están abiertos:\n\n" + string.Join(", ", open) + "\n\nCiérralos para que sus favoritos y datos se restauren bien.\n\n¿Seguir?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    return;
                RunRestorePage(backup, tasks, system);
            };
        }

        // Lo normal es que el respaldo esté en la raíz del disco externo: se ofrece sin tener que buscarlo.
        static string FindNewestBackup()
        {
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady || (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable)) continue;
                    foreach (var dir in Directory.GetDirectories(d.RootDirectory.FullName, "Respaldo_*"))
                    {
                        if (Info.Read(dir) == null) continue;
                        var time = Directory.GetLastWriteTime(dir);
                        if (time > bestTime) { best = dir; bestTime = time; }
                    }
                }
                catch { }
            }
            return best;
        }

        void RunRestorePage(string backup, List<CopyTask> tasks, List<SystemTask> system)
        {
            var engine = new CopyEngine();
            RunPage("Restaurando…", "No desconectes el disco del respaldo.", engine, status =>
                Jobs.RunRestore(backup, tasks, system, engine, status),
                r => ResultPage(r, engine, backup, () => RunRestorePage(backup, tasks, system), true));
        }
    }
}
