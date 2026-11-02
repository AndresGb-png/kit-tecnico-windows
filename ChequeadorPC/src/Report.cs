using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace ChequeadorPC
{
    public static class Report
    {
        static string E(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        static string Color(St s)
        {
            switch (s)
            {
                case St.Ok: return "#1b7f4b";
                case St.Warn: return "#a86200";
                case St.Bad: return "#c62828";
                case St.Unknown: return "#5f6b76";
                default: return "#2b6a8e";
            }
        }

        public static string Html(List<Section> sections, Ctx ctx)
        {
            string detail; St overall;
            string verdict = Fmt.Verdict(sections, out detail, out overall);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=es><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'>");
            sb.Append("<title>Diagnóstico de " + E(Environment.MachineName) + "</title><style>");
            sb.Append("body{margin:0;background:#eef1f3;color:#1e2a32;font:15px/1.5 'Segoe UI',system-ui,sans-serif}");
            sb.Append(".band{color:#fff;padding:24px 24px 22px}.band h1{font-size:26px;line-height:1.2;margin:0 0 6px;font-weight:600}.band p{margin:0;max-width:70ch}");
            sb.Append("main{max-width:860px;margin:0 auto;padding:8px 16px 40px}h2{font-size:18px;margin:28px 0 8px;font-weight:600}");
            sb.Append(".f{background:#fff;border-left:5px solid;padding:10px 14px;margin:0 0 6px}.f b{font-weight:600}.w{float:right;font-size:13px;font-weight:600;margin-left:12px}");
            sb.Append(".f p{margin:2px 0;max-width:72ch}.do{font-weight:500}details{margin-top:4px;font-size:13px;color:#46535d}pre{white-space:pre-wrap;word-break:break-word;margin:4px 0;font:12.5px Consolas,monospace}");
            sb.Append("footer{color:#46535d;font-size:13px;margin-top:28px;max-width:72ch}@media print{body{background:#fff}.band{-webkit-print-color-adjust:exact;print-color-adjust:exact}.f{break-inside:avoid}}");
            sb.Append("</style><div class=band style='background:" + Color(overall) + "'><h1>" + E(verdict) + "</h1><p>" + E(detail) + "</p>");
            sb.Append("<p>Equipo " + E(Environment.MachineName) + ", revisado el " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
                      (ctx.IsAdmin ? "." : ", con permisos limitados.") + "</p></div><main>");
            foreach (var s in sections)
            {
                sb.Append("<h2>" + E(s.Title) + "</h2>");
                foreach (var f in s.All().OrderByDescending(x => x.Status == St.Bad ? 3 : x.Status == St.Warn ? 2 : x.Status == St.Unknown ? 1 : 0))
                {
                    string col = Color(f.Status);
                    sb.Append("<div class=f style='border-color:" + col + "'><span class=w style='color:" + col + "'>" + E(Fmt.StatusGlyph(f.Status) + " " + Fmt.StatusWord(f.Status)) + "</span>");
                    sb.Append("<b>" + E(f.Title) + "</b><p>" + E(f.Plain) + "</p>");
                    if (!string.IsNullOrEmpty(f.Action)) sb.Append("<p class=do>Qué hacer: " + E(f.Action) + "</p>");
                    if (!string.IsNullOrEmpty(f.Tech)) sb.Append("<details><summary>Datos técnicos</summary><pre>" + E(f.Tech) + "</pre></details>");
                    sb.Append("</div>");
                }
            }
            sb.Append("<footer>Windows no puede comprobar físicamente un puerto vacío ni una ranura vacía. Lo que aparece como «Sin comprobar» o «Información» no es una falla: indica qué hay que probar a mano.</footer>" + Comun.Credits.HtmlFooter(Program.Info) + "</main></html>");
            return sb.ToString();
        }
    }
}
