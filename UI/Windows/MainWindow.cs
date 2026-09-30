using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Geometry;
using PUP_AUTO.Semantics;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PUP_AUTO.UI.Windows
{
    /// <summary>
    /// PUP_AUTO main WPF window built entirely in code (no XAML required).
    /// Handles file browsing, geometry picking from AutoCAD, and report generation.
    /// </summary>
    public class MainWindow : Window
    {
        // ---- UI Controls ----
        private TextBox _txtCadPath = null!;
        private TextBox _txtTemplatePath = null!;
        private TextBlock _lblServitude = null!;
        private TextBlock _lblPoles = null!;
        private TextBlock _lblParcels = null!;
        private CheckBox _chkExcel = null!;
        private CheckBox _chkWord = null!;
        private CheckBox _chkCoordinates = null!;
        private CheckBox _chkMvpMathTest = null!;
        private CheckBox _chkPoleSteps = null!;
        private CheckBox _chkCadControl = null!;
        private TextBlock _lblCadRegister = null!;
        private TextBox _txtStartNumLeft = null!;
        private TextBox _txtStartNumRight = null!;
        private TextBox _txtSegmentDistance = null!;
        private TextBox _txtLog = null!;

        // ---- State ----
        private string _projectDir = string.Empty;
        private string? _cadFilePath;
        private string? _templateDirPath;

        // The loaded AGKK .cad (one землище). Not bound to a drawing, so it survives switching drawings.
        private CadRegisterData? _cadRegister;

        // Picks are data (ObjectIds) bound to the drawing they were made in; see "PICK STATE".
        private Document? _pickDoc;
        private ObjectId _servitudeId = ObjectId.Null;
        private List<ParcelPick> _parcelPicks = new List<ParcelPick>();
        private List<PolePick> _polePicks = new List<PolePick>();

        private SelectionService? _selection;
        private Logger? _logger;

        // ---- Colors (Catppuccin Mocha dark theme) ----
        private static readonly SolidColorBrush BgBrush       = B("#1E1E2E");
        private static readonly SolidColorBrush SurfaceBrush   = B("#313244");
        private static readonly SolidColorBrush Surface2Brush  = B("#45475A");
        private static readonly SolidColorBrush AccentBrush    = B("#89B4FA");
        private static readonly SolidColorBrush TextBrush      = B("#CDD6F4");
        private static readonly SolidColorBrush SubtextBrush   = B("#A6ADC8");
        private static readonly SolidColorBrush GreenBrush     = B("#A6E3A1");
        private static readonly SolidColorBrush RedBrush       = B("#F38BA8");
        private static readonly SolidColorBrush YellowBrush    = B("#F9E2AF");

        private static SolidColorBrush B(string hex) =>
            new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

        public MainWindow()
        {
            BuildUI();
            ResolveDefaults();
            Application.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;
        }

        // ================================================================
        //  UI CONSTRUCTION
        // ================================================================

        private void BuildUI()
        {
            Title = "ПУП АВТОМАТИЗАЦИЯ — PUP_AUTO v0.1";
            Width = 720;
            Height = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = BgBrush;

            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 0: Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 1: Files
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 2: Geometry
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 3: Options
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4: Log
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 5: Generate

            // ── Row 0: Header ──
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            header.Children.Add(new TextBlock
            {
                Text = "⚡ ПУП АВТОМАТИЗАЦИЯ",
                FontSize = 22, FontWeight = FontWeights.Bold, Foreground = AccentBrush
            });
            header.Children.Add(new TextBlock
            {
                Text = "Автоматично генериране на баланси и регистри за засегнати имоти",
                FontSize = 12, Foreground = SubtextBrush, Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // ── Row 1: File Selection ──
            var fileGroup = MakeGroupBox("📂 Входни файлове", 1);
            var fileGrid = new Grid();
            fileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fileGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            fileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fileGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
            fileGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            fileGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _txtCadPath = MakeTextBox("(Автоматично от папка на чертежа)", true);
            Grid.SetRow(_txtCadPath, 0); Grid.SetColumn(_txtCadPath, 0);
            fileGrid.Children.Add(_txtCadPath);

            var btnCad = MakeButton("📁 CSV Регистър");
            btnCad.Click += BtnBrowseCad_Click;
            Grid.SetRow(btnCad, 0); Grid.SetColumn(btnCad, 2);
            fileGrid.Children.Add(btnCad);

            _txtTemplatePath = MakeTextBox("(Автоматично от _Templates)", true);
            Grid.SetRow(_txtTemplatePath, 2); Grid.SetColumn(_txtTemplatePath, 0);
            fileGrid.Children.Add(_txtTemplatePath);

            var btnTpl = MakeButton("📁 Шаблони");
            btnTpl.Click += BtnBrowseTemplate_Click;
            Grid.SetRow(btnTpl, 2); Grid.SetColumn(btnTpl, 2);
            fileGrid.Children.Add(btnTpl);

            _lblCadRegister = new TextBlock
            {
                Text = "(не е зареден .cad регистър)", FontSize = 12, Foreground = YellowBrush,
                VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetRow(_lblCadRegister, 4); Grid.SetColumn(_lblCadRegister, 0);
            fileGrid.Children.Add(_lblCadRegister);

            var btnLoadCad = MakeButton("📂 Зареди .cad");
            btnLoadCad.Click += BtnLoadCadRegister_Click;
            Grid.SetRow(btnLoadCad, 4); Grid.SetColumn(btnLoadCad, 2);
            fileGrid.Children.Add(btnLoadCad);

            ((GroupBox)fileGroup).Content = fileGrid;
            mainGrid.Children.Add(fileGroup);

            // ── Row 2: Geometry Pick ──
            var geoGroup = MakeGroupBox("🎯 Избор на геометрии от чертежа", 2);
            var geoGrid = new Grid();
            geoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            geoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            geoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            geoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            geoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Servitude pick button
            _lblServitude = new TextBlock { Text = "(не е избран)", FontSize = 10, Foreground = YellowBrush, HorizontalAlignment = HorizontalAlignment.Center };
            var btnServ = MakePickButton("🔲 Сервитут", _lblServitude);
            btnServ.Click += BtnPickServitude_Click;
            Grid.SetColumn(btnServ, 0); geoGrid.Children.Add(btnServ);

            // Poles pick button
            _lblPoles = new TextBlock { Text = "(не са избрани)", FontSize = 10, Foreground = YellowBrush, HorizontalAlignment = HorizontalAlignment.Center };
            var btnPole = MakePickButton("📍 Стълбове", _lblPoles);
            btnPole.Click += BtnPickPoles_Click;
            Grid.SetColumn(btnPole, 2); geoGrid.Children.Add(btnPole);

            // Parcels pick button
            _lblParcels = new TextBlock { Text = "(не са избрани)", FontSize = 10, Foreground = YellowBrush, HorizontalAlignment = HorizontalAlignment.Center };
            var btnParc = MakePickButton("🗺️ Имоти", _lblParcels);
            btnParc.Click += BtnPickParcels_Click;
            Grid.SetColumn(btnParc, 4); geoGrid.Children.Add(btnParc);

            ((GroupBox)geoGroup).Content = geoGrid;
            mainGrid.Children.Add(geoGroup);

            // ── Row 3: Output Options ──
            var optGroup = MakeGroupBox("📤 Генериране", 3);
            var optStack = new StackPanel();
            _chkExcel = new CheckBox { Content = "Генерирай Excel отчет (.xls)", IsChecked = true, Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 4) };
            _chkWord = new CheckBox { Content = "Генерирай Word регистри (.docm)", IsChecked = true, Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 4) };
            _chkCoordinates = new CheckBox { Content = "Генерирай координатни регистри", IsChecked = true, Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 8) };
            _chkMvpMathTest = new CheckBox { Content = "🧪 MVP Математически тест (Excel)", IsChecked = false, Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 8) };
            _chkPoleSteps = new CheckBox { Content = "📐 Таблица стъпки на стълбове (Excel)", IsChecked = false, Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 8) };
            _chkCadControl = new CheckBox { Content = "🔎 Контролна справка от .cad (Excel)", IsChecked = false, Foreground = TextBrush, Margin = new Thickness(0, 0, 0, 8) };
            
            var numsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            numsPanel.Children.Add(new TextBlock { Text = "Старт Ляво:", Foreground = SubtextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            _txtStartNumLeft = new TextBox { Text = "5001", Width = 50, Background = SurfaceBrush, Foreground = TextBrush, BorderBrush = Surface2Brush };
            numsPanel.Children.Add(_txtStartNumLeft);
            
            numsPanel.Children.Add(new TextBlock { Text = "Старт Дясно:", Foreground = SubtextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
            _txtStartNumRight = new TextBox { Text = "1", Width = 50, Background = SurfaceBrush, Foreground = TextBrush, BorderBrush = Surface2Brush };
            numsPanel.Children.Add(_txtStartNumRight);

            var btnGenMarkers = new Button
            {
                Content = "📍 Само Точки (20м)",
                Width = 140, Height = 26, Margin = new Thickness(16, 0, 0, 0),
                Foreground = BgBrush, Background = AccentBrush,
                BorderBrush = AccentBrush, BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };
            btnGenMarkers.Click += BtnGenMarkers_Click;
            numsPanel.Children.Add(btnGenMarkers);

            var lblDist = new TextBlock { Text = "Разстояние:", Foreground = SubtextBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 4, 0) };
            numsPanel.Children.Add(lblDist);
            
            _txtSegmentDistance = new TextBox { Text = "50", Width = 40, Background = SurfaceBrush, Foreground = TextBrush, BorderBrush = Surface2Brush };
            numsPanel.Children.Add(_txtSegmentDistance);

            var btnSegment = new Button
            {
                Content = "✂ Сегментиране",
                Width = 120, Height = 26, Margin = new Thickness(8, 0, 0, 0),
                Foreground = BgBrush, Background = AccentBrush,
                BorderBrush = AccentBrush, BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };
            btnSegment.Click += BtnSegment_Click;
            numsPanel.Children.Add(btnSegment);

            optStack.Children.Add(_chkExcel);
            optStack.Children.Add(_chkWord);
            optStack.Children.Add(_chkCoordinates);
            optStack.Children.Add(_chkMvpMathTest);
            optStack.Children.Add(_chkPoleSteps);
            optStack.Children.Add(_chkCadControl);
            optStack.Children.Add(numsPanel);
            ((GroupBox)optGroup).Content = optStack;
            mainGrid.Children.Add(optGroup);

            // ── Row 4: Log ──
            _txtLog = new TextBox
            {
                Background = SurfaceBrush, Foreground = SubtextBrush,
                BorderBrush = Surface2Brush, BorderThickness = new Thickness(1),
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Consolas"), FontSize = 11,
                Padding = new Thickness(8),
                Text = "Готов за работа. Изберете геометрии и натиснете 'Генерирай'."
            };
            Grid.SetRow(_txtLog, 4);
            mainGrid.Children.Add(_txtLog);

            // ── Row 5: Generate Button ──
            var btnGen = new Button
            {
                Content = "🚀  ГЕНЕРИРАЙ ОТЧЕТИ",
                Height = 48,
                FontSize = 15, FontWeight = FontWeights.Bold,
                Foreground = GreenBrush,
                Background = B("#2A4A2A"),
                BorderBrush = GreenBrush, BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 12, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            btnGen.Click += BtnGenerate_Click;
            Grid.SetRow(btnGen, 5);
            mainGrid.Children.Add(btnGen);

            Content = mainGrid;
        }

        // ---- UI Factory Helpers ----

        private FrameworkElement MakeGroupBox(string header, int row)
        {
            var gb = new GroupBox
            {
                BorderBrush = Surface2Brush, Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 12), Foreground = TextBrush,
                Header = new TextBlock { Text = header, Foreground = AccentBrush, FontWeight = FontWeights.SemiBold }
            };
            Grid.SetRow(gb, row);
            return gb;
        }

        private TextBox MakeTextBox(string placeholder, bool readOnly)
        {
            return new TextBox
            {
                Background = SurfaceBrush, Foreground = TextBrush,
                BorderBrush = Surface2Brush, BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 6, 8, 6), FontSize = 12,
                IsReadOnly = readOnly, Text = placeholder
            };
        }

        private Button MakeButton(string text)
        {
            return new Button
            {
                Content = text, Padding = new Thickness(16, 8, 16, 8),
                FontSize = 13, Foreground = TextBrush,
                Background = SurfaceBrush, BorderBrush = Surface2Brush,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        private Button MakePickButton(string title, TextBlock statusLabel)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = title, HorizontalAlignment = HorizontalAlignment.Center, FontWeight = FontWeights.SemiBold });
            sp.Children.Add(statusLabel);

            return new Button
            {
                Content = sp, Padding = new Thickness(16, 8, 16, 8),
                FontSize = 13, Foreground = TextBrush,
                Background = B("#2A3A5E"), BorderBrush = AccentBrush,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand
            };
        }

        // ================================================================
        //  INITIALIZATION
        // ================================================================

        private void ResolveDefaults()
        {
            try
            {
                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null && !string.IsNullOrEmpty(doc.Name))
                {
                    string? dir = Path.GetDirectoryName(doc.Name);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        _projectDir = dir;

                        string cadPath = Path.Combine(dir, FileNames.TestFilesFolder, FileNames.CadLibraryFile);
                        if (File.Exists(cadPath))
                        {
                            _cadFilePath = cadPath;
                            _txtCadPath.Text = cadPath;
                        }

                        string tplDir = Path.Combine(dir, FileNames.TemplatesFolder);
                        if (Directory.Exists(tplDir))
                        {
                            _templateDirPath = tplDir;
                            _txtTemplatePath.Text = tplDir;
                        }
                    }
                }
            }
            catch { }
        }

        // ================================================================
        //  FILE BROWSING
        // ================================================================

        private void BtnBrowseCad_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Изберете .cad база данни",
                Filter = "GeoJSON/CSV Registri (*.geojson;*.csv;*.cad)|*.geojson;*.csv;*.cad|All Files (*.*)|*.*",
                InitialDirectory = _projectDir
            };
            if (dlg.ShowDialog() == true)
            {
                _cadFilePath = dlg.FileName;
                _txtCadPath.Text = dlg.FileName;
            }
        }

        private void BtnBrowseTemplate_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Изберете папката с шаблоните (_Templates)";
                dlg.SelectedPath = _templateDirPath ?? _projectDir;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _templateDirPath = dlg.SelectedPath;
                    _txtTemplatePath.Text = dlg.SelectedPath;
                }
            }
        }

        private const int MaxWindowWarnings = 20;

        /// <summary>Loads ONE AGKK .cad (one землище). Warnings go to the log file in full and to the window in part.</summary>
        private void BtnLoadCadRegister_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Изберете .cad файл на землището (АГКК)",
                Filter = "AGKK .cad (*.cad)|*.cad|All Files (*.*)|*.*",
                InitialDirectory = _projectDir
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                EnsureServices();

                var warnings = new List<string>();
                CadRegisterData register = new CadRegisterReader(warnings.Add).ReadFile(dlg.FileName);
                _cadRegister = register;

                _lblCadRegister.Text = $"✅ ЕКАТТЕ {register.Ekatte} — {register.SettlementName} · {register.Parcels.Count} имота";
                _lblCadRegister.Foreground = GreenBrush;
                _lblCadRegister.ToolTip = dlg.FileName;

                // Counts only: no names and no ЕГН/БУЛСТАТ in the log
                AppendLog($".cad заредено: ЕКАТТЕ {register.Ekatte}, {register.SettlementName} — " +
                          $"{register.Parcels.Count} имота, {register.Rights.Values.Sum(r => r.Count)} права, {register.PersonCount} лица.");

                foreach (string warning in warnings) _logger!.LogWarning(warning);
                foreach (string warning in warnings.Take(MaxWindowWarnings)) AppendLog($"ПРЕДУПРЕЖДЕНИЕ: {warning}");
                if (warnings.Count > MaxWindowWarnings)
                {
                    AppendLog($"… още {warnings.Count - MaxWindowWarnings} предупреждения — виж {FileNames.LogFile}.");
                }

                if (register.Parcels.Count == 0)
                {
                    AppendLog("ПРЕДУПРЕЖДЕНИЕ: В .cad няма имоти (таблица POZEMLIMOTI) — проверете файла.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при зареждане на .cad: {ex.Message}");
                _logger?.LogError($"Loading .cad failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ================================================================
        //  PICK STATE
        //  Picks are stored as data (ObjectIds), bound to the drawing they were made in.
        //  No transaction outlives a click; every handler locks the document, opens the
        //  objects by ObjectId in its own short transaction and commits.
        // ================================================================

        private sealed class ParcelPick
        {
            public ObjectId Id;
            public string ParcelId = string.Empty;
        }

        private sealed class PolePick
        {
            public ObjectId BlockId;
            public string Key = string.Empty;
        }

        private bool HasAnyPick() =>
            !_servitudeId.IsNull || _parcelPicks.Count > 0 || _polePicks.Count > 0;

        private void ClearPicks()
        {
            _servitudeId = ObjectId.Null;
            _parcelPicks = new List<ParcelPick>();
            _polePicks = new List<PolePick>();

            _lblServitude.Text = "(не е избран)";
            _lblServitude.Foreground = YellowBrush;
            _lblPoles.Text = "(не са избрани)";
            _lblPoles.Foreground = YellowBrush;
            _lblParcels.Text = "(не са избрани)";
            _lblParcels.Foreground = YellowBrush;
        }

        /// <summary>Binds the picks to the active drawing; picks made in another drawing are cleared.</summary>
        private bool BeginPick(out Document doc)
        {
            doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;

            if (!ReferenceEquals(_pickDoc, doc))
            {
                bool hadPicks = HasAnyPick();
                ClearPicks();
                _pickDoc = doc;

                // Outputs and the log follow the drawing the picks belong to
                string? dir = string.IsNullOrEmpty(doc.Name) ? null : Path.GetDirectoryName(doc.Name);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    _projectDir = dir!;
                }
                _logger = null;
                _selection = null;

                if (hadPicks) AppendLog("Изборът от предишния чертеж е изчистен.");
            }
            return true;
        }

        /// <summary>Checks that the picks belong to the active drawing before a button uses them.</summary>
        private bool TryUsePicks(out Document doc)
        {
            doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;

            if (_pickDoc != null && !ReferenceEquals(_pickDoc, doc) && HasAnyPick())
            {
                AppendLog("Изборът е направен в друг чертеж — изберете отново.");
                return false;
            }
            return true;
        }

        private void OnDocumentToBeDestroyed(object? sender, DocumentCollectionEventArgs e)
        {
            if (ReferenceEquals(e.Document, _pickDoc))
            {
                ClearPicks();
                _pickDoc = null;
            }
        }

        private void EnsureServices()
        {
            if (_logger == null)
            {
                string logPath = Path.Combine(_projectDir, FileNames.LogFile);
                _logger = new Logger(logPath);
            }
            if (_selection == null)
                _selection = new SelectionService(_logger);
        }

        // ---- Re-opening picks inside a handler's own transaction ----

        private static Polyline? OpenPolyline(Transaction tr, ObjectId id)
        {
            if (id.IsNull || id.IsErased) return null;
            return tr.GetObject(id, OpenMode.ForRead) as Polyline;
        }

        private List<KeyValuePair<string, Polyline>> OpenParcels(Transaction tr)
        {
            var parcels = new List<KeyValuePair<string, Polyline>>();
            foreach (var pick in _parcelPicks)
            {
                Polyline? pline = OpenPolyline(tr, pick.Id);
                if (pline != null)
                    parcels.Add(new KeyValuePair<string, Polyline>(pick.ParcelId, pline));
            }
            return parcels;
        }

        /// <summary>
        /// Extracts the pole footprints (in-memory polylines) of the picked pole blocks.
        /// The caller must dispose them with <see cref="DisposeFootprints"/>.
        /// </summary>
        private List<PoleFootprintEntry> ExtractPoles(Transaction tr)
        {
            var blocks = new List<KeyValuePair<string, BlockReference>>();
            foreach (var pick in _polePicks)
            {
                if (pick.BlockId.IsNull || pick.BlockId.IsErased) continue;
                if (tr.GetObject(pick.BlockId, OpenMode.ForRead) is BlockReference blockRef)
                    blocks.Add(new KeyValuePair<string, BlockReference>(pick.Key, blockRef));
            }
            return PoleFootprintExtractor.ExtractAll(blocks, tr).ToList();
        }

        private static List<KeyValuePair<string, Polyline>> PoleFootprints(List<PoleFootprintEntry> entries)
        {
            var poles = new List<KeyValuePair<string, Polyline>>();
            foreach (var entry in entries)
            {
                if (entry.Result.FootprintPolyline != null)
                    poles.Add(new KeyValuePair<string, Polyline>(entry.PoleId, entry.Result.FootprintPolyline));
            }
            return poles;
        }

        private static void DisposeFootprints(List<PoleFootprintEntry> entries)
        {
            foreach (var entry in entries)
            {
                entry.Result.FootprintPolyline?.Dispose();
            }
        }

        private static void FlushGraphics(Document doc)
        {
            doc.TransactionManager.QueueForGraphicsFlush();
            doc.Editor.UpdateScreen();
        }

        // ================================================================
        //  GEOMETRY PICKING
        // ================================================================

        private void BtnPickServitude_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginPick(out Document doc)) return;
            Editor ed = doc.Editor;

            using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
            {
                try
                {
                    EnsureServices();
                    using (doc.LockDocument())
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        Polyline? servitude = _selection!.SelectSinglePolyline(
                            tr, "\nSelect the Servitude (Right of Way) polyline: ");
                        _servitudeId = servitude?.ObjectId ?? ObjectId.Null;
                        tr.Commit();
                    }

                    if (!_servitudeId.IsNull)
                    {
                        _lblServitude.Text = "✅ Избран";
                        _lblServitude.Foreground = GreenBrush;
                        AppendLog("Сервитут избран успешно.");
                    }
                    else
                    {
                        _lblServitude.Text = "❌ Не е избран";
                        _lblServitude.Foreground = RedBrush;
                        AppendLog("ПРЕДУПРЕЖДЕНИЕ: Сервитутът не беше избран.");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"ГРЕШКА: {ex.Message}");
                }
            }
        }

        private void BtnPickPoles_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginPick(out Document doc)) return;
            Editor ed = doc.Editor;

            using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
            {
                try
                {
                    EnsureServices();

                    var picks = new List<PolePick>();
                    using (doc.LockDocument())
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var poleBlocks = _selection!.SelectMultipleBlockReferences(
                            tr, "\nSelect Pole blocks: ");

                        // Extract now to validate the blocks; the footprints are in-memory only
                        // and are disposed here (each handler re-extracts what it needs).
                        var entries = new List<PoleFootprintEntry>();
                        try
                        {
                            int index = 0;
                            foreach (var entry in PoleFootprintExtractor.ExtractAll(poleBlocks, tr))
                            {
                                entries.Add(entry);
                                if (entry.Result.FootprintPolyline != null)
                                {
                                    picks.Add(new PolePick
                                    {
                                        BlockId = poleBlocks[index].Value.ObjectId,
                                        Key = poleBlocks[index].Key
                                    });
                                }
                                else
                                {
                                    AppendLog($"ПРЕДУПРЕЖДЕНИЕ: {entry.Result.ErrorMessage}");
                                }
                                index++;
                            }
                        }
                        finally
                        {
                            DisposeFootprints(entries);
                        }
                        tr.Commit();
                    }
                    _polePicks = picks;

                    if (_polePicks.Count > 0)
                    {
                        _lblPoles.Text = $"✅ {_polePicks.Count} стълба";
                        _lblPoles.Foreground = GreenBrush;
                        AppendLog($"Избрани и екстрактнати {_polePicks.Count} стълба.");
                    }
                    else
                    {
                        _lblPoles.Text = "❌ Не са избрани";
                        _lblPoles.Foreground = RedBrush;
                        AppendLog("ПРЕДУПРЕЖДЕНИЕ: Не бяха извлечени валидни стълбове.");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"ГРЕШКА: {ex.Message}");
                }
            }
        }

        private void BtnPickParcels_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginPick(out Document doc)) return;
            Editor ed = doc.Editor;

            using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
            {
                try
                {
                    EnsureServices();

                    // Load GeoJSON geometries for spatial matching (if GeoJSON file is selected)
                    List<GeoParcel>? geoParcels = null;
                    string cadPath = _cadFilePath ?? "";
                    if (cadPath.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase))
                    {
                        var reader = new ParcelRegisterReader(_logger!);
                        geoParcels = reader.LoadGeoJsonGeometries(cadPath);
                        if (geoParcels.Count > 0)
                        {
                            AppendLog($"Заредени {geoParcels.Count} геометрии от GeoJSON за пространствено съвпадение.");
                        }
                        else
                        {
                            AppendLog("ПРЕДУПРЕЖДЕНИЕ: Няма геометрии в GeoJSON за пространствено съвпадение. Ще се използва Handle.");
                        }
                    }
                    else
                    {
                        AppendLog("ПРЕДУПРЕЖДЕНИЕ: Не е избран GeoJSON файл. Ще се опита Map3D OD (може да не работи).");
                    }

                    var selStats = new PolylineSelectionStats();
                    using (doc.LockDocument())
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var selected = _selection!.SelectMultiplePolylines(
                            tr, "\nSelect Parcel polylines: ", geoParcels,
                            _servitudeId, selStats);

                        // Keep the ID resolved now (XData / GeoJSON / handle); it is never re-read
                        _parcelPicks = selected
                            .Select(kvp => new ParcelPick { Id = kvp.Value.ObjectId, ParcelId = kvp.Key })
                            .ToList();
                        tr.Commit();
                    }

                    if (selStats.SkippedPluginLayer > 0)
                    {
                        AppendLog($"Пропуснати {selStats.SkippedPluginLayer} полилинии от служебни слоеве.");
                    }
                    if (selStats.HandleFallbacks.Count > 0)
                    {
                        AppendLog($"Внимание: {selStats.HandleFallbacks.Count} полилинии нямат идентификатор на имот (XData) — проверете: " +
                                  string.Join(", ", selStats.HandleFallbacks));
                    }

                    if (_parcelPicks.Count > 0)
                    {
                        _lblParcels.Text = $"✅ {_parcelPicks.Count} имота";
                        _lblParcels.Foreground = GreenBrush;
                        AppendLog($"Избрани {_parcelPicks.Count} имота.");
                    }
                    else
                    {
                        _lblParcels.Text = "❌ Не са избрани";
                        _lblParcels.Foreground = RedBrush;
                        AppendLog("ПРЕДУПРЕЖДЕНИЕ: Имотите не бяха избрани.");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"ГРЕШКА: {ex.Message}");
                }
            }
        }

        // ================================================================
        //  GENERATE REPORTS
        // ================================================================

        /// <summary>PLACEHOLDER — not production (Generate-All; only the MVP math-test branch is live).</summary>
        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryUsePicks(out Document doc)) return;

                // Standalone tables: independent of the MVP test and of the placeholder Generate-All.
                bool ranStandalone = false;

                // Pole-steps table: needs only poles and parcels (no servitude).
                if (_chkPoleSteps.IsChecked == true)
                {
                    if (_polePicks.Count == 0 || _parcelPicks.Count == 0)
                    {
                        if (_polePicks.Count == 0) AppendLog("ГРЕШКА: Не са избрани стълбове!");
                        if (_parcelPicks.Count == 0) AppendLog("ГРЕШКА: Не са избрани имоти!");
                        return;
                    }

                    RunPoleStepsTable(doc);
                    ranStandalone = true;
                }

                // Control report from the loaded .cad: needs the .cad and the parcels only.
                if (_chkCadControl.IsChecked == true)
                {
                    if (_cadRegister == null || _parcelPicks.Count == 0)
                    {
                        if (_cadRegister == null) AppendLog("ГРЕШКА: Не е зареден .cad регистър (бутон \"Зареди .cad\")!");
                        if (_parcelPicks.Count == 0) AppendLog("ГРЕШКА: Не са избрани имоти!");
                        return;
                    }

                    RunCadControlReport(doc, _cadRegister);
                    ranStandalone = true;
                }

                if (ranStandalone && _chkMvpMathTest.IsChecked != true) return;

                if (_servitudeId.IsNull)  { AppendLog("ГРЕШКА: Не е избран сервитут!"); return; }
                if (_polePicks.Count == 0) { AppendLog("ГРЕШКА: Не са избрани стълбове!"); return; }
                if (_parcelPicks.Count == 0) { AppendLog("ГРЕШКА: Не са избрани имоти!"); return; }

                EnsureServices();
                var topo = new TopologyProcessor(_logger!);

                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    Polyline? servitude = OpenPolyline(tr, _servitudeId);
                    if (servitude == null)
                    {
                        AppendLog("ГРЕШКА: Сервитутът вече не съществува в чертежа — изберете отново.");
                        return;
                    }

                    var parcels = OpenParcels(tr);
                    var entries = ExtractPoles(tr);
                    try
                    {
                        RunGenerate(topo, servitude, PoleFootprints(entries), parcels, entries);
                        tr.Commit();
                    }
                    finally
                    {
                        DisposeFootprints(entries);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"\nГРЕШКА: {ex.Message}\n{ex.StackTrace}");
                _logger?.LogError($"PUP_GENERATE (GUI) failed: {ex.Message}\n{ex.StackTrace}");

                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.Editor.WriteMessage($"\nFatal error in Generate: {ex.Message}\n");
                }
            }
        }

        /// <summary>
        /// Builds Стъпки_на_стълбове.xlsx from the picked poles and parcels: footprint ∩ parcel
        /// areas, one row per (parcel, pole) pair. Short transaction under a document lock; the
        /// in-memory footprints are disposed before the file is written.
        /// </summary>
        private void RunPoleStepsTable(Document doc)
        {
            try
            {
                AppendLog("── СТАРТИРАНЕ НА ТАБЛИЦА СТЪПКИ НА СТЪЛБОВЕ ──");
                EnsureServices();
                var topo = new TopologyProcessor(_logger!);

                PoleStepsGeometry? geometry = null;
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var parcels = OpenParcels(tr);
                    var entries = ExtractPoles(tr);
                    try
                    {
                        geometry = topo.ComputePoleStepPieces(PoleFootprints(entries), parcels);
                        tr.Commit();
                    }
                    finally
                    {
                        DisposeFootprints(entries);
                    }
                }

                foreach (var step in PoleStepsTableBuilder.FindUncoveredSteps(
                    geometry!.Footprints, geometry.Pieces, GeometryTolerances.SliverAreaSqm))
                {
                    AppendLog(PoleStepsTableBuilder.FormatUncoveredWarning(step));
                }

                var table = PoleStepsTableBuilder.Build(geometry.Pieces);
                PoleStepsExporter.Export(table, _projectDir);
                AppendLog($"  Записан {FileNames.PoleStepsFile} в {_projectDir}");
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при таблицата със стъпки: {ex.Message}");
                _logger?.LogError($"Pole steps table failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Builds Контролна_справка_cad.xlsx: the picked parcels against the loaded .cad, one row per right.
        /// Only the drawn areas are read from the drawing, in a short transaction under a document lock;
        /// everything else works on plain data. Personal data (ЕГН/БУЛСТАТ, names) never goes to the log.
        /// </summary>
        private void RunCadControlReport(Document doc, CadRegisterData register)
        {
            try
            {
                AppendLog("── СТАРТИРАНЕ НА КОНТРОЛНА СПРАВКА ОТ .CAD ──");
                EnsureServices();

                var drawn = new List<KeyValuePair<string, double>>();
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var parcel in OpenParcels(tr))
                    {
                        drawn.Add(new KeyValuePair<string, double>(parcel.Key, parcel.Value.Area));
                    }
                    tr.Commit();
                }

                string templateDir = _templateDirPath ?? Path.Combine(_projectDir, FileNames.TemplatesFolder);
                var warnings = new List<string>();
                Nomenclatures nomenclatures = Nomenclatures.Load(
                    Path.Combine(templateDir, FileNames.NomenclaturesFolder), warnings.Add);
                EkatteRegister ekatte = EkatteRegister.LoadWithDefaults(
                    Path.Combine(templateDir, FileNames.EkatteRegisterFile), warnings.Add);
                if (ekatte.Count > 0 && !ekatte.TryGet(register.Ekatte, out _))
                {
                    warnings.Add($"ЕКАТТЕ {register.Ekatte} не е в регистъра на ЕКАТТЕ — заглавието е непълно.");
                }

                string title = ekatte.FormatTitle(register.Ekatte, register.SettlementName);
                CadControlReport report = CadControlReportBuilder.Build(drawn, register, nomenclatures, title);

                // The nomenclature warnings ("no text for code N") are raised while the report is built
                foreach (string warning in warnings) LogWarning(warning);

                if (report.ForeignEkatte.Count > 0)
                {
                    LogWarning($"{report.ForeignEkatte.Count} избрани имота са с ЕКАТТЕ, различно от заредения .cad ({register.Ekatte}): " +
                               JoinLimited(report.ForeignEkatte.Select(f => $"{f.Key} (ЕКАТТЕ {f.Value})"), 10));
                }
                if (report.NotFound.Count > 0)
                {
                    LogWarning($"{report.NotFound.Count} избрани имота не са намерени в .cad: " +
                               JoinLimited(report.NotFound, 30));
                }
                if (report.WithoutRights.Count > 0)
                {
                    LogWarning($"{report.WithoutRights.Count} имота нямат права в .cad (един ред без собственик): " +
                               JoinLimited(report.WithoutRights, 30));
                }

                if (report.Rows.Count == 0)
                {
                    AppendLog("ГРЕШКА: Нито един избран имот не е намерен в .cad — файлът не е записан.");
                    return;
                }

                string path = CadControlReportExporter.Export(report, _projectDir);
                int parcelCount = report.Rows.Select(r => r.ParcelId).Distinct().Count();
                AppendLog($"  Записан {Path.GetFileName(path)} в {_projectDir}: {report.Rows.Count} реда, {parcelCount} имота.");
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при контролната справка: {ex.Message}");
                _logger?.LogError($"Cad control report failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>Writes a warning to the window log and to the log file.</summary>
        private void LogWarning(string message)
        {
            AppendLog($"ПРЕДУПРЕЖДЕНИЕ: {message}");
            _logger?.LogWarning(message);
        }

        private static string JoinLimited(IEnumerable<string> items, int limit)
        {
            List<string> all = items.ToList();
            string shown = string.Join(", ", all.Take(limit));
            return all.Count > limit ? $"{shown} … и още {all.Count - limit}" : shown;
        }

        private void RunGenerate(
            TopologyProcessor topo,
            Polyline servitude,
            List<KeyValuePair<string, Polyline>> poles,
            List<KeyValuePair<string, Polyline>> parcels,
            List<PoleFootprintEntry> entries)
        {
            if (_chkMvpMathTest.IsChecked == true)
            {
                AppendLog("── СТАРТИРАНЕ НА MVP MATH TEST ──");
                var testResults = topo.RunMvpMathTest(servitude, poles, parcels);
                MvpMathTestExporter.ExportMathTest(testResults, _projectDir);
                AppendLog($"  Записан {FileNames.MvpMathTestFile} в {_projectDir}");
                return;
            }

            AppendLog("═══ ГЕНЕРИРАНЕ СТАРТИРАНО ═══");

            // Footprint vertices by pole ID (a later duplicate ID wins, as before)
            var footprintVertices = new Dictionary<string, List<VertexCoordinate>>();
            foreach (var entry in entries)
            {
                if (entry.Result.FootprintPolyline != null)
                    footprintVertices[entry.PoleId] = entry.Vertices!;
            }

            // Step 1 — Load CAD database
            AppendLog("── Стъпка 1: Зареждане на CAD база ──");
            string cadPath = _cadFilePath ?? Path.Combine(_projectDir, FileNames.TestFilesFolder, FileNames.CadLibraryFile);
            var reader = new ParcelRegisterReader(_logger!);
            var parcelDb = reader.LoadLibrary(cadPath);
            AppendLog($"  Заредени {parcelDb.Count} записа от базата.");

            // Step 2 — Topology calculations
            AppendLog("── Стъпка 2: Топологични изчисления ──");
            var servitudeAreas = topo.CalculateServitudeIntersections(
                servitude, parcels);
            AppendLog($"  Сечения сервитут: {servitudeAreas.Count} имота.");

            var assignedPoles = topo.AssignPolesToParcels(
                poles, parcels);

            foreach (var p in assignedPoles)
            {
                if (footprintVertices.TryGetValue(p.PoleId, out var fv))
                {
                    p.FootprintVertices = fv;
                }
            }

            int assignedCount = assignedPoles.Count(p => p.OverlappingParcels.Count > 0);
            AppendLog($"  Стълбове: {assignedPoles.Count} обработени ({assignedCount} причислени).");

            // Step 3 — Merge results
            AppendLog("── Стъпка 3: Обединяване на резултати ──");
            var reportRows = ReportBuilder.BuildReportRows(
                parcels.Select(kvp => kvp.Key), parcelDb, servitudeAreas, assignedPoles,
                message => _logger!.LogWarning(message));
            AppendLog($"  Генерирани {reportRows.Count} реда за отчет.");

            // Step 4 — Extract coordinates (if enabled)
            Dictionary<string, List<VertexCoordinate>>? poleVertices = null;
            List<VertexCoordinate>? servitudeVertices = null;

            if (_chkCoordinates.IsChecked == true)
            {
                AppendLog("── Стъпка 3b: Извличане на координати ──");
                poleVertices = new Dictionary<string, List<VertexCoordinate>>();
                foreach (var p in assignedPoles)
                {
                    if (p.FootprintVertices != null && p.FootprintVertices.Count > 0)
                    {
                        poleVertices[p.PoleId] = p.FootprintVertices;
                    }
                }
                servitudeVertices = TopologyProcessor.ExtractPolylineVertices(servitude);
                AppendLog($"  Координати: {poleVertices.Count} стълба, {servitudeVertices.Count} точки сервитут.");
            }

            // Step 5 — Generate Excel
            if (_chkExcel.IsChecked == true)
            {
                AppendLog("── Стъпка 4: Генериране на Excel ──");
                string excelPath = Path.Combine(_projectDir, FileNames.ReportXlsFile);
                var excelGen = new ExcelReportGenerator(_logger!, _projectDir);
                excelGen.GenerateReport(reportRows, assignedPoles, parcelDb, excelPath);
                AppendLog($"  Excel запазен: {excelPath}");
            }

            // Step 6 — Generate Word documents
            if (_chkWord.IsChecked == true)
            {
                AppendLog("── Стъпка 5: Генериране на Word регистри ──");
                var wordGen = new WordReportGenerator(_logger!, _projectDir);
                wordGen.GenerateAllReports(
                    reportRows, assignedPoles, parcelDb, _projectDir,
                    poleVertices: poleVertices,
                    servitudeVertices: servitudeVertices);
                AppendLog("  Word регистри генерирани успешно.");
            }

            int warningCount = reportRows.Count(r => r.Owner == ReportBuilder.NoDataOwner);
            AppendLog(
                $"\n═══ ГЕНЕРИРАНЕ ЗАВЪРШЕНО ═══\n" +
                $"  Обработени имоти: {reportRows.Count}\n" +
                $"  Стълбове причислени: {assignedCount}/{assignedPoles.Count}\n" +
                $"  Липсващи данни: {warningCount} имота\n" +
                $"  Проверете лога за подробности.");
        }

        // ================================================================
        //  MARKER GENERATION EVENT
        // ================================================================
        /// <summary>PLACEHOLDER — not production.</summary>
        private void BtnGenMarkers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryUsePicks(out Document doc)) return;
                if (_servitudeId.IsNull)
                {
                    AppendLog("Моля, първо изберете сервитут (🔲 Сервитут) от бутоните горе!");
                    return;
                }

                if (!int.TryParse(_txtStartNumLeft.Text, out int startL) || !int.TryParse(_txtStartNumRight.Text, out int startR))
                {
                    AppendLog("ГРЕШКА: Въведете валидни числа за начален номер.");
                    return;
                }

                AppendLog("── Генериране на 20m точки по сервитута ──");
                EnsureServices();

                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    Polyline? servitude = OpenPolyline(tr, _servitudeId);
                    if (servitude == null)
                    {
                        AppendLog("ГРЕШКА: Сервитутът вече не съществува в чертежа — изберете отново.");
                        return;
                    }

                    var markerGen = new Geometry.ServitudeMarkerGenerator(_logger!);
                    markerGen.GenerateMarkers(servitude, tr, startL, startR);
                    tr.Commit();
                }
                FlushGraphics(doc);

                AppendLog("  Точките са генерирани в чертежа успешно.");
            }
            catch (Exception ex)
            {
                AppendLog($"\nГРЕШКА при генериране на точки: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ================================================================
        //  SEGMENTATION EVENT
        // ================================================================
        /// <summary>PLACEHOLDER — not production.</summary>
        private void BtnSegment_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryUsePicks(out Document doc)) return;
                if (_servitudeId.IsNull)
                {
                    AppendLog("Моля, първо изберете сервитут (🔲 Сервитут) от бутоните горе!");
                    return;
                }

                if (!double.TryParse(_txtSegmentDistance.Text, out double dist))
                {
                    dist = SegmentDefaults.WindowFallbackDistanceM;
                }

                AppendLog($"── Сегментиране на избрания сервитут (на {dist}м) ──");

                var db = doc.Database;
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline? servitude = OpenPolyline(tr, _servitudeId);
                    if (servitude == null)
                    {
                        AppendLog("ГРЕШКА: Сервитутът вече не съществува в чертежа — изберете отново.");
                        return;
                    }

                    // Sanitize the servitude polyline
                    using (Polyline? cleanServitude = Geometry.GeometrySanitizer.Sanitize(servitude, dist, GeometryTolerances.SanitizeMinVertexDistanceM))
                    {
                        if (cleanServitude != null)
                        {
                            var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                            DrawingWriter.EnsureLayer(db, tr, PluginLayers.SegmentedServitude, 3);

                            // Clone it because we are inside a using block
                            Polyline newPline = (Polyline)cleanServitude.Clone();

                            newPline.ConstantWidth = GeometryTolerances.SegmentedServitudeWidth; // Make it thicker to see it!

                            // Green (color 3) to distinguish it from the original
                            DrawingWriter.Append(btr, tr, newPline, PluginLayers.SegmentedServitude, 3);
                        }
                    }

                    tr.Commit();
                }
                FlushGraphics(doc);

                AppendLog("  Сегментираната линия е добавена в чертежа (Слой: segmented SERV).");
            }
            catch (Exception ex)
            {
                AppendLog($"\nГРЕШКА при сегментиране: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        private void AppendLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.Text += $"\n[{timestamp}] {message}";
            _txtLog.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            Application.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            base.OnClosed(e);
        }
    }
}
