using System.Globalization;

namespace PUP_AUTO.CadRegister
{
    /// <summary>One line of the control report: one right of one parcel (parcel data repeated).</summary>
    public sealed class CadControlRow
    {
        public string ParcelId { get; set; } = string.Empty;
        public string Vidt { get; set; } = string.Empty;
        public string Ntp { get; set; } = string.Empty;
        public string Mestnost { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;

        /// <summary>Official (.cad) area in square meters; null when the file has none.</summary>
        public double? CadAreaSqm { get; set; }

        /// <summary>Area of the drawn polyline in square meters.</summary>
        public double DrawnAreaSqm { get; set; }

        /// <summary>Drawn minus official area, square meters; null when there is no official area.</summary>
        public double? DifferenceSqm => CadAreaSqm.HasValue ? DrawnAreaSqm - CadAreaSqm.Value : (double?)null;

        public string Vids { get; set; } = string.Empty;
        public string PravoVid { get; set; } = string.Empty;
        public string Share { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
    }

    public sealed class CadControlReport
    {
        public string Title { get; set; } = string.Empty;
        public List<CadControlRow> Rows { get; } = new List<CadControlRow>();

        /// <summary>Picked parcels that are not in the .cad (this includes the ones of another EKATTE).</summary>
        public List<string> NotFound { get; } = new List<string>();

        /// <summary>Picked parcels whose EKATTE differs from the loaded file: (parcel ID, its EKATTE).</summary>
        public List<KeyValuePair<string, string>> ForeignEkatte { get; } = new List<KeyValuePair<string, string>>();

        /// <summary>Found parcels that have no rights in the file; they still get one row.</summary>
        public List<string> WithoutRights { get; } = new List<string>();
    }

    /// <summary>Builds the control report rows from the drawn parcels and one loaded .cad. Pure logic.</summary>
    public static class CadControlReportBuilder
    {
        /// <param name="drawnParcels">Picked parcels: parcel ID and drawn area (m²). One ID drawn as several polylines is summed.</param>
        public static CadControlReport Build(
            IEnumerable<KeyValuePair<string, double>> drawnParcels,
            CadRegisterData register,
            Nomenclatures nomenclatures,
            string title)
        {
            var report = new CadControlReport { Title = title };

            var drawnAreas = new Dictionary<string, double>();
            foreach (KeyValuePair<string, double> parcel in drawnParcels)
            {
                drawnAreas.TryGetValue(parcel.Key, out double sum);
                drawnAreas[parcel.Key] = sum + parcel.Value;
            }

            var ids = drawnAreas.Keys.ToList();
            ids.Sort(CompareParcelIds);

            foreach (string id in ids)
            {
                if (!register.Parcels.TryGetValue(id, out CadastralParcel? parcel))
                {
                    report.NotFound.Add(id);

                    string ekatte = CadRegisterData.EkatteOf(id);
                    if (ekatte.Length > 0 && ekatte != register.Ekatte)
                    {
                        report.ForeignEkatte.Add(new KeyValuePair<string, string>(id, ekatte));
                    }
                    continue;
                }

                IReadOnlyList<OwnershipRight> rights = register.RightsOf(id);
                if (rights.Count == 0)
                {
                    report.WithoutRights.Add(id);
                    report.Rows.Add(NewRow(parcel, drawnAreas[id], nomenclatures));
                    continue;
                }

                foreach (OwnershipRight right in rights)
                {
                    CadControlRow row = NewRow(parcel, drawnAreas[id], nomenclatures);
                    row.PravoVid = nomenclatures.PravoVid.Describe(right.PravoVid);
                    row.Share = FormatShare(right.DocId1, right.DocId2);
                    row.PersonId = right.PersonId;
                    row.PersonName = right.PersonName;
                    report.Rows.Add(row);
                }
            }
            return report;
        }

        private static CadControlRow NewRow(CadastralParcel parcel, double drawnAreaSqm, Nomenclatures nomenclatures) =>
            new CadControlRow
            {
                ParcelId = parcel.Id,
                Vidt = nomenclatures.Vidt.Describe(parcel.Vidt),
                Ntp = nomenclatures.Ntp.Describe(parcel.Ntp),
                Mestnost = parcel.MestnostName,
                Category = CategoryFormat.Format(parcel.Kat),
                CadAreaSqm = parcel.AreaSqm,
                DrawnAreaSqm = drawnAreaSqm,
                Vids = nomenclatures.Vids.Describe(parcel.Vids)
            };

        /// <summary>DOCID1/DOCID2 exactly as read: "1/2"; a single value alone; empty when both are empty.</summary>
        public static string FormatShare(string docId1, string docId2)
        {
            string a = docId1.Trim();
            string b = docId2.Trim();
            if (a.Length > 0 && b.Length > 0) return a + "/" + b;
            return a.Length > 0 ? a : b;
        }

        /// <summary>"06433.9.1" sorts before "06433.10.1": dot-separated parts compare as numbers when both are numeric.</summary>
        public static int CompareParcelIds(string x, string y)
        {
            string[] a = x.Split('.');
            string[] b = y.Split('.');
            int count = Math.Min(a.Length, b.Length);
            for (int i = 0; i < count; i++)
            {
                int c;
                if (long.TryParse(a[i], NumberStyles.None, CultureInfo.InvariantCulture, out long na) &&
                    long.TryParse(b[i], NumberStyles.None, CultureInfo.InvariantCulture, out long nb))
                {
                    c = na.CompareTo(nb);
                    if (c == 0) c = string.CompareOrdinal(a[i], b[i]); // "01" vs "1"
                }
                else
                {
                    c = string.CompareOrdinal(a[i], b[i]);
                }
                if (c != 0) return c;
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}
