using System.Text;
using BaselineDiff;

// Compares "before" vs "after" outputs of PUP_AUTO for the refactor baseline.
// Exit code: 0 = identical, 1 = differences found, 2 = usage / IO error.

const string Usage =
    "usage:\n" +
    "  BaselineDiff excel  <before.xls|xlsx> <after.xls|xlsx> [--formats]\n" +
    "  BaselineDiff word   <before.docm|docx> <after.docm|docx>\n" +
    "  BaselineDiff dxf    <before.dxf> <after.dxf>\n" +
    "  BaselineDiff folder <before-dir> <after-dir> [--formats]\n";

// Layer names, headers and cell text are Cyrillic.
Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 3)
{
    Console.Error.Write(Usage);
    return 2;
}

string mode = args[0].ToLowerInvariant();
string before = args[1], after = args[2];
bool formats = args.Skip(3).Contains("--formats");

try
{
    var report = new DiffReport(Console.Out);
    switch (mode)
    {
        case "excel":  ExcelDiff.Compare(before, after, formats, report); break;
        case "word":   WordDiff.Compare(before, after, report); break;
        case "dxf":    DxfDiff.Compare(before, after, report); break;
        case "folder": FolderDiff.Compare(before, after, formats, report); break;
        default:
            Console.Error.Write(Usage);
            return 2;
    }

    Console.WriteLine(report.Count == 0 ? "IDENTICAL" : $"{report.Count} difference(s)");
    return report.Count == 0 ? 0 : 1;
}
catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}
