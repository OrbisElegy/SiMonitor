// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class MonitorManualTile : Border
{
    private readonly TextBlock _caption = new() { FontSize = 14, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _value = new() { FontSize = 30, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock _detail = new() { FontSize = 12, Foreground = Brush.Parse("#BAC5D1") };
    private readonly TextBlock _auxiliary = new() { FontSize = 38 };
    private readonly TextBlock _unit = new() { Text = "mmHg", FontSize = 10 };
    internal BedsideNibpCountdown Countdown { get; } = new();
    private readonly Viewbox _auxiliaryBox;
    private readonly Viewbox _valueBox;
    private readonly LayoutTransformControl _condensedValue = new() { LayoutTransform = new ScaleTransform(.66, 1) };
    private readonly Grid _panel = new();
    private readonly Grid _numbers = new() { ColumnDefinitions = new("*,Auto") };
    private readonly StackPanel _labels = new() { Spacing = 1 };
    private bool _showAuxiliary;
    internal event Action? ReadingChanged;
    internal string Reading => $"{_caption.Text} {_value.Text}{(_auxiliary.Text is { Length: > 0 } mean ? " " + mean : "")} {_detail.Text}".Trim();
    internal void SetAppearance(IBrush color, double fontSize, bool showAuxiliary = false)
    {
        _caption.Foreground = _value.Foreground = _detail.Foreground = _auxiliary.Foreground = color;
        _unit.Foreground = color;
        Countdown.SetColor(color);
        _value.FontSize = showAuxiliary ? 112 : fontSize;
        _value.FontWeight = fontSize > 30 ? FontWeight.Normal : FontWeight.SemiBold;
        _showAuxiliary = showAuxiliary;
        if (showAuxiliary)
        {
            _value.FontWeight = FontWeight.Normal;
            _caption.FontSize = 13;
            _detail.FontSize = 10;
            _value.ClearValue(TextBlock.LineHeightProperty);
            _value.FontFamily = _auxiliary.FontFamily = new FontFamily("Arial, Liberation Sans, sans-serif");
            _auxiliary.FontSize = 40;
        }
        else
        {
            _caption.FontSize = 14;
            _detail.FontSize = 12;
            _auxiliary.FontSize = 38;
            _value.ClearValue(TextBlock.LineHeightProperty);
            _value.ClearValue(TextBlock.FontFamilyProperty);
            _auxiliary.ClearValue(TextBlock.FontFamilyProperty);
        }
        ArrangeContent();
    }

    internal MonitorManualTile()
    {
        ClipToBounds = true;
        _valueBox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _auxiliaryBox = new Viewbox { Child = _auxiliary, Margin = new Thickness(10, 0, 0, 0), Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, IsVisible = false };
        _detail.TextTrimming = TextTrimming.CharacterEllipsis;
        ArrangeContent();
    }

    private void ArrangeContent()
    {
        // Release the shared controls before switching between the two layouts.
        Child = null;
        _panel.Children.Clear();
        _numbers.Children.Clear();
        _labels.Children.Clear();
        Countdown.Detail = null;
        _valueBox.Child = null;
        _condensedValue.Child = null;
        Padding = _showAuxiliary ? new Thickness(4, 0, 4, 0) : new Thickness(10, 8);
        Height = _showAuxiliary ? 118 : double.NaN;
        _panel.RowDefinitions = new(_showAuxiliary ? "18,*" : "Auto,*,Auto");
        _panel.ColumnDefinitions = new(_showAuxiliary ? "40,*" : "*");
        _numbers.Children.Add(_valueBox);
        Grid.SetColumn(_auxiliaryBox, 1);
        _numbers.Children.Add(_auxiliaryBox);
        _auxiliaryBox.IsVisible = _showAuxiliary && _auxiliary.Text is { Length: > 0 };
        Grid.SetRow(_numbers, 1);
        Grid.SetColumn(_numbers, _showAuxiliary ? 1 : 0);
        _panel.Children.Add(_numbers);
        if (_showAuxiliary)
        {
            _condensedValue.Child = _value;
            _valueBox.Child = _condensedValue;
            _valueBox.HorizontalAlignment = HorizontalAlignment.Center;
            _valueBox.Margin = new Thickness(0, 2);
            _labels.Children.Add(_caption);
            _unit.IsVisible = _caption.Text is { Length: > 0 };
            _labels.Children.Add(_unit);
            Grid.SetRow(_labels, 1);
            _panel.Children.Add(_labels);
            _auxiliaryBox.VerticalAlignment = VerticalAlignment.Bottom;
            _auxiliaryBox.Margin = new Thickness(8, 0, 24, 13);
            _detail.VerticalAlignment = VerticalAlignment.Top;
            Countdown.Detail = _detail;
            Grid.SetColumn(Countdown, 1);
            _panel.Children.Add(Countdown);
        }
        else
        {
            _valueBox.Child = _value;
            _valueBox.HorizontalAlignment = HorizontalAlignment.Left;
            _valueBox.Margin = default;
            _panel.Children.Add(_caption);
            _auxiliaryBox.VerticalAlignment = VerticalAlignment.Stretch;
            _auxiliaryBox.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(_detail, 0);
            Grid.SetRow(_detail, 2);
            _detail.VerticalAlignment = VerticalAlignment.Stretch;
            _panel.Children.Add(_detail);
        }
        Child = _panel;
    }

    internal void Clear()
    {
        Countdown.Clear();
        Update("", "", "");
    }

    internal void UpdateNibpCountdown(NibpMeasurement measurement, long simTimeNs, DesktopLocalization localization)
    {
        if (_showAuxiliary) { Countdown.Update(measurement, simTimeNs, localization); }
        else { Countdown.Clear(); }
    }

    internal void Update(string caption, string value, string detail, string? auxiliary = null)
    {
        string previous = Reading;
        _caption.Text = caption;
        _value.Text = value;
        _detail.Text = detail;
        _auxiliary.Text = _showAuxiliary ? auxiliary : null;
        _auxiliaryBox.IsVisible = _auxiliary.Text is { Length: > 0 };
        _unit.IsVisible = _showAuxiliary && caption.Length > 0;
        string reading = Reading;
        ToolTip.SetTip(this, reading);
        AutomationProperties.SetName(this, reading);
        if (!string.Equals(previous, reading, StringComparison.Ordinal)) { ReadingChanged?.Invoke(); }
    }
}
