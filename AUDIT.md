# PUP_AUTO — Code audit (read-only)

Scope: every tracked file on `refactor/cleanup` (= `master` @ `f2ad599` + the uncommitted MVP header rename).
Method: I read all 15 `.cs` files in full and ran the build (18 warnings, listed in BASELINE.md §1). I also did one analyzer pass (IDE0005/0051/0060) on a **copy** of the sources in a temp folder. No repo file was changed.
All line numbers refer to the current working tree.

Severity tags: **[H]** high, **[M]** medium, **[L]** low.

---

## A. Map of the codebase

### A.1 Files

| File | Lines | What it does | Called by |
|------|------:|--------------|-----------|
| `UI/App.cs` | 127 | `IExtensionApplication`; builds the ribbon tab on first `Idle`; `RibbonCommandHandler` sends command strings | AutoCAD (assembly attribute, :6) |
| `UI/MainCommands.cs` | 602 | 4 `[CommandMethod]`s: `PUP_WINDOW`, `PUP_SERV`, `PUP_DRAW_FOOTPRINTS`, `PUP_GENERATE`. Also holds the **business-logic** `MergeResultsStatic` (:495) and `ResolveProjectDirectory` (:580) | AutoCAD, ribbon; `MainWindow.cs:604` calls `MergeResultsStatic` |
| `UI/Windows/MainWindow.cs` | 851 | Code-only WPF window. UI build, file dialogs, geometry picking, a long-lived transaction, the whole generate pipeline (2nd copy), MVP test, markers, segmentation | `MainCommands.cs:40` |
| `Core/TransactionManager.cs` | 342 | Despite the name: gets doc/editor/db from `MdiActiveDocument` and holds the selection prompts, parcel-ID resolution (GeoJSON centroid / XData / handle) and pole-ID-from-attribute logic | `MainCommands.cs:137,264`; `MainWindow.cs:544` |
| `Core/Logger.cs` | 50 | Appends lines to a text log; swallows all IO errors | everything |
| `Core/AreaUnits.cs` | 26 | The single m² → дка conversion and 3-decimal rounding (AwayFromZero, InvariantCulture) | DomainModels, Excel/Word/MVP exporters |
| `Geometry/TopologyProcessor.cs` | 477 | Region booleans: servitude∩parcel, pole→parcel overlap, MVP gross/net/pole math, vertex extraction | `MainCommands.cs:265`; `MainWindow.cs:562` |
| `Geometry/GeometrySanitizer.cs` | 144 | Port of the LISP `plseg`/`cleansrv`: densify segments longer than N m, drop vertices closer than 5 cm | `TopologyProcessor.cs:58,76`; `MainCommands.cs:90`; `MainWindow.cs:737` |
| `Geometry/PoleFootprintExtractor.cs` | 191 | Builds a 4-vertex footprint polyline from the `P1..P4` attributes of a (dynamic) pole block | `MainCommands.cs:179,303`; `MainWindow.cs:440` |
| `Geometry/ServitudeMarkerGenerator.cs` | 186 | Splits the servitude into 2 sides and places a DBPoint and DBText every 20 m | `MainWindow.cs:698` |
| `DataBridge/CadLibraryReader.cs` | 330 | Parcel register reader: delimited text **or** GeoJSON (attributes plus centroids for matching). It does **not** read the MKADWIN `.cad` format (see bug #9) | `MainCommands.cs:336,360`; `MainWindow.cs:497,578` |
| `DataBridge/XDataExtractor.cs` | 35 | Reads the parcel ID from `TransCAD` XData | `TransactionManager.cs:176`; `TopologyProcessor.cs:253` |
| `DataBridge/ExcelReportGenerator.cs` | 531 | NPOI/HSSF: fills the 3-sheet `_Templates/TemplateX.xls` and writes `PUP_Report.xls` | `MainCommands.cs:424`; `MainWindow.cs:632` |
| `DataBridge/WordReportGenerator.cs` | 868 | OpenXML: fills 7 `.docm` templates (02–08) by cloning the last table row | `MainCommands.cs:453`; `MainWindow.cs:641` |
| `DataBridge/BasicExcelExporter.cs` | 155 | OpenXML: writes `MVP_Math_Test_Parcels.xlsx` from scratch (no template) | `MainWindow.cs:568` |
| `Semantics/DomainModels.cs` | 182 | `ParcelData`, `Servitude` (unused), `Pole`, `ReportRow`, `VertexCoordinate`, `GeoParcel` | everything |
| `PUP_AUTO.csproj` / `PUP_AUTO.slnx` | 38 / 3 | net48, C# latest, nullable, **ImplicitUsings on**; AutoCAD.NET 24.3 (compile only), NPOI 2.7.2, OpenXml 2.20.0, Newtonsoft 13.0.3 | — |
| `_Bundle/PackageContents.xml` | 17 | Autoload bundle manifest (`AppVersion 0.1.0`, `SeriesMax R24.3`, `./Contents/R24/PUP_AUTO.dll`). No build step produces the bundle | — |
| `_Templates/` | — | 7 `.docm` + `TemplateX.xls` (read-only attr) + 2 sample `.xls` (tracked) + `BLOCKS.dwg` and a GeoJSON (both untracked via `.gitignore`) | the generators |
| `_TestFiles/TemplateC.cad` | 478 | **MKADWIN v4.02 CAD file, Windows-1251**, not CSV (bug #9) | default register path |
| `README.md`, `TECHNICAL_DOCUMENTATION.md`, `Architecture_Plan.md`, `PUP_AUTO_User_Manual.md`, `_Architecture_state/architecture_state.md` | 110–639 | Docs (Bulgarian) | — |

### A.2 Dependency direction

Intended: `UI → (Core, Geometry, DataBridge) → Semantics`, with only `UI`/`Geometry`/`Core` touching the AutoCAD API.

Actual:

```
UI/App ─────────────► (sends command strings)
UI/MainCommands ────► Core.TransactionManager, Core.Logger, Geometry.*, DataBridge.*, Semantics
UI/MainWindow ──────► same  +  UI/MainCommands.MergeResultsStatic          ◄── wrong: window → command class
Core/TransactionManager ► DataBridge.XDataExtractor, Semantics.GeoParcel    ◄── wrong: Core → DataBridge
Geometry/TopologyProcessor ► DataBridge.XDataExtractor                      ◄── wrong: Geometry → DataBridge
Semantics/DomainModels ► Autodesk (ObjectId, Point3d) + Core.AreaUnits     ◄── wrong: domain → AutoCAD API
DataBridge/* ───────► Semantics, Core (OK; no AutoCAD except XDataExtractor)
```

Flagged:
1. **`Semantics` depends on AutoCAD** (`DomainModels.cs:5-8,22,63,83-84`). This blocks unit testing: any test touching `ParcelData`/`Pole`/`ReportRow` needs `AcDbMgd.dll`, which only loads inside `acad.exe`. Of those members, only `Pole.Location` is actually read (`WordReportGenerator.cs:502-503`). `ParcelData.ObjectId` is never set or read, and `Pole.ObjectId` is set (`TopologyProcessor.cs:170`) but never read.
2. **`XDataExtractor` lives in `DataBridge`** but is an AutoCAD entity reader, so `Core` and `Geometry` depend on `DataBridge` because of it (`TransactionManager.cs:176`, `TopologyProcessor.cs:253`).
3. **Business logic in the command class, consumed by the window**: `MainWindow.cs:604` → `MainCommands.MergeResultsStatic`.
4. **`Core.TransactionManager` depends on `Semantics.GeoParcel`** and contains parcel-matching business rules (`TransactionManager.cs:157-188,205-252`).

---

## B. AutoCAD-specific risks (highest priority)

### B.1 Transactions

| # | Where | Finding | Sev |
|---|-------|---------|-----|
| B1 | `MainWindow.cs:52, 536-547` | **Long-lived transaction in a modeless window.** `_activeTransaction` is started on the first pick and kept open across button clicks. It is only committed by a non-MVP Generate (:650) or Segment (:767), and aborted in `OnClosed` (:845). Every other transaction on that database (user commands, `PUP_SERV` run from the command line) **nests inside it** and is rolled back when the window closes. The picked `Polyline`s (:47-49) are DBObjects cached across UI events. | H |
| B2 | `MainWindow.cs:650-652` | After Generate commits and nulls the transaction, `_servitudePline` and `_parcelPolylines` still reference objects opened by the **disposed** transaction. A 2nd Generate, Markers, Segment or MVP run in the same window uses closed objects. (Bug #4) | H |
| B3 | `MainWindow.cs:696-701` | Markers: nested transaction inside `_activeTransaction`. The inner `Commit` doesn't persist anything; if the window is closed without a non-MVP Generate, `OnClosed` aborts and **the markers disappear**. No graphics flush (compare Segment :762-763). (Bug #5) | H |
| B4 | `MainWindow.cs:731-767, 791-827` | Segment: nested transaction, then `CommitAndRefreshTransaction` commits the outer one and re-opens the objects by `ObjectId`. Pole footprints are **in-memory** polylines (`PoleFootprintExtractor.cs:141`, `ObjectId.Null`), so they are skipped at :817 and **all poles are dropped**. (Bug #3) | H |
| B5 | `MainCommands.cs:278-475` | `PUP_GENERATE` keeps one transaction open across all user prompts **and** all Excel/Word file IO. It is committed at the end although nothing was written. The early `return`s (:286,:295,:327,:345) abort through `using`, which is fine. | L |
| B6 | `MainCommands.cs:147, 173-231` | `PUP_DRAW_FOOTPRINTS` opens Model Space **ForWrite** and **appends entities while enumerating it** (`foreach (ObjectId objId in btr)` + `btr.AppendEntity`). Also, running it twice duplicates everything (no idempotency). | M |

### B.2 Open modes, disposal

| # | Where | Finding | Sev |
|---|-------|---------|-----|
| B7 | `PoleFootprintExtractor.cs:141` → `MainCommands.cs:308`, `MainWindow.cs:444` | Footprint `Polyline`s are non-database-resident DBObjects and are **never disposed**. `_polePolylines.Clear()` (:435, :814) drops them without disposing. | M |
| B8 | `ServitudeMarkerGenerator.cs:163, 169` | `DBPoint`/`DBText` are created without `SetDatabaseDefaults()`, so they get whatever the current layer/style is. `PUP_DRAW_FOOTPRINTS` does call it (`MainCommands.cs:218`). | L |
| B9 | `TopologyProcessor.cs:389-416` | `SafeCreateRegion` swallows every exception (`catch { return null; }` :413). Region objects beyond index 0 are disposed (:405-408), which is good. | L |
| — | OpenMode generally | Selection helpers and readers use `ForRead` correctly (`TransactionManager.cs:77,130,287,299`; `PoleFootprintExtractor.cs:49,164`). `ForWrite` is used only on the space BTR / layer table when appending. I found no unnecessary ForWrite. | — |

### B.3 Document locking

| # | Where | Finding | Sev |
|---|-------|---------|-----|
| B10 | whole repo | `LockDocument`/`DocumentLock` appears **nowhere** (grep). The modeless window writes to the database from button handlers (application context): Markers `MainWindow.cs:696-701` (→ `ServitudeMarkerGenerator.cs:146` ForWrite), Segment `:731-758` (ForWrite at :741, layer table :782), commit `:650, :802`. The expected result is `eLockViolation`. **Please confirm in baseline runs 3.6/3.7.** Picks correctly use `StartUserInteraction` (:392,:426,:486), but that does not lock. | H |

### B.4 Top-level exception handling

| # | Where | Finding | Sev |
|---|-------|---------|-----|
| B11 | `MainCommands.cs:134-138`, `:259-267` | `PUP_DRAW_FOOTPRINTS` and `PUP_GENERATE` create `Logger`, `TransactionManager` and `Editor` **outside** their `try`. With no active document, `GetEditor()` (`TransactionManager.cs:35`) throws an NRE, and `new Logger` can throw from `Directory.CreateDirectory` (`Logger.cs:18`). Either way the exception is unhandled inside an AutoCAD command. | M |
| B12 | `MainWindow.cs:392, 426, 486` | `ed.StartUserInteraction(this)` is outside the handler's `try`. An exception in a WPF handler hosted in AutoCAD can take AutoCAD down. `BtnBrowseCad_Click`/`BtnBrowseTemplate_Click` (:353, :368) have no try either (low risk). | M |
| B13 | `UI/App.cs:118-125` | `RibbonCommandHandler.Execute` has no try/catch. | L |
| — | OK | `PUP_WINDOW` (:38-48), `PUP_SERV` (:63-124), `App.OnAppIdle` (:38-48) have top-level catches. | — |

### B.5 Wrong document / database

| # | Where | Finding | Sev |
|---|-------|---------|-----|
| B14 | `TransactionManager.cs:27-50` + `MainWindow.cs:53, 543-546, 696, 731` | `TransactionManager` resolves `MdiActiveDocument` **on every call**, but the window caches `_activeTransaction`, the picked objects and `_projectDir` (:43, set once at :328). If the user switches drawings while the window is open: picks in drawing B run `GetObject` with drawing A's transaction; Markers/Segment start a transaction on B and append entities built from A's servitude to B; and reports for B go to A's folder. `PUP_WINDOW` can also open several windows at once, each with its own open transaction (`MainCommands.cs:40-41`). | H |
| B15 | `ServitudeMarkerGenerator.cs:145` | `pline.Database ?? HostApplicationServices.WorkingDatabase`. `pline` is always an in-memory temp polyline (:88-89), so this is always `WorkingDatabase`, not the servitude's database. | L |
| B16 | `MainCommands.cs:580-600`, `MainWindow.cs:318-347` | For an **unsaved drawing**, `doc.Name` has no directory. `PUP_GENERATE` then falls back to `Environment.CurrentDirectory` (:599), and the window keeps `_projectDir = ""`, so every output path is relative to AutoCAD's process CWD (`MainWindow.cs:540,568,577,631`). | M |

---

## C. Excel / Word export risks

| # | Where | Finding | Sev |
|---|-------|---------|-----|
| C1 | — | **No COM interop.** Excel uses NPOI (`ExcelReportGenerator`) and OpenXML (`BasicExcelExporter`); Word uses OpenXML. No Excel/Word processes are involved, so there are no process leaks. | ✓ |
| C2 | `ExcelReportGenerator.cs:111-116, 118-122, 136-141, 149-154, 291-294` | Every failure (template missing, sheet missing, **output file open in Excel** → `IOException` at :281) is logged and `return`s. The callers then print success anyway: `MainCommands.cs:427` "Report saved", `MainWindow.cs:634` "Excel запазен". | H |
| C3 | `WordReportGenerator.cs:104-108, 192-195, 216-220, 309-312, …` | Same pattern for all 7 Word files. `GenerateAllReports` logs "Word reports generated" unconditionally (:86), and the callers print success (`MainCommands.cs:459`, `MainWindow.cs:646`). A missing template is only a *warning* in the log. | M |
| C4 | `BasicExcelExporter.cs:18` | `SpreadsheetDocument.Create` on a file open in Excel throws. This one **does** reach the UI (`MainWindow.cs:662-671`) but shows a raw exception message plus stack trace. Inconsistent with C2/C3. | L |
| C5 | Hard-coded paths/names | `MainCommands.cs:26-29`; `MainWindow.cs:330, 337, 540, 577, 631`; `ExcelReportGenerator.cs:33-34`; `WordReportGenerator.cs:27-33, 38`; `BasicExcelExporter.cs:16`; `Logger.cs:10`. The same literals (`"_TestFiles"`, `"TemplateC.cad"`, `"_Templates"`, `"PUP_Report.xls"`, `"PUP_AUTO_Logs.txt"`) are repeated in `MainCommands` and `MainWindow`. The Word **output file names equal the template names** (`:102, 214, 326, …`), which is fine only while output dir ≠ template dir. | M |
| C6 | `MainWindow.cs:45, 340, 376` | The **template folder chosen in the UI is never used**. Both generators are built from `_projectDir` (:632, :641). (Bug #2) | H |
| C7 | `CadLibraryReader.cs:38` | `File.ReadAllLines(fileName)` uses the default encoding (UTF-8). The default register `_TestFiles/TemplateC.cad` is **Windows-1251** (verified: its `NAME` header is not valid UTF-8). A Bulgarian CSV exported from Excel/MKAD is usually 1251 too, so its Cyrillic owner names would be garbled. GeoJSON (:129, :233) is UTF-8, which is fine. | H |
| C8 | Cyrillic in outputs | NPOI and OpenXML write Unicode correctly. The source files are UTF-8, so the Cyrillic literals are safe. `Logger` writes UTF-8 without BOM (`File.AppendAllText`), which old Notepad may show as mojibake. | L |
| C9 | Header / label strings duplicated | Balance grouping titles and "(без …)" fallbacks: `ExcelReportGenerator.cs:357-363` = `WordReportGenerator.cs:352-358` (+ `:769-770`). Balance header labels exist only in Excel (`ExcelReportGenerator.cs:366-374`). MVP headers exist only once (`BasicExcelExporter.cs:44-51`) and are **not** used as keys. `"Стълб №"` formatting: `DomainModels.cs:153`, `WordReportGenerator.cs:493`, and a different form `"№{n}"` at `WordReportGenerator.cs:280`. | M |
| C10 | Strings used as **lookup keys** (must stay byte-identical) | Sheet names `ExcelReportGenerator.cs:37-39` (index fallback :189-192, :254-257). GeoJSON property names `CadLibraryReader.cs:144-145, 160, 167, 175, 184, 196-200, 243-244, 256`. Pole attribute tags `TransactionManager.cs:303` **and** `PoleFootprintExtractor.cs:55` (duplicated; `ToUpper()` vs `ToUpperInvariant()`). Visibility property names `PoleFootprintExtractor.cs:179-182`. XData sentinels `"Неизвестен_Имот"`/`"Грешка_XData"` returned at `XDataExtractor.cs:16,26,31` and compared at `TransactionManager.cs:179`. `"NO DATA"` set at `MainCommands.cs:530` and compared at `MainCommands.cs:464`, `MainWindow.cs:654`. RegApp `"TransCAD"` at `XDataExtractor.cs:8`. | M |
| C11 | `BasicExcelExporter.cs:61` | The status text is **Cyrillic** `"ОК"` (bytes `D0 9E D0 9A`), not Latin `"OK"`. Your task spec quotes `"0.000 - OK"`. Anyone filtering or diffing on Latin "OK" will miss it. It has to stay as-is (output format), so I'm only reporting it. | L |
| C12 | `BasicExcelExporter.cs:152` | All MVP values, numbers included, are written as **inline strings**, so Excel can't sum or sort them numerically. That is the current output format, so it must not change without your decision. | L |

---

## D. Correctness risks in the math

| # | Where | Finding | Sev |
|---|-------|---------|-----|
| D1 | m² → дка | **Already centralised**: `AreaUnits.SqmToDka` / `FormatDka` (`AreaUnits.cs:19-24`) is the only `/1000` in the code (grep). All exporters call it. The only leftovers are the computed `*Decares`/`*Dka` properties (`DomainModels.cs:49, 81, 136-145`), which are **unused** (grep). | ✓ |
| D2 | Same quantity, 3 different algorithms | Servitude∩parcel in the main report **sanitises** both polygons (densify at 50 m and drop vertices < 5 cm, which changes the geometry) **without** origin shift (`TopologyProcessor.cs:58, 76-89`). Pole∩parcel in the main report uses **no** sanitising and **no** shift (:182-187). The MVP test uses **origin shift** and **no** sanitising (:300-376). So the MVP "Брутна площ" for a parcel can differ from the "Площ с ограничение" in `PUP_Report.xls` for the same parcel, which means **the MVP test does not validate the main report**. Coordinates are around 4.78e6 / 3.6e5 (`TemplateC.cad` REFERENCE), where the shift matters. | H |
| D3 | Tolerances without names / duplicated | `0.001` m² sliver: `TopologyProcessor.cs:27` (named) and literals at `:389`, `BasicExcelExporter.cs:61` (OK/ГРЕШКА threshold), `:65` (has-pole test). `0.01` closure/duplicate: `TransactionManager.cs:141`, `ServitudeMarkerGenerator.cs:113, 123`. `50.0` m geo-match radius + `0.5` area ratio: `TransactionManager.cs:235, 243`. Sanitize `50.0` / `0.05`: `TopologyProcessor.cs:58, 76`, `GeometrySanitizer.cs:9`; `0.05` again at `MainCommands.cs:90`, `MainWindow.cs:737`. Parasite `10.0`: `GeometrySanitizer.cs:34`. Markers `20.0` step / `15.0` parasite: `ServitudeMarkerGenerator.cs:142-143` (and "20м"/"20m" in UI text `MainWindow.cs:192, 694`, `ServitudeMarkerGenerator.cs:103`). `1e-6`, `1e-10`: `GeometrySanitizer.cs:40, 57, 92, 114`. Text heights `1.0` / `2.0`, offset `1.5`, width `0.5`: `MainCommands.cs:222, 109`, `ServitudeMarkerGenerator.cs:172, 177`, `MainWindow.cs:751`. | M |
| D4 | Two different segmentation defaults | `PUP_SERV` defaults to **20.0** m (`MainCommands.cs:74-75`), the window to **50** (`MainWindow.cs:205, 726`), and the topology uses **50.0** (`TopologyProcessor.cs:58`). These might be intended, but nothing documents it. | L |
| D5 | Rounding at different stages | Areas: rounded once at output (good). **Coordinates**: rounded at extraction with `Math.Round(x, 3)` (**banker's rounding**, ToEven) at `MainCommands.cs:314`, `MainWindow.cs:450`, `TopologyProcessor.cs:469-470`, then formatted again with `"F3"` (`WordReportGenerator.cs:512-513, 579-580`). The pole centre is *not* pre-rounded (:502-503). **Percent**: `Math.Round(…, 2)` ToEven, then `"F2"` (`:400, 410, 678, 690`). **Totals** rows = `round(Σ raw)` (`WordReportGenerator.cs:415-428, 695-708, 796-806`), while the rows above are `round(each)`, so the printed column can differ from the printed total by ±0.001·n. This is by design (see `AreaUnits` doc), but it is exactly the "0.001 mismatch" you asked about. | M |
| D6 | Culture-dependent numbers | Output: `FormatDka` uses **InvariantCulture** (`"0.123"`), but coordinates and percentages use the **current culture** (`WordReportGenerator.cs:410, 502-503, 512-513, 579-580, 690`), and the totals use a hard-coded `"100.00"` (:428, :708). On a bg-BG Windows that mixes `,` and `.` in the same document. Input: `double.TryParse` with the current culture at `CadLibraryReader.cs:79, 160, 256` and `MainWindow.cs:724`. `PoleFootprintExtractor.cs:120-121` correctly uses Invariant. (Bug #12) | H |
| D7 | Two definitions of "remainder" | `ReportRow.RemainderAreaSqM = max(0, DocumentArea(register) − ServitudeGross)` (`DomainModels.cs:132`). `ParcelData.RemainderAreaSqm = max(0, TotalArea(drawn) − Net − Pole)` (`:46`). They use different bases (register vs drawing area) and different subtractions. Both are used in different exports. | M — decision |
| D8 | Duplicated formula | `MathDifference` is computed and stored at `TopologyProcessor.cs:288`, then **recomputed** at `BasicExcelExporter.cs:60`. The stored property is never read. | L |
| D9 | MVP pole area not clipped to the servitude | `PoleAreaSqm` = pole∩**parcel** (`TopologyProcessor.cs:271`), but Net = (parcel∩servitude) − poles (:286). If a footprint sticks out of the servitude, Gross − (Net + Pole) ≠ 0 and the row shows **ГРЕШКА** even though the geometry is valid. Is that the intended check? | M — question |
| D10 | "Net = 0.000 when no pole" | `BasicExcelExporter.cs:64-67` shows 0.000 net for parcels without poles (per the comment, by request). The row then reads Gross − (0 + 0) ≠ 0 but says **ОК**, because the check uses the real net (:60). `Остатък` also uses the real net (`DomainModels.cs:46`). It's consistent in code but can confuse a reader. | L — note |
| D11 | Overwrite instead of sum | `result[parcelId] = area` (`TopologyProcessor.cs:94`) and `pole.OverlappingParcels[parcelId] = area` (:192). If two selected polylines resolve to the **same parcel ID** (same XData, or two polylines matched to the same GeoJSON centroid within 50 m), one piece's area is silently lost. The MVP path uses `IndividualPoleAreas[poleNumber] =` (:277) the same way for duplicate pole numbers. | M |
| D12 | Centroids | 4 separate vertex-average implementations: `TransactionManager.cs:208-219`, `TopologyProcessor.cs:421-436`, `PoleFootprintExtractor.cs:134-136`, `CadLibraryReader.cs:290-300`. The GeoJSON one **includes the duplicated closing vertex**, which biases it toward the first vertex. The AutoCAD one doesn't. | L |

---

## E. Consistency / readability

### E.1 Naming

- **Unit suffix casing**: `Sqm` (`ParcelData.TotalAreaSqm`, `ServitudeGrossAreaSqm`, … `DomainModels.cs:35-46`) vs `SqM` (`ReportRow.*SqM`, `Pole.PoleAreaSqM`, `GeoParcel.AreaSqM`, `PoleFootprintResult.AreaSqM`). `Dka` (`RemainderAreaDka`, `AreaUnits.SqmToDka`) vs `Decares` (`ReportRow.*Decares`, `Pole.PoleAreaDecares`).
- **Pole identity**: `Pole.PoleId` (string key), `Pole.PoleNumber` (int, **never set**), `PoleFootprintResult.PoleNumber` (string), `ParcelData.AssignedPoleNumbers` (strings). 4 names for 2 concepts.
- **Misleading names**: `Core.TransactionManager` is a selection/ID-resolution service and collides with `Autodesk.AutoCAD.DatabaseServices.TransactionManager`, which forces `Core.TransactionManager` qualification (`MainCommands.cs:137, 264`; `MainWindow.cs:53, 544`). `CadLibraryReader` reads CSV/GeoJSON, not CAD. `MergeResultsStatic` has a "Static" suffix. `DataBridge.BasicExcelExporter` is the MVP exporter.
- **Mixed languages** in layer names: `"POLE_STEPS"`, `"diagonali"` (transliterated), `"Текст"` (Cyrillic), `"segmented SERV"` (English with a space) (`MainCommands.cs:96, 169-171`; `MainWindow.cs:743`). The sheet name `"Math Test"` (`BasicExcelExporter.cs:32`) sits in an otherwise Bulgarian output.
- Local lambda in PascalCase: `EnsureLayer` (`MainCommands.cs:155`). Column-constant prefixes `P_`/`T_`/`B_` (`ExcelReportGenerator.cs:44-72`) are fine but undocumented.
- Version strings disagree: the window title says `v0.1` (`MainWindow.cs:82`), the bundle says `0.1.0` (`PackageContents.xml:6`), and the commits say `V0.3`.

### E.2 Size (methods > ~80 lines)

| Method | Lines | Span |
|--------|------:|------|
| `MainCommands.PupGenerate` | 228 | `MainCommands.cs:256-483` |
| `ExcelReportGenerator.GenerateReport` | 191 | `ExcelReportGenerator.cs:105-295` |
| `MainWindow.BuildUI` | 180 | `MainWindow.cs:80-259` |
| `BasicExcelExporter.ExportMathTest` | 135 | `BasicExcelExporter.cs:14-148` |
| `GeometrySanitizer.Sanitize` | 134 | `GeometrySanitizer.cs:9-142` |
| `PoleFootprintExtractor.ExtractFootprint` | 128 | `PoleFootprintExtractor.cs:31-158` |
| `MainWindow.BtnGenerate_Click` | 121 | `MainWindow.cs:553-673` |
| `WordReportGenerator.GenerateBalancesTerritory` | 121 | `WordReportGenerator.cs:323-443` |
| `MainCommands.PupDrawFootprints` | 118 | `MainCommands.cs:132-249` |
| `WordReportGenerator.GenerateBalancesMunicipality` | 116 | `WordReportGenerator.cs:607-722` |
| `WordReportGenerator.GeneratePoleStepsRegister` | 107 | `WordReportGenerator.cs:207-313` |
| `TransactionManager.SelectMultiplePolylines` | 102 | `TransactionManager.cs:97-198` |
| `WordReportGenerator.GenerateParcelRegister` | 98 | `WordReportGenerator.cs:99-196` |

Classes: `WordReportGenerator` (868) and `MainWindow` (851). `MainWindow` mixes 5 responsibilities: layout, picking, transaction lifetime, the pipeline, and drawing edits.

### E.3 Duplicated code

| # | Copy 1 | Copy 2 (+) | What |
|---|--------|-----------|------|
| 1 | `MainCommands.cs:298-322` | `MainWindow.cs:432-458` | Footprint extraction loop + `VertexCoordinate` building (identical) |
| 2 | `MainCommands.cs:373-400, 434-441, 464` | `MainWindow.cs:584-600, 615-622, 654` | Topology steps, pole→vertices dict, "NO DATA" count: the **whole pipeline** exists twice |
| 3 | `MainCommands.cs:90-116` | `MainWindow.cs:737-755` | Sanitize → clone → layer → append (segmentation) |
| 4 | `MainCommands.cs:95-104` | `MainCommands.cs:155-167`, `MainWindow.cs:777-789` | "Ensure layer exists" ×3 |
| 5 | `WordReportGenerator.cs:101-195` | `:213-312, 325-442, 454-528, 539-595, 615-721, 734-818` | Template load → find table → take last row → clone → save: **7 copies** |
| 6 | `WordReportGenerator.cs:394-429` | `:674-709` | Balance row + totals computation |
| 7 | `ExcelReportGenerator.cs:357-363` | `WordReportGenerator.cs:352-358` | The 4 balance groupings |
| 8 | `ExcelReportGenerator.cs:205-219` | `WordReportGenerator.cs:251-257` | "Flatten poles × parcels". **Different semantics**: Excel keeps unassigned poles, Word drops them |
| 9 | `CadLibraryReader.cs:141-163` | `:240-259` | GeoJSON feature → parcelId / area |
| 10 | `TransactionManager.cs:296-310` | `PoleFootprintExtractor.cs:47-60` | Pole-number attribute tags |
| 11 | `TopologyProcessor.cs:300-326` | `:328-376` | Clone + origin shift + regions |
| 12 | `MainCommands.cs:310-315`, `MainWindow.cs:446-451` | `TopologyProcessor.cs:452-475` | Vertex extraction (the helper exists with `labelPrefix` but isn't reused) |
| 13 | `MainCommands.cs:580-600` | `MainWindow.cs:318-347` | Project-dir resolution |
| 14 | `BasicExcelExporter.cs:78-87` | `:104-113` | First-row cell list |
| 15 | D12 | | Centroid ×4 |

### E.4 Business logic in UI/command handlers

- `MainCommands.MergeResultsStatic` (`MainCommands.cs:495-570`) is pure domain logic.
- Pipeline orchestration lives in `MainCommands.PupGenerate` and `MainWindow.BtnGenerate_Click`.
- Drawing-edit logic (layers, entity styling, footprint drawing, diagonals, labels) lives in `MainCommands.cs:153-231` and `MainWindow.cs:731-789`.
- Parcel-ID resolution rules live in `Core/TransactionManager.cs:157-188`.

### E.5 Dead / stale code

| Where | What |
|-------|------|
| `DomainModels.cs:58-64` | class `Servitude` is never used |
| `DomainModels.cs:22` | `ParcelData.ObjectId` is never set or read |
| `DomainModels.cs:84` / `TopologyProcessor.cs:170` | `Pole.ObjectId` is written, never read |
| `DomainModels.cs:39` | `ParcelData.MathDifference` is written, never read (D8) |
| `DomainModels.cs:49, 81, 136-145` | `RemainderAreaDka`, `PoleAreaDecares` ×2, `DocumentAreaDecares`, `ServitudeAreaDecares`, `RemainderAreaDecares` are unused |
| `PoleFootprintExtractor.cs:17, 149` | `PoleFootprintResult.AreaSqM` is written, never read |
| `ServitudeMarkerGenerator.cs:111-116` | `IsSameSegment` is unused (IDE0051) |
| `MainWindow.cs:35` | `_chkMarkers` is never created (CS0414 / IDE0051) |
| `TransactionManager.cs:177` | local `areaSqm` is unused ("as requested") |
| `WordReportGenerator.cs:140, 145-148` | `templateRowParent` and `rowNum` are unused |
| `MainCommands.cs:331-338` | GeoJSON branch is **unreachable**: the path is always `…\TemplateC.cad` |
| Unused parameters (IDE0060) | `transaction` in `TopologyProcessor.cs:51, 143, 246`; `logger` in `PoleFootprintExtractor.cs:31`; `ekatte` in `WordReportGenerator.cs:54`; `settlementName`/`municipality`/`oblast` in `WordReportGenerator.cs:611-613` (passed down but never used); `workbook` in `ExcelReportGenerator.cs:502` |
| Unused usings (IDE0005) | Every file has redundant `using System*;` because `ImplicitUsings` is on. Also unused: `Autodesk.AutoCAD.Runtime` (`TransactionManager.cs:7`, `TopologyProcessor.cs:8`), `…EditorInput` (`TopologyProcessor.cs:6`, `DomainModels.cs:6`), `…Runtime` (`DomainModels.cs:8`), `…Geometry` (`MainWindow.cs:11`), `NPOI.SS.Util` (`ExcelReportGenerator.cs:7`), `System.Linq` (`PoleFootprintExtractor.cs:4`), `…ApplicationServices` (`ServitudeMarkerGenerator.cs:4`) |
| Stale comments/docs | "Map3D OD" fallback removed but still referenced: `TransactionManager.cs:94-95, 338-340`, `CadLibraryReader.cs:219-220`, UI text `MainWindow.cs:510`. Think-aloud comment block at `GeometrySanitizer.cs:116-122`. The "(plseg logic)"/"(cleansrv logic)" LISP references are undocumented. |
| TODO/FIXME | none found |
| `App.cs:24-27` | Empty `Terminate()` with a "Cleanup if needed" comment |

### E.6 Error handling / logging / user messages

- Three output channels with no rule for which to use: `ed.WriteMessage` (commands), `AppendLog` (window), `Logger` (file). There are **no `MessageBox`es**.
- **Message language is mixed**: `PUP_GENERATE` and `PUP_DRAW_FOOTPRINTS` print English (`MainCommands.cs:142, 240, 271-481`); `PUP_SERV` and the window print Bulgarian; the file log is English. Selection prompts are English even inside the Bulgarian UI (`MainWindow.cs:398, 433, 514` → `TransactionManager.cs:66`). Error prefixes vary: `[ERROR]` (`MainCommands.cs:47, 247, 480`), `[ГРЕШКА]` (:123), `ГРЕШКА:` (window), "Fatal error in Generate" (`MainWindow.cs:670`).
- **Silent catches**: `catch { }` / `catch { return … }` at `TopologyProcessor.cs:323, 365, 373, 413`; `CadLibraryReader.cs:178, 303`; `MainWindow.cs:346, 845`; `MainCommands.cs:594`; `Logger.cs:44`. There are catch-all-and-log blocks in every generator (C2/C3) and in `ServitudeMarkerGenerator.cs:105-108` (the UI reports success anyway, bug #7).
- `TopologyProcessor` catches only `Autodesk.AutoCAD.Runtime.Exception` (:98, :195, :220), while every other class catches `System.Exception`.

### E.7 "Not on par" parts

- **`GeometrySanitizer.cs`**: a LISP-port style. There's no class doc, the think-aloud comments (:116-122) are still in, the nullable contract is wrong (CS8603), and there are magic numbers (:34).
- **`ServitudeMarkerGenerator.cs`**: no XML docs, hard-coded 20/15/2.0/1.5, a dead method, entities without layer/defaults, and every exception swallowed.
- **`TopologyProcessor.RunMvpMathTest` + helpers (:242-376)**: no docs, no logging, a different parcel-ID source (bug #14), and a different precision strategy (D2).
- **`BasicExcelExporter.cs`**: the only OpenXML spreadsheet writer (the rest use NPOI). It hard-codes its file name, writes numbers as text, and uses an English sheet name.
- **Broken indentation** (the code compiles but misleads): `TopologyProcessor.cs:67-109` (the `foreach` body is not indented under the `using`), and every Word generator's body inside `using (var doc …)` (`WordReportGenerator.cs:121-185, 233-302, 345-432, 474-518, 559-585, 635-711, 754-808`).
- **`MainWindow`**: the long-lived transaction design (B1) is unlike the rest of the code, which uses short `using` transactions.

---

## F. Proposed refactor plan

Rules for every step: **behaviour stays identical**, including the bugs listed below. One commit per step. After each step: `dotnet build` (0 errors, warnings ≤ previous), unit tests (once they exist), and the listed baseline diff.

| # | Step | Files touched | Risk | Verify |
|---|------|---------------|------|--------|
| 0 | Commit the pending MVP header rename on its own, add `baseline/` to `.gitignore`, then capture the baseline (BASELINE.md §3) | `BasicExcelExporter.cs`, `.gitignore` | — | baseline captured |
| 1 | Whitespace only: fix the indentation in `TopologyProcessor.cs:67-109` and the 7 Word methods | 2 files | L | build; `git diff -w` is empty |
| 2 | Remove unused `using`s; add an `.editorconfig` enabling IDE0005/0051/0060 as warnings | all `.cs`, new `.editorconfig` | L | build |
| 3 | Remove dead code from E.5: `Servitude`, the unused `*Decares`/`*Dka` properties, `PoleFootprintResult.AreaSqM`, `IsSameSegment`, `_chkMarkers`, unused locals, stale comments, the unreachable GeoJSON branch (`MainCommands.cs:331-338`), and the unused parameters (all are internal call sites) | DomainModels, PoleFootprintExtractor, ServitudeMarkerGenerator, MainWindow, MainCommands, TransactionManager, TopologyProcessor, WordReportGenerator, ExcelReportGenerator | L | build; baseline 3.1 + 3.3 |
| 4 | **Make `Semantics` AutoCAD-free**: delete `ParcelData.ObjectId` and `Pole.ObjectId` (never read), and replace `Pole.Location: Point3d` with `LocationX/LocationY` doubles (read only at `WordReportGenerator.cs:502-503`; set at `TopologyProcessor.cs:169`) | DomainModels, TopologyProcessor, WordReportGenerator | L-M | build; baseline Word 07 (`PUP_WINDOW_Generate_All`) |
| 5 | **Add the test project** `tests/PUP_AUTO.Tests` (xUnit, net48) with *characterisation* tests that pin the current behaviour, bugs included: `AreaUnits` (midpoint rounding, invariant formatting under `bg-BG` CurrentCulture), `ReportRow`/`ParcelData` computed properties, `CadLibraryReader` (CSV incl. a 1251 file, GeoJSON, `bg-BG` vs invariant parsing), `BasicExcelExporter` golden file (headers, merges, "ОК"/"ГРЕШКА" boundary at 0.001 m²), `ExcelReportGenerator` and `WordReportGenerator` golden files against `_Templates` | new project, `PUP_AUTO.slnx` | L | `dotnet test` |
| 6 | Fix the nullable warnings **by annotation only** (`(JArray?)`, `Polyline?`, `Region?`), keeping explicit casts where they currently throw | CadLibraryReader, GeometrySanitizer, TopologyProcessor, MainCommands, BasicExcelExporter | L | build → 0 warnings; tests |
| 7 | Name the magic numbers from D3 in one `Core/GeometryTolerances.cs` (+ `ReportDefaults`). **Same values**, keeping the 20 vs 50 defaults as two separately named constants | TopologyProcessor, GeometrySanitizer, TransactionManager, ServitudeMarkerGenerator, BasicExcelExporter, MainCommands, MainWindow | L | build; tests; baseline all |
| 8 | Centralise the string keys from C5/C9/C10 (file/folder names, sentinels, attribute tags, visibility names, layer names, balance groupings shared by Excel and Word). **Byte-identical literals** | new `Core/Names.cs` (or similar), 8 files | L | tests; baseline all |
| 9 | Move `MergeResultsStatic` into `Semantics/ReportBuilder` (pure; takes parcel IDs instead of `KeyValuePair<string,Polyline>`) and unit-test it | MainCommands, MainWindow, new file | L | tests; baseline 3.1/3.3 |
| 10 | Extract the shared "blocks → footprints + vertices" helper (E.3 #1, #12) and "poles → vertex dict" (#2) | PoleFootprintExtractor/TopologyProcessor, MainCommands, MainWindow | L-M | baseline 3.1/3.3/3.5 |
| 11 | `WordReportGenerator`: one private `FillTemplate(templateName, Action<Table, TableRow>)` skeleton; each of the 7 reports becomes its table-filling body. **Careful**: 04 uses `tables[t]` (:345), the others use "table with most rows"; that difference must be kept | WordReportGenerator | M | Word golden tests; baseline Word diff |
| 12 | Share the balance-row computation between Word 04/05 (E.3 #6) and the grouping definitions with Excel (#7) | WordReportGenerator, ExcelReportGenerator | M | golden tests; baseline |
| 13 | `CadLibraryReader`: extract the GeoJSON feature parser (E.3 #9) | CadLibraryReader | L | tests |
| 14 | Extract a drawing-writer helper (`EnsureLayer`, "append styled entity") used by `PUP_SERV`, Segment and `PUP_DRAW_FOOTPRINTS` (E.3 #3, #4) | MainCommands, MainWindow, new `Geometry/DrawingWriter.cs` | M | baseline 3.7/3.8/3.9 DXF counts |
| 15 | Move `XDataExtractor` to `Geometry` (or `Core/Cad`) so `Core`/`Geometry` no longer depend on `DataBridge` | 3 files (namespace only) | L | build |
| 16 | Extract a single `GenerationPipeline` (load register → topology → merge → exports) used by both `PUP_GENERATE` and the window, with options reproducing today's differences exactly (command = always all outputs, fixed register, English messages; window = flags, chosen register, Bulgarian messages) | MainCommands, MainWindow, new `Core/GenerationPipeline.cs` | M-H | baseline 3.1–3.5 |
| 17 | Renames (compile-checked, no public command changes): `Core.TransactionManager` → `SelectionService`, `CadLibraryReader` → `ParcelRegisterReader`, unify `SqM`/`Sqm` → `Sqm`, `MergeResultsStatic` → `BuildReportRows` | many | M | build; tests |
| 18 | Split `MainWindow` into layout / view-state / actions. **Do this only after decision D3 below**, because it is the same code the transaction bugs live in | MainWindow (+ new files) | H | baseline 3.3–3.7 |

Why this order: steps 1–6 are mechanical and unlock tests. Steps 7–15 cut duplication with the tests as a safety net. Steps 16–18 are the structural moves and come last.

### F.1 Decisions needed from you

| ID | Decision | Affects |
|----|----------|---------|
| **D1** | Commit the MVP header rename now, so the baseline maps to a commit? | step 0 |
| **D2** | **Bug fixes are behaviour changes.** Fix them in a separate branch *after* the refactor, each with a deliberate baseline update? Or fix some first? My recommendation: first #1 (pole numbers), then #3/#4/#5/#10 (window transactions) and #8 (false success). | all bugs below |
| **D3** | Window transaction model: move to short transactions per click + keep `ObjectId`s instead of DBObjects + `DocumentLock` for writes + dispose in-memory footprints. This **fixes** bugs #3–#6 and B14, so it changes behaviour in those paths. | step 18 |
| **D4** | Message language: all Bulgarian, or keep today's mix? Unify `ToUpper()`→`ToUpperInvariant()` for the attribute tags? This changes command-line and log text, not the exported files. | steps 8, 16 |
| **D5** | Test framework: xUnit (my proposal) or NUnit. The first restore needs internet. | step 5 |
| **D6** | Keep the MVP export on OpenXML (it produces a different file structure than NPOI would)? My recommendation: keep it. | — |
| **D7** | Is `TemplateC.cad` (MKADWIN format) supposed to be a *real* input? If yes, a proper MKAD reader is a feature; if no, the default register should point at a CSV/GeoJSON. | bug #9 |
| **D8** | Which "remainder" definition (D7 above) is correct for which report? And the MVP pole-area rule (D9)? | math |
| **D9** | Public command names (`PUP_WINDOW`, `PUP_SERV`, `PUP_DRAW_FOOTPRINTS`, `PUP_GENERATE`), ribbon texts and output file names stay unchanged. That's my assumption; tell me if not. | — |

### F.2 Suspected bugs (report only, not fixed)

| # | Where | What and why | Sev |
|---|-------|--------------|-----|
| 1 | `DomainModels.cs:75`; `TopologyProcessor.cs:165-171` | **`Pole.PoleNumber` is never assigned** (grep: no writer). Every output that shows it prints **0**: `"Стълб №0"` (`DomainModels.cs:153` → Excel col 9 and Word 02 col 9), Word 03 `"№0"` (`WordReportGenerator.cs:280`), Word 07 `"Стълб №0"` (:493), Excel "Стълбове" col 1 (`ExcelReportGenerator.cs:240`). All `OrderBy(p => p.PoleNumber)` calls (:201; Word :248, :483; DomainModels :153) are no-ops, so row order follows the selection order. The real number is in `Pole.PoleId` (string). | H |
| 2 | `MainWindow.cs:376` vs `:632, :641` | The templates folder selected via `📁 Шаблони` is stored but never used. Generators always read `<dwg dir>\_Templates`. | H |
| 3 | `MainWindow.cs:798, 814-819` | `✂ Сегментиране` empties `_polePolylines` (in-memory footprints have `ObjectId.Null`). The label still shows "✅ N стълба", and the next Generate aborts with "Не са избрани стълбове!". | H |
| 4 | `MainWindow.cs:650-652` | After a successful Generate, the cached picks belong to a disposed transaction. A 2nd Generate, Markers, Segment or MVP run in the same window uses closed DBObjects (exception or crash). | H |
| 5 | `MainWindow.cs:696-701, 845` | Markers are committed only into the outer window transaction. Closing the window without a (non-MVP) Generate aborts it and the markers vanish. | H |
| 6 | `MainWindow.cs:696-764` | Database writes from a modeless window without `LockDocument`. Expected `eLockViolation` (please confirm in baseline). | H |
| 7 | `ServitudeMarkerGenerator.cs:105-108` → `MainWindow.cs:703` | The generator swallows all exceptions, and the window prints "Точките са генерирани в чертежа успешно." even when nothing was drawn. | M |
| 8 | `ExcelReportGenerator.cs:111-116, 291-294`; `WordReportGenerator.cs` each method; callers `MainCommands.cs:427, 459`, `MainWindow.cs:634, 646` | Export failures (missing template, file open in Excel/Word) are reported to the user as success. | H |
| 9 | `CadLibraryReader.cs:38, 61-83` + default path `MainCommands.cs:26-27, 357`, `MainWindow.cs:330, 577` | The default register `_TestFiles/TemplateC.cad` is an **MKADWIN CAD file in Windows-1251**, not a delimited register. Its coordinate lines split on `;` into ≥4 parts, but `parts[3]` is never a number, so ~0 records load and **every parcel gets Owner "NO DATA"**. `PUP_GENERATE` has no way to choose another file. | H |
| 10 | `MainCommands.cs:333-334` | GeoJSON matching in `PUP_GENERATE` is unreachable, because the path is a constant `.cad`. | M |
| 11 | `WordReportGenerator.cs:396, 409, 427, 676-677, 689, 707` | "Обща засегната площ" = servitude + poles. But `ServitudeAreaSqM` is the **gross** intersection, which already contains the pole footprints (as stated in `DomainModels.cs:129-131`), so poles are counted twice; the "%" column uses the same inflated denominator. **Please confirm the business rule.** | H? |
| 12 | `WordReportGenerator.cs:410, 428, 502-503, 512-513, 579-580, 690, 708`; `CadLibraryReader.cs:79, 160, 256`; `MainWindow.cs:724` | Culture-dependent formatting and parsing. On a bg-BG Windows, the Word docs mix `,` and `.`, and CSV areas like `1234.5` fail to parse, so that row is dropped and the parcel shows "NO DATA". `2.5` typed in `Разстояние` silently falls back to 50. | H |
| 13 | `TopologyProcessor.cs:94, 192, 277` | Duplicate parcel/pole IDs overwrite areas instead of summing them (D11). | M |
| 14 | `TopologyProcessor.cs:253` | The MVP test re-reads the parcel ID from XData and ignores the ID resolved at pick time (`parcelKvp.Key`, which may be GeoJSON-matched). MVP rows can show `Неизвестен_Имот` where the main report shows the real ID. | M |
| 15 | `TransactionManager.cs:78` | `SelectSinglePolyline` accepts **open** polylines with > 2 vertices, contrary to its doc (:57-60). `SafeCreateRegion` then force-closes the clone (`TopologyProcessor.cs:397`). | L |
| 16 | `MainCommands.cs:147, 173-231` | `PUP_DRAW_FOOTPRINTS` appends to Model Space while enumerating it, and duplicates its output on every run (B6). | M |
| 17 | `MainCommands.cs:134-138, 259-267` | Command bootstrap is outside `try`, so with no active document or an unwritable folder there is an unhandled exception inside AutoCAD (B11). | M |
| 18 | `MainCommands.cs:599`; `MainWindow.cs:43, 540, 568, 631` | On an unsaved drawing, outputs and log go to AutoCAD's current working directory (B16). | M |
| 19 | `TransactionManager.cs:313-327` | The pole-ID fallback takes the first ASCII string from **any** application's XData, not a specific RegApp. | L |
| 20 | `MainWindow.cs:612-625` vs `:638` | ☑ Coordinates without ☑ Word computes the coordinates and then discards them, with no output and no message. | L |
| 21 | `CadLibraryReader.cs:290-300` | The GeoJSON centroid includes the closing vertex (biased; D12). Low impact because of the 50 m tolerance. | L |
| 22 | `MainWindow.cs:52` + `MainCommands.cs:40-41` | Several `PUP_WINDOW` instances can each hold an open transaction on the same database (B14). | M |

---

*End of audit. No source files were modified. The only files created are `BASELINE.md` and `AUDIT.md`. Waiting for your review.*
