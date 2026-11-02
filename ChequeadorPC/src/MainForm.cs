using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace ChequeadorPC
{
    public static class Ui
    {
        public static readonly Color Ink = Color.FromArgb(0x1E, 0x2A, 0x32), Muted = Color.FromArgb(0x54, 0x62, 0x6D),
            Page = Color.FromArgb(0xEE, 0xF1, 0xF3), Line = Color.FromArgb(0xD5, 0xDB, 0xE0), Slate = Color.FromArgb(0x2B, 0x4A, 0x5E);

        public static Color Of(St s)
        {
            switch (s)
            {
                case St.Ok: return Color.FromArgb(0x1B, 0x7F, 0x4B);
                case St.Warn: return Color.FromArgb(0xA8, 0x62, 0x00);
                case St.Bad: return Color.FromArgb(0xC6, 0x28, 0x28);
                case St.Unknown: return Color.FromArgb(0x5F, 0x6B, 0x76);
                default: return Color.FromArgb(0x2B, 0x6A, 0x8E);
            }
        }

        public static Font Body = new Font("Segoe UI", 10f), Bold = new Font("Segoe UI Semibold", 10.5f), Small = new Font("Segoe UI", 9f),
            SmallBold = new Font("Segoe UI Semibold", 9f), Mono = new Font("Consolas", 9f), H1 = new Font("Segoe UI Semibold", 19f), H2 = new Font("Segoe UI Semibold", 14f);

        public static float Scale = 1f;
        public static int D(int v) { return (int)Math.Round(v * Scale); }

        public static void Badge(Graphics g, St s, int x, int y, int size)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Of(s))) g.FillEllipse(b, x, y, size, size);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            TextRenderer.DrawText(g, Fmt.StatusGlyph(s), SmallBold, new Rectangle(x, y, size, size), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

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
    }

    // Lista de hallazgos pintada a mano: una sola superficie, sin parpadeo aunque cambie durante la revisión.
    public class FindingView : Panel
    {
        List<Finding> items = new List<Finding>();
        List<int> tops = new List<int>();
        bool showTech, showSection;
        string empty = "";
        const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

        public FindingView()
        {
            DoubleBuffered = true;
            AutoScroll = true;
            BackColor = Ui.Page;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            TabStop = true;
        }

        public void SetData(List<Finding> list, bool tech, bool section, string emptyText)
        {
            items = list; showTech = tech; showSection = section; empty = emptyText;
            Relayout();
        }

        public void ResetScroll() { AutoScrollPosition = new Point(0, 0); }

        int ContentWidth { get { return Math.Max(Ui.D(200), ClientSize.Width - Ui.D(34)); } }
        int TextWidth { get { return Math.Min(ContentWidth - Ui.D(52), Ui.D(760)); } }

        static int H(string t, Font f, int w)
        {
            return TextRenderer.MeasureText(t, f, new Size(w, 0), Wrap).Height;
        }

        string TitleOf(Finding f) { return showSection ? f.SectionTitle + ": " + f.Title : f.Title; }

        string TechOf(Finding f) { return showTech ? f.Tech : null; }

        void Relayout()
        {
            tops.Clear();
            int y = Ui.D(8), tw = TextWidth;
            foreach (var f in items)
            {
                tops.Add(y);
                y += RowHeight(f, tw) + Ui.D(6);
            }
            var size = new Size(0, y + Ui.D(10));
            if (AutoScrollMinSize != size) AutoScrollMinSize = size;
            Invalidate();
        }

        int RowHeight(Finding f, int tw)
        {
            int h = Ui.D(10) + H(TitleOf(f), Ui.Bold, tw - Ui.D(110)) + Ui.D(2) + H(f.Plain ?? "", Ui.Body, tw);
            if (!string.IsNullOrEmpty(f.Action)) h += Ui.D(4) + H("Qué hacer: " + f.Action, Ui.Body, tw);
            if (!string.IsNullOrEmpty(TechOf(f))) h += Ui.D(6) + H(f.Tech, Ui.Mono, tw);
            return h + Ui.D(10);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int y = -AutoScrollPosition.Y, step = Ui.D(40), page = ClientSize.Height - Ui.D(40);
            if (e.KeyCode == Keys.Down) y += step;
            else if (e.KeyCode == Keys.Up) y -= step;
            else if (e.KeyCode == Keys.PageDown) y += page;
            else if (e.KeyCode == Keys.PageUp) y -= page;
            else if (e.KeyCode == Keys.Home) y = 0;
            else if (e.KeyCode == Keys.End) y = AutoScrollMinSize.Height;
            else return;
            AutoScrollPosition = new Point(0, Math.Max(0, y));
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ui.Page);
            int off = AutoScrollPosition.Y, left = Ui.D(16), tw = TextWidth;
            if (items.Count == 0)
            {
                TextRenderer.DrawText(g, empty, Ui.Body, new Rectangle(left, Ui.D(12) + off, tw, Ui.D(200)), Ui.Muted, Wrap);
                return;
            }
            for (int i = 0; i < items.Count && i < tops.Count; i++)
            {
                var f = items[i];
                int y = tops[i] + off, h = RowHeight(f, tw);
                if (y > ClientSize.Height || y + h < 0) continue;
                var rect = new Rectangle(left, y, ContentWidth, h);
                g.FillRectangle(Brushes.White, rect);
                using (var b = new SolidBrush(Ui.Of(f.Status))) g.FillRectangle(b, left, y, Ui.D(4), h);

                int x = left + Ui.D(14), cy = y + Ui.D(10);
                Ui.Badge(g, f.Status, x, cy, Ui.D(20));
                x += Ui.D(30);
                string word = Fmt.StatusWord(f.Status);
                TextRenderer.DrawText(g, word, Ui.SmallBold, new Rectangle(x + tw - Ui.D(106), cy, Ui.D(100), Ui.D(20)), Ui.Of(f.Status),
                    TextFormatFlags.Right | TextFormatFlags.NoPrefix);
                string title = TitleOf(f);
                int th = H(title, Ui.Bold, tw - Ui.D(110));
                TextRenderer.DrawText(g, title, Ui.Bold, new Rectangle(x, cy, tw - Ui.D(110), th), Ui.Ink, Wrap);
                cy += th + Ui.D(2);
                int ph = H(f.Plain ?? "", Ui.Body, tw);
                TextRenderer.DrawText(g, f.Plain ?? "", Ui.Body, new Rectangle(x, cy, tw, ph), Ui.Ink, Wrap);
                cy += ph;
                if (!string.IsNullOrEmpty(f.Action))
                {
                    string act = "Qué hacer: " + f.Action;
                    int ah = H(act, Ui.Body, tw);
                    cy += Ui.D(4);
                    TextRenderer.DrawText(g, act, Ui.Body, new Rectangle(x, cy, tw, ah), f.Status == St.Info ? Ui.Muted : Ui.Of(f.Status), Wrap);
                    cy += ah;
                }
                var tech = TechOf(f);
                if (!string.IsNullOrEmpty(tech))
                {
                    int mh = H(tech, Ui.Mono, tw);
                    cy += Ui.D(6);
                    TextRenderer.DrawText(g, tech, Ui.Mono, new Rectangle(x, cy, tw, mh), Ui.Muted, Wrap);
                }
            }
        }
    }

    public class MainForm : Form
    {
        readonly Ctx ctx;
        Engine engine;
        readonly Panel band = new Panel { Dock = DockStyle.Top };
        readonly Label verdict = new Label { AutoSize = false, ForeColor = Color.White, BackColor = Color.Transparent };
        readonly Label subline = new Label { AutoSize = false, ForeColor = Color.White, BackColor = Color.Transparent };
        readonly FlowLayoutPanel actions = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, BackColor = Color.Transparent };
        readonly ListBox nav = new ListBox { Dock = DockStyle.Left, DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.None, IntegralHeight = false, BackColor = Color.White };
        readonly Label heading = new Label { Dock = DockStyle.Top, AutoSize = false, ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft };
        readonly FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        readonly CheckBox techToggle = new CheckBox { Text = "Mostrar datos técnicos", AutoSize = true };
        readonly FindingView view = new FindingView { Dock = DockStyle.Fill };
        readonly Timer timer = new Timer { Interval = 300 };
        Button stop, save, again;
        int seenVersion = -1;
        bool wasDone;

        const string SummaryKey = "__resumen", CreditsKey = "__creditos";
        readonly Comun.CreditsPanel credits = new Comun.CreditsPanel(Program.Info, Ui.Ink, Ui.Muted) { Dock = DockStyle.Fill, Visible = false };

        public MainForm(Ctx c)
        {
            ctx = c;
            using (var g = CreateGraphics()) Ui.Scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Chequeador de PC";
            Font = Ui.Body;
            BackColor = Ui.Page;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // Se adapta a la pantalla, por pequeña que sea, sin salirse de ella.
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(Ui.D(760), area.Width), Math.Min(Ui.D(500), area.Height));
            Size = new Size(Math.Min(Ui.D(1120), area.Width), Math.Min(Ui.D(760), area.Height));
            StartPosition = FormStartPosition.CenterScreen;
            Comun.WindowFit.Apply(this);
            if (area.Width < Ui.D(900) || area.Height < Ui.D(600)) WindowState = FormWindowState.Maximized;

            BuildLayout();
            timer.Tick += (s, e) => Refresh2();
            FormClosed += (s, e) => Environment.Exit(0);
            StartScan();
        }

        void BuildLayout()
        {
            band.Height = Ui.D(108);
            verdict.Font = Ui.H1;
            subline.Font = Ui.Body;
            stop = Ui.Btn("Terminar ya", false, (s, e) => { if (engine != null) engine.StopNow("Se detuvo antes de terminar, a petición del usuario."); });
            save = Ui.Btn("Guardar informe", false, (s, e) => SaveReport());
            again = Ui.Btn("Repetir revisión", false, (s, e) => StartScan());
            actions.Controls.AddRange(new Control[] { stop, again, save });
            band.Controls.Add(verdict);
            band.Controls.Add(subline);
            band.Controls.Add(actions);
            band.Resize += (s, e) => LayoutBand();

            nav.ItemHeight = Ui.D(48);
            nav.Width = Ui.D(262);
            nav.DrawItem += DrawNav;
            nav.SelectedIndexChanged += (s, e) => { view.ResetScroll(); ShowSelected(); };

            heading.Font = Ui.H2;
            heading.Height = Ui.D(44);
            heading.Padding = new Padding(Ui.D(16), 0, 0, 0);
            tools.Padding = new Padding(Ui.D(12), 0, Ui.D(12), Ui.D(4));
            techToggle.Margin = new Padding(Ui.D(4), Ui.D(9), Ui.D(4), 0);
            techToggle.ForeColor = Ui.Ink;
            techToggle.CheckedChanged += (s, e) => ShowSelected();

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(view);
            right.Controls.Add(credits);
            right.Controls.Add(tools);
            right.Controls.Add(heading);

            Controls.Add(right);
            Controls.Add(nav);
            if (!ctx.IsAdmin) Controls.Add(BuildPermissionBar());
            Controls.Add(band);
        }

        Control BuildPermissionBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(0xFB, 0xEE, 0xD5), Padding = new Padding(Ui.D(12), Ui.D(2), Ui.D(12), Ui.D(2)) };
            var text = new Label
            {
                Text = "Sin permisos de administrador, algunos datos no se pueden leer (temperatura, salud de discos).",
                AutoSize = true, ForeColor = Ui.Ink, Margin = new Padding(Ui.D(4), Ui.D(9), Ui.D(4), Ui.D(4))
            };
            var b = Ui.Btn("Reabrir como administrador", false, (s, e) =>
            {
                if (Program.Relaunch("--no-elevar")) Environment.Exit(0);
                else MessageBox.Show(this, "No se concedió el permiso. Puedes seguir usando el programa con los datos disponibles.", Text);
            });
            bar.Controls.Add(text);
            bar.Controls.Add(b);
            return bar;
        }

        void LayoutBand()
        {
            int pad = Ui.D(20);
            actions.Location = new Point(Math.Max(pad, band.ClientSize.Width - actions.Width - Ui.D(12)), Ui.D(14));
            int w = Math.Max(Ui.D(200), actions.Left - pad - Ui.D(8));
            verdict.SetBounds(pad, Ui.D(12), w, Ui.D(48));
            // En ventanas estrechas el veredicto reduce su tamaño para no perder palabras.
            var fit = TextRenderer.MeasureText(verdict.Text ?? "", Ui.H1).Width <= w ? Ui.H1 : Ui.H2;
            if (verdict.Font != fit) verdict.Font = fit;
            subline.SetBounds(pad, Ui.D(62), band.ClientSize.Width - pad * 2, Ui.D(40));
        }

        void StartScan()
        {
            engine = new Engine(ctx);
            nav.Items.Clear();
            nav.Items.Add(SummaryKey);
            foreach (var s in engine.Sections) nav.Items.Add(s);
            nav.Items.Add(CreditsKey);
            nav.SelectedIndex = 0;
            wasDone = false;
            seenVersion = -1;
            stop.Visible = true;
            engine.Start();
            timer.Start();
            Refresh2();
            LayoutBand();
        }

        void Refresh2()
        {
            if (engine == null) return;
            bool done = engine.Done;
            if (ctx.Version == seenVersion && done == wasDone) return;
            seenVersion = ctx.Version;
            wasDone = done;
            if (done) stop.Visible = false;

            string detail; St overall;
            string text = Fmt.Verdict(engine.Sections, out detail, out overall);
            if (!done)
            {
                int left = engine.Sections.Count(s => s.Running);
                verdict.Text = "Revisando el equipo…";
                subline.Text = "Faltan " + left + " de " + engine.Sections.Count + " partes. Puedes usar la ventana mientras tanto o pulsar «Terminar ya».";
                band.BackColor = Ui.Slate;
            }
            else
            {
                verdict.Text = text;
                subline.Text = detail;
                band.BackColor = Ui.Of(overall);
            }
            nav.Invalidate();
            ShowSelected();
            LayoutBand();
        }

        void DrawNav(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= nav.Items.Count) return;
            var g = e.Graphics;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Ui.Page : Color.White)) g.FillRectangle(b, e.Bounds);
            var section = nav.Items[e.Index] as Section;
            string title; string sub; St st; bool busy = false;
            if (CreditsKey.Equals(nav.Items[e.Index]))
            {
                title = "Créditos";
                sub = "Autor y sobre el programa";
                st = St.Info;
            }
            else if (section == null)
            {
                title = "Resumen";
                var pend = Attention().Count;
                sub = pend == 0 ? "Sin puntos pendientes" : pend + (pend == 1 ? " punto pendiente" : " puntos pendientes");
                string d; Fmt.Verdict(engine.Sections, out d, out st);
                busy = !engine.Done;
            }
            else
            {
                title = section.Title;
                st = section.Worst();
                busy = section.Running;
                sub = busy ? "Revisando…" : Fmt.StatusWord(st);
            }
            int size = Ui.D(22), x = e.Bounds.X + Ui.D(14), y = e.Bounds.Y + (e.Bounds.Height - size) / 2;
            if (busy)
                using (var p = new Pen(Ui.Muted, Ui.D(2))) { g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.DrawEllipse(p, x + 1, y + 1, size - 2, size - 2); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None; }
            else
                Ui.Badge(g, st, x, y, size);
            int tx = x + size + Ui.D(10), w = e.Bounds.Right - tx - Ui.D(6);
            TextRenderer.DrawText(g, title, sel ? Ui.Bold : Ui.Body, new Rectangle(tx, e.Bounds.Y + Ui.D(6), w, Ui.D(21)), Ui.Ink, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, sub, Ui.Small, new Rectangle(tx, e.Bounds.Y + Ui.D(26), w, Ui.D(18)), busy ? Ui.Muted : Ui.Of(st), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (sel)
                using (var b = new SolidBrush(Ui.Slate)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y, Ui.D(4), e.Bounds.Height);
        }

        List<Finding> Attention()
        {
            return engine.Sections.SelectMany(s => s.All()).Where(f => f.Status == St.Bad || f.Status == St.Warn || f.Status == St.Unknown)
                .OrderByDescending(f => (int)f.Status).ToList();
        }

        string shownKey;

        void ShowSelected()
        {
            var section = nav.SelectedItem as Section;
            bool showCredits = CreditsKey.Equals(nav.SelectedItem);
            string key = showCredits ? CreditsKey : section == null ? SummaryKey : section.Key;
            if (key != shownKey)
            {
                shownKey = key;
                BuildTools(section);
            }
            credits.Visible = showCredits;
            view.Visible = !showCredits;
            tools.Visible = !showCredits;
            if (showCredits)
                heading.Text = "Créditos";
            else if (section == null)
            {
                heading.Text = "Resumen";
                view.SetData(Attention(), techToggle.Checked, true,
                    engine.Done ? "No hay nada pendiente. Las secciones de la izquierda muestran el detalle de cada componente, y las pruebas manuales confirman lo que Windows no puede ver."
                                : "Aquí aparecerá lo que requiera atención a medida que avance la revisión.");
            }
            else
            {
                heading.Text = section.Title;
                var list = section.All().OrderByDescending(f => f.Status == St.Bad ? 3 : f.Status == St.Warn ? 2 : f.Status == St.Unknown ? 1 : 0).ToList();
                view.SetData(list, techToggle.Checked, false, section.Running ? "Revisando esta parte…" : "Sin datos.");
            }
        }

        void BuildTools(Section s)
        {
            tools.SuspendLayout();
            tools.Controls.Clear();
            if (s != null)
                foreach (var t in TestCatalog.For(s.Key))
                {
                    var test = t;
                    tools.Controls.Add(Ui.Btn(test.Label, true, (o, e) => { test.Run(this, ctx); ShowSelected(); }));
                }
            tools.Controls.Add(techToggle);
            tools.ResumeLayout();
        }

        void SaveReport()
        {
            string html;
            try { html = Report.Html(engine.Sections, ctx); }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo crear el informe: " + ex.Message, Text); return; }
            string name = "Diagnostico_" + Environment.MachineName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + ".html";
            string path = null;
            using (var d = new SaveFileDialog { FileName = name, Filter = "Página web (*.html)|*.html", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                path = d.FileName;
            }
            try { File.WriteAllText(path, html, new UTF8Encoding(true)); }
            catch (Exception)
            {
                // Sin permiso en esa carpeta: se guarda en una que siempre se puede escribir.
                try
                {
                    path = Path.Combine(Path.GetTempPath(), name);
                    File.WriteAllText(path, html, new UTF8Encoding(true));
                    MessageBox.Show(this, "No se pudo guardar en esa carpeta. El informe quedó en:\n" + path, Text);
                }
                catch (Exception ex2) { MessageBox.Show(this, "No se pudo guardar el informe: " + ex2.Message, Text); return; }
            }
            try { Process.Start(path); } catch { MessageBox.Show(this, "Informe guardado en:\n" + path, Text); }
        }
    }
}
