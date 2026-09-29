using System.Globalization;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

namespace BaselineDiff
{
    /// <summary>
    /// Cell-by-cell comparison of two workbooks (.xls or .xlsx): sheet count and names,
    /// cell type + value (header text included), merged regions and, optionally,
    /// number-format strings. A missing cell and a blank cell are treated as equal.
    /// </summary>
    public static class ExcelDiff
    {
        public static void Compare(string beforePath, string afterPath, bool formats, DiffReport report)
        {
            IWorkbook a = Open(beforePath), b = Open(afterPath);

            if (a.NumberOfSheets != b.NumberOfSheets)
                report.Add("sheet count", a.NumberOfSheets.ToString(), b.NumberOfSheets.ToString());

            for (int s = 0; s < Math.Min(a.NumberOfSheets, b.NumberOfSheets); s++)
            {
                ISheet sa = a.GetSheetAt(s), sb = b.GetSheetAt(s);
                if (sa.SheetName != sb.SheetName)
                    report.Add($"sheet {s} name", sa.SheetName, sb.SheetName);

                int lastRow = Math.Max(sa.LastRowNum, sb.LastRowNum);
                for (int r = 0; r <= lastRow; r++)
                {
                    IRow? ra = sa.GetRow(r), rb = sb.GetRow(r);
                    int lastCol = Math.Max(ra?.LastCellNum ?? 0, rb?.LastCellNum ?? 0);
                    for (int c = 0; c < lastCol; c++)
                    {
                        string va = Describe(ra?.GetCell(c), formats);
                        string vb = Describe(rb?.GetCell(c), formats);
                        if (va != vb)
                            report.Add($"[{sa.SheetName}] {new CellReference(r, c).FormatAsString()}", va, vb);
                    }
                }

                string ma = Merged(sa), mb = Merged(sb);
                if (ma != mb)
                    report.Add($"[{sa.SheetName}] merged regions", ma, mb);
            }
        }

        private static IWorkbook Open(string path)
        {
            using var fs = File.OpenRead(path);
            return WorkbookFactory.Create(fs);
        }

        private static string Merged(ISheet sheet) => string.Join(",",
            Enumerable.Range(0, sheet.NumMergedRegions)
                .Select(i => sheet.GetMergedRegion(i).FormatAsString())
                .OrderBy(x => x, StringComparer.Ordinal));

        private static string Describe(ICell? cell, bool formats)
        {
            if (cell == null || cell.CellType == CellType.Blank) return "";

            var type = cell.CellType == CellType.Formula ? cell.CachedFormulaResultType : cell.CellType;
            string value = type switch
            {
                CellType.Numeric => "N:" + cell.NumericCellValue.ToString("R", CultureInfo.InvariantCulture),
                CellType.String  => "S:" + cell.StringCellValue,
                CellType.Boolean => "B:" + cell.BooleanCellValue,
                CellType.Error   => "E:" + cell.ErrorCellValue,
                _                => "?:" + cell
            };

            if (cell.CellType == CellType.Formula) value = "F:" + cell.CellFormula + " = " + value;
            if (formats) value += "  fmt:" + cell.CellStyle?.GetDataFormatString();
            return value;
        }
    }
}
