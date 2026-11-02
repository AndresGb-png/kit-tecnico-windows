using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FormsTimer = System.Windows.Forms.Timer;

namespace ReparadorPC
{
    public static class Ui
    {
        public static readonly Color Ink = Color.FromArgb(0x1E, 0x2A, 0x32), Muted = Color.FromArgb(0x54, 0x62, 0x6D),
            Page = Color.FromArgb(0xEE, 0xF1, 0xF3), Slate = Color.FromArgb(0x2B, 0x4A, 0x5E);
        public static Font Body = new Font("Segoe UI", 10f), H1 = new Font("Segoe UI Semibold", 18f), H2 = new Font("Segoe UI Semibold", 12f), Small = new Font("Segoe UI", 9f);
        public static float Scale = 1f;
        public static int D(int v) { return (int)Math.Round(v * Scale); }

        public static Color Of(St s)
        {
            switch (s)
            {
                case St.Ok: return Color.FromArgb(0x1B, 0x7F, 0x4B);
                case St.Warn: return Color.FromArgb(0xA8, 0x62, 0x00);
                case St.Fail: return Color.FromArgb(0xC6, 0x28, 0x28);
                case St.Running: return Slate;
                default: return Muted;
            }
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

        public static Label Text(string text, Font font = null, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Ink, Margin = new Padding(D(4), D(7), D(4), D(2)), UseMnemonic = false };
        }
    }

    public class MainForm : Form
    {
        readonly Panel band = new Panel { Dock = DockStyle.Top };
        readonly Label title = new Label { AutoEllipsis = true, ForeColor = Color.White, Font = Ui.H1, BackColor = Color.Transparent };
        readonly Label subtitle = new Label { AutoEllipsis = true, ForeColor = Color.White, Font = Ui.Body, BackColor = Color.Transparent };
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly FormsTimer timer = new FormsTimer { Interval = 400 };
        EventHandler timerHandler;
        bool busy;
        readonly bool admin = Env.Admin();
        readonly List<Repair> repairs = Repairs.All();
        List<Check> before;

        public MainForm()
        {
            using (var g = CreateGraphics()) Ui.Scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Reparador de Windows";
            Font = Ui.Body;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(Ui.D(720), area.Width), Math.Min(Ui.D(520), area.Height));
            Size = new Size(Math.Min(Ui.D(1000), area.Width), Math.Min(Ui.D(780), area.Height));
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
                if (busy && MessageBox.Show(this, "Hay una reparación en curso. Es mejor dejarla terminar.\n\n¿Cerrar de todos modos?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    e.Cancel = true;
            };
            FormClosed += (s, e) => Environment.Exit(0);
            RunDiagnosis();
        }

        Control PermissionBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(0xFB, 0xEE, 0xD5), Padding = new Padding(Ui.D(12), Ui.D(2), Ui.D(12), Ui.D(2)) };
            bar.Controls.Add(new Label { Text = "Sin administrador casi ninguna reparación puede ejecutarse.", AutoSize = true, ForeColor = Ui.Ink, Margin = new Padding(Ui.D(4), Ui.D(9), Ui.D(4), Ui.D(4)) });
            bar.Controls.Add(Ui.Btn("Reabrir como administrador", false, (s, e) =>
            {
                if (busy) { MessageBox.Show(this, "Espera a que termine.", Text); return; }
                if (Program.Relaunch("--no-elevar")) Environment.Exit(0);
                else MessageBox.Show(this, "No se concedió el permiso.", Text);
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

        // ---------- diagnóstico ----------

        void RunDiagnosis()
        {
            Header("Revisando el equipo…", "Esto no cambia nada. Tarda menos de un minuto.");
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Ui.D(24), Ui.D(24), Ui.D(24), Ui.D(10)) };
            var now = Ui.Text("Empezando…");
            flow.Controls.Add(now);
            flow.Controls.Add(Ui.Btn("Omitir la revisión", false, (s, e) => { before = new List<Check>(); ShowSetup(); }));
            Page(flow);

            string status = null;
            List<Check> result = null;
            var thread = new Thread(() =>
            {
                try { result = Diagnosis.Run(x => status = x, admin); }
                catch (Exception ex) { Program.LogCrash(ex); result = new List<Check>(); }
            }) { IsBackground = true };
            thread.Start();
            Tick((s, e) =>
            {
                if (status != null) now.Text = "Revisando: " + status;
                if (thread.IsAlive) return;
                before = result;
                ShowSetup();
            });
        }

        void ShowSetup()
        {
            int issues = before == null ? 0 : before.Count(c => c.Result == St.Warn || c.Result == St.Fail);
            Header(issues == 0 ? "No se detectaron problemas" : issues == 1 ? "Se detectó un punto por revisar" : "Se detectaron " + issues + " puntos por revisar",
                "Marca qué le pasa al equipo y elige qué reparar.", issues == 0 ? ColorTranslator.FromHtml("#1b7f4b") : ColorTranslator.FromHtml("#a86200"));

            foreach (var key in (before ?? new List<Check>()).SelectMany(c => c.Suggest))
            {
                var r = repairs.FirstOrDefault(x => x.Key == key);
                if (r != null) r.Selected = true;
            }

            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 5, Padding = new Padding(Ui.D(12), Ui.D(6), Ui.D(12), Ui.D(4)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle };
            view.Columns.Add("Revisión", Ui.D(180));
            view.Columns.Add("Estado", Ui.D(100));
            view.Columns.Add("Detalle", Ui.D(640));
            foreach (var c in before ?? new List<Check>())
            {
                var it = new ListViewItem(c.Label);
                it.SubItems.Add(Report.Word(c.Result));
                it.SubItems.Add(c.Detail);
                it.ForeColor = c.Result == St.Warn || c.Result == St.Fail ? Ui.Of(c.Result) : Ui.Ink;
                view.Items.Add(it);
            }
            root.Controls.Add(view, 0, 0);

            root.Controls.Add(Ui.Text("¿Qué le pasa al equipo? Marca lo que corresponda:", Ui.H2), 0, 1);
            var symptoms = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            TreeView tree = null;
            foreach (var pair in Repairs.Symptoms)
            {
                var keys = pair.Value;
                var box = new CheckBox { Text = pair.Key, AutoSize = true, Margin = new Padding(Ui.D(4), Ui.D(2), Ui.D(10), Ui.D(2)) };
                box.CheckedChanged += (s, e) =>
                {
                    if (!box.Checked) return;
                    foreach (var r in repairs.Where(x => keys.Contains(x.Key))) r.Selected = true;
                    FillTree(tree);
                };
                symptoms.Controls.Add(box);
            }
            root.Controls.Add(symptoms, 0, 2);

            tree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, BorderStyle = BorderStyle.FixedSingle, ShowLines = false, HideSelection = true, ItemHeight = Ui.D(24), ShowNodeToolTips = true };
            tree.AfterCheck += (s, e) =>
            {
                if (e.Action == TreeViewAction.Unknown) return;
                var r = e.Node.Tag as Repair;
                if (r != null) r.Selected = e.Node.Checked;
                foreach (TreeNode child in e.Node.Nodes)
                {
                    child.Checked = e.Node.Checked;
                    var c = child.Tag as Repair;
                    if (c != null) c.Selected = child.Checked;
                }
            };
            FillTree(tree);
            root.Controls.Add(tree, 0, 3);

            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            bar.Controls.Add(Ui.Btn("Reparar lo marcado", true, (s, e) => Begin()));
            bar.Controls.Add(Ui.Btn("Revisar de nuevo", false, (s, e) => RunDiagnosis()));
            bar.Controls.Add(Ui.Btn("Créditos", false, (s, e) => ShowCredits()));
            root.Controls.Add(bar, 0, 4);
            Page(root);
        }

        void FillTree(TreeView tree)
        {
            tree.BeginUpdate();
            tree.Nodes.Clear();
            foreach (var g in repairs.GroupBy(r => r.Group))
            {
                var parent = tree.Nodes.Add(g.Key);
                parent.Checked = g.All(r => r.Selected);
                foreach (var r in g)
                {
                    bool blocked = r.NeedsAdmin && !admin;
                    var n = parent.Nodes.Add(r.Label + " (" + r.Duration + ")" + (blocked ? ". Requiere administrador" : ""));
                    n.Tag = r; n.Checked = r.Selected;
                    n.ToolTipText = r.Why + (r.Note != null ? " " + r.Note : "");
                    r.Node = n;
                }
                parent.Expand();
            }
            tree.EndUpdate();
        }

        // ---------- ejecución ----------

        void Begin()
        {
            var chosen = repairs.Where(r => r.Selected).ToList();
            if (chosen.Count == 0)
            {
                MessageBox.Show(this, "No hay nada marcado. Elige una reparación o un síntoma.", Text);
                return;
            }
            var ps = SystemInformation.PowerStatus;
            if (ps.PowerLineStatus == PowerLineStatus.Offline && ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery &&
                MessageBox.Show(this, "El equipo está funcionando con batería. Enchúfalo: cortar una reparación a medias es mala idea.\n\n¿Seguir?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            var risky = chosen.Where(r => r.Note != null).ToList();
            if (risky.Count > 0)
            {
                string text = string.Join("\n\n", risky.Select(r => r.Label + ":\n" + r.Note));
                if (MessageBox.Show(this, "Antes de seguir, ten en cuenta:\n\n" + text + "\n\n¿Seguir?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            RunRepairs(chosen);
        }

        void RunRepairs(List<Repair> chosen)
        {
            Header("Reparando…", "Algunas tardan varios minutos. Puedes seguir usando el equipo.");
            foreach (var r in chosen) { r.Result = St.None; r.Detail = ""; }
            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.D(12), Ui.D(10), Ui.D(12), Ui.D(6)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var now = new Label { AutoSize = false, Dock = DockStyle.Fill, Height = Ui.D(46), ForeColor = Ui.Ink, AutoEllipsis = true, UseMnemonic = false };
            layout.Controls.Add(now, 0, 0);
            var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle };
            view.Columns.Add("Reparación", Ui.D(380));
            view.Columns.Add("Estado", Ui.D(110));
            view.Columns.Add("Detalle", Ui.D(520));
            foreach (var r in chosen)
            {
                var it = new ListViewItem(r.Label) { Tag = r };
                it.SubItems.Add(Report.Word(r.Result));
                it.SubItems.Add("");
                view.Items.Add(it);
            }
            layout.Controls.Add(view, 0, 1);

            var ctx = new Ctx();
            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var stop = Ui.Btn("Detener", false, null);
            stop.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Se detendrá la reparación en curso y no se harán las siguientes. Windows queda como antes de esa reparación.\n\n¿Detener?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                ctx.Stop(); stop.Enabled = false; stop.Text = "Deteniendo…";
            };
            bar.Controls.Add(stop);
            layout.Controls.Add(bar, 0, 2);
            Page(layout);

            busy = true;
            var thread = new Thread(() =>
            {
                try { Repairs.RunAll(chosen, ctx, admin); }
                catch (Exception ex) { Program.LogCrash(ex); }
            }) { IsBackground = true };
            thread.Start();

            Tick((s, e) =>
            {
                foreach (ListViewItem it in view.Items)
                {
                    var r = (Repair)it.Tag;
                    string word = Report.Word(r.Result);
                    if (it.SubItems[1].Text != word || it.SubItems[2].Text != r.Detail)
                    {
                        it.SubItems[1].Text = word;
                        it.SubItems[2].Text = r.Detail;
                        it.ForeColor = r.Result == St.Warn || r.Result == St.Fail || r.Result == St.Running ? Ui.Of(r.Result) : Ui.Ink;
                    }
                }
                var cur = ctx.Current;
                now.Text = cur != null ? cur.Label + " (" + cur.Duration + ")\n" + ctx.Line : "Terminando…";
                if (thread.IsAlive) return;
                busy = false;
                ShowAfter(chosen);
            });
        }

        // Tras reparar se repite la revisión, para el antes y después del informe.
        void ShowAfter(List<Repair> chosen)
        {
            Header("Comprobando el resultado…", "Repitiendo la revisión para compararla con la de antes.");
            var flow = new FlowLayoutPanel { Padding = new Padding(Ui.D(24)) };
            flow.Controls.Add(Ui.Text("Un momento…"));
            Page(flow);
            List<Check> after = null;
            var thread = new Thread(() =>
            {
                try { after = Diagnosis.Run(null, admin); }
                catch (Exception ex) { Program.LogCrash(ex); }
            }) { IsBackground = true };
            thread.Start();
            Tick((s, e) =>
            {
                if (thread.IsAlive) return;
                Finish(chosen, after);
            });
        }

        void Finish(List<Repair> chosen, List<Check> after)
        {
            string path = null;
            try { path = Report.Save(Report.Page(chosen, before, after, Program.Info)); }
            catch (Exception ex) { Program.LogCrash(ex); }

            string detail, color;
            string verdict = Report.Verdict(chosen, out detail, out color);
            Header(verdict, detail, ColorTranslator.FromHtml(color));
            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.D(14), Ui.D(10), Ui.D(14), Ui.D(6)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(Ui.Text("Resultado", Ui.H2), 0, 0);
            var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle, ShowItemToolTips = true };
            view.Columns.Add("Reparación", Ui.D(340));
            view.Columns.Add("Estado", Ui.D(100));
            view.Columns.Add("Detalle", Ui.D(760));
            foreach (var r in chosen.OrderBy(x => x.Result == St.Fail ? 0 : x.Result == St.Warn ? 1 : x.Result == St.Skipped ? 2 : 3))
            {
                var it = new ListViewItem(r.Label) { ToolTipText = r.Detail };
                it.SubItems.Add(Report.Word(r.Result));
                it.SubItems.Add(r.Detail);
                it.ForeColor = r.Result == St.Warn || r.Result == St.Fail ? Ui.Of(r.Result) : Ui.Ink;
                view.Items.Add(it);
            }
            layout.Controls.Add(view, 0, 1);
            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            bar.Controls.Add(Ui.Btn("Volver", false, (s, e) => RunDiagnosis()));
            if (path != null) bar.Controls.Add(Ui.Btn("Abrir informe", true, (s, e) =>
            {
                try { Process.Start(path); }
                catch (Exception ex) { MessageBox.Show(this, "No se pudo abrir: " + ex.Message + "\n\n" + path, Text); }
            }));
            if (chosen.Any(r => r.NeedsReboot && r.Result == St.Ok) || Env.RebootPending())
                bar.Controls.Add(Ui.Btn("Reiniciar ahora", false, (s, e) =>
                {
                    if (MessageBox.Show(this, "El equipo se reiniciará en 10 segundos. Guarda lo que tengas abierto.\n\n¿Reiniciar?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    try { Process.Start(new ProcessStartInfo("shutdown", "/r /t 10") { UseShellExecute = false, CreateNoWindow = true }); }
                    catch (Exception ex) { MessageBox.Show(this, "No se pudo reiniciar: " + ex.Message, Text); }
                }));
            layout.Controls.Add(bar, 0, 2);
            Page(layout);
        }

        void ShowCredits()
        {
            Header("Créditos", "Autor y sobre el programa.");
            var host = new Panel();
            var panel = new Comun.CreditsPanel(Program.Info, Ui.Ink, Ui.Muted) { Dock = DockStyle.Fill };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Ui.D(12), Ui.D(4), Ui.D(12), Ui.D(8)) };
            bar.Controls.Add(Ui.Btn("Volver", true, (s, e) => ShowSetup()));
            host.Controls.Add(panel);
            host.Controls.Add(bar);
            Page(host);
        }
    }
}
