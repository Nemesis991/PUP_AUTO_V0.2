using System.Globalization;
using System.Text;

namespace BaselineDiff
{
    /// <summary>
    /// Compares the ENTITIES section of two ASCII DXF files (DXFOUT).
    /// Handles differ between runs, so entities are compared as a multiset of signatures:
    /// type | layer | block name | text | coordinates rounded to 3 decimals.
    /// Also reports per-layer entity counts.
    /// </summary>
    public static class DxfDiff
    {
        public static void Compare(string beforePath, string afterPath, DiffReport report)
        {
            List<Entity> a = ReadEntities(beforePath), b = ReadEntities(afterPath);

            // 1. Per-layer counts
            var countsA = CountByLayer(a);
            var countsB = CountByLayer(b);
            foreach (string layer in countsA.Keys.Union(countsB.Keys).OrderBy(l => l, StringComparer.Ordinal))
            {
                countsA.TryGetValue(layer, out int ca);
                countsB.TryGetValue(layer, out int cb);
                report.Note($"layer '{layer}': {ca} -> {cb}");
                if (ca != cb) report.Add($"layer '{layer}' entity count", ca.ToString(), cb.ToString());
            }

            // 2. Entity signatures (multiset difference)
            var remaining = a.Select(e => e.Signature).GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
            var onlyAfter = new List<string>();
            foreach (var e in b)
            {
                if (remaining.TryGetValue(e.Signature, out int n) && n > 0) remaining[e.Signature] = n - 1;
                else onlyAfter.Add(e.Signature);
            }
            var onlyBefore = remaining.Where(kv => kv.Value > 0).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value));

            foreach (string s in onlyBefore.OrderBy(s => s, StringComparer.Ordinal))
                report.Add("entity only in before", s, "<none>");
            foreach (string s in onlyAfter.OrderBy(s => s, StringComparer.Ordinal))
                report.Add("entity only in after", "<none>", s);
        }

        private sealed class Entity
        {
            public string Type = "";
            public string Layer = "";
            public string Block = "";
            public string Text = "";
            public readonly List<string> Coords = new List<string>();

            public string Signature =>
                $"{Type} | layer={Layer}" +
                (Block.Length > 0 ? $" | block={Block}" : "") +
                (Text.Length > 0 ? $" | text={Text}" : "") +
                $" | {string.Join(" ", Coords)}";
        }

        private static Dictionary<string, int> CountByLayer(List<Entity> entities) =>
            entities.GroupBy(e => e.Layer).ToDictionary(g => g.Key, g => g.Count());

        private static List<Entity> ReadEntities(string path)
        {
            // AutoCAD 2007+ writes DXF as UTF-8.
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            var entities = new List<Entity>();
            bool inEntities = false;
            Entity? current = null;

            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                string code = lines[i].Trim();
                string value = lines[i + 1].Trim();

                if (!inEntities)
                {
                    if (code == "2" && value == "ENTITIES") inEntities = true;
                    continue;
                }

                if (code == "0")
                {
                    if (current != null) entities.Add(current);
                    if (value == "ENDSEC") return entities;
                    current = new Entity { Type = value };
                    continue;
                }
                if (current == null) continue;

                switch (code)
                {
                    case "8": current.Layer = value; break;
                    case "2": current.Block = value; break;
                    case "1": current.Text = value; break;
                    default:
                        // X/Y/Z of any point (10-18 / 20-28 / 30-38)
                        if (int.TryParse(code, out int gc) && gc >= 10 && gc <= 38)
                            current.Coords.Add($"{gc}:{Round3(value)}");
                        break;
                }
            }

            if (current != null) entities.Add(current);
            return entities;
        }

        private static string Round3(string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                return value;
            string s = Math.Round(d, 3, MidpointRounding.AwayFromZero).ToString("F3", CultureInfo.InvariantCulture);
            return s == "-0.000" ? "0.000" : s;
        }
    }
}
