using System.Diagnostics;
using System.Globalization;
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
        private enum Requirement { Cad, Servitude, Poles, Parcels, Axis }

        private static readonly Requirement[] AllRequirements =
            { Requirement.Cad, Requirement.Servitude, Requirement.Poles, Requirement.Parcels };

        /// <summary>One report row: its checkbox, its card and the readiness label on its title row.</summary>
        private sealed class ReportOption
        {
            public string Title = string.Empty;
            public Requirement[] Needs = Array.Empty<Requirement>();
            public CheckBox Check = null!;
            public Border Card = null!;

            /// <summary>"Готово ✓" (green) or "Липсва: ..." (amber), right-aligned on the title row.</summary>
            public TextBlock Status = null!;
            public bool IsChecked => Check.IsChecked == true;
        }

        /// <summary>A pick tile: the button, its icon, its status and action lines and the picked check mark.</summary>
        private sealed class PickTile
        {
            public Button Button = null!;
            public TextBlock Icon = null!;
            public TextBlock Status = null!;
            public TextBlock Action = null!;
            public TextBlock Check = null!;

            // the route-axis row only
            public System.Windows.Shapes.Rectangle? Frame;
            public Border? Badge;
        }

        // ---- UI Controls ----
        private TextBlock _lblCadRegister = null!;
        private TextBlock _lblCadDetails = null!;
        private PickTile _tileServitude = null!;
        private PickTile _tilePoles = null!;
        private PickTile _tileParcels = null!;
        private PickTile _tileAxis = null!;
        private ReportOption _optMvpMathTest = null!;
        private ReportOption _optPoleSteps = null!;
        private ReportOption _optCadControl = null!;
        private ReportOption _optAffectedRegister = null!;
        private ReportOption _optPoleStepsRegister = null!;
        private ReportOption _optCoordinateRegister = null!;
        private ReportOption _optServitudeRegister = null!;
        private ReportOption _optTerritoryBalance = null!;
        private readonly List<ReportOption> _reports = new List<ReportOption>();
        private TextBox _txtRegisterProject = null!;
        private TextBox _txtServitudeLeftStart = null!;
        private TextBox _txtServitudeRightStart = null!;
        private CheckBox _chkDrawServitudePoints = null!;
        private CheckBox _chkReverseRoute = null!;
        private TextBox _txtLog = null!;
        private Button _btnGenerate = null!;
        private Button _btnOpenFolder = null!;
        private TextBlock _lblFooterBlocked = null!;
        private TextBlock _lblFooterFirst = null!;

        // ---- State ----
        private string _projectDir = string.Empty;

        // The loaded AGKK .cad files (one землище each). Not bound to a drawing, so they survive switching drawings.
        private CadRegisterSet? _cadSet;

        // Picks are data (ObjectIds) bound to the drawing they were made in; see "PICK STATE".
        private Document? _pickDoc;
        private ObjectId _servitudeId = ObjectId.Null;
        private List<ParcelPick> _parcelPicks = new List<ParcelPick>();
        private List<PolePick> _polePicks = new List<PolePick>();
        private List<ObjectId> _axisIds = new List<ObjectId>();

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
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // Отвори папката
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // status
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });                       // Generate

            _btnOpenFolder = new Button
            {
                Content = IconText(GlyphFolder, "Отвори папката"),
                Margin = new Thickness(0, 0, 14, 0), Visibility = System.Windows.Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Отваря папката на чертежа, където се записват справките"
            };
            _btnOpenFolder.Click += BtnOpenFolder_Click;
            Grid.SetColumn(_btnOpenFolder, 0);
            footerGrid.Children.Add(_btnOpenFolder);

            // Why a ticked report cannot run: how many, and the first one with what it lacks
            _lblFooterBlocked = new TextBlock
            {
                FontSize = 12.5, Foreground = SubtextBrush, TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = System.Windows.Visibility.Collapsed
            };
            _lblFooterFirst = new TextBlock
            {
                FontSize = 12.5, Foreground = WarningBrush, TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0), Visibility = System.Windows.Visibility.Collapsed
            };
            var footerStatus = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            footerStatus.Children.Add(_lblFooterBlocked);
            footerStatus.Children.Add(_lblFooterFirst);
            Grid.SetColumn(footerStatus, 1);
            footerGrid.Children.Add(footerStatus);

            _btnGenerate = new Button { Style = KeyedStyle(PrimaryButton), VerticalAlignment = VerticalAlignment.Center };
            _btnGenerate.Click += BtnGenerate_Click;
            Grid.SetColumn(_btnGenerate, 2);
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
                FontSize = 12.5, Foreground = SubtextBrush, Margin = new Thickness(0, 3, 0, 0),
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

        /// <summary>Step 2: the servitude, pole and parcel picks, and the optional route axis under them.</summary>
        private UIElement BuildPickCard()
        {
            var tiles = new UniformGrid { Columns = 3, Margin = new Thickness(-5, 0, -5, 0) };

            _tileServitude = MakePickTile(GlyphLayers, "Сервитут");
            _tileServitude.Button.Click += BtnPickServitude_Click;
            tiles.Children.Add(_tileServitude.Button);

            _tilePoles = MakePickTile(GlyphPin, "Стълбове");
            _tilePoles.Button.Click += BtnPickPoles_Click;
            tiles.Children.Add(_tilePoles.Button);

            _tileParcels = MakePickTile(GlyphParcels, "Имоти");
            _tileParcels.Button.Click += BtnPickParcels_Click;
            tiles.Children.Add(_tileParcels.Button);

            SetTile(_tileServitude, false);
            SetTile(_tilePoles, false);
            SetTile(_tileParcels, false);

            // The axis is optional for every report but the servitude register: a row of its own, less prominent
            UIElement axisRow = MakeAxisRow();
            _tileAxis.Button.Click += BtnPickAxis_Click;

            var content = new StackPanel();
            content.Children.Add(tiles);
            content.Children.Add(axisRow);
            RefreshAxisRow();

            return MakeCard(2, "Геометрии от чертежа",
                "Натиснете и изберете обектите в чертежа. Изборът важи за текущия чертеж.", content);
        }

        /// <summary>Step 3: the reports, each with the inputs it needs.</summary>
        private UIElement BuildReportsCard()
        {
            var list = new StackPanel();

            AddGroupHeader(list, "ПРОВЕРКИ", first: true);

            _optMvpMathTest = AddReport(list, "MVP математически тест",
                "Площи в сервитута и под стълбовете по имоти, с проверка на баланса.",
                Requirement.Servitude, Requirement.Poles, Requirement.Parcels);

            _optCadControl = AddReport(list, "Контролна справка от .cad",
                "Избраните имоти срещу данните в .cad, по един ред на право.",
                Requirement.Cad, Requirement.Parcels);

            _optPoleSteps = AddReport(list, "Таблица стъпки на стълбове",
                "Площта на всяка стъпка, разделена по имоти.",
                Requirement.Poles, Requirement.Parcels);

            AddGroupHeader(list, "РЕГИСТРИ");

            _optAffectedRegister = AddReport(list, "Регистър на засегнатите имоти",
                "Собственици и засегнати площи по имоти.",
                AllRequirements);

            _optPoleStepsRegister = AddReport(list, "Регистър на стъпките на стълбовете",
                "Стъпките по стълбове и имоти, със собствениците от .cad.",
                Requirement.Cad, Requirement.Poles, Requirement.Parcels);

            _optCoordinateRegister = AddReport(list, "Координатен регистър на стъпките",
                "Координатите на центъра и чупките на всяка стъпка, по землища.",
                Requirement.Cad, Requirement.Poles, Requirement.Parcels);

            _optServitudeRegister = AddReport(list, "Координатен регистър на сервитута",
                "Координатите на точките на сервитута — ляво и дясно, по землища.",
                Requirement.Cad, Requirement.Servitude, Requirement.Axis, Requirement.Parcels);
            AddCardExtra(_optServitudeRegister, BuildServitudeOptions());

            AddGroupHeader(list, "БАЛАНСИ");

            _optTerritoryBalance = AddReport(list, "Баланси на територията и общата рекапитулация",
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
                    Text = hint, FontSize = 12.5, Foreground = SubtextBrush, TextWrapping = TextWrapping.Wrap,
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

        private PickTile MakePickTile(string glyph, string title)
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
                    FontSize = 12.5, Foreground = SubtextBrush, Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                Action = new TextBlock
                {
                    FontSize = 12.5, Foreground = AccentBrush, TextTrimming = TextTrimming.CharacterEllipsis
                },
                Check = new TextBlock
                {
                    Text = GlyphCheck, FontFamily = IconFont, FontSize = 16, Foreground = SuccessBrush,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0),
                    Visibility = System.Windows.Visibility.Collapsed
                }
            };

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold });
            text.Children.Add(tile.Status);
            text.Children.Add(tile.Action);

            var content = new DockPanel();
            DockPanel.SetDock(tile.Icon, Dock.Left);
            content.Children.Add(tile.Icon);
            DockPanel.SetDock(tile.Check, Dock.Right);
            content.Children.Add(tile.Check);
            content.Children.Add(text);

            tile.Button = new Button
            {
                Content = content, Style = KeyedStyle(TileButton), Margin = new Thickness(5, 0, 5, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = $"Изберете {title.ToLowerInvariant()} в чертежа"
            };
            return tile;
        }

        /// <summary>
        /// A pick tile picked: neutral border, a green check on the right and "&lt;what&gt; · промени". Not picked: accent border and
        /// background, the amber "Задължително" and the action "Избери в чертежа". A failed pick shows the not-picked look (the log
        /// says why).
        /// </summary>
        private static void SetTile(PickTile tile, bool picked, string pickedText = "")
        {
            tile.Check.Visibility = picked ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (picked)
            {
                tile.Status.Text = pickedText;
                tile.Status.Foreground = SubtextBrush;
                tile.Action.Visibility = System.Windows.Visibility.Collapsed;
                tile.Button.BorderBrush = SurfaceBorderBrush;
                tile.Button.ClearValue(Control.BackgroundProperty);
            }
            else
            {
                tile.Status.Text = "Задължително";
                tile.Status.Foreground = WarningBrush;
                tile.Action.Text = "Избери в чертежа";
                tile.Action.Visibility = System.Windows.Visibility.Visible;
                tile.Button.BorderBrush = AccentBrush;
                tile.Button.Background = AccentSoftBrush;
            }
        }

        /// <summary>
        /// The route axis: a full-width row under the three tiles, dashed and muted ("по избор"), because only the servitude register
        /// needs it. <see cref="RefreshAxisRow"/> switches it to the required look when that report is ticked and the axis is missing.
        /// </summary>
        private UIElement MakeAxisRow()
        {
            var tile = new PickTile
            {
                Icon = new TextBlock
                {
                    Text = GlyphLayers, FontFamily = IconFont, FontSize = 18, Foreground = SubtextBrush,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0)
                },
                Status = new TextBlock
                {
                    FontSize = 12.5, Foreground = SubtextBrush, Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                Action = new TextBlock
                {
                    FontSize = 12.5, Foreground = AccentBrush, VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                },
                Check = new TextBlock()
            };

            var badgeText = new TextBlock { Text = "по избор", FontSize = 12, Foreground = SubtextBrush };
            tile.Badge = new Border
            {
                BorderBrush = SurfaceBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 0, 8, 1), Margin = new Thickness(10, 0, 0, 0), Child = badgeText,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            titleRow.Children.Add(new TextBlock { Text = "Ос на трасето", FontSize = 14, FontWeight = FontWeights.SemiBold });
            titleRow.Children.Add(tile.Badge);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(titleRow);
            text.Children.Add(tile.Status);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(text, 1);
            Grid.SetColumn(tile.Action, 2);
            grid.Children.Add(tile.Icon);
            grid.Children.Add(text);
            grid.Children.Add(tile.Action);

            tile.Button = new Button
            {
                Content = grid, Style = KeyedStyle(GhostButton), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 10, 0, 0),
                ToolTip = "Изберете оста на трасето в чертежа"
            };
            tile.Frame = new System.Windows.Shapes.Rectangle
            {
                RadiusX = 8, RadiusY = 8, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 },
                Stroke = SurfaceBorderBrush, IsHitTestVisible = false, Margin = new Thickness(0, 10, 0, 0)
            };

            _tileAxis = tile;
            var root = new Grid();
            root.Children.Add(tile.Button);
            root.Children.Add(tile.Frame);
            return root;
        }

        /// <summary>
        /// Axis row look: optional and muted by default; required (accent, amber text) while the servitude register is ticked and
        /// no axis is picked, because that report is skipped without it.
        /// </summary>
        private void RefreshAxisRow()
        {
            if (_tileAxis == null || _tileAxis.Frame == null || _tileAxis.Badge == null) return;

            bool picked = _axisIds.Count > 0;
            bool required = !picked && _optServitudeRegister != null && _optServitudeRegister.IsChecked;

            _tileAxis.Frame.Stroke = required ? AccentBrush : SurfaceBorderBrush;
            if (required) _tileAxis.Button.Background = AccentSoftBrush; else _tileAxis.Button.ClearValue(Control.BackgroundProperty);
            _tileAxis.Badge.Visibility = required ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
            _tileAxis.Icon.Foreground = required ? AccentBrush : SubtextBrush;

            if (picked)
            {
                _tileAxis.Status.Text = _axisIds.Count == 1 ? "1 обект" : $"{_axisIds.Count} обекта";
                _tileAxis.Status.Foreground = SubtextBrush;
                _tileAxis.Action.Text = "Промени";
            }
            else if (required)
            {
                _tileAxis.Status.Text = "Задължително за координатния регистър на сервитута";
                _tileAxis.Status.Foreground = WarningBrush;
                _tileAxis.Action.Text = "Избери в чертежа";
            }
            else
            {
                _tileAxis.Status.Text = "Не е избрана";
                _tileAxis.Status.Foreground = SubtextBrush;
                _tileAxis.Action.Text = "Избери в чертежа";
            }
        }

        /// <summary>A small uppercase subheader over a group of reports.</summary>
        private static void AddGroupHeader(Panel list, string text, bool first = false)
        {
            list.Children.Add(new TextBlock
            {
                Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = SubtextBrush,
                Margin = new Thickness(2, first ? 0 : 10, 0, 8)
            });
        }

        private ReportOption AddReport(Panel list, string title, string description, params Requirement[] needs)
        {
            var option = new ReportOption { Title = title, Needs = needs };

            option.Status = new TextBlock
            {
                FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0), TextAlignment = System.Windows.TextAlignment.Right, TextWrapping = TextWrapping.Wrap
            };
            var titleRow = new Grid();
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 300 });
            titleRow.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(option.Status, 1);
            titleRow.Children.Add(option.Status);

            var text = new StackPanel();
            text.Children.Add(titleRow);
            text.Children.Add(new TextBlock
            {
                Text = description, FontSize = 12.5, Foreground = SubtextBrush, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });

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

        /// <summary>Adds extra controls (number boxes, a tick) under a report's checkbox, inside its own card.</summary>
        private static void AddCardExtra(ReportOption option, UIElement extra)
        {
            ((StackPanel)option.Card.Child).Children.Add(extra);
        }

        /// <summary>
        /// The servitude register's own settings: the first number of each edge and whether the same click also draws the
        /// numbered point blocks in the drawing.
        /// </summary>
        private UIElement BuildServitudeOptions()
        {
            var row = new WrapPanel { Margin = new Thickness(28, 0, 0, 0) };

            _txtServitudeLeftStart = NumberBox(ServitudeRegisterBuilder.DefaultLeftStart);
            _txtServitudeRightStart = NumberBox(ServitudeRegisterBuilder.DefaultRightStart);

            row.Children.Add(new TextBlock
            {
                Text = "Ляво от", Foreground = SubtextBrush, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0)
            });
            row.Children.Add(_txtServitudeLeftStart);
            row.Children.Add(new TextBlock
            {
                Text = "Дясно от", Foreground = SubtextBrush, FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0)
            });
            row.Children.Add(_txtServitudeRightStart);

            _chkDrawServitudePoints = new CheckBox
            {
                Content = new TextBlock { Text = "Начертай точките в чертежа", FontSize = 12 },
                IsChecked = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 0, 0),
                ToolTip = $"Вмъква блок {ServitudePointBlockNames.BlockName} с номера на всяка точка " +
                          $"в слой {ServitudePointBlockNames.Layer}"
            };
            row.Children.Add(_chkDrawServitudePoints);

            _chkReverseRoute = new CheckBox
            {
                Content = new TextBlock { Text = "Обратна посока", FontSize = 12 },
                IsChecked = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(18, 0, 0, 0),
                ToolTip = "Броенето започва от другия край на трасето. \"Ляво\" и \"дясно\" са спрямо посоката на движение, " +
                          "затова страните се разменят: това, което иначе е дясно, става ляво и получава номерата от \"Ляво от\"."
            };
            row.Children.Add(_chkReverseRoute);

            return row;
        }

        private static TextBox NumberBox(int value) => new TextBox
        {
            Text = value.ToString(CultureInfo.InvariantCulture),
            Width = 64, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };

        private static string RequirementName(Requirement need)
        {
            switch (need)
            {
                case Requirement.Cad:       return ".cad";
                case Requirement.Servitude: return "Сервитут";
                case Requirement.Poles:     return "Стълбове";
                case Requirement.Axis:      return "Ос на трасето";
                default:                    return "Имоти";
            }
        }

        // ================================================================
        //  READINESS
        //  Each report says on its title row whether it is ready or what it lacks; the footer
        //  says how many ticked reports are blocked and counts the ones that can run.
        // ================================================================

        private bool Has(Requirement need)
        {
            switch (need)
            {
                case Requirement.Cad:       return _cadSet != null && _cadSet.Count > 0;
                case Requirement.Servitude: return !_servitudeId.IsNull;
                case Requirement.Poles:     return _polePicks.Count > 0;
                case Requirement.Axis:      return _axisIds.Count > 0;
                default:                    return _parcelPicks.Count > 0;
            }
        }

        private List<Requirement> Missing(ReportOption option) => option.Needs.Where(n => !Has(n)).ToList();

        private void UpdateReadiness()
        {
            if (_btnGenerate == null) return;

            foreach (ReportOption option in _reports)
            {
                List<Requirement> missing = Missing(option);
                if (missing.Count == 0)
                {
                    option.Status.Text = "Готово ✓";
                    option.Status.Foreground = SuccessBrush;
                }
                else
                {
                    option.Status.Text = "Липсва: " + string.Join(", ", missing.Select(RequirementName));
                    option.Status.Foreground = WarningBrush;
                }
                option.Card.BorderBrush = option.IsChecked ? AccentBrush : SurfaceBorderBrush;
            }
            RefreshAxisRow();

            List<ReportOption> ticked = _reports.Where(r => r.IsChecked).ToList();
            List<ReportOption> blocked = ticked.Where(r => Missing(r).Count > 0).ToList();
            int total = ticked.Count, ready = total - blocked.Count;

            // The button stays enabled while something is ticked, even if nothing can run: the skip warnings then explain why
            _btnGenerate.IsEnabled = total > 0;
            _btnGenerate.Content = IconText(GlyphPlay,
                total == 0 ? "Отметнете справка"
                : blocked.Count == 0 ? (total == 1 ? "Генерирай 1 справка" : $"Генерирай {total} справки")
                : $"Генерирай {ready} от {total} справки");

            if (blocked.Count == 0)
            {
                _lblFooterBlocked.Visibility = System.Windows.Visibility.Collapsed;
                _lblFooterFirst.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                _lblFooterBlocked.Text = $"{blocked.Count} от {total} справки не може да се генерира";
                _lblFooterBlocked.Visibility = System.Windows.Visibility.Visible;
                _lblFooterFirst.Text = $"{blocked[0].Title} — липсва: {string.Join(", ", Missing(blocked[0]).Select(RequirementName))}";
                _lblFooterFirst.Visibility = System.Windows.Visibility.Visible;
            }
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
                    LogFailure("Reading the .cad folder", ex);
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
                LogFailure("Loading .cad", ex);
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
            !_servitudeId.IsNull || _parcelPicks.Count > 0 || _polePicks.Count > 0 || _axisIds.Count > 0;

        private void ClearPicks()
        {
            _servitudeId = ObjectId.Null;
            _parcelPicks = new List<ParcelPick>();
            _polePicks = new List<PolePick>();
            _axisIds = new List<ObjectId>();

            RefreshAxisRow();
            SetTile(_tileServitude, false);
            SetTile(_tilePoles, false);
            SetTile(_tileParcels, false);
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
                LogWarning("Изборът е направен в друг чертеж — изберете отново. Справките не са пуснати.");
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
                        SetTile(_tileServitude, true, "Избран · промени");
                        AppendLog("Сервитут избран успешно.");
                    }
                    else
                    {
                        SetTile(_tileServitude, false);
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
                            foreach (var entry in PoleFootprintExtractor.ExtractAll(poleBlocks, tr))
                            {
                                entries.Add(entry);
                                Polyline? footprint = entry.Result.FootprintPolyline;
                                if (footprint != null)
                                {
                                    // The same pole drawn twice (same number, same footprint) is taken once
                                    Extents3d ext = footprint.GeometricExtents;
                                    double[] box = { ext.MinPoint.X, ext.MinPoint.Y, ext.MaxPoint.X, ext.MaxPoint.Y };
                                    string handle = entry.BlockId.Handle.ToString();
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
                                        continue;
                                    }
                                    seenFootprints.Add((entry.PoleId, footprint.Area, box, handle));

                                    picks.Add(new PolePick
                                    {
                                        BlockId = entry.BlockId,
                                        Key = entry.Key
                                    });
                                }
                                else
                                {
                                    AppendLog($"ПРЕДУПРЕЖДЕНИЕ: {entry.Result.ErrorMessage}");
                                }
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
                        SetTile(_tilePoles, true, $"{_polePicks.Count} стълба · промени");
                        AppendLog($"Избрани и екстрактнати {_polePicks.Count} стълба.");
                    }
                    else
                    {
                        SetTile(_tilePoles, false);
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
                        SetTile(_tileParcels, true, $"{_parcelPicks.Count} имота · промени");
                        AppendLog($"Избрани {_parcelPicks.Count} имота.");
                    }
                    else
                    {
                        SetTile(_tileParcels, false);
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

        /// <summary>The route axis (optional): only the recapitulation uses it, for the route length per землище.</summary>
        private void BtnPickAxis_Click(object sender, RoutedEventArgs e)
        {
            if (!BeginPick(out Document doc)) return;
            Editor ed = doc.Editor;

            using (EditorUserInteraction interaction = ed.StartUserInteraction(this))
            {
                try
                {
                    EnsureServices();
                    _axisIds = _selection!.SelectAxisCurves("\nSelect the route axis (polylines, lines, arcs): ");

                    if (_axisIds.Count > 0)
                    {
                        RefreshAxisRow();
                        AppendLog($"Избрана ос на трасето: {_axisIds.Count} обекта.");
                    }
                    else
                    {
                        RefreshAxisRow();
                        AppendLog("Осът на трасето не беше избрана — дължината на трасето няма да се изчисли.");
                    }
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
                    LogWarning("Отметнете поне една справка.");
                    return;
                }

                EnsureServices();
                ResetClickCache();
                _clickWarnUncovered = ticked.Any(o => o == _optAffectedRegister || o == _optTerritoryBalance || o == _optPoleStepsRegister);
                // Only the affected-parcels register and the balances read the servitude areas (BuildAffectedRegisters)
                _clickNeedsServitude = ticked.Any(o => o == _optAffectedRegister || o == _optTerritoryBalance);
                PerfTimer.LogMemory(_logger, "at start of Generate");
                bool ranAny = false;
                using (PerfTimer.Measure(_logger, "BtnGenerate_Click"))
                foreach (ReportOption option in ticked)
                {
                    List<Requirement> missing = Missing(option);
                    if (missing.Count > 0)
                    {
                        LogWarning($"{option.Title} — липсва: {string.Join(", ", missing.Select(RequirementName))}. Справката е пропусната.");
                        continue;
                    }

                    using (PerfTimer timer = PerfTimer.Measure(_logger, "Report: " + option.Title))
                    {
                        if (option == _optMvpMathTest) RunMvpMathTest(doc);
                        else if (option == _optPoleSteps) RunPoleStepsTable(doc);
                        else if (option == _optCadControl) RunCadControlReport(doc, _cadSet!);
                        else if (option == _optAffectedRegister) RunAffectedParcelsRegister(doc, _cadSet!);
                        else if (option == _optPoleStepsRegister) RunPoleStepsRegister(doc, _cadSet!);
                        else if (option == _optCoordinateRegister) RunCoordinateRegister(doc, _cadSet!);
                        else if (option == _optServitudeRegister) RunServitudeRegister(doc, _cadSet!);
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
                LogFailure("Generate (GUI)", ex);

                Document doc = Application.DocumentManager.MdiActiveDocument;
                if (doc != null)
                {
                    doc.Editor.WriteMessage($"\nFatal error in Generate: {ex.Message}\n");
                }
            }
            finally
            {
                ResetClickCache();
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
                LogFailure("MVP math test", ex);
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
                ClickGeometry shared = GetClickGeometry(doc);
                PoleStepsGeometry geometry = shared.AsPoleSteps();

                if (!shared.UncoveredLogged)
                {
                    foreach (var step in PoleStepsTableBuilder.FindUncoveredSteps(
                        geometry.Footprints, geometry.Pieces, GeometryTolerances.SliverAreaSqm))
                    {
                        AppendLog(PoleStepsTableBuilder.FormatUncoveredWarning(step));
                    }
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
                LogFailure("Pole steps table", ex);
            }
        }

        /// <summary>The nomenclatures and the EKATTE register the .cad reports share; the warnings are logged by the caller.</summary>
        private void LoadReportReferenceData(out Nomenclatures nomenclatures, out EkatteRegister ekatte)
        {
            if (_clickReferenceData == null)
            {
                using (PerfTimer.Measure(_logger, "LoadReportReferenceData"))
                {
                    string templateDir = TemplateDir;
                    Nomenclatures loadedNomenclatures = Nomenclatures.Load(Path.Combine(templateDir, FileNames.NomenclaturesFolder), _clickReferenceWarnings.Add);
                    EkatteRegister loadedEkatte = EkatteRegister.LoadWithDefaults(Path.Combine(templateDir, FileNames.EkatteRegisterFile), _clickReferenceWarnings.Add);
                    _clickReferenceData = (loadedNomenclatures, loadedEkatte);
                }
            }
            nomenclatures = _clickReferenceData.Value.Nomenclatures;
            ekatte = _clickReferenceData.Value.Ekatte;
        }

        /// <summary>Logs the nomenclature warnings raised since the last call (each one once per click, whichever report raised it).</summary>
        private void LogNewReferenceWarnings()
        {
            while (_clickReferenceWarningsLogged < _clickReferenceWarnings.Count)
            {
                LogWarning(_clickReferenceWarnings[_clickReferenceWarningsLogged++]);
            }
        }

        // What one click of "Генерирай" shares between its reports; never kept across clicks (the drawing may change).
        private (Nomenclatures Nomenclatures, EkatteRegister Ekatte)? _clickReferenceData;
        private readonly List<string> _clickReferenceWarnings = new List<string>();
        private int _clickReferenceWarningsLogged;
        private ClickGeometry? _clickGeometry;
        private bool _clickWarnUncovered;
        private bool _clickNeedsServitude = true;
        private AffectedRegisterRun? _clickRun;
        private string? _clickRunProject;

        private void ResetClickCache()
        {
            _clickReferenceData = null;
            _clickReferenceWarnings.Clear();
            _clickReferenceWarningsLogged = 0;
            _clickGeometry = null;
            _clickWarnUncovered = false;
            _clickNeedsServitude = true;
            _clickRun = null;
            _clickRunProject = null;
        }

        /// <summary>
        /// The geometry of one click: footprints and pieces (as <see cref="TopologyProcessor.ComputePoleStepPieces"/> gives them),
        /// plus the per-parcel servitude areas when a servitude is picked and still in the drawing.
        /// </summary>
        private sealed class ClickGeometry
        {
            public List<PoleFootprintArea> Footprints = new List<PoleFootprintArea>();
            public List<PoleStepPiece> Pieces = new List<PoleStepPiece>();

            /// <summary>Null when there is no servitude to intersect (only the pole-steps reports can use this geometry then).</summary>
            public List<RegisterParcelAreas>? Parcels;

            /// <summary>One ID drawn as several polylines is one parcel: the areas are summed.</summary>
            public Dictionary<string, double> DrawnAreaSqmById = new Dictionary<string, double>(StringComparer.Ordinal);

            /// <summary>The "pole not entirely inside the parcels" warnings were already logged for this click.</summary>
            public bool UncoveredLogged;

            /// <summary>Footprint corners in P-tag order and the label rotation, for the coordinate register.</summary>
            public List<PoleCorners> PoleCorners = new List<PoleCorners>();

            /// <summary>Picked poles whose footprint could not be extracted in this click.</summary>
            public List<string> PolesWithoutFootprint = new List<string>();

            public PoleStepsGeometry AsPoleSteps()
            {
                var geometry = new PoleStepsGeometry();
                geometry.Footprints.AddRange(Footprints);
                geometry.Pieces.AddRange(Pieces);
                return geometry;
            }
        }

        /// <summary>
        /// Intersects the poles with the parcels (and the servitude with the parcels) at most once per click, in one short
        /// transaction under a document lock; the in-memory footprints are disposed before returning.
        /// </summary>
        private ClickGeometry GetClickGeometry(Document doc)
        {
            if (_clickGeometry != null) return _clickGeometry;

            EnsureServices();
            var topo = new TopologyProcessor(_logger!);
            var shared = new ClickGeometry();
            var roundedPoles = new List<string>();
            int polesFromFields = 0;
            int polesWithFallback = 0;
            string? fieldCodeSample = null;

            using (PerfTimer.Measure(_logger, "ClickGeometry transaction (parcels, poles, topology)"))
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                // The servitude region (~7 s) is only needed by the reports that work on the servitude areas; without it the
                // poles are intersected with the parcels alone
                Polyline? servitude = _clickNeedsServitude ? OpenPolyline(tr, _servitudeId) : null;
                LogPoleFieldDiagnostic(tr);
                var parcels = OpenParcels(tr);
                var entries = ExtractPoles(tr);
                try
                {
                    foreach (PoleFootprintEntry entry in entries)
                    {
                        if (entry.Result.CornerPoints == null)
                        {
                            shared.PolesWithoutFootprint.Add(entry.PoleId);
                            continue;
                        }
                        if (entry.Result.RoundedByUnits) roundedPoles.Add(PoleLabels.StripPrefix(entry.PoleId));
                        if (entry.Result.FieldValues > 0) polesFromFields++;
                        fieldCodeSample ??= entry.Result.FieldCodeSample;
                        if (entry.Result.FallbackUsed) polesWithFallback++;

                        var corners = entry.Result.CornerPoints.Select(p => (p.X, p.Y)).ToList();
                        Polyline footprint = entry.Result.FootprintPolyline!;
                        var vertices = Enumerable.Range(0, footprint.NumberOfVertices)
                            .Select(i => footprint.GetPoint2dAt(i)).Select(p => (p.X, p.Y)).ToList();
                        bool matches = FootprintCorners.AllMatch(corners, vertices, CornerMatchToleranceM);
                        if (!matches)
                        {
                            LogWarning($"Стълб {PoleLabels.StripPrefix(entry.PoleId)}: чупките не съвпадат със стъпката (повече от {CornerMatchToleranceM} м) — блоковете {PoleCornerBlockNames.BlockName} не се чертаят.");
                        }
                        shared.PoleCorners.Add(new PoleCorners
                        {
                            PoleNumber = entry.PoleId,
                            Corners = corners,
                            LabelRotation = string.IsNullOrEmpty(entry.Result.PoleNumber) ? null : entry.Result.LabelRotation,
                            DrawBlocks = matches
                        });
                    }

                    if (servitude != null)
                    {
                        RegisterGeometry registerGeometry = topo.ComputeRegisterGeometry(servitude, PoleFootprints(entries), parcels);
                        shared.Footprints = registerGeometry.Footprints;
                        shared.Pieces = registerGeometry.Pieces;
                        shared.Parcels = registerGeometry.Parcels;
                    }
                    else
                    {
                        PoleStepsGeometry poleSteps = topo.ComputePoleStepPieces(PoleFootprints(entries), parcels);
                        shared.Footprints = poleSteps.Footprints;
                        shared.Pieces = poleSteps.Pieces;
                    }

                    foreach (var parcel in parcels)
                    {
                        shared.DrawnAreaSqmById.TryGetValue(parcel.Key, out double sum);
                        shared.DrawnAreaSqmById[parcel.Key] = sum + parcel.Value.Area;
                    }
                    tr.Commit();
                }
                finally
                {
                    DisposeFootprints(entries);
                }
            }

            LogCoordinatePrecision(doc, roundedPoles, polesFromFields, polesWithFallback, shared.PoleCorners.Count, fieldCodeSample);

            if (_clickWarnUncovered)
            {
                foreach (var step in PoleStepsTableBuilder.FindUncoveredSteps(
                    shared.Footprints, shared.Pieces, GeometryTolerances.SliverAreaSqm))
                {
                    LogWarning(PoleStepsTableBuilder.FormatUncoveredWarning(step));
                }
                shared.UncoveredLogged = true;
            }

            _clickGeometry = shared;
            return shared;
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

                LoadReportReferenceData(out Nomenclatures nomenclatures, out EkatteRegister ekatte);
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
                LogNewReferenceWarnings();

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
                LogFailure("Cad control report", ex);
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
            if (_clickRun != null && _clickRunProject == project) return _clickRun;

            EnsureServices();
            ClickGeometry shared = GetClickGeometry(doc);
            if (shared.Parcels == null)
            {
                AppendLog("ГРЕШКА: Сервитутът вече не съществува в чертежа — изберете отново.");
                return null;
            }
            ClickGeometry geometry = shared;
            List<RegisterParcelAreas> geometryParcels = shared.Parcels;

            var run = new AffectedRegisterRun { Pieces = geometry.Pieces };
            LoadReportReferenceData(out Nomenclatures nomenclatures, out EkatteRegister ekatte);
            run.Nomenclatures = nomenclatures;
            MunicipalityGroupingResult grouping = GroupParcelsForReport(
                cadSet, geometryParcels.Select(p => p.ParcelId), ekatte, MunicipalityGrouping.LowestPoleByParcel(geometry.Pieces));

            using var buildTimer = PerfTimer.Measure(_logger, "BuildAffectedRegisters builder loop");
            foreach (MunicipalityGroup group in grouping.Groups)
            {
                var reports = new List<AffectedRegister>();
                foreach (SettlementSection section in group.Sections)
                {
                    var ids = new HashSet<string>(section.ParcelIds, StringComparer.Ordinal);
                    AffectedRegister report = AffectedParcelsRegisterBuilder.Build(
                        geometryParcels.Where(p => ids.Contains(p.ParcelId)),
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
            _clickRun = run;
            _clickRunProject = project;
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
                LogNewReferenceWarnings();

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
                LogFailure("Affected parcels register", ex);
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
                LogNewReferenceWarnings();
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

                WriteRecapitulation(doc, municipalities, totals, project);
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при балансите: {ex.Message}");
                LogFailure("Territory balance", ex);
            }
        }

        /// <summary>
        /// Builds Обща_рекапитулация.xlsx from the territory balances just built (nothing is recomputed) and the route length
        /// per землище. A failure here is logged and does not affect the balances already written.
        /// </summary>
        private void WriteRecapitulation(
            Document doc,
            List<(MunicipalityGroup Group, List<TerritoryBalance> Sections)> municipalities,
            List<(string SheetName, TerritoryBalance Balance)> totals,
            string project)
        {
            try
            {
                Dictionary<string, decimal>? routeMetres = ComputeRouteMetres(doc);
                EkatteRegister ekatte = EkatteRegister.LoadWithDefaults(Path.Combine(TemplateDir, FileNames.EkatteRegisterFile));

                List<RecapitulationSheet> sheets = RecapitulationBuilder.Build(municipalities, project, ekatte, routeMetres);
                foreach (RecapitulationMunicipality municipality in sheets.SelectMany(sheet => sheet.Municipalities))
                {
                    (string SheetName, TerritoryBalance Balance) combined = totals.FirstOrDefault(t => t.SheetName == municipality.Key);
                    if (combined.Balance != null && !RecapitulationBuilder.MatchesCombinedBalance(municipality, combined.Balance))
                    {
                        LogWarning($"Рекапитулация, общ. {municipality.Name}: общото за общината не съвпада с общия баланс.");
                    }
                }

                if (sheets.Count == 0)
                {
                    AppendLog("Няма данни за рекапитулацията — файлът не е записан.");
                    return;
                }

                string path = RecapitulationExporter.Export(sheets, _projectDir);
                AppendLog($"  Записан {Path.GetFileName(path)} в {_projectDir}.");
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при рекапитулацията: {ex.Message}");
                LogFailure("Recapitulation", ex);
            }
        }

        /// <summary>
        /// The route length in metres per EKATTE: the picked axis split at the parcel boundaries, each piece in the parcel that
        /// contains it. One short transaction under a document lock. Null (column stays empty) when no axis is picked or it is
        /// gone from the drawing. Only counts and metres go to the log.
        /// </summary>
        private Dictionary<string, decimal>? ComputeRouteMetres(Document doc)
        {
            using (PerfTimer.Measure(_logger, "ComputeRouteMetres"))
            {
                return ComputeRouteMetresCore(doc);
            }
        }

        private Dictionary<string, decimal>? ComputeRouteMetresCore(Document doc)
        {
            if (_axisIds.Count == 0)
            {
                AppendLog("Дължина на трасето: не е избрана ос — колоната е празна.");
                return null;
            }

            RouteLengthResult? result = null;
            int axisCount = 0;
            var stats = new RouteLengthStats();
            using (PerfTimer.Measure(_logger, "ComputeRouteMetres transaction"))
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                var axes = new List<Curve>();
                using (PerfTimer.Measure(_logger, "ComputeRouteMetres open axis"))
                {
                    foreach (ObjectId id in _axisIds)
                    {
                        if (id.IsNull || id.IsErased) continue;
                        if (tr.GetObject(id, OpenMode.ForRead) is Curve curve) axes.Add(curve);
                    }
                }
                axisCount = axes.Count;
                if (axes.Count > 0)
                {
                    var parcels = OpenParcels(tr);
                    using (PerfTimer.Measure(_logger, "RouteLengthCalculator.Compute"))
                    {
                        result = RouteLengthCalculator.Compute(axes, parcels, stats);
                    }
                    if (_logger != null) stats.Log(_logger);
                }
                tr.Commit();
            }

            if (result == null)
            {
                AppendLog("Дължина на трасето: избраната ос вече не е в чертежа — колоната е празна.");
                return null;
            }

            Dictionary<string, decimal> byEkatte = RouteLengths.SumByEkatte(result.MetresByParcelId);
            AppendLog($"  Дължина на трасето: {RouteLengths.ToKm(byEkatte.Values.Sum()):0.000} km в {byEkatte.Count} землища " +
                      $"({axisCount} обекта на оста, {result.Pieces} части).");
            if (result.OutsideMetres > 0.01)
            {
                LogWarning($"Дължина на трасето: {result.OutsideMetres:0.0} m от оста са извън избраните имоти.");
            }
            return byEkatte;
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
                ClickGeometry shared = GetClickGeometry(doc);
                PoleStepsGeometry geometry = shared.AsPoleSteps();
                Dictionary<string, double> drawnAreaSqmById = shared.DrawnAreaSqmById;

                if (geometry.Pieces.Count == 0)
                {
                    AppendLog("Няма стъпки на стълбове в избраните имоти — регистърът не е създаден.");
                    return;
                }

                LoadReportReferenceData(out Nomenclatures nomenclatures, out EkatteRegister ekatte);
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
                LogNewReferenceWarnings();

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
                LogFailure("Pole steps register", ex);
            }
        }

        /// <summary>
        /// Builds Координатен_регистър_на_стъпките.xlsx (official 07) from this click's footprints: one sheet per municipality,
        /// one section per землище, a pole listed once, in the землище of the parcel with its largest piece. Then draws a GBP032
        /// block at every corner of the listed poles (replacing the plugin's own blocks of a previous run). No owner data.
        /// </summary>
        private void RunCoordinateRegister(Document doc, CadRegisterSet cadSet)
        {
            try
            {
                AppendLog("── СТАРТИРАНЕ НА КООРДИНАТЕН РЕГИСТЪР НА СТЪПКИТЕ ──");
                EnsureServices();
                ClickGeometry shared = GetClickGeometry(doc);

                foreach (string pole in shared.PolesWithoutFootprint)
                {
                    LogWarning($"Стълб {PoleLabels.StripPrefix(pole)}: стъпката не е извлечена — стълбът е пропуснат в координатния регистър.");
                }
                if (shared.Pieces.Count == 0)
                {
                    AppendLog("Няма стъпки на стълбове в избраните имоти — координатният регистър не е създаден.");
                    return;
                }

                LoadReportReferenceData(out _, out EkatteRegister ekatte);
                MunicipalityGroupingResult grouping = GroupParcelsForReport(
                    cadSet, shared.DrawnAreaSqmById.Keys, ekatte, MunicipalityGrouping.LowestPoleByParcel(shared.Pieces));
                Dictionary<string, string> winners = TerritoryBalanceBuilder.WinningParcels(shared.Pieces);

                string project = string.IsNullOrWhiteSpace(_txtRegisterProject.Text) ? DefaultRegisterProject : _txtRegisterProject.Text;
                var sheets = new List<(string SheetName, IReadOnlyList<CoordinateRegister> Sections)>();
                var listed = new List<CoordinateRegisterBlock>();
                int reversed = 0;
                using (PerfTimer.Measure(_logger, "RunCoordinateRegister builder loop"))
                {
                    foreach (MunicipalityGroup group in grouping.Groups)
                    {
                        var reports = new List<CoordinateRegister>();
                        foreach (SettlementSection section in group.Sections)
                        {
                            CoordinateRegister report = CoordinateRegisterBuilder.Build(
                                shared.PoleCorners, winners, section.ParcelIds, project, section.EkatteTitle);
                            if (report.Blocks.Count == 0) continue;

                            reports.Add(report);
                            listed.AddRange(report.Blocks);
                            reversed += report.Reversed.Count;
                        }

                        if (reports.Count == 0) continue;
                        sheets.Add((group.SheetName, reports));
                        AppendLog($"  {group.SheetName}: {reports.Count} землища, {reports.Sum(r => r.Blocks.Count)} стълба.");
                    }
                }

                foreach (CoordinateRegisterBlock block in listed.Where(b => b.Corners.Count != 4))
                {
                    LogWarning($"Стълб {block.PoleNumber}: стъпката има {block.Corners.Count} чупки вместо 4 — изписани са всички.");
                }
                if (reversed > 0)
                {
                    AppendLog($"  {reversed} стъпки бяха обратно на часовниковата стрелка — обърнати, с точка 1 първа.");
                }
                var listedNumbers = new HashSet<string>(listed.Select(b => b.PoleNumber), StringComparer.Ordinal);
                List<string> unlisted = shared.PoleCorners.Select(p => PoleLabels.StripPrefix(p.PoleNumber))
                    .Where(n => !listedNumbers.Contains(n)).Distinct().ToList();
                if (unlisted.Count > 0)
                {
                    LogWarning($"{unlisted.Count} стълба не са в нито един избран имот с ЕКАТТЕ и не са в регистъра: " + JoinLimited(unlisted, 30));
                }

                if (sheets.Count == 0)
                {
                    AppendLog("Няма стълбове в избраните имоти — координатният регистър не е създаден.");
                    return;
                }

                string path;
                using (PerfTimer.Measure(_logger, "CoordinateRegisterExporter.Export")) path = CoordinateRegisterExporter.Export(sheets, _projectDir);
                AppendLog($"  Записан {Path.GetFileName(path)} в {_projectDir}.");

                DrawPoleCornerBlocks(doc, listed, shared.PoleCorners);
            }
            catch (Exception ex)
            {
                AppendLog($"ГРЕШКА при координатния регистър: {ex.Message}");
                LogFailure("Coordinate register", ex);
            }
        }

        /// <summary>
        /// Builds Координатен_регистър_на_сервитута.xlsx (official 08): the servitude outline split into its left and right
        /// edge along the route axis, every point numbered along the route and listed under the землище it falls in, with a
        /// point added wherever an edge crosses a землище boundary. One short transaction under a document lock reads the
        /// servitude, the axis, the poles and the parcels; nothing is written to the drawing unless the tick asks for it.
        /// No owner data.
        /// </summary>
        private void RunServitudeRegister(Document doc, CadRegisterSet cadSet)
        {
            try
            {
                AppendLog("── СТАРТИРАНЕ НА КООРДИНАТЕН РЕГИСТЪР НА СЕРВИТУТА ──");
                EnsureServices();

                int leftStart = ReadStartNumber(_txtServitudeLeftStart, ServitudeRegisterBuilder.DefaultLeftStart, "Ляво от");
                int rightStart = ReadStartNumber(_txtServitudeRightStart, ServitudeRegisterBuilder.DefaultRightStart, "Дясно от");

                ServitudeEdgePoints? edges = ReadServitudeEdgePoints(doc);
                if (edges == null) return;
                if (!edges.Ok)
                {
                    LogWarning($"Координатен регистър на сервитута: {edges.Error}. Справката не е създадена.");
                    return;
                }
                if (_polePicks.Count == 0)
                {
                    AppendLog("  Няма избрани стълбове — посоката на оста е както е начертана.");
                }
                else if (edges.AxisReversed)
                {
                    AppendLog("  Осът на трасето е обърнат, за да върви от най-малкия към най-големия номер стълб.");
                }
                if (edges.RouteReversedByRequest)
                {
                    AppendLog("  Обратна посока: броенето започва от другия край на трасето; ляво и дясно са разменени.");
                }
                if (edges.DuplicatesDropped > 0)
                {
                    AppendLog($"  Пропуснати {edges.DuplicatesDropped} повтарящи се възела на сервитута.");
                }
                if (edges.LeftMerged + edges.RightMerged > 0)
                {
                    LogWarning($"{edges.LeftMerged + edges.RightMerged} съседни точки на по-малко от " +
                               $"{ServitudeRegisterBuilder.MinPointSpacingM * 100:0} см бяха слети с предходната " +
                               $"(ляво {edges.LeftMerged}, дясно {edges.RightMerged}).");
                }
                if (edges.GapsBetweenSettlements > 0)
                {
                    LogWarning($"{edges.GapsBetweenSettlements} пресичания между землища минават през място без избран имот — " +
                               "точката е сложена там, където свършва предишното землище.");
                }

                // The count starts at the first point inside the picked parcels, whichever end of the route that is
                List<NumberedServitudePoint> left = ServitudeRegisterBuilder.NumberInsidePickedParcels(
                    edges.Left, leftStart, true, out int leftBefore, out int leftAfter);
                List<NumberedServitudePoint> right = ServitudeRegisterBuilder.NumberInsidePickedParcels(
                    edges.Right, rightStart, false, out int rightBefore, out int rightAfter);
                LogPointsLeftOut("ляво", left, leftBefore + leftAfter);
                LogPointsLeftOut("дясно", right, rightBefore + rightAfter);
                LogBoundaryDiagnostics("ляво", left, edges.LeftStats, edges.LeftMerged);
                LogBoundaryDiagnostics("дясно", right, edges.RightStats, edges.RightMerged);

                LoadReportReferenceData(out _, out EkatteRegister ekatte);
                MunicipalityGroupingResult grouping = GroupParcelsForReport(
                    cadSet, _parcelPicks.Select(p => p.ParcelId), ekatte, null);

                // This report has no poles to order the sections by, so they follow the route: where each землище is first met
                Dictionary<string, double> routeOrder = ServitudeRegisterBuilder.RouteOrder(left, right);
                double Position(string code) => routeOrder.TryGetValue(code, out double at) ? at : double.MaxValue;
                foreach (MunicipalityGroup group in grouping.Groups)
                {
                    group.Sections.Sort((a, b) => Position(a.Ekatte).CompareTo(Position(b.Ekatte)));
                }
                grouping.Groups.Sort((a, b) => Position(a.Sections[0].Ekatte).CompareTo(Position(b.Sections[0].Ekatte)));

                string project = string.IsNullOrWhiteSpace(_txtRegisterProject.Text) ? DefaultRegisterProject : _txtRegisterProject.Text;
                var sheets = new List<(string SheetName, IReadOnlyList<ServitudeRegister> Sections)>();
                using (PerfTimer.Measure(_logger, "RunServitudeRegister builder loop"))
                {
                    foreach (MunicipalityGroup group in grouping.Groups)
                    {
                        var reports = new List<ServitudeRegister>();
                        foreach (SettlementSection section in group.Sections)
                        {
                            ServitudeRegister report = ServitudeRegisterBuilder.Build(
                                left, right, section.Ekatte, project, section.EkatteTitle);
                            if (report.Rows.Count == 0) continue;

                            reports.Add(report);
                            AppendLog($"  {section.DisplayName}: ляво {string.Join(", ", ServitudeRegisterBuilder.NumberRanges(report.Rows.Where(r => r.Left != null).Select(r => r.Left!)))}" +
                                      $"; дясно {string.Join(", ", ServitudeRegisterBuilder.NumberRanges(report.Rows.Where(r => r.Right != null).Select(r => r.Right!)))}.");
                        }

                        if (reports.Count == 0) continue;
                        sheets.Add((group.SheetName, reports));
                    }
                }

                if (sheets.Count == 0)
                {
                    LogWarning("Няма точки на сервитута в избраните имоти — регистърът на сервитута не е създаден, блокове не са начертани.");
                    return;
                }

                AppendLog($"  Точки на сервитута: ляво {left.Count} ({leftStart}–{leftStart + left.Count - 1}), " +
                          $"дясно {right.Count} ({rightStart}–{rightStart + right.Count - 1}), " +
                          $"от които {edges.BoundaryPointsInserted} на граница между землища.");

                // The numbering is final here, so the blocks are drawn whether or not the xlsx could be written (it fails while
                // Excel has the previous one open): the drawing must show the same numbers the next xlsx will
                Exception? exportFailure = null;
                try
                {
                    string path;
                    using (PerfTimer.Measure(_logger, "ServitudeRegisterExporter.Export")) path = ServitudeRegisterExporter.Export(sheets, _projectDir);
                    AppendLog($"  Записан {Path.GetFileName(path)} в {_projectDir}.");
                }
                catch (Exception ex)
                {
                    exportFailure = ex;
                    LogWarning($"Регистърът на сервитута не е записан: {ex.Message}");
                    LogFailure("Servitude register export", ex);
                }

                if (_chkDrawServitudePoints.IsChecked == true)
                {
                    DrawServitudePoints(doc, left, right, edges.Outline, edges.RouteReversedByRequest);
                }
                else
                {
                    AppendLog("  Начертай точките в чертежа: не е отметнато — блокове не са начертани.");
                }
                if (exportFailure != null) AppendLog("  Справката е частично изпълнена: файлът не е записан.");
            }
            catch (Exception ex)
            {
                LogWarning($"Грешка при регистъра на сервитута: {ex.Message}");
                LogFailure("Servitude register", ex);
            }
        }

        /// <summary>
        /// Numbers only, no names: one line per boundary point (side, number, the two EKATTE codes, the distance to the nearest
        /// vertex, how many raw hits were merged into it, the gap or overlap between the two землища there) and one summary
        /// line per side. These go to the log file as well, since they are what tells a wrong boundary point from a right one.
        /// </summary>
        private void LogBoundaryDiagnostics(string side, List<NumberedServitudePoint> points, EdgeWalkStats stats, int mergedNeighbours)
        {
            foreach (NumberedServitudePoint point in points.Where(p => p.IsBoundary))
            {
                string between = point.SpanM <= 0
                    ? "без разстояние между землищата"
                    : $"{(point.IsOverlap ? "припокриване" : "празнина")} {point.SpanM:0.000} м";
                LogInfo($"  Гранична точка {side} {point.Number}: {string.Join("→", point.Ekattes)}, " +
                        $"до най-близкия възел {point.NearestVertexM:0.000} м, обединени попадения {point.MergedHits}, {between}.");
            }
            LogInfo($"  {side}: възли {stats.Vertices}, гранични точки {stats.BoundaryPoints}, обединени попадения {stats.MergedHits}, " +
                    $"закачени към възел пресичания {stats.SnappedCrossings}, слети общи възли {stats.SharedMerged}, " +
                    $"свързани без пресичане {stats.Bridged}, слети съседни точки {mergedNeighbours}.");
        }

        /// <summary>
        /// Points in no picked parcel. Those before the first and after the last included point do not use up numbers: counts
        /// only. A gap in the middle (the route leaves the picked parcels and comes back) does use up numbers, so those are named.
        /// </summary>
        private void LogPointsLeftOut(string side, List<NumberedServitudePoint> points, int atTheEnds)
        {
            if (atTheEnds > 0)
            {
                LogWarning($"{side}: {atTheEnds} точки извън избраните имоти не са включени.");
            }

            List<NumberedServitudePoint> gaps = points.Where(p => p.Ekattes.Count == 0).ToList();
            if (gaps.Count > 0)
            {
                LogWarning($"{side}: {gaps.Count} точки извън избраните имоти по трасето (номера " +
                           $"{string.Join(", ", ServitudeRegisterBuilder.NumberRanges(gaps))}) използват номера и не са включени.");
            }
        }

        /// <summary>The first number of one edge, from its box on the card; a value that is not a positive number falls back.</summary>
        private int ReadStartNumber(TextBox box, int fallback, string label)
        {
            if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0)
            {
                return value;
            }
            LogWarning($"\"{label}\" не е цяло положително число — използва се {fallback}.");
            box.Text = fallback.ToString(CultureInfo.InvariantCulture);
            return fallback;
        }

        /// <summary>
        /// Reads the servitude outline, the route axis, the pole positions and the parcels in one short transaction and
        /// splits the outline into its two edges. Null when the servitude or the axis is no longer in the drawing.
        /// </summary>
        private ServitudeEdgePoints? ReadServitudeEdgePoints(Document doc)
        {
            using (PerfTimer.Measure(_logger, "ServitudeGeometryReader.Read"))
            using (doc.LockDocument())
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Polyline? servitude = OpenPolyline(tr, _servitudeId);
                if (servitude == null)
                {
                    LogWarning("Избраният сервитут вече не е в чертежа — изберете го отново. Регистърът на сервитута не е създаден.");
                    tr.Commit();
                    return null;
                }

                var axis = new List<Curve>();
                foreach (ObjectId id in _axisIds)
                {
                    if (id.IsNull || id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is Curve curve) axis.Add(curve);
                }
                if (axis.Count == 0)
                {
                    LogWarning("Избраната ос на трасето вече не е в чертежа — изберете я отново. Регистърът на сервитута не е създаден.");
                    tr.Commit();
                    return null;
                }

                var poles = new List<(string Number, double X, double Y)>();
                foreach (PolePick pick in _polePicks)
                {
                    if (pick.BlockId.IsNull || pick.BlockId.IsErased) continue;
                    if (!(tr.GetObject(pick.BlockId, OpenMode.ForRead) is BlockReference blockRef)) continue;
                    (string number, double x, double y) = PoleFootprintExtractor.ReadNumberAndPosition(blockRef, tr);
                    poles.Add((PoleLabels.StripPrefix(string.IsNullOrEmpty(number) ? pick.Key : number), x, y));
                }

                ServitudeEdgePoints result = ServitudeGeometryReader.Read(
                    servitude, axis, poles, OpenParcels(tr), _chkReverseRoute.IsChecked == true);
                tr.Commit();
                return result;
            }
        }

        /// <summary>A SERV_TOCHKA block at every numbered point; a failure here never loses the xlsx already written.</summary>
        private void DrawServitudePoints(
            Document doc, List<NumberedServitudePoint> left, List<NumberedServitudePoint> right, PlanarPolygon? outline,
            bool routeReversed)
        {
            try
            {
                var placements = new List<ServitudeLabelPlacement>();
                var sides = new List<bool>();
                int movedToOtherSide = 0;
                // Only the points listed in the xlsx get a block: inside the picked parcels, with their final numbers
                foreach (NumberedServitudePoint point in ServitudeRegisterBuilder.PointsToDraw(left, right))
                {
                    // The label must sit OUTSIDE the servitude; one whose anchor would fall inside goes to the other side
                    // Worked out in the forward orientation of the route, so a point looks the same with "Обратна посока"
                    placements.Add(ServitudePointPlacement.PlaceInForwardOrientation(
                        point.Number, point.X, point.Y, point.Direction, point.IsLeft, routeReversed, outline, out bool moved));
                    if (moved) movedToOtherSide++;
                    sides.Add(point.IsLeft);
                }

                ServitudePointDrawResult result;
                using (PerfTimer.Measure(_logger, "ServitudePointBlockWriter.Draw"))
                {
                    result = ServitudePointBlockWriter.Draw(doc, placements, sides);
                }

                foreach (string failure in result.Failures) LogWarning($"Точка на сервитута не е начертана — {failure}");
                AppendLog($"  Точки на сервитута: {result.Drawn} блока {ServitudePointBlockNames.BlockName} начертани " +
                          $"(ляво {result.Left}, дясно {result.Right}); заменени {result.Replaced} от предишен пуск.");
                _logger?.LogInfo($"Servitude points: {result.Drawn} blocks drawn (left {result.Left}, right {result.Right}), {result.Replaced} replaced.");
                // The writer counted what is really in model space after the commit: the two numbers must agree
                AppendLog($"  Проверка: в моделното пространство има {result.FoundInModelSpace} блока {ServitudePointBlockNames.BlockName} " +
                          $"с етикет на плъгина (начертани {result.Drawn}). Активен лист: {(result.OnModelTab ? "Model" : result.ActiveLayout)}.");
                _logger?.LogInfo($"Servitude points verify: drawn {result.Drawn}, found in model space {result.FoundInModelSpace}, " +
                                 $"model tab {result.OnModelTab}, layout {result.ActiveLayout}.");
                if (result.VerifyNote != null) LogWarning(result.VerifyNote);
                else if (result.FoundInModelSpace != result.Drawn)
                {
                    LogWarning($"Начертани са {result.Drawn} блока, но в моделното пространство са намерени {result.FoundInModelSpace} — " +
                               "блоковете не са се запазили или не са в моделното пространство.");
                }
                if (result.LayerRestored != null) LogWarning(result.LayerRestored + ".");
                if (result.FrozenInViewports > 0)
                {
                    LogWarning($"Слоят {ServitudePointBlockNames.Layer} е замразен във {result.FrozenInViewports} изгледа (viewport) на лист " +
                               $"{result.ActiveLayout} — блоковете са в моделното пространство, но не се виждат там. " +
                               "Размразете слоя в изгледа (VPLAYER или Layer Properties) или отворете листа Model.");
                }
                if (movedToOtherSide > 0)
                {
                    AppendLog($"  {movedToOtherSide} етикета щяха да попаднат вътре в сервитута и са преместени от другата страна.");
                    _logger?.LogInfo($"Servitude point labels moved to the other side of the point: {movedToOtherSide}.");
                }
                if (result.DefinitionsUnlocked > 0)
                {
                    LogInfo($"  Определението на {ServitudePointBlockNames.BlockName} от по-ранна версия е отключено — " +
                            "текстът вече се движи и върти без точката.");
                }
                if (result.Replaced > 0)
                {
                    _logger?.LogSuccess($"Servitude points: {result.Replaced} plugin inserts of a previous run replaced.");
                }
                // The screen refresh runs inside the writer's document lock; if it fails the blocks are still drawn
                if (result.RefreshWarning != null) LogWarning(result.RefreshWarning);
            }
            catch (Exception ex)
            {
                LogWarning($"Точките на сервитута не са начертани: {ex.Message}");
                LogFailure("Servitude points", ex);
            }
        }

        /// <summary>A GBP032 block at every corner of the listed poles; a failure here never loses the xlsx already written.</summary>
        private void DrawPoleCornerBlocks(Document doc, List<CoordinateRegisterBlock> listed, List<PoleCorners> poleCorners)
        {
            try
            {
                var rotations = new Dictionary<string, double?>(StringComparer.Ordinal);
                foreach (PoleCorners pole in poleCorners) rotations[PoleLabels.StripPrefix(pole.PoleNumber)] = pole.LabelRotation;

                var sets = new List<PoleCornerSet>();
                var skipBlocks = new HashSet<string>(
                    poleCorners.Where(p => !p.DrawBlocks).Select(p => PoleLabels.StripPrefix(p.PoleNumber)), StringComparer.Ordinal);
                foreach (CoordinateRegisterBlock block in listed)
                {
                    if (skipBlocks.Contains(block.PoleNumber)) continue;
                    var corners = block.Corners.Select(c => (c.East, c.North)).ToList();
                    rotations.TryGetValue(block.PoleNumber, out double? rotation);
                    List<CornerLabelPlacement> placements = PoleCornerPlacement.Place(block.PoleNumber, corners, rotation, out double theta);
                    sets.Add(new PoleCornerSet { PoleNumber = block.PoleNumber, Theta = theta, Corners = placements });
                }

                string pluginDir = Path.GetDirectoryName(typeof(MainWindow).Assembly.Location) ?? string.Empty;
                PoleCornerDrawResult result;
                using (PerfTimer.Measure(_logger, "PoleCornerBlockWriter.Draw")) result = PoleCornerBlockWriter.Draw(doc, sets, pluginDir);

                if (result.Warning != null)
                {
                    LogWarning(result.Warning);
                    return;
                }
                foreach (string note in result.DefinitionNotes.Distinct())
                {
                    LogWarning($"Блок {PoleCornerBlockNames.BlockName}: {note} — вмъкването ще е изместено спрямо чупката.");
                }
                foreach (string failure in result.Failures) LogWarning($"Ъглова точка не е начертана — {failure}");
                AppendLog($"  Ъглови точки: {result.Drawn} блока {PoleCornerBlockNames.BlockName} начертани ({result.SkippedExisting} вече съществуващи пропуснати).");
                if (result.Replaced > 0)
                {
                    _logger?.LogSuccess($"Pole corner blocks: {result.Replaced} plugin inserts of a previous run replaced.");
                }
                // The screen refresh runs inside the writer's document lock; if it fails the blocks are still drawn
                if (result.RefreshWarning != null) LogWarning(result.RefreshWarning);
            }
            catch (Exception ex)
            {
                LogWarning($"Ъгловите точки не са начертани: {ex.Message}");
                LogFailure("Pole corner blocks", ex);
            }
        }

        /// <summary>
        /// Once per click: where the pole corner coordinates came from (exact field values or the text, which the drawing's
        /// UNITS precision rounds), one example field code, one warning for the poles read from rounded text, and the
        /// drawing's LUPREC when it is below 3.
        /// </summary>
        private void LogCoordinatePrecision(
            Document doc, List<string> roundedPoles, int polesFromFields, int polesWithFallback, int poleCount, string? fieldCodeSample)
        {
            // Window AND log file: the file is what gets sent back after a run
            if (poleCount > 0)
            {
                LogInfo($"  Координати на стъпките: {polesFromFields} от {poleCount} стълба са прочетени точно от полета, " +
                        $"{poleCount - polesFromFields} от текста.");
            }
            if (polesWithFallback > 0)
            {
                LogInfo($"  {polesWithFallback} стълба: полетата на P-етикетите са преизчислени временно с LUPREC " +
                        $"{PoleAttributeValues.FallbackPrecision}; LUPREC и текстовете са върнати както бяха.");
            }
            if (fieldCodeSample != null)
            {
                LogInfo($"  Пример за поле на P-етикет: {fieldCodeSample}");
            }
            if (roundedPoles.Count > 0)
            {
                LogWarning($"{roundedPoles.Count} стълба имат координати с по-малко от {CoordinateText.FullPrecisionDecimals} знака след запетаята " +
                           $"(напр. стълб {roundedPoles[0]}). Задайте UNITS → Precision 0.000, изпълнете REGEN/UPDATEFIELD и пуснете отново.");
            }
            int luprec = doc.Database.Luprec;
            if (luprec < CoordinateText.FullPrecisionDecimals && poleCount > 0)
            {
                LogWarning($"Точността на единиците на чертежа (LUPREC) е {luprec}, по-малка от {CoordinateText.FullPrecisionDecimals}.");
            }
        }

        /// <summary>
        /// Once per click, first picked pole only, to the log file as DEBUG: what its P-tags really hold (text, field, child fields,
        /// and for a dynamic block the attribute definitions). Read-only; it never fails the click.
        /// </summary>
        private void LogPoleFieldDiagnostic(Transaction tr)
        {
            try
            {
                PolePick? first = _polePicks.FirstOrDefault(p => !p.BlockId.IsNull && !p.BlockId.IsErased);
                if (first == null || !(tr.GetObject(first.BlockId, OpenMode.ForRead) is BlockReference blockRef)) return;

                _logger?.LogDebug($"P-tag diagnostic, pole {first.Key}, dynamic={blockRef.IsDynamicBlock}, LUPREC={blockRef.Database.Luprec}");
                foreach (string line in PoleAttributeValues.Describe(blockRef, tr)) _logger?.LogDebug(line);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug($"P-tag diagnostic failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private const string DefaultRegisterProject = "НОВА ВЛ 110kV";

        /// <summary>The P-tag corners must be the footprint polyline's vertices to within this (m).</summary>
        private const double CornerMatchToleranceM = 0.001;

        /// <summary>
        /// Writes a failure to the log file: one line when an output file is open in another program (the window already shows
        /// the message), otherwise the message with its stack trace.
        /// </summary>
        private void LogFailure(string what, Exception ex)
        {
            if (ex is FileInUseException) _logger?.LogError($"{what} failed: {ex.Message}");
            else _logger?.LogError($"{what} failed: {ex.Message}\n{ex.StackTrace}");
        }

        /// <summary>Writes a line to the window log and, as INFO, to the log file.</summary>
        private void LogInfo(string message)
        {
            AppendLog(message);
            _logger?.LogInfo(message.Trim());
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
