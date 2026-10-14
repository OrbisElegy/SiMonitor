// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Monitor.Desktop;

// Scale the complete numeric presentation, including labels, secondary readings
// and alarm backgrounds. Waveforms retain their independent time/voltage mapping.
internal sealed class GenericMonitorArea : Grid
{
    private readonly LayoutTransformControl[] _numericSurfaces;
    private readonly Func<int> _waveformRowCount;
    private readonly RowSeparators _separators;
    private double _scale = 1;

    internal GenericMonitorArea(MonitorSkinContent content)
    {
        _waveformRowCount = content.WaveformRowCount;
        RowDefinitions = new("*,100");
        ColumnDefinitions = new("*,200");
        Children.Add(content.Waveforms);
        var extras = new UniformGrid { Columns = 3 };
        extras.Children.Add(content.Temperature);
        extras.Children.Add(content.Custom1);
        extras.Children.Add(content.Custom2);
        _numericSurfaces = [Wrap(content.Numerics, 0, 1), Wrap(extras, 1, 0), Wrap(content.Nibp, 1, 1)];
        // Draw once over both columns, outside the numeric scaling transform.
        _separators = new RowSeparators(content) { IsHitTestVisible = false };
        SetColumnSpan(_separators, 2);
        Children.Add(_separators);
    }

    internal void RefreshLayout()
    {
        InvalidateMeasure();
        _separators.InvalidateVisual();
    }

    private LayoutTransformControl Wrap(Control content, int row, int column)
    {
        // Composition positions the wrapper; its child uses the wrapper's local coordinates.
        var surface = new LayoutTransformControl { Child = content };
        SetRow(surface, row);
        SetColumn(surface, column);
        Children.Add(surface);
        return surface;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Compact-monitor baseline in logical pixels; Avalonia applies display DPI separately.
        // Both dimensions constrain scaling, including below the baseline in short windows.
        double referenceHeight = 100 * (Math.Max(4, _waveformRowCount()) + 1);
        double scale = Math.Min(availableSize.Width / 500, availableSize.Height / referenceHeight);
        scale = double.IsFinite(scale) ? Math.Max(.5, scale) : 1;
        if (Math.Abs(scale - _scale) > .001)
        {
            _scale = scale;
            ColumnDefinitions[1].Width = new GridLength(200 * scale);
            RowDefinitions[1].Height = new GridLength(100 * scale);
            foreach (var surface in _numericSurfaces)
            {
                surface.LayoutTransform = new ScaleTransform(scale, scale);
            }
        }
        return base.MeasureOverride(availableSize);
    }

    private sealed class RowSeparators(MonitorSkinContent content) : Control
    {
        private static readonly IBrush Stroke = Brush.Parse("#607080");

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            int rows = content.WaveformRowCount();
            for (int row = 1; row <= rows; row++)
            {
                double left = content.TwelveLeadWaveforms.IsVisible && row < rows ? content.Waveforms.Bounds.Width : 0;
                double bottom = Math.Floor(Bounds.Height * row / rows);
                context.FillRectangle(Stroke, new Rect(left, Math.Max(0, bottom - 1), Math.Max(0, Bounds.Width - left), 1));
            }
        }
    }
}
