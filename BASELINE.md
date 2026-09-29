# PUP_AUTO — Baseline capture (before refactor)

Branch: `refactor/cleanup` (created from `master` @ `f2ad599`).

> **Working tree note:** when the branch was created the tree was **not clean**:
> `DataBridge/BasicExcelExporter.cs` has the uncommitted MVP header rename, and `.vscode/` is untracked.
> Commit the header rename (or stash it) **before** capturing the baseline, so the baseline maps to a commit.
> The baseline must be produced with the DLL built from exactly that commit.

---

## 1. Build status at baseline

`dotnet build PUP_AUTO.csproj --no-incremental` → **0 errors, 18 warnings**:

| # | File:line | Code | Message |
|---|-----------|------|---------|
| 1 | DataBridge/BasicExcelExporter.cs:77 | CS8602 | Dereference of a possibly null reference (`AssignedPoleNumbers[0]`) |
| 2 | DataBridge/BasicExcelExporter.cs:97 | CS8602 | Dereference of a possibly null reference (`AssignedPoleNumbers[i]`) |
| 3 | DataBridge/CadLibraryReader.cs:131 | CS8600 | Null → non-nullable (`(JArray)root["features"]`) |
| 4 | DataBridge/CadLibraryReader.cs:141 | CS8600 | Null → non-nullable (`(JObject)feature["properties"]`) |
| 5 | DataBridge/CadLibraryReader.cs:167 | CS8600 | Null → non-nullable (`rights_data?.ToString()`) |
| 6 | DataBridge/CadLibraryReader.cs:172 | CS8604 | Possible null argument to `JArray.Parse` |
| 7 | DataBridge/CadLibraryReader.cs:235 | CS8600 | Null → non-nullable (`features`) |
| 8 | DataBridge/CadLibraryReader.cs:240 | CS8600 | Null → non-nullable (`properties`) |
| 9 | DataBridge/CadLibraryReader.cs:262 | CS8600 | Null → non-nullable (`geometry`) |
| 10 | DataBridge/CadLibraryReader.cs:266 | CS8600 | Null → non-nullable (`coordinates`) |
| 11 | Geometry/GeometrySanitizer.cs:11 | CS8603 | Possible null return (`Sanitize` returns `null` but is declared non-nullable) |
| 12 | Geometry/TopologyProcessor.cs:313 | CS8600 | Null → non-nullable (`using (Region r1 = SafeCreateRegion(...))`) |
| 13 | Geometry/TopologyProcessor.cs:314 | CS8600 | same (`r2`) |
| 14 | Geometry/TopologyProcessor.cs:341 | CS8600 | same (`r1`) |
| 15 | Geometry/TopologyProcessor.cs:342 | CS8600 | same (`r2`) |
| 16 | Geometry/TopologyProcessor.cs:357 | CS8600 | same (`rPole`) |
| 17 | UI/MainCommands.cs:87 | CS8600 | Null → non-nullable (`as Polyline`) |
| 18 | UI/Windows/MainWindow.cs:35 | CS0414 | `_chkMarkers` is assigned but never used |

All 18 warnings are nullable annotations (17) or dead code (1). None are expected to change behaviour.

---

## 2. Inventory: every command / button and what it outputs

> **Update (refactor step 1):** `PUP_GENERATE` and the ribbon button `Генерирай Отчети` were removed on purpose. Runs 3.1 and 3.2 are obsolete.

### 2.1 AutoCAD commands (`[CommandMethod]`, all in `UI/MainCommands.cs`)

| Command | Line | What it does | Outputs |
|---------|------|--------------|---------|
| `PUP_WINDOW` | 35 | Opens the modeless WPF window `MainWindow` | None directly (see 2.3) |
| `PUP_SERV` | 55 | Prompts for a polyline and a segment length (default **20.0**), then densifies and cleans it with `GeometrySanitizer` | **Drawing:** 1 new polyline on layer `segmented SERV` (ACI 3, width 0.5) in the *current space*. Creates the layer if it is missing. |
| `PUP_DRAW_FOOTPRINTS` | 131 | For **every** block reference in Model Space, reads the P1..P4 attributes and draws the footprint | **Drawing:** a footprint polyline on `POLE_STEPS`, 2 diagonals on `diagonali`, 1 DBText (pole number) on `Текст`. Creates the layers if they are missing. **File:** appends to `PUP_AUTO_Logs.txt` |
| `PUP_GENERATE` | 255 | Command-line pipeline: pick the servitude, then poles, then parcels. Loads `<dwg dir>\_TestFiles\TemplateC.cad`. Computes topology and writes every report. | **Files in the DWG folder:** `PUP_Report.xls`, 7 × `D306-31Y0-0x-0A - ….docm` (02–08), and appended lines in `PUP_AUTO_Logs.txt` |

### 2.2 Ribbon (tab `ПУП АВТОМАТИЗАЦИЯ` → panel `Генериране`, `UI/App.cs`)

| Button | Line | Sends |
|--------|------|-------|
| `Генерирай Отчети` | 78 | `PUP_GENERATE ` |
| `Отвори Прозорец` | 89 | `PUP_WINDOW ` |

### 2.3 `MainWindow` (opened by `PUP_WINDOW`, `UI/Windows/MainWindow.cs`)

| Control | Line | What it does | Outputs |
|---------|------|--------------|---------|
| `📁 CSV Регистър` | 126 | File dialog for the register (.cad/.csv/.geojson). The default is `_TestFiles\TemplateC.cad`. | state only |
| `📁 Шаблони` | 135 | Folder dialog for the templates | state only (**ignored by the generators**, see AUDIT bug #2) |
| `🔲 Сервитут` | 154 | Pick 1 polyline | state only |
| `📍 Стълбове` | 160 | Pick blocks, then extract the footprints | state only |
| `🗺️ Имоти` | 166 | Pick polylines. If the register is a `.geojson`, parcels are matched by centroid. | state only |
| `📍 Само Точки (20м)` | 190 | Places a DBPoint and a DBText every 20 m on both sides of the servitude, numbered from `Старт Ляво` (5001) and `Старт Дясно` (1) | **Drawing:** DBPoint + DBText entities (current layer) |
| `✂ Сегментиране` | 208 | Same as `PUP_SERV`, using the `Разстояние` box (default **50**) | **Drawing:** 1 polyline on `segmented SERV` |
| `🚀 ГЕНЕРИРАЙ ОТЧЕТИ` + ☑ `Excel` | 243/176 | Full pipeline | `PUP_Report.xls` |
| … + ☑ `Word` | 177 | | `.docm` 02, 03, 04, 05, 06 (+ 07, 08 only if ☑ Coordinates) |
| … + ☑ `координатни регистри` | 178 | Only takes effect together with ☑ Word | adds `.docm` 07 and 08 |
| … + ☑ `🧪 MVP Математически тест (Excel)` | 179 | **Replaces** the whole pipeline (early `return`, MainWindow.cs:564-571) | `MVP_Math_Test_Parcels.xlsx` only |

All file outputs are written to the **folder of the active DWG** with fixed names, so every run overwrites the previous one. `PUP_AUTO_Logs.txt` is **appended** to, never overwritten.

---

## 3. How to capture the baseline (manual, in AutoCAD)

### 3.0 One-time preparation

1. Build and load the DLL from the baseline commit (`dotnet build -c Release`, then NETLOAD `bin\Release\net48\PUP_AUTO.dll`). Write the commit hash in `baseline\README.txt`.
2. Pick **one reference DWG** that has a servitude polyline, pole blocks (with P1..P4 attributes) and parcel polylines, and store it as `baseline\_reference\reference.dwg`. **Never save over it.** For each run, copy it to a scratch folder and open the copy.
3. Put `_Templates\` and `_TestFiles\` next to the scratch copy, because the plugin resolves both relative to the DWG.
4. **Make the selections reproducible.** Pole order and MVP row order follow the *selection order* (see AUDIT bug #1). So:
   - Servitude: click it (single pick, so order doesn't matter).
   - Poles and parcels: at `Select objects:` type `W`, then type **both window corners as coordinates** (e.g. `4700,690` ↵ `5400,750` ↵). Write the corners in `baseline\README.txt` and reuse them for every run, before and after the refactor.
5. The outputs contain **owner names and ЕГН/БУЛСТАТ** (personal data). Keep `baseline\` out of git by adding `baseline/` to `.gitignore` in a separate commit.

For every run below: open a **fresh copy** of `reference.dwg`, delete `PUP_AUTO_Logs.txt` from the scratch folder, run the steps, then copy the listed outputs to `baseline\<folder>\`. Close AutoCAD's copy **without saving** unless a step says otherwise.

### 3.1 `baseline\PUP_GENERATE\`

1. Command line: `PUP_GENERATE`.
2. Pick the servitude, then poles (window by coordinates), then parcels (window by coordinates).
3. Copy: `PUP_Report.xls`, all `D306-31Y0-0*.docm`, `PUP_AUTO_Logs.txt`.
4. Press F2 and paste the command-line text into `commandline.txt`.

### 3.2 `baseline\RIBBON_Generate\`

Same as 3.1, but start from the ribbon button **Генерирай Отчети**. The output should be identical to 3.1. This run proves the ribbon wiring works.

### 3.3 `baseline\PUP_WINDOW_Generate_All\`

1. Ribbon **Отвори Прозорец** (or `PUP_WINDOW`).
2. Leave the default register (`TemplateC.cad`) and default templates.
3. Click `🔲 Сервитут`, `📍 Стълбове`, then `🗺️ Имоти`, in that order (windows by coordinates).
4. Checkboxes: ☑ Excel, ☑ Word, ☑ Coordinates, ☐ MVP.
5. Click `🚀 ГЕНЕРИРАЙ ОТЧЕТИ`, **once**. (A second click in the same window is known to misbehave: AUDIT bug #4.)
6. Copy: `PUP_Report.xls`, `D306-*.docm`, `PUP_AUTO_Logs.txt`. Save the window's log text box (Ctrl+A, Ctrl+C) as `window_log.txt`.

### 3.4 `baseline\PUP_WINDOW_Generate_GeoJSON\` (only if the reference DWG is in the Луковит area)

Same as 3.3, but use `📁 CSV Регистър` to choose `_Templates\Луковит-Справка от КК 01-440686-23_06_2026_44327.geojson` **before** picking parcels.

### 3.5 `baseline\PUP_WINDOW_MVP\` (button "MVP Математически тест (Excel)")

1. Fresh window. Pick servitude, poles and parcels as in 3.3.
2. ☑ `🧪 MVP Математически тест (Excel)` (the other boxes don't matter).
3. Click `🚀 ГЕНЕРИРАЙ ОТЧЕТИ` once.
4. Copy: `MVP_Math_Test_Parcels.xlsx`, `PUP_AUTO_Logs.txt`, `window_log.txt`.

### 3.6 `baseline\PUP_WINDOW_Markers\`

1. Fresh window. Pick the servitude only.
2. Keep `Старт Ляво` = 5001 and `Старт Дясно` = 1. Click `📍 Само Точки (20м)`.
3. Write down in `notes.txt` whether points and texts **appear** in the drawing. They may not (AUDIT bugs #5–#7).
4. **Before closing the window**, run `DXFOUT` → `baseline\PUP_WINDOW_Markers\result.dxf` (ASCII, 16 decimals).
5. Close the window. Write in `notes.txt` whether the points **are still there** after it closes.
6. Copy `window_log.txt` and `PUP_AUTO_Logs.txt`.

### 3.7 `baseline\PUP_WINDOW_Segment\`

1. Fresh window. Pick the servitude **and** poles.
2. Keep `Разстояние` = 50. Click `✂ Сегментиране`.
3. Write down in `notes.txt` whether a green polyline on `segmented SERV` appeared, and what the `📍 Стълбове` label says afterwards.
4. `DXFOUT` → `result.dxf`. Copy `window_log.txt`.

### 3.8 `baseline\PUP_SERV\`

1. `PUP_SERV`, pick the servitude, accept the default `20`.
2. `DXFOUT` → `result.dxf`. Paste the command-line text into `commandline.txt`.

### 3.9 `baseline\PUP_DRAW_FOOTPRINTS\`

1. `PUP_DRAW_FOOTPRINTS` (no prompts; it processes every block in Model Space).
2. `DXFOUT` → `result.dxf`. Copy `PUP_AUTO_Logs.txt` and `commandline.txt`.

When you're done, the `baseline\` folder should contain:

```
baseline\
  README.txt                 (commit hash, window corners, AutoCAD version, Windows region/decimal symbol)
  _reference\reference.dwg
  PUP_GENERATE\  RIBBON_Generate\  PUP_WINDOW_Generate_All\  [PUP_WINDOW_Generate_GeoJSON\]
  PUP_WINDOW_MVP\  PUP_WINDOW_Markers\  PUP_WINDOW_Segment\  PUP_SERV\  PUP_DRAW_FOOTPRINTS\
```

Put the **Windows regional format / decimal symbol** in README.txt. Some Word outputs depend on it (AUDIT bug #12), and the "after" run must use the same setting.

---

## 4. Proposed diff tool (not created yet)

Proposal: a small console project `tools/BaselineDiff/` (net10.0; the installed SDK is 10.0.301) using packages that are already in the local NuGet cache: **NPOI 2.7.2** (reads both `.xls` and `.xlsx`) and **DocumentFormat.OpenXml 2.20.0** (`.docm` table text).

```
BaselineDiff excel  <before.xls|xlsx> <after.xls|xlsx> [--formats]
BaselineDiff word   <before.docm> <after.docm>
BaselineDiff folder <baseline\X> <after\X>         (runs the right mode per file; exit code 0 = identical)
```

Excel mode compares, cell by cell: the sheet count and names, every cell's **type + value** (so the text `"1.000"` ≠ the number `1`), header text included, merged regions, and (with `--formats`) the number-format string. Word mode dumps every table as `cell | cell | …` lines and diffs the text, because byte-comparing `.docm` fails on zip timestamps.

Core of the Excel mode (sketch):

```csharp
using System.Globalization;
using NPOI.SS.UserModel;
using NPOI.SS.Util;

static int DiffExcel(string before, string after, bool formats)
{
    IWorkbook a = Open(before), b = Open(after);
    int diffs = 0;
    void Report(string where, string x, string y)
    { diffs++; Console.WriteLine($"{where}\n  before: {x}\n  after:  {y}"); }

    if (a.NumberOfSheets != b.NumberOfSheets)
        Report("sheet count", a.NumberOfSheets.ToString(), b.NumberOfSheets.ToString());

    for (int s = 0; s < Math.Min(a.NumberOfSheets, b.NumberOfSheets); s++)
    {
        ISheet sa = a.GetSheetAt(s), sb = b.GetSheetAt(s);
        if (sa.SheetName != sb.SheetName) Report($"sheet {s} name", sa.SheetName, sb.SheetName);

        int lastRow = Math.Max(sa.LastRowNum, sb.LastRowNum);
        for (int r = 0; r <= lastRow; r++)
        {
            IRow? ra = sa.GetRow(r), rb = sb.GetRow(r);
            int lastCol = Math.Max(ra?.LastCellNum ?? 0, rb?.LastCellNum ?? 0);
            for (int c = 0; c < lastCol; c++)
            {
                string va = Describe(ra?.GetCell(c), formats), vb = Describe(rb?.GetCell(c), formats);
                if (va != vb) Report($"[{sa.SheetName}] {new CellReference(r, c).FormatAsString()}", va, vb);
            }
        }
        string ma = Merged(sa), mb = Merged(sb);
        if (ma != mb) Report($"[{sa.SheetName}] merged regions", ma, mb);
    }
    Console.WriteLine(diffs == 0 ? "IDENTICAL" : $"{diffs} difference(s)");
    return diffs == 0 ? 0 : 1;
}

static IWorkbook Open(string path) { using var fs = File.OpenRead(path); return WorkbookFactory.Create(fs); }

static string Merged(ISheet s) => string.Join(",",
    Enumerable.Range(0, s.NumMergedRegions).Select(i => s.GetMergedRegion(i).FormatAsString()).OrderBy(x => x));

static string Describe(ICell? cell, bool formats)
{
    if (cell == null || cell.CellType == CellType.Blank) return "";          // missing == blank
    var type = cell.CellType == CellType.Formula ? cell.CachedFormulaResultType : cell.CellType;
    string v = type switch
    {
        CellType.Numeric => "N:" + cell.NumericCellValue.ToString("R", CultureInfo.InvariantCulture),
        CellType.String  => "S:" + cell.StringCellValue,
        CellType.Boolean => "B:" + cell.BooleanCellValue,
        CellType.Error   => "E:" + cell.ErrorCellValue,
        _                => "?:" + cell
    };
    if (cell.CellType == CellType.Formula) v = "F:" + cell.CellFormula + " = " + v;
    if (formats) v += "  fmt:" + cell.CellStyle?.GetDataFormatString();
    return v;
}
```

For the drawing outputs (`result.dxf`), handles differ between runs, so a text diff of the DXF is noisy. Phase 1 proposal: compare **per-layer entity counts + rounded coordinates** by extending the same tool with a small DXF section parser. Until that exists, compare `notes.txt` and the `Found … Extracted … Failed …` line by hand.

---

## 5. Test project

**None exists** (the solution `PUP_AUTO.slnx` contains only `PUP_AUTO.csproj`). The proposal (not created) is in AUDIT.md §F, step 5. Short version:

- `tests/PUP_AUTO.Tests/PUP_AUTO.Tests.csproj`, **xUnit**, `net48`, referencing `PUP_AUTO.csproj`. The AutoCAD packages are already `PrivateAssets=all` / `ExcludeAssets=runtime`, so they don't flow into the tests.
- **Blocker:** `Semantics/DomainModels.cs` puts AutoCAD types (`ObjectId`, `Point3d`) on `ParcelData`, `Pole` and `ReportRow` (via `List<Pole>`). Loading those types outside `acad.exe` fails, because `AcDbMgd.dll` is mixed-mode. Today, **only `AreaUnits` and `Logger`** are testable without AutoCAD. Refactor step 4 (make `Semantics` AutoCAD-free) unlocks the readers, the merge logic, and golden-file tests of all three exporters.
- The packages (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) are **not** in the local NuGet cache, so the first restore needs internet access.
