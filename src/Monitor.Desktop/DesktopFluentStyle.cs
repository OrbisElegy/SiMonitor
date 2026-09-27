// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Monitor.Desktop;

// All desktop entry points share Fluent templates and semantic resource tokens.
internal static class DesktopFluentStyle
{
    internal static readonly IBrush Canvas = Brush.Parse("#F3F3F3");
    internal static readonly IBrush Surface = Brushes.White;
    internal static readonly IBrush Stroke = Brush.Parse("#E5E5E5");
    internal static readonly IBrush Text = Brush.Parse("#1A1A1A");
    internal static void Install(Avalonia.Application app)
    {
        app.RequestedThemeVariant = ThemeVariant.Light;
        app.Styles.Add(new FluentTheme());
        app.Resources["ControlCornerRadius"] = new CornerRadius(4);
        app.Resources["ButtonBorderThemeThickness"] = new Thickness(1);
        app.Resources["ButtonBackground"] = Surface;
        app.Resources["ButtonBackgroundPointerOver"] = Brush.Parse("#F9F9F9");
        app.Resources["ButtonBackgroundPressed"] = Brush.Parse("#F0F0F0");
        app.Resources["ButtonBorderBrush"] = Brush.Parse("#D1D1D1");
        app.Resources["ButtonBorderBrushPointerOver"] = Brush.Parse("#C7C7C7");
        app.Resources["ButtonBorderBrushPressed"] = Stroke;
        app.Resources["SystemControlHighlightListAccentLowBrush"] = Brush.Parse("#E5E5E5");
        app.Resources["SystemControlHighlightListAccentMediumBrush"] = Brush.Parse("#DCDCDC");
        app.Resources["SystemControlHighlightListAccentHighBrush"] = Brush.Parse("#D1D1D1");
        var items = new Style(s => s.OfType<ListBoxItem>());
        items.Setters.Add(new Setter(ListBoxItem.CornerRadiusProperty, new CornerRadius(4)));
        app.Styles.Add(items);
        var buttons = new Style(s => s.OfType<Button>());
        buttons.Setters.Add(new Setter(Button.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        buttons.Setters.Add(new Setter(Button.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        app.Styles.Add(buttons);
        var windows = new Style(s => s.OfType<Window>());
        windows.Setters.Add(new Setter(Window.FontFamilyProperty, new FontFamily("Segoe UI, Microsoft YaHei UI, WenQuanYi Zen Hei, sans-serif")));
        app.Styles.Add(windows);
    }
}
