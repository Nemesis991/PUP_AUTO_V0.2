using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PUP_AUTO.Core;

namespace PUP_AUTO.Geometry
{
    public sealed class ServitudePointDrawResult
    {
        public int Drawn { get; set; }

        /// <summary>How many of the drawn points are on the left edge, and how many on the right.</summary>
        public int Left { get; set; }
        public int Right { get; set; }

        /// <summary>Plugin-tagged inserts erased before drawing (a re-run; the numbering can change).</summary>
        public int Replaced { get; set; }

        /// <summary>NOMER definitions of an earlier build that still had their position locked in the block, now unlocked.</summary>
        public int DefinitionsUnlocked { get; set; }

        /// <summary>One entry per point that failed: its number and the call that threw. The other points are still drawn.</summary>
        public List<string> Failures { get; } = new List<string>();

        /// <summary>Set when the blocks were drawn but the screen refresh failed (low-level, not a drawing failure).</summary>
        public string? RefreshWarning { get; set; }
    }

    /// <summary>
    /// Draws a SERV_TOCHKA block (scale 1, rotation 0, layer S-Trass-сервитут) at every servitude point it is given, with
    /// NOMER placed by <see cref="ServitudePointPlacement"/>. The block definition and its NUM_Align text style are created
    /// in code when the drawing has neither; the look follows Simeon's sample (a DBPoint on layer 0 plus the number text).
    ///
    /// Every insert is tagged with XData, and ALL of the plugin's own tagged inserts in the drawing are erased first,
    /// because the numbering can change between runs (an earlier build also drew blocks on points outside the picked parcels). Untagged blocks and Simeon's own TEXT/POINT labels are never touched.
    /// One transaction under the document lock, so the whole lot is one undo step.
    /// </summary>
    public static class ServitudePointBlockWriter
    {
        public static ServitudePointDrawResult Draw(
            Document doc, IReadOnlyList<ServitudeLabelPlacement> points, IReadOnlyList<bool> isLeft)
        {
            var result = new ServitudePointDrawResult();
            Database db = doc.Database;

            // AdjustAlignment and the text style lookups work against the working database
            Database previousWorking = HostApplicationServices.WorkingDatabase;
            HostApplicationServices.WorkingDatabase = db;
            try
            {
                Draw(doc, db, points, isLeft, result);
            }
            finally
            {
                HostApplicationServices.WorkingDatabase = previousWorking;
            }
            return result;
        }

        private static void Draw(Document doc, Database db, IReadOnlyList<ServitudeLabelPlacement> points,
            IReadOnlyList<bool> isLeft, ServitudePointDrawResult result)
        {
            using (doc.LockDocument())
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DrawingWriter.EnsureLayer(db, tr, ServitudePointBlockNames.Layer, ServitudePointBlockNames.LayerColor);
                    EnsureRegApp(db, tr);
                    ObjectId styleId = EnsureTextStyle(db, tr);
                    ObjectId blockId = EnsureBlock(db, tr, styleId);

                    var block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                    var definitions = new List<AttributeDefinition>();
                    foreach (ObjectId id in block)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad) || ad.Constant) continue;

                        // A definition made by the earlier build locks the text to the block: the grip would then move the whole
                        // point. Unlock it, so a label can be rotated or moved on its own
                        if (ad.LockPositionInBlock)
                        {
                            ad.UpgradeOpen();
                            ad.LockPositionInBlock = false;
                            result.DefinitionsUnlocked++;
                        }
                        definitions.Add(ad);
                    }

                    // Every insert of ours from a previous run, wherever it is in the drawing: the numbering may have changed,
                    // and an earlier build also drew blocks on points outside the picked parcels
                    foreach (ObjectId id in block.GetBlockReferenceIds(true, false))
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br) || br.IsErased) continue;
                        if (!IsTagged(br)) continue;
                        br.UpgradeOpen();
                        br.Erase();
                        result.Replaced++;
                    }

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    for (int i = 0; i < points.Count; i++)
                    {
                        try
                        {
                            Insert(db, tr, modelSpace, blockId, definitions, points[i]);
                            result.Drawn++;
                            if (i < isLeft.Count && isLeft[i]) result.Left++; else result.Right++;
                        }
                        catch (InvalidOperationException ex)
                        {
                            result.Failures.Add($"Точка {points[i].Label}: {ex.Message}");
                        }
                    }
                    tr.Commit();
                }

                // The refresh needs the document lock too (eLockViolation outside it); never let it turn a drawn result into a failure
                try
                {
                    doc.TransactionManager.QueueForGraphicsFlush();
                    doc.Editor.UpdateScreen();
                }
                catch (Autodesk.AutoCAD.Runtime.Exception ex)
                {
                    result.RefreshWarning = $"екранът не е опреснен ({ex.ErrorStatus}) — блоковете са начертани, опреснете с REGEN.";
                }
            }
        }

        private static void Insert(Database db, Transaction tr, BlockTableRecord space, ObjectId blockId,
            List<AttributeDefinition> definitions, ServitudeLabelPlacement point)
        {
            string step = "new BlockReference";
            var br = new BlockReference(new Point3d(point.PointX, point.PointY, 0), blockId);
            try
            {
                // The block itself is not rotated: the number text carries its own rotation and alignment point (world
                // coordinates), so rotating or moving the text by grip leaves the point where it is
                step = "BlockReference.SetDatabaseDefaults/Layer/ScaleFactors/Rotation";
                br.SetDatabaseDefaults(db);
                br.Layer = ServitudePointBlockNames.Layer;
                br.ScaleFactors = new Scale3d(1.0);
                br.Rotation = 0;
                step = "BlockTableRecord.AppendEntity(BlockReference)";
                space.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                step = "BlockReference.XData";
                br.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, ServitudePointBlockNames.XDataApp),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, ServitudePointBlockNames.XDataValue));

                foreach (AttributeDefinition ad in definitions)
                {
                    step = $"AttributeReference.SetAttributeFromBlock({ad.Tag})";
                    var ar = new AttributeReference();
                    ar.SetAttributeFromBlock(ad, br.BlockTransform);
                    ar.LockPositionInBlock = false;

                    if (string.Equals(ad.Tag, ServitudePointBlockNames.NumberTag, StringComparison.OrdinalIgnoreCase))
                    {
                        // The order the 07 settled on: text, justification, alignment point, rotation, then AdjustAlignment
                        step = "AttributeReference.TextString";
                        ar.TextString = point.Label;
                        step = "AttributeReference.HorizontalMode/VerticalMode";
                        ar.HorizontalMode = point.MiddleLeft ? TextHorizontalMode.TextLeft : TextHorizontalMode.TextRight;
                        ar.VerticalMode = TextVerticalMode.TextVerticalMid;
                        step = "AttributeReference.AlignmentPoint";
                        ar.AlignmentPoint = new Point3d(point.AlignX, point.AlignY, 0);
                        step = "AttributeReference.Rotation";
                        ar.Rotation = point.Rotation;
                    }

                    step = $"BlockReference.AttributeCollection.AppendAttribute({ad.Tag})";
                    br.AttributeCollection.AppendAttribute(ar);
                    tr.AddNewlyCreatedDBObject(ar, true);

                    step = "AttributeReference.AdjustAlignment";
                    ar.AdjustAlignment(db);
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                // Don't leave a half-built block behind; the caller reports the failed point and goes on
                if (!br.IsErased && br.ObjectId.IsValid) br.Erase();
                throw new InvalidOperationException($"{step} -> {ex.ErrorStatus}", ex);
            }
        }

        /// <summary>The NUM_Align text style, created (simplex.shx, height 0, width factor 0.65) when the drawing has none.</summary>
        private static ObjectId EnsureTextStyle(Database db, Transaction tr)
        {
            var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (table.Has(ServitudePointBlockNames.TextStyle)) return table[ServitudePointBlockNames.TextStyle];

            table.UpgradeOpen();
            var style = new TextStyleTableRecord
            {
                Name = ServitudePointBlockNames.TextStyle,
                FileName = ServitudePointBlockNames.TextStyleFont,
                TextSize = 0,
                XScale = ServitudePointBlockNames.TextStyleWidthFactor
            };
            ObjectId id = table.Add(style);
            tr.AddNewlyCreatedDBObject(style, true);
            return id;
        }

        /// <summary>
        /// The SERV_TOCHKA definition, created in code when the drawing has none: a DBPoint at the base point on layer 0
        /// (so it takes the insert's layer and the drawing's PDMODE/PDSIZE) and the NOMER attribute.
        /// </summary>
        private static ObjectId EnsureBlock(Database db, Transaction tr, ObjectId styleId)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(ServitudePointBlockNames.BlockName)) return bt[ServitudePointBlockNames.BlockName];

            bt.UpgradeOpen();
            var record = new BlockTableRecord
            {
                Name = ServitudePointBlockNames.BlockName,
                Origin = Point3d.Origin
            };
            ObjectId blockId = bt.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);

            var marker = new DBPoint(Point3d.Origin) { Layer = "0" };
            record.AppendEntity(marker);
            tr.AddNewlyCreatedDBObject(marker, true);

            var number = new AttributeDefinition
            {
                Tag = ServitudePointBlockNames.NumberTag,
                Prompt = ServitudePointBlockNames.NumberPrompt,
                TextString = string.Empty,
                Height = ServitudePointBlockNames.TextHeight,
                WidthFactor = ServitudePointBlockNames.TextStyleWidthFactor,
                TextStyleId = styleId,
                Layer = "0",
                HorizontalMode = TextHorizontalMode.TextRight,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                Rotation = 0,
                LockPositionInBlock = false     // the text can be moved and rotated by grip without moving the point
            };
            number.AlignmentPoint = new Point3d(-ServitudePointPlacement.LabelOffsetM, 0, 0);
            record.AppendEntity(number);
            tr.AddNewlyCreatedDBObject(number, true);

            return blockId;
        }

        private static bool IsTagged(BlockReference br)
        {
            using (ResultBuffer? xdata = br.GetXDataForApplication(ServitudePointBlockNames.XDataApp))
            {
                if (xdata == null) return false;
                return xdata.AsArray().Any(tv => tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString &&
                                                  (tv.Value as string) == ServitudePointBlockNames.XDataValue);
            }
        }

        private static void EnsureRegApp(Database db, Transaction tr)
        {
            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (table.Has(ServitudePointBlockNames.XDataApp)) return;
            table.UpgradeOpen();
            var record = new RegAppTableRecord { Name = ServitudePointBlockNames.XDataApp };
            table.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
        }
    }
}
