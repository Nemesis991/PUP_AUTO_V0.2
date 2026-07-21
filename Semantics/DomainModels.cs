using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace PUP_AUTO.Semantics
{
    /// <summary>
    /// Represents cadastral parcel data.
    /// </summary>
    public class ParcelData
    {
        public string ParcelId { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;
        public string Ekatte { get; set; } = string.Empty;
        public double DocumentArea { get; set; }
        public ObjectId ObjectId { get; set; } = ObjectId.Null;

        // --- Cadastral Register Fields ---
        public string SubDivision { get; set; } = string.Empty;
        public string TerritoryType { get; set; } = string.Empty;
        public string Usage { get; set; } = string.Empty;
        public string Locality { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string OwnershipType { get; set; } = string.Empty;
        public string OwnerId { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a Right of Way (Servitude) area.
    /// </summary>
    public class Servitude
    {
        public string ServitudeId { get; set; } = string.Empty;
        public string AssignedParcelId { get; set; } = string.Empty;
        public double Area { get; set; }
        public ObjectId ObjectId { get; set; } = ObjectId.Null;
    }

    /// <summary>
    /// Represents a physical transmission tower / pole.
    /// </summary>
    public class Pole
    {
        public string PoleId { get; set; } = string.Empty;
        public string AssignedParcelId { get; set; } = string.Empty;

        /// <summary>Sequential number of the pole along the line.</summary>
        public int PoleNumber { get; set; }

        /// <summary>Footprint area of the pole in Square Meters.</summary>
        public double PoleAreaSqM { get; set; }

        /// <summary>Pole footprint area converted to Decares (SqM / 1000), rounded to 3 decimal places.</summary>
        public double PoleAreaDecares => Math.Round(PoleAreaSqM / 1000.0, 3);

        public Point3d Location { get; set; }
        public ObjectId ObjectId { get; set; } = ObjectId.Null;
    }

    /// <summary>
    /// Structures the data row for output reporting.
    /// </summary>
    public class ReportRow
    {
        public string ParcelId { get; set; } = string.Empty;
        public string Owner { get; set; } = string.Empty;

        // --- Cadastral Register Fields ---
        public string SubDivision { get; set; } = string.Empty;
        public string TerritoryType { get; set; } = string.Empty;
        public string Usage { get; set; } = string.Empty;
        public string Locality { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string OwnershipType { get; set; } = string.Empty;
        public string OwnerId { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;

        // --- Area Fields (Square Meters) ---
        /// <summary>Total parcel area from the cadastral register, in Square Meters.</summary>
        public double DocumentAreaSqM { get; set; }

        /// <summary>Servitude (right-of-way) area in Square Meters.</summary>
        public double ServitudeAreaSqM { get; set; }

        /// <summary>Combined pole footprint area in Square Meters.</summary>
        public double PoleAreaSqM { get; set; }

        public int PoleCount { get; set; }

        // --- Computed Decare Helpers ---
        /// <summary>Total parcel area converted to Decares (SqM / 1000), rounded to 3 decimal places.</summary>
        public double DocumentAreaDecares => Math.Round(DocumentAreaSqM / 1000.0, 3);

        /// <summary>Servitude area converted to Decares (SqM / 1000), rounded to 3 decimal places.</summary>
        public double ServitudeAreaDecares => Math.Round(ServitudeAreaSqM / 1000.0, 3);

        /// <summary>Pole area converted to Decares (SqM / 1000), rounded to 3 decimal places.</summary>
        public double PoleAreaDecares => Math.Round(PoleAreaSqM / 1000.0, 3);
    }
}
