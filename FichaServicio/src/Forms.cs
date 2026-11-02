using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FichaServicio
{
    public static class Ui
    {
        public static readonly Color Ink = Color.FromArgb(0x1E, 0x2A, 0x32), Muted = Color.FromArgb(0x54, 0x62, 0x6D),
            Page = Color.FromArgb(0xEE, 0xF1, 0xF3), Slate = Color.FromArgb(0x2B, 0x4A, 0x5E);
        public static Font Body = new Font("Segoe UI", 10f), H1 = new Font("Segoe UI Semibold", 18f), H2 = new Font("Segoe UI Semibold", 12f), Small = new Font("Segoe UI", 9f);
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
            return new Label { Text = text, AutoSize = true, Font = font ?? Body, ForeColor = color ?? Ink, Margin = new Padding(D(2), D(7), D(4), D(1)), UseMnemonic = false };
        }
    }

    // Dos columnas de etiqueta y campo; se reparte con el ancho de la ventana.
    public class FieldGrid : TableLayoutPanel
    {
        public FieldGrid()
        {
            ColumnCount = 2;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Dock = DockStyle.Top;
        }

        Panel Cell(string label, Control input)
        {
            var host = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, Margin = new Padding(Ui.D(4), Ui.D(1), Ui.D(4), Ui.D(1)) };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            host.Controls.Add(Ui.Text(label, Ui.Small, Ui.Muted), 0, 0);
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0);
            host.Controls.Add(input, 0, 1);
            return host;
        }

        int row;

        public T Add<T>(string leftLabel, T left, string rightLabel = null, Control right = null) where T : Control
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(Cell(leftLabel, left), 0, row);
            if (right != null) Controls.Add(Cell(rightLabel, right), 1, row);
            row++;
            return left;
        }

        public T Wide<T>(string label, T input) where T : Control
        {
            RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var cell = Cell(label, input);
            Controls.Add(cell, 0, row);
            SetColumnSpan(cell, 2);
            row++;
            return input;
        }
    }

    public class MainForm : Form
    {
        readonly Panel band = new Panel { Dock = DockStyle.Top };
        readonly Label title = new Label { AutoEllipsis = true, ForeColor = Color.White, Font = Ui.H1, BackColor = Color.Transparent, UseMnemonic = false };
        readonly Label subtitle = new Label { AutoEllipsis = true, ForeColor = Color.White, Font = Ui.Body, BackColor = Color.Transparent, UseMnemonic = false };
        readonly Panel content = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        readonly Store store;
        Func<bool> leaveCheck;

        public MainForm(Store s)
        {
            store = s;
            using (var g = CreateGraphics()) Ui.Scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Ficha de servicio";
            Font = Ui.Body;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(Ui.D(760), area.Width), Math.Min(Ui.D(520), area.Height));
            Size = new Size(Math.Min(Ui.D(1040), area.Width), Math.Min(Ui.D(760), area.Height));
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
            Controls.Add(band);
            FormClosing += (o, e) => { if (leaveCheck != null && !leaveCheck()) e.Cancel = true; };
            FormClosed += (o, e) => Environment.Exit(0);
            ShowList();
        }

        void Header(string t, string s) { title.Text = t; subtitle.Text = s; }

        void Page(Control c)
        {
            content.SuspendLayout();
            foreach (Control old in content.Controls.Cast<Control>().ToList()) { content.Controls.Remove(old); old.Dispose(); }
            c.Dock = DockStyle.Fill;
            content.Controls.Add(c);
            content.ResumeLayout();
        }

        void Warn(string text) { MessageBox.Show(this, text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }

        void Open(string path)
        {
            if (Environment.GetEnvironmentVariable("FICHAS_SIN_ABRIR") == "1") return;
            try { Process.Start(path); }
            catch (Exception ex) { Warn("No se pudo abrir: " + ex.Message + "\n\nEstá en:\n" + path); }
        }

        // ---------- lista ----------

        void ShowList()
        {
            leaveCheck = null;
            List<Ficha> all;
            try { all = store.LoadAll(); }
            catch (Exception ex) { all = new List<Ficha>(); Warn("No se pudieron leer las fichas: " + ex.Message); }
            Header("Ficha de servicio", all.Count == 0 ? "Crea la primera ficha cuando recibas un equipo." : all.Count + (all.Count == 1 ? " ficha guardada." : " fichas guardadas."));

            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 4, Padding = new Padding(Ui.D(12), Ui.D(8), Ui.D(12), Ui.D(4)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            top.Controls.Add(Ui.Btn("Nueva ficha", true, (o, e) =>
            {
                try { ShowEdit(store.NewFicha(), true); }
                catch (Exception ex) { Warn("No se pudo crear la ficha: " + ex.Message); }
            }));
            top.Controls.Add(Ui.Text("Buscar"));
            var search = new TextBox { Width = Ui.D(280), Margin = new Padding(Ui.D(4), Ui.D(7), Ui.D(4), Ui.D(4)) };
            top.Controls.Add(search);
            var filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.D(190), Margin = new Padding(Ui.D(4), Ui.D(7), Ui.D(4), Ui.D(4)) };
            filter.Items.Add("Todos los estados");
            filter.Items.Add("Sin entregar");
            filter.Items.AddRange(Statuses.All.Cast<object>().ToArray());
            filter.SelectedIndex = 0;
            top.Controls.Add(filter);
            root.Controls.Add(top, 0, 0);

            var note = Ui.Text(store.Unreadable.Count > 0 ? "No se pudieron leer: " + string.Join(", ", store.Unreadable) + ". Siguen en la carpeta de datos." : "Datos guardados en: " + store.Root, Ui.Small, store.Unreadable.Count > 0 ? Color.FromArgb(0xC6, 0x28, 0x28) : Ui.Muted);
            root.Controls.Add(note, 0, 1);

            var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle, MultiSelect = false };
            view.Columns.Add("Ficha", Ui.D(110));
            view.Columns.Add("Cliente", Ui.D(210));
            view.Columns.Add("Equipo", Ui.D(220));
            view.Columns.Add("Estado", Ui.D(140));
            view.Columns.Add("Recibido", Ui.D(95));
            view.Columns.Add("Garantía", Ui.D(170));
            root.Controls.Add(view, 0, 2);

            Action fill = () =>
            {
                string q = search.Text.Trim().ToLowerInvariant();
                string st = filter.SelectedItem as string;
                view.BeginUpdate();
                view.Items.Clear();
                foreach (var x in all)
                {
                    if (q.Length > 0 && !(x.Number + " " + x.ClientName + " " + x.ClientPhone + " " + x.ClientDocument + " " + x.DeviceBrandModel + " " + x.DeviceSerial).ToLowerInvariant().Contains(q)) continue;
                    if (st == "Sin entregar" && (x.Status == Statuses.Delivered || x.Status == "Cancelado")) continue;
                    if (st != "Todos los estados" && st != "Sin entregar" && x.Status != st) continue;
                    var it = new ListViewItem(x.Number) { Tag = x };
                    it.SubItems.Add(x.ClientName);
                    it.SubItems.Add(x.DeviceBrandModel);
                    it.SubItems.Add(x.Status);
                    it.SubItems.Add(x.ReceivedOn.Length >= 10 ? x.ReceivedOn.Substring(0, 10) : x.ReceivedOn);
                    it.SubItems.Add(x.WarrantyText);
                    view.Items.Add(it);
                }
                view.EndUpdate();
            };
            search.TextChanged += (o, e) => fill();
            filter.SelectedIndexChanged += (o, e) => fill();
            fill();
            view.DoubleClick += (o, e) => { if (view.SelectedItems.Count == 1) ShowEdit((Ficha)view.SelectedItems[0].Tag, false); };
            view.KeyDown += (o, e) => { if (e.KeyCode == Keys.Enter && view.SelectedItems.Count == 1) ShowEdit((Ficha)view.SelectedItems[0].Tag, false); };

            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            bar.Controls.Add(Ui.Btn("Abrir ficha", false, (o, e) =>
            {
                if (view.SelectedItems.Count == 1) ShowEdit((Ficha)view.SelectedItems[0].Tag, false);
                else Warn("Selecciona una ficha de la lista.");
            }));
            bar.Controls.Add(Ui.Btn("Créditos", false, (o, e) => ShowCredits()));
            bar.Controls.Add(Ui.Btn("Datos de mi negocio", false, (o, e) => ShowSettings()));
            bar.Controls.Add(Ui.Btn("Hacer copia de seguridad", false, (o, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Elige dónde guardar la copia de las fichas." })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    try { MessageBox.Show(this, store.BackupTo(d.SelectedPath) + " archivos copiados.", Text); }
                    catch (Exception ex) { Warn("No se pudo copiar: " + ex.Message); }
                }
            }));
            bar.Controls.Add(Ui.Btn("Exportar a Excel", false, (o, e) =>
            {
                using (var d = new SaveFileDialog { FileName = "Fichas_" + DateTime.Now.ToString("yyyy-MM-dd") + ".csv", Filter = "CSV para Excel (*.csv)|*.csv" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    try { Csv.Export(d.FileName, all, store.Settings); Open(d.FileName); }
                    catch (Exception ex) { Warn("No se pudo exportar: " + ex.Message); }
                }
            }));
            root.Controls.Add(bar, 0, 3);
            Page(root);
        }

        // ---------- ficha ----------

        void ShowEdit(Ficha x, bool isNew)
        {
            x.Normalize();
            Header("Ficha " + x.Number, isNew ? "Completa lo que tengas: puedes volver a ella en cualquier momento." : (x.ClientName + " " + x.DeviceBrandModel).Trim());
            var settings = store.Settings;
            var tabs = new TabControl { Dock = DockStyle.Fill };

            // Cliente y equipo
            var tab1 = new TabPage("Cliente y equipo") { BackColor = Color.White, AutoScroll = true };
            var g1 = new FieldGrid();
            var client = new TextBox { Text = x.ClientName };
            var phone = new TextBox { Text = x.ClientPhone };
            var doc = new TextBox { Text = x.ClientDocument };
            var email = new TextBox { Text = x.ClientEmail };
            var address = new TextBox { Text = x.ClientAddress };
            g1.Add("Nombre del cliente *", client, "Teléfono", phone);
            g1.Add("Documento", doc, "Correo", email);
            g1.Wide("Dirección", address);
            var dtype = new ComboBox { Text = x.DeviceType };
            dtype.Items.AddRange(new object[] { "Portátil", "Escritorio", "Todo en uno", "Tableta", "Impresora", "Otro" });
            dtype.Text = x.DeviceType;
            var brand = new TextBox { Text = x.DeviceBrandModel };
            var serial = new TextBox { Text = x.DeviceSerial };
            var status = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            status.Items.AddRange(Statuses.All.Cast<object>().ToArray());
            status.SelectedItem = Statuses.All.Contains(x.Status) ? x.Status : Statuses.Received;
            var specs = new TextBox { Text = x.DeviceSpecs };
            g1.Add("Tipo de equipo", dtype, "Marca y modelo", brand);
            g1.Add("Número de serie", serial, "Estado de la ficha", status);
            g1.Wide("Características", specs);
            var accessories = new TextBox { Text = x.Accessories };
            g1.Wide("Accesorios recibidos (cargador, funda…)", accessories);
            var condition = Multi(x.Condition, 2);
            g1.Wide("Estado físico al recibirlo (golpes, rayones…)", condition);
            var problem = Multi(x.Problem, 3);
            g1.Wide("Problema que informa el cliente", problem);
            var history = Ui.Text("", Ui.Small, Ui.Slate);
            history.MaximumSize = new Size(Ui.D(900), 0);
            history.Dock = DockStyle.Top;
            var read = Ui.Btn("Leer los datos de este equipo", false, null);
            var readBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
            readBar.Controls.Add(read);
            readBar.Controls.Add(Ui.Text("Úsalo solo si este programa está abierto en el equipo del cliente. No anotes contraseñas en la ficha.", Ui.Small, Ui.Muted));
            tab1.Controls.Add(g1);
            tab1.Controls.Add(history);
            tab1.Controls.Add(readBar);
            g1.BringToFront();
            read.Click += (o, e) =>
            {
                try
                {
                    var t = new Ficha();
                    ThisPc.Read(t);
                    if (t.DeviceType.Length > 0) dtype.Text = t.DeviceType;
                    if (t.DeviceBrandModel.Length > 0) brand.Text = t.DeviceBrandModel;
                    if (t.DeviceSerial.Length > 0) serial.Text = t.DeviceSerial;
                    if (t.DeviceSpecs.Length > 0) specs.Text = t.DeviceSpecs;
                }
                catch (Exception ex) { Warn("No se pudieron leer: " + ex.Message + "\n\nEscríbelos a mano."); }
            };
            EventHandler serialChanged = (o, e) =>
            {
                string sv = serial.Text.Trim();
                history.Text = "";
                if (sv.Length < 3) return;
                try
                {
                    var prev = store.LoadAll().Where(f => f.Number != x.Number && f.DeviceSerial.Equals(sv, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (prev.Count > 0) history.Text = "Este equipo ya vino antes: " + string.Join("; ", prev.Take(5).Select(f => f.Number + " (" + (f.ReceivedOn.Length >= 10 ? f.ReceivedOn.Substring(0, 10) : "") + ")" + (f.WarrantyEnd >= DateTime.Today ? ", con garantía vigente" : "")));
                }
                catch { }
            };
            serial.Leave += serialChanged;
            serialChanged(null, null);
            tabs.TabPages.Add(tab1);

            // Trabajo y costos
            var tab2 = new TabPage("Trabajo y costos") { BackColor = Color.White };
            var lay2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.D(4)) };
            lay2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            lay2.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lay2.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            lay2.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var g2 = new FieldGrid();
            var diagnosis = Multi(x.Diagnosis, 3);
            var work = Multi(x.WorkDone, 3);
            g2.Add("Diagnóstico", diagnosis, "Trabajo realizado", work);
            lay2.Controls.Add(g2, 0, 0);
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = true, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect
            };
            // Sin esto, los títulos se recortan en pantallas con escala mayor al 100 %.
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.RowTemplate.Height = Ui.D(26);
            grid.RowHeadersWidth = Ui.D(32);
            grid.Columns.Add("d", "Repuesto o concepto"); grid.Columns[0].FillWeight = 58;
            grid.Columns.Add("q", "Cantidad"); grid.Columns[1].FillWeight = 12;
            grid.Columns.Add("u", "Precio"); grid.Columns[2].FillWeight = 15;
            grid.Columns.Add("t", "Total"); grid.Columns[3].FillWeight = 15; grid.Columns[3].ReadOnly = true;
            grid.Columns[3].DefaultCellStyle.BackColor = Ui.Page;
            foreach (var it in x.Items)
                grid.Rows.Add(it.Description, it.Quantity.ToString("0.##", CultureInfo.CurrentCulture), Money.Plain(it.UnitPrice, settings), Money.Plain(it.Total, settings));
            lay2.Controls.Add(grid, 0, 1);

            var totals = new TableLayoutPanel { ColumnCount = 6, AutoSize = true, Dock = DockStyle.Fill };
            for (int i = 0; i < 5; i++) totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
            totals.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            Func<string, string, TextBox> money = (label, value) =>
            {
                var cell = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
                cell.Controls.Add(Ui.Text(label, Ui.Small, Ui.Muted));
                var b = new TextBox { Text = value, Width = Ui.D(120), Margin = new Padding(Ui.D(2), 0, Ui.D(2), Ui.D(2)) };
                cell.Controls.Add(b);
                totals.Controls.Add(cell);
                return b;
            };
            var labor = money("Mano de obra", Money.Plain(x.Labor, settings));
            var discount = money("Descuento", Money.Plain(x.Discount, settings));
            var tax = money("Impuesto %", x.TaxPercent.ToString("0.##", CultureInfo.CurrentCulture));
            var advance = money("Abono", Money.Plain(x.Advance, settings));
            var warranty = money("Garantía (días)", x.WarrantyDays.ToString());
            var totalLabel = new Label { AutoSize = true, Font = Ui.H2, ForeColor = Ui.Ink, Margin = new Padding(Ui.D(6), Ui.D(8), 0, 0), UseMnemonic = false };
            totals.Controls.Add(totalLabel);
            lay2.Controls.Add(totals, 0, 2);
            tab2.Controls.Add(lay2);
            tabs.TabPages.Add(tab2);

            // Diagnósticos
            var tab3 = new TabPage("Antes y después") { BackColor = Color.White };
            var lay3 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(Ui.D(8)) };
            lay3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            lay3.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lay3.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lay3.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lay3.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var compareView = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle };
            compareView.Columns.Add("Revisión", Ui.D(430));
            compareView.Columns.Add("Antes", Ui.D(130));
            compareView.Columns.Add("Después", Ui.D(130));
            compareView.Columns.Add("Resultado", Ui.D(110));
            Action refreshCompare = () => { };
            Func<string, string, Control> row3 = (kind, label) =>
            {
                var r = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
                var state = Ui.Text("", Ui.Body);
                state.Width = Ui.D(500);
                Action show = () =>
                {
                    var a = x.Report(kind);
                    state.Text = label + ": " + (a == null ? "sin diagnóstico" : a.Verdict + " (" + a.Date + ")");
                };
                show();
                Action<string> attach = source =>
                {
                    try
                    {
                        var d = Reports.Parse(source);
                        string name = store.AddAttachment(x, source, kind);
                        x.Attachments.RemoveAll(a => a.Kind == kind);
                        x.Attachments.Add(new Attachment { Kind = kind, File = name, Verdict = d.Verdict.Length > 0 ? d.Verdict : Path.GetFileName(source), Detail = d.Detail, Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm") });
                        store.Save(x);
                        show();
                        refreshCompare();
                    }
                    catch (Exception ex) { Warn("No se pudo adjuntar: " + ex.Message); }
                };
                var runBtn = Ui.Btn("Hacer diagnóstico ahora", false, null);
                runBtn.Click += (o, e) =>
                {
                    string exe = Chequeador.Find();
                    if (exe == null)
                    {
                        Warn("No encontré ChequeadorPC.exe en esta carpeta. Ponlo junto a este programa, o usa «Elegir informe».");
                        return;
                    }
                    runBtn.Enabled = false; runBtn.Text = "Analizando…";
                    var th = new Thread(() =>
                    {
                        string file = null; string err = null;
                        try { file = Chequeador.Run(exe); }
                        catch (Exception ex) { err = ex.Message; }
                        try
                        {
                            BeginInvoke((Action)(() =>
                            {
                                if (runBtn.IsDisposed) return;
                                runBtn.Enabled = true; runBtn.Text = "Hacer diagnóstico ahora";
                                if (file != null) { attach(file); try { File.Delete(file); } catch { } }
                                else Warn("El diagnóstico no terminó" + (err != null ? ": " + err : "") + ". Usa «Elegir informe» si tienes uno guardado.");
                            }));
                        }
                        catch { }
                    }) { IsBackground = true };
                    th.Start();
                };
                r.Controls.Add(state);
                r.Controls.Add(runBtn);
                r.Controls.Add(Ui.Btn("Elegir informe…", false, (o, e) =>
                {
                    using (var d = new OpenFileDialog { Filter = "Informes HTML (*.html)|*.html|Todos los archivos (*.*)|*.*", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) })
                        if (d.ShowDialog(this) == DialogResult.OK) attach(d.FileName);
                }));
                r.Controls.Add(Ui.Btn("Ver", false, (o, e) =>
                {
                    var a = x.Report(kind);
                    if (a == null) Warn("Aún no hay un informe aquí.");
                    else Open(store.AttachmentPath(x, a));
                }));
                return r;
            };
            lay3.Controls.Add(row3("antes", "Al recibirlo"), 0, 0);
            lay3.Controls.Add(row3("despues", "Al entregarlo"), 0, 1);
            var compareNote = Ui.Text("", Ui.Small, Ui.Muted);
            lay3.Controls.Add(compareNote, 0, 2);
            lay3.Controls.Add(compareView, 0, 3);
            refreshCompare = () =>
            {
                compareView.Items.Clear();
                var rows = CompareOf(x);
                if (rows == null) { compareNote.Text = "Cuando haya un informe del Chequeador antes y otro después, aquí verás qué cambió."; return; }
                compareNote.Text = rows.Count == 0 ? "No hay diferencias entre las dos revisiones." : "Cambios detectados: " + rows.Count;
                foreach (var c in rows)
                {
                    var it = new ListViewItem(c.Title);
                    it.SubItems.Add(c.Before); it.SubItems.Add(c.After); it.SubItems.Add(c.Change);
                    if (c.Change == "Empeoró" || c.Change == "Nuevo") it.ForeColor = Color.FromArgb(0xC6, 0x28, 0x28);
                    compareView.Items.Add(it);
                }
            };
            refreshCompare();
            tab3.Controls.Add(lay3);
            tabs.TabPages.Add(tab3);

            // Notas
            var tab4 = new TabPage("Notas internas") { BackColor = Color.White };
            var lay4 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(Ui.D(8)) };
            lay4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            lay4.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            lay4.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            var notes = Multi(x.Notes, 4);
            notes.Dock = DockStyle.Fill;
            var notesHost = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1 };
            notesHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            notesHost.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            notesHost.Controls.Add(Ui.Text("Observaciones. Aparecen en el comprobante de servicio.", Ui.Small, Ui.Muted), 0, 0);
            notesHost.Controls.Add(notes, 0, 1);
            lay4.Controls.Add(notesHost, 0, 0);
            var hist = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            foreach (var h in x.History.AsEnumerable().Reverse()) hist.Items.Add(h.Date + "   " + h.Text);
            lay4.Controls.Add(hist, 0, 1);
            tab4.Controls.Add(lay4);
            tab4.Text = "Observaciones e historial";
            tabs.TabPages.Add(tab4);

            // Pasa lo escrito a un objeto; si algo no es válido devuelve el mensaje.
            Func<Ficha, string> collect = t =>
            {
                t.ClientName = client.Text.Trim(); t.ClientPhone = phone.Text.Trim(); t.ClientDocument = doc.Text.Trim(); t.ClientEmail = email.Text.Trim(); t.ClientAddress = address.Text.Trim();
                t.DeviceType = dtype.Text.Trim(); t.DeviceBrandModel = brand.Text.Trim(); t.DeviceSerial = serial.Text.Trim(); t.DeviceSpecs = specs.Text.Trim();
                t.Accessories = accessories.Text.Trim(); t.Condition = condition.Text.Trim(); t.Problem = problem.Text.Trim();
                t.Diagnosis = diagnosis.Text.Trim(); t.WorkDone = work.Text.Trim(); t.Notes = notes.Text.Trim();
                t.Status = status.SelectedItem as string ?? Statuses.Received;
                var items = new List<Item>();
                foreach (DataGridViewRow r in grid.Rows)
                {
                    if (r.IsNewRow) continue;
                    string d = Convert.ToString(r.Cells[0].Value ?? "").Trim(), q = Convert.ToString(r.Cells[1].Value ?? "").Trim(), u = Convert.ToString(r.Cells[2].Value ?? "").Trim();
                    if (d.Length == 0 && q.Length == 0 && u.Length == 0) continue;
                    decimal qv, uv;
                    if (q.Length == 0) qv = 1;
                    else if (!Money.TryParse(q, out qv)) return "La cantidad «" + q + "» no es un número.";
                    if (!Money.TryParse(u, out uv)) return "El precio «" + u + "» no es un número.";
                    if (qv < 0 || uv < 0) return "Las cantidades y los precios no pueden ser negativos.";
                    items.Add(new Item { Description = d, Quantity = qv, UnitPrice = uv });
                }
                t.Items = items;
                decimal lv, dv, tv, av;
                if (!Money.TryParse(labor.Text, out lv) || lv < 0) return "La mano de obra no es un número válido.";
                if (!Money.TryParse(discount.Text, out dv) || dv < 0) return "El descuento no es un número válido.";
                if (!Money.TryParse(tax.Text, out tv) || tv < 0 || tv > 100) return "El impuesto debe estar entre 0 y 100.";
                if (!Money.TryParse(advance.Text, out av) || av < 0) return "El abono no es un número válido.";
                int days;
                if (!int.TryParse(warranty.Text.Trim().Length == 0 ? "0" : warranty.Text.Trim(), out days) || days < 0) return "Los días de garantía deben ser un número entero.";
                t.Labor = lv; t.Discount = dv; t.TaxPercent = tv; t.Advance = av; t.WarrantyDays = days;
                return null;
            };

            Action showTotal = () =>
            {
                var t = new Ficha();
                string err = collect(t);
                foreach (DataGridViewRow r in grid.Rows)
                {
                    if (r.IsNewRow) continue;
                    decimal q, u;
                    string qs = Convert.ToString(r.Cells[1].Value ?? "");
                    bool ok = Money.TryParse(qs, out q) & Money.TryParse(Convert.ToString(r.Cells[2].Value ?? ""), out u);
                    if (qs.Trim().Length == 0) q = 1;
                    string shown = ok ? Money.Plain(q * u, settings) : "?";
                    if (Convert.ToString(r.Cells[3].Value) != shown) r.Cells[3].Value = shown;
                }
                totalLabel.Text = err != null ? "Revisa los valores" : "Total: " + Money.Show(t.Total, settings) + "\nSaldo: " + Money.Show(t.Balance, settings);
            };
            grid.CellEndEdit += (o, e) => showTotal();
            grid.RowsRemoved += (o, e) => showTotal();
            foreach (var b in new[] { labor, discount, tax, advance }) b.TextChanged += (o, e) => showTotal();
            showTotal();

            Func<bool> save = () =>
            {
                if (grid.IsCurrentCellInEditMode) grid.EndEdit();
                var t = new Ficha();
                string err = collect(t);
                if (err != null) { Warn(err); return false; }
                if (t.ClientName.Length == 0) { tabs.SelectedIndex = 0; client.Focus(); Warn("Escribe el nombre del cliente para guardar la ficha."); return false; }
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                if (t.Status != x.Status)
                {
                    x.History.Add(new HistoryEntry { Date = now, Text = "Estado: " + x.Status + " pasa a " + t.Status });
                    if (t.Status == Statuses.Delivered) x.DeliveredOn = now;
                    else if (x.Status == Statuses.Delivered) x.DeliveredOn = "";
                }
                x.ClientName = t.ClientName; x.ClientPhone = t.ClientPhone; x.ClientDocument = t.ClientDocument; x.ClientEmail = t.ClientEmail; x.ClientAddress = t.ClientAddress;
                x.DeviceType = t.DeviceType; x.DeviceBrandModel = t.DeviceBrandModel; x.DeviceSerial = t.DeviceSerial; x.DeviceSpecs = t.DeviceSpecs;
                x.Accessories = t.Accessories; x.Condition = t.Condition; x.Problem = t.Problem; x.Diagnosis = t.Diagnosis; x.WorkDone = t.WorkDone; x.Notes = t.Notes;
                x.Status = t.Status; x.Items = t.Items; x.Labor = t.Labor; x.Discount = t.Discount; x.TaxPercent = t.TaxPercent; x.Advance = t.Advance; x.WarrantyDays = t.WarrantyDays;
                try { store.Save(x); }
                catch (Exception ex) { Warn("No se pudo guardar: " + ex.Message + "\n\nNo cierres esta ventana: revisa la carpeta o la USB y pulsa Guardar otra vez."); return false; }
                subtitle.Text = "Guardado a las " + DateTime.Now.ToString("HH:mm") + ".";
                return true;
            };

            string baseline = null;
            Func<string> snapshot = () =>
            {
                var t = new Ficha();
                if (collect(t) != null) return "invalido";
                t.Number = x.Number; t.ReceivedOn = x.ReceivedOn; t.History = null; t.Attachments = null;
                return Json.Write(t);
            };
            baseline = snapshot();
            leaveCheck = () =>
            {
                if (grid.IsCurrentCellInEditMode) grid.EndEdit();
                if (snapshot() == baseline) return true;
                var a = MessageBox.Show(this, "Hay cambios sin guardar. ¿Guardarlos?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (a == DialogResult.Cancel) return false;
                if (a == DialogResult.No) return true;
                return save();
            };

            Action<string> pdf = which =>
            {
                if (!save()) return;
                baseline = snapshot();
                string dir = store.AttachmentsDir(x.Number);
                string path = Path.Combine(dir, (which == "recepcion" ? "Recepcion_" : "Servicio_") + x.Number + ".pdf");
                try
                {
                    Directory.CreateDirectory(dir);
                    Program.BuildPdf(x, store, which, path);
                    Open(path);
                }
                catch (IOException ex) { Warn("No se pudo crear el PDF. Si ya lo tienes abierto, ciérralo y reintenta.\n\n" + ex.Message); }
                catch (Exception ex) { Warn("No se pudo crear el PDF: " + ex.Message); }
            };

            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Padding = new Padding(Ui.D(10), Ui.D(6), Ui.D(10), Ui.D(4)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(tabs, 0, 0);
            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            bar.Controls.Add(Ui.Btn("Guardar", true, (o, e) => { if (save()) baseline = snapshot(); }));
            bar.Controls.Add(Ui.Btn("Comprobante de servicio (PDF)", false, (o, e) => pdf("servicio")));
            bar.Controls.Add(Ui.Btn("Comprobante de recepción (PDF)", false, (o, e) => pdf("recepcion")));
            bar.Controls.Add(Ui.Btn("Volver a la lista", false, (o, e) => { if (leaveCheck()) ShowList(); }));
            root.Controls.Add(bar, 0, 1);
            Page(root);
            if (isNew) client.Focus();
        }

        public static List<CompareRow> CompareOf(Ficha x)
        {
            return Program.CompareOf(x, Program.Data);
        }

        static TextBox Multi(string text, int lines)
        {
            return new TextBox { Text = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"), Multiline = true, ScrollBars = ScrollBars.Vertical, Height = Ui.D(24 * lines), AcceptsReturn = true };
        }

        // ---------- ajustes ----------

        void ShowSettings()
        {
            leaveCheck = null;
            Header("Datos de mi negocio", "Aparecen en los comprobantes. Todo es opcional.");
            var s = store.Settings;
            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Padding = new Padding(Ui.D(14), Ui.D(8), Ui.D(14), Ui.D(4)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var g = new FieldGrid();
            var business = new TextBox { Text = s.BusinessName };
            var tech = new TextBox { Text = s.TechnicianName };
            var phone = new TextBox { Text = s.Phone };
            var email = new TextBox { Text = s.Email };
            var bid = new TextBox { Text = s.BusinessId };
            var address = new TextBox { Text = s.Address };
            g.Add("Nombre del negocio", business, "Técnico", tech);
            g.Add("Teléfono", phone, "Correo", email);
            g.Add("Identificación fiscal", bid, "Dirección", address);
            var currency = new TextBox { Text = s.Currency };
            var decimals = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            decimals.Items.AddRange(new object[] { "Con centavos", "Sin centavos" });
            decimals.SelectedIndex = s.Decimals <= 0 ? 1 : 0;
            g.Add("Símbolo de moneda", currency, "Precios", decimals);
            var tax = new TextBox { Text = s.TaxPercent.ToString("0.##", CultureInfo.CurrentCulture) };
            var warranty = new TextBox { Text = s.WarrantyDays.ToString() };
            g.Add("Impuesto habitual (%)", tax, "Garantía habitual (días)", warranty);
            var size = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            size.Items.AddRange(new object[] { "Carta", "A4" });
            size.SelectedItem = s.PageSize.Equals("A4", StringComparison.OrdinalIgnoreCase) ? "A4" : "Carta";
            var logo = new TextBox { Text = s.LogoFile, ReadOnly = true, BackColor = Color.White };
            g.Add("Tamaño del papel", size, "Logo", logo);
            var reception = Multi(s.ReceptionTerms, 3);
            var delivery = Multi(s.DeliveryTerms, 3);
            g.Wide("Condiciones del comprobante de recepción", reception);
            g.Wide("Condiciones del comprobante de servicio", delivery);
            scroll.Controls.Add(g);
            root.Controls.Add(scroll, 0, 0);

            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            bar.Controls.Add(Ui.Btn("Guardar", true, (o, e) =>
            {
                decimal t; int w;
                if (!Money.TryParse(tax.Text, out t) || t < 0 || t > 100) { Warn("El impuesto debe estar entre 0 y 100."); return; }
                if (!int.TryParse(warranty.Text.Trim().Length == 0 ? "0" : warranty.Text.Trim(), out w) || w < 0) { Warn("La garantía debe ser un número entero de días."); return; }
                s.BusinessName = business.Text.Trim(); s.TechnicianName = tech.Text.Trim(); s.Phone = phone.Text.Trim(); s.Email = email.Text.Trim();
                s.BusinessId = bid.Text.Trim(); s.Address = address.Text.Trim(); s.Currency = currency.Text.Trim().Length > 0 ? currency.Text.Trim() : "$";
                s.Decimals = decimals.SelectedIndex == 1 ? 0 : 2; s.TaxPercent = t; s.WarrantyDays = w; s.PageSize = size.SelectedItem as string ?? "Carta";
                s.LogoFile = logo.Text; s.ReceptionTerms = reception.Text.Trim(); s.DeliveryTerms = delivery.Text.Trim();
                try { store.SaveSettings(); ShowList(); }
                catch (Exception ex) { Warn("No se pudo guardar: " + ex.Message); }
            }));
            bar.Controls.Add(Ui.Btn("Volver sin guardar", false, (o, e) => ShowList()));
            bar.Controls.Add(Ui.Btn("Quitar logo", false, (o, e) => logo.Text = ""));
            bar.Controls.Add(Ui.Btn("Elegir logo…", false, (o, e) =>
            {
                using (var d = new OpenFileDialog { Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        using (Image.FromFile(d.FileName)) { }
                        string name = "logo" + Path.GetExtension(d.FileName).ToLowerInvariant();
                        File.Copy(d.FileName, Path.Combine(store.Root, name), true);
                        logo.Text = name;
                    }
                    catch (Exception ex) { Warn("No se pudo usar esa imagen: " + ex.Message); }
                }
            }));
            root.Controls.Add(bar, 0, 1);
            Page(root);
        }

        void ShowCredits()
        {
            leaveCheck = null;
            Header("Créditos", "Autor y sobre el programa.");
            var host = new Panel();
            var panel = new Comun.CreditsPanel(Program.Info, Ui.Ink, Ui.Muted) { Dock = DockStyle.Fill };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(Ui.D(12), Ui.D(4), Ui.D(12), Ui.D(8)) };
            bar.Controls.Add(Ui.Btn("Volver", true, (o, e) => ShowList()));
            host.Controls.Add(panel);
            host.Controls.Add(bar);
            Page(host);
        }
    }
}
