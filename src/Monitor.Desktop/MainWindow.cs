// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Application.Presentation;
using Monitor.Infrastructure.Presentation;

namespace Monitor.Desktop;

// Startup shell with no verified record/provider connected by default.
public sealed class MainWindow : Window
{
    private readonly TextBlock _recordStatus;
    private readonly TextBlock _demoNotice = new()
    {
        Text = "合成测试记录 · 仅用于界面交互验证",
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly ContentControl _recordContent = new();
    private readonly TextBlock _measurementReadout = new()
    {
        IsVisible = false,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private readonly Button _resetDemo = new()
    {
        Content = "重置测试卡尺",
        IsVisible = false,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private Action? _resetDemoAction;
    private readonly Button _clearCursors = new()
    {
        Content = "清除卡尺",
        IsVisible = false,
        IsEnabled = false,
        HorizontalAlignment = HorizontalAlignment.Center,
    };
    private Action<CapturedRecordSvgInputSession>? _clearCommand;
    private Func<CapturedRecordSvgInputSession, Point, RecordCursorHits>? _pointerQuery;

    public MainWindow()
    {
        Title = "心电监护教学模拟";
        Width = 1280;
        Height = 720;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ToolTip.SetShowOnDisabled(_clearCursors, true);
        AutomationProperties.SetName(_measurementReadout, "人工卡尺测量结果");
        _resetDemo.Click += (_, _) => _resetDemoAction?.Invoke();
        _clearCursors.Click += (_, _) =>
        {
            if (_clearCursors.IsEnabled && CurrentPublication?.Input is { } input)
            { _clearCommand?.Invoke(input); }
        };
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
                    _demoNotice,
                    DemoViews,
                    _recordContent,
                    _measurementReadout,
                    _clearCursors,
                    _resetDemo,
                    new TextBlock { Text = "仅用于教学模拟", TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
    }

    public CapturedRecordSvgPublication? CurrentPublication { get; private set; }
    internal bool HasDemoNotice => _demoNotice.IsVisible;
    internal Button ResetDemoButton => _resetDemo;
    internal DemoViewControls DemoViews { get; } = new();

    internal void SetDemoReset(Action reset)
    {
        Dispatcher.UIThread.VerifyAccess();
        _resetDemoAction = reset;
        _resetDemo.IsVisible = true;
        _demoNotice.Text = "合成测试记录 · 蓝色为时间起点，红色为终点，起点不能晚于终点；拖动中按 Esc 取消，清除后可重置";
    }

    internal void ShowDemoNotice()
    {
        Dispatcher.UIThread.VerifyAccess();
        Title = "心电监护教学模拟 — 合成记录交互验证";
        _demoNotice.IsVisible = true;
    }
    internal event EventHandler? PublicationChanging;

    // Caller supplies a native record control built for this same publication.
    // Invoke on the UI thread; native waveform/input integration remains separate.
    public void ApplyPublication(CapturedRecordSvgPublication publication, RecordStudyControl? readyContent = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        // Withdraw first: malformed or unavailable updates cannot retain old visual input.
        if (_recordContent.Content is RecordStudyControl previous) { previous.BindPointerQuery(null); }
        _recordContent.Content = null;
        CurrentPublication = null;
        _measurementReadout.Text = null;
        _measurementReadout.IsVisible = false;
        AutomationProperties.SetHelpText(_measurementReadout, null);
        PublicationChanging?.Invoke(this, EventArgs.Empty);
        UpdateClearButton();
        _recordStatus.Text = "暂时无法显示记录";
        if (publication is null || !Enum.IsDefined(publication.Status) || string.IsNullOrWhiteSpace(publication.ReasonCode) ||
            (publication.Status == CapturedRecordSvgStatus.Ready ? publication.Input is null || readyContent is null || !ReferenceEquals(readyContent.InputSession, publication.Input) : publication.Input is not null))
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
        _measurementReadout.Text = MeasurementReadout.Format(publication.Input?.Display.Content.Content.Display.Content.Content.Study.Measurement);
        _measurementReadout.IsVisible = _measurementReadout.Text is not null;
        AutomationProperties.SetHelpText(_measurementReadout, _measurementReadout.Text);
        UpdateClearButton();
        UpdatePointerQuery();
    }

    internal void SetPointerQuery(Func<CapturedRecordSvgInputSession, Point, RecordCursorHits>? query)
    {
        Dispatcher.UIThread.VerifyAccess();
        _pointerQuery = query;
        UpdatePointerQuery();
    }

    private void UpdatePointerQuery()
    {
        if (_recordContent.Content is not RecordStudyControl control) { return; }
        var query = _pointerQuery;
        if (control.InputSession.Display.Content.Content.Display.Content.Content.Study.Measurement?.ReasonCode != "RecordMeasurement.Ready")
        { query = null; }
        control.BindPointerQuery(query is null ? null : point => query(control.InputSession, point));
        if (DragInput is not null && control.InputSession.Display.Content.Content.Display.Content.Content.Study.Measurement?.ReasonCode == "RecordMeasurement.Ready")
        { control.IsHitTestVisible = true; }
    }

    internal NativeDragInput? DragInput { get; private set; }

    internal void SetDragInput(NativeDragInput? input)
    {
        Dispatcher.UIThread.VerifyAccess();
        DragInput = input;
        UpdatePointerQuery();
    }

    internal RecordStudyControl? RecordControl => _recordContent.Content as RecordStudyControl;
    internal string? MeasurementReadoutText => _measurementReadout.Text;
    internal string? MeasurementAccessibilityText => AutomationProperties.GetHelpText(_measurementReadout);

    internal void SetClearCommand(Action<CapturedRecordSvgInputSession>? command)
    {
        Dispatcher.UIThread.VerifyAccess();
        _clearCommand = command;
        UpdateClearButton();
    }

    private void UpdateClearButton()
    {
        string? reason = CurrentPublication?.Input?.Display.Content.Content.Display.Content.Content.Study.Measurement?.ReasonCode;
        _clearCursors.IsVisible = _clearCommand is not null &&
            reason is "RecordMeasurement.Ready" or "RecordMeasurement.NoCursorPair" or "RecordMeasurement.CourseLocked";
        _clearCursors.IsEnabled = _clearCursors.IsVisible && reason == "RecordMeasurement.Ready";
        string? explanation = _clearCursors.IsVisible ? reason switch
        {
            "RecordMeasurement.NoCursorPair" => "当前没有可清除的卡尺",
            "RecordMeasurement.CourseLocked" => "课程已锁定快速测量",
            _ => null,
        } : null;
        ToolTip.SetTip(_clearCursors, explanation);
        AutomationProperties.SetHelpText(_clearCursors, explanation);
        if (!_clearCursors.IsVisible || _clearCursors.IsEnabled) { ToolTip.SetIsOpen(_clearCursors, false); }
    }

    internal Button ClearCursorButton => _clearCursors;

    internal bool HasUnloadedRecordState => _recordStatus.Text == "尚未载入已验证记录" && _recordContent.Content is null;
    internal bool HasNoRecordContent => _recordContent.Content is null;
}
