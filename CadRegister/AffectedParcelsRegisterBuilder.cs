using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.CadRegister
{
    /// <summary>One printed row of the register of affected parcels. Empty cells are "" / null.</summary>
    public sealed class AffectedRegisterRow
    {
        /// <summary>True on the row that carries the parcel data (columns 1-11); the following owners' rows leave them empty.</summary>
        public bool IsFirstOfParcel { get; set; }

        public string Number { get; set; } = string.Empty;          // 1
        public string Subdivisions { get; set; } = string.Empty;    // 2
        public string Vidt { get; set; } = string.Empty;            // 3
        public string Ntp { get; set; } = string.Empty;             // 4
        public string Mestnost { get; set; } = string.Empty;        // 5
        public string Category { get; set; } = string.Empty;        // 6
        public double? AreaDka { get; set; }                        // 7
        public double? RestrictedDka { get; set; }                  // 8
        public double? RemainderDka { get; set; }                   // 9
        public string PoleNumbers { get; set; } = string.Empty;     // 10
        public double? StepDka { get; set; }                        // 11
        public string Vids { get; set; } = string.Empty;            // 12
        public string PersonId { get; set; } = string.Empty;        // 13
        public string PersonName { get; set; } = string.Empty;      // 14

        // Raw .cad codes of the parcel row (empty when the parcel is not in the .cad); not printed by the register,
        // used by the territory balance to group the same rows.
        public string KatCode { get; set; } = string.Empty;
        public string VidsCode { get; set; } = string.Empty;
        public string VidtCode { get; set; } = string.Empty;
        public string NtpCode { get; set; } = string.Empty;
    }

    public sealed class AffectedRegister
    {
        /// <summary>Row 1: "РЕГИСТЪР НА ЗАСЕГНАТИТЕ ИМОТИ ОТ &lt;text&gt;".</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Row 2: the EKATTE title.</summary>
        public string Subtitle { get; set; } = string.Empty;

        /// <summary>EKATTE code and the settlement name of the .cad, for the reports built from this register (the recapitulation).</summary>
        public string Ekatte { get; set; } = string.Empty;
        public string SettlementName { get; set; } = string.Empty;

        public List<AffectedRegisterRow> Rows { get; } = new List<AffectedRegisterRow>();

        // ОБЩО: sums of the printed values, each parcel counted once
        public double TotalAreaDka { get; set; }
        public double TotalRestrictedDka { get; set; }
        public double TotalRemainderDka { get; set; }
        public double TotalStepDka { get; set; }

        /// <summary>Picked parcels that are not in the .cad (they still get a row with the drawn areas).</summary>
        public List<string> NotFound { get; } = new List<string>();

        /// <summary>Parcels in the .cad without a right of ownership (one row, column 12 filled, 13-14 empty).</summary>
        public List<string> WithoutOwners { get; } = new List<string>();

        /// <summary>Parcels whose printed remainder came out negative (steps + servitude larger than the parcel).</summary>
        public List<string> NegativeRemainder { get; } = new List<string>();
    }

    /// <summary>
    /// Builds the register of affected parcels from plain data (no AutoCAD types): drawn/servitude areas and pole
    /// pieces per parcel, plus the loaded .cad. Areas come in as raw m², are rounded to decares once per printed value
    /// and the remainder is derived from those printed values (decimal arithmetic), so every row adds up on paper.
    /// </summary>
    public static class AffectedParcelsRegisterBuilder
    {
        public const string TitlePrefix = "РЕГИСТЪР НА ЗАСЕГНАТИТЕ ИМОТИ ОТ ";
        public static AffectedRegister Build(
            IEnumerable<RegisterParcelAreas> parcels,
            IEnumerable<PoleStepPiece> pieces,
            CadRegisterData register,
            Nomenclatures nomenclatures,
            string projectName,
            string ekatteTitle)
        {
            var result = new AffectedRegister
            {
                Title = TitlePrefix + projectName.Trim(),
                Subtitle = ekatteTitle,
                Ekatte = register.Ekatte,
                SettlementName = register.SettlementName
            };

            // One ID drawn as several polylines is one parcel: the areas are summed
            var areasById = new Dictionary<string, RegisterParcelAreas>(StringComparer.Ordinal);
            foreach (RegisterParcelAreas parcel in parcels)
            {
                if (!areasById.TryGetValue(parcel.ParcelId, out RegisterParcelAreas? sum))
                {
                    sum = new RegisterParcelAreas { ParcelId = parcel.ParcelId };
                    areasById[parcel.ParcelId] = sum;
                }
                sum.DrawnAreaSqm += parcel.DrawnAreaSqm;
                sum.ServitudeGrossAreaSqm += parcel.ServitudeGrossAreaSqm;
            }

            var piecesById = new Dictionary<string, List<PoleStepPiece>>(StringComparer.Ordinal);
            foreach (PoleStepPiece piece in pieces)
            {
                if (!piecesById.TryGetValue(piece.ParcelId, out List<PoleStepPiece>? list))
                {
                    list = new List<PoleStepPiece>();
                    piecesById[piece.ParcelId] = list;
                }
                list.Add(piece);
            }

            var ids = areasById.Keys.ToList();
            ids.Sort(PoleStepsTableBuilder.CompareParcelIds);

            decimal totalArea = 0m, totalRestricted = 0m, totalRemainder = 0m, totalStep = 0m;

            foreach (string id in ids)
            {
                RegisterParcelAreas areas = areasById[id];
                decimal areaDka = (decimal)AreaUnits.SqmToDka(areas.DrawnAreaSqm);
                decimal grossDka = (decimal)AreaUnits.SqmToDka(areas.ServitudeGrossAreaSqm);

                // Poles of the parcel ascending; the step is the sum of the printed (rounded) pieces
                piecesById.TryGetValue(id, out List<PoleStepPiece>? parcelPieces);
                decimal stepDka = 0m;
                var poleNumbers = new List<string>();
                foreach (PoleStepPiece piece in parcelPieces ?? new List<PoleStepPiece>())
                {
                    stepDka += (decimal)AreaUnits.SqmToDka(piece.PieceAreaSqm);
                    string number = PoleLabels.StripPrefix(piece.PoleNumber);
                    if (!poleNumbers.Contains(number)) poleNumbers.Add(number);
                }
                poleNumbers.Sort(PoleStepsTableBuilder.ComparePoleNumbers);
                bool hasPoles = poleNumbers.Count > 0;

                // Column 8 = printed gross servitude minus the printed steps (not the rounded net area)
                decimal restrictedDka = grossDka - stepDka;
                decimal remainderDka = areaDka - restrictedDka - stepDka;
                if (remainderDka < 0m) result.NegativeRemainder.Add(id);

                totalArea += areaDka;
                totalRestricted += restrictedDka;
                totalRemainder += remainderDka;
                totalStep += stepDka;

                var first = new AffectedRegisterRow
                {
                    IsFirstOfParcel = true,
                    Number = id,
                    AreaDka = (double)areaDka,
                    RestrictedDka = (double)restrictedDka,
                    RemainderDka = (double)remainderDka,
                    PoleNumbers = string.Join(", ", poleNumbers.Select(n => PoleLabels.Prefix + n)),
                    StepDka = hasPoles ? (double)stepDka : (double?)null
                };

                if (register.Parcels.TryGetValue(id, out CadastralParcel? cad))
                {
                    // Column 2 (Подотдели) stays empty for now: it will come from separate forest files, not from the .cad.
                    // FormatSubdivisions is kept for that; GORIMOTI is still read into CadastralParcel.Subdivisions.
                    first.Vidt = nomenclatures.Vidt.TextOf(cad.Vidt);
                    first.Ntp = nomenclatures.Ntp.TextOf(cad.Ntp);
                    first.Mestnost = cad.MestnostName;
                    first.Category = CategoryFormat.Format(cad.Kat);
                    first.KatCode = cad.Kat;
                    first.VidsCode = cad.Vids;
                    first.VidtCode = cad.Vidt;
                    first.NtpCode = cad.Ntp;
                }
                else
                {
                    result.NotFound.Add(id);
                }

                List<AffectedRegisterRow> ownerRows = OwnerRows(register, id, cad, nomenclatures);
                if (ownerRows.Count == 0)
                {
                    if (cad != null)
                    {
                        result.WithoutOwners.Add(id);
                        first.Vids = nomenclatures.Vids.TextOf(cad.Vids); // columns 13-14 stay empty
                    }
                    result.Rows.Add(first);
                    continue;
                }

                CopyOwner(ownerRows[0], first);
                result.Rows.Add(first);
                for (int i = 1; i < ownerRows.Count; i++)
                {
                    result.Rows.Add(ownerRows[i]); // columns 1-11 stay empty
                }
            }

            result.TotalAreaDka = (double)totalArea;
            result.TotalRestrictedDka = (double)totalRestricted;
            result.TotalRemainderDka = (double)totalRemainder;
            result.TotalStepDka = (double)totalStep;
            return result;
        }

        /// <summary>
        /// One row per owner (rules in <see cref="CadOwners"/>). Column 12 is the parcel's ownership type.
        /// A parcel that is not in the .cad has no owners.
        /// </summary>
        private static List<AffectedRegisterRow> OwnerRows(
            CadRegisterData register, string parcelId, CadastralParcel? cad, Nomenclatures nomenclatures)
        {
            var rows = new List<AffectedRegisterRow>();
            if (cad == null) return rows;

            foreach (CadOwner owner in CadOwners.OwnersOf(register, parcelId, cad, nomenclatures))
            {
                rows.Add(new AffectedRegisterRow { Vids = owner.Vids, PersonId = owner.PersonId, PersonName = owner.PersonName });
            }
            return rows;
        }

        private static void CopyOwner(AffectedRegisterRow from, AffectedRegisterRow to)
        {
            to.Vids = from.Vids;
            to.PersonId = from.PersonId;
            to.PersonName = from.PersonName;
        }

        /// <summary>"45/а, 46/б": "&lt;OTDEL&gt;/&lt;PODOTDEL&gt;", one entry per row; a row without подотдел is just the отдел.</summary>
        public static string FormatSubdivisions(CadastralParcel parcel)
        {
            var parts = new List<string>();
            foreach (CadSubdivision s in parcel.Subdivisions)
            {
                string otdel = s.Otdel.Trim();
                string podotdel = s.Podotdel.Trim();
                string text = otdel.Length > 0 && podotdel.Length > 0 ? otdel + "/" + podotdel : otdel + podotdel;
                if (text.Length > 0) parts.Add(text);
            }
            return string.Join(", ", parts);
        }
    }
}
