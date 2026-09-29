using Autodesk.AutoCAD.DatabaseServices;

namespace PUP_AUTO.Geometry
{
    /// <summary>
    /// Writes plugin geometry into a drawing: makes sure a layer exists and appends an
    /// entity with its layer / color / line weight.
    /// </summary>
    public static class DrawingWriter
    {
        /// <summary>
        /// Creates the layer (ByAci <paramref name="colorIndex"/>, optional line weight)
        /// if it is not in the layer table yet.
        /// </summary>
        public static void EnsureLayer(
            Database db, Transaction tr, string layerName, short colorIndex, LineWeight? lineWeight = null)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                if (lineWeight.HasValue) ltr.LineWeight = lineWeight.Value;
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        /// <summary>
        /// Sets layer, color and (optionally) line weight on the entity, then appends it to
        /// <paramref name="space"/> (which must be open for write) and registers it with the transaction.
        /// </summary>
        public static void Append(
            BlockTableRecord space, Transaction tr, Entity entity,
            string layerName, short colorIndex, LineWeight? lineWeight = null)
        {
            entity.Layer = layerName;
            entity.ColorIndex = colorIndex;
            if (lineWeight.HasValue) entity.LineWeight = lineWeight.Value;
            space.AppendEntity(entity);
            tr.AddNewlyCreatedDBObject(entity, true);
        }
    }
}
