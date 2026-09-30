namespace PUP_AUTO.CadRegister
{
    /// <summary>A cadastral parcel (POZEMLIMOTI row). Codes are kept raw; nomenclature text is resolved at report time.</summary>
    public class CadastralParcel
    {
        /// <summary>Full parcel ID "EKATTE.IDENT", e.g. "06433.501.1".</summary>
        public string Id { get; set; } = string.Empty;

        public string Vidt { get; set; } = string.Empty;
        public string Ntp { get; set; } = string.Empty;
        public string Vids { get; set; } = string.Empty;
        public string Kat { get; set; } = string.Empty;

        public string MestnostCode { get; set; } = string.Empty;

        /// <summary>Locality name from MESTNOSTI; empty when the code is empty or unknown.</summary>
        public string MestnostName { get; set; } = string.Empty;

        /// <summary>Official area from CONTROL CADASTER / CONTUR_AREA, in square meters; null when absent.</summary>
        public double? AreaSqm { get; set; }
    }

    /// <summary>One right (PRAVA row) of a parcel. ЕГН/БУЛСТАТ is text: leading zeros and odd values are kept as they are.</summary>
    public class OwnershipRight
    {
        /// <summary>Full parcel ID "EKATTE.IDENT".</summary>
        public string ParcelId { get; set; } = string.Empty;

        public string PravoVid { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;

        /// <summary>DOCID1 as read from the file (share numerator, unverified).</summary>
        public string DocId1 { get; set; } = string.Empty;

        /// <summary>DOCID2 as read from the file (share denominator, unverified).</summary>
        public string DocId2 { get; set; } = string.Empty;
    }

    /// <summary>The data of ONE .cad file (one землище). Keyed by full parcel ID so several files can be merged later.</summary>
    public class CadRegisterData
    {
        public string Ekatte { get; set; } = string.Empty;

        /// <summary>The NAME header line as written in the file, e.g. "с.ЦАРЕВЕЦ".</summary>
        public string SettlementName { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public Dictionary<string, CadastralParcel> Parcels { get; } = new Dictionary<string, CadastralParcel>();

        public Dictionary<string, List<OwnershipRight>> Rights { get; } = new Dictionary<string, List<OwnershipRight>>();

        /// <summary>Number of distinct persons (after de-duplication by ID).</summary>
        public int PersonCount { get; set; }

        public IReadOnlyList<OwnershipRight> RightsOf(string parcelId) =>
            Rights.TryGetValue(parcelId, out List<OwnershipRight>? rights) ? rights : Array.Empty<OwnershipRight>();

        /// <summary>The EKATTE part of a full parcel ID ("06433.501.1" -> "06433"); empty when there is no dot.</summary>
        public static string EkatteOf(string fullParcelId)
        {
            int dot = fullParcelId.IndexOf('.');
            return dot < 0 ? string.Empty : fullParcelId.Substring(0, dot);
        }
    }
}
