using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using PUP_AUTO.CadRegister;
using PUP_AUTO.Core;
using PUP_AUTO.DataBridge;
using PUP_AUTO.Geometry;
using PUP_AUTO.Semantics;
using static PUP_AUTO.UI.Windows.Theme;
using Application = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PUP_AUTO.UI.Windows
{
    /// <summary>
    /// PUP_AUTO main WPF window built entirely in code (no XAML required).
    /// Three steps: load the .cad register, pick the geometries from the drawing, tick the reports and generate.
    /// Each report shows which inputs it needs and turns them green as they are loaded or picked.
    /// </summary>
    public class MainWindow : Window
    {
        /// <summary>An input a report needs.</summary>
        private enum Requirement { Cad, Servitude, Poles, Parcels }

        private static readonly Requirement[] AllRequirements =
            { Requirement.Cad, Requirement.Servitude, Requirement.Poles, Requirement.Parcels };

        /// <summary>One report row: its checkbox, its card and one chip per required input.</summary>
        private sealed class ReportOption
        {
            public string Title = string.Empty;
            public Requirement[] Needs = Array.Empty<Requirement>();
            public CheckBox Check = null!;
            public Border Card = null!;
            public readonly Dictionary<Requirement, (Border Chip, TextBlock Label)> Chips =
                new Dictionary<Requirement, (Border, TextBlock)>();
            public bool IsChecked => Check.IsChecked == true;
        }

        /// <summary>A pick tile: the button, its icon and its status line.</summary>
        private sealed class PickTile
        {
            public Button Button = null!;
            public TextBlock Icon = null!;
            public TextBlock Status = null!;
        }

        // ---- UI Controls ----
        private TextBlock _lblCadRegister = null!;
        private TextBlock _lblCadDetails = null!;
        private PickTile _tileServitude = null!;
        private PickTile _tilePoles = null!;
        private PickTile _tileParcels = null!;
        private ReportOption _optMvpMathTest = null!;
        private ReportOption _optPoleSteps = null!;
        private ReportOption _optCadControl = null!;
        private ReportOption _optAffectedRegister = null!;
        private ReportOption _optPoleStepsRegister = null!;
        private ReportOption _optTerritoryBalance = null!;
        private readonly List<ReportOption> _reports = new List<ReportOption>();
        private TextBox _txtRegisterProject = null!;
        private TextBox _txtLog = null!;
        private Button _btnGenerate = null!;
        private Button _btnOpenFolder = null!;

        // ---- State ----
        private string _projectDir = string.Empty;

        // The loaded AGKK .cad files (one землище each). Not bound to a drawing, so they survive switching drawings.
        private CadRegisterSet? _cadSet;

        // Picks are data (ObjectIds) bound to the drawing they were made in; see "PICK STATE".
        private Document? _pickDoc;
        private ObjectId _servitudeId = ObjectId.Null;
        private List<ParcelPick> _parcelPicks = new List<ParcelPick>();
        private List<PolePick> _polePicks = new List<PolePick>();

        private SelectionService? _selection;
        private Logger? _logger;

        public MainWindow()
        {
            string? themeError = null;
            try
            {
                Resources = LoadStyles();
            }
            catch (Exception ex)
            {
                // The window still works with the default WPF look
                themeError = ex.Message;
            }

            BuildUI();
            ResolveDefaults();
            UpdateReadiness();
            Application.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;

            if (themeError != null) AppendLog($"ПРЕДУПРЕЖДЕНИЕ: Темата на прозореца не се зареди ({themeError}).");
        }

        // ================================================================
        //  UI CONSTRUCTION
        // ================================================================

        private void BuildUI()
        {
            Title = "ПУП Автоматизация — PUP_AUTO";
            Width = 760;
            Height = 860;
            MinWidth = 600;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = BgBrush;
            Foreground = TextBrush;
            FontFamily = UiFont;
            FontSize = 13;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 0: Header
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });  // 1: Steps + log (scrolls)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                       // 2: Footer

            // ── Header ──
            var header = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
            header.Children.Add(new TextBlock { Text = "ПУП Автоматизация", FontSize = 22, FontWeight = FontWeights.SemiBold });
            header.Children.Add(new TextBlock
            {
                Text = "Справки и регистри за имотите, засегнати от сервитута и стълбовете",
                Foreground = SubtextBrush, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            // ── Steps ──
            var steps = new StackPanel { Margin = new Thickness(24, 0, 24, 0) };
            steps.Children.Add(BuildCadCard());
            steps.Children.Add(BuildPickCard());
            steps.Children.Add(BuildReportsCard());
            steps.Children.Add(BuildLogCard());

            var scroll = new ScrollViewer
            {
                Content = steps,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            // ── Footer ──
            var footer = new Border
            {
                Background = CardBrush, BorderBrush = CardBorderBrush, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 14, 24, 14)
            };
            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _btnOpenFolder = new Button
            {
                Content = IconText(GlyphFolder, "Отвори папката"),
                Margin = new Thickness(0, 0, 12, 0), Visibility = System.Windows.Visibility.Collapsed,
                ToolTip = "Отваря папката на чертежа, където се записват справките"
            };
            _btnOpenFolder.Click += BtnOpenFolder_Click;
            Grid.SetColumn(_btnOpenFolder, 0);
            footerGrid.Children.Add(_btnOpenFolder);

            _btnGenerate = new Button { Style = KeyedStyle(PrimaryButton) };
            _btnGenerate.Click += BtnGenerate_Click;
            Grid.SetColumn(_btnGenerate, 1);
            footerGrid.Children.Add(_btnGenerate);

            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;
        }

        /// <summary>Step 1: the AGKK .cad files (one землище each): several files, or a whole folder with its subfolders.</summary>
        private UIElement BuildCadCard()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var status = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _lblCadRegister = new TextBlock
            {
                Text = "Не е зареден .cad файл", FontWeight = FontWeights.SemiBold, Foreground = WarningBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _lblCadDetails = new TextBlock
            {
                Text = "Файловете на землищата от АГКК. Нужни са за контролната справка и регистрите.",
                FontSize = 12, Foreground = SubtextBrush, Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            status.Children.Add(_lblCadRegister);
            status.Children.Add(_lblCadDetails);
            grid.Children.Add(status);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
            };
            var btnLoadFiles = new Button { Content = IconText(GlyphOpenFile, "Зареди .cad") };
            btnLoadFiles.Click += BtnLoadCadFiles_Click;
            buttons.Children.Add(btnLoadFiles);
            var btnLoadFolder = new Button { Content = IconText(GlyphOpenFile, "Зареди папка"), Margin = new Thickness(8, 0, 0, 0) };
            btnLoadFolder.Click += BtnLoadCadFolder_Click;
            buttons.Children.Add(btnLoadFolder);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);

            return MakeCard(1, "Кадастрален регистър", null, grid);
        }

        /// <summary>Step 2: the servitude, pole and parcel picks.</summary>
        private UIElement BuildPickCard()
        {
            var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };

            _tileServitude = MakePickTile(GlyphLayers, "Сервитут", "Не е избран");
            _tileServitude.Button.Click += BtnPickServitude_Click;
            tiles.Children.Add(_tileServitude.Button);

            _tilePoles = MakePickTile(GlyphPin, "Стълбове", "Не са избрани");
            _tilePoles.Button.Click += BtnPickPoles_Click;
            tiles.Children.Add(_tilePoles.Button);

            _tileParcels = MakePickTile(GlyphParcels, "Имоти", "Не са избрани");
            _tileParcels.Button.Click += BtnPickParcels_Click;
            tiles.Children.Add(_tileParcels.Button);

            return MakeCard(2, "Геометрии от чертежа",
                "Натиснете и изберете обектите в чертежа. Изборът важи за текущия чертеж.", tiles);
        }

        /// <summary>Step 3: the reports, each with the inputs it needs.</summary>
        private UIElement BuildReportsCard()
        {
            var list = new StackPanel();

            _optMvpMathTest = AddReport(list, "MVP математически тест",
                "Площи в сервитута и под стълбовете по имоти, с проверка на баланса.",
                Requirement.Servitude, Requirement.Poles, Requirement.Parcels);

            _optPoleSteps = AddReport(list, "Таблица стъпки на стълбове",
                "Площта на всяка стъпка, разделена по имоти.",
                Requirement.Poles, Requirement.Parcels);

            _optCadControl = AddReport(list, "Контролна справка от .cad",
                "Избраните имоти срещу данните в .cad, по един ред на право.",
                Requirement.Cad, Requirement.Parcels);

            _optAffectedRegister = AddReport(list, "Регистър на засегнатите имоти",
                "Собственици и засегнати площи по имоти.",
                AllRequirements);

            _optPoleStepsRegister = AddReport(list, "Регистър на стъпките на стълбовете",
                "Стъпките по стълбове и имоти, със собствениците от .cad.",
                Requirement.Cad, Requirement.Poles, Requirement.Parcels);

            _optTerritoryBalance = AddReport(list, "Баланси на територията",
                "Балансите по землища и общият баланс за общината (категория, собственост, територия, НТП).",
                AllRequirements);

            // The object name is used by both registers: one text box above the list
            var projectRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            projectRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            projectRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            projectRow.Children.Add(new TextBlock
            {
                Text = "Обект", Foreground = SubtextBrush, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            _txtRegisterProject = new TextBox
            {
                Text = DefaultRegisterProject, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left,
                MinWidth = 220, ToolTip = "Текстът след \"РЕГИСТЪР НА ... ОТ\" в регистрите и след \"БАЛАНСИ НА ... ЗА\" в балансите"
            };
            Grid.SetColumn(_txtRegisterProject, 1);
            projectRow.Children.Add(_txtRegisterProject);

            var content = new StackPanel();
            content.Children.Add(projectRow);
            content.Children.Add(list);

            return MakeCard(3, "Справки", "Отметнете кои справки да се генерират. Файловете се записват в папката на чертежа.", content);
        }

        private UIElement BuildLogCard()
        {
            var clear = new Button { Content = "Изчисти", Style = KeyedStyle(GhostButton) };
            clear.Click += (s, e) => _txtLog.Clear();

            _txtLog = new TextBox
            {
                Height = 190,
                Foreground = SubtextBrush,
                IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = MonoFont, FontSize = 12,
                Text = "Готов за работа. Заредете .cad, изберете геометриите и отметнете справките."
            };

            return MakeCard(null, "Дневник", null, _txtLog, clear);
        }

        // ---- UI Factory Helpers ----

        private Style? KeyedStyle(string key) => TryFindResource(key) as Style;

        /// <summary>A rounded card with an optional step number, a title, an optional hint and an optional header action.</summary>
        private static Border MakeCard(int? step, string title, string? hint, UIElement content, UIElement? headerAction = null)
        {
            var body = new StackPanel();

            var head = new Grid { Margin = new Thickness(0, 0, 0, hint == null ? 12 : 4) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (step != null)
            {
                var badge = new Border
                {
                    Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = AccentSoftBrush,
                    Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = step.Value.ToString(), Foreground = AccentBrush, FontWeight = FontWeights.SemiBold, FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    }
                };
                head.Children.Add(badge);
            }

            var titleText = new TextBlock
            {
                Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleText, 1);
            head.Children.Add(titleText);

            if (headerAction != null)
            {
                Grid.SetColumn(headerAction, 2);
                head.Children.Add(headerAction);
            }
            body.Children.Add(head);

            if (hint != null)
            {
                body.Children.Add(new TextBlock
                {
                    Text = hint, FontSize = 12, Foreground = SubtextBrush, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(step != null ? 34 : 0, 0, 0, 12)
                });
            }

            body.Children.Add(content);

            return new Border
            {
                Background = CardBrush, BorderBrush = CardBorderBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 16, 18, 18),
                Margin = new Thickness(0, 0, 0, 14), Child = body
            };
        }

        /// <summary>An icon glyph followed by a label, for button content.</summary>
        private static StackPanel IconText(string glyph, string text)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return panel;
        }

        private PickTile MakePickTile(string glyph, string title, string status)
        {
            var tile = new PickTile
            {
                Icon = new TextBlock
                {
                    Text = glyph, FontFamily = IconFont, FontSize = 20, Foreground = AccentBrush,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
                },
                Status = new TextBlock
                {
                    Text = status, FontSize = 12, Foreground = SubtextBrush, Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            text.Children.Add(tile.Status);

            var content = new DockPanel();
            DockPanel.SetDock(tile.Icon, Dock.Left);
            content.Children.Add(tile.Icon);
            content.Children.Add(text);

            tile.Button = new Button
            {
                Content = content, Style = KeyedStyle(TileButton), Margin = new Thickness(5, 0, 5, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = $"Изберете {title.ToLowerInvariant()} в чертежа"
            };
            return tile;
        }

        private static void SetTile(PickTile tile, string status, SolidColorBrush statusBrush, bool done)
        {
            tile.Status.Text = status;
            tile.Status.Foreground = statusBrush;
            tile.Icon.Foreground = done ? SuccessBrush : AccentBrush;
            tile.Button.BorderBrush = done ? SuccessBrush : SurfaceBorderBrush;
        }

        private ReportOption AddReport(Panel list, string title, string description, params Requirement[] needs)
        {
            var option = new ReportOption { Title = title, Needs = needs };

            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = description, FontSize = 12, Foreground = SubtextBrush, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 8)
            });

            var chips = new WrapPanel();
            foreach (Requirement need in needs)
            {
                var label = new TextBlock { FontSize = 11, Text = RequirementName(need) };
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 3),
                    Margin = new Thickness(0, 0, 6, 0), Child = label
                };
                option.Chips[need] = (chip, label);
                chips.Children.Add(chip);
            }
            text.Children.Add(chips);

            option.Check = new CheckBox { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
            option.Check.Checked += (s, e) => UpdateReadiness();
            option.Check.Unchecked += (s, e) => UpdateReadiness();

            var cardBody = new StackPanel();
            cardBody.Children.Add(option.Check);
            option.Card = new Border
            {
                Background = SurfaceBrush, BorderBrush = SurfaceBorderBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 8), Child = cardBody
            };

            list.Children.Add(option.Card);
            _reports.Add(option);
            return option;
        }

        private static string RequirementName(Requirement need)
        {
            switch (need)
            {
                case Requirement.Cad:       return ".cad";
                case Requirement.Servitude: return "Сервитут";
                case Requirement.Poles:     return "Стълбове";
                default:                    return "Имоти";
            }
        }

        // ================================================================
        //  READINESS
        //  Each report's chips turn green when their input is loaded or picked;
        //  the generate button counts the ticked reports.
        // ================================================================

        private bool Has(Requirement need)
        {
            switch (need)
            {
                case Requirement.Cad:       return _cadSet != null && _cadSet.Count > 0;
                case Requirement.Servitude: return !_servitudeId.IsNull;
                case Requirement.Poles:     return _polePicks.Count > 0;
                default:                    return _parcelPicks.Count > 0;
            }
        }

        private List<Requirement> Missing(ReportOption option) => option.Needs.Where(n => !Has(n)).ToList();

        private void UpdateReadiness()
        {
            if (_btnGenerate == null) return;

            foreach (ReportOption option in _reports)
            {
                foreach (var pair in option.Chips)
                {
                    bool ready = Has(pair.Key);
                    pair.Value.Chip.Background = ready ? SuccessSoftBrush : CardBrush;
                    pair.Value.Label.Foreground = ready ? SuccessBrush : SubtextBrush;
                    pair.Value.Label.Text = (ready ? "✓ " : "") + RequirementName(pair.Key);
                }
                option.Card.BorderBrush = option.IsChecked ? AccentBrush : SurfaceBorderBrush;
            }

            int count = _reports.Count(r => r.IsChecked);
            _btnGenerate.IsEnabled = count > 0;
            _btnGenerate.Content = IconText(GlyphPlay,
                count == 0 ? "Отметнете справка" : count == 1 ? "Генерирай 1 справка" : $"Генерирай {count} справки");
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
                        _projectDir = dir!;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Optional overrides of the built-in nomenclatures and EKATTE register: a _Templates folder next to the drawing.
        /// </summary>
        private string TemplateDir => Path.Combine(_projectDir, FileNames.TemplatesFolder);

        // ================================================================
        //  .CAD REGISTER
        // ================================================================

        private const int MaxWindowWarnings = 20;

        /// <summary>Loads one or more AGKK .cad files (one землище each); the new set REPLACES the current one.</summary>
        private void BtnLoadCadFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Изберете .cad файлове на землищата (АГКК)",
                Filter = "AGKK .cad (*.cad)|*.cad|All Files (*.*)|*.*",
                InitialDirectory = _projectDir,
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;

            LoadCadPaths(dlg.FileNames);
        }

        /// <summary>Loads every .cad of a folder and its subfolders (a whole "област/община" tree at once).</summary>
        private void BtnLoadCadFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Изберете папка с .cad файлове (включително подпапките)",
                ShowNewFolderButton = false,
                SelectedPath = Directory.Exists(_projectDir) ? _projectDir : string.Empty
            })
            {
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                try
                {
                    List<string> files = CadRegisterSet.FindCadFiles(dlg.SelectedPath);
                    if (files.Count == 0)
                    {
                        AppendLog("ПРЕДУПРЕЖДЕНИЕ: В избраната папка няма .cad файлове.");
                        return;
                    }
                    LoadCadPaths(files);
                }
                catch (Exception ex)
                {
                    AppendLog($"ГРЕШКА при четене на папката: {ex.Message}");
                    _logger?.LogError($"Reading the .cad folder failed: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        /// <summary>
        /// Reads the files into a new <see cref="CadRegisterSet"/>. Warnings go to the log file in full and to the window in part.
        /// Counts only in the log: no names and no ЕГН/БУЛСТАТ. If nothing could be loaded, the current set is kept.
        /// </summary>
        private void LoadCadPaths(IReadOnlyCollection<string> paths)
        {
            try
            {
                EnsureServices();

                var warnings = new List<string>();
                var infos = new List<string>();
                CadRegisterSet set = CadRegisterSet.Load(paths, warnings.Add, infos.Add);

                foreach (string line in infos)
                {
                    _logger!.LogSuccess(line);
                    AppendLog(line);
                }

                foreach (string warning in warnings) _logger!.LogWarning(warning);
                foreach (string warning in warnings.Take(MaxWindowWarnings)) AppendLog($"ПРЕДУПРЕЖДЕНИЕ: {warning}");
                if (warnings.Count > MaxWindowWarnings)
                {
                    AppendLog($"… още {warnings.Count - MaxWindowWarnings} предупреждения — виж {FileNames.LogFile}.");
                }

                if (set.Count == 0)
                {
                    AppendLog("ПРЕДУПРЕЖДЕНИЕ: Няма заредено нито едно землище — предишните .cad файлове (ако има) са запазени.");
                    UpdateReadiness();
                    return;
                }

                _cadSet = set;
                AppendLog($".cad заредено: {set.Count} землища от {paths.Count} файла.");
                foreach (KeyValuePair<string, CadRegisterData> pair in set.ByEkatte.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    CadRegisterData register = pair.Value;
                    AppendLog($"  ЕКАТТЕ {register.Ekatte}, {register.SettlementName} — " +
                              $"{register.Parcels.Count} имота, {register.Rights.Values.Sum(r => r.Count)} права, {register.PersonCount} лица.");
                }
                UpdateCadStatus();
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при зареждане на .cad: {ex.Message}");
                _logger?.LogError($"Loading .cad failed: {ex.Message}\n{ex.StackTrace}");
            }
            UpdateReadiness();
        }

        /// <summary>
        /// The status of card 1. One землище: "ЕКАТТЕ 06433 · с.БРЕСТЕ". Several: "5 землища · общ. Червен бряг" or
        /// "7 землища · 2 общини", the parcel count below and one line per землище in the tooltip.
        /// </summary>
        private void UpdateCadStatus()
        {
            if (_cadSet == null || _cadSet.Count == 0) return;

            EkatteRegister ekatte = EkatteRegister.LoadWithDefaults(Path.Combine(TemplateDir, FileNames.EkatteRegisterFile));
            var ordered = _cadSet.ByEkatte.OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
            int parcels = ordered.Sum(p => p.Value.Parcels.Count);

            _lblCadRegister.Foreground = SuccessBrush;
            if (ordered.Count == 1)
            {
                CadRegisterData only = ordered[0].Value;
                IReadOnlyList<string> paths = _cadSet.SourceFilesOf(ordered[0].Key);
                _lblCadRegister.Text = $"ЕКАТТЕ {only.Ekatte} · {only.SettlementName}";
                _lblCadDetails.Text = $"{only.Parcels.Count} имота · " +
                                      (paths.Count == 1 ? Path.GetFileName(paths[0]) : $"{paths.Count} файла");
                _lblCadDetails.ToolTip = string.Join("\n", paths);
                return;
            }

            var municipalities = new List<string>();
            var tooltip = new List<string>();
            foreach (KeyValuePair<string, CadRegisterData> pair in ordered)
            {
                string name = pair.Value.SettlementName;
                string municipality = MunicipalityGrouping.UnknownMunicipality;
                if (ekatte.TryGet(pair.Key, out EkatteEntry entry))
                {
                    name = $"{entry.Kind} {entry.Name}".Trim();
                    municipality = entry.Municipality;
                }
                if (!municipalities.Contains(municipality)) municipalities.Add(municipality);
                tooltip.Add($"{pair.Key} {name} — {string.Join(", ", _cadSet.SourceFilesOf(pair.Key).Select(Path.GetFileName))}");
            }

            _lblCadRegister.Text = municipalities.Count == 1
                ? $"{ordered.Count} землища · " + (municipalities[0] == MunicipalityGrouping.UnknownMunicipality
                    ? municipalities[0] : MunicipalityGrouping.SheetPrefix + municipalities[0])
                : $"{ordered.Count} землища · {municipalities.Count} общини";
            _lblCadDetails.Text = $"{parcels} имота";
            _lblCadDetails.ToolTip = string.Join("\n", tooltip);
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

            SetTile(_tileServitude, "Не е избран", SubtextBrush, false);
            SetTile(_tilePoles, "Не са избрани", SubtextBrush, false);
            SetTile(_tileParcels, "Не са избрани", SubtextBrush, false);
            UpdateReadiness();
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
            using (PerfTimer.Measure(_logger, "OpenParcels"))
            {
                return OpenParcelsCore(tr);
            }
        }

        private List<KeyValuePair<string, Polyline>> OpenParcelsCore(Transaction tr)
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
            using (PerfTimer.Measure(_logger, "ExtractPoles"))
            {
                return ExtractPolesCore(tr);
            }
        }

        private List<PoleFootprintEntry> ExtractPolesCore(Transaction tr)
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
                        SetTile(_tileServitude, "✓ Избран", SuccessBrush, true);
                        AppendLog("Сервитут избран успешно.");
                    }
                    else
                    {
                        SetTile(_tileServitude, "Не е избран", ErrorBrush, false);
                        AppendLog("ПРЕДУПРЕЖДЕНИЕ: Сервитутът не беше избран.");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"ГРЕШКА: {ex.Message}");
                }
            }
            UpdateReadiness();
        }

        private void BtnPickPoles_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginPick(out Document doc)) return;
            Editor ed = doc.Editor;
            var pickWatch = Stopwatch.StartNew();

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
                        var seenFootprints = new List<(string PoleId, double Area, double[] Box, string Handle)>();
                        var duplicatePoles = new List<DuplicateParcel>();
                        PerfTimer extractTimer = PerfTimer.Measure(_logger, "BtnPickPoles extract + duplicate check");
                        try
                        {
                            int index = 0;
                            foreach (var entry in PoleFootprintExtractor.ExtractAll(poleBlocks, tr))
                            {
                                entries.Add(entry);
                                Polyline? footprint = entry.Result.FootprintPolyline;
                                if (footprint != null)
                                {
                                    // The same pole drawn twice (same number, same footprint) is taken once
                                    Extents3d ext = footprint.GeometricExtents;
                                    double[] box = { ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y };
                                    string handle = poleBlocks[index].Value.Handle.ToString();
                                    var twin = seenFootprints.FirstOrDefault(s => s.PoleId == entry.PoleId &&
                                        DuplicatePolylines.SameFootprint(s.Area, s.Box, footprint.Area, box));
                                    if (twin.PoleId != null)
                                    {
                                        DuplicateParcel? known = duplicatePoles.FirstOrDefault(d => d.ParcelId == entry.PoleId);
                                        if (known == null)
                                        {
                                            known = new DuplicateParcel { ParcelId = entry.PoleId, KeptHandle = twin.Handle };
                                            duplicatePoles.Add(known);
                                        }
                                        known.DroppedHandles.Add(handle);
                                        index++;
                                        continue;
                                    }
                                    seenFootprints.Add((entry.PoleId, footprint.Area, box, handle));

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
                            extractTimer.Dispose();
                            DisposeFootprints(entries);
                        }
                        if (duplicatePoles.Count > 0)
                        {
                            string text = DuplicatePolylines.FormatWarning(duplicatePoles).Replace("имота", "стълба");
                            _logger!.LogWarning(text);
                            AppendLog("ПРЕДУПРЕЖДЕНИЕ: " + DuplicatePolylines.FormatWarning(duplicatePoles, 20).Replace("имота", "стълба"));
                        }
                        tr.Commit();
                    }
                    _polePicks = picks;

                    if (_polePicks.Count > 0)
                    {
                        SetTile(_tilePoles, $"✓ {_polePicks.Count} стълба", SuccessBrush, true);
                        AppendLog($"Избрани и екстрактнати {_polePicks.Count} стълба.");
                    }
                    else
                    {
                        SetTile(_tilePoles, "Не са избрани", ErrorBrush, false);
                        AppendLog("ПРЕДУПРЕЖДЕНИЕ: Не бяха извлечени валидни стълбове.");
                    }
                    _logger?.LogPerf($"BtnPickPoles_Click: {pickWatch.ElapsedMilliseconds} ms (includes the time spent selecting)");
                }
                catch (Exception ex)
                {
                    AppendLog($"ГРЕШКА: {ex.Message}");
                }
            }
            UpdateReadiness();
        }

        private void BtnPickParcels_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginPick(out Document doc)) return;
            Editor ed = doc.Editor;
            var pickWatch = Stopwatch.StartNew();

            using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
            {
                try
                {
                    EnsureServices();

                    var selStats = new PolylineSelectionStats();
                    using (doc.LockDocument())
                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var selected = _selection!.SelectMultiplePolylines(
                            tr, "\nSelect Parcel polylines: ", null,
                            _servitudeId, selStats);

                        // A parcel drawn twice (identical copies) is taken once; separate parts of one parcel stay
                        var topo = new TopologyProcessor(_logger!);
                        var shapes = selected
                            .Select(kvp => new ParcelShape<KeyValuePair<string, Polyline>>
                            {
                                ParcelId = kvp.Key,
                                Handle = kvp.Value.Handle.ToString(),
                                AreaSqm = kvp.Value.Area,
                                Item = kvp
                            })
                            .ToList();
                        DuplicateFilterResult<KeyValuePair<string, Polyline>> unique;
                        int overlapCalls = 0;
                        using (PerfTimer.Measure(_logger, $"BtnPickParcels duplicate check ({shapes.Count} polylines)"))
                        {
                            unique = DuplicatePolylines.Filter(shapes, (x, y) =>
                            {
                                overlapCalls++;
                                return topo.IntersectionAreaSqm(x.Item.Value, y.Item.Value);
                            });
                        }
                        _logger!.LogPerf($"BtnPickParcels duplicate check: {overlapCalls} overlap booleans");
                        if (unique.Duplicates.Count > 0)
                        {
                            _logger!.LogWarning(DuplicatePolylines.FormatWarning(unique.Duplicates));
                            AppendLog("ПРЕДУПРЕЖДЕНИЕ: " + DuplicatePolylines.FormatWarning(unique.Duplicates, 20));
                        }
                        foreach (string id in unique.OverlappingParts) LogWarning(DuplicatePolylines.FormatOverlapWarning(id));

                        // Keep the ID resolved now (XData / handle); it is never re-read
                        _parcelPicks = unique.Kept
                            .Select(s => new ParcelPick { Id = s.Item.Value.ObjectId, ParcelId = s.ParcelId })
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
                        SetTile(_tileParcels, $"✓ {_parcelPicks.Count} имота", SuccessBrush, true);
                        AppendLog($"Избрани {_parcelPicks.Count} имота.");
                    }
                    else
                    {
                        SetTile(_tileParcels, "Не са избрани", ErrorBrush, false);
                        AppendLog("ПРЕДУПРЕЖДЕНИЕ: Имотите не бяха избрани.");
                    }
                    _logger?.LogPerf($"BtnPickParcels_Click: {pickWatch.ElapsedMilliseconds} ms (includes the time spent selecting)");
                }
                catch (Exception ex)
                {
                    AppendLog($"ГРЕШКА: {ex.Message}");
                }
            }
            UpdateReadiness();
        }

        // ================================================================
        //  GENERATE REPORTS
        // ================================================================

        /// <summary>
        /// Runs every ticked report. A report whose inputs are missing is skipped with an error in the log;
        /// the others still run.
        /// </summary>
        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryUsePicks(out Document doc)) return;

                List<ReportOption> ticked = _reports.Where(r => r.IsChecked).ToList();
                if (ticked.Count == 0)
                {
                    AppendLog("Отметнете поне една справка.");
                    return;
                }

                EnsureServices();
                PerfTimer.LogMemory(_logger, "at start of Generate");
                bool ranAny = false;
                using (PerfTimer.Measure(_logger, "BtnGenerate_Click"))
                foreach (ReportOption option in ticked)
                {
                    List<Requirement> missing = Missing(option);
                    if (missing.Count > 0)
                    {
                        AppendLog($"ГРЕШКА: {option.Title} — липсва: {string.Join(", ", missing.Select(RequirementName))}. Справката е пропусната.");
                        continue;
                    }

                    using (PerfTimer timer = PerfTimer.Measure(_logger, "Report: " + option.Title))
                    {
                        if (option == _optMvpMathTest) RunMvpMathTest(doc);
                        else if (option == _optPoleSteps) RunPoleStepsTable(doc);
                        else if (option == _optCadControl) RunCadControlReport(doc, _cadSet!);
                        else if (option == _optAffectedRegister) RunAffectedParcelsRegister(doc, _cadSet!);
                        else if (option == _optPoleStepsRegister) RunPoleStepsRegister(doc, _cadSet!);
                        else if (option == _optTerritoryBalance) RunTerritoryBalance(doc, _cadSet!);
                        AppendLog($"  Време: {timer.ElapsedText}");
                    }
                    ranAny = true;
                }
                PerfTimer.LogMemory(_logger, "at end of Generate");

                if (ranAny && Directory.Exists(_projectDir))
                {
                    _btnOpenFolder.Visibility = System.Windows.Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"\nГРЕШКА: {ex.Message}\n{ex.StackTrace}");
                _logger?.LogError($"Generate (GUI) failed: {ex.Message}\n{ex.StackTrace}");

                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.Editor.WriteMessage($"\nFatal error in Generate: {ex.Message}\n");
                }
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!Directory.Exists(_projectDir))
                {
                    AppendLog("ГРЕШКА: Папката на чертежа не е намерена.");
                    return;
                }
                Process.Start("explorer.exe", $"\"{_projectDir}\"");
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при отваряне на папката: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds MVP_Math_Test_Parcels.xlsx from the picked servitude, poles and parcels.
        /// Short transaction under a document lock; the in-memory footprints are disposed before the file is written.
        /// </summary>
        private void RunMvpMathTest(Document doc)
        {
            try
            {
                AppendLog("── СТАРТИРАНЕ НА MVP MATH TEST ──");
                EnsureServices();
                var topo = new TopologyProcessor(_logger!);

                List<ParcelData>? results = null;
                using (PerfTimer.Measure(_logger, "RunMvpMathTest transaction (parcels, poles, topology)"))
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
                        results = topo.RunMvpMathTest(servitude, PoleFootprints(entries), parcels);
                        tr.Commit();
                    }
                    finally
                    {
                        DisposeFootprints(entries);
                    }
                }

                using (PerfTimer.Measure(_logger, "MvpMathTestExporter.ExportMathTest"))
                {
                    MvpMathTestExporter.ExportMathTest(results!, _projectDir);
                }
                AppendLog($"  Записан {FileNames.MvpMathTestFile} в {_projectDir}");
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при MVP математическия тест: {ex.Message}");
                _logger?.LogError($"MVP math test failed: {ex.Message}\n{ex.StackTrace}");
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
                using (PerfTimer.Measure(_logger, "RunPoleStepsTable transaction (parcels, poles, topology)"))
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
                using (PerfTimer.Measure(_logger, "PoleStepsExporter.Export"))
                {
                    PoleStepsExporter.Export(table, _projectDir);
                }
                AppendLog($"  Записан {FileNames.PoleStepsFile} в {_projectDir}");
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при таблицата със стъпки: {ex.Message}");
                _logger?.LogError($"Pole steps table failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>The nomenclatures and the EKATTE register the .cad reports share; the warnings are logged by the caller.</summary>
        private void LoadReportReferenceData(out Nomenclatures nomenclatures, out EkatteRegister ekatte, List<string> warnings)
        {
            using var timer = PerfTimer.Measure(_logger, "LoadReportReferenceData");
            string templateDir = TemplateDir;
            nomenclatures = Nomenclatures.Load(Path.Combine(templateDir, FileNames.NomenclaturesFolder), warnings.Add);
            ekatte = EkatteRegister.LoadWithDefaults(Path.Combine(templateDir, FileNames.EkatteRegisterFile), warnings.Add);
        }

        /// <summary>
        /// Groups the picked parcels by municipality (one sheet each) and землище (one section each) and logs what could not be
        /// grouped. Only IDs and counts go to the log.
        /// </summary>
        private MunicipalityGroupingResult GroupParcelsForReport(
            CadRegisterSet cadSet, IEnumerable<string> parcelIds, EkatteRegister ekatte, IReadOnlyDictionary<string, string>? lowestPoleByParcelId)
        {
            MunicipalityGroupingResult grouping;
            using (PerfTimer.Measure(_logger, "MunicipalityGrouping.Group"))
            {
                grouping = MunicipalityGrouping.Group(parcelIds, cadSet, ekatte, lowestPoleByParcelId);
            }

            if (grouping.IgnoredParcelIds.Count > 0)
            {
                LogWarning($"{grouping.IgnoredParcelIds.Count} имота са без ЕКАТТЕ в номера и са пропуснати от справките: " +
                           JoinLimited(grouping.IgnoredParcelIds, 30));
            }
            if (grouping.UnknownEkatte.Count > 0)
            {
                LogWarning($"ЕКАТТЕ {string.Join(", ", grouping.UnknownEkatte)} не е в регистъра на ЕКАТТЕ — " +
                           $"имотите им са в лист \"{MunicipalityGrouping.UnknownMunicipality}\".");
            }
            foreach (SettlementSection section in grouping.SectionsWithoutCad)
            {
                LogWarning($"Няма зареден .cad за ЕКАТТЕ {section.Ekatte} ({section.DisplayName}) — " +
                           $"{section.ParcelIds.Count} имота без данни от кадастъра.");
            }
            return grouping;
        }

        /// <summary>
        /// Builds Контролна_справка_cad.xlsx: the picked parcels against the loaded .cad files, one sheet per municipality and
        /// one section per землище, one row per right. Only the drawn areas are read from the drawing, in a short transaction
        /// under a document lock; everything else works on plain data. Personal data (ЕГН/БУЛСТАТ, names) never goes to the log.
        /// </summary>
        private void RunCadControlReport(Document doc, CadRegisterSet cadSet)
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

                var warnings = new List<string>();
                LoadReportReferenceData(out Nomenclatures nomenclatures, out EkatteRegister ekatte, warnings);
                MunicipalityGroupingResult grouping = GroupParcelsForReport(cadSet, drawn.Select(d => d.Key), ekatte, null);

                var sheets = new List<(string SheetName, IReadOnlyList<CadControlReport> Sections)>();
                using var buildTimer = PerfTimer.Measure(_logger, "RunCadControlReport builder loop");
                var notFound = new List<string>();
                var withoutRights = new List<string>();
                foreach (MunicipalityGroup group in grouping.Groups)
                {
                    var reports = new List<CadControlReport>();
                    int parcelCount = 0, rowCount = 0;
                    foreach (SettlementSection section in group.Sections)
                    {
                        var ids = new HashSet<string>(section.ParcelIds, StringComparer.Ordinal);
                        CadControlReport report = CadControlReportBuilder.Build(
                            drawn.Where(d => ids.Contains(d.Key)), section.Register, nomenclatures, section.EkatteTitle);

                        if (section.HasCad) notFound.AddRange(report.NotFound); // a землище without a .cad is already reported
                        withoutRights.AddRange(report.WithoutRights);
                        if (report.Rows.Count == 0) continue;

                        reports.Add(report);
                        parcelCount += report.Rows.Select(r => r.ParcelId).Distinct().Count();
                        rowCount += report.Rows.Count;
                    }

                    if (reports.Count == 0) continue;
                    sheets.Add((group.SheetName, reports));
                    AppendLog($"  {group.SheetName}: {reports.Count} землища, {parcelCount} имота, {rowCount} реда.");
                }

                // The nomenclature warnings ("no text for code N") are raised while the reports are built
                foreach (string warning in warnings) LogWarning(warning);

                if (notFound.Count > 0)
                {
                    LogWarning($"{notFound.Count} избрани имота не са намерени в .cad: " + JoinLimited(notFound, 30));
                }
                if (withoutRights.Count > 0)
                {
                    LogWarning($"{withoutRights.Count} имота нямат права в .cad (един ред без собственик): " +
                               JoinLimited(withoutRights, 30));
                }

                if (sheets.Count == 0)
                {
                    AppendLog("ГРЕШКА: Нито един избран имот не е намерен в .cad — файлът не е записан.");
                    return;
                }

                string path;
                using (PerfTimer.Measure(_logger, "CadControlReportExporter.Export")) path = CadControlReportExporter.Export(sheets, _projectDir);
                AppendLog($"  Записан {Path.GetFileName(path)} в {_projectDir}.");
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при контролната справка: {ex.Message}");
                _logger?.LogError($"Cad control report failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>The finished registers of affected parcels of a run, grouped like the report sheets, plus what was found on the way.</summary>
        private sealed class AffectedRegisterRun
        {
            /// <summary>Groups (sheets) with their registers (sections); a section without rows is left out.</summary>
            public readonly List<(MunicipalityGroup Group, List<AffectedRegister> Reports)> Groups =
                new List<(MunicipalityGroup, List<AffectedRegister>)>();

            /// <summary>All pole-step pieces of the run (not per section).</summary>
            public List<PoleStepPiece> Pieces = new List<PoleStepPiece>();

            public readonly List<string> NotFound = new List<string>();
            public readonly List<string> WithoutOwners = new List<string>();
            public readonly List<string> NegativeRemainder = new List<string>();

            /// <summary>Nomenclature warnings ("no text for code N"); raised while the registers are built, logged by the caller.</summary>
            public readonly List<string> Warnings = new List<string>();

            public Nomenclatures Nomenclatures = null!;
        }

        /// <summary>
        /// The geometry of the picked servitude, poles and parcels (one short transaction under a document lock; the in-memory
        /// footprints are disposed before returning), grouped by municipality and землище, and one register of affected parcels
        /// per section. Shared by the register and the territory balance, so both are built from the same rows.
        /// Returns null (after logging the reason) when the servitude no longer exists.
        /// </summary>
        private AffectedRegisterRun? BuildAffectedRegisters(Document doc, CadRegisterSet cadSet, string project)
        {
            EnsureServices();
            var topo = new TopologyProcessor(_logger!);

            RegisterGeometry? geometry = null;
            using (PerfTimer.Measure(_logger, "BuildAffectedRegisters transaction (parcels, poles, topology)"))
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Polyline? servitude = OpenPolyline(tr, _servitudeId);
                if (servitude == null)
                {
                    AppendLog("ГРЕШКА: Сервитутът вече не съществува в чертежа — изберете отново.");
                    return null;
                }

                var parcels = OpenParcels(tr);
                var entries = ExtractPoles(tr);
                try
                {
                    geometry = topo.ComputeRegisterGeometry(servitude, PoleFootprints(entries), parcels);
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
                LogWarning(PoleStepsTableBuilder.FormatUncoveredWarning(step));
            }

            var run = new AffectedRegisterRun { Pieces = geometry.Pieces };
            LoadReportReferenceData(out Nomenclatures nomenclatures, out EkatteRegister ekatte, run.Warnings);
            run.Nomenclatures = nomenclatures;
            MunicipalityGroupingResult grouping = GroupParcelsForReport(
                cadSet, geometry.Parcels.Select(p => p.ParcelId), ekatte, MunicipalityGrouping.LowestPoleByParcel(geometry.Pieces));

            using var buildTimer = PerfTimer.Measure(_logger, "BuildAffectedRegisters builder loop");
            foreach (MunicipalityGroup group in grouping.Groups)
            {
                var reports = new List<AffectedRegister>();
                foreach (SettlementSection section in group.Sections)
                {
                    var ids = new HashSet<string>(section.ParcelIds, StringComparer.Ordinal);
                    AffectedRegister report = AffectedParcelsRegisterBuilder.Build(
                        geometry.Parcels.Where(p => ids.Contains(p.ParcelId)),
                        geometry.Pieces.Where(p => ids.Contains(p.ParcelId)),
                        section.Register, nomenclatures, project, section.EkatteTitle);

                    if (section.HasCad) run.NotFound.AddRange(report.NotFound); // a землище without a .cad is already reported
                    run.WithoutOwners.AddRange(report.WithoutOwners);
                    run.NegativeRemainder.AddRange(report.NegativeRemainder);
                    if (report.Rows.Count == 0) continue;

                    reports.Add(report);
                }

                if (reports.Count > 0) run.Groups.Add((group, reports));
            }
            return run;
        }

        /// <summary>
        /// Builds Регистър_на_засегнатите_имоти.xlsx from the picked servitude, poles and parcels and the loaded .cad files, one
        /// sheet per municipality and one section per землище (see <see cref="BuildAffectedRegisters"/>).
        /// Personal data (ЕГН/БУЛСТАТ, names) never goes to the log.
        /// </summary>
        private void RunAffectedParcelsRegister(Document doc, CadRegisterSet cadSet)
        {
            try
            {
                AppendLog("── СТАРТИРАНЕ НА РЕГИСТЪР НА ЗАСЕГНАТИТЕ ИМОТИ ──");
                string project = string.IsNullOrWhiteSpace(_txtRegisterProject.Text) ? DefaultRegisterProject : _txtRegisterProject.Text;
                AffectedRegisterRun? run = BuildAffectedRegisters(doc, cadSet, project);
                if (run == null) return;

                var sheets = new List<(string SheetName, IReadOnlyList<AffectedRegister> Sections)>();
                foreach ((MunicipalityGroup group, List<AffectedRegister> reports) in run.Groups)
                {
                    int parcelCount = reports.Sum(r => r.Rows.Count(x => x.IsFirstOfParcel));
                    int rowCount = reports.Sum(r => r.Rows.Count);
                    sheets.Add((group.SheetName, reports));
                    AppendLog($"  {group.SheetName}: {reports.Count} землища, {parcelCount} имота, {rowCount} реда.");
                }

                // The nomenclature warnings ("no text for code N") are raised while the registers are built
                foreach (string warning in run.Warnings) LogWarning(warning);

                if (run.NotFound.Count > 0)
                {
                    LogWarning($"{run.NotFound.Count} избрани имота не са намерени в .cad (ред само с площите от чертежа): " +
                               JoinLimited(run.NotFound, 30));
                }
                if (run.WithoutOwners.Count > 0)
                {
                    LogWarning($"{run.WithoutOwners.Count} имота нямат собственик (право 1) в .cad — ред без собственик: " +
                               JoinLimited(run.WithoutOwners, 30));
                }
                if (run.NegativeRemainder.Count > 0)
                {
                    LogWarning($"Отрицателен остатък при {run.NegativeRemainder.Count} имота: " + JoinLimited(run.NegativeRemainder, 30));
                }

                if (sheets.Count == 0)
                {
                    AppendLog("Няма имоти за регистъра — файлът не е записан.");
                    return;
                }

                string path;
                using (PerfTimer.Measure(_logger, "AffectedParcelsRegisterExporter.Export")) path = AffectedParcelsRegisterExporter.Export(sheets, _projectDir);
                AppendLog($"  Записан {Path.GetFileName(path)} в {_projectDir}.");
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при регистъра на засегнатите имоти: {ex.Message}");
                _logger?.LogError($"Affected parcels register failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Builds Баланси_на_територията.xlsx: the register of affected parcels (same rows, same printed areas) summed by
        /// category, ownership, territory type and НТП, one sheet per municipality and one section per землище. Every pole is
        /// counted once in the whole run, in the землище and parcel with its largest piece. In the same run, Общ_баланс_за_общината.xlsx: the
        /// землища of each municipality combined (<see cref="TerritoryBalanceBuilder.Combine"/>). Only counts go to the log.
        /// </summary>
        private void RunTerritoryBalance(Document doc, CadRegisterSet cadSet)
        {
            try
            {
                AppendLog("── СТАРТИРАНЕ НА БАЛАНСИ НА ТЕРИТОРИЯТА ──");
                string project = string.IsNullOrWhiteSpace(_txtRegisterProject.Text) ? DefaultRegisterProject : _txtRegisterProject.Text;
                AffectedRegisterRun? run = BuildAffectedRegisters(doc, cadSet, project);
                if (run == null) return;

                var sheets = new List<(string SheetName, IReadOnlyList<TerritoryBalance> Sections)>();
                using var buildTimer = PerfTimer.Measure(_logger, "RunTerritoryBalance builder loop");
                var municipalities = new List<(MunicipalityGroup Group, List<TerritoryBalance> Sections)>();
                int notFoundCount = 0;
                // One winning parcel per pole for the whole run, so a border pole is counted in one землище only
                Dictionary<string, string> winners = TerritoryBalanceBuilder.WinningParcels(run.Pieces);
                int countedPoles = 0;
                foreach ((MunicipalityGroup group, List<AffectedRegister> reports) in run.Groups)
                {
                    var balances = new List<TerritoryBalance>();
                    int parcelCount = 0;
                    foreach (AffectedRegister report in reports)
                    {
                        // Step area stays per piece; a pole is counted only in the землище of its largest piece
                        SectionPoles poles = TerritoryBalanceBuilder.PolesOfSection(report, winners);
                        countedPoles += poles.DistinctPoles;
                        TerritoryBalance balance = TerritoryBalanceBuilder.Build(
                            report, poles.ByParcel, project, run.Nomenclatures, poles.DistinctPoles);
                        if (!balance.PoleCountsAgree)
                        {
                            LogWarning($"Баланси, {balance.Subtitle}: \"Стъпки бр.\" не съвпада с броя на стълбовете с най-голямо парче в землището " +
                                       $"({poles.DistinctPoles}) — проверете таблиците.");
                        }
                        balances.Add(balance);
                        parcelCount += balance.Tables[0].Total.ParcelCount;
                        notFoundCount += balance.NotFoundCount;
                    }
                    sheets.Add((group.SheetName, balances));
                    municipalities.Add((group, balances));
                    AppendLog($"  {group.SheetName}: {balances.Count} землища, {parcelCount} имота.");
                }

                if (countedPoles != winners.Count)
                {
                    LogWarning($"Баланси: сборът на \"Стъпки бр.\" по землища ({countedPoles}) не е равен на броя на стълбовете ({winners.Count}).");
                }

                // The nomenclature warnings ("no text for code N") are raised while the balances are built
                foreach (string warning in run.Warnings) LogWarning(warning);
                if (notFoundCount > 0)
                {
                    LogWarning($"{notFoundCount} избрани имота не са намерени в .cad (в групата \"{TerritoryBalanceBuilder.NotInCad}\").");
                }

                if (sheets.Count == 0)
                {
                    AppendLog("Няма имоти за баланса — файлът не е записан.");
                    return;
                }

                string path;
                using (PerfTimer.Measure(_logger, "TerritoryBalanceExporter.Export")) path = TerritoryBalanceExporter.Export(sheets, _projectDir);
                AppendLog($"  Записан {Path.GetFileName(path)} в {_projectDir}.");

                // Общ баланс за общината: the землища of each sheet combined, so it always equals the sum of the sections above
                var totals = new List<(string SheetName, TerritoryBalance Balance)>();
                foreach ((MunicipalityGroup group, List<TerritoryBalance> sections) in municipalities)
                {
                    try
                    {
                        totals.Add((group.SheetName, TerritoryBalanceBuilder.Combine(
                            sections,
                            TerritoryBalanceBuilder.MunicipalityTitle(project),
                            TerritoryBalanceBuilder.MunicipalitySubtitle(group.Municipality, group.Province))));
                    }
                    catch (InvalidOperationException ex)
                    {
                        LogWarning($"{group.SheetName}: общият баланс не е създаден — {ex.Message}");
                    }
                }
                if (totals.Count > 0)
                {
                    string totalsPath;
                using (PerfTimer.Measure(_logger, "TerritoryBalanceExporter.ExportMunicipalities")) totalsPath = TerritoryBalanceExporter.ExportMunicipalities(totals, _projectDir);
                    AppendLog($"  Записан {Path.GetFileName(totalsPath)} в {_projectDir}.");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при балансите: {ex.Message}");
                _logger?.LogError($"Territory balance failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Builds Регистър_на_стъпките_на_стълбовете.xlsx from the picked poles and parcels and the loaded .cad files, one
        /// sheet per municipality and one section per землище. Short transaction under a document lock, nothing is written to
        /// the drawing; the in-memory footprints are disposed before anything is written. A pole with pieces in two землища
        /// stands in both sections. Personal data (ЕГН/БУЛСТАТ, names) never goes to the log.
        /// </summary>
        private void RunPoleStepsRegister(Document doc, CadRegisterSet cadSet)
        {
            try
            {
                AppendLog("── СТАРТИРАНЕ НА РЕГИСТЪР НА СТЪПКИТЕ НА СТЪЛБОВЕТЕ ──");
                EnsureServices();
                var topo = new TopologyProcessor(_logger!);

                PoleStepsGeometry? geometry = null;
                var drawnAreaSqmById = new Dictionary<string, double>(StringComparer.Ordinal);
                using (PerfTimer.Measure(_logger, "RunPoleStepsRegister transaction (parcels, poles, topology)"))
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var parcels = OpenParcels(tr);
                    var entries = ExtractPoles(tr);
                    try
                    {
                        geometry = topo.ComputePoleStepPieces(PoleFootprints(entries), parcels);
                        // One ID drawn as several polylines is one parcel: the areas are summed
                        foreach (var parcel in parcels)
                        {
                            drawnAreaSqmById.TryGetValue(parcel.Key, out double sum);
                            drawnAreaSqmById[parcel.Key] = sum + parcel.Value.Area;
                        }
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
                    LogWarning(PoleStepsTableBuilder.FormatUncoveredWarning(step));
                }

                if (geometry.Pieces.Count == 0)
                {
                    AppendLog("Няма стъпки на стълбове в избраните имоти — регистърът не е създаден.");
                    return;
                }

                var warnings = new List<string>();
                LoadReportReferenceData(out Nomenclatures nomenclatures, out EkatteRegister ekatte, warnings);
                MunicipalityGroupingResult grouping = GroupParcelsForReport(
                    cadSet, drawnAreaSqmById.Keys, ekatte, MunicipalityGrouping.LowestPoleByParcel(geometry.Pieces));

                string project = string.IsNullOrWhiteSpace(_txtRegisterProject.Text) ? DefaultRegisterProject : _txtRegisterProject.Text;
                var sheets = new List<(string SheetName, IReadOnlyList<PoleStepsRegister> Sections)>();
                var notFound = new List<string>();
                var withoutOwners = new List<string>();
                using var buildTimer = PerfTimer.Measure(_logger, "RunPoleStepsRegister builder loop");
                foreach (MunicipalityGroup group in grouping.Groups)
                {
                    var reports = new List<PoleStepsRegister>();
                    int parcelCount = 0, rowCount = 0;
                    foreach (SettlementSection section in group.Sections)
                    {
                        var ids = new HashSet<string>(section.ParcelIds, StringComparer.Ordinal);
                        PoleStepsRegister report = PoleStepsRegisterBuilder.Build(
                            geometry.Pieces.Where(p => ids.Contains(p.ParcelId)),
                            drawnAreaSqmById, section.Register, nomenclatures, project, section.EkatteTitle);

                        if (section.HasCad) notFound.AddRange(report.NotFound); // a землище without a .cad is already reported
                        withoutOwners.AddRange(report.WithoutOwners);
                        if (report.Rows.Count == 0) continue; // no steps in this землище

                        reports.Add(report);
                        parcelCount += report.Rows.Where(r => r.IsFirstOfBlock).Select(r => r.ParcelId).Distinct().Count();
                        rowCount += report.Rows.Count;
                    }

                    if (reports.Count == 0) continue;
                    sheets.Add((group.SheetName, reports));
                    AppendLog($"  {group.SheetName}: {reports.Count} землища, {parcelCount} имота, {rowCount} реда.");
                }

                // The nomenclature warnings ("no text for code N") are raised while the registers are built
                foreach (string warning in warnings) LogWarning(warning);

                if (notFound.Count > 0)
                {
                    LogWarning($"{notFound.Count} избрани имота не са намерени в .cad (ред само с площите от чертежа): " +
                               JoinLimited(notFound, 30));
                }
                if (withoutOwners.Count > 0)
                {
                    LogWarning($"{withoutOwners.Count} имота нямат собственик (право 1) в .cad — ред без собственик: " +
                               JoinLimited(withoutOwners, 30));
                }

                if (sheets.Count == 0)
                {
                    AppendLog("Няма стъпки на стълбове в избраните имоти — регистърът не е създаден.");
                    return;
                }

                string path;
                using (PerfTimer.Measure(_logger, "PoleStepsRegisterExporter.Export")) path = PoleStepsRegisterExporter.Export(sheets, _projectDir);
                AppendLog($"  Записан {Path.GetFileName(path)} в {_projectDir}.");
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при регистъра на стъпките: {ex.Message}");
                _logger?.LogError($"Pole steps register failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private const string DefaultRegisterProject = "НОВА ВЛ 110kV";

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

        // ================================================================
        //  HELPERS
        // ================================================================

        private void AppendLog(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            if (_txtLog.Text.Length > 0) _txtLog.AppendText("\n");
            _txtLog.AppendText($"[{timestamp}] {message}");
            _txtLog.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            Application.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
            base.OnClosed(e);
        }
    }
}
