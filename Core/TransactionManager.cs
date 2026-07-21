using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace PUP_AUTO.Core
{
    /// <summary>
    /// Wraps AutoCAD document / transaction operations, providing helpers
    /// for opening transactions and prompting users for entity selection.
    /// </summary>
    public class TransactionManager
    {
        private readonly Logger _logger;

        public TransactionManager(Logger logger)
        {
            _logger = logger;
        }

        /// <summary>Returns the active AutoCAD Document.</summary>
        public Document GetActiveDocument()
        {
            return Application.DocumentManager.MdiActiveDocument;
        }

        /// <summary>Returns the Editor of the active document.</summary>
        public Editor GetEditor()
        {
            return GetActiveDocument().Editor;
        }

        /// <summary>Returns the Database of the active document.</summary>
        public Database GetDatabase()
        {
            return GetActiveDocument().Database;
        }

        /// <summary>
        /// Starts and returns a new Transaction on the active database.
        /// The caller is responsible for Commit / Dispose.
        /// </summary>
        public Transaction StartTransaction()
        {
            return GetDatabase().TransactionManager.StartTransaction();
        }

        // -----------------------------------------------------------------
        //  Selection helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Prompts the user to select a single closed Polyline with the given
        /// <paramref name="promptMessage"/>. Returns the Polyline opened
        /// ForRead inside <paramref name="transaction"/>, or null if
        /// selection was cancelled or the entity is not a closed Polyline.
        /// </summary>
        public Polyline? SelectSinglePolyline(
            Transaction transaction,
            string promptMessage)
        {
            Editor ed = GetEditor();
            var opts = new PromptEntityOptions(promptMessage);
            opts.SetRejectMessage("\nPlease select a Polyline.");
            opts.AddAllowedClass(typeof(Polyline), true);

            PromptEntityResult result = ed.GetEntity(opts);
            if (result.Status != PromptStatus.OK)
            {
                _logger.LogWarning("User cancelled polyline selection.");
                return null;
            }

            var pline = transaction.GetObject(result.ObjectId, OpenMode.ForRead) as Polyline;
            if (pline == null || !pline.Closed)
            {
                _logger.LogWarning(
                    $"Selected entity is not a closed Polyline (Handle: {result.ObjectId.Handle}).");
                return null;
            }

            return pline;
        }

        /// <summary>
        /// Prompts the user to select multiple entities filtered to Polylines.
        /// Returns a list of (entityName, Polyline) pairs opened ForRead
        /// inside <paramref name="transaction"/>.
        /// The entity name is derived from XData or Handle as a fallback.
        /// </summary>
        public List<KeyValuePair<string, Polyline>> SelectMultiplePolylines(
            Transaction transaction,
            string promptMessage)
        {
            var polylines = new List<KeyValuePair<string, Polyline>>();
            Editor ed = GetEditor();

            var opts = new PromptSelectionOptions();
            opts.MessageForAdding = promptMessage;

            // Filter to lightweight polylines only
            var filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
            });

            PromptSelectionResult selResult = ed.GetSelection(opts, filter);
            if (selResult.Status != PromptStatus.OK)
            {
                _logger.LogWarning("User cancelled multi-polyline selection.");
                return polylines;
            }

            SelectionSet selSet = selResult.Value;
            foreach (SelectedObject selObj in selSet)
            {
                if (selObj == null) continue;

                var pline = transaction.GetObject(selObj.ObjectId, OpenMode.ForRead) as Polyline;
                if (pline == null || !pline.Closed)
                {
                    _logger.LogWarning(
                        $"Skipped non-closed or invalid polyline (Handle: {selObj.ObjectId.Handle}).");
                    continue;
                }

                // Use the Handle as a default identifier
                string entityId = pline.Handle.ToString();

                // Try to read ParcelId / PoleId from XData if available
                ResultBuffer? xdata = pline.XData;
                if (xdata != null)
                {
                    foreach (TypedValue tv in xdata)
                    {
                        if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                        {
                            entityId = tv.Value.ToString() ?? entityId;
                            break;
                        }
                    }
                }

                polylines.Add(new KeyValuePair<string, Polyline>(entityId, pline));
            }

            _logger.LogSuccess(
                $"Selected {polylines.Count} polylines from '{promptMessage}'.");

            return polylines;
        }
    }
}
