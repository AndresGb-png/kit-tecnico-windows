using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FichaServicio
{
    // PDF propio, sin librerías: usa las fuentes base de cualquier lector y codificación WinAnsi.
    public class Pdf
    {
        static readonly int[] Regular = { 278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, 1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556, 333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584 };
        static readonly int[] BoldW = { 278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611, 975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556, 333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584 };
        static readonly Encoding Win1252 = Encoding.GetEncoding(1252, new EncoderReplacementFallback("?"), new DecoderReplacementFallback("?"));

        public readonly float W, H;
        public const float Margin = 46;
        readonly List<StringBuilder> pages = new List<StringBuilder>();
        StringBuilder cur;
        byte[] jpeg;
        int jpegW, jpegH;
        public float Y;
        public Action<Pdf> OnNewPage;

        public Pdf(bool letter)
        {
            W = letter ? 612 : 595.28f;
            H = letter ? 792 : 841.89f;
            NewPage();
        }

        public int PageCount { get { return pages.Count; } }
        public float Right { get { return W - Margin; } }
        public float Bottom { get { return Margin + 26; } }

        public void NewPage()
        {
            cur = new StringBuilder();
            pages.Add(cur);
            Y = H - Margin;
            if (OnNewPage != null) OnNewPage(this);
        }

        public void Need(float height)
        {
            if (Y - height < Bottom) NewPage();
        }

        static string N(float v) { return v.ToString("0.##", CultureInfo.InvariantCulture); }

        static string C(Color c) { return N(c.R / 255f) + " " + N(c.G / 255f) + " " + N(c.B / 255f); }

        public static float Width(string text, float size, bool bold)
        {
            var table = bold ? BoldW : Regular;
            double total = 0;
            foreach (char ch in text ?? "")
            {
                char c = ch;
                if (char.IsLowSurrogate(c)) continue;
                if (char.IsHighSurrogate(c)) c = '?';   // emoji: las fuentes base no lo tienen
                else if (c > 126)
                {
                    string d;
                    try { d = c.ToString().Normalize(NormalizationForm.FormD); }
                    catch (ArgumentException) { d = "?"; }
                    c = d.Length > 0 ? d[0] : '?';
                }
                total += c >= 32 && c <= 126 ? table[c - 32] : 556;
            }
            return (float)(total * size / 1000);
        }

        static string Escape(string text)
        {
            var sb = new StringBuilder();
            foreach (byte b in Win1252.GetBytes(text ?? ""))
            {
                if (b == (byte)'(' || b == (byte)')' || b == (byte)'\\') sb.Append('\\').Append((char)b);
                else if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                else sb.Append((char)b);
            }
            return sb.ToString();
        }

        public void Text(string text, float x, float y, float size, bool bold, Color color)
        {
            cur.Append("BT /" + (bold ? "F2 " : "F1 ") + N(size) + " Tf " + C(color) + " rg " + N(x) + " " + N(y) + " Td (" + Escape(text) + ") Tj ET\n");
        }

        public void TextRight(string text, float right, float y, float size, bool bold, Color color)
        {
            Text(text, right - Width(text, size, bold), y, size, bold, color);
        }

        public void Rect(float x, float y, float w, float h, Color fill)
        {
            cur.Append(C(fill) + " rg " + N(x) + " " + N(y) + " " + N(w) + " " + N(h) + " re f\n");
        }

        public void Line(float x1, float y1, float x2, float y2, Color color, float width = 0.6f)
        {
            cur.Append(C(color) + " RG " + N(width) + " w " + N(x1) + " " + N(y1) + " m " + N(x2) + " " + N(y2) + " l S\n");
        }

        // Parte por palabras; una palabra más larga que la línea (serie, enlace) se corta por caracteres.
        public static List<string> Wrap(string text, float size, bool bold, float width)
        {
            var lines = new List<string>();
            foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (var raw in para.Split(' '))
                {
                    string word = raw;
                    while (Width(word, size, bold) > width && word.Length > 1)
                    {
                        if (line.Length > 0) { lines.Add(line); line = ""; }
                        int n = word.Length - 1;
                        while (n > 1 && Width(word.Substring(0, n), size, bold) > width) n--;
                        lines.Add(word.Substring(0, n));
                        word = word.Substring(n);
                    }
                    string test = line.Length == 0 ? word : line + " " + word;
                    if (Width(test, size, bold) <= width) line = test;
                    else { lines.Add(line); line = word; }
                }
                lines.Add(line);
            }
            return lines;
        }

        public void Paragraph(string text, float x, float width, float size, bool bold, Color color, float leading = 0)
        {
            if (leading <= 0) leading = size * 1.35f;
            foreach (var l in Wrap(text, size, bold, width))
            {
                Need(leading);
                Y -= leading;
                Text(l, x, Y + leading * 0.24f, size, bold, color);
            }
        }

        public bool SetLogo(string path)
        {
            try
            {
                using (var img = Image.FromFile(path))
                using (var flat = new Bitmap(img.Width, img.Height))
                using (var g = Graphics.FromImage(flat))
                using (var ms = new MemoryStream())
                {
                    g.Clear(Color.White);
                    g.DrawImage(img, 0, 0, img.Width, img.Height);
                    flat.Save(ms, ImageFormat.Jpeg);
                    jpeg = ms.ToArray();
                    jpegW = img.Width;
                    jpegH = img.Height;
                    return true;
                }
            }
            catch { jpeg = null; return false; }
        }

        public bool HasLogo { get { return jpeg != null; } }

        public float DrawLogo(float x, float top, float maxW, float maxH)
        {
            if (jpeg == null) return 0;
            float k = Math.Min(maxW / jpegW, maxH / jpegH);
            float w = jpegW * k, h = jpegH * k;
            cur.Append("q " + N(w) + " 0 0 " + N(h) + " " + N(x) + " " + N(top - h) + " cm /Im1 Do Q\n");
            return w;
        }

        public void Footer(Func<int, int, string> leftText, Color color)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                cur = pages[i];
                string page = "Página " + (i + 1) + " de " + pages.Count;
                Line(Margin, Margin + 12, Right, Margin + 12, Color.FromArgb(0xD5, 0xDB, 0xE0));
                Text(leftText(i + 1, pages.Count), Margin, Margin, 7.5f, false, color);
                TextRight(page, Right, Margin, 7.5f, false, color);
            }
        }

        public void Save(string path, string title)
        {
            var objects = new List<byte[]>();
            int pageCount = pages.Count;
            int fontA = 3, fontB = 4, image = 5, firstPage = 6;
            Func<string, byte[]> latin = s => Encoding.GetEncoding(28591).GetBytes(s);

            objects.Add(latin("<< /Type /Catalog /Pages 2 0 R >>"));
            var kids = string.Join(" ", Enumerable.Range(0, pageCount).Select(i => (firstPage + i * 2) + " 0 R"));
            objects.Add(latin("<< /Type /Pages /Kids [" + kids + "] /Count " + pageCount + " >>"));
            objects.Add(latin("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
            objects.Add(latin("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
            if (jpeg != null)
            {
                using (var ms = new MemoryStream())
                {
                    var head = latin("<< /Type /XObject /Subtype /Image /Width " + jpegW + " /Height " + jpegH + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpeg.Length + " >>\nstream\n");
                    ms.Write(head, 0, head.Length);
                    ms.Write(jpeg, 0, jpeg.Length);
                    var tail = latin("\nendstream");
                    ms.Write(tail, 0, tail.Length);
                    objects.Add(ms.ToArray());
                }
            }
            else objects.Add(latin("<< >>"));

            string resources = "<< /Font << /F1 " + fontA + " 0 R /F2 " + fontB + " 0 R >>" + (jpeg != null ? " /XObject << /Im1 " + image + " 0 R >>" : "") + " >>";
            for (int i = 0; i < pageCount; i++)
            {
                objects.Add(latin("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + N(W) + " " + N(H) + "] /Resources " + resources + " /Contents " + (firstPage + i * 2 + 1) + " 0 R >>"));
                var content = latin(pages[i].ToString());
                using (var ms = new MemoryStream())
                {
                    var head = latin("<< /Length " + content.Length + " >>\nstream\n");
                    ms.Write(head, 0, head.Length);
                    ms.Write(content, 0, content.Length);
                    var tail = latin("\nendstream");
                    ms.Write(tail, 0, tail.Length);
                    objects.Add(ms.ToArray());
                }
            }
            // El título va en UTF-16 para conservar acentos en cualquier lector.
            var titleHex = "FEFF" + string.Concat(Encoding.BigEndianUnicode.GetBytes(title ?? "").Select(b => b.ToString("X2")));
            objects.Add(latin("<< /Title <" + titleHex + "> /Producer (Ficha de servicio) >>"));
            int info = objects.Count;

            string temp = path + ".tmp";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
            {
                Action<byte[]> put = b => fs.Write(b, 0, b.Length);
                put(latin("%PDF-1.4\n%âãÏÓ\n"));
                var offsets = new List<long>();
                for (int i = 0; i < objects.Count; i++)
                {
                    offsets.Add(fs.Position);
                    put(latin((i + 1) + " 0 obj\n"));
                    put(objects[i]);
                    put(latin("\nendobj\n"));
                }
                long xref = fs.Position;
                put(latin("xref\n0 " + (objects.Count + 1) + "\n0000000000 65535 f \n"));
                foreach (var o in offsets) put(latin(o.ToString("0000000000") + " 00000 n \n"));
                put(latin("trailer\n<< /Size " + (objects.Count + 1) + " /Root 1 0 R /Info " + info + " 0 R >>\nstartxref\n" + xref + "\n%%EOF\n"));
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }
    }

    public static class Docs
    {
        static readonly Color Ink = Color.FromArgb(0x1E, 0x2A, 0x32), Muted = Color.FromArgb(0x54, 0x62, 0x6D), Slate = Color.FromArgb(0x2B, 0x4A, 0x5E),
            Line = Color.FromArgb(0xD5, 0xDB, 0xE0), Soft = Color.FromArgb(0xEE, 0xF1, 0xF3);

        static void SectionTitle(Pdf p, string text)
        {
            p.Need(44);
            p.Y -= 20;
            p.Text(text, Pdf.Margin, p.Y, 11, true, Slate);
            p.Y -= 5;
            p.Line(Pdf.Margin, p.Y, p.Right, p.Y, Slate, 0.9f);
            p.Y -= 2;
        }

        static void Field(Pdf p, string label, string value, float x, float width)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            p.Need(28);
            p.Y -= 11;
            p.Text(label, x, p.Y, 7.5f, false, Muted);
            p.Y -= 1.5f;
            p.Paragraph(value, x, width, 9.5f, false, Ink);
            p.Y -= 2;
        }

        static void TwoFields(Pdf p, params string[] labelsAndValues)
        {
            float gap = 16, col = (p.Right - Pdf.Margin - gap) / 2;
            var items = new List<KeyValuePair<string, string>>();
            for (int i = 0; i + 1 < labelsAndValues.Length; i += 2)
                if (!string.IsNullOrWhiteSpace(labelsAndValues[i + 1]))
                    items.Add(new KeyValuePair<string, string>(labelsAndValues[i], labelsAndValues[i + 1]));
            for (int i = 0; i < items.Count; i += 2)
            {
                float top = p.Y;
                int pageBefore = p.PageCount;
                Field(p, items[i].Key, items[i].Value, Pdf.Margin, col);
                float endA = p.Y;
                if (i + 1 < items.Count && p.PageCount == pageBefore)
                {
                    p.Y = top;
                    Field(p, items[i + 1].Key, items[i + 1].Value, Pdf.Margin + col + gap, col);
                    if (p.PageCount == pageBefore) p.Y = Math.Min(p.Y, endA);
                }
                else if (i + 1 < items.Count)
                    Field(p, items[i + 1].Key, items[i + 1].Value, Pdf.Margin + col + gap, col);
            }
        }

        static void Head(Pdf p, Ficha x, Settings s, string title)
        {
            float top = p.Y, left = Pdf.Margin;
            float logoW = p.DrawLogo(left, top, 90, 50);
            if (logoW > 0) left += logoW + 12;
            float ly = top - 13;
            string name = s.BusinessName.Length > 0 ? s.BusinessName : s.TechnicianName;
            float rightColumn = 190;
            float maxName = p.Right - rightColumn - left;
            if (name.Length > 0)
                foreach (var l in Pdf.Wrap(name, 14, true, maxName).Take(2)) { p.Text(l, left, ly, 14, true, Ink); ly -= 16; }
            var contact = new[] { s.BusinessName.Length > 0 ? s.TechnicianName : "", s.BusinessId, s.Phone, s.Email, s.Address }.Where(v => !string.IsNullOrWhiteSpace(v));
            foreach (var c in contact)
                foreach (var l in Pdf.Wrap(c, 8.5f, false, maxName).Take(2)) { p.Text(l, left, ly, 8.5f, false, Muted); ly -= 11; }

            p.TextRight(title, p.Right, top - 13, 12, true, Slate);
            p.TextRight("Ficha " + x.Number, p.Right, top - 29, 10, true, Ink);
            p.TextRight("Fecha: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"), p.Right, top - 42, 8.5f, false, Muted);
            p.Y = Math.Min(ly, top - 54) - 4;
            p.Line(Pdf.Margin, p.Y, p.Right, p.Y, Line);
        }

        static void ClientAndDevice(Pdf p, Ficha x)
        {
            SectionTitle(p, "Cliente");
            TwoFields(p, "Nombre", x.ClientName, "Teléfono", x.ClientPhone, "Documento", x.ClientDocument, "Correo", x.ClientEmail, "Dirección", x.ClientAddress);
            SectionTitle(p, "Equipo");
            TwoFields(p, "Tipo", x.DeviceType, "Marca y modelo", x.DeviceBrandModel, "Número de serie", x.DeviceSerial, "Recibido el", x.ReceivedOn);
            Field(p, "Características", x.DeviceSpecs, Pdf.Margin, p.Right - Pdf.Margin);
        }

        static void Signatures(Pdf p, Ficha x, Settings s)
        {
            p.Need(80);
            p.Y -= 58;
            float gap = 40, col = (p.Right - Pdf.Margin - gap) / 2;
            p.Line(Pdf.Margin, p.Y, Pdf.Margin + col, p.Y, Ink);
            p.Line(Pdf.Margin + col + gap, p.Y, p.Right, p.Y, Ink);
            p.Y -= 11;
            p.Text("Firma del cliente" + (x.ClientName.Length > 0 && x.ClientName.Length < 40 ? ": " + x.ClientName : ""), Pdf.Margin, p.Y, 8, false, Muted);
            p.Text("Técnico" + (s.TechnicianName.Length > 0 && s.TechnicianName.Length < 40 ? ": " + s.TechnicianName : ""), Pdf.Margin + col + gap, p.Y, 8, false, Muted);
        }

        static void Finish(Pdf p, Ficha x, string path, string title)
        {
            p.Footer((a, b) => "Ficha " + x.Number + ". Generado con Ficha de servicio, de " + Comun.Credits.Author, Muted);
            p.Save(path, title);
        }

        static Pdf Start(Settings s, string root)
        {
            var p = new Pdf(!s.PageSize.Equals("A4", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(s.LogoFile))
            {
                string logo = Path.IsPathRooted(s.LogoFile) ? s.LogoFile : Path.Combine(root, s.LogoFile);
                if (File.Exists(logo)) p.SetLogo(logo);
            }
            return p;
        }

        public static void Reception(Ficha x, Settings s, string root, string path)
        {
            var p = Start(s, root);
            Head(p, x, s, "Comprobante de recepción");
            ClientAndDevice(p, x);
            SectionTitle(p, "Lo que se recibe");
            float full = p.Right - Pdf.Margin;
            Field(p, "Problema que informa el cliente", x.Problem, Pdf.Margin, full);
            Field(p, "Accesorios entregados", x.Accessories, Pdf.Margin, full);
            Field(p, "Estado físico al recibirlo", x.Condition, Pdf.Margin, full);
            Field(p, "Abono recibido", x.Advance > 0 ? Money.Show(x.Advance, s) : "", Pdf.Margin, full);
            if (!string.IsNullOrWhiteSpace(s.ReceptionTerms))
            {
                SectionTitle(p, "Condiciones");
                p.Paragraph(s.ReceptionTerms, Pdf.Margin, full, 8.5f, false, Muted);
            }
            Signatures(p, x, s);
            Finish(p, x, path, "Comprobante de recepción " + x.Number);
        }

        public static void Delivery(Ficha x, Settings s, string root, string path, List<CompareRow> compare, string before, string after)
        {
            var p = Start(s, root);
            Head(p, x, s, "Comprobante de servicio");
            ClientAndDevice(p, x);
            float full = p.Right - Pdf.Margin;
            if (!string.IsNullOrWhiteSpace(x.Problem + x.Diagnosis + x.WorkDone + x.Notes)) SectionTitle(p, "Servicio realizado");
            Field(p, "Problema que informó el cliente", x.Problem, Pdf.Margin, full);
            Field(p, "Diagnóstico", x.Diagnosis, Pdf.Margin, full);
            Field(p, "Trabajo realizado", x.WorkDone, Pdf.Margin, full);
            Field(p, "Observaciones", x.Notes, Pdf.Margin, full);

            if (before != null || after != null)
            {
                SectionTitle(p, "Diagnóstico antes y después");
                TwoFields(p, "Al recibirlo", before, "Al entregarlo", after);
                if (compare != null && compare.Count > 0) CompareTable(p, compare);
                else if (before != null && after != null) p.Paragraph("Sin cambios entre las dos revisiones.", Pdf.Margin, full, 9, false, Muted);
            }
            Costs(p, x, s);
            SectionTitle(p, "Garantía");
            string warranty = x.WarrantyDays > 0
                ? x.WarrantyDays + " días" + (x.WarrantyEnd != null ? ", hasta el " + x.WarrantyEnd.Value.ToString("yyyy-MM-dd") + "." : ", desde la entrega.")
                : "Este servicio no incluye garantía.";
            p.Paragraph(warranty, Pdf.Margin, full, 9.5f, true, Ink);
            if (!string.IsNullOrWhiteSpace(s.DeliveryTerms)) p.Paragraph(s.DeliveryTerms, Pdf.Margin, full, 8.5f, false, Muted);
            Signatures(p, x, s);
            Finish(p, x, path, "Comprobante de servicio " + x.Number);
        }

        static void CompareTable(Pdf p, List<CompareRow> rows)
        {
            float full = p.Right - Pdf.Margin;
            float[] w = { full * 0.46f, full * 0.19f, full * 0.19f, full * 0.16f };
            Action head = () =>
            {
                p.Need(18);
                p.Y -= 15;
                p.Rect(Pdf.Margin, p.Y - 3, full, 14, Soft);
                float cx = Pdf.Margin + 4;
                string[] t = { "Revisión", "Antes", "Después", "Resultado" };
                for (int i = 0; i < 4; i++) { p.Text(t[i], cx, p.Y + 1, 8, true, Ink); cx += w[i]; }
                p.Y -= 3;
            };
            head();
            foreach (var r in rows)
            {
                var lines = Pdf.Wrap(r.Title, 8.5f, false, w[0] - 8);
                float h = lines.Count * 11.5f + 4;
                if (p.Y - h < p.Bottom) { p.NewPage(); head(); }
                float y = p.Y - 11;
                foreach (var l in lines) { p.Text(l, Pdf.Margin + 4, y, 8.5f, false, Ink); y -= 11.5f; }
                float cx = Pdf.Margin + 4 + w[0];
                p.Text(r.Before, cx, p.Y - 11, 8.5f, false, Muted); cx += w[1];
                p.Text(r.After, cx, p.Y - 11, 8.5f, false, Ink); cx += w[2];
                p.Text(r.Change, cx, p.Y - 11, 8.5f, true, r.Change == "Mejoró" ? Color.FromArgb(0x1B, 0x7F, 0x4B) : r.Change == "Empeoró" || r.Change == "Nuevo" ? Color.FromArgb(0xC6, 0x28, 0x28) : Ink);
                p.Y -= h;
                p.Line(Pdf.Margin, p.Y, p.Right, p.Y, Line, 0.4f);
            }
        }

        static void Costs(Pdf p, Ficha x, Settings s)
        {
            SectionTitle(p, "Costos");
            float full = p.Right - Pdf.Margin;
            float qty = 46, unit = 86, total = 92, desc = full - qty - unit - total;
            Action head = () =>
            {
                p.Need(18);
                p.Y -= 15;
                p.Rect(Pdf.Margin, p.Y - 3, full, 14, Soft);
                p.Text("Descripción", Pdf.Margin + 4, p.Y + 1, 8, true, Ink);
                p.TextRight("Cant.", Pdf.Margin + desc + qty - 4, p.Y + 1, 8, true, Ink);
                p.TextRight("Precio", Pdf.Margin + desc + qty + unit - 4, p.Y + 1, 8, true, Ink);
                p.TextRight("Total", p.Right - 4, p.Y + 1, 8, true, Ink);
                p.Y -= 3;
            };
            if (x.Items.Count > 0) head();
            foreach (var i in x.Items)
            {
                var lines = Pdf.Wrap(i.Description, 9, false, desc - 8);
                float h = lines.Count * 12 + 4;
                if (p.Y - h < p.Bottom) { p.NewPage(); head(); }
                float y = p.Y - 11;
                foreach (var l in lines) { p.Text(l, Pdf.Margin + 4, y, 9, false, Ink); y -= 12; }
                p.TextRight(i.Quantity.ToString("0.##", CultureInfo.CurrentCulture), Pdf.Margin + desc + qty - 4, p.Y - 11, 9, false, Ink);
                p.TextRight(Money.Plain(i.UnitPrice, s), Pdf.Margin + desc + qty + unit - 4, p.Y - 11, 9, false, Ink);
                p.TextRight(Money.Plain(i.Total, s), p.Right - 4, p.Y - 11, 9, false, Ink);
                p.Y -= h;
                p.Line(Pdf.Margin, p.Y, p.Right, p.Y, Line, 0.4f);
            }
            Action<string, decimal, bool> row = (label, value, strong) =>
            {
                p.Need(16);
                p.Y -= 14;
                p.TextRight(label, p.Right - 100, p.Y, strong ? 10.5f : 9, strong, Ink);
                p.TextRight(Money.Show(value, s), p.Right - 4, p.Y, strong ? 10.5f : 9, strong, Ink);
            };
            // El bloque de totales no se parte entre páginas.
            int rows = 1 + (x.Items.Count > 0 ? 1 : 0) + (x.Labor != 0 ? 1 : 0) + (x.Discount != 0 ? 1 : 0) + (x.Tax != 0 ? 1 : 0) + (x.Advance != 0 ? 2 : 0);
            p.Need(rows * 14 + 6);
            p.Y -= 3;
            if (x.Items.Count > 0) row("Repuestos y otros", x.PartsTotal, false);
            if (x.Labor != 0) row("Mano de obra", x.Labor, false);
            if (x.Discount != 0) row("Descuento", -x.Discount, false);
            if (x.Tax != 0) row("Impuesto (" + x.TaxPercent.ToString("0.##", CultureInfo.CurrentCulture) + "%)", x.Tax, false);
            row("Total", x.Total, true);
            if (x.Advance != 0)
            {
                row("Abono", -x.Advance, false);
                row(x.Balance < 0 ? "Saldo a favor del cliente" : "Saldo pendiente", Math.Abs(x.Balance), true);
            }
        }
    }
}
