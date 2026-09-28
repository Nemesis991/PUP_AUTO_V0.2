# 📘 ТЕХНИЧЕСКА ДОКУМЕНТАЦИЯ И ПАСПОРТ НА СОФТУЕРА PUP_AUTO

---

## 1. 📌 ОБЩА АРХИТЕКТУРА И КОНЦЕПЦИЯ

Софтуерният пакет **PUP_AUTO** е специализиран .NET модул (плагин) за **Autodesk Civil 3D** и **AutoCAD**, разработен на C# (.NET Framework 4.8). Неговата основна цел е автоматизация на пространствения анализ, топологичното пресичане и генерацията на ведомости за засегнати имоти при изготвяне на Подробни Устройствени Планове (ПУП) за линейни обекти (електропроводи, водопроводи, газопроводи и пътна инфраструктура).

Системата генерира два типа изходни документи: **Excel (.xls)** отчет с 3 работни листа и **7 Word (.docm)** регистъра, включително координатни регистри.

### Разделение на отговорностите (Separation of Concerns):
Архитектурата е организирана в 5 строго изолирани слоя (Layers), гарантиращи висока модулност, лесна поддръжка и възможност за самостоятелно модулно тестване:

1. **`Core` (Определящ и системен слой):**
   - Управлява взаимодействията с базата данни на AutoCAD DWG чертежа, транзакциите (`TransactionManager`) и логването на системни събития и грешки (`Logger`).
   - Гарантира термична памет и безупречно управление на CAD ресурсите.

2. **`Semantics` (Домейнов слой):**
   - Съдържа чисто домейнови бизнес модели (`ParcelData`, `Servitude`, `Pole`, `ReportRow`, `VertexCoordinate`).
   - Независим от AutoCAD API интерфейсите, капсулира математиката за преобразуване на мерни единици (от квадратни метри $m^2$ в декари $\text{дка}$).

3. **`DataBridge` (Интеграционен слой за данни):**
   - **Входен мост (`CadLibraryReader`):** Парсва външни текстови бази данни (`TemplateC.cad`), съдържащи кадастрални регистрови данни.
   - **Изходен мост — Excel (`ExcelReportGenerator`):** Обвива NPOI библиотеката за работа с Excel шаблони (`TemplateX.xls`), извършва динамично вмъкване на редове и агрегиране на данни.
   - **Изходен мост — Word (`WordReportGenerator`):** Използва OpenXML SDK (`DocumentFormat.OpenXml`) за генерация на 7 Word (.docm) регистъра от шаблони, без да изисква инсталиран Microsoft Office.

4. **`Geometry` (Топологичен и пространствен слой):**
   - `TopologyProcessor` съдържа векторните алгоритми за пресичане на площи, създаване на 2D `Region` обекти, прилагане на булеви операции, причисляване на стълбове към имоти и извличане на координати от полилинии. Съдържа и отделен "MVP Math Test" път (`RunMvpMathTest`) за диагностична проверка на площния баланс.
   - `GeometrySanitizer` премахва микросегменти и сегментира дълги отсечки на полилинии (densify + clean) преди булевите операции, за да предпази `Region.CreateFromCurves` от грешки при самозасичащи се/твърде дълги сегменти.
   - `PoleFootprintExtractor` извлича 4-точковия ("P-tag") контур на стъпката на стълб от динамичен блок (`BlockReference`), спазвайки строга йерархия на атрибутите и игнорирайки "TP" таговете.
   - `ServitudeMarkerGenerator` генерира точки и номерирани текстови етикети на всеки 20 м по двете страни на сервитутния коридор (диагностична/чертожна функция, не участва в отчетния pipeline).

5. **`UI` (Потребителски и команден слой):**
   - `App` е входната точка на плагина (`IExtensionApplication`), създаваща Ribbon таб "ПУП АВТОМАТИЗАЦИЯ" с два бутона.
   - `MainCommands` регистрира двете AutoCAD команди: `PUP_GENERATE` (CLI) и `PUP_WINDOW` (GUI).
   - `MainWindow` е WPF модален прозорец с тъмна Catppuccin Mocha тема, предоставящ визуален интерфейс за избор на геометрии, файлове и опции за генериране.

### Интеграция с Autodesk Civil 3D / Map 3D API:
Софтуерът стъпва директно върху управляваните сглобки (Managed Assemblies) на Autodesk:
- `Autodesk.AutoCAD.ApplicationServices`
- `Autodesk.AutoCAD.DatabaseServices`
- `Autodesk.AutoCAD.EditorInput`
- `Autodesk.AutoCAD.Geometry`

Това гарантира съвместимост както със стандартен AutoCAD, така и с надградените платформи Civil 3D и Map 3D, като използва родната транзакционна система на AutoCAD Database за четене на затворени полилинии (`LWPOLYLINE` / `Polyline`) и техните DXF разширени данни (`XData`).

### Преглед на потока на данните (Data Flow Overview):
1. **Вход 1 (Чертеж):** Потребителят избира сервитутна полилиния, полилинии на стълбове и кадастрални имоти от DWG чертежа.
2. **Вход 2 (Външен файл):** Чете се текстов файл `TemplateC.cad` с кадастралните собственици, категории, начини на трайно ползване (НТП) и видове собственост.
3. **Обработка:** `TopologyProcessor` пресича геометричните контури и пресмята площи. `MergeResultsStatic` свързва графичните Handle/XData идентификатори с текстовите регистри. `ExtractPolylineVertices` извлича координати.
4. **Изход:** Генерира се Excel доклад (`PUP_Report.xls`) с 3 работни листа, 7 Word регистъра (.docm) и текстов лог файл (`PUP_AUTO_Logs.txt`).

---

## 2. 🔄 ПЪЛЕН ЖИЗНЕН ЦИКЪЛ (Execution Trace)

### 2.1. Зареждане на плагина (Plugin Load)
При `NETLOAD` на `PUP_AUTO.dll`, AutoCAD изпълнява:
1. `App.Initialize()` → абонира се за `Application.Idle`.
2. `OnAppIdle()` → еднократно създава Ribbon таб "ПУП АВТОМАТИЗАЦИЯ" с два бутона:
   - "Генерирай Отчети" → `PUP_GENERATE`
   - "Отвори Прозорец" → `PUP_WINDOW`

### 2.2. Процес 'PUP_GENERATE' (Команден Ред)

При въвеждане на командата `PUP_GENERATE` в командния ред на Civil 3D, системата преминава през следната строга хронологична последователност:

```mermaid
sequenceDiagram
    autonumber
    actor User as Графичен Потребител (Civil 3D)
    participant APP as UI.App (Ribbon)
    participant MC as UI.MainCommands
    participant TM as Core.TransactionManager
    participant CR as DataBridge.CadLibraryReader
    participant TP as Geometry.TopologyProcessor
    participant EG as DataBridge.ExcelReportGenerator
    participant WG as DataBridge.WordReportGenerator
    participant LG as Core.Logger

    User->>APP: NETLOAD → Initialize() → CreateRibbon()
    User->>MC: Задейства команда 'PUP_GENERATE'
    MC->>MC: ResolveProjectDirectory() → Намира работната папка
    MC->>LG: Инициализира Logger("PUP_AUTO_Logs.txt")
    MC->>TM: Инициализира TransactionManager(logger)
    MC->>TP: Инициализира TopologyProcessor(logger)

    rect rgb(235, 245, 255)
        note over MC,TM: 1. Графична селекция от DWG чертежа
        MC->>TM: StartTransaction()
        TM-->>MC: Transaction (tr)
        MC->>TM: SelectSinglePolyline(tr, "Servitude Prompt")
        TM-->>MC: Polyline (servitudePline)
        MC->>TM: SelectMultiplePolylines(tr, "Poles Prompt")
        TM-->>MC: List<KeyValuePair<string, Polyline>> (polePolylines)
        MC->>TM: SelectMultiplePolylines(tr, "Parcels Prompt")
        TM-->>MC: List<KeyValuePair<string, Polyline>> (parcelPolylines)
    end

    rect rgb(255, 248, 230)
        note over MC,CR: 2. Зареждане на външната база данни
        MC->>CR: CadLibraryReader(logger)
        MC->>CR: LoadLibrary("TemplateC.cad")
        CR-->>MC: Dictionary<string, ParcelData> (parcelDb)
    end

    rect rgb(235, 255, 235)
        note over MC,TP: 3. Топологични и векторни изчисления
        MC->>TP: CalculateServitudeIntersections(servitudePline, parcelPolylines, tr)
        TP-->>MC: Dictionary<string, double> (servitudeAreas)
        MC->>TP: AssignPolesToParcels(polePolylines, parcelPolylines, tr)
        TP-->>MC: List<Pole> (assignedPoles)
    end

    rect rgb(255, 235, 245)
        note over MC,EG: 4. Обединяване на данни и генерация
        MC->>MC: MergeResultsStatic(parcelPolylines, parcelDb, servitudeAreas, assignedPoles, logger)
        MC-->>MC: List<ReportRow> (reportRows)
        MC->>EG: ExcelReportGenerator(logger, projectDir)
        MC->>EG: GenerateReport(reportRows, assignedPoles, parcelDb, "PUP_Report.xls")
    end

    rect rgb(245, 235, 255)
        note over MC,WG: 5. Координати и Word регистри
        MC->>TP: ExtractPolylineVertices() × N стълба + сервитут
        TP-->>MC: Dictionary<string, List<VertexCoordinate>> + List<VertexCoordinate>
        MC->>WG: WordReportGenerator(logger, projectDir)
        MC->>WG: GenerateAllReports(reportRows, poles, parcelDb, dir, poleVertices, servitudeVertices)
        WG->>WG: 7 × Word .docm регистъра
    end

    MC->>TM: tr.Commit()
    MC->>LG: LogSuccess("PUP_GENERATE COMPLETE")
    MC->>User: Извежда обобщение в командния ред
```

### 2.3. Процес 'PUP_WINDOW' (Графичен Прозорец)

Извършва същите стъпки, но чрез WPF модален прозорец (`MainWindow`):
1. Потребителят избира геометрии чрез бутони в GUI (прозорецът се скрива по време на селекция в AutoCAD).
2. Потребителят избира кои отчети да генерира чрез checkboxes (Excel / Word / Координатни регистри).
3. Целият прогрес се показва в реално време в лог конзолата на прозореца.
4. Поддържа персистираща транзакция (`_activeTransaction`) между отделните pick операции.

---

## 3. ⚙️ ПОДРОБЕН РАЗБОР НА ВСЯКА ФУНКЦИЯ ПО КЛАСОВЕ

---

### 📁 UI Layer

#### 📄 `UI/App.cs`
Входна точка на плагина, имплементираща `IExtensionApplication`.

1. **`public void Initialize()`**
   - **Логика:** Абонира се за `Application.Idle += OnAppIdle`, за да отложи създаването на Ribbon таба до пълното зареждане на AutoCAD.
2. **`private void OnAppIdle(object sender, EventArgs e)`**
   - **Логика:** Извиква се еднократно, незабавно се отписва от `Application.Idle`, след което извиква `CreateRibbon()`.
3. **`private void CreateRibbon()`**
   - **Логика:** Създава нов Ribbon Tab "ПУП АВТОМАТИЗАЦИЯ" с панел, съдържащ два `RibbonButton`:
     - "Генерирай Отчети" → `PUP_GENERATE`
     - "Отвори Прозорец" → `PUP_WINDOW`
4. **`class RibbonCommandHandler : ICommand`**
   - **Логика:** Маршрутизира кликвания от Ribbon бутоните към AutoCAD чрез `doc.SendStringToExecute(commandName, ...)`.

---

#### 📄 `UI/MainCommands.cs`
Регистрира AutoCAD командите и оркестрира целия pipeline.

1. **`[CommandMethod("PUP_GENERATE")] public void PupGenerate()`**
   - **Сигнатура:** `() -> void`
   - **Логика:** Изпълнява последователно стъпки 1 до 6 от жизнения цикъл (селекция → зареждане → топология → обединяване → Excel → координати → Word → обобщение). Обхванат от глобален `try-catch` за безопасно логване на непокрити грешки.
2. **`[CommandMethod("PUP_WINDOW")] public void PupWindow()`**
   - **Сигнатура:** `() -> void`
   - **Логика:** Създава инстанция на `MainWindow` и я показва чрез `Application.ShowModelessWindow(window)` (немодален прозорец — потребителят може да превключва между него и AutoCAD, без да го затваря).
3. **`[CommandMethod("PUP_SERV")] public void PupServ()`**
   - **Сигнатура:** `() -> void`
   - **Логика:** Диагностична/чертожна команда извън основния отчетен pipeline. Подканва потребителя за полилиния (сервитут) и разстояние за сегментиране (по подразбиране 20.0 м), извиква `GeometrySanitizer.Sanitize()` и добавя резултата като нова полилиния в чертежа на автоматично създаван слой `"segmented SERV"` (цвят ACI 3, `ConstantWidth = 0.5`).
4. **`[CommandMethod("PUP_DRAW_FOOTPRINTS")] public void PupDrawFootprints()`**
   - **Сигнатура:** `() -> void`
   - **Логика:** Диагностична команда, която обхожда **всички** блокови референции в текущото Model Space, извиква `PoleFootprintExtractor.ExtractFootprint()` за всяка от тях и чертае извлечените 4-точкови контури на слой `"POLE_STEPS"`, диагоналите между върховете на слой `"diagonali"` и номера на стълба като `DBText` на слой `"Текст"`. Автоматично създава трите слоя, ако липсват. Извежда обобщение (обработени / успешни / неуспешни) в командния ред.
5. **`public static List<ReportRow> MergeResultsStatic(...)`**
   - **Сигнатура:** `(List<KeyValuePair<string, Polyline>> parcelPolylines, Dictionary<string, ParcelData> parcelDb, Dictionary<string, double> servitudeAreas, List<Pole> assignedPoles, Logger logger) -> List<ReportRow>`
   - **Логика:** Обединява данните от геометрията и текста. За имоти от чертежа, които липсват в `.cad` файла, задава `Owner = "NO DATA"` и записва Warning в лога. **Публичен статичен метод**, споделен между CLI и GUI входните точки.
6. **`private static string ResolveProjectDirectory()`**
   - **Сигнатура:** `() -> string`
   - **Логика:** Извлича папката на текущия чертеж от `Path.GetDirectoryName(doc.Name)` (абсолютен път) или използва `Environment.CurrentDirectory` като fallback.

---

#### 📄 `UI/Windows/MainWindow.cs`
WPF модален прозорец, изграден изцяло в C# код (без XAML), с тъмна тема **Catppuccin Mocha**.

1. **`public MainWindow()`**
   - **Логика:** Извиква `BuildUI()` за конструиране на WPF контролите и `ResolveDefaults()` за автоматично разпознаване на `.cad` файл и `_Templates` папка.
2. **`private void BuildUI()`**
   - **Логика:** Конструира 6-редов Grid layout:
     - Ред 0: Заглавие "⚡ ПУП АВТОМАТИЗАЦИЯ"
     - Ред 1: Полета за `.cad` база и шаблони с Browse бутони
     - Ред 2: 3 бутона за избор на геометрии (Сервитут / Стълбове / Имоти)
     - Ред 3: Checkboxes (Excel / Word / Координатни регистри / 🧪 MVP Математически тест) + панел с полета "Старт Ляво"/"Старт Дясно" за номерация, бутон "📍 Само Точки (20м)", поле "Разстояние" и бутон "✂ Сегментиране"
     - Ред 4: Лог конзола (`Consolas` шрифт)
     - Ред 5: Бутон "🚀 ГЕНЕРИРАЙ ОТЧЕТИ"
3. **`private void BtnPickServitude_Click / BtnPickPoles_Click / BtnPickParcels_Click(object sender, RoutedEventArgs e)`**
   - **Логика:** Всяка от трите обвива селекцията в `ed.StartUserInteraction(this)`, извиква `EnsureTransaction()` и съответния метод на `TransactionManager`/`PoleFootprintExtractor`. Обновява статус етикета (✅/❌) и лог конзолата. Изборът на имоти зарежда GeoJSON геометрии за пространствено съвпадение, ако е посочен `.geojson` файл.
4. **`private void EnsureTransaction()`**
   - **Логика:** Lazy-инициализира `Logger`, `TransactionManager` и `Transaction` ако не съществуват или са disposed. Поддържа персистираща транзакция между pick операциите.
5. **`private void BtnGenerate_Click(object sender, RoutedEventArgs e)`**
   - **Логика:** Валидира, че всички геометрии са избрани. Ако е отметнат `🧪 MVP Математически тест`, изпълнява `TopologyProcessor.RunMvpMathTest()` и `BasicExcelExporter.ExportMathTest()`, записва `MVP_Math_Test_Parcels.xlsx` и прекратява (не генерира стандартните отчети). Иначе изпълнява 7-стъпков pipeline (Load CAD → Topology → Merge → Coordinates → Excel → Word → Commit). Извежда прогрес в лог конзолата.
6. **`private void BtnGenMarkers_Click(object sender, RoutedEventArgs e)`**
   - **Логика:** Извиква `ServitudeMarkerGenerator.GenerateMarkers()` върху избрания сервитут с началните номера от полетата "Старт Ляво"/"Старт Дясно" (по подразбиране 5001/1).
7. **`private void BtnSegment_Click(object sender, RoutedEventArgs e)`**
   - **Логика:** Извиква `GeometrySanitizer.Sanitize()` върху избрания сервитут с разстоянието от полето "Разстояние" (по подразбиране 50 м), добавя резултата на слой `"segmented SERV"` и незабавно комитва и презарежда активната транзакция чрез `CommitAndRefreshTransaction()`, за да се вижда веднага в чертежа.
8. **`protected override void OnClosed(EventArgs e)`**
   - **Логика:** Abort-ва и Dispose-ва активната транзакция при затваряне на прозореца.

---

### 📁 Core Layer

#### 📄 `Core/TransactionManager.cs`
Управлява жизнения цикъл на транзакциите в DWG базата данни и събирането на обекти от чертежа.

1. **`public TransactionManager(Logger logger)`**
   - **Сигнатура:** `(Logger logger) -> void`
   - **Логика:** Приема и инжектира логер съобщението в локално поле `_logger`.
2. **`public Document GetActiveDocument()`**
   - **Сигнатура:** `() -> Document`
   - **Логика:** Връща референция към текущия активен документ `Application.DocumentManager.MdiActiveDocument`.
3. **`public Editor GetEditor()`**
   - **Сигнатура:** `() -> Editor`
   - **Логика:** Връща редактора за интеракция с потребителя `GetActiveDocument().Editor`.
4. **`public Database GetDatabase()`**
   - **Сигнатура:** `() -> Database`
   - **Логика:** Връща структурата от данни на чертежа `GetActiveDocument().Database`.
5. **`public Transaction StartTransaction()`**
   - **Сигнатура:** `() -> Transaction`
   - **Логика:** Стартира нова транзакция чрез `GetDatabase().TransactionManager.StartTransaction()`.
6. **`public Polyline? SelectSinglePolyline(Transaction transaction, string promptMessage)`**
   - **Сигнатура:** `(Transaction transaction, string promptMessage) -> Polyline?`
   - **Логика:** Задава `PromptEntityOptions` с филтър за тип `Polyline`. Подканва потребителя за единичен избор. Проверява дали обектът е валидна и затворена полилиния (`pline.Closed`). Ако е отворена или селекцията е отменена, логва Warning и връща `null`.
7. **`public List<KeyValuePair<string, Polyline>> SelectMultiplePolylines(Transaction transaction, string promptMessage)`**
   - **Сигнатура:** `(Transaction transaction, string promptMessage) -> List<KeyValuePair<string, Polyline>>`
   - **Логика:** Прилага `SelectionFilter` с DXF код `LWPOLYLINE`. Прихваща множество обекти. Обхожда селекцията, пропуска незатворените полилинии. Проверява `XData` за ASCII низ с идентификатор (ParcelId / PoleId). Ако липсва, като идентификатор ползва геометричния `Handle` на обекта.

---

#### 📄 `Core/Logger.cs`
Предоставя fault-tolerant (защитена от сривове) лог система.

1. **`public Logger(string logFilePath = "PUP_AUTO_Logs.txt")`**
   - **Сигнатура:** `(string logFilePath) -> void`
   - **Логика:** Запазва пътя. Извлича папката с `Path.GetDirectoryName`. Ако папка съществува и липсва на диска, извиква `Directory.CreateDirectory`.
2. **`public void LogSuccess(string message)`**
   - **Логика:** Извиква `WriteLog("SUCCESS", message)`.
3. **`public void LogError(string message)`**
   - **Логика:** Извиква `WriteLog("ERROR", message)`.
4. **`public void LogWarning(string message)`**
   - **Логика:** Извиква `WriteLog("WARNING", message)`.
5. **`private void WriteLog(string level, string message)`**
   - **Логика:** Форматира времеви маркер: `yyyy-MM-dd HH:mm:ss [LEVEL] Message\r\n`. Използва `File.AppendAllText`. Прихваща всички изключения с празен `catch` блок, за да гарантира, че проблем със записването на диска няма да спре изпълнението на AutoCAD.

---

### 📁 Semantics Layer

#### 📄 `Core/AreaUnits.cs`
Единствената точка на преобразуване квадратни метри → декари в целия проект.

- `SqmToDka(double sqm) -> double` — `Math.Round(sqm / 1000.0, 3, MidpointRounding.AwayFromZero)`.
- `FormatDka(double sqm) -> string` — `SqmToDka(sqm).ToString("F3", OutputCulture)` (`InvariantCulture` по подразбиране, за да не зависи изходният `.` от locale на машината).
- **Правило (валидно за целия проект):** всяко изчисление — сборуване, изваждане, сумиране в LINQ групиране, проверка на толеранс — работи с необработени стойности в $m^2$ (`double`). Преобразуването в декари и закръгляването до 3 знака става **само веднъж**, в момента на записване в Excel/Word клетка.

#### 📄 `Semantics/DomainModels.cs`
Дефинира структурата на данните. Всички `*SqM`/`*Sqm` полета са необработени (unrounded) стойности в $m^2$ и участват свободно в по-нататъшна аритметика. Всички `*Decares`/`*Dka` свойства са **само за показване** — тънки обвивки върху `AreaUnits.SqmToDka(...)`, които никога не се сумират или изваждат едно от друго.

1. **`class ParcelData`**
   - Полета за имот: `ParcelId`, `Owner`, `Ekatte`, `DocumentArea` ($m^2$), `ObjectId`.
   - Регистрови полета: `SubDivision`, `TerritoryType`, `Usage`, `Locality`, `Category`, `OwnershipType`, `OwnerId`, `OwnerName`.
   - MVP Math Test полета: `TotalAreaSqm`, `ServitudeGrossAreaSqm`, `ServitudeNetAreaSqm`, `PoleAreaSqm`, `MathDifference` ($m^2$).
   - **`RemainderAreaSqm`** (изчислимо, $m^2$, необработено): $\max(0,\ \text{TotalAreaSqm} - \text{ServitudeNetAreaSqm} - \text{PoleAreaSqm})$.
   - **`RemainderAreaDka`** (само за показване): `AreaUnits.SqmToDka(RemainderAreaSqm)`.
2. **`class Servitude`**
   - Полета: `ServitudeId`, `AssignedParcelId`, `Area` ($m^2$), `ObjectId`.
3. **`class Pole`**
   - Полета: `PoleId`, `AssignedParcelId`, `PoleNumber`, `PoleAreaSqM` ($m^2$), `Location` (`Point3d`), `ObjectId`.
   - **`PoleAreaDecares`** (само за показване): `AreaUnits.SqmToDka(PoleAreaSqM)`.
4. **`class ReportRow`**
   - Съдържа пълния набор от данни за ред в отчетите: `DocumentAreaSqM`, `ServitudeAreaSqM`, `PoleAreaSqM` ($m^2$, необработени).
   - **`RemainderAreaSqM`** (изчислимо, $m^2$, необработено): $\max(0,\ \text{DocumentAreaSqM} - \text{ServitudeAreaSqM})$. `ServitudeAreaSqM` е брутното сечение имот/сервитут (вече съдържа площта на стълбовете), затова тя не се изважда повторно тук.
   - **Преобразувания в декари (само за показване):**
     $$\text{DocumentAreaDecares} \implies \text{AreaUnits.SqmToDka}(\text{DocumentAreaSqM})$$
     $$\text{ServitudeAreaDecares} \implies \text{AreaUnits.SqmToDka}(\text{ServitudeAreaSqM})$$
     $$\text{PoleAreaDecares} \implies \text{AreaUnits.SqmToDka}(\text{PoleAreaSqM})$$
     $$\text{RemainderAreaDecares} \implies \text{AreaUnits.SqmToDka}(\text{RemainderAreaSqM})$$
   - **Допълнителни изчислими свойства:**
     - `AssignedPoles` (`List<Pole>`) — списък на причислените стълбове към имота.
     - `PoleNumbers` — форматиран низ от номера: `"Стълб №24,Стълб №23"` (сортиран по `PoleNumber`).
5. **`class VertexCoordinate`**
   - Полета: `PointIndex`, `PointLabel` (string), `X` (double), `Y` (double).
   - Използва се за координатните регистри (Word шаблони 07 и 08).

---

### 📁 DataBridge Layer

#### 📄 `DataBridge/CadLibraryReader.cs`
Парсва текстовия регистър от външни среди.

1. **`public CadLibraryReader(Logger logger)`**
   - **Логика:** Инжектира `Logger`.
2. **`public Dictionary<string, ParcelData> LoadLibrary(string fileName)`**
   - **Сигнатура:** `(string fileName) -> Dictionary<string, ParcelData>`
   - **Логика:** Проверява съществуването на файла. Прочита редовете с `File.ReadAllLines`. Разделя всеки ред по символи `,`, `;`, `\t`.
   - **Защитна функция (Safe Parsing):**
     Дефинира локалната функция `SafeCol(int index, string fallback = "")`, която връща почистения низ или `fallback`, ако колоната липсва в по-стари версии на `.cad` файла. Парсва 12 колони (индекси 0–11).
   - Преобразува площта чрез `double.TryParse`. Добавя валидните редове в речника с ключ `ParcelId`.

---

#### 📄 `DataBridge/ExcelReportGenerator.cs`
Попълва Excel файл от шаблон `TemplateX.xls` с помощта на NPOI.

1. **`public ExcelReportGenerator(Logger logger, string projectDirectory)`**
   - **Логика:** Дефинира абсолютен път до шаблона: `Path.Combine(projectDirectory, "_Templates", "TemplateX.xls")`.
2. **`public void GenerateReport(List<ReportRow> data, List<Pole> poles, Dictionary<string, ParcelData> parcelDb, string outputFilePath)`**
   - **Логика:** Зарежда `HSSFWorkbook` от потока на шаблона. Зарежда последователно трите работни листа:
     - Sheet 0: `"Засегнати имоти"` (попълва 14 колони, индекси 0–13, съгласно реда в шаблона: имот, подотдели, територия, НТП, местност, категория, площ, сервитут, остатък, стълбове, площ на стъпка, собственост, ЕГН, име).
     - Sheet 1: `"Стълбове"` (сортира по `PoleNumber`, търси собственик по `AssignedParcelId`).
     - Sheet 2: `"Баланси"` (извиква `PopulateBalancesSheet`).
     Записва изходния файл.
3. **`private void PopulateSheet(ISheet sheet, int startRowIndex, int rowCount, Action<IRow, IRow, int> writeAction)`**
   - **Логика:** Взема примерния ред (за преписване на стила). Извиква `ShiftRowsDown` за изместване на формулите и футера надолу, след което изписва данните реда по ред.
4. **`private void PopulateBalancesSheet(IWorkbook workbook, ISheet sheet, List<ReportRow> data)`**
   - **Логика:** Изпълнява 4 групиращи LINQ заявки (по `Category`, `OwnershipType`, `TerritoryType`, `Usage`). За всяко групиране измества съдържанието надолу, изписва заглавие, заглавни колони и сумите. Сумирането става върху необработените `*SqM` полета на всеки ред от групата (`group.Sum(r => r.DocumentAreaSqM)` и т.н.), а преобразуването в декари — веднъж, чрез `AreaUnits.SqmToDka(...)`, при записа на клетката. Броячите (`PoleCount`) остават цели числа без преобразуване.
5. **`private void ShiftRowsDown(ISheet sheet, int firstRowIndex, int count)`**
   - **Логика:** Използва `sheet.ShiftRows(firstRowIndex, sheet.LastRowNum, count, copyRowHeight: true, resetOriginalRowHeight: false)`.
6. **`private void SetCell(...)` (Overloads)**
   - **Логика:** Задава типа на клетката (`CellType.String` / `CellType.Numeric`), вписва стойността и прилага шаблония стил.
7. **`private ICellStyle GetAreaCellStyle(IWorkbook workbook, ICellStyle? baseStyle)`**
   - **Логика:** Клонира `baseStyle` (бордюри, шрифт, подравняване) и налага числов формат `"0.000"`, така че всяка площна стойност да се показва с точно 3 знака след десетичната запетая. Клонираните стилове се кешират в `_areaStyleCache` (изчистван в началото на всяко `GenerateReport`), защото legacy `.xls` форматът има таван на броя различни стилове в една книга.

---

#### 📄 `DataBridge/WordReportGenerator.cs`
Генерира Word (.docm) регистри от шаблони чрез OpenXML SDK.

1. **`public WordReportGenerator(Logger logger, string projectDirectory)`**
   - **Логика:** Инжектира `Logger` и задава пътя до `_Templates` папката.
2. **`public void GenerateAllReports(...)`**
   - **Сигнатура:** `(List<ReportRow> reportRows, List<Pole> assignedPoles, Dictionary<string, ParcelData> parcelDb, string outputDir, string settlementName, string ekatte, string municipality, string oblast, Dictionary<string, List<VertexCoordinate>>? poleVertices, List<VertexCoordinate>? servitudeVertices) -> void`
   - **Логика:** Диспечира генерацията на всички 7 регистъра последователно.
3. **`private void GenerateParcelRegister(List<ReportRow> data, string outputDir)`**
   - **Шаблон:** `D306-31Y0-02` — Регистър на засегнатите имоти (14 колони).
   - **Логика:** Клонира template row, попълва 14 колони с данни за всеки засегнат имот.
4. **`private void GeneratePoleStepsRegister(List<Pole> poles, Dictionary<string, ParcelData> parcelDb, List<ReportRow> reportRows, string outputDir)`**
   - **Шаблон:** `D306-31Y0-03` — Регистър на стъпките на стълбове (9 колони + ред с тотали).
   - **Логика:** Сортира по `PoleNumber`, включва ред с тотали.
5. **`private void GenerateBalancesTerritory(List<ReportRow> data, string outputDir)`**
   - **Шаблон:** `D306-31Y0-04` — Баланси територията (4 групирани подтаблици, 9 колони).
   - **Логика:** Работи с 4 таблици в шаблона. Групира данните по Category, OwnershipType, TerritoryType, Usage. Всяка група включва тотали и процентно разпределение.
6. **`private void GenerateBalancesMunicipality(List<ReportRow> data, Dictionary<string, ParcelData> parcelDb, string outputDir, ...)`**
   - **Шаблон:** `D306-31Y0-05` — Общ Баланс за общината (9 колони).
   - **Логика:** Групира по землище (Ekatte от `ParcelData`). Единична агрегирана таблица с тотали за всяко населено място.
7. **`private void GenerateRecapitulation(List<ReportRow> data, string outputDir)`**
   - **Шаблон:** `D306-31Y0-06` — Обща рекапитулация (7 колони).
   - **Логика:** Групира по `TerritoryType`. Включва ред с общи тотали.
8. **`private void GenerateCoordinateRegisterPoles(List<Pole> poles, Dictionary<string, List<VertexCoordinate>> poleVertices, string outputDir)`**
   - **Шаблон:** `D306-31Y0-07` — Координатен регистър на стъпките на стълбовете.
   - **Логика:** Центроид + върхови координати за всеки стълб.
9. **`private void GenerateCoordinateRegisterServitude(List<VertexCoordinate> vertices, string outputDir)`**
   - **Шаблон:** `D306-31Y0-08` — Координатен регистър на сервитута.
   - **Логика:** Последователен списък на върховите координати.
10. **`private void SetCellText(List<TableCell> cells, int index, string text)`**
    - **Логика:** Запазва оригиналните `RunProperties` (шрифт, размер, bold) от шаблона, изчиства старите `Run` елементи и вмъква нов `Run` с новия текст.

---

#### 📄 `DataBridge/BasicExcelExporter.cs`
Диагностичен, самостоятелен експортер за "MVP Математически тест" резултатите — **не** използва NPOI/шаблони, а генерира `.xlsx` директно чрез OpenXML SDK (`DocumentFormat.OpenXml.Spreadsheet`).

1. **`public static void ExportMathTest(List<ParcelData> parcels, string outputDir)`**
   - **Сигнатура:** `(List<ParcelData> parcels, string outputDir) -> void`
   - **Логика:** Създава `MVP_Math_Test_Parcels.xlsx` с 8 колони (Идентификатор, TotalArea, ServitudeGrossAreaSqm, ServitudeNetAreaSqm, PoleAreaSqm, Остатък, MathDifference, PoleNumbers). За имот с 0 или 1 стълб пише единичен ред; за имот с повече от 1 стълб пише по един ред на стълб и merge-ва (`MergeCells`) общите за имота колони (A, B, C, D, F, G), за да не се повтарят стойностите. `MathDifference` се изчислява като `ServitudeGrossAreaSqm − (ServitudeNetAreaSqm + PoleAreaSqm)` и се маркира текстово "ОК" при `|diff| ≤ 0.001`, иначе "ГРЕШКА".
   - **Забележка:** Тук се използва отделен, паралелен формат (`.xlsx` вместо основния `.xls`) и различна имплементация от `ExcelReportGenerator` — този файл е чисто диагностичен инструмент, не част от стандартния отчетен pipeline.

---

### 📁 Geometry Layer

#### 📄 `Geometry/TopologyProcessor.cs`
Извършва геометричните анализи с AutoCAD `Region` обекти.

1. **`public TopologyProcessor(Logger logger)`**
   - **Сигнатура:** `(Logger logger) -> void`
2. **`public Dictionary<string, double> CalculateServitudeIntersections(Polyline servitudePline, List<KeyValuePair<string, Polyline>> parcelPolylines, Transaction transaction)`**
   - **Логика:** Санитизира сервитута и всеки имот чрез `GeometrySanitizer.Sanitize()`, преобразува ги в `Region`. За всеки имот клонира сервитутния регион и изпълнява `intersectRegion.BooleanOperation(BooleanOperationType.BoolIntersect, parcelRegion)`. Записва площта, ако е по-голяма от `SliverTolerance` ($0.001 m^2$).
3. **`public List<Pole> AssignPolesToParcels(List<KeyValuePair<string, Polyline>> polePolylines, List<KeyValuePair<string, Polyline>> parcelPolylines, Transaction transaction)`**
   - **Логика:** ⚠️ Въпреки името "Dominant Area", методът **не** избира само доминиращия имот — за всеки стълб изчислява сечението с **всеки** имот от списъка и записва в `pole.OverlappingParcels` **всички** резултати над `SliverTolerance` (не само максимума). Ако стълб не пресича нито един имот, логва Warning с Handle и X,Y координатите на центроида ("floating geometry"). Виж коригирания раздел §4.1 по-долу за пълния анализ на последствията.
4. **`public List<ParcelData> RunMvpMathTest(Polyline servitudePline, List<KeyValuePair<string, Polyline>> polePolylines, List<KeyValuePair<string, Polyline>> parcelPolylines, Transaction transaction)`**
   - **Сигнатура:** `(...) -> List<ParcelData>`
   - **Логика:** Диагностичен път, независим от `CalculateServitudeIntersections`/`AssignPolesToParcels`. За всеки имот изчислява: (1) `ServitudeGrossAreaSqm` — директно сечение имот∩сервитут чрез `GetPreciseIntersectionArea()`; (2) `PoleAreaSqm` и `IndividualPoleAreas` — сумата от сеченията имот∩всеки стълб; (3) `ServitudeNetAreaSqm` чрез `GetPreciseSubtractedArea()` — сечение имот∩сервитут, от което последователно се изважда ("`BoolSubtract`") площта на всеки застъпващ стълб; (4) `MathDifference = ServitudeGrossAreaSqm − (ServitudeNetAreaSqm + PoleAreaSqm)`, очаквано ≈ 0, като проверка за баланс. Резултатът се визуализира чрез `BasicExcelExporter.ExportMathTest()`.
5. **`private double GetPreciseIntersectionArea(...)` / `private double GetPreciseSubtractedArea(...)`**
   - **Логика:** Помощни методи за `RunMvpMathTest`. Клонират геометриите и ги транслират ("origin shift" чрез `Matrix3d.Displacement`) така, че минималната точка на имота да падне в началото на координатната система, преди да построят `Region`-и и да изпълнят булевите операции — цели се по-висока числена прецизност при координати с голяма абсолютна стойност (напр. в БГС2005).
6. **`public List<VertexCoordinate> ExtractPolylineVertices(Polyline pline, string labelPrefix = "")`**
   - **Сигнатура:** `(Polyline pline, string labelPrefix) -> List<VertexCoordinate>`
   - **Логика:** Обхожда всички върхове на полилинията (от `0` до `pline.NumberOfVertices`), извлича X и Y координатите, генерира етикети с опционален префикс (напр. `"23-1"`, `"23-2"`). Връща списък от `VertexCoordinate` обекти. Използва се за координатните регистри (шаблони 07 и 08).
7. **`private static Region? SafeCreateRegion(Polyline polyline)`**
   - **Логика:** Клонира полилинията (за да не гърми `eNotOpenForWrite`, ако е отворена `ForRead`), насилствено я затваря (`clone.Closed = true`), после извиква `Region.CreateFromCurves`. Връща първия генериран регион и освобождава останалите. Връща `null` при площ < 0.001 или грешка.
8. **`private Point3d GetPolylineCentroid(Polyline pline)`**
   - **Логика:** Изчислява усреднения център на върховете на полилинията (за логване, не геометричен центроид на площта).

---

#### 📄 `Geometry/GeometrySanitizer.cs`
Статичен помощен клас, който "почиства" полилиния преди подаването ѝ към булеви операции.

1. **`public static Polyline Sanitize(Polyline source, double maxSegmentLength = 50.0, double minVertexDistance = 0.05)`**
   - **Сигнатура:** `(Polyline source, double maxSegmentLength, double minVertexDistance) -> Polyline`
   - **Логика — Фаза 1 (Densification):** Всеки сегмент по-дълъг от `maxSegmentLength` се разделя на подсегменти с приблизително равна дължина (пропорционално разпределя bulge стойността за дъгови сегменти чрез `4·atan(bulge)`), за да се избегнат прекалено дълги хорди при последващи геометрични операции.
   - **Логика — Фаза 2 (Cleaning):** Премахва последователни върхове, чието разстояние е под `minVertexDistance` **и** нямат bulge (праволинейни микросегменти/дублирани точки), за да се избегнат "sliver" артефакти при `Region.CreateFromCurves`.
   - Копира `Elevation`, `Normal`, `Layer`, `Color`, `Linetype` от оригинала в резултата. Използва се от `TopologyProcessor.CalculateServitudeIntersections()` (сервитут и имот преди сечение), `PUP_SERV` и бутона "✂ Сегментиране" в GUI.

---

#### 📄 `Geometry/PoleFootprintExtractor.cs`
Статичен клас за извличане на 4-точковия ("P-tag") контур на стъпката на стълб от динамичен блок.

1. **`public static PoleFootprintResult ExtractFootprint(BlockReference blockRef, Transaction tr, Logger logger)`**
   - **Сигнатура:** `(BlockReference blockRef, Transaction tr, Logger logger) -> PoleFootprintResult`
   - **Логика:** Чете атрибутите на блока. Номерът на стълба се търси в тагове `НОМЕР_НА_СТЪЛБА`/`СТЪЛБ_№`/`NOMER`. Координатните точки се търсят в тагове, започващи с `P` (стриктно изключвайки тагове, започващи с `TP`).
   - **Йерархия на съвпадение:** (1) Ако блокът е динамичен и има активно състояние на видимост (`Visibility`/`Видимост`), първо се търсят тагове от вида `P1-<VISIBILITY>` … `P4-<VISIBILITY>`; (2) при непълен резултат — директно съвпадение по `P1`…`P4` без суфикс за видимост.
   - Всяка точка се парсва от низ `"X, Y"` чрез `double.TryParse(..., NumberStyles.Any, CultureInfo.InvariantCulture)`. Изисква се намирането на точно 4 валидни точки, иначе връща `ErrorMessage` и празен `FootprintPolyline`.
   - 4-те точки се подреждат обратно на часовниковата стрелка около центроида си (`Math.Atan2`) и се конструира затворена `Polyline`. `AreaSqM` се задава от `pline.Area`.
   - **`private static string GetEffectiveName(...)`** / **`private static string GetVisibilityState(...)`** — помощни методи за име на динамичен блок и текущо състояние на видимост.

---

#### 📄 `Geometry/ServitudeMarkerGenerator.cs`
Чертожна (не отчетна) функционалност за поставяне на номерирани точки на всеки 20 м по двете страни на сервитутния коридор.

1. **`public void GenerateMarkers(Polyline servitudePline, Transaction tr, int startLeft = 5001, int startRight = 1)`**
   - **Сигнатура:** `(Polyline servitudePline, Transaction tr, int startLeft, int startRight) -> void`
   - **Логика:** Намира двата върха на полилинията с максимално разстояние помежду им (приема се, че бележат двата ѝ края), след което обхожда контура в двете посоки от тази двойка, за да раздели корпуса на "лява" и "дясна" страна. Премахва дублирани съседни точки (`RemoveAdjacentDuplicates`), ориентира двете страни в еднаква посока и поставя `DBPoint` + номериран `DBText` на всеки 20 м (пропуска последния "паразитен" остатък под 15 м), с текст, завъртян перпендикулярно на посоката на движение.
   - Лявата страна стартира от `startLeft` (по подразбиране 5001), дясната — от `startRight` (по подразбиране 1). Извиква се от бутона "📍 Само Точки (20м)" в GUI; няма CLI еквивалент.

---

## 4. 🧮 КЛЮЧОВИ АЛГОРИТМИ И МАТЕМАТИКА

### 1️⃣ Алгоритъм "Multi-Parcel Overlap Assignment" (Причисляване по всички засечени имоти)

> ⚠️ **Корекция спрямо по-стара версия на този документ:** тук по-рано беше описан "argmax" (доминираща площ) алгоритъм. Прегледът на реалния код в `TopologyProcessor.AssignPolesToParcels()` (2026-09-28) показа, че методът **не** избира само имота с максимална площ — той запазва сечението с **всеки** имот, чиято площ на застъпване е над толеранса. Описанието по-долу отразява действителното поведение на кода.

При стълбове, разположени на имотни граници, техният геометричен контур пресича повече от един имот.

```
       Имот 101                 Имот 102
+-----------------------+-----------------------+
|                       |                       |
|                 +-----+-----+                 |
|                 |  A1 |  A2 |                 |  <- Стълб (Pole)
|                 |     |     |                 |
|                 +-----+-----+                 |
|                       |                       |
+-----------------------+-----------------------+
```

#### Математически модел (действително поведение):
1. За контур на стълб $P$ и имот $K_i$ се изчислява площта на застъпване:
   $$A_{\text{int}}(i) = \text{Area}(\text{Region}(P) \cap \text{Region}(K_i))$$
2. **Филтриране на микро-сечения (Slivers):**
   $$\text{Ако } A_{\text{int}}(i) \le 0.001 \text{ m}^2 \implies \text{сечението се отхвърля.}$$
3. **Причисляване (без избор на максимум):**
   $$\text{За всеки } K_i \text{ с } A_{\text{int}}(i) > 0.001 \implies \text{pole.OverlappingParcels}[K_i] = A_{\text{int}}(i)$$
   Стълб A от примера по-горе (пресичащ и Имот 101, и Имот 102) ще се появи в отчетите на **и двата** имота — веднъж с площ $A_1$, веднъж с площ $A_2$ — а не само в единия. Съответно `PoleCount`/"Брой стълбове" в обобщените баланси **сумира по имот**, така че един физически стълб на граница се преброява повече от веднъж на ниво "Баланси"/"Рекапитулация" (сумата от площите му по имоти остава коректна — тя не се дублира, дублира се само броят).
   Ако желаното поведение е класическият "доминираща площ" избор (стълб → точно един имот с `arg max`), е нужна изрична промяна в `AssignPolesToParcels()` (напр. `OrderByDescending(area).First()` след събиране на всички сечения); към момента на този одит кодът не прави това.

---

### 2️⃣ Алгоритъм "Dynamic Template Row Insertion" (Динамично вмъкване в Excel)

За да не се нарушат формулите и форматирането във футера на шаблона `TemplateX.xls`:

1. Извлича се стилът от реда-образец `startRowIndex = 5`.
2. За $N$ броя редове с данни се изчисляват нужните нови редове $\Delta R = N - 1$.
3. Изпълнява се NPOI ShiftRows:
   ```csharp
   sheet.ShiftRows(startRowIndex + 1, sheet.LastRowNum, rowsToInsert, copyRowHeight: true, resetOriginalRowHeight: false);
   ```
4. Всички формули във футера (напр. `=SUM(...)`) автоматично актуализират своя обхват без загуба на референции.

---

### 3️⃣ Алгоритъм "Word Template Row Cloning" (Клониране на шаблонни редове в Word)

За генерация на Word регистрите, системата прилага следния подход:

1. Копира `.docm` шаблона в изходната папка.
2. Отваря документа с `WordprocessingDocument.Open(path, true)`.
3. Намира таблицата и извлича последния ред като template row чрез `CloneNode(true)`.
4. Премахва template row от таблицата.
5. За всеки запис клонира template row и попълва клетките чрез `SetCellText()`.
6. `SetCellText()` запазва оригиналните `RunProperties` (шрифт, размер, bold, italic) от шаблона, гарантирайки консистентно форматиране.

---

## 5. 📊 ТРАНСФОРМАЦИЯ НА ДАННИТЕ (Data Pipeline Diagram)

```
[DWG Polyline Handle: "A2F"] (Графика)         [TemplateC.cad String] (Текст)
       │                                               │
       ▼                                               ▼
+------------------------------------+  +------------------------------------+
| TransactionManager                 |  | CadLibraryReader                   |
| SelectMultiplePolylines()          |  | LoadLibrary()                      |
| Извлича: KeyValuePair<string,Poly> |  | SafeCol(), double.TryParse()       |
+------------------------------------+  +------------------------------------+
       │                                               │
       │  (ParcelId: "68134.501.123")                  │  (ParcelId: "68134.501.123")
       ▼                                               ▼
+----------------------------------------------------------------------------+
| TopologyProcessor & Geometry Engine                                        |
| 1. CalculateServitudeIntersections() -> ServitudeAreaSqM = 450.25 m²       |
| 2. AssignPolesToParcels()            -> PoleAreaSqM = 12.50 m², Count = 1 |
| 3. ExtractPolylineVertices()         -> List<VertexCoordinate>             |
+----------------------------------------------------------------------------+
                                       │
                                       ▼
+----------------------------------------------------------------------------+
| MainCommands.MergeResultsStatic()   (public static)                        |
| Напасване по ParcelId:                                                      |
|  - Взема кадастралните полета от ParcelData (или "NO DATA" ако липсва)     |
|  - Обединява графично изчислените площи в m²                               |
+----------------------------------------------------------------------------+
                                       │
                                       ▼
+----------------------------------------------------------------------------+
| Semantics.ReportRow (Domain Object)                                        |
|  Съхранени полета (m², необработени, участват в аритметика):               |
|  - DocumentAreaSqM   = 12500.0 m²                                          |
|  - ServitudeAreaSqM  =   450.25 m²                                         |
|  - PoleAreaSqM       =    12.50 m²                                         |
|  - RemainderAreaSqM  = max(0, 12500.0 - 450.25) = 12049.75 m² (необработено)|
|  Показвани свойства (AreaUnits.SqmToDka, само в изходния файл):            |
|  - DocumentAreaDecares  = 12.500 дка                                       |
|  - ServitudeAreaDecares =  0.450 дка                                       |
|  - PoleAreaDecares      =  0.013 дка   (12.50 / 1000 = 0.0125 → AwayFromZero)|
|  - RemainderAreaDecares = AreaUnits.SqmToDka(12049.75) = 12.050 дка         |
|  - AssignedPoles: [Pole{...}]     ──► PoleNumbers = "Стълб №24"            |
+----------------------------------------------------------------------------+
                                       │
                          ┌────────────┴────────────┐
                          ▼                          ▼
+-------------------------------+  +-------------------------------+
| ExcelReportGenerator          |  | WordReportGenerator           |
| "Засегнати имоти" (14 col)    |  | D306-31Y0-02 → 08            |
| "Стълбове" (5 col)            |  | 7 × .docm регистъра          |
| "Баланси" (4× LINQ groups)   |  | CloneNode + SetCellText      |
+-------------------------------+  +-------------------------------+
              │                                │
              ▼                                ▼
    📄 PUP_Report.xls            📄 7 × Word (.docm) регистри
                    📝 PUP_AUTO_Logs.txt
```

### 5.1 ⚠️ Граница на закръгляване (m² → дка) и очакван страничен ефект

Всички суми, разлики и проценти в `ExcelReportGenerator` и `WordReportGenerator` (редове на баланси, обща рекапитулация, тотали) се смятат върху необработени стойности в $m^2$. Преобразуването в декари и закръгляването до 3 знака (`AreaUnits.SqmToDka`) става **само веднъж**, точно преди записа в клетката — не преди сумирането.

**Последица:** обща сума в ред "Общо:" вече е `SqmToDka(Σ m²)`, а не `Σ SqmToDka(m²)` на отделните редове. Двете могат да се различават с до ±0.001 дка при много редове — това е коректно и по-точно поведение (грешките от закръгляване вече не се натрупват), **не дефект**. Пример:

| Ред | ServitudeAreaSqM (m²) | Старо: закръгли, после сумирай | Ново: сумирай, после закръгли |
|---|---|---|---|
| Имот А | 450.2 | 0.450 | — |
| Имот Б | 12.35 | 0.012 | — |
| **Общо** | **462.55** | 0.450 + 0.012 = **0.462** дка | `SqmToDka(462.55)` = **0.463** дка |

Разликата (0.001 дка) идва от това, че 12.35 m² сам по себе си закръглява до 0.012 дка, но приносът му към общата сума в декари всъщност е 0.01235 — новият метод го отчита точно.

---

## 6. 🛠️ РЪКОВОДСТВО ЗА ПОТРЕБИТЕЛЯ

### 1. Компилиране на проекта:
Отворете командния ред (Terminal) в коренната папка на проекта и изпълнете:
```bash
dotnet build PUP_AUTO.slnx --configuration Release
```
Или компилирайте през Visual Studio (`Build Solution`). Генерираният DLL файл ще се намира в `bin/Release/net48/PUP_AUTO.dll`.

### 2. Зареждане в Autodesk Civil 3D / AutoCAD:
1. Стартирайте Civil 3D.
2. Отворете целевия DWG чертеж.
3. Въведете командата `NETLOAD` в командния ред.
4. Навигирайте и изберете компилирания файл `PUP_AUTO.dll`.
5. Ribbon табът **"ПУП АВТОМАТИЗАЦИЯ"** ще се появи автоматично с два бутона.

### 3. Настройка на тестови папки и шаблони:
Уверете се, че в папката на DWG чертежа съществуват следните поддиректории:
- `_TestFiles/TemplateC.cad` — Текстовият файл с кадастралния регистър.
- `_Templates/` — Папка с шаблонни файлове:
  - `TemplateX.xls` — Excel шаблон с редове-образци и формули.
  - `D306-31Y0-02-0A - Регистър на засегнатите имоти.docm`
  - `D306-31Y0-03-0A - Регистър на стъпките на стълбове.docm`
  - `D306-31Y0-04-0A - Баланси територията.docm`
  - `D306-31Y0-05-0A - Общ Баланс за общината.docm`
  - `D306-31Y0-06-0A - Обща рекапитулация.docm`
  - `D306-31Y0-07-0A - Координатен регистър на стъпките на стълбовете.docm`
  - `D306-31Y0-08-0A - Координатен регистър на сервитута.docm`

### 4. Изпълнение на командите:

#### Вариант А: Команден ред (PUP_GENERATE)
1. Напишете `PUP_GENERATE` в командния ред и натиснете `Enter`.
2. Изберете полилинията на сервитута.
3. Изберете полилиниите на стълбовете (натиснете `Enter` за потвърждение).
4. Изберете полилиниите на имотите (натиснете `Enter` за потвърждение).
5. Системата автоматично генерира всички Excel и Word отчети.

#### Вариант Б: Графичен прозорец (PUP_WINDOW)
1. Напишете `PUP_WINDOW` в командния ред (или натиснете бутона "Отвори Прозорец" от Ribbon таба).
2. Проверете/променете пътищата до `.cad` базата и папката с шаблони.
3. Изберете геометриите чрез трите бутона (Сервитут / Стълбове / Имоти).
4. Включете/изключете желаните изходи чрез checkboxes.
5. Натиснете "🚀 ГЕНЕРИРАЙ ОТЧЕТИ".
6. Следете прогреса в лог конзолата.

### 5. Допълнителни / диагностични команди:
- **`PUP_SERV`** — избира полилиния (сервитут) и разстояние за сегментиране (по подразбиране 20 м), чертае санитизирана/сегментирана версия на слой `"segmented SERV"`. Полезно за визуална проверка преди основния `PUP_GENERATE`.
- **`PUP_DRAW_FOOTPRINTS`** — обхожда всички блокови референции в чертежа, извлича 4-точковите стъпки на стълбовете (P-tag логика) и ги чертае заедно с диагонали и номера на стълб, без да генерира отчети. Диагностичен инструмент за проверка на блоковите атрибути преди пускане на `PUP_GENERATE`.
- **"🧪 MVP Математически тест"** (само в `PUP_WINDOW`) — генерира `MVP_Math_Test_Parcels.xlsx` с детайлна разбивка Gross/Net/Pole площ и автоматична проверка за баланс ("ОК"/"ГРЕШКА"), без да пипа стандартните Excel/Word изходи.

### 6. Резултати и проверка на лог файла:
- Генерираният Excel доклад се записва като `PUP_Report.xls` в папката на чертежа.
- Word регистрите се записват в папката на чертежа с оригиналните имена на шаблоните.
- При възникнали предупреждения (липсващи данни за имоти или стълбове извън имоти), отворете файла `PUP_AUTO_Logs.txt` за подробна диагностика.
