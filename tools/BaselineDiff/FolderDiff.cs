namespace BaselineDiff
{
    /// <summary>
    /// Compares every file in a baseline folder with the same-named file in the "after" folder,
    /// choosing the mode by extension. Logs and notes (.txt) are listed but not compared,
    /// because they contain timestamps.
    /// </summary>
    public static class FolderDiff
    {
        public static void Compare(string beforeDir, string afterDir, bool formats, DiffReport report)
        {
            var names = Directory.GetFiles(beforeDir).Select(Path.GetFileName)
                .Union(Directory.GetFiles(afterDir).Select(Path.GetFileName))
                .OfType<string>()
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            foreach (string name in names)
            {
                string a = Path.Combine(beforeDir, name), b = Path.Combine(afterDir, name);
                if (!File.Exists(a)) { report.Add($"file {name}", "<missing>", "present"); continue; }
                if (!File.Exists(b)) { report.Add($"file {name}", "present", "<missing>"); continue; }

                string ext = Path.GetExtension(name).ToLowerInvariant();
                int before = report.Count;
                report.Note($"--- {name}");
                switch (ext)
                {
                    case ".xls": case ".xlsx": ExcelDiff.Compare(a, b, formats, report); break;
                    case ".docm": case ".docx": WordDiff.Compare(a, b, report); break;
                    case ".dxf": DxfDiff.Compare(a, b, report); break;
                    default: report.Note("    (not compared)"); continue;
                }
                report.Note(report.Count == before ? "    identical" : $"    {report.Count - before} difference(s)");
            }
        }
    }
}
