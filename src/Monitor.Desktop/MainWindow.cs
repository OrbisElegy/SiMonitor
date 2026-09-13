// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Monitor.Desktop;

// Startup shell with no verified record/provider connected by default.
public sealed class MainWindow : Window
{
    private readonly TextBlock _recordStatus;

    public MainWindow()
    {
        Title = "心电监护教学模拟";
        Width = 1280;
        Height = 720;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _recordStatus = new TextBlock
        {
            Text = "尚未载入已验证记录",
            FontSize = 24,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(32),
                Spacing = 16,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "心电监护教学模拟", FontSize = 32, TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center },
                    _recordStatus,
                    new TextBlock { Text = "仅用于教学模拟", TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
    }

    internal bool HasUnloadedRecordState => _recordStatus.Text == "尚未载入已验证记录" && Content is ScrollViewer;
}
