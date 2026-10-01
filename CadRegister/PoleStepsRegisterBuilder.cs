using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.CadRegister
{
    /// <summary>One printed row of the register of pole steps. Empty cells are "" / null.</summary>
    public sealed class PoleStepsRegisterRow
    {
        /// <summary>True on the row that carries the block data (columns 1-10); further owners' rows only fill columns 11-12.</summary>
        public bool IsFirstOfBlock { get; set; }

        public string PoleLabel { get; set; } = string.Empty;       // 1
        public double? PieceDka { get; set; }                       // 2
        public string ParcelId { get; set; } = string.Empty;        // 3
        public string Subdivisions { get; set; } = string.Empty;    // 4
        public string Vidt { get; set; } = string.Empty;            // 5
        public string Ntp { get; set; } = string.Empty;             // 6
        public string Mestnost { get; set; } = string.Empty;        // 7
        public string Category { get; set; } = string.Empty;        // 8
        public double? AreaDka { get; set; }                        // 9
        public string Vids { get; set; } = string.Empty;            // 10
        public string PersonId { get; set; } = string.Empty;        // 11
        public string PersonName { get; set; } = string.Empty;      // 12
    }

    public sealed class PoleStepsRegister
    {
        /// <summary>Row 1: "РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ОТ &lt;text&gt;".</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Row 2: the EKATTE title.</summary>
        public string Subtitle { get; set; } = string.Empty;

        public List<PoleStepsRegisterRow> Rows { get; } = new List<PoleStepsRegisterRow>();

        /// <summary>Distinct poles in the table (a pole standing in two parcels counts once).</summary>
        public int PoleCount { get; set; }

        /// <summary>Decimal sum of the printed (rounded) column-2 values.</summary>
        public double TotalPieceDka { get; set; }

        /// <summary>Parcels that are not in the .cad (one row with columns 1, 2, 3 and 9 only).</summary>
        public List<string> NotFound { get; } = new List<string>();

        /// <summary>Parcels in the .cad without a right of ownership (one row, column 10 filled, 11-12 empty).</summary>
        public List<string> WithoutOwners { get; } = new List<string>();
    }

    /// <summary>
    /// Builds the register of pole steps from plain data (no AutoCAD types): one block per (pole, parcel) piece with
    /// the parcel data from the .cad and one row per owner. Areas come in as raw m² and are rounded to decares once
    /// per printed value.
    /// </summary>
    public static class PoleStepsRegisterBuilder
    {
        public const string TitlePrefix = "РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ОТ ";
        public const string CountLabel = "Брой: ";

        /// <param name="drawnAreaSqmById">Drawn parcel area per parcel ID: the sum over all picked polylines with that ID.</param>
        public static PoleStepsRegister Build(
            IEnumerable<PoleStepPiece> pieces,
            IReadOnlyDictionary<string, double> drawnAreaSqmById,
            CadRegisterData register,
            Nomenclatures nomenclatures,
            string projectName,
            string ekatteTitle)
        {
            var result = new PoleStepsRegister
            {
                Title = TitlePrefix + projectName.Trim(),
                Subtitle = ekatteTitle
            };

            // One pole in one parcel ID is one block: pieces over several polylines of the same ID are added in m²
            var blocks = new List<(string Pole, string ParcelId, double Sqm, double FallbackParcelSqm)>();
            var blockIndex = new Dictionary<(string, string), int>();
            foreach (PoleStepPiece piece in pieces)
            {
                string pole = PoleLabels.StripPrefix(piece.PoleNumber);
                var key = (pole, piece.ParcelId);
                if (blockIndex.TryGetValue(key, out int index))
                {
                    var block = blocks[index];
                    block.Sqm += piece.PieceAreaSqm;
                    blocks[index] = block;
                }
                else
                {
                    blockIndex[key] = blocks.Count;
                    blocks.Add((pole, piece.ParcelId, piece.PieceAreaSqm, piece.ParcelAreaSqm));
                }
            }

            // Pole ascending, then parcel ID; the (pole, parcel) keys are unique, so the unstable List.Sort is safe
            blocks.Sort((a, b) =>
            {
                int c = PoleStepsTableBuilder.ComparePoleNumbers(a.Pole, b.Pole);
                return c != 0 ? c : PoleStepsTableBuilder.CompareParcelIds(a.ParcelId, b.ParcelId);
            });

            var poles = new HashSet<string>(StringComparer.Ordinal);
            decimal totalPiece = 0m;
            var notFound = new HashSet<string>(StringComparer.Ordinal);
            var withoutOwners = new HashSet<string>(StringComparer.Ordinal);

            foreach (var block in blocks)
            {
                poles.Add(block.Pole);

                decimal pieceDka = (decimal)AreaUnits.SqmToDka(block.Sqm);
                totalPiece += pieceDka;

                double drawnSqm = drawnAreaSqmById.TryGetValue(block.ParcelId, out double drawn) ? drawn : block.FallbackParcelSqm;

                var first = new PoleStepsRegisterRow
                {
                    IsFirstOfBlock = true,
                    PoleLabel = PoleLabels.Prefix + block.Pole,
                    PieceDka = (double)pieceDka,
                    ParcelId = block.ParcelId,
                    AreaDka = AreaUnits.SqmToDka(drawnSqm)
                };

                if (!register.Parcels.TryGetValue(block.ParcelId, out CadastralParcel? cad))
                {
                    if (notFound.Add(block.ParcelId)) result.NotFound.Add(block.ParcelId);
                    result.Rows.Add(first);
                    continue;
                }

                // Column 4 (Подотдели) stays empty for now: it will come from separate forest files, not from the .cad.
                first.Vidt = nomenclatures.Vidt.TextOf(cad.Vidt);
                first.Ntp = nomenclatures.Ntp.TextOf(cad.Ntp);
                first.Mestnost = cad.MestnostName;
                first.Category = CategoryFormat.Format(cad.Kat);

                List<CadOwner> owners = CadOwners.OwnersOf(register, block.ParcelId, cad, nomenclatures);
                if (owners.Count == 0)
                {
                    if (withoutOwners.Add(block.ParcelId)) result.WithoutOwners.Add(block.ParcelId);
                    first.Vids = nomenclatures.Vids.TextOf(cad.Vids); // columns 11-12 stay empty
                    result.Rows.Add(first);
                    continue;
                }

                first.Vids = owners[0].Vids;
                first.PersonId = owners[0].PersonId;
                first.PersonName = owners[0].PersonName;
                result.Rows.Add(first);

                // Further owners: ONLY columns 11 and 12 (column 10 is not repeated, as in the official document)
                for (int i = 1; i < owners.Count; i++)
                {
                    result.Rows.Add(new PoleStepsRegisterRow { PersonId = owners[i].PersonId, PersonName = owners[i].PersonName });
                }
            }

            result.PoleCount = poles.Count;
            result.TotalPieceDka = (double)totalPiece;
            return result;
        }
    }
}
