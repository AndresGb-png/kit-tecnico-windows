using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using FormsTimer = System.Windows.Forms.Timer;

namespace PosformateoPC
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
        readonly bool admin = Checks.Admin();

        List<App> apps = Catalog.All();
        readonly List<Tweak> tweaks = Tweaks.Build();
        readonly Dictionary<string, List<KeyValuePair<string, string>>> saved = SavedLists.Load();
        List<string> oldPrograms = new List<string>();
        string backupFound;
        TextBox nameBox;
        ComboBox profileCombo;

        public MainForm()
        {
            using (var g = CreateGraphics()) Ui.Scale = g.DpiX / 96f;
            AutoScaleMode = AutoScaleMode.None;
            Text = "Instalador posformateo";
            Font = Ui.Body;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            MinimumSize = new Size(Math.Min(Ui.D(720), area.Width), Math.Min(Ui.D(500), area.Height));
            Size = new Size(Math.Min(Ui.D(980), area.Width), Math.Min(Ui.D(740), area.Height));
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
                if (busy && MessageBox.Show(this, "Hay una instalación en curso. Si cierras, se detendrá lo que falte.\n\n¿Cerrar?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    e.Cancel = true;
            };
            FormClosed += (s, e) => Environment.Exit(0);

            LoadExtras();
            Winget.Detect();
            ShowSetup();
        }

        Control PermissionBar()
        {
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Color.FromArgb(0xFB, 0xEE, 0xD5), Padding = new Padding(Ui.D(12), Ui.D(2), Ui.D(12), Ui.D(2)) };
            bar.Controls.Add(new Label { Text = "Sin administrador, Windows pedirá permiso en cada programa y algunos ajustes fallarán.", AutoSize = true, ForeColor = Ui.Ink, Margin = new Padding(Ui.D(4), Ui.D(9), Ui.D(4), Ui.D(4)) });
            bar.Controls.Add(Ui.Btn("Reabrir como administrador", false, (s, e) =>
            {
                if (busy) { MessageBox.Show(this, "Espera a que termine la instalación.", Text); return; }
                if (Program.Relaunch("--no-elevar")) Environment.Exit(0);
                else MessageBox.Show(this, "No se concedió el permiso. Puedes seguir con lo disponible.", Text);
            }));
            return bar;
        }

        // Instaladores en una carpeta junto al programa y lista del respaldo, si existen.
        void LoadExtras()
        {
            try
            {
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Instaladores");
                if (Directory.Exists(local)) AddLocalFolder(local);
            }
            catch { }
            try
            {
                backupFound = BackupImport.FindNewest();
                if (backupFound == null) return;
                oldPrograms = BackupImport.ProgramNames(backupFound);
                foreach (var id in BackupImport.WingetIds(backupFound))
                {
                    if (apps.Any(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                        apps.First(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Group = "Del equipo anterior";
                    else
                        apps.Insert(0, new App { Id = id, Name = id, Group = "Del equipo anterior", Hint = "Encontrado en el respaldo" });
                }
            }
            catch { }
        }

        void AddLocalFolder(string folder)
        {
            foreach (var f in Directory.GetFiles(folder).Where(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
            {
                if (apps.Any(a => a.Id.Equals(f, StringComparison.OrdinalIgnoreCase))) continue;
                bool msi = f.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
                apps.Add(new App { Kind = Kind.Local, Id = f, Name = Path.GetFileName(f), Group = "Instaladores de la USB", Hint = msi ? "Se instala en silencio" : "Abre su propia ventana: sigue sus pasos", Selected = true });
            }
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

        // ---------- configuración ----------

        void ShowSetup()
        {
            Header("Instalador posformateo", "Elige un perfil, ajusta la lista y deja el equipo listo.");
            var root = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.D(12), Ui.D(8), Ui.D(12), Ui.D(4)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            if (!Winget.Available) root.Controls.Add(WingetMissingBar(), 0, 0);
            else if (backupFound != null) root.Controls.Add(Note("Respaldo encontrado: " + Path.GetFileName(backupFound) + ". Sus programas aparecen en «Del equipo anterior»."), 0, 0);
            else root.Controls.Add(Note("Los programas se descargan de sus fabricantes con winget " + Winget.Version + "."), 0, 0);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var appsTab = new TabPage("Programas") { BackColor = Color.White };
            var tweaksTab = new TabPage("Ajustes del equipo") { BackColor = Color.White };
            tabs.TabPages.Add(appsTab);
            tabs.TabPages.Add(tweaksTab);
            BuildAppsTab(appsTab);
            BuildTweaksTab(tweaksTab);
            root.Controls.Add(tabs, 0, 1);

            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            bar.Controls.Add(Ui.Btn("Empezar", true, (s, e) => Begin()));
            bar.Controls.Add(Ui.Btn("Créditos", false, (s, e) => ShowCredits()));
            root.Controls.Add(bar, 0, 2);
            Page(root);
        }

        static Label Note(string text)
        {
            var l = Ui.Text(text, Ui.Small, Ui.Muted);
            l.Margin = new Padding(Ui.D(2), Ui.D(2), Ui.D(2), Ui.D(6));
            return l;
        }

        Control WingetMissingBar()
        {
            var bar = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.FromArgb(0xFB, 0xEE, 0xD5), Padding = new Padding(Ui.D(8), Ui.D(2), Ui.D(8), Ui.D(2)) };
            bar.Controls.Add(Ui.Text("Este Windows no tiene winget activo, así que solo funcionan los instaladores de una carpeta."));
            bar.Controls.Add(Ui.Btn("Activar winget", false, (s, e) =>
            {
                var b = (Button)s;
                b.Enabled = false; b.Text = "Activando…";
                var th = new Thread(() =>
                {
                    bool ok = Winget.TryRegister();
                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            if (ok) { ShowSetup(); return; }
                            b.Enabled = true; b.Text = "Activar winget";
                            MessageBox.Show(this, "No se pudo activar. Conéctate a internet, actualiza «Instalador de aplicación» desde Microsoft Store y vuelve a abrir este programa.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }));
                    }
                    catch { }
                }) { IsBackground = true };
                th.Start();
            }));
            bar.Controls.Add(Ui.Btn("Abrir Microsoft Store", false, (s, e) =>
            {
                try { Process.Start("ms-windows-store://pdp/?productid=9NBLGGH4NNS1"); }
                catch { MessageBox.Show(this, "Este Windows no tiene Microsoft Store. Usa una carpeta de instaladores.", Text); }
            }));
            return bar;
        }

        TreeView tree;

        void BuildAppsTab(TabPage page)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.D(6)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var top = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            top.Controls.Add(Ui.Text("Perfil"));
            profileCombo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Ui.D(180), Margin = new Padding(Ui.D(4), Ui.D(6), Ui.D(4), Ui.D(4)) };
            profileCombo.Items.Add("Ninguno");
            foreach (var p in Catalog.Profiles.Keys) profileCombo.Items.Add(p);
            foreach (var p in saved.Keys) profileCombo.Items.Add(p);
            profileCombo.SelectedIndexChanged += (s, e) => ApplyProfile(profileCombo.SelectedItem as string);
            top.Controls.Add(profileCombo);
            top.Controls.Add(Ui.Btn("Guardar esta lista…", false, (s, e) => SaveList()));
            top.Controls.Add(Ui.Btn("Agregar carpeta de instaladores…", false, (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Elige la carpeta con instaladores .exe y .msi." })
                    if (d.ShowDialog(this) == DialogResult.OK)
                    {
                        try { AddLocalFolder(d.SelectedPath); FillTree(); }
                        catch (Exception ex) { MessageBox.Show(this, "No se pudo leer la carpeta: " + ex.Message, Text); }
                    }
            }));
            layout.Controls.Add(top, 0, 0);

            var searchRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            searchRow.Controls.Add(Ui.Text("Buscar otro programa"));
            var box = new TextBox { Width = Ui.D(240), Margin = new Padding(Ui.D(4), Ui.D(7), Ui.D(4), Ui.D(4)) };
            searchRow.Controls.Add(box);
            var go = Ui.Btn("Buscar", false, null);
            go.Enabled = Winget.Available;
            EventHandler doSearch = (s, e) => { if (box.Text.Trim().Length >= 2 && go.Enabled) Search(box.Text.Trim(), go); };
            go.Click += doSearch;
            box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; doSearch(s, e); } };
            searchRow.Controls.Add(go);
            layout.Controls.Add(searchRow, 0, 1);

            tree = new TreeView { Dock = DockStyle.Fill, CheckBoxes = true, BorderStyle = BorderStyle.FixedSingle, ShowLines = false, HideSelection = true, ItemHeight = Ui.D(24) };
            tree.AfterCheck += (s, e) =>
            {
                if (e.Action == TreeViewAction.Unknown) return;
                var a = e.Node.Tag as App;
                if (a != null) a.Selected = e.Node.Checked;
                foreach (TreeNode child in e.Node.Nodes)
                {
                    child.Checked = e.Node.Checked;
                    var c = child.Tag as App;
                    if (c != null) c.Selected = child.Checked;
                }
            };
            layout.Controls.Add(tree, 0, 2);
            page.Controls.Add(layout);

            FillTree();
            if (profileCombo.SelectedIndex < 0 && !apps.Any(a => a.Selected)) profileCombo.SelectedItem = "Ninguno";
        }

        void FillTree()
        {
            if (tree == null) return;
            tree.BeginUpdate();
            tree.Nodes.Clear();
            var visible = apps.Where(a => a.Kind == Kind.Local || Winget.Available).ToList();
            foreach (var g in visible.GroupBy(a => a.Group))
            {
                var parent = tree.Nodes.Add(g.Key);
                parent.Checked = g.All(a => a.Selected);
                foreach (var a in g)
                {
                    var n = parent.Nodes.Add(a.Name + (a.Hint != null ? ". " + a.Hint : ""));
                    n.Tag = a; n.Checked = a.Selected; a.Node = n;
                }
                if (g.Key == "Del equipo anterior" || g.Key == "Instaladores de la USB" || g.Any(a => a.Selected)) parent.Expand();
            }
            if (visible.Count == 0)
                tree.Nodes.Add("No hay programas disponibles. Activa winget o agrega una carpeta de instaladores.");
            tree.EndUpdate();
        }

        void ApplyProfile(string name)
        {
            if (name == null) return;
            foreach (var a in apps) if (a.Kind == Kind.Winget) a.Selected = false;
            string[] ids;
            if (Catalog.Profiles.TryGetValue(name, out ids))
            {
                foreach (var a in apps) if (ids.Contains(a.Id, StringComparer.OrdinalIgnoreCase)) a.Selected = true;
            }
            else if (saved.ContainsKey(name))
            {
                foreach (var row in saved[name])
                {
                    var a = apps.FirstOrDefault(x => x.Id.Equals(row.Key, StringComparison.OrdinalIgnoreCase));
                    if (a == null) { a = new App { Id = row.Key, Name = row.Value, Group = "Añadidos por ti" }; apps.Add(a); }
                    a.Selected = true;
                }
            }
            FillTree();
        }

        void SaveList()
        {
            var chosen = apps.Where(a => a.Selected && a.Kind == Kind.Winget).ToList();
            if (chosen.Count == 0) { MessageBox.Show(this, "Marca al menos un programa antes de guardar la lista.", Text); return; }
            using (var d = new Form { Text = "Guardar esta lista", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(Ui.D(420), Ui.D(130)), Font = Ui.Body, AutoScaleMode = AutoScaleMode.None })
            {
                var box = new TextBox { Left = Ui.D(14), Top = Ui.D(40), Width = Ui.D(390) };
                var ok = new Button { Text = "Guardar", DialogResult = DialogResult.OK, Left = Ui.D(310), Top = Ui.D(80), Width = Ui.D(94), Height = Ui.D(32) };
                d.Controls.Add(new Label { Text = "Nombre, por ejemplo el de un cliente:", Left = Ui.D(14), Top = Ui.D(14), AutoSize = true });
                d.Controls.Add(box);
                d.Controls.Add(ok);
                d.AcceptButton = ok;
                if (d.ShowDialog(this) != DialogResult.OK || box.Text.Trim().Length == 0) return;
                try
                {
                    string name = box.Text.Trim();
                    string path = SavedLists.Save(name, chosen);
                    saved[name] = chosen.Select(a => new KeyValuePair<string, string>(a.Id, a.Name)).ToList();
                    if (!profileCombo.Items.Contains(name)) profileCombo.Items.Add(name);
                    MessageBox.Show(this, "Lista guardada en:\n" + path, Text);
                }
                catch (Exception ex) { MessageBox.Show(this, "No se pudo guardar: " + ex.Message, Text); }
            }
        }

        void Search(string query, Button go)
        {
            go.Enabled = false; go.Text = "Buscando…";
            var th = new Thread(() =>
            {
                string o = "";
                List<App> found = new List<App>();
                try
                {
                    new ToolRun().Run(Winget.Path_, Winget.SearchArgs(query), 60000, out o);
                    found = Winget.ParseSearch(o);
                }
                catch { }
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        go.Enabled = true; go.Text = "Buscar";
                        if (found.Count == 0)
                        {
                            MessageBox.Show(this, "No se encontró «" + query + "». Revisa el nombre y la conexión a internet.", Text);
                            return;
                        }
                        PickResults(found);
                    }));
                }
                catch { }
            }) { IsBackground = true };
            th.Start();
        }

        void PickResults(List<App> found)
        {
            using (var d = new Form { Text = "Resultados", StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, ClientSize = new Size(Ui.D(560), Ui.D(420)), Font = Ui.Body, AutoScaleMode = AutoScaleMode.None })
            {
                var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false, BorderStyle = BorderStyle.None };
                foreach (var a in found.Take(40)) list.Items.Add(a.Name + "   (" + a.Id + ")");
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = Ui.Page, Padding = new Padding(Ui.D(6)) };
                var add = Ui.Btn("Agregar a la lista", true, null);
                add.DialogResult = DialogResult.OK;
                var cancel = Ui.Btn("Cancelar", false, null);
                cancel.DialogResult = DialogResult.Cancel;
                bar.Controls.Add(add);
                bar.Controls.Add(cancel);
                d.Controls.Add(list);
                d.Controls.Add(bar);
                d.CancelButton = cancel;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                foreach (int i in list.CheckedIndices)
                {
                    var a = found[i];
                    var existing = apps.FirstOrDefault(x => x.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase));
                    if (existing != null) existing.Selected = true;
                    else { a.Group = "Añadidos por ti"; a.Selected = true; apps.Add(a); }
                }
                FillTree();
            }
        }

        void BuildTweaksTab(TabPage page)
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(Ui.D(14), Ui.D(10), Ui.D(14), Ui.D(10)) };
            foreach (var t in tweaks)
            {
                var tw = t;
                var c = new CheckBox { Text = t.Label, Checked = t.Selected, AutoSize = true, Margin = new Padding(Ui.D(2), Ui.D(6), Ui.D(2), 0) };
                c.CheckedChanged += (s, e) => tw.Selected = c.Checked;
                flow.Controls.Add(c);
                if (t.Hint != null)
                {
                    var h = Ui.Text(t.Hint, Ui.Small, Ui.Muted);
                    h.Margin = new Padding(Ui.D(20), 0, Ui.D(2), Ui.D(2));
                    flow.Controls.Add(h);
                }
            }
            var lbl = Ui.Text("Nombre del equipo (opcional)", Ui.H2);
            lbl.Margin = new Padding(Ui.D(2), Ui.D(18), Ui.D(2), Ui.D(2));
            flow.Controls.Add(lbl);
            flow.Controls.Add(new Label { Text = "Actual: " + Environment.MachineName + ". Hasta 15 letras, números o guiones. Se aplica al reiniciar.", AutoSize = true, ForeColor = Ui.Muted, Font = Ui.Small, Margin = new Padding(Ui.D(2), 0, Ui.D(2), Ui.D(2)) });
            nameBox = new TextBox { Width = Ui.D(240), MaxLength = 15, Margin = new Padding(Ui.D(2), Ui.D(4), Ui.D(2), Ui.D(4)) };
            flow.Controls.Add(nameBox);
            page.Controls.Add(flow);
        }

        // ---------- ejecución ----------

        void Begin()
        {
            var chosen = apps.Where(a => a.Selected && (a.Kind == Kind.Local || Winget.Available)).ToList();
            var list = tweaks.Where(t => t.Key != "nombre").ToList();
            string newName = nameBox == null ? "" : nameBox.Text.Trim();
            if (newName.Length > 0)
            {
                if (!Tweaks.ValidName(newName))
                {
                    MessageBox.Show(this, "Ese nombre no es válido. Usa hasta 15 letras, números o guiones, sin espacios, sin empezar ni terminar en guion y no solo números.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                list.Add(Tweaks.RenameTweak(newName));
            }
            if (chosen.Count == 0 && !list.Any(t => t.Selected))
            {
                MessageBox.Show(this, "No hay nada seleccionado. Marca programas o ajustes.", Text);
                return;
            }
            if (chosen.Any(a => a.Kind == Kind.Winget) && !Checks.Internet() &&
                MessageBox.Show(this, "No parece haber conexión a internet y los programas de winget se descargan.\n\nConéctate primero. ¿Seguir de todos modos?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            var ps = SystemInformation.PowerStatus;
            if (ps.PowerLineStatus == PowerLineStatus.Offline && ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery &&
                MessageBox.Show(this, "El equipo está funcionando con batería. Conviene enchufarlo antes de instalar.\n\n¿Seguir?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            RunAll(new Installer(chosen), list);
        }

        void RunAll(Installer inst, List<Tweak> list)
        {
            Header("Preparando el equipo…", "Puedes omitir un programa si se atasca o detener el resto.");
            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.D(12), Ui.D(10), Ui.D(12), Ui.D(6)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var now = new Label { AutoSize = false, Dock = DockStyle.Fill, Height = Ui.D(46), ForeColor = Ui.Ink, AutoEllipsis = true, UseMnemonic = false };
            layout.Controls.Add(now, 0, 0);
            var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle };
            view.Columns.Add("Programa", Ui.D(320));
            view.Columns.Add("Estado", Ui.D(210));
            view.Columns.Add("Detalle", Ui.D(380));
            foreach (var s in inst.Steps)
            {
                var it = new ListViewItem(s.App.Name) { Tag = s };
                it.SubItems.Add(Report.StateWord(s.State));
                it.SubItems.Add("");
                view.Items.Add(it);
            }
            layout.Controls.Add(view, 0, 1);

            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            var stop = Ui.Btn("Detener lo que falta", false, null);
            var skip = Ui.Btn("Omitir este programa", false, (s, e) => inst.SkipCurrent());
            stop.Click += (s, e) => { inst.StopAll(); stop.Enabled = false; skip.Enabled = false; stop.Text = "Deteniendo…"; };
            bar.Controls.Add(stop);
            bar.Controls.Add(skip);
            layout.Controls.Add(bar, 0, 2);
            Page(layout);

            busy = true;
            string stage = "Aplicando ajustes…";
            var thread = new Thread(() =>
            {
                try
                {
                    Tweaks.RunAll(list.Where(t => !t.Done && !inst.Cancel));
                    stage = null;
                    inst.Run();
                }
                catch (Exception ex) { Program.LogCrash(ex); }
            }) { IsBackground = true };
            thread.Start();

            Tick((s, e) =>
            {
                foreach (ListViewItem it in view.Items)
                {
                    var st = (Step)it.Tag;
                    string word = Report.StateWord(st.State);
                    if (it.SubItems[1].Text != word)
                    {
                        it.SubItems[1].Text = word;
                        it.SubItems[2].Text = st.Detail;
                        it.ForeColor = st.State == StepState.Failed ? Color.FromArgb(0xC6, 0x28, 0x28) : st.State == StepState.Running ? Ui.Slate : Ui.Ink;
                        if (st.State == StepState.Running) it.EnsureVisible();
                    }
                }
                var cur = inst.Current;
                int done = inst.Steps.Count(x => x.State != StepState.Pending && x.State != StepState.Running);
                now.Text = stage ?? (cur != null ? "Instalando " + cur.App.Name + " (" + (done + 1) + " de " + inst.Steps.Count + ")\n" + inst.Line : "Terminando…");
                if (thread.IsAlive) return;
                busy = false;
                Finish(inst, list);
            });
        }

        void Finish(Installer inst, List<Tweak> list)
        {
            var drivers = Checks.MissingDrivers();
            string path = null;
            try { path = Report.Save(Report.Page(inst, list, drivers, oldPrograms, new AppInfoHolder { Info = Program.Info })); }
            catch (Exception ex) { Program.LogCrash(ex); }

            var wu = list.FirstOrDefault(t => t.Key == "update");
            if (wu != null && wu.Selected && !Tweaks.TestMode)
                try { Process.Start("ms-settings:windowsupdate"); } catch { }

            string detail, color;
            string verdict = Report.Verdict(inst, list, out detail, out color);
            Header(verdict, detail, ColorTranslator.FromHtml(color));

            var layout = new TableLayoutPanel { ColumnCount = 1, RowCount = 3, Padding = new Padding(Ui.D(14), Ui.D(10), Ui.D(14), Ui.D(6)) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(Ui.Text("Resultado", Ui.H2), 0, 0);

            var view = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.FixedSingle };
            view.Columns.Add("Elemento", Ui.D(300));
            view.Columns.Add("Estado", Ui.D(200));
            view.Columns.Add("Detalle", Ui.D(560));
            foreach (var st in inst.Steps.OrderBy(x => x.State == StepState.Failed ? 0 : x.State == StepState.Pending || x.State == StepState.Skipped ? 1 : 2))
            {
                var it = new ListViewItem(st.App.Name);
                it.SubItems.Add(Report.StateWord(st.State));
                it.SubItems.Add(st.Detail);
                if (st.State == StepState.Failed) it.ForeColor = Color.FromArgb(0xC6, 0x28, 0x28);
                view.Items.Add(it);
            }
            foreach (var t in list.Where(x => x.Selected))
            {
                var it = new ListViewItem(t.Label);
                it.SubItems.Add(t.Failed ? "Falló" : t.Done ? "Hecho" : "Sin aplicar");
                it.SubItems.Add(t.Detail);
                if (t.Failed) it.ForeColor = Color.FromArgb(0xC6, 0x28, 0x28);
                view.Items.Add(it);
            }
            foreach (var d in drivers)
            {
                var it = new ListViewItem(d);
                it.SubItems.Add("Sin controlador");
                it.SubItems.Add("Búscalo en Windows Update o en la web del fabricante.");
                view.Items.Add(it);
            }
            layout.Controls.Add(view, 0, 1);

            var bar = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            bar.Controls.Add(Ui.Btn("Volver al inicio", false, (s, e) => ShowSetup()));
            if (inst.Steps.Any(x => x.State == StepState.Failed || x.State == StepState.Skipped || x.State == StepState.Pending))
                bar.Controls.Add(Ui.Btn("Reintentar lo que falta", true, (s, e) =>
                {
                    inst.Cancel = false;
                    RunAll(inst, list);
                }));
            if (path != null)
                bar.Controls.Add(Ui.Btn("Abrir informe", false, (s, e) => Open(path)));
            if (inst.Steps.Any(x => x.State == StepState.Reboot) || list.Any(t => t.Key == "nombre" && t.Done))
                bar.Controls.Add(Ui.Btn("Reiniciar ahora", false, (s, e) =>
                {
                    if (MessageBox.Show(this, "El equipo se reiniciará en 10 segundos. Guarda lo que tengas abierto.\n\n¿Reiniciar?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
                        Open2("shutdown", "/r /t 10");
                }));
            layout.Controls.Add(bar, 0, 2);
            Page(layout);
        }

        void Open(string path)
        {
            try { Process.Start(path); }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo abrir: " + ex.Message + "\n\n" + path, Text); }
        }

        void Open2(string file, string args)
        {
            try { Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true }); }
            catch (Exception ex) { MessageBox.Show(this, "No se pudo: " + ex.Message, Text); }
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
