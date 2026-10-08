using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace PUP_AUTO.Geometry
{
    /// <summary>What was found while reading the P-tag values of one pole block.</summary>
    public sealed class PoleValueStats
    {
        /// <summary>Values read exactly from a field (not rounded by the drawing's UNITS precision).</summary>
        public int FromFields { get; set; }

        /// <summary>The exact "X, Y" strings; any other P-tag string is the text as the drawing shows it.</summary>
        public HashSet<string> ExactValues { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>P-tags that are fields whose raw value could not be read, and whose text is rounded: candidates for the fallback.</summary>
        public List<(string Tag, ObjectId FieldId, string Text)> Pending { get; } = new List<(string, ObjectId, string)>();

        /// <summary>True when the LUPREC fallback was used for this block.</summary>
        public bool FallbackUsed { get; set; }

        /// <summary>The field code of the first field met (for the log), else null.</summary>
        public string? FieldCodeSample { get; set; }
    }

    /// <summary>
    /// Reads a pole block's P-tag attribute as "X, Y" at full precision. The text follows the drawing's UNITS precision, so a
    /// field is read from its raw value (the field itself, its child fields, or the object its code names). When that fails,
    /// <see cref="ReadAtFullPrecision"/> re-evaluates the fields with LUPREC 8 and puts LUPREC and the texts back afterwards.
    /// </summary>
    public static class PoleAttributeValues
    {
        /// <summary>LUPREC used by the fallback.</summary>
        public const int FallbackPrecision = 8;

        private const string FieldDictionary = "ACAD_FIELD";
        private const string TextFieldKey = "TEXT";

        private const int EvaluationContext = (int)(FieldEvaluationContext.Demand | FieldEvaluationContext.Regen);

        public static string Read(string tag, AttributeReference attribute, Transaction tr, PoleValueStats stats)
        {
            string text = attribute.TextString;
            Field? field = FindTextField(attribute, tr);
            if (field == null) return text;   // plain text: what it shows is all there is

            try
            {
                stats.FieldCodeSample ??= field.GetFieldCode(FieldCodeFlags.AddMarkers | FieldCodeFlags.FieldCode);

                if (TryExact(field, tr, out double x, out double y) && CoordinateText.IsConsistent(x, y, text))
                {
                    string exact = CoordinateText.FormatPoint(x, y);
                    stats.ExactValues.Add(exact);
                    stats.FromFields++;
                    return exact;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // fall through
            }

            if (CoordinateText.HasFewerDecimals(text)) stats.Pending.Add((tag, field.ObjectId, text));
            return text;
        }

        /// <summary>
        /// The fallback: sets LUPREC to <see cref="FallbackPrecision"/>, evaluates the pending fields and reads their strings, then
        /// (in finally) restores LUPREC and evaluates the same fields again so the texts are what they were. Needs the document lock
        /// and a transaction. Returns tag -> exact "X, Y" for the fields that gave a consistent full-precision value.
        /// </summary>
        public static Dictionary<string, string> ReadAtFullPrecision(Database db, Transaction tr, PoleValueStats stats)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (stats.Pending.Count == 0) return values;

            int original = db.Luprec;
            stats.FallbackUsed = true;
            try
            {
                db.Luprec = FallbackPrecision;
                foreach ((string tag, ObjectId fieldId, string text) in stats.Pending)
                {
                    var field = (Field)tr.GetObject(fieldId, OpenMode.ForWrite);
                    field.Evaluate(EvaluationContext, db);
                    string full = field.GetStringValue();

                    List<(double X, double Y)> parsed = FootprintCorners.Parse(new[] { full });
                    if (parsed.Count == 1 && CoordinateText.IsConsistent(parsed[0].X, parsed[0].Y, text))
                    {
                        string exact = CoordinateText.FormatPoint(parsed[0].X, parsed[0].Y);
                        stats.ExactValues.Add(exact);
                        stats.FromFields++;
                        values[tag] = exact;
                    }
                }
            }
            finally
            {
                db.Luprec = original;
                foreach ((string _, ObjectId fieldId, string _) in stats.Pending)
                {
                    try
                    {
                        ((Field)tr.GetObject(fieldId, OpenMode.ForWrite)).Evaluate(EvaluationContext, db);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception)
                    {
                        // the next REGEN/UPDATEFIELD rewrites it from the restored LUPREC anyway
                    }
                }
            }
            return values;
        }

        /// <summary>The field behind an object's text: ACAD_FIELD/TEXT in its extension dictionary, else GetField("TEXT").</summary>
        public static Field? FindTextField(DBObject owner, Transaction tr)
        {
            if (!owner.ExtensionDictionary.IsNull &&
                tr.GetObject(owner.ExtensionDictionary, OpenMode.ForRead) is DBDictionary extension &&
                extension.Contains(FieldDictionary) &&
                tr.GetObject(extension.GetAt(FieldDictionary), OpenMode.ForRead) is DBDictionary fields &&
                fields.Contains(TextFieldKey) &&
                tr.GetObject(fields.GetAt(TextFieldKey), OpenMode.ForRead) is Field field)
            {
                return field;
            }

            if (owner.HasFields)
            {
                ObjectId id = owner.GetField(TextFieldKey);
                if (!id.IsNull && tr.GetObject(id, OpenMode.ForRead) is Field byName) return byName;
            }
            return null;
        }

        /// <summary>The raw point of a field: its own value, two numeric child fields (X, Y), a point child, or the named object.</summary>
        private static bool TryExact(Field field, Transaction tr, out double x, out double y)
        {
            if (TryPoint(field.Value, out x, out y)) return true;

            var numbers = new List<double>();
            foreach (ObjectId childId in field.GetChildrenIds())
            {
                if (!(tr.GetObject(childId, OpenMode.ForRead) is Field child)) continue;
                if (TryPoint(child.Value, out x, out y)) return true;
                if (TryNumber(child.Value, out double number)) numbers.Add(number);
            }
            if (numbers.Count >= 2)
            {
                x = numbers[0];
                y = numbers[1];
                return true;
            }

            return TryObjectProperty(field.GetFieldCode(FieldCodeFlags.FieldCode), tr, out x, out y);
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

        private static bool TryNumber(object? value, out double number)
        {
            switch (value)
            {
                case double d: number = d; return true;
                case float f: number = f; return true;
                case int i: number = i; return true;
                default: number = 0; return false;
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

        // -----------------------------------------------------------------
        //  Diagnostic
        // -----------------------------------------------------------------

        /// <summary>
        /// One line per P-tag of a pole block (TP-tags and the number skipped): tag, text, HasFields, whether ACAD_FIELD/TEXT is in the
        /// extension dictionary, the field code, value type and value, the child fields, and for a dynamic block whether the attribute
        /// definition in the dynamic and in the anonymous definition has a field. Changes nothing.
        /// </summary>
        public static List<string> Describe(BlockReference blockRef, Transaction tr)
        {
            var lines = new List<string>();
            var inv = CultureInfo.InvariantCulture;
            foreach (ObjectId attId in blockRef.AttributeCollection)
            {
                if (!(tr.GetObject(attId, OpenMode.ForRead) is AttributeReference att)) continue;
                string tag = att.Tag.ToUpperInvariant();
                if (!tag.StartsWith("P") || tag.StartsWith("TP")) continue;

                Field? field = SafeFind(att, tr);
                string line = $"P-tag {att.Tag}: text \"{att.TextString}\", HasFields={att.HasFields}, ACAD_FIELD/TEXT={HasFieldEntry(att, tr)}";
                if (field != null)
                {
                    line += $", code {field.GetFieldCode(FieldCodeFlags.AddMarkers | FieldCodeFlags.FieldCode)}" +
                            $", value {Describe(field.Value, inv)}, option {field.EvaluationOption}, state {field.State}";
                    int n = 0;
                    foreach (ObjectId childId in field.GetChildrenIds())
                    {
                        if (tr.GetObject(childId, OpenMode.ForRead) is Field child)
                        {
                            line += $"; child {n++}: code {child.GetFieldCode(FieldCodeFlags.FieldCode)}, value {Describe(child.Value, inv)}";
                        }
                    }
                }

                if (blockRef.IsDynamicBlock)
                {
                    line += $"; dynamic: definition attdef field={DefinitionHasField(blockRef.DynamicBlockTableRecord, att.Tag, tr)}" +
                            $", anonymous attdef field={DefinitionHasField(blockRef.BlockTableRecord, att.Tag, tr)}";
                }
                lines.Add(line);
            }
            return lines;
        }

        private static Field? SafeFind(DBObject owner, Transaction tr)
        {
            try { return FindTextField(owner, tr); }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
        }

        private static bool HasFieldEntry(DBObject owner, Transaction tr)
        {
            if (owner.ExtensionDictionary.IsNull) return false;
            if (!(tr.GetObject(owner.ExtensionDictionary, OpenMode.ForRead) is DBDictionary extension) || !extension.Contains(FieldDictionary)) return false;
            return tr.GetObject(extension.GetAt(FieldDictionary), OpenMode.ForRead) is DBDictionary fields && fields.Contains(TextFieldKey);
        }

        private static string DefinitionHasField(ObjectId blockId, string tag, Transaction tr)
        {
            if (blockId.IsNull || !(tr.GetObject(blockId, OpenMode.ForRead) is BlockTableRecord block)) return "no block";
            foreach (ObjectId id in block)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad && string.Equals(ad.Tag, tag, StringComparison.OrdinalIgnoreCase))
                {
                    Field? field = SafeFind(ad, tr);
                    return field == null ? $"no (HasFields={ad.HasFields})" : $"yes, code {field.GetFieldCode(FieldCodeFlags.FieldCode)}";
                }
            }
            return "no attdef";
        }

        private static string Describe(object? value, IFormatProvider inv) =>
            value == null ? "null" : $"{value.GetType().Name} {Convert.ToString(value, inv)}";
    }
}
