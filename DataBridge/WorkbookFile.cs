using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using PUP_AUTO.Core;

namespace PUP_AUTO.DataBridge
{
    /// <summary>Creates the xlsx files of the reports; a file open in Excel becomes a <see cref="FileInUseException"/>.</summary>
    internal static class WorkbookFile
    {
        public static SpreadsheetDocument Create(string filePath)
        {
            try
            {
                return SpreadsheetDocument.Create(filePath, SpreadsheetDocumentType.Workbook);
            }
            catch (IOException ex) when (FileInUseException.IsSharingViolation(ex.HResult))
            {
                throw new FileInUseException(filePath, ex);
            }
        }
    }
}
