using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using FormsTimer = System.Windows.Forms.Timer;

namespace LimpiezaPC
{
    public static class Ui
    {
        public static readonly Color Ink = Color.FromArgb(0x1E, 0x2A, 0x32), Muted = Color.FromArgb(0x54, 0x62, 0x6D), Slate = Color.FromArgb(0x2B, 0x4A, 0x5E),
            Red = Color.FromArgb(0xC6, 0x28, 0x28), Amber = Color.FromArgb(0xA8, 0x62, 0x00);
        public static Font Body = new Font("Segoe UI", 10f), H1 = new Font("Segoe UI Semibold", 18f), Small = new Font("Segoe UI", 9f), Mono = new Font("Consolas", 9.5f);
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
    }

    public class MainForm : Form
    {
        readonly Panel band = new Panel { Dock = DockStyle.Top };
        readonly Label title = new Label { AutoEllipsis = true, ForeColor = Color.White, Font = Ui.H1, BackColor = Color.Transparent, UseMnemonic = false };
        readonly Label subtitle = new Label { AutoEllipsis = true, ForeColor = Color.White, Font = Ui.Body, BackColor = Color.Transparent, UseMnemonic = false };
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly FormsTimer timer = new FormsTimer { Interval = 300 };
        EventHandler tickHandler;
        readonly bool admin = Env.Admin();
        ScanResult result;
        string defenderText = "";
        volatile bool scanRunning, cancelScan;

        public MainForm()
        {
            using (var g = CreateGraphics()) Ui.Scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Limpieza de virus y arranque";
            Font = Ui.Body;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(Ui.D(760), area.Width), Math.Min(Ui.D(520), area.Height));
            Size = new Size(Math.Min(Ui.D(1040), area.Width), Math.Min(Ui.D(740), area.Height));
            StartPosition = FormStartPosition.CenterScreen;
            Comun.WindowFit.Apply(this);
            band.Height = Ui.D(84);
            band.BackColor = Ui.Slate;
            band.Controls.Add(title);
            band.Controls.Add(subtitle);
            band.Resize += (o, e) =>
            {
                int pad = Ui.D(20), w = band.ClientSize.Width - pad * 2;
                title.SetBounds(pad, Ui.D(9), w, Ui.D(42));
                subtitle.SetBounds(pad, Ui.D(52), w, Ui.D(26));
            };
            Controls.Add(content);
            if (!admin) Controls.Add(PermissionBar());
            Controls.Add(band);
            FormClosing += (o, e) =>
            {
                if (scanRunning && MessageBox.Show(this, "Hay un análisis en curso. ¿Cerrar y cancelarlo?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) e.Cancel = true;
                else cancelScan = true;
            };
            FormClosed += (o, e) => Environment.Exit(0);
            RunScan();
        }

        Control PermissionBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(0xFB, 0xEE, 0xD5), Padding = new Padding(Ui.D(12), Ui.D(2), Ui.D(12), Ui.D(2)) };
            bar.Controls.Add(new Label { Text = "Sin administrador la revisión es parcial y no se pueden desactivar servicios.", AutoSize = true, ForeColor = Ui.Ink, Margin = new Padding(Ui.D(4), Ui.D(9), Ui.D(4), Ui.D(4)) });
            bar.Controls.Add(Ui.Btn("Reabrir como administrador", false, (s, e) =>
            {
                if (Program.Relaunch("--no-elevar")) Environment.Exit(0);
                else MessageBox.Show(this, "No se concedió el permiso. Puedes seguir con la revisión parcial.", Text);
            }));
            return bar;
        }

        void Header(string t, string s, Color? c = null) { title.Text = t; subtitle.Text = s; band.BackColor = c ?? Ui.Slate; }

        void Page(Control c)
        {
            if (tickHandler != null) { timer.Tick -= tickHandler; tickHandler = null; }
            timer.Stop();
            content.SuspendLayout();
            foreach (Control old in content.Controls.Cast<Control>().ToList()) { content.Controls.Remove(old); old.Dispose(); }
            c.Dock = DockStyle.Fill;
            content.Controls.Add(c);
            content.ResumeLayout();
        }

        void Tick(EventHandler h) { tickHandler = h; timer.Tick += h; timer.Start(); }

        void Warn(string text) { MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }

        void RunScan()
        {
            Header("Revisando lo que arranca con Windows…", "Esto no cambia nada. Suele tardar menos de dos minutos.");
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Ui.D(24)) };
            var now = new Label { AutoSize = true, ForeColor = Ui.Ink, Text = "Empezando…" };
            flow.Controls.Add(now);
            Page(flow);
            string status = null;
            ScanResult got = null;
            string error = null;
            var th = new Thread(() =>
            {
                try { got = Scanner.Run(s => status = s); }
                catch (Exception ex) { error = ex.Message; Program.LogCrash(ex); }
            }) { IsBackground = true };
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
            Tick((s, e) =>
            {
                if (status != null) now.Text = "Revisando: " + status;
                if (th.IsAlive) return;
                result = got ?? new ScanResult();
                if (error != null) result.Gaps.Add("La revisión no terminó: " + error);
                ShowResults();
            });
        }

        void ShowResults()
        {
            string detail, color;
            string verdict = Report.Verdict(result, out detail, out color);
            Header(verdict, detail, ColorTranslator.FromHtml(color));

            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Padding = new Padding(Ui.D(12), Ui.D(6), Ui.D(12), Ui.D(4)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var showAll = new CheckBox { Text = "Mostrar también lo normal", AutoSize = true, Margin = new Padding(Ui.D(4), Ui.D(8), Ui.D(12), Ui.D(4)) };
            top.Controls.Add(showAll);
            top.Controls.Add(new Label { Text = "«Sospechoso» es una señal, no una prueba: confirma antes de desactivar. Esto no sustituye a un antivirus.", AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(Ui.D(4), Ui.D(9), Ui.D(4), Ui.D(4)) });
            root.Controls.Add(top, 0, 0);

            var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle, MultiSelect = false, HideSelection = false };
            view.Columns.Add("Estado", Ui.D(105));
            view.Columns.Add("Elemento", Ui.D(300));
            view.Columns.Add("Dónde", Ui.D(190));
            view.Columns.Add("Motivo", Ui.D(420));
            root.Controls.Add(view, 0, 1);

            var info = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, Font = Ui.Body };
            root.Controls.Add(info, 0, 2);

            Button disable = null, locate = null, vt = null;
            Action fill = () =>
            {
                view.BeginUpdate();
                view.Items.Clear();
                foreach (var i in result.Items.OrderByDescending(x => x.Severity).ThenBy(x => x.Group).ThenBy(x => x.Name))
                {
                    if (i.Severity == Sev.Normal && !showAll.Checked) continue;
                    var it = new ListViewItem(Report.Word(i.Severity) + (i.Disabled ? " (apagado)" : "")) { Tag = i };
                    it.SubItems.Add(i.Name);
                    it.SubItems.Add(i.Group);
                    it.SubItems.Add(i.Reasons.Count > 0 ? i.Reasons[0] : "");
                    it.ForeColor = i.Disabled ? Ui.Muted : i.Severity == Sev.Suspicious ? Ui.Red : i.Severity == Sev.Review ? Ui.Amber : Ui.Ink;
                    view.Items.Add(it);
                }
                view.EndUpdate();
                if (view.Items.Count == 0)
                    info.Text = "No hay nada que mostrar en esta lista." + (result.Gaps.Count > 0 ? "\r\n\r\nNo se pudo revisar del todo:\r\n" + string.Join("\r\n", result.Gaps) : "") + "\r\n\r\nEsto no garantiza que el equipo esté limpio: analízalo también con el antivirus.";
                else if (result.Gaps.Count > 0)
                    info.Text = "Selecciona un elemento para ver su detalle.\r\n\r\nNo se pudo revisar del todo:\r\n" + string.Join("\r\n", result.Gaps);
                else info.Text = "Selecciona un elemento para ver su detalle.";
            };
            showAll.CheckedChanged += (o, e) => fill();

            Func<Item> selected = () => view.SelectedItems.Count == 1 ? (Item)view.SelectedItems[0].Tag : null;

            view.SelectedIndexChanged += (o, e) =>
            {
                var i = selected();
                if (disable != null)
                {
                    disable.Enabled = i != null && i.CanDisable && !i.Disabled;
                    bool hasFile = false;
                    if (i != null) try { hasFile = !string.IsNullOrEmpty(i.Path) && (File.Exists(i.Path) || Directory.Exists(i.Path)); } catch { }
                    locate.Enabled = hasFile;
                    vt.Enabled = i != null && hasFile && File.Exists(i.Path);
                }
                if (i == null) return;
                var sb = new StringBuilder();
                sb.AppendLine(i.Name + "  (" + i.Location + ")");
                foreach (var r in i.Reasons) sb.AppendLine("• " + r);
                if (i.Command.Length > 0) sb.AppendLine("Comando: " + i.Command);
                else if (i.RegValue.Length > 0 && i.Kind != ItemKind.Startup) sb.AppendLine("Valor: " + i.RegValue.Replace("\n", "\r\n"));
                if (!string.IsNullOrEmpty(i.Path)) sb.AppendLine("Archivo: " + i.Path);
                if (i.SigStatus.Length > 0) sb.AppendLine("Firma: " + (i.SigStatus == "Valid" ? "válida" : i.SigStatus == "NotSigned" ? "sin firma" : i.SigStatus) + (i.Signer.Length > 0 ? ", " + i.Signer : ""));
                if (i.Advice.Length > 0) sb.AppendLine("Qué hacer: " + i.Advice);
                info.Text = sb.ToString();
            };

            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            disable = Ui.Btn("Desactivar", true, (o, e) =>
            {
                var i = selected();
                if (i == null) return;
                string warn = i.Signer.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "Este elemento está firmado por Microsoft. Desactivarlo puede hacer fallar Windows.\n\n"
                    : i.Severity == Sev.Normal ? "No se detectó nada raro en este elemento.\n\n" : "";
                if (MessageBox.Show(this, warn + "¿Desactivar «" + i.Name + "»?\n\nNo se borra nada y puedes deshacerlo con «Deshacer cambios».", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                try
                {
                    string msg = Actions.Disable(i);
                    i.Disabled = true;
                    fill();
                    string d2, c2;
                    Header(Report.Verdict(result, out d2, out c2), d2, ColorTranslator.FromHtml(c2));
                    info.Text = msg;
                }
                catch (UnauthorizedAccessException) { Warn("Hace falta abrir el programa como administrador para cambiar este elemento."); }
                catch (Exception ex) { Warn("No se pudo desactivar: " + ex.Message); }
            });
            disable.Enabled = false;
            locate = Ui.Btn("Abrir ubicación", false, (o, e) =>
            {
                var i = selected();
                if (i == null || string.IsNullOrEmpty(i.Path)) return;
                try { Process.Start("explorer.exe", Directory.Exists(i.Path) ? "\"" + i.Path + "\"" : "/select,\"" + i.Path + "\""); }
                catch (Exception ex) { Warn("No se pudo abrir: " + ex.Message); }
            });
            locate.Enabled = false;
            vt = Ui.Btn("Buscar en VirusTotal", false, (o, e) =>
            {
                var i = selected();
                if (i == null) return;
                if (MessageBox.Show(this, "Se abrirá VirusTotal en el navegador con la huella SHA-256 del archivo.\n\nEl archivo no se sube, pero la huella sí llega a ese sitio.\n\n¿Continuar?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { Process.Start("https://www.virustotal.com/gui/file/" + Tools.Sha256(i.Path)); }
                catch (Exception ex) { Warn("No se pudo abrir: " + ex.Message); }
            });
            vt.Enabled = false;
            bar.Controls.Add(disable);
            bar.Controls.Add(locate);
            bar.Controls.Add(vt);
            bar.Controls.Add(Ui.Btn("Deshacer cambios", false, (o, e) => ShowUndo()));
            bar.Controls.Add(Ui.Btn("Analizar con Defender", false, (o, e) => ShowDefender()));
            bar.Controls.Add(Ui.Btn("Revisar de nuevo", false, (o, e) => RunScan()));
            bar.Controls.Add(Ui.Btn("Guardar informe", false, (o, e) => SaveReport()));
            bar.Controls.Add(Ui.Btn("Créditos", false, (o, e) => ShowCredits()));
            root.Controls.Add(bar, 0, 3);
            Page(root);
            fill();
        }

        void SaveReport()
        {
            using (var d = new SaveFileDialog { FileName = "Revision_inicio_" + Environment.MachineName + "_" + DateTime.Now.ToString("yyyy-MM-dd_HHmm") + ".html", Filter = "Página web (*.html)|*.html", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, Report.Page(result, defenderText, Program.Info), new UTF8Encoding(true));
                    Process.Start(d.FileName);
                }
                catch (Exception ex) { Warn("No se pudo guardar: " + ex.Message); }
            }
        }

        void ShowUndo()
        {
            Header("Deshacer cambios", "Lo que este programa desactivó en este equipo.");
            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Padding = new Padding(Ui.D(12), Ui.D(8), Ui.D(12), Ui.D(4)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle };
            view.Columns.Add("Elemento", Ui.D(520));
            view.Columns.Add("Tipo", Ui.D(160));
            view.Columns.Add("Fecha", Ui.D(180));
            Action fill = () =>
            {
                view.Items.Clear();
                foreach (var c in Actions.Load())
                {
                    var it = new ListViewItem(c.Name) { Tag = c };
                    it.SubItems.Add(c.Kind == "service" ? "Servicio" : c.Kind == "task" ? "Tarea" : "Inicio");
                    it.SubItems.Add(c.Date);
                    view.Items.Add(it);
                }
            };
            fill();
            root.Controls.Add(view, 0, 0);
            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            bar.Controls.Add(Ui.Btn("Volver y revisar de nuevo", false, (o, e) => RunScan()));
            bar.Controls.Add(Ui.Btn("Restaurar lo seleccionado", true, (o, e) =>
            {
                if (view.SelectedItems.Count != 1) { Warn("Selecciona un elemento de la lista."); return; }
                try { Actions.Undo((Change)view.SelectedItems[0].Tag); fill(); }
                catch (UnauthorizedAccessException) { Warn("Hace falta abrir el programa como administrador para restaurar este elemento."); }
                catch (Exception ex) { Warn("No se pudo restaurar: " + ex.Message); }
            }));
            root.Controls.Add(bar, 0, 1);
            Page(root);
        }

        void ShowDefender()
        {
            Header("Analizar con Windows Defender", "El rápido puede tardar desde minutos hasta más de media hora; el completo, horas.");
            var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(Ui.D(24)) };
            var msg = new Label { AutoSize = true, ForeColor = Ui.Ink, Text = defenderText.Length > 0 ? defenderText : "Puedes seguir usando el equipo durante el análisis.", MaximumSize = new Size(Ui.D(760), 0) };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            Button quick = null, full = null, back = null, stop = null;
            Action<bool> start = isFull =>
            {
                quick.Enabled = full.Enabled = back.Enabled = false;
                stop.Enabled = true;
                msg.Text = "Analizando… No se envía nada desde este programa.";
                cancelScan = false;
                scanRunning = true;
                string res = null;
                var th = new Thread(() =>
                {
                    bool threats;
                    try { res = Tools.DefenderScan(isFull, () => cancelScan, out threats); }
                    catch (Exception ex) { res = "No se pudo ejecutar el análisis: " + ex.Message; }
                }) { IsBackground = true };
                th.Start();
                Tick((o, e) =>
                {
                    if (th.IsAlive) return;
                    timer.Stop();
                    scanRunning = false;
                    defenderText = res;
                    msg.Text = res;
                    quick.Enabled = full.Enabled = back.Enabled = true;
                    stop.Enabled = false;
                });
            };
            quick = Ui.Btn("Análisis rápido", true, (o, e) => start(false));
            full = Ui.Btn("Análisis completo", false, (o, e) => start(true));
            stop = Ui.Btn("Cancelar", false, (o, e) => { cancelScan = true; msg.Text = "Cancelando…"; });
            stop.Enabled = false;
            back = Ui.Btn("Volver", false, (o, e) => ShowResults());
            row.Controls.AddRange(new Control[] { quick, full, stop, back });
            flow.Controls.Add(msg);
            flow.Controls.Add(row);
            Page(flow);
            if (Tools.FindMpCmdRun() == null)
            {
                msg.Text = "Este equipo no tiene Windows Defender disponible. Usa el antivirus que tenga instalado.";
                quick.Enabled = full.Enabled = false;
            }
        }

        void ShowCredits()
        {
            Header("Créditos", "Autor y sobre el programa.");
            var host = new Panel();
            var panel = new Comun.CreditsPanel(Program.Info, Ui.Ink, Ui.Muted) { Dock = DockStyle.Fill };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Ui.D(12), Ui.D(4), Ui.D(12), Ui.D(8)) };
            bar.Controls.Add(Ui.Btn("Volver", true, (s, e) => ShowResults()));
            host.Controls.Add(panel);
            host.Controls.Add(bar);
            Page(host);
        }
    }
}
