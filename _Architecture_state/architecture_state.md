# PUP_AUTO - Architecture State
*Този файл проследява текущото състояние, структурата и архитектурните решения в проекта.*

## 1. Текущ статус
- [x] Инициализация на проекта PUP_AUTO (.NET Framework 4.8, AutoCAD API 24.3.0) — Завършено
- [x] ~~`PUP_GENERATE` команда~~ — **премахната** (2026-09-29, refactor/cleanup); генерирането е само през `PUP_WINDOW`
- [x] `PUP_WINDOW` команда — WPF немодален прозорец с GUI за интерактивна работа (файлове, геометрии, опции)
- [x] `App.cs` — Ribbon таб "ПУП АВТОМАТИЗАЦИЯ" с бутон "Отвори Прозорец" (бутонът "Генерирай Отчети" е премахнат заедно с `PUP_GENERATE`)
- [x] `WordReportGenerator.cs` — Генерация на 7 Word (.docm) регистъра чрез OpenXML SDK
- [x] `ExtractPolylineVertices()` — Координатен регистър за стълбове и сервитут
- [x] Генератори за Templates 05 и 06 — имплементирани (`GenerateBalancesMunicipality`, `GenerateRecapitulation`)
- [x] TECHNICAL_DOCUMENTATION.md — пълно преписване с всички компоненти
- [x] `GeometrySanitizer.cs` — Densify/clean на полилинии (използва се от `PUP_SERV`, "✂ Сегментиране" и `CalculateServitudeIntersections`)
- [x] `PoleFootprintExtractor.cs` — P-tag извличане на 4-точков контур от динамичен блок, вкл. поддръжка на visibility state
- [x] `ServitudeMarkerGenerator.cs` — 20м номерирани маркери по двете страни на сервитута ("📍 Само Точки")
- [x] `PUP_SERV` / `PUP_DRAW_FOOTPRINTS` команди — диагностични/чертожни, извън основния отчетен pipeline
- [x] `RunMvpMathTest()` + `BasicExcelExporter.ExportMathTest()` — диагностичен path за проверка на площния баланс (Gross/Net/Pole), извикван от "🧪 MVP Математически тест"
- [x] Документация актуализирана (2026-09-28) с всички гореизброени компоненти + коригирано описание на причисляването на стълбове (виж т.4)

## 2. Файлова Структура

- `Core/`
  - `TransactionManager.cs` — Транзакции, селекция на полилинии, XData/Handle/GeoJSON идентификация
  - `Logger.cs` — Fault-tolerant логване (SUCCESS / WARNING / ERROR)
- `Semantics/`
  - `DomainModels.cs` — `ParcelData`, `Pole`, `ReportRow`, `VertexCoordinate`, `GeoParcel`
- `DataBridge/`
  - `CadLibraryReader.cs` — Парсване на `.cad`/CSV/GeoJSON текстова база с `SafeCol()` защита
  - `ExcelReportGenerator.cs` — NPOI генерация на `.xls` отчет (3 листа)
  - `WordReportGenerator.cs` — OpenXML генерация на 7 `.docm` регистъра
  - `BasicExcelExporter.cs` — **(ново документирано)** Самостоятелен OpenXML `.xlsx` експорт за "MVP Математически тест" (диагностика на площния баланс)
- `Geometry/`
  - `TopologyProcessor.cs` — Boolean сечения, причисляване на стълбове по всички засечени имоти (виж бел. в т.4), координатно извличане, `RunMvpMathTest()`
  - `GeometrySanitizer.cs` — **(ново документирано)** Densify + clean на полилинии преди булеви операции
  - `PoleFootprintExtractor.cs` — **(ново документирано)** Извличане на 4-точков P-tag контур на стъпка на стълб от динамичен блок
  - `ServitudeMarkerGenerator.cs` — **(ново документирано)** Генерира номерирани 20м точки по двете страни на сервитута (чертожна помощна функция)
- `UI/`
  - `App.cs` — `IExtensionApplication` входна точка, Ribbon Tab създаване
  - `MainCommands.cs` — `PUP_WINDOW`, `PUP_SERV`, `PUP_DRAW_FOOTPRINTS` команди, `MergeResultsStatic()`
  - `Windows/`
    - `MainWindow.cs` — WPF модален прозорец (Catppuccin Mocha тема), вкл. MVP Math Test checkbox и бутони за маркери/сегментиране **(ново документирано)**
- `_TestFiles/`
  - `TemplateC.cad` — Кадастрален текстов регистър (входни данни)
- `_Templates/`
  - `TemplateX.xls` — Excel шаблон за PUP_Report.xls
  - `D306-31Y0-02-0A - Регистър на засегнатите имоти.docm`
  - `D306-31Y0-03-0A - Регистър на стъпките на стълбове.docm`
  - `D306-31Y0-04-0A - Баланси територията.docm`
  - `D306-31Y0-05-0A - Общ Баланс за общината.docm`
  - `D306-31Y0-06-0A - Обща рекапитулация.docm`
  - `D306-31Y0-07-0A - Координатен регистър на стъпките на стълбовете.docm`
  - `D306-31Y0-08-0A - Координатен регистър на сервитута.docm`
- `_Bundle/`
  - `PackageContents.xml` — AutoCAD .bundle манифест за auto-load

## 3. Архитектурни Решения
- **Dependency Passing**: `Logger` и `TransactionManager` се предават като параметри, не като Singletons.
- **Single Interface**: Отчетите се генерират само от GUI прозореца (`PUP_WINDOW`), който извиква `MergeResultsStatic()` (public static в `MainCommands`). CLI командата `PUP_GENERATE` е премахната (2026-09-29).
- **Dual Output**: Генерира едновременно Excel (.xls чрез NPOI) и Word (.docm чрез OpenXML SDK).
- **Memory Safety**: Всички AutoCAD `Region` обекти се освобождават в `finally` блокове.
- **Fault Tolerance**: Logger, CadLibraryReader и TopologyProcessor обгръщат критичните I/O операции в `try/catch`.

## 4. Критични бележки от Одита
- ✅ Одит завършен (2026-09-14). Всички 6 липсващи компонента бяха добавени към документацията.
- ✅ `MergeResultsStatic()` е коректно обозначен като `public static` (използва се от `MainWindow.cs`).
- ✅ Мermaid диаграмата е обновена с `App.cs`, `MainWindow.cs`, `WordReportGenerator.cs` и Word изход.
- ✅ Templates 05 (`GenerateBalancesMunicipality`) и 06 (`GenerateRecapitulation`) — **имплементирани** (2026-09-14).
- ✅ `PUP_GENERATE` CLI — добавен Word и координатен експорт, пълен паритет с GUI (2026-09-14).
- ✅ `TECHNICAL_DOCUMENTATION.md` — пълно преписване с всички компоненти (2026-09-14).

### Одит 2026-09-28
- ✅ Документирани 5 напълно липсващи от всички .md файлове компонента: `GeometrySanitizer.cs`, `PoleFootprintExtractor.cs`, `ServitudeMarkerGenerator.cs`, `BasicExcelExporter.cs`, командите `PUP_SERV`/`PUP_DRAW_FOOTPRINTS`, `TopologyProcessor.RunMvpMathTest()` и MVP Math Test UI елементите.
- ⚠️ **Коригирано разминаване документация↔код:** `TopologyProcessor.AssignPolesToParcels()` НЕ избира доминиращия по площ имот (`arg max`), а записва стълба във **всички** имоти, с които има сечение над `SliverTolerance`. README.md и Architecture_Plan.md все още описват старото ("доминираща площ") поведение — не са коригирани в тази сесия, само `TECHNICAL_DOCUMENTATION.md`. Ако желаното поведение е "1 стълб → 1 имот", нужна е промяна в кода (`OrderByDescending(area).First()`); ако текущото multi-parcel поведение е желано, README.md/Architecture_Plan.md трябва да се актуализират в следваща сесия.
- ⚠️ **Открит потенциален бъг, все още НЕ поправен в кода:** `CadLibraryReader.LoadLibrary()` (ред ~79) и `LoadGeoJsonLibrary()`/`LoadGeoJsonGeometries()` (площ от GeoJSON) използват `double.TryParse(...)` **без** `CultureInfo.InvariantCulture` — за разлика от `PoleFootprintExtractor.cs`, който изрично го прави. При регионални настройки с десетична запетая (bg-BG) площите с точка като десетичен разделител могат да се провалят тихо (лог Warning + пропуснат ред). Препоръка: добавяне на `NumberStyles.Any, CultureInfo.InvariantCulture` на трите места.
- ⚠️ **Наблюдение:** `TransactionManager.SelectSinglePolyline()` (избор на единичния сервитут) има по-слаба проверка за затвореност от `SelectMultiplePolylines()` (без tolerance-базирана проверка start↔end за близо затворени контури). Няма промяна в кода в тази сесия.
- 🧹 Изтрити build артефакти `bin/` и `obj/` от `D:\repo\PUP_AUTO` (регенерират се автоматично при `dotnet build`; и двете вече са в `.gitignore`).