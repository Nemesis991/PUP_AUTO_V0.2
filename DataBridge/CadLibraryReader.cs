using Newtonsoft.Json.Linq;
using PUP_AUTO.Core;
using PUP_AUTO.Semantics;

namespace PUP_AUTO.DataBridge
{
    public class CadLibraryReader
    {
        private readonly Logger _logger;

        public CadLibraryReader(Logger logger)
        {
            _logger = logger;
        }

        public Dictionary<string, ParcelData> LoadLibrary(string fileName)
        {
            var result = new Dictionary<string, ParcelData>();
            
            if (!File.Exists(fileName))
            {
                _logger.LogError($"File missing: {fileName}");
                return result;
            }

            if (fileName.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase))
            {
                return LoadGeoJsonLibrary(fileName);
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to read file {fileName}. Exception: {ex.Message}");
                return result;
            }

            if (lines.Length == 0)
            {
                _logger.LogError($"File is empty: {fileName}");
                return result;
            }

            int loadedRecords = 0;
            int lineNumber = 0;

            foreach (var line in lines)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Split by common delimiters
                string[] parts = line.Split(new[] { ',', ';', '\t' }, StringSplitOptions.None);

                if (parts.Length < 4)
                {
                    _logger.LogWarning($"Malformed line {lineNumber} in {fileName}. Expected at least 4 columns, got {parts.Length}. Skipped.");
                    continue;
                }

                // Safe accessor: returns trimmed value or the fallback if the column is absent.
                string SafeCol(int index, string fallback = "") =>
                    index < parts.Length ? (parts[index].Trim()) : fallback;

                try
                {
                    string parcelId = parts[0].Trim();
                    string owner = parts[1].Trim();
                    string ekatte = parts[2].Trim();
                    
                    if (!double.TryParse(parts[3].Trim(), out double documentArea))
                    {
                        _logger.LogWarning($"Invalid DocumentArea on line {lineNumber} in {fileName}. Skipped.");
                        continue;
                    }

                    if (!result.ContainsKey(parcelId))
                    {
                        result[parcelId] = new ParcelData
                        {
                            ParcelId     = parcelId,
                            Owner        = owner,
                            Ekatte       = ekatte,
                            DocumentArea = documentArea,

                            // --- Cadastral Register Fields (columns 4–11) ---
                            // SafeCol returns "" when the column is absent in legacy files.
                            SubDivision   = SafeCol(4),
                            TerritoryType = SafeCol(5),
                            Usage         = SafeCol(6),
                            Locality      = SafeCol(7),
                            Category      = SafeCol(8),
                            OwnershipType = SafeCol(9),
                            OwnerId       = SafeCol(10),
                            OwnerName     = SafeCol(11)
                        };
                        loadedRecords++;
                    }
                    else
                    {
                        _logger.LogWarning($"Duplicate ParcelId {parcelId} on line {lineNumber} in {fileName}. Skipped.");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error parsing line {lineNumber} in {fileName}: {ex.Message}. Skipped.");
                }
            }

            _logger.LogSuccess($"Successfully loaded {loadedRecords} records from {fileName} (CSV)");
            return result;
        }

        private Dictionary<string, ParcelData> LoadGeoJsonLibrary(string fileName)
        {
            var result = new Dictionary<string, ParcelData>();
            int loadedRecords = 0;

            try
            {
                string jsonContent = File.ReadAllText(fileName);
                JObject root = JObject.Parse(jsonContent);
                JArray features = (JArray)root["features"]!;

                if (features == null)
                {
                    _logger.LogError($"No 'features' array found in GeoJSON file {fileName}.");
                    return result;
                }

                foreach (JObject feature in features)
                {
                    JObject? props = (JObject?)feature["properties"];
                    if (props == null) continue;

                    string ident = props["Идентификатор"]?.ToString() ?? "";
                    string ekatteStr = props["ЕКАТТЕ / Населено място"]?.ToString() ?? "";
                    
                    // Extract ekatte code (e.g. "44327" from "44327 - гр. Луковит")
                    string ekatte = ekatteStr.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

                    // Construct parcelId matching CAD (e.g. 44327.31.111)
                    string parcelId = ident;
                    if (!string.IsNullOrEmpty(ekatte) && !parcelId.StartsWith(ekatte))
                    {
                        parcelId = $"{ekatte}.{ident}";
                    }

                    if (string.IsNullOrEmpty(parcelId)) continue;

                    double area = 0;
                    if (double.TryParse(props["Площ по КК (кв.м)"]?.ToString(), out double a))
                    {
                        area = a;
                    }

                    // Extract owner from rights_data JSON string
                    string ownerName = "";
                    string? rightsDataStr = props["rights_data"]?.ToString();
                    if (!string.IsNullOrEmpty(rightsDataStr))
                    {
                        try
                        {
                            JArray rightsArray = JArray.Parse(rightsDataStr!);
                            if (rightsArray.Count > 0)
                            {
                                ownerName = rightsArray[0]["person_name"]?.ToString() ?? "";
                            }
                        }
                        catch { } // Ignore JSON parse errors in rights_data
                    }
                    
                    // If owner not found in rights_data, fallback to Арендатори / Ползватели
                    if (string.IsNullOrEmpty(ownerName))
                    {
                        ownerName = props["Арендатори / Ползватели"]?.ToString() ?? "";
                    }

                    if (!result.ContainsKey(parcelId))
                    {
                        result[parcelId] = new ParcelData
                        {
                            ParcelId     = parcelId,
                            Owner        = ownerName, // We populate both Owner and OwnerName for compatibility
                            Ekatte       = ekatte,
                            DocumentArea = area,

                            TerritoryType = props["Вид територия (ЗСПЗЗ)"]?.ToString() ?? "",
                            Usage         = props["Нов НТП по ЗКИР"]?.ToString() ?? "",
                            Locality      = props["Местност"]?.ToString() ?? "",
                            Category      = props["Категория земя"]?.ToString() ?? "",
                            OwnershipType = props["Вид собственост"]?.ToString() ?? "",
                            OwnerName     = ownerName
                        };
                        loadedRecords++;
                    }
                }

                _logger.LogSuccess($"Successfully loaded {loadedRecords} records from {fileName} (GeoJSON)");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to parse GeoJSON file {fileName}. Exception: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Loads GeoJSON polygon geometries and computes their centroids.
        /// Used for spatial matching of AutoCAD polylines to parcel IDs.
        /// </summary>
        public List<GeoParcel> LoadGeoJsonGeometries(string fileName)
        {
            var parcels = new List<GeoParcel>();

            if (!File.Exists(fileName) || !fileName.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase))
            {
                return parcels;
            }

            try
            {
                string jsonContent = File.ReadAllText(fileName);
                JObject root = JObject.Parse(jsonContent);
                JArray? features = (JArray?)root["features"];
                if (features == null) return parcels;

                foreach (JObject feature in features)
                {
                    JObject? props = (JObject?)feature["properties"];
                    if (props == null) continue;

                    string ident = props["Идентификатор"]?.ToString() ?? "";
                    string ekatteStr = props["ЕКАТТЕ / Населено място"]?.ToString() ?? "";
                    string ekatte = ekatteStr.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

                    string parcelId = ident;
                    if (!string.IsNullOrEmpty(ekatte) && !parcelId.StartsWith(ekatte))
                    {
                        parcelId = $"{ekatte}.{ident}";
                    }

                    if (string.IsNullOrEmpty(parcelId)) continue;

                    double area = 0;
                    if (double.TryParse(props["Площ по КК (кв.м)"]?.ToString(), out double a))
                    {
                        area = a;
                    }

                    // Extract polygon centroid from geometry coordinates
                    JObject? geometry = (JObject?)feature["geometry"];
                    if (geometry == null) continue;

                    string geoType = geometry["type"]?.ToString() ?? "";
                    JArray? coordinates = (JArray?)geometry["coordinates"];
                    if (coordinates == null) continue;

                    double cx = 0, cy = 0;
                    int pointCount = 0;

                    try
                    {
                        // For Polygon: coordinates is [ring][point][x,y]
                        // For MultiPolygon: coordinates is [polygon][ring][point][x,y]
                        JArray ring;
                        if (geoType == "Polygon")
                        {
                            ring = (JArray)coordinates[0]; // outer ring
                        }
                        else if (geoType == "MultiPolygon")
                        {
                            ring = (JArray)((JArray)coordinates[0])[0]; // first polygon, outer ring
                        }
                        else
                        {
                            continue;
                        }

                        foreach (JArray point in ring)
                        {
                            cx += (double)point[0];
                            cy += (double)point[1];
                            pointCount++;
                        }

                        if (pointCount > 0)
                        {
                            cx /= pointCount;
                            cy /= pointCount;
                        }
                    }
                    catch
                    {
                        continue; // Skip malformed geometry
                    }

                    if (pointCount > 0)
                    {
                        parcels.Add(new GeoParcel
                        {
                            ParcelId = parcelId,
                            CentroidX = cx,
                            CentroidY = cy,
                            AreaSqM = area
                        });
                    }
                }

                _logger.LogSuccess($"Loaded {parcels.Count} parcel geometries for spatial matching from {Path.GetFileName(fileName)}.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to load GeoJSON geometries: {ex.Message}");
            }

            return parcels;
        }
    }
}
