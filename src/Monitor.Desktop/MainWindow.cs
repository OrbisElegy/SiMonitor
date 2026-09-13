// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

// Startup shell with no verified record/provider connected by default.
public sealed class MainWindow : Window
{
    private readonly TextBlock _recordStatus;
    private readonly ContentControl _recordContent = new();

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
                    _recordContent,
                    new TextBlock { Text = "仅用于教学模拟", TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
    }

    public CapturedRecordSvgPublication? CurrentPublication { get; private set; }

    // Caller supplies a native record control built for this same publication.
    // Invoke on the UI thread; native record drawing/input integration remains separate.
    public void ApplyPublication(CapturedRecordSvgPublication publication, Control? readyContent = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        // Withdraw first: malformed or unavailable updates cannot retain old visual input.
        _recordContent.Content = null;
        CurrentPublication = null;
        _recordStatus.Text = "暂时无法显示记录";
        if (publication is null || !Enum.IsDefined(publication.Status) || string.IsNullOrWhiteSpace(publication.ReasonCode) ||
            (publication.Status == CapturedRecordSvgStatus.Ready ? publication.Input is null || readyContent is null : publication.Input is not null))
        { throw new ArgumentException("Incomplete desktop publication", nameof(publication)); }
        string message = publication.Status switch
        {
            CapturedRecordSvgStatus.NotRendered => "尚未载入已验证记录",
            CapturedRecordSvgStatus.Refreshing => "正在刷新记录",
            CapturedRecordSvgStatus.Ready => "已载入记录",
            CapturedRecordSvgStatus.Denied => "当前无法查看记录",
            CapturedRecordSvgStatus.Cancelled => "记录刷新已取消",
            CapturedRecordSvgStatus.Withdrawn => "记录画面已关闭",
            _ => "暂时无法显示记录",
        };
        if (publication.Status == CapturedRecordSvgStatus.Ready) { _recordContent.Content = readyContent; }
        _recordStatus.Text = message;
        CurrentPublication = publication;
    }

    internal bool HasUnloadedRecordState => _recordStatus.Text == "尚未载入已验证记录" && _recordContent.Content is null;
    internal bool HasNoRecordContent => _recordContent.Content is null;
}
