// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Monitor.Desktop;

// Reference-device layout: uninterrupted traces, a narrow numeric column and
// a lower strip shared by measurement history and the large cuff reading.
internal sealed class BedsideMonitorArea : Grid
{
    private readonly UniformGrid _extras;
    private readonly MonitorManualTile[] _manualTiles;

    internal BedsideMonitorArea(MonitorSkinContent content)
    {
        RowDefinitions = new("*,118");
        ColumnDefinitions = new("68*,32*");
        Children.Add(content.Waveforms);
        SetColumn(content.Numerics, 1);
        Children.Add(content.Numerics);
        var footer = new Grid { ColumnDefinitions = new("51*,49*") };
        SetRow(footer, 1);
        SetColumnSpan(footer, 2);
        Children.Add(footer);
        var history = new Grid { RowDefinitions = new("*,Auto") };
        if (content.Trends is not null) { history.Children.Add(content.Trends); }
        _extras = new UniformGrid { Columns = 3, Height = 64 };
        _extras.Children.Add(content.Temperature);
        _extras.Children.Add(content.Custom1);
        _extras.Children.Add(content.Custom2);
        _manualTiles = _extras.Children.OfType<MonitorManualTile>().ToArray();
        foreach (var tile in _manualTiles) { tile.ReadingChanged += UpdateExtras; }
        SetRow(_extras, 1);
        history.Children.Add(_extras);
        footer.Children.Add(history);
        SetColumn(content.Nibp, 1);
        footer.Children.Add(content.Nibp);
        UpdateExtras();
    }

    private void UpdateExtras()
    {
        bool visible = _manualTiles.Any(tile => tile.IsVisible && tile.Reading.Length > 0);
        _extras.IsVisible = visible;
        RowDefinitions[1].Height = new GridLength(visible ? 182 : 118);
    }
}
