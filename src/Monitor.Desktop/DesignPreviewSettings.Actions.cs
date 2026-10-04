// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;

namespace Monitor.Desktop;

internal sealed partial class DesignPreviewSettings
{
    private Border BuildActionFooter(Action apply, Action run)
    {
        // Keep the native NumericUpDown parsing, keyboard, range and spin logic;
        // only replace the oversized horizontal arrow arrangement in this field.
        var spinnerStyle = new Style(s => s.OfType<NumericUpDown>().Template().OfType<ButtonSpinner>());
        spinnerStyle.Setters.Add(new Setter(TemplatedControl.TemplateProperty,
            new FuncControlTemplate<ButtonSpinner>((spinner, scope) =>
            {
                var grid = new Grid { ColumnDefinitions = new("*,28"), RowDefinitions = new("*,*") };
                var content = new ContentPresenter();
                content.Bind(ContentPresenter.ContentProperty, new Binding(nameof(ButtonSpinner.Content)) { Source = spinner });
                Grid.SetRowSpan(content, 2);
                grid.Children.Add(content);
                AddArrow("PART_IncreaseButton", "⌃", "增加接续延迟", 0);
                AddArrow("PART_DecreaseButton", "⌄", "减少接续延迟", 1);
                var border = new Border
                {
                    BorderBrush = Brush.Parse("#8A8A8A"),
                    BorderThickness = new Thickness(1),
                    Background = DesktopFluentStyle.Surface,
                    CornerRadius = new CornerRadius(4),
                    ClipToBounds = true,
                    Child = grid
                };
                spinner.PropertyChanged += (_, change) =>
                {
                    if (change.Property == IsKeyboardFocusWithinProperty)
                    { border.BorderBrush = spinner.IsKeyboardFocusWithin ? DesktopFluentStyle.Accent : Brush.Parse("#8A8A8A"); }
                };
                return border;

                void AddArrow(string name, string symbol, string label, int row)
                {
                    var button = new RepeatButton
                    {
                        Name = name,
                        Content = symbol,
                        Padding = new Thickness(0),
                        MinHeight = 0,
                        MinWidth = 0,
                        FontSize = 14,
                        Focusable = false,
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        VerticalAlignment = VerticalAlignment.Stretch,
                        CornerRadius = new CornerRadius(0),
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        BorderThickness = new Thickness(1, row == 0 ? 0 : 1, 0, 0)
                    };
                    var normal = new Style(s => s.OfType<RepeatButton>());
                    normal.Setters.Add(new Setter(BackgroundProperty, DesktopFluentStyle.Surface));
                    normal.Setters.Add(new Setter(BorderBrushProperty, DesktopFluentStyle.Stroke));
                    button.Styles.Add(normal);
                    var hover = new Style(s => s.OfType<RepeatButton>().Class(":pointerover"));
                    hover.Setters.Add(new Setter(BackgroundProperty, Brush.Parse("#F0F0F0")));
                    button.Styles.Add(hover);
                    var pressed = new Style(s => s.OfType<RepeatButton>().Class(":pressed"));
                    pressed.Setters.Add(new Setter(BackgroundProperty, Brush.Parse("#DCDCDC")));
                    button.Styles.Add(pressed);
                    AutomationProperties.SetName(button, label);
                    scope.Register(name, button);
                    Grid.SetColumn(button, 1);
                    Grid.SetRow(button, row);
                    grid.Children.Add(button);
                }
            })));
        ApplyDelaySeconds.Styles.Add(spinnerStyle);
        AutomationProperties.SetName(ApplyDelaySeconds, "接续延迟（秒）");
        AutomationProperties.SetHelpText(ApplyDelaySeconds, "0 到 60 秒，可直接输入；上下方向键每次调整 0.1 秒。");
        var delay = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var label = new Label { Content = "接续延迟", Target = ApplyDelaySeconds, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        delay.Children.Add(label);
        delay.Children.Add(ApplyDelaySeconds);
        delay.Children.Add(new TextBlock { Text = "秒 · 0–60", Foreground = Brush.Parse("#616161"), VerticalAlignment = VerticalAlignment.Center });

        var actions = new Grid { ColumnDefinitions = new("*,Auto") };
        ResetAll.HorizontalAlignment = HorizontalAlignment.Left;
        ResetAll.Background = Brushes.Transparent;
        ResetAll.BorderBrush = Brushes.Transparent;
        ResetAll.Foreground = Brush.Parse("#B42318");
        actions.Children.Add(ResetAll);
        var primary = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        primary.Children.Add(Run);
        primary.Children.Add(Restart);
        primary.Children.Add(Apply);
        Grid.SetColumn(primary, 1);
        actions.Children.Add(primary);
        ToolTip.SetTip(Apply, "按接续延迟应用设置，保留当前扫描历史。");
        ToolTip.SetTip(Restart, "应用当前设置并清空扫描历史，从头开始。");
        ToolTip.SetTip(ResetAll, "恢复全部默认设置，包括波形、显示、声音和报警；清除当前历史。");
        Apply.Click += (_, _) => apply();
        Run.Click += (_, _) => run();
        var contents = new StackPanel { Spacing = 16 };
        contents.Children.Add(delay);
        contents.Children.Add(actions);
        return new Border
        {
            BorderBrush = DesktopFluentStyle.Stroke,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(20, 16, 20, 12),
            Child = contents
        };
    }
}
