// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

// The same draft controls reflow from a table into labeled rows; resizing never
// rebuilds inputs, resets values or changes the active monitor configuration.
internal sealed class SettingsDisplayTable : UserControl
{
    private const string Columns = "40,180,100,*,*,128";
    private readonly DesktopLocalization _localization;
    private readonly Grid _headings = new() { ColumnDefinitions = new(Columns) };
    private readonly StackPanel _rows = new() { Spacing = 12 };

    internal SettingsDisplayTable(DesktopLocalization localization)
    {
        _localization = localization;
        string[] labels = ["display.row", "display.channel", "display.range", "display.minimum", "display.maximum", "display.speed"];
        for (int column = 0; column < labels.Length; column++)
        {
            var heading = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) };
            localization.Bind(heading, TextBlock.TextProperty, labels[column]);
            Grid.SetColumn(heading, column);
            _headings.Children.Add(heading);
        }
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(_headings);
        panel.Children.Add(_rows);
        Content = panel;
    }

    internal void ClearRows() => _rows.Children.Clear();

    internal void AddRow(int number, DesignPreviewSettings.SlotEditor editor) =>
        _rows.Children.Add(new DisplayRow(_localization, number, editor));

    protected override Size MeasureOverride(Size availableSize)
    {
        bool compact = availableSize.Width < 720;
        _headings.IsVisible = !compact;
        foreach (var row in _rows.Children.OfType<DisplayRow>()) { row.Adapt(compact); }
        return base.MeasureOverride(availableSize);
    }

    private sealed class DisplayRow : Grid
    {
        private readonly TextBlock _number;
        private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        private readonly TextBlock _error = new() { Foreground = Brush.Parse("#B42318"), TextWrapping = TextWrapping.Wrap, IsVisible = false };
        private readonly StackPanel[] _fields;
        private readonly Label[] _labels;
        private bool? _compact;

        internal DisplayRow(DesktopLocalization localization, int number, DesignPreviewSettings.SlotEditor editor)
        {
            _number = new TextBlock { Text = number.ToString(CultureInfo.InvariantCulture), VerticalAlignment = VerticalAlignment.Center };
            localization.Bind(_title, TextBlock.TextProperty, "display.rowTitle", number);
            Children.Add(_number);
            Children.Add(_title);
            Control[] inputs = [editor.Channel, editor.Auto, editor.Minimum, editor.Maximum, editor.Speed];
            string[] labels = ["display.channel", "display.range", "display.minimum", "display.maximum", "display.speed"];
            _fields = new StackPanel[inputs.Length];
            _labels = new Label[inputs.Length];
            for (int index = 0; index < inputs.Length; index++)
            {
                var caption = new TextBlock { TextWrapping = TextWrapping.Wrap };
                localization.Bind(caption, TextBlock.TextProperty, labels[index]);
                _labels[index] = new Label { Target = inputs[index], Content = caption, Padding = new Thickness(0) };
                _fields[index] = new StackPanel { Spacing = 6 };
                _fields[index].Children.Add(_labels[index]);
                _fields[index].Children.Add(inputs[index]);
                inputs[index].HorizontalAlignment = HorizontalAlignment.Stretch;
                Children.Add(_fields[index]);
            }
            Children.Add(_error);
            void RefreshValidation()
            {
                bool valid = ValidRange(editor);
                string key = editor.Auto.IsChecked == true ? "display.invalidAutomaticRange" : "display.invalidRange";
                _error.IsVisible = !valid;
                localization.Bind(_error, TextBlock.TextProperty, key);
                foreach (var input in new[] { editor.Minimum, editor.Maximum })
                {
                    localization.Bind(input, AutomationProperties.HelpTextProperty, text => valid ? "" : text.GetString(key));
                }
            }
            editor.Minimum.TextChanged += (_, _) => RefreshValidation();
            editor.Maximum.TextChanged += (_, _) => RefreshValidation();
            editor.Auto.IsCheckedChanged += (_, _) => RefreshValidation();
            RefreshValidation();
        }

        internal void Adapt(bool compact)
        {
            if (_compact == compact) { return; }
            _compact = compact;
            ColumnDefinitions = new(compact ? "*,*" : Columns);
            RowDefinitions = new(compact ? "Auto,Auto,Auto,Auto,Auto" : "Auto,Auto");
            _number.IsVisible = !compact;
            _title.IsVisible = compact;
            SetColumnSpan(_title, 2);
            _title.Margin = new Thickness(0, 0, 0, 10);
            // The table and compact form have the same reading and tab order.
            for (int index = 0; index < _fields.Length; index++)
            {
                _labels[index].IsVisible = compact && index != 1;
                SetColumn(_fields[index], compact ? index switch { 1 => 1, 3 => 1, _ => 0 } : index + 1);
                SetRow(_fields[index], compact ? index switch { 0 or 1 => 1, 2 or 3 => 2, _ => 3 } : 0);
                _fields[index].Margin = new Thickness(0, 0, compact && index is 1 or 3 ? 0 : 10, compact ? 12 : 0);
                _fields[index].VerticalAlignment = index == 1 ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            }
            SetRow(_error, compact ? 4 : 1);
            SetColumn(_error, compact ? 0 : 1);
            SetColumnSpan(_error, compact ? 2 : 5);
            _error.Margin = new Thickness(0, 6, 0, 0);
        }

        private static bool ValidRange(DesignPreviewSettings.SlotEditor editor)
        {
            if (!double.TryParse(editor.Minimum.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double minimum) ||
                !double.TryParse(editor.Maximum.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double maximum))
            { return false; }
            try { new MonitorAmplitudeRange(minimum, maximum).Validate(); }
            catch (ArgumentException) { return false; }
            return true;
        }
    }
}
