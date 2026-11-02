using System;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyCompany("Julio Gutiérrez")]
[assembly: System.Reflection.AssemblyCopyright("Julio Gutiérrez")]

namespace Comun
{
    // Compartido por todos los programas de la suite: el autor y el enlace se cambian solo aquí.
    public static class Credits
    {
        public const string Author = "Julio Gutiérrez";
        public const string GitHub = "https://github.com/AndresGb-png/kit-tecnico-windows";

        public static string HtmlFooter(AppInfo app)
        {
            return "<footer style='color:#46535d;font-size:13px;margin-top:28px;max-width:72ch'>" +
                   WebUtility.HtmlEncode(app.Name + " " + app.Version) + ", creado por " + WebUtility.HtmlEncode(Author) +
                   ". Más programas: <a href='" + WebUtility.HtmlEncode(GitHub) + "'>repositorio en GitHub</a>.</footer>";
        }
    }

    public static class WindowFit
    {
        // Windows puede ignorar el centrado y escalonar las ventanas, dejando los botones fuera de la pantalla.
        public static void Apply(Form form)
        {
            form.Load += (s, e) =>
            {
                if (form.WindowState != FormWindowState.Normal) return;
                var area = Screen.FromPoint(Cursor.Position).WorkingArea;
                if (form.MinimumSize.Width > area.Width || form.MinimumSize.Height > area.Height) form.MinimumSize = Size.Empty;
                form.Size = new Size(Math.Min(form.Width, area.Width), Math.Min(form.Height, area.Height));
                form.Location = new Point(area.X + (area.Width - form.Width) / 2, area.Y + (area.Height - form.Height) / 2);
            };
        }
    }

    public class AppInfo
    {
        public string Name, Version, Description, Privacy;
        public string[] Uses;
    }

    public class CreditsPanel : FlowLayoutPanel
    {
        public CreditsPanel(AppInfo app, Color ink, Color muted)
        {
            float scale;
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;
            Func<int, int> D = v => (int)Math.Round(v * scale);
            FlowDirection = FlowDirection.TopDown;
            WrapContents = false;
            AutoScroll = true;
            BackColor = Color.White;
            Padding = new Padding(D(20), D(12), D(20), D(16));

            Action<string, string, Color, float, int> add = (text, family, color, size, top) =>
            {
                Controls.Add(new Label
                {
                    Text = text, AutoSize = true, ForeColor = color, Font = new Font(family, size),
                    MaximumSize = new Size(D(680), 0), Margin = new Padding(0, D(top), 0, D(2)), UseMnemonic = false
                });
            };
            const string Regular = "Segoe UI", Strong = "Segoe UI Semibold";

            add("Creado por", Regular, muted, 10f, 0);
            add(Credits.Author, Strong, ink, 20f, 0);

            add("Más programas", Strong, ink, 12f, 18);
            add("En este repositorio de GitHub encontrarás los demás programas de la colección.", Regular, ink, 10f, 0);
            var link = new LinkLabel { Text = Credits.GitHub, AutoSize = true, Font = new Font(Regular, 10f), Margin = new Padding(0, D(2), 0, D(2)), UseMnemonic = false };
            link.LinkClicked += (s, e) =>
            {
                try { Process.Start(Credits.GitHub); }
                catch { MessageBox.Show("No se pudo abrir el navegador. Copia esta dirección:\n\n" + Credits.GitHub, app.Name); }
            };
            Controls.Add(link);

            add("Sobre este programa", Strong, ink, 12f, 18);
            add(app.Name + ", versión " + app.Version + ".", Regular, ink, 10f, 0);
            add(app.Description, Regular, ink, 10f, 2);
            add("Se ejecuta sobre .NET Framework " + Environment.Version + " en " + Environment.OSVersion.VersionString + " de " + (Environment.Is64BitOperatingSystem ? "64" : "32") + " bits.", Regular, muted, 10f, 6);

            add("Qué utiliza", Strong, ink, 12f, 18);
            foreach (var u in app.Uses) add("•  " + u, Regular, ink, 10f, 1);

            add("Tu información", Strong, ink, 12f, 18);
            add(app.Privacy, Regular, ink, 10f, 0);
        }
    }
}
