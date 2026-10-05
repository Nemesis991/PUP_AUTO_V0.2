using System.Globalization;
using System.Text;

namespace PUP_AUTO.CadRegister
{
    /// <summary>
    /// Reads ONE AGKK .cad file (format 4.02, MIK encoded) = one землище. Pure C#, no AutoCAD types.
    /// <para>
    /// Every TABLE declares its fields with "F name ..." lines and then its rows with "D v1,v2,..." lines:
    /// comma separated, one value per field in F order, strings in double quotes, empty numbers/dates
    /// as nothing between commas. The trailing comma is the (empty) last field, so a row must have
    /// exactly as many values as declared fields. Columns are always read BY FIELD NAME. Unknown sections,
    /// tables and fields are skipped. A malformed row produces a warning with its line number and parsing continues.
    /// Warnings never contain row content, so ЕГН/БУЛСТАТ and names cannot leak through them.
    /// </para>
    /// <para>Results are keyed by the full parcel ID "EKATTE.IDENT" so several files can be merged later.</para>
    /// </summary>
    public sealed class CadRegisterReader
    {
        public const string SupportedVersion = "4.02";

        private const string TablePozemlimoti = "POZEMLIMOTI";
        private const string TableMestnosti = "MESTNOSTI";
        private const string TablePrava = "PRAVA";
        private const string TablePersons = "PERSONS";
        private const string TableGorimoti = "GORIMOTI";
        private const string ControlCadaster = "CADASTER";

        private enum Section { None, Header, Layer, Control, Table }

        private sealed class RawRow
        {
            public int Line;
            public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public string Get(string field) => Values.TryGetValue(field, out string? value) ? value : string.Empty;
        }

        private sealed class RawTable
        {
            public readonly List<RawRow> Rows = new List<RawRow>();
            public readonly HashSet<string> DeclaredFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool ReportedNoFields;
        }

        private readonly Action<string> _warn;

        /// <param name="warn">Receives each warning; null discards them.</param>
        public CadRegisterReader(Action<string>? warn = null)
        {
            _warn = warn ?? (_ => { });
        }

        public CadRegisterData ReadFile(string path) => Read(File.ReadAllBytes(path));

        public CadRegisterData Read(byte[] data)
        {
            string text = MikEncoding.Decode(data, out int undecodable);
            if (undecodable > 0)
            {
                _warn($"{undecodable} байта извън азбуката на MIK (0xC0-0xFF) са заменени със символа � — проверете декодирането.");
            }

            var result = new CadRegisterData();
            var tables = new Dictionary<string, RawTable>
            {
                [TablePozemlimoti] = new RawTable(),
                [TableMestnosti] = new RawTable(),
                [TablePrava] = new RawTable(),
                [TablePersons] = new RawTable(),
                [TableGorimoti] = new RawTable()
            };
            var contourAreas = new List<KeyValuePair<string, double>>();

            Section section = Section.None;
            string sectionName = string.Empty;
            string tableName = string.Empty;
            List<string>? fields = null;
            RawTable? sink = null;

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                int lineNumber = i + 1;
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                int space = IndexOfWhitespace(line);
                string keyword = space < 0 ? line : line.Substring(0, space);
                string rest = space < 0 ? string.Empty : line.Substring(space).Trim();

                switch (keyword)
                {
                    case "HEADER":
                        section = Section.Header; fields = null; sink = null;
                        continue;
                    case "LAYER":
                        section = Section.Layer; sectionName = FirstToken(rest); fields = null; sink = null;
                        continue;
                    case "CONTROL":
                        section = Section.Control; sectionName = FirstToken(rest); fields = null; sink = null;
                        continue;
                    case "TABLE":
                        section = Section.Table; tableName = FirstToken(rest); fields = new List<string>();
                        sink = tables.TryGetValue(tableName, out RawTable? wanted) ? wanted : null;
                        continue;
                    case "END_HEADER":
                    case "END_LAYER":
                    case "END_CONTROL":
                    case "END_TABLE":
                        section = Section.None; fields = null; sink = null;
                        continue;
                }

                switch (section)
                {
                    case Section.Header:
                        ReadHeaderLine(result, keyword, rest);
                        break;

                    case Section.Table:
                        if (keyword == "F")
                        {
                            string field = FirstToken(rest);
                            if (field.Length > 0)
                            {
                                fields!.Add(field);
                                sink?.DeclaredFields.Add(field);
                            }
                        }
                        else if (keyword == "D" && sink != null)
                        {
                            AddRow(sink, tableName, fields!, rest, lineNumber);
                        }
                        break;

                    default:
                        // The official area lives in CONTROL CADASTER; accept it anywhere except in another layer's CONTROL
                        if (keyword == "CONTUR_AREA" && (section != Section.Control || sectionName == ControlCadaster))
                        {
                            ReadContourArea(contourAreas, rest, lineNumber);
                        }
                        break;
                }
            }

            if (result.Version.Length > 0 && result.Version != SupportedVersion)
            {
                _warn($"Версия на формата {result.Version}, очаквана {SupportedVersion} — четенето продължава.");
            }
            if (result.Ekatte.Length == 0)
            {
                _warn("В хедъра няма ЕКАТТЕ — идентификаторите на имотите няма да са с префикс.");
            }

            Assemble(result, tables, contourAreas);
            return result;
        }

        // ------------------------------------------------------------------
        //  Header, contour areas, rows
        // ------------------------------------------------------------------

        private static void ReadHeaderLine(CadRegisterData result, string keyword, string value)
        {
            switch (keyword)
            {
                case "VERSION": result.Version = value; break;
                case "EKATTE": result.Ekatte = value; break;
                case "NAME": result.SettlementName = value; break;
            }
        }

        private void ReadContourArea(List<KeyValuePair<string, double>> areas, string rest, int lineNumber)
        {
            string[] parts = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 ||
                !double.TryParse(parts[1].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double area))
            {
                _warn($"ред {lineNumber}: CONTUR_AREA — невалиден запис, пропуснат.");
                return;
            }
            areas.Add(new KeyValuePair<string, double>(parts[0], area));
        }

        private void AddRow(RawTable table, string tableName, List<string> fields, string rest, int lineNumber)
        {
            if (fields.Count == 0)
            {
                if (!table.ReportedNoFields)
                {
                    table.ReportedNoFields = true;
                    _warn($"ред {lineNumber}: таблица {tableName} — данни преди описанието на полетата, редовете се пропускат.");
                }
                return;
            }

            List<string> values = SplitCsv(rest);
            if (values.Count != fields.Count)
            {
                _warn($"ред {lineNumber}: таблица {tableName} — {values.Count} стойности за {fields.Count} полета, редът е пропуснат.");
                return;
            }

            var row = new RawRow { Line = lineNumber };
            for (int i = 0; i < values.Count; i++)
            {
                row.Values[fields[i]] = values[i];
            }
            table.Rows.Add(row);
        }

        // ------------------------------------------------------------------
        //  Assembling the result
        // ------------------------------------------------------------------

        private void Assemble(
            CadRegisterData result,
            Dictionary<string, RawTable> tables,
            List<KeyValuePair<string, double>> contourAreas)
        {
            string ekatte = result.Ekatte;

            // MESTNOSTI: code -> name
            var mestnosti = new Dictionary<string, string>();
            RawTable mestnostTable = tables[TableMestnosti];
            if (HasKey(mestnostTable, TableMestnosti, "MESTNOST"))
            {
                foreach (RawRow row in mestnostTable.Rows)
                {
                    string code = row.Get("MESTNOST");
                    if (code.Length == 0) { WarnMalformed(row, TableMestnosti, "MESTNOST"); continue; }
                    if (!mestnosti.ContainsKey(code)) mestnosti[code] = UnescapeQuotes(row.Get("NAME"));
                }
            }

            // PERSONS: same ID appears once per address -> de-duplicate by ID (first wins)
            var persons = new Dictionary<string, (string Name, bool IsHeirs)>();
            int duplicatePersons = 0;
            int conflictingNames = 0;
            RawTable personTable = tables[TablePersons];
            if (HasKey(personTable, TablePersons, "PERSON"))
            {
                foreach (RawRow row in personTable.Rows)
                {
                    string id = row.Get("PERSON");
                    if (id.Length == 0) { WarnMalformed(row, TablePersons, "PERSON"); continue; }

                    string name = UnescapeQuotes(row.Get("NAME"));
                    if (persons.TryGetValue(id, out var known))
                    {
                        duplicatePersons++;
                        if (known.Name != name) conflictingNames++;
                    }
                    else
                    {
                        persons[id] = (name, row.Get("FLAG").Equals("T", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            result.PersonCount = persons.Count;
            foreach (string personId in persons.Keys) result.PersonIds.Add(personId);
            if (conflictingNames > 0)
            {
                _warn($"{conflictingNames} лица се повтарят в PERSONS с различно име — взето е първото.");
            }

            // Official areas by full parcel ID
            var areas = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> area in contourAreas)
            {
                areas[FullId(ekatte, area.Key)] = area.Value;
            }

            // POZEMLIMOTI
            RawTable parcelTable = tables[TablePozemlimoti];
            if (HasKey(parcelTable, TablePozemlimoti, "IDENT"))
            {
                foreach (RawRow row in parcelTable.Rows)
                {
                    string ident = row.Get("IDENT");
                    if (ident.Length == 0) { WarnMalformed(row, TablePozemlimoti, "IDENT"); continue; }

                    string id = FullId(ekatte, ident);
                    if (result.Parcels.ContainsKey(id))
                    {
                        _warn($"ред {row.Line}: таблица {TablePozemlimoti} — повторен имот {id}, пропуснат.");
                        continue;
                    }

                    string mestnostCode = row.Get("MESTNOST");
                    result.Parcels[id] = new CadastralParcel
                    {
                        Id = id,
                        Vidt = row.Get("VIDT"),
                        Ntp = row.Get("NTP"),
                        Vids = row.Get("VIDS"),
                        Kat = row.Get("KAT"),
                        MestnostCode = mestnostCode,
                        MestnostName = mestnosti.TryGetValue(mestnostCode, out string? mestnostName) ? mestnostName : string.Empty,
                        AreaSqm = areas.TryGetValue(id, out double sqm) ? sqm : (double?)null
                    };
                }
            }

            // GORIMOTI: отдели/подотдели of a parcel (the table may be absent)
            RawTable subdivisionTable = tables[TableGorimoti];
            if (HasKey(subdivisionTable, TableGorimoti, "IDENT"))
            {
                foreach (RawRow row in subdivisionTable.Rows)
                {
                    string ident = row.Get("IDENT");
                    if (ident.Length == 0) { WarnMalformed(row, TableGorimoti, "IDENT"); continue; }

                    if (result.Parcels.TryGetValue(FullId(ekatte, ident), out CadastralParcel? forestParcel))
                    {
                        forestParcel.Subdivisions.Add(new CadSubdivision
                        {
                            Otdel = row.Get("OTDEL"),
                            Podotdel = row.Get("PODOTDEL")
                        });
                    }
                }
            }

            // PRAVA
            int rightsWithoutPerson = 0;
            RawTable rightsTable = tables[TablePrava];
            if (HasKey(rightsTable, TablePrava, "IDENT"))
            {
                foreach (RawRow row in rightsTable.Rows)
                {
                    string ident = row.Get("IDENT");
                    if (ident.Length == 0) { WarnMalformed(row, TablePrava, "IDENT"); continue; }

                    string personId = row.Get("PERSON");
                    string personName = string.Empty;
                    bool isHeirs = false;
                    if (personId.Length > 0)
                    {
                        if (persons.TryGetValue(personId, out var found)) { personName = found.Name; isHeirs = found.IsHeirs; }
                        else rightsWithoutPerson++;
                    }

                    string parcelId = FullId(ekatte, ident);
                    if (!result.Rights.TryGetValue(parcelId, out List<OwnershipRight>? list))
                    {
                        list = new List<OwnershipRight>();
                        result.Rights[parcelId] = list;
                    }
                    list.Add(new OwnershipRight
                    {
                        ParcelId = parcelId,
                        PravoVid = row.Get("PRAVOVID"),
                        PersonId = personId,
                        PersonName = personName,
                        PersonIsHeirs = isHeirs,
                        DocId1 = row.Get("DOCID1"),
                        DocId2 = row.Get("DOCID2"),
                        Srok = row.Get("SROK")
                    });
                }
            }
            if (rightsWithoutPerson > 0)
            {
                _warn($"{rightsWithoutPerson} права сочат лице, което липсва в PERSONS — името е празно.");
            }
        }

        /// <summary>False (with one warning) when rows exist but the table never declared its key field.</summary>
        private bool HasKey(RawTable table, string tableName, string keyField)
        {
            if (table.Rows.Count == 0 || table.DeclaredFields.Contains(keyField)) return true;
            _warn($"таблица {tableName} няма поле {keyField} — таблицата е пропусната.");
            return false;
        }

        private void WarnMalformed(RawRow row, string tableName, string keyField) =>
            _warn($"ред {row.Line}: таблица {tableName} — празно поле {keyField}, редът е пропуснат.");

        // ------------------------------------------------------------------
        //  Text helpers
        // ------------------------------------------------------------------

        /// <summary>"EKATTE.IDENT"; an IDENT that already carries the EKATTE prefix is not prefixed twice.</summary>
        public static string FullId(string ekatte, string ident)
        {
            if (ekatte.Length == 0) return ident;
            if (ident.StartsWith(ekatte + ".", StringComparison.Ordinal)) return ident;
            return ekatte + "." + ident;
        }

        /// <summary>
        /// Both escapes of a quote inside a name give ONE straight quote: \"АГРО\" and \АГРО\ are "АГРО".
        /// </summary>
        public static string UnescapeQuotes(string name) => name.Replace("\\\"", "\"").Replace('\\', '"');

        /// <summary>
        /// Comma-separated values. A string is wrapped in double quotes and may contain unescaped quotes
        /// ("ОБЩИНСКА СЛУЖБА "ЗГ"ГР.Ч.БРЯГ"), so a quote only ends the field when a comma or the end of the line follows.
        /// </summary>
        public static List<string> SplitCsv(string line)
        {
            var values = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (inQuotes)
                {
                    if (c == '\\' && i + 1 < line.Length && line[i + 1] == '"' && !EndsField(line, i + 1))
                    {
                        // \" inside the text is an escaped quote; a \ right before the closing quote is the old \АГРО\ form
                        sb.Append(c).Append('"');
                        i++;
                    }
                    else if (c == '"' && EndsField(line, i)) inQuotes = false;
                    else sb.Append(c);
                }
                else if (c == '"' && sb.ToString().Trim().Length == 0)
                {
                    inQuotes = true;
                    sb.Clear();
                }
                else if (c == ',')
                {
                    values.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }
            values.Add(sb.ToString().Trim());
            return values;
        }

        /// <summary>True when only spaces separate the quote at <paramref name="quote"/> from a comma or the end of the line.</summary>
        private static bool EndsField(string line, int quote)
        {
            int j = quote + 1;
            while (j < line.Length && line[j] == ' ') j++;
            return j >= line.Length || line[j] == ',';
        }

        private static int IndexOfWhitespace(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i])) return i;
            }
            return -1;
        }

        private static string FirstToken(string text)
        {
            int space = IndexOfWhitespace(text);
            return space < 0 ? text : text.Substring(0, space);
        }
    }
}
