namespace PUP_AUTO.CadRegister
{
    /// <summary>One owner of a parcel as the registers print it: the parcel's VIDS text, the person ID and the display name.</summary>
    internal sealed class CadOwner
    {
        public string Vids { get; set; } = string.Empty;
        public string PersonId { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
    }

    /// <summary>The owner rules shared by the registers built from the .cad.</summary>
    internal static class CadOwners
    {
        public const string HeirsPrefix = "н-ци на ";

        /// <summary>PRAVOVID code of the right of ownership.</summary>
        public const string OwnershipRightCode = "1";

        /// <summary>
        /// One entry per owner: PRAVA rows with PRAVOVID 1 only, de-duplicated by person ID (first order kept).
        /// The name carries the heirs prefix when the person is FLAG = T. Empty when the parcel has no owner.
        /// </summary>
        public static List<CadOwner> OwnersOf(
            CadRegisterData register, string parcelId, CadastralParcel cad, Nomenclatures nomenclatures)
        {
            var owners = new List<CadOwner>();
            string vids = nomenclatures.Vids.TextOf(cad.Vids);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (OwnershipRight right in register.RightsOf(parcelId))
            {
                if (!IsOwnership(right.PravoVid)) continue;
                if (!seen.Add(right.PersonId)) continue;

                owners.Add(new CadOwner
                {
                    Vids = vids,
                    PersonId = right.PersonId,
                    PersonName = (right.PersonIsHeirs ? HeirsPrefix : string.Empty) + right.PersonName
                });
            }
            return owners;
        }

        public static bool IsOwnership(string pravoVid)
        {
            string code = pravoVid.Trim().TrimStart('0');
            return code == OwnershipRightCode;
        }
    }
}
