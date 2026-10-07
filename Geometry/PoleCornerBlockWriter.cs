using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PUP_AUTO.Core;

namespace PUP_AUTO.Geometry
{
    /// <summary>The corner blocks of one pole: the label rotation θ (radians) and one placement per corner.</summary>
    public sealed class PoleCornerSet
    {
        public string PoleNumber { get; set; } = string.Empty;
        public double Theta { get; set; }
        public List<CornerLabelPlacement> Corners { get; set; } = new List<CornerLabelPlacement>();
    }

    public sealed class PoleCornerDrawResult
    {
        public int Drawn { get; set; }

        /// <summary>Corners skipped because an untagged GBP032 with the same NOMER already sits there.</summary>
        public int SkippedExisting { get; set; }

        /// <summary>Plugin-tagged inserts of the same poles erased before drawing (a re-run).</summary>
        public int Replaced { get; set; }

        /// <summary>Set when nothing was drawn because the block is not available.</summary>
        public string? Warning { get; set; }
    }

    /// <summary>
    /// Draws a GBP032 block (scale 2, rotation θ, layer S-Trass) at every footprint corner, with NOMER = the corner label
    /// placed by <see cref="PoleCornerPlacement"/>. Every insert is tagged with XData so a re-run replaces exactly the plugin's
    /// inserts of the same poles; untagged GBP032 inserts are never touched. One transaction under the document lock.
    /// </summary>
    public static class PoleCornerBlockWriter
    {
        private const double SameCornerToleranceM = 0.01;

        public static PoleCornerDrawResult Draw(Document doc, IReadOnlyList<PoleCornerSet> poles, string pluginDir)
        {
            var result = new PoleCornerDrawResult();
            Database db = doc.Database;

            using (doc.LockDocument())
            {
                if (!EnsureBlock(db, pluginDir, out string? warning))
                {
                    result.Warning = warning;
                    return result;
                }

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DrawingWriter.EnsureLayer(db, tr, PoleCornerBlockNames.Layer, PoleCornerBlockNames.LayerColor);
                    EnsureRegApp(db, tr);

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    ObjectId blockId = bt[PoleCornerBlockNames.BlockName];
                    var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);

                    var definitions = new List<AttributeDefinition>();
                    foreach (ObjectId id in block)
                    {
                        if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad && !ad.Constant) definitions.Add(ad);
                    }

                    // Erase our own inserts of these poles; remember the untagged ones so they are not drawn twice
                    var poleNumbers = new HashSet<string>(poles.Select(p => p.PoleNumber), StringComparer.Ordinal);
                    var untagged = new List<(string Nomer, Point3d Position)>();
                    foreach (ObjectId id in block.GetBlockReferenceIds(true, false))
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br) || br.IsErased) continue;
                        string? nomer = ReadNomer(br, tr);
                        if (IsTagged(br))
                        {
                            if (nomer != null && poleNumbers.Contains(PoleOf(nomer)))
                            {
                                br.UpgradeOpen();
                                br.Erase();
                                result.Replaced++;
                            }
                        }
                        else if (nomer != null)
                        {
                            untagged.Add((nomer, br.Position));
                        }
                    }

                    var modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    foreach (PoleCornerSet pole in poles)
                    {
                        foreach (CornerLabelPlacement corner in pole.Corners)
                        {
                            var position = new Point3d(corner.CornerX, corner.CornerY, 0);
                            if (untagged.Any(u => u.Nomer == corner.Label && u.Position.DistanceTo(position) <= SameCornerToleranceM))
                            {
                                result.SkippedExisting++;
                                continue;
                            }

                            Insert(db, tr, modelSpace, blockId, definitions, position, pole.Theta, corner);
                            result.Drawn++;
                        }
                    }
                    tr.Commit();
                }
            }
            return result;
        }

        private static void Insert(Database db, Transaction tr, BlockTableRecord space, ObjectId blockId,
            List<AttributeDefinition> definitions, Point3d position, double theta, CornerLabelPlacement corner)
        {
            var br = new BlockReference(position, blockId);
            br.SetDatabaseDefaults(db);
            br.Layer = PoleCornerBlockNames.Layer;
            br.ScaleFactors = new Scale3d(PoleCornerPlacement.BlockScale);
            br.Rotation = theta;
            space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            br.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, PoleCornerBlockNames.XDataApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, PoleCornerBlockNames.XDataValue));

            foreach (AttributeDefinition ad in definitions)
            {
                var ar = new AttributeReference();
                ar.SetAttributeFromBlock(ad, br.BlockTransform);   // KOTA keeps its default and its invisibility
                br.AttributeCollection.AppendAttribute(ar);
                tr.AddNewlyCreatedDBObject(ar, true);

                if (!string.Equals(ad.Tag, PoleCornerBlockNames.NumberTag, StringComparison.OrdinalIgnoreCase)) continue;
                ar.TextString = corner.Label;
                ar.Justify = Justification(corner);
                ar.AlignmentPoint = new Point3d(corner.AlignX, corner.AlignY, 0);
                ar.Rotation = theta;
                ar.AdjustAlignment(db);
            }
        }

        private static AttachmentPoint Justification(CornerLabelPlacement corner)
        {
            bool left = corner.Horizontal == CornerLabelHorizontal.Left;
            if (corner.Vertical == CornerLabelVertical.Baseline) return left ? AttachmentPoint.BaseLeft : AttachmentPoint.BaseRight;
            return left ? AttachmentPoint.TopLeft : AttachmentPoint.TopRight;
        }

        /// <summary>"20-1" -> "20" (the part before the last dash).</summary>
        private static string PoleOf(string label)
        {
            int dash = label.LastIndexOf('-');
            return dash > 0 ? label.Substring(0, dash) : label;
        }

        private static string? ReadNomer(BlockReference br, Transaction tr)
        {
            foreach (ObjectId id in br.AttributeCollection)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is AttributeReference ar &&
                    string.Equals(ar.Tag, PoleCornerBlockNames.NumberTag, StringComparison.OrdinalIgnoreCase))
                {
                    return ar.TextString.Trim();
                }
            }
            return null;
        }

        private static bool IsTagged(BlockReference br)
        {
            using (ResultBuffer? xdata = br.GetXDataForApplication(PoleCornerBlockNames.XDataApp))
            {
                if (xdata == null) return false;
                return xdata.AsArray().Any(tv => tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                                                  (tv.Value as string) == PoleCornerBlockNames.XDataValue);
            }
        }

        private static void EnsureRegApp(Database db, Transaction tr)
        {
            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (table.Has(PoleCornerBlockNames.XDataApp)) return;
            table.UpgradeOpen();
            var record = new RegAppTableRecord { Name = PoleCornerBlockNames.XDataApp };
            table.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        /// <summary>
        /// Makes sure the drawing has GBP032: imported from the block file next to the plugin when missing (as a block of
        /// that name, or the whole file as the block when it was written with WBLOCK). False with a warning when neither exists.
        /// </summary>
        private static bool EnsureBlock(Database db, string pluginDir, out string? warning)
        {
            warning = null;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                bool has = bt.Has(PoleCornerBlockNames.BlockName);
                tr.Commit();
                if (has) return true;
            }

            string path = Path.Combine(pluginDir, PoleCornerBlockNames.BlockFile);
            if (!File.Exists(path))
            {
                warning = $"Блокът {PoleCornerBlockNames.BlockName} не е в чертежа, а файлът {path} липсва — ъгловите точки не са начертани.";
                return false;
            }

            using (var source = new Database(false, true))
            {
                source.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                source.CloseInput(true);

                ObjectId sourceBlock = ObjectId.Null;
                using (Transaction str = source.TransactionManager.StartTransaction())
                {
                    var sbt = (BlockTable)str.GetObject(source.BlockTableId, OpenMode.ForRead);
                    if (sbt.Has(PoleCornerBlockNames.BlockName)) sourceBlock = sbt[PoleCornerBlockNames.BlockName];
                    str.Commit();
                }

                if (!sourceBlock.IsNull)
                {
                    // Brings the block's text style and layers along
                    source.WblockCloneObjects(new ObjectIdCollection { sourceBlock }, db.BlockTableId, new IdMapping(),
                        DuplicateRecordCloning.Ignore, false);
                }
                else
                {
                    db.Insert(PoleCornerBlockNames.BlockName, source, true);
                }
            }
            return true;
        }
    }
}
