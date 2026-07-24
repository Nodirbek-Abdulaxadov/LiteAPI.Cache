using System.Text;
using CacheBench.Caches;

namespace CacheBench.Reporting;

public static class Charts
{
    private static readonly Dictionary<string, string> Colors = new()
    {
        ["MemoryCache"] = "#4E79A7",
        ["FusionCache"] = "#F28E2B",
        ["ActualLab.Fusion"] = "#59A14F",
        ["LiteAPI.Cache"] = "#E15759",
    };

    private static string Esc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // Horizontal bar chart, one bar per cache, scaled to the max value.
    public static string Metric(string title, Func<string, double> value, Func<double, string> label, bool lowerBetter)
    {
        string[] caches = CacheRegistry.Names;
        const int width = 720, rowH = 44, top = 70, left = 150, right = 40;
        int height = top + caches.Length * rowH + 30;
        int plotW = width - left - right;
        double max = caches.Max(value);
        if (max <= 0) max = 1;

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\" font-family=\"Segoe UI,Arial,sans-serif\">");
        sb.Append($"<rect width=\"{width}\" height=\"{height}\" fill=\"#ffffff\"/>");
        sb.Append($"<text x=\"{width / 2}\" y=\"30\" text-anchor=\"middle\" font-size=\"19\" font-weight=\"700\" fill=\"#1a1a1a\">{Esc(title)}</text>");
        sb.Append($"<text x=\"{width / 2}\" y=\"50\" text-anchor=\"middle\" font-size=\"12\" fill=\"#888\">{(lowerBetter ? "lower is better ◄" : "► higher is better")}</text>");

        for (int i = 0; i < caches.Length; i++)
        {
            string c = caches[i];
            double v = value(c);
            int y = top + i * rowH;
            int barW = (int)Math.Round(plotW * (v / max));
            if (barW < 2 && v > 0) barW = 2;
            sb.Append($"<text x=\"{left - 10}\" y=\"{y + 20}\" text-anchor=\"end\" font-size=\"13\" fill=\"#333\">{Esc(c)}</text>");
            sb.Append($"<rect x=\"{left}\" y=\"{y + 6}\" width=\"{barW}\" height=\"24\" rx=\"4\" fill=\"{Colors[c]}\"/>");
            sb.Append($"<text x=\"{left + barW + 8}\" y=\"{y + 23}\" font-size=\"13\" font-weight=\"600\" fill=\"#444\">{Esc(label(v))}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    public static string Nanos(double ns) =>
        ns >= 1_000_000 ? $"{ns / 1_000_000.0:0.00} ms"
        : ns >= 1_000 ? $"{ns / 1000.0:0.00} µs"
        : $"{ns:0} ns";

    public static string Ops(double v) =>
        v >= 1_000_000 ? $"{v / 1_000_000:0.00} M/s" : v >= 1000 ? $"{v / 1000:0.0} K/s" : $"{v:0}/s";

    public static string Bytes(double b) =>
        b >= 1_048_576 ? $"{b / 1_048_576:0.0} MB" : b >= 1024 ? $"{b / 1024:0.0} KB" : $"{b:0} B";
}
