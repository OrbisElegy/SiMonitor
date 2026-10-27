// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Monitor.Desktop;

// Both print workflows use the same form rhythm, resizable preview and export footer.
// Paper dimensions and document rendering remain the responsibility of each workflow.
internal static class PrintPreviewLayout
{
    internal static StackPanel CreateForm(string paperSize) => new()
    {
        Spacing = 16,
        Margin = new Thickness(20),
        Children =
        {
            new TextBlock { Text = paperSize, FontSize = 18, FontWeight = FontWeight.SemiBold }
        }
    };

    internal static void AddField(StackPanel form, Control control, DesktopLocalization localization, string key)
    {
        var label = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = DesktopFluentStyle.SecondaryText };
        localization.Bind(label, TextBlock.TextProperty, key);
        localization.Bind(control, AutomationProperties.NameProperty, key);
        AutomationProperties.SetLabeledBy(control, label);
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.MinHeight = Math.Max(44, control.MinHeight);
        var field = new StackPanel { Spacing = 6 };
        field.Children.Add(label);
        field.Children.Add(control);
        form.Children.Add(field);
    }

    internal static Grid Compose(Window window, StackPanel form, Image preview, TextBlock status,
        Button save, Button open, DesktopLocalization localization, string scaleHintKey)
    {
        var body = new Grid { ColumnDefinitions = new("300,6,*") };
        body.ColumnDefinitions[0].MinWidth = 250;
        body.ColumnDefinitions[0].MaxWidth = 450;
        body.ColumnDefinitions[2].MinWidth = 250;
        body.Children.Add(new ScrollViewer
        {
            Content = form,
            Background = DesktopFluentStyle.Surface,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = DesktopFluentStyle.Stroke,
            KeyboardIncrement = 16
        };
        localization.Bind(splitter, AutomationProperties.NameProperty, "print.preview");
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);
        var paper = new Border
        {
            Background = Brush.Parse("#DDE1E5"),
            Padding = new Thickness(20),
            Child = preview,
            ClipToBounds = true
        };
        localization.Bind(preview, AutomationProperties.NameProperty, "print.preview");
        Grid.SetColumn(paper, 2);
        body.Children.Add(paper);

        var close = new Button { IsCancel = true };
        localization.Bind(close, ContentControl.ContentProperty, "print.close");
        localization.Bind(save, ContentControl.ContentProperty, "print.save");
        localization.Bind(open, ContentControl.ContentProperty, "print.open");
        close.Click += (_, _) => window.Close();
        save.Classes.Add("accent");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var button in new[] { close, open, save })
        {
            button.MinHeight = 44;
            button.Padding = new Thickness(16, 8);
            button.Margin = new Thickness(8, 4, 0, 0);
            actions.Children.Add(button);
        }
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = DesktopFluentStyle.SecondaryText };
        localization.Bind(hint, TextBlock.TextProperty, scaleHintKey);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var footer = new StackPanel { Spacing = 8, Margin = new Thickness(20, 12) };
        footer.Children.Add(hint);
        footer.Children.Add(status);
        footer.Children.Add(actions);
        var footerSurface = new Border
        {
            Background = DesktopFluentStyle.Surface,
            BorderBrush = DesktopFluentStyle.Stroke,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footer
        };
        var root = new Grid { RowDefinitions = new("*,Auto"), Background = DesktopFluentStyle.Canvas };
        root.Children.Add(body);
        Grid.SetRow(footerSurface, 1);
        root.Children.Add(footerSurface);
        ScreenWatermark.Attach(root);
        return root;
    }
}
