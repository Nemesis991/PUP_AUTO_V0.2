using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace PUP_AUTO.Geometry
{
    /// <summary>What was found while reading the P-tag values of one pole block.</summary>
    public sealed class PoleValueStats
    {
        /// <summary>Values read exactly from a field (not rounded by the drawing's UNITS precision).</summary>
        public int FromFields { get; set; }

        /// <summary>Attributes that have a field whose raw value could not be read: the (rounded) text was used.</summary>
        public int FieldsUnreadable { get; set; }

        /// <summary>The exact "X, Y" strings built from raw values; any other P-tag string came from the text.</summary>
        public HashSet<string> ExactValues { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The field code of the first field met (for the log), else null.</summary>
        public string? FieldCodeSample { get; set; }
    }

    /// <summary>
    /// Reads a pole block's P-tag attribute as "X, Y". When the attribute is a field its raw value is taken (unformatted, so the
    /// drawing's UNITS precision does not round it); otherwise the text is returned. The drawing is never changed: no field is
    /// evaluated and no setting touched.
    /// </summary>
    public static class PoleAttributeValues
    {
        public static string Read(AttributeReference attribute, Transaction tr, PoleValueStats stats)
        {
            string text = attribute.TextString;
            if (!attribute.HasFields) return text;

            try
            {
                ObjectId fieldId = attribute.GetField();
                if (fieldId.IsNull) return text;

                var field = (Field)tr.GetObject(fieldId, OpenMode.ForRead);
                string code = field.GetFieldCode(FieldCodeFlags.FieldCode);
                stats.FieldCodeSample ??= code;

                if (TryPoint(field.Value, out double x, out double y) || TryObjectProperty(code, tr, out x, out y))
                {
                    // The raw value must be what the text shows, rounded; otherwise the field points somewhere else
                    if (CoordinateText.IsConsistent(x, y, text))
                    {
                        string exact = CoordinateText.FormatPoint(x, y);
                        stats.ExactValues.Add(exact);
                        stats.FromFields++;
                        return exact;
                    }
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // fall through to the text
            }

            stats.FieldsUnreadable++;
            return text;
        }

        private static bool TryPoint(object? value, out double x, out double y)
        {
            x = y = 0;
            switch (value)
            {
                case Point3d p3: x = p3.X; y = p3.Y; return true;
                case Point2d p2: x = p2.X; y = p2.Y; return true;
                case double[] array when array.Length >= 2: x = array[0]; y = array[1]; return true;
                default: return false;
            }
        }

        /// <summary>Follows <c>Object(%&lt;\_ObjId n&gt;%).Property</c> to the object and reads that point property directly.</summary>
        private static bool TryObjectProperty(string fieldCode, Transaction tr, out double x, out double y)
        {
            x = y = 0;
            var reference = CoordinateText.ParseObjectProperty(fieldCode);
            if (reference == null) return false;

            var id = new ObjectId(new IntPtr(reference.Value.ObjectId));
            if (id.IsNull || id.IsErased) return false;
            DBObject target = tr.GetObject(id, OpenMode.ForRead);

            string property = reference.Value.Property;
            if (property == "InsertionPoint") property = "Position";   // COM name -> .NET name
            System.Reflection.PropertyInfo? info = target.GetType().GetProperty(property);
            return info != null && TryPoint(info.GetValue(target, null), out x, out y);
        }
    }
}
