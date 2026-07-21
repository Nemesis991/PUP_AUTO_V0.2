using System;
using System.Collections.Generic;
using System.IO;
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

            _logger.LogSuccess($"Successfully loaded {loadedRecords} records from {fileName}");
            return result;
        }
    }
}
