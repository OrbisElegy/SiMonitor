// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Monitor.Desktop;

internal sealed class MonitorManualTile : Border
{
    private readonly TextBlock _caption = new() { FontSize = 14, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _value = new() { FontSize = 30, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White };
    private readonly TextBlock _detail = new() { FontSize = 12, Foreground = Brush.Parse("#BAC5D1") };
    internal string Reading => $"{_caption.Text} {_value.Text} {_detail.Text}".Trim();

    internal MonitorManualTile()
    {
        Padding = new Thickness(10, 8);
        BorderThickness = new Thickness(0, 1, 0, 0);
        BorderBrush = Brush.Parse("#34414F");
        ClipToBounds = true;
        var panel = new Grid { RowDefinitions = new("Auto,*,Auto") };
        panel.Children.Add(_caption);
        var value = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = _value
        };
        Grid.SetRow(value, 1);
        panel.Children.Add(value);
        Grid.SetRow(_detail, 2);
        panel.Children.Add(_detail);
        _detail.TextTrimming = TextTrimming.CharacterEllipsis;
        Child = panel;
    }

    internal void Clear()
    {
        Update("", "", "");
    }

    internal void Update(string caption, string value, string detail)
    {
        _caption.Text = caption;
        _value.Text = value;
        _detail.Text = detail;
        ToolTip.SetTip(this, Reading);
        AutomationProperties.SetName(this, Reading);
    }
}
