// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Monitor.Desktop;

internal enum BedsideToolbarIcon
{
    More,
    Nibp,
    NibpStop,
    NibpAuto,
    Patient,
    Layout,
    Alarm,
    Review,
    Standby,
    Menu,
    Ecg,
    Lead,
    Print,
    Silence
}

internal sealed class BedsideToolbar : UniformGrid
{
    internal BedsideToolbar(IReadOnlyList<(Button Button, BedsideToolbarIcon Icon)> actions)
    {
        if (actions.Count == 0) { throw new ArgumentException("At least one toolbar action is required.", nameof(actions)); }
        Columns = actions.Count;
        Rows = 1;
        Height = 58;
        MinHeight = 44;
        Background = Brush.Parse("#151D2B");
        foreach (var (button, iconKind) in actions)
        {
            button.MinWidth = 0;
            button.MinHeight = 44;
            button.Margin = new Thickness(0);
            button.Padding = new Thickness(3, 29, 3, 7);
            button.CornerRadius = new CornerRadius(2);
            button.BorderThickness = new Thickness(0.5);
            button.BorderBrush = Brush.Parse("#151E2D");
            button.Background = KeyBackground();
            button.Foreground = Brush.Parse("#D0E7F8");
            button.FontSize = 12;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.VerticalAlignment = VerticalAlignment.Stretch;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.VerticalContentAlignment = VerticalAlignment.Bottom;
            if (button.Content is TextBlock caption)
            {
                caption.FontSize = 12;
                caption.TextWrapping = TextWrapping.NoWrap;
                caption.TextTrimming = TextTrimming.CharacterEllipsis;
                caption.TextAlignment = TextAlignment.Center;
            }

            var icon = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse(PathData(iconKind)),
                Stroke = Brush.Parse("#D5EFFC"),
                StrokeThickness = 1.4,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Width = 20,
                Height = 20,
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                IsHitTestVisible = false,
                Opacity = button.IsEnabled ? 1 : 0.4
            };
            button.PropertyChanged += (_, change) =>
            {
                if (change.Property == IsEnabledProperty) { icon.Opacity = button.IsEnabled ? 1 : 0.4; }
            };

            // Keep the actual button and its original caption together: the host updates
            // TextBlock content directly, and pointer / keyboard actions remain on the button.
            var key = new Panel();
            key.Children.Add(button);
            key.Children.Add(icon);
            Children.Add(key);
        }
    }

    internal static IBrush KeyBackground(bool selected = false) => new LinearGradientBrush
    {
        StartPoint = RelativePoint.TopLeft,
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = new GradientStops
        {
            new(Color.Parse(selected ? "#356889" : "#3A4B65"), 0),
            new(Color.Parse(selected ? "#28516D" : "#2B394F"), 0.48),
            new(Color.Parse(selected ? "#1A3D59" : "#202B3F"), 1)
        }
    };

    private static string PathData(BedsideToolbarIcon icon) => icon switch
    {
        BedsideToolbarIcon.More =>
            "M 12,3 A 9,9 0 1 0 12,21 A 9,9 0 1 0 12,3 M 3,12 H 21 M 5,7.5 H 19 M 5,16.5 H 19 M 12,3 C 7,8 7,16 12,21 C 17,16 17,8 12,3",
        BedsideToolbarIcon.Nibp =>
            "M 4,11 L 8,8 L 15,16 L 11,19 Z M 7,9 L 13,17 M 14,17 C 20,20 22,16 19,13 L 16,10 M 18,3 A 3,3 0 1 0 18,9 A 3,3 0 1 0 18,3 M 18,6 L 19,5",
        BedsideToolbarIcon.NibpStop =>
            "M 3,12 L 7,9 L 14,17 L 10,20 Z M 6,10 L 12,18 M 14,17 C 20,20 22,16 19,13 M 16,3 H 22 V 9 H 16 Z",
        BedsideToolbarIcon.NibpAuto =>
            "M 3,12 L 7,9 L 14,17 L 10,20 Z M 6,10 L 12,18 M 14,17 C 20,20 22,16 19,13 M 14,6 A 4,4 0 1 1 18,10 M 14,3 V 6 H 17",
        BedsideToolbarIcon.Patient =>
            "M 12,3 A 3,3 0 1 0 12,9 A 3,3 0 1 0 12,3 M 6,21 V 15 C 6,9 18,9 18,15 V 21 M 9,14 V 21 M 15,14 V 21 M 4,21 H 20",
        BedsideToolbarIcon.Layout =>
            "M 3,4 H 21 V 17 H 3 Z M 6,7 H 14 M 6,10 H 14 M 6,13 H 14 M 17,7 H 18 M 17,10 H 18 M 17,13 H 18 M 10,17 V 20 M 14,17 V 20 M 7,20 H 17",
        BedsideToolbarIcon.Alarm =>
            "M 5,17 H 19 L 17,14 V 10 C 17,3 7,3 7,10 V 14 Z M 10,20 C 10,22 14,22 14,20 M 12,3 V 2 M 3,9 C 3,6 4,4 6,3 M 21,9 C 21,6 20,4 18,3",
        BedsideToolbarIcon.Review =>
            "M 3,4 H 21 V 20 H 3 Z M 3,8 H 21 M 8,8 V 20 M 3,12 H 21 M 3,16 H 21 M 13,8 V 20",
        BedsideToolbarIcon.Standby =>
            "M 12,2 V 12 M 7,5 A 8,8 0 1 0 17,5",
        BedsideToolbarIcon.Menu =>
            "M 4,4 H 20 Q 22,4 22,6 V 18 Q 22,20 20,20 H 4 Q 2,20 2,18 V 6 Q 2,4 4,4 Z M 6,8 H 18 M 6,12 H 18 M 6,16 H 18",
        BedsideToolbarIcon.Ecg =>
            "M 3,4 H 21 V 20 H 3 Z M 5,12 H 8 L 10,8 L 13,17 L 15,10 L 17,12 H 19",
        BedsideToolbarIcon.Lead =>
            "M 5,4 V 9 C 5,15 12,12 12,17 V 21 M 12,4 V 21 M 19,4 V 9 C 19,15 12,12 12,17 M 3,3 H 7 V 6 H 3 Z M 10,3 H 14 V 6 H 10 Z M 17,3 H 21 V 6 H 17 Z",
        BedsideToolbarIcon.Print =>
            "M 6,8 V 3 H 18 V 8 M 6,17 H 3 V 8 H 21 V 17 H 18 M 6,13 H 18 V 21 H 6 Z M 9,16 H 15 M 9,18 H 15 M 17,10 H 18",
        BedsideToolbarIcon.Silence =>
            "M 5,17 H 19 L 17,14 V 10 C 17,3 7,3 7,10 V 14 Z M 10,20 C 10,22 14,22 14,20 M 12,3 V 2 M 3,3 L 21,21",
        _ => throw new ArgumentOutOfRangeException(nameof(icon))
    };
}
