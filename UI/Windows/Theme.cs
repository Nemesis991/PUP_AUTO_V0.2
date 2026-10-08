using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace PUP_AUTO.UI.Windows
{
    /// <summary>
    /// Colors and control styles of the PUP_AUTO window: a neutral dark theme with rounded controls.
    /// The styles are XAML parsed at runtime (the plugin has no compiled XAML); if parsing fails the
    /// window still opens with the default WPF look (see <see cref="MainWindow"/>).
    /// </summary>
    internal static class Theme
    {
        public const string Bg            = "#16181D";
        public const string Card          = "#1E2128";
        public const string CardBorder    = "#2B2F38";
        public const string Surface       = "#262A33";
        public const string SurfaceBorder = "#3A3F4B";
        public const string Text          = "#E6E9EF";
        public const string Subtext       = "#A9B2C0";
        public const string Accent        = "#4C8DFF";
        public const string AccentSoft    = "#1D2B45";
        public const string Success       = "#3FB950";
        public const string SuccessSoft   = "#16301F";
        public const string Warning       = "#D29922";
        public const string Error         = "#F85149";
        public const string Scroll        = "#4A505C";

        public static readonly SolidColorBrush BgBrush            = B(Bg);
        public static readonly SolidColorBrush CardBrush          = B(Card);
        public static readonly SolidColorBrush CardBorderBrush    = B(CardBorder);
        public static readonly SolidColorBrush SurfaceBrush       = B(Surface);
        public static readonly SolidColorBrush SurfaceBorderBrush = B(SurfaceBorder);
        public static readonly SolidColorBrush TextBrush          = B(Text);
        public static readonly SolidColorBrush SubtextBrush       = B(Subtext);
        public static readonly SolidColorBrush AccentBrush        = B(Accent);
        public static readonly SolidColorBrush AccentSoftBrush    = B(AccentSoft);
        public static readonly SolidColorBrush SuccessBrush       = B(Success);
        public static readonly SolidColorBrush SuccessSoftBrush   = B(SuccessSoft);
        public static readonly SolidColorBrush WarningBrush       = B(Warning);
        public static readonly SolidColorBrush ErrorBrush         = B(Error);

        /// <summary>Segoe UI Variable on Windows 11, Segoe UI elsewhere.</summary>
        public static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");

        /// <summary>Icon font shipped with Windows 10 and 11.</summary>
        public static readonly FontFamily IconFont = new FontFamily("Segoe MDL2 Assets");

        public static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas");

        // Segoe MDL2 Assets glyphs
        public const string GlyphOpenFile = "";
        public const string GlyphFolder   = "";
        public const string GlyphLayers   = "";
        public const string GlyphPin      = "";
        public const string GlyphParcels  = "";
        public const string GlyphPlay     = "";
        public const string GlyphCheck    = "\uE73E";   // CheckMark

        private static SolidColorBrush B(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        /// <summary>Implicit styles (Button, TextBox, CheckBox, ScrollBar, ToolTip) and the keyed button styles.</summary>
        public static ResourceDictionary LoadStyles()
        {
            string xaml = StylesXaml
                .Replace("%BG%", Bg)
                .Replace("%CARD%", Card)
                .Replace("%SURFACE_BORDER%", SurfaceBorder)
                .Replace("%SURFACE%", Surface)
                .Replace("%TEXT%", Text)
                .Replace("%SUBTEXT%", Subtext)
                .Replace("%ACCENT%", Accent)
                .Replace("%SCROLL%", Scroll);
            return (ResourceDictionary)XamlReader.Parse(xaml);
        }

        public const string PrimaryButton = "PrimaryButton";
        public const string GhostButton   = "GhostButton";
        public const string TileButton    = "TileButton";

        private const string StylesXaml = @"
<ResourceDictionary xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
                    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">

  <Style TargetType=""ToolTip"">
    <Setter Property=""Background"" Value=""%CARD%""/>
    <Setter Property=""Foreground"" Value=""%TEXT%""/>
    <Setter Property=""BorderBrush"" Value=""%SURFACE_BORDER%""/>
    <Setter Property=""Padding"" Value=""8,4""/>
  </Style>

  <Style TargetType=""Button"">
    <Setter Property=""Background"" Value=""%SURFACE%""/>
    <Setter Property=""Foreground"" Value=""%TEXT%""/>
    <Setter Property=""BorderBrush"" Value=""%SURFACE_BORDER%""/>
    <Setter Property=""BorderThickness"" Value=""1""/>
    <Setter Property=""Padding"" Value=""14,7""/>
    <Setter Property=""FontSize"" Value=""13""/>
    <Setter Property=""Cursor"" Value=""Hand""/>
    <Setter Property=""HorizontalContentAlignment"" Value=""Center""/>
    <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
    <Setter Property=""FocusVisualStyle"" Value=""{x:Null}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""Button"">
          <Grid>
            <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                    BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""8""/>
            <Border x:Name=""overlay"" Background=""#FFFFFF"" Opacity=""0"" CornerRadius=""8""/>
            <ContentPresenter Margin=""{TemplateBinding Padding}""
                              HorizontalAlignment=""{TemplateBinding HorizontalContentAlignment}""
                              VerticalAlignment=""{TemplateBinding VerticalContentAlignment}""/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""overlay"" Property=""Opacity"" Value=""0.07""/>
            </Trigger>
            <Trigger Property=""IsPressed"" Value=""True"">
              <Setter TargetName=""overlay"" Property=""Opacity"" Value=""0.14""/>
            </Trigger>
            <Trigger Property=""IsKeyboardFocused"" Value=""True"">
              <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""%ACCENT%""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Opacity"" Value=""0.4""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key=""PrimaryButton"" TargetType=""Button"" BasedOn=""{StaticResource {x:Type Button}}"">
    <Setter Property=""Background"" Value=""%ACCENT%""/>
    <Setter Property=""BorderBrush"" Value=""%ACCENT%""/>
    <Setter Property=""Foreground"" Value=""#FFFFFF""/>
    <Setter Property=""FontWeight"" Value=""SemiBold""/>
    <Setter Property=""FontSize"" Value=""14""/>
    <Setter Property=""Padding"" Value=""20,11""/>
  </Style>

  <Style x:Key=""GhostButton"" TargetType=""Button"" BasedOn=""{StaticResource {x:Type Button}}"">
    <Setter Property=""Background"" Value=""Transparent""/>
    <Setter Property=""BorderBrush"" Value=""Transparent""/>
    <Setter Property=""Foreground"" Value=""%SUBTEXT%""/>
    <Setter Property=""FontSize"" Value=""12""/>
    <Setter Property=""Padding"" Value=""10,4""/>
  </Style>

  <Style x:Key=""TileButton"" TargetType=""Button"" BasedOn=""{StaticResource {x:Type Button}}"">
    <Setter Property=""HorizontalContentAlignment"" Value=""Stretch""/>
    <Setter Property=""Padding"" Value=""14,12""/>
  </Style>

  <Style TargetType=""TextBox"">
    <Setter Property=""Background"" Value=""%SURFACE%""/>
    <Setter Property=""Foreground"" Value=""%TEXT%""/>
    <Setter Property=""BorderBrush"" Value=""%SURFACE_BORDER%""/>
    <Setter Property=""BorderThickness"" Value=""1""/>
    <Setter Property=""Padding"" Value=""8,6""/>
    <Setter Property=""FontSize"" Value=""13""/>
    <Setter Property=""CaretBrush"" Value=""%TEXT%""/>
    <Setter Property=""SelectionBrush"" Value=""%ACCENT%""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""TextBox"">
          <Border x:Name=""bd"" Background=""{TemplateBinding Background}"" BorderBrush=""{TemplateBinding BorderBrush}""
                  BorderThickness=""{TemplateBinding BorderThickness}"" CornerRadius=""6"">
            <ScrollViewer x:Name=""PART_ContentHost"" Margin=""{TemplateBinding Padding}"" Focusable=""False""/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsKeyboardFocused"" Value=""True"">
              <Setter TargetName=""bd"" Property=""BorderBrush"" Value=""%ACCENT%""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType=""CheckBox"">
    <Setter Property=""Foreground"" Value=""%TEXT%""/>
    <Setter Property=""Cursor"" Value=""Hand""/>
    <Setter Property=""FocusVisualStyle"" Value=""{x:Null}""/>
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""CheckBox"">
          <Grid Background=""Transparent"">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width=""Auto""/>
              <ColumnDefinition Width=""*""/>
            </Grid.ColumnDefinitions>
            <Border x:Name=""box"" Width=""20"" Height=""20"" CornerRadius=""5"" BorderThickness=""1.5""
                    BorderBrush=""%SURFACE_BORDER%"" Background=""%SURFACE%"" VerticalAlignment=""Top"" Margin=""0,1,0,0"">
              <Path x:Name=""mark"" Data=""M 4,9 L 7.5,12.5 L 13.5,5"" Stroke=""#FFFFFF"" StrokeThickness=""2""
                    StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round"" StrokeLineJoin=""Round"" Visibility=""Collapsed""/>
            </Border>
            <ContentPresenter Grid.Column=""1"" Margin=""12,0,0,0"" VerticalAlignment=""Top""/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property=""IsMouseOver"" Value=""True"">
              <Setter TargetName=""box"" Property=""BorderBrush"" Value=""%ACCENT%""/>
            </Trigger>
            <Trigger Property=""IsKeyboardFocused"" Value=""True"">
              <Setter TargetName=""box"" Property=""BorderBrush"" Value=""%ACCENT%""/>
            </Trigger>
            <Trigger Property=""IsChecked"" Value=""True"">
              <Setter TargetName=""box"" Property=""Background"" Value=""%ACCENT%""/>
              <Setter TargetName=""box"" Property=""BorderBrush"" Value=""%ACCENT%""/>
              <Setter TargetName=""mark"" Property=""Visibility"" Value=""Visible""/>
            </Trigger>
            <Trigger Property=""IsEnabled"" Value=""False"">
              <Setter Property=""Opacity"" Value=""0.4""/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key=""ScrollThumb"" TargetType=""Thumb"">
    <Setter Property=""Template"">
      <Setter.Value>
        <ControlTemplate TargetType=""Thumb"">
          <Border CornerRadius=""4"" Background=""%SCROLL%"" Margin=""2""/>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <ControlTemplate x:Key=""VerticalScrollBar"" TargetType=""ScrollBar"">
    <Grid Background=""Transparent"">
      <Track x:Name=""PART_Track"" Orientation=""Vertical"" IsDirectionReversed=""True"">
        <Track.Thumb>
          <Thumb Style=""{StaticResource ScrollThumb}""/>
        </Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>

  <ControlTemplate x:Key=""HorizontalScrollBar"" TargetType=""ScrollBar"">
    <Grid Background=""Transparent"">
      <Track x:Name=""PART_Track"" Orientation=""Horizontal"">
        <Track.Thumb>
          <Thumb Style=""{StaticResource ScrollThumb}""/>
        </Track.Thumb>
      </Track>
    </Grid>
  </ControlTemplate>

  <Style TargetType=""ScrollBar"">
    <Setter Property=""Width"" Value=""10""/>
    <Setter Property=""MinWidth"" Value=""10""/>
    <Setter Property=""Template"" Value=""{StaticResource VerticalScrollBar}""/>
    <Style.Triggers>
      <Trigger Property=""Orientation"" Value=""Horizontal"">
        <Setter Property=""Width"" Value=""Auto""/>
        <Setter Property=""MinWidth"" Value=""0""/>
        <Setter Property=""Height"" Value=""10""/>
        <Setter Property=""MinHeight"" Value=""10""/>
        <Setter Property=""Template"" Value=""{StaticResource HorizontalScrollBar}""/>
      </Trigger>
    </Style.Triggers>
  </Style>

</ResourceDictionary>";
    }
}
