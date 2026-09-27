using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
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
        private CheckBox _chkMarkers = null!;
        private CheckBox _chkMvpMathTest = null!;
        private TextBox _txtStartNumLeft = null!;
        private TextBox _txtStartNumRight = null!;
        private TextBox _txtSegmentDistance = null!;
        private TextBox _txtLog = null!;

        // ---- State ----
        private string _projectDir = string.Empty;
        private string? _cadFilePath;
        private string? _templateDirPath;

        private Polyline? _servitudePline;
        private List<KeyValuePair<string, Polyline>> _polePolylines = new List<KeyValuePair<string, Polyline>>();
        private List<KeyValuePair<string, Polyline>> _parcelPolylines = new List<KeyValuePair<string, Polyline>>();
        private Dictionary<string, List<VertexCoordinate>> _footprintVerticesDict = new Dictionary<string, List<VertexCoordinate>>();

        private Transaction? _activeTransaction;
        private Core.TransactionManager? _txMgr;
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

                        string cadPath = Path.Combine(dir, "_TestFiles", "TemplateC.cad");
                        if (File.Exists(cadPath))
                        {
                            _cadFilePath = cadPath;
                            _txtCadPath.Text = cadPath;
                        }

                        string tplDir = Path.Combine(dir, "_Templates");
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

        // ================================================================
        //  GEOMETRY PICKING
        // ================================================================

        private void BtnPickServitude_Click(object sender, RoutedEventArgs e)
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
            {
                try
                {
                    EnsureTransaction();
                    _servitudePline = _txMgr!.SelectSinglePolyline(
                        _activeTransaction!, "\nSelect the Servitude (Right of Way) polyline: ");

                    if (_servitudePline != null)
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
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
            {
                try
                {
                    EnsureTransaction();
                    
                    var poleBlocks = _txMgr!.SelectMultipleBlockReferences(
                        _activeTransaction!, "\nSelect Pole blocks: ");
                        
                    _polePolylines.Clear();
                    _footprintVerticesDict.Clear();

                    foreach (var kvp in poleBlocks)
                    {
                        var result = PoleFootprintExtractor.ExtractFootprint(kvp.Value, _activeTransaction!, _logger!);
                        if (result.FootprintPolyline != null)
                        {
                            string poleId = string.IsNullOrEmpty(result.PoleNumber) ? kvp.Key : result.PoleNumber;
                            _polePolylines.Add(new KeyValuePair<string, Polyline>(poleId, result.FootprintPolyline));
                            
                            var pts = new List<VertexCoordinate>();
                            for(int i=0; i<result.FootprintPolyline.NumberOfVertices; i++)
                            {
                                var pt = result.FootprintPolyline.GetPoint3dAt(i);
                                pts.Add(new VertexCoordinate { PointIndex = i+1, PointLabel = $"{poleId}-{i+1}", X = Math.Round(pt.X,3), Y = Math.Round(pt.Y,3) });
                            }
                            _footprintVerticesDict[poleId] = pts;
                        }
                        else
                        {
                            AppendLog($"ПРЕДУПРЕЖДЕНИЕ: {result.ErrorMessage}");
                        }
                    }

                    if (_polePolylines.Count > 0)
                    {
                        _lblPoles.Text = $"✅ {_polePolylines.Count} стълба";
                        _lblPoles.Foreground = GreenBrush;
                        AppendLog($"Избрани и екстрактнати {_polePolylines.Count} стълба.");
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
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
            {
                try
                {
                    EnsureTransaction();

                    // Load GeoJSON geometries for spatial matching (if GeoJSON file is selected)
                    List<GeoParcel>? geoParcels = null;
                    string cadPath = _cadFilePath ?? "";
                    if (cadPath.EndsWith(".geojson", StringComparison.OrdinalIgnoreCase))
                    {
                        var reader = new CadLibraryReader(_logger!);
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

                    _parcelPolylines = _txMgr!.SelectMultiplePolylines(
                        _activeTransaction!, "\nSelect Parcel polylines: ", geoParcels);

                    if (_parcelPolylines.Count > 0)
                    {
                        _lblParcels.Text = $"✅ {_parcelPolylines.Count} имота";
                        _lblParcels.Foreground = GreenBrush;
                        AppendLog($"Избрани {_parcelPolylines.Count} имота.");
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

        private void EnsureTransaction()
        {
            if (_logger == null)
            {
                string logPath = Path.Combine(_projectDir, "PUP_AUTO_Logs.txt");
                _logger = new Logger(logPath);
            }
            if (_txMgr == null)
                _txMgr = new Core.TransactionManager(_logger);
            if (_activeTransaction == null || _activeTransaction.IsDisposed)
                _activeTransaction = _txMgr.StartTransaction();
        }

        // ================================================================
        //  GENERATE REPORTS
        // ================================================================

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_servitudePline == null)  { AppendLog("ГРЕШКА: Не е избран сервитут!"); return; }
                if (_polePolylines.Count == 0) { AppendLog("ГРЕШКА: Не са избрани стълбове!"); return; }
                if (_parcelPolylines.Count == 0) { AppendLog("ГРЕШКА: Не са избрани имоти!"); return; }

                EnsureTransaction();
                var topo = new TopologyProcessor(_logger!);

                if (_chkMvpMathTest.IsChecked == true)
                {
                    AppendLog("── СТАРТИРАНЕ НА MVP MATH TEST ──");
                    var testResults = topo.RunMvpMathTest(_servitudePline, _polePolylines, _parcelPolylines, _activeTransaction!);
                    BasicExcelExporter.ExportMathTest(testResults, _projectDir);
                    AppendLog($"  Записан MVP_Math_Test_Parcels.xlsx в {_projectDir}");
                    return;
                }

                AppendLog("═══ ГЕНЕРИРАНЕ СТАРТИРАНО ═══");

                // Step 1 — Load CAD database
                AppendLog("── Стъпка 1: Зареждане на CAD база ──");
                string cadPath = _cadFilePath ?? Path.Combine(_projectDir, "_TestFiles", "TemplateC.cad");
                var reader = new CadLibraryReader(_logger!);
                var parcelDb = reader.LoadLibrary(cadPath);
                AppendLog($"  Заредени {parcelDb.Count} записа от базата.");

                // Step 2 — Topology calculations
                AppendLog("── Стъпка 2: Топологични изчисления ──");
                var servitudeAreas = topo.CalculateServitudeIntersections(
                    _servitudePline, _parcelPolylines, _activeTransaction!);
                AppendLog($"  Сечения сервитут: {servitudeAreas.Count} имота.");

                var assignedPoles = topo.AssignPolesToParcels(
                    _polePolylines, _parcelPolylines, _activeTransaction!);
                    
                foreach (var p in assignedPoles)
                {
                    if (_footprintVerticesDict.TryGetValue(p.PoleId, out var fv))
                    {
                        p.FootprintVertices = fv;
                    }
                }
                
                int assignedCount = assignedPoles.Count(p => p.OverlappingParcels.Count > 0);
                AppendLog($"  Стълбове: {assignedPoles.Count} обработени ({assignedCount} причислени).");

                // Step 3 — Merge results
                AppendLog("── Стъпка 3: Обединяване на резултати ──");
                var reportRows = MainCommands.MergeResultsStatic(
                    _parcelPolylines, parcelDb, servitudeAreas, assignedPoles, _logger!);
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
                    servitudeVertices = topo.ExtractPolylineVertices(_servitudePline);
                    AppendLog($"  Координати: {poleVertices.Count} стълба, {servitudeVertices.Count} точки сервитут.");
                }

                // Step 5 — Generate Excel
                if (_chkExcel.IsChecked == true)
                {
                    AppendLog("── Стъпка 4: Генериране на Excel ──");
                    string excelPath = Path.Combine(_projectDir, "PUP_Report.xls");
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

                // Step 7 — Commit transaction
                _activeTransaction?.Commit();
                _activeTransaction?.Dispose();
                _activeTransaction = null;

                int warningCount = reportRows.Count(r => r.Owner == "NO DATA");
                AppendLog(
                    $"\n═══ ГЕНЕРИРАНЕ ЗАВЪРШЕНО ═══\n" +
                    $"  Обработени имоти: {reportRows.Count}\n" +
                    $"  Стълбове причислени: {assignedCount}/{assignedPoles.Count}\n" +
                    $"  Липсващи данни: {warningCount} имота\n" +
                    $"  Проверете лога за подробности.");
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

        // ================================================================
        //  MARKER GENERATION EVENT
        // ================================================================
        private void BtnGenMarkers_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_servitudePline == null)
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
                
                using (var tr = _txMgr!.StartTransaction())
                {
                    var markerGen = new Geometry.ServitudeMarkerGenerator(_logger!);
                    markerGen.GenerateMarkers(_servitudePline, tr, startL, startR);
                    tr.Commit();
                }

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
        private void BtnSegment_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_servitudePline == null)
                {
                    AppendLog("Моля, първо изберете сервитут (🔲 Сервитут) от бутоните горе!");
                    return;
                }
                
                if (!double.TryParse(_txtSegmentDistance.Text, out double dist))
                {
                    dist = 50.0;
                }

                AppendLog($"── Сегментиране на избрания сервитут (на {dist}м) ──");
                
                using (var tr = _txMgr!.StartTransaction())
                {
                    var doc = Application.DocumentManager.MdiActiveDocument;
                    var db = doc.Database;
                    
                    // Sanitize the servitude polyline
                    using (Polyline cleanServitude = Geometry.GeometrySanitizer.Sanitize(_servitudePline, dist, 0.05))
                    {
                        if (cleanServitude != null)
                        {
                            var btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                            
                            EnsureLayerExists(db, tr, "segmented SERV", 3);
                            
                            // Clone it because we are inside a using block
                            Polyline newPline = (Polyline)cleanServitude.Clone();
                            
                            // Make it green to distinguish it from the original
                            newPline.Layer = "segmented SERV";
                            newPline.ColorIndex = 3; 
                            newPline.ConstantWidth = 0.5; // Make it thicker to see it!
                            
                            btr.AppendEntity(newPline);
                            tr.AddNewlyCreatedDBObject(newPline, true);
                        }
                    }

                    tr.Commit();
                    
                    // Force AutoCAD to display the new entity immediately,
                    // since the top-level _activeTransaction hasn't committed yet.
                    doc.TransactionManager.QueueForGraphicsFlush();
                    doc.Editor.UpdateScreen();
                }

                // Force a commit to the database so it is saved instantly
                CommitAndRefreshTransaction();

                AppendLog("  Сегментираната линия е добавена в чертежа (Слой: segmented SERV).");
            }
            catch (Exception ex)
            {
                AppendLog($"\nГРЕШКА при сегментиране: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void EnsureLayerExists(Database db, Transaction tr, string layerName, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord();
                ltr.Name = layerName;
                ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        private void CommitAndRefreshTransaction()
        {
            if (_activeTransaction == null || _activeTransaction.IsDisposed) return;
            
            // Save ObjectIds
            var servitudeId = _servitudePline?.ObjectId;
            
            var poleIds = _polePolylines.Select(p => new { Key = p.Key, Id = p.Value.ObjectId }).ToList();
            var parcelIds = _parcelPolylines.Select(p => new { Key = p.Key, Id = p.Value.ObjectId }).ToList();

            // Commit and dispose current
            _activeTransaction.Commit();
            _activeTransaction.Dispose();

            // Start a new one
            _activeTransaction = _txMgr!.StartTransaction();

            // Re-open objects
            if (servitudeId.HasValue && !servitudeId.Value.IsNull)
            {
                _servitudePline = (Polyline)_activeTransaction.GetObject(servitudeId.Value, OpenMode.ForRead);
            }

            _polePolylines.Clear();
            foreach (var p in poleIds)
            {
                if (!p.Id.IsNull)
                    _polePolylines.Add(new KeyValuePair<string, Polyline>(p.Key, (Polyline)_activeTransaction.GetObject(p.Id, OpenMode.ForRead)));
            }

            _parcelPolylines.Clear();
            foreach (var p in parcelIds)
            {
                if (!p.Id.IsNull)
                    _parcelPolylines.Add(new KeyValuePair<string, Polyline>(p.Key, (Polyline)_activeTransaction.GetObject(p.Id, OpenMode.ForRead)));
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
            if (_activeTransaction != null && !_activeTransaction.IsDisposed)
            {
                try { _activeTransaction.Abort(); } catch { }
                _activeTransaction.Dispose();
            }
            base.OnClosed(e);
        }
    }
}
