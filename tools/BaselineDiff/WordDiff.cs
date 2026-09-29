using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace BaselineDiff
{
    /// <summary>
    /// Compares the text of every table in two Word documents, row by row.
    /// Byte comparison of .docm is useless (zip timestamps), so only table content is compared.
    /// </summary>
    public static class WordDiff
    {
        public static void Compare(string beforePath, string afterPath, DiffReport report)
        {
            List<string> a = DumpTables(beforePath), b = DumpTables(afterPath);

            if (a.Count != b.Count)
                report.Add("table line count", a.Count.ToString(), b.Count.ToString());

            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                string la = i < a.Count ? a[i] : "<none>";
                string lb = i < b.Count ? b[i] : "<none>";
                if (la != lb) report.Add($"line {i + 1}", la, lb);
            }
        }

        /// <summary>One line per table header ("== table N") and one per row ("cell | cell | …").</summary>
        public static List<string> DumpTables(string path)
        {
            var lines = new List<string>();
            using var doc = WordprocessingDocument.Open(path, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null) return lines;

            int t = 0;
            foreach (var table in body.Descendants<Table>())
            {
                lines.Add($"== table {t++}");
                foreach (var row in table.Elements<TableRow>())
                    lines.Add(string.Join(" | ", row.Elements<TableCell>().Select(c => c.InnerText)));
            }
            return lines;
        }
    }
}
