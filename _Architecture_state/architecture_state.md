# PUP_AUTO - Architecture State
*Този файл проследява текущото състояние, структурата и архитектурните решения в проекта.*

## 1. Текущ статус
- [x] Инициализация на проекта PUP_AUTO (.NET Framework 4.8, AutoCAD API 24.3.0) — Завършено
- [x] `PUP_GENERATE` команда — пълен workflow имплементиран (Select → Load → Topo → Merge → Excel → Coords → Word → Summary)
- [x] `PUP_WINDOW` команда — WPF модален прозорец с GUI за интерактивна работа (файлове, геометрии, опции)
- [x] `App.cs` — Ribbon таб "ПУП АВТОМАТИЗАЦИЯ" с два бутона (Генерирай / Отвори Прозорец)
- [x] `WordReportGenerator.cs` — Генерация на 7 Word (.docm) регистъра чрез OpenXML SDK
- [x] `ExtractPolylineVertices()` — Координатен регистър за стълбове и сервитут
- [x] Генератори за Templates 05 и 06 — имплементирани (`GenerateBalancesMunicipality`, `GenerateRecapitulation`)
- [x] PUP_GENERATE CLI — добавен Word и координатен експорт (пълен паритет с GUI)
- [x] TECHNICAL_DOCUMENTATION.md — пълно преписване с всички компоненти

## 2. Файлова Структура

- `Core/`
  - `TransactionManager.cs` — Транзакции, селекция на полилинии, XData/Handle идентификация
  - `Logger.cs` — Fault-tolerant логване (SUCCESS / WARNING / ERROR)
- `Semantics/`
  - `DomainModels.cs` — `ParcelData`, `Servitude`, `Pole`, `ReportRow`, `VertexCoordinate`
- `DataBridge/`
  - `CadLibraryReader.cs` — Парсване на `.cad` текстова база с `SafeCol()` защита
  - `ExcelReportGenerator.cs` — NPOI генерация на `.xls` отчет (3 листа)
  - `WordReportGenerator.cs` — OpenXML генерация на 7 `.docm` регистъра
- `Geometry/`
  - `TopologyProcessor.cs` — Boolean сечения, Dominant Area, координатно извличане
- `UI/`
  - `App.cs` — `IExtensionApplication` входна точка, Ribbon Tab създаване
  - `MainCommands.cs` — `PUP_GENERATE`, `PUP_WINDOW` команди, `MergeResultsStatic()`
  - `Windows/`
    - `MainWindow.cs` — WPF модален прозорец (Catppuccin Mocha тема)
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

## 3. Архитектурни Решения
- **Dependency Passing**: `Logger` и `TransactionManager` се предават като параметри, не като Singletons.
- **Dual Interface**: Системата има и CLI команда (`PUP_GENERATE`) и GUI прозорец (`PUP_WINDOW`), и двете споделящи `MergeResultsStatic()` (public static).
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