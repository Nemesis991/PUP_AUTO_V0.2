using DocumentFormat.OpenXml.Spreadsheet;

namespace PUP_AUTO.DataBridge
{
    /// <summary>Small OpenXML spreadsheet cell helpers shared by the Excel exporters.</summary>
    internal static class SheetCells
    {
        /// <summary>An inline-string cell; the style attribute is only written when a style is given.</summary>
        public static Cell InlineString(string text, uint? styleIndex = null)
        {
            var cell = new Cell(new InlineString(new Text(text))) { DataType = CellValues.InlineString };
            if (styleIndex.HasValue) cell.StyleIndex = styleIndex.Value;
            return cell;
        }
    }
}
