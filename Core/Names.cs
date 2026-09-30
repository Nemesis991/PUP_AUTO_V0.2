namespace PUP_AUTO.Core
{
    /// <summary>File and folder names, relative to the drawing folder. Values are part of the output contract.</summary>
    public static class FileNames
    {
        public const string LogFile = "PUP_AUTO_Logs.txt";
        public const string TestFilesFolder = "_TestFiles";
        public const string TemplatesFolder = "_Templates";
        public const string CadLibraryFile = "TemplateC.cad";
        public const string ReportXlsFile = "PUP_Report.xls";
        public const string MvpMathTestFile = "MVP_Math_Test_Parcels.xlsx";
        public const string PoleStepsFile = "Стъпки_на_стълбове.xlsx";
        public const string CadControlReportFile = "Контролна_справка_cad.xlsx";
        public const string NomenclaturesFolder = "Номенклатури";
        public const string EkatteRegisterFile = "EKATTE.csv";
    }

    /// <summary>Parcel XData: registered application name and the ID sentinels returned when it cannot be read.</summary>
    public static class XDataNames
    {
        public const string RegApp = "TransCAD";
        public const string UnknownParcel = "Неизвестен_Имот";
        public const string XDataError = "Грешка_XData";
    }

    /// <summary>Attribute tags and dynamic-block property names read from pole blocks.</summary>
    public static class PoleAttributeTags
    {
        /// <summary>Tags (upper-case) that carry the pole number.</summary>
        public static readonly string[] PoleNumberTags = { "НОМЕР_НА_СТЪЛБА", "СТЪЛБ_№", "NOMER" };

        /// <summary>The four footprint corner tags, in order.</summary>
        public static readonly string[] PointTags = { "P1", "P2", "P3", "P4" };

        /// <summary>Dynamic-block property names that hold the active visibility state.</summary>
        public static readonly string[] VisibilityPropertyNames = { "Visibility", "Visibility1", "Видимост", "Видимост1" };

        /// <summary>Upper-cases a tag the way all tag comparisons expect (culture independent).</summary>
        public static string Normalize(string tag) => tag.ToUpperInvariant();

        /// <summary>True if the tag (any case) is one of <see cref="PoleNumberTags"/>.</summary>
        public static bool IsPoleNumberTag(string tag)
        {
            string normalized = Normalize(tag);
            foreach (string known in PoleNumberTags)
            {
                if (normalized == known) return true;
            }
            return false;
        }

        /// <summary>True if the property name is one of <see cref="VisibilityPropertyNames"/> (case-insensitive).</summary>
        public static bool IsVisibilityProperty(string propertyName)
        {
            foreach (string known in VisibilityPropertyNames)
            {
                if (propertyName.Equals(known, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
