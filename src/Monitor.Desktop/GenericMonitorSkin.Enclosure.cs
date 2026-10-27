// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;

namespace Monitor.Desktop;

internal sealed partial class GenericMonitorSkin
{
    private static readonly DeviceOperatingMode[] ModeSelectorOrder =
        [DeviceOperatingMode.Pacer, DeviceOperatingMode.Off, DeviceOperatingMode.Monitor, DeviceOperatingMode.Aed, DeviceOperatingMode.Manual];
    private static readonly double[] ModeSelectorAngles = [-140, -70, 0, 70, 140];
    private readonly List<Button> _softKeys = [];
    private readonly List<TextBlock> _softKeyLabels = [];
    private readonly List<Action> _softKeyActions = [];
    private readonly List<Button> _modeChoices = [];
    private readonly List<Button> _menuChoices = [];
    private readonly List<Control> _poweredControls = [];
    private Border? _powerOffOverlay;
    private Border? _screenOverlay;
    private Control? _overlayReturnFocus;
    private Panel? _waveformLayer;
    private Image? _frozenWaveformImage;
    private RenderTargetBitmap? _frozenWaveformBitmap;
    private Border? _alarmLamp;
    private Border? _readyLamp;
    private Border? _chargeLamp;
    private Border? _syncLamp;
    private TextBlock? _operatingModeCaption;
    private TextBlock? _energyCaption;
    private TextBlock? _gainCaption;
    private TextBlock? _frozenWaveformCaption;
    private ProgressBar? _chargeProgress;
    private TextBlock? _chargeProgressCaption;
    private Control? _trends;
    private int _navigationIndex;
    private bool _gainCaptionBound;
    private int? _shownEnergyJoules;
    private int _shownChargeProgressPermille = -1;
    private string? _softKeyStateIdentity;
    private bool _editingPacingParameter;
    private TemplatedControl? _navigationTarget;
    private IBrush? _navigationBorderBrush;
    private Thickness _navigationBorderThickness;
    internal DeviceRotaryDial? ModeSelector { get; private set; }
    internal DeviceRotaryDial? NavigationDial { get; private set; }
    internal IReadOnlyList<Button> SoftKeys => _softKeys;
    internal IReadOnlyList<TextBlock> SoftKeyLabels => _softKeyLabels;
    internal IReadOnlyList<Button> ModeChoices => _modeChoices;
    internal ProgressBar? ChargeProgress => _chargeProgress;
    internal Button? GainButton { get; private set; }
    internal Button? EventButton { get; private set; }
    internal bool WaveformsFrozen => _frozenWaveformBitmap is not null;
    private void RenderDeviceOperatingMode(DeviceOperatingMode mode)
    {
        if (_powerOffOverlay is null) { return; }
        ClearWaveformFreeze();
        CloseScreenOverlay();
        bool powered = mode != DeviceOperatingMode.Off;
        _powerOffOverlay.IsVisible = !powered;
        foreach (var control in _poweredControls) { control.IsEnabled = powered; }
        _readyLamp!.Background = Brush.Parse(powered ? "#65B85D" : "#334439");
        _localization.Bind(_operatingModeCaption!, TextBlock.TextProperty, ModeCaptionKey(mode));
        int selected = Array.IndexOf(ModeSelectorOrder, mode);
        ModeSelector!.SetSelectedPosition(selected);
        UpdateSoftKeys();
        RefreshTherapyIndicators();
    }

    private void RenderDeviceGain(double? gain, bool changed)
    {
        if (_gainCaption is null || _gainCaptionBound && !changed) { return; }
        _gainCaptionBound = true;
        if (gain is { } value) { _localization.Bind(_gainCaption, TextBlock.TextProperty, "devicePanel.gainValue", value); }
        else { _localization.Bind(_gainCaption, TextBlock.TextProperty, "devicePanel.gainAuto"); }
    }

    private void RenderDeviceAlarmState(MonitorNoticeLevel? level)
    {
        if (_alarmLamp is null) { return; }
        _alarmLamp.Background = Brush.Parse(OperatingMode == DeviceOperatingMode.Off ? "#8B9690" : level switch
        {
            MonitorNoticeLevel.Critical => "#F4473F",
            MonitorNoticeLevel.Warning or MonitorNoticeLevel.Notice => "#FFC441",
            _ => "#8B9690"
        });
    }

    private ThemeVariantScope ComposeEnclosure(MonitorSkinContent content)
    {
        _trends = content.Trends;
        Defibrillator.DetachPanelControls();
        Defibrillator.UseFixedHardwareLabels();
        var discardedToolbar = new StackPanel();
        AddNibpControls(discardedToolbar);
        AddAlarmControls(discardedToolbar);
        discardedToolbar.Children.Clear();
        LayoutButton = ToolbarButton("skin.layout");
        LayoutButton.IsEnabled = false;
        EcgButton = ToolbarButton("skin.ecg");
        EcgButton.Click += (_, _) => SetTwelveLeadVisible(!TwelveLeadVisible);
        PrintButton = ToolbarButton("skin.print");
        PrintButton.Click += (_, _) => PrintRequested?.Invoke();
        LeadButton = ToolbarButton("skin.leads");
        LeadButton.Click += (_, _) => CycleDisplayedLead();
        RefreshLeadCaption();
        GainButton = ToolbarButton("devicePanel.gain");
        GainButton.Click += (_, _) => GainRequested?.Invoke();
        EventButton = ToolbarButton("devicePanel.event");
        EventButton.Click += (_, _) => EventRequested?.Invoke();
        MenuButton = ToolbarButton("bedside.menu");
        MenuButton.Click += (_, _) =>
        {
            if (_screenOverlay!.IsVisible) { CloseScreenOverlay(); }
            else { OpenPanelMenu(); }
        };

        var shell = new Canvas { Width = 1180, Height = 850 };
        var shellMaterial = _package.Material("shell");
        PlaceOnEnclosure(shell, new Border
        {
            Background = Brush.Parse("#5E6C62"),
            CornerRadius = new CornerRadius(43),
            BorderBrush = Brush.Parse("#435148"),
            BorderThickness = new Thickness(3)
        }, 10, 44, 1160, 792);
        PlaceOnEnclosure(shell, new Border
        {
            Background = shellMaterial.Fill,
            BorderBrush = shellMaterial.Border,
            BorderThickness = new Thickness(shellMaterial.BorderThickness),
            CornerRadius = new CornerRadius(shellMaterial.CornerRadius),
            BoxShadow = new BoxShadows(new BoxShadow { Blur = 16, OffsetY = 8, Color = Color.Parse("#50000000") })
        }, 24, 55, 1132, 780);
        // The carry handle and bumper are original vector geometry, not manufacturer artwork.
        PlaceOnEnclosure(shell, new Border
        {
            Background = Brush.Parse("#78867D"),
            BorderBrush = Brush.Parse("#54655A"),
            BorderThickness = new Thickness(8),
            CornerRadius = new CornerRadius(20),
            Child = new Border { Background = Brush.Parse("#161E22"), CornerRadius = new CornerRadius(11), Margin = new Thickness(17, 7) }
        }, 278, 5, 560, 61);
        PlaceOnEnclosure(shell, CreatePanelText("SiMonitor", 26, Brush.Parse("#385C50"), FontWeight.Bold), 82, 74, 230, 35);
        PlaceOnEnclosure(shell, CreatePanelCaption("device.model", 14, shellMaterial.Foreground), 595, 77, 320, 28);
        _alarmLamp = CreateIndicatorLamp("#8B9690", 12);
        PlaceOnEnclosure(shell, _alarmLamp, 438, 78, 140, 14);

        const double screenFrameLeft = 90, screenFrameTop = 112, screenInset = 15, screenWidth = 800, screenHeight = 600;
        var display = new Grid { RowDefinitions = new("40,*,84,52,42"), Background = Background, ClipToBounds = true };
        Screen = display;
        content.Header.Margin = new Thickness(6, 0);
        display.Children.Add(content.Header);
        _waveforms = content.Waveforms;
        _twelveLeadWaveforms = content.TwelveLeadWaveforms;
        _waveformLayer = new Panel { ClipToBounds = true };
        _waveformLayer.Children.Add(_waveforms);
        _waveformLayer.Children.Add(_twelveLeadWaveforms);
        _frozenWaveformImage = new Image { Stretch = Stretch.Fill, IsVisible = false, IsHitTestVisible = false };
        _waveformLayer.Children.Add(_frozenWaveformImage);
        var monitor = new Grid { ColumnDefinitions = new("70*,30*") };
        _monitorArea = monitor;
        monitor.Children.Add(_waveformLayer);
        Grid.SetColumn(content.Numerics, 1);
        monitor.Children.Add(content.Numerics);
        Grid.SetRow(monitor, 1);
        display.Children.Add(monitor);
        var measurements = new Grid { ColumnDefinitions = new("54*,46*"), Background = Background };
        var extras = new UniformGrid { Columns = 3 };
        extras.Children.Add(content.Temperature);
        extras.Children.Add(content.Custom1);
        extras.Children.Add(content.Custom2);
        measurements.Children.Add(extras);
        Grid.SetColumn(content.Nibp, 1);
        if (content.Nibp is MonitorManualTile nibp) { nibp.SetAppearance(_package.NibpBrush, 44); }
        measurements.Children.Add(content.Nibp);
        Grid.SetRow(measurements, 2);
        display.Children.Add(measurements);
        var therapyStatus = new Grid { ColumnDefinitions = new("120,138,*,52"), Margin = new Thickness(8, 2) };
        _operatingModeCaption = CreatePanelCaption("devicePanel.modeMonitor", 16, Brushes.White);
        var modeStatus = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        modeStatus.Children.Add(_operatingModeCaption);
        _gainCaption = CreatePanelText("", 11, Brush.Parse("#9FB7AD"));
        modeStatus.Children.Add(_gainCaption);
        therapyStatus.Children.Add(modeStatus);
        _energyCaption = CreatePanelText("", 24, Brush.Parse("#FFD15A"), FontWeight.Bold);
        Grid.SetColumn(_energyCaption, 1);
        therapyStatus.Children.Add(_energyCaption);
        var status = new Grid { RowDefinitions = new("*,18"), Margin = new Thickness(4, 0) };
        status.Children.Add(Defibrillator.Status);
        PacingStatus.FontSize = 12;
        status.Children.Add(PacingStatus);
        var chargeProgress = new Grid { ColumnDefinitions = new("*,52"), ColumnSpacing = 8 };
        _chargeProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1000,
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#FFD15A"),
            Background = Brush.Parse("#394339")
        };
        _chargeProgressCaption = CreatePanelText("", 12, Brush.Parse("#FFD15A"));
        chargeProgress.Children.Add(_chargeProgress);
        Grid.SetColumn(_chargeProgressCaption, 1);
        chargeProgress.Children.Add(_chargeProgressCaption);
        Grid.SetRow(chargeProgress, 1);
        status.Children.Add(chargeProgress);
        Grid.SetColumn(status, 2);
        therapyStatus.Children.Add(status);
        _syncLamp = CreateIndicatorLamp("#26382D", 4);
        _syncLamp.Child = CreatePanelText("SYNC", 9, Brushes.White);
        _syncLamp.Margin = new Thickness(2, 16);
        Grid.SetColumn(_syncLamp, 3);
        therapyStatus.Children.Add(_syncLamp);
        Grid.SetRow(therapyStatus, 3);
        display.Children.Add(therapyStatus);

        var labels = new UniformGrid { Columns = 3, Background = Brush.Parse("#16262C") };
        var softKeys = new UniformGrid { Columns = 3 };
        PlaceOnEnclosure(shell, softKeys, screenFrameLeft + screenInset,
            screenFrameTop + screenHeight + 2 * screenInset + 3, screenWidth, 30);
        for (int index = 0; index < 3; index++)
        {
            var label = CreatePanelText("", 15, Brushes.White);
            label.TextWrapping = TextWrapping.Wrap;
            label.Margin = new Thickness(6, 0);
            _softKeyLabels.Add(label);
            labels.Children.Add(new Border { BorderBrush = Brush.Parse("#61736A"), BorderThickness = new Thickness(0.5, 0, 0.5, 0), Child = label });
            int slot = index;
            var key = new Button { Content = "▴", FontSize = 23 };
            ApplyButtonMaterial(key, "softkey");
            key.Click += (_, _) => _softKeyActions[slot]();
            if (slot == 2) { ConfigurePacingQuarterKey(key); }
            _softKeys.Add(key);
            _poweredControls.Add(key);
            key.PropertyChanged += (_, _) => label.Opacity = key.IsEffectivelyEnabled ? 1 : .4;
            key.Width = 98;
            key.Height = 30;
            key.HorizontalAlignment = HorizontalAlignment.Center;
            softKeys.Children.Add(key);
        }
        Grid.SetRow(labels, 4);
        display.Children.Add(labels);
        _frozenWaveformCaption = CreatePanelCaption("devicePanel.frozen", 17, Brush.Parse("#FFE072"));
        _frozenWaveformCaption.IsVisible = false;
        _frozenWaveformCaption.Background = Brush.Parse("#17252E");
        _frozenWaveformCaption.HorizontalAlignment = HorizontalAlignment.Left;
        _frozenWaveformCaption.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(_frozenWaveformCaption, 1);
        display.Children.Add(_frozenWaveformCaption);
        _screenOverlay = new Border
        {
            IsVisible = false,
            Background = Brush.Parse("#17232B"),
            BorderBrush = Brush.Parse("#8DA99A"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(32, 12),
            Padding = new Thickness(18)
        };
        Grid.SetRow(_screenOverlay, 1);
        Grid.SetRowSpan(_screenOverlay, 3);
        display.Children.Add(_screenOverlay);
        _powerOffOverlay = new Border
        {
            Background = Brush.Parse("#060B0D"),
            Child = CreatePanelCaption("devicePanel.powerOff", 21, Brush.Parse("#56665C"))
        };
        Grid.SetRowSpan(_powerOffOverlay, 5);
        display.Children.Add(_powerOffOverlay);
        PlaceOnEnclosure(shell, new Border
        {
            Background = Brush.Parse("#17221C"),
            BorderBrush = Brush.Parse("#526358"),
            BorderThickness = new Thickness(3),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12),
            Child = display
        }, screenFrameLeft, screenFrameTop, screenWidth + 2 * screenInset, screenHeight + 2 * screenInset);

        var leftKeys = new[] { (LeadButton, "I / II", "skin.leads"), (GainButton, "↕", "devicePanel.gain") };
        for (int index = 0; index < leftKeys.Length; index++)
        {
            var (button, symbol, labelKey) = leftKeys[index];
            PlaceOnEnclosure(shell, CreateHardwareKey(button, symbol, labelKey), 34, 259 + 120 * index, 52, 90);
        }
        SetGain(_deviceGainValue);
        CreateModeSelector(shell);
        PlaceOnEnclosure(shell, CreatePanelCaption("skin.energy", 17, shellMaterial.Foreground), 954, 357, 170, 26);
        var energyKeys = new Grid { ColumnDefinitions = new("*,*") };
        ApplyButtonMaterial(Defibrillator.LowerEnergy, "key");
        ApplyButtonMaterial(Defibrillator.HigherEnergy, "key");
        Defibrillator.LowerEnergy.FontSize = Defibrillator.HigherEnergy.FontSize = 30;
        energyKeys.Children.Add(Defibrillator.LowerEnergy);
        Grid.SetColumn(Defibrillator.HigherEnergy, 1);
        energyKeys.Children.Add(Defibrillator.HigherEnergy);
        PlaceOnEnclosure(shell, energyKeys, 963, 392, 155, 53);
        ApplyButtonMaterial(Defibrillator.Charge, "charge");
        ApplyButtonMaterial(Defibrillator.Shock, "shock");
        Defibrillator.Charge.FontSize = Defibrillator.Shock.FontSize = 21;
        Defibrillator.Shock.FontWeight = FontWeight.Bold;
        PlaceOnEnclosure(shell, CreatePanelText("2", 28, shellMaterial.Foreground, FontWeight.Bold), 944, 483, 28, 48);
        PlaceOnEnclosure(shell, Defibrillator.Charge, 982, 479, 130, 62);
        _chargeLamp = CreateIndicatorLamp("#67572A", 6);
        PlaceOnEnclosure(shell, _chargeLamp, 1038, 461, 18, 8);
        PlaceOnEnclosure(shell, CreatePanelText("3", 28, shellMaterial.Foreground, FontWeight.Bold), 944, 578, 28, 48);
        PlaceOnEnclosure(shell, Defibrillator.Shock, 982, 575, 130, 68);
        PlaceOnEnclosure(shell, CreatePanelCaption("defib.holdHint", 11, shellMaterial.Foreground), 958, 653, 178, 35);
        CreateStatusIndicators(shell);
        var hardKeys = new[]
        {
            (NibpStartButton, "◉", "devicePanel.nibp"), (SilenceButton, "♩", "skin.silence"),
            (EventButton, "⚑", "devicePanel.event"), (MenuButton, "☷", "bedside.menu")
        };
        for (int index = 0; index < hardKeys.Length; index++)
        {
            var (button, symbol, labelKey) = hardKeys[index];
            PlaceOnEnclosure(shell, CreateHardwareKey(button, symbol, labelKey), 380 + 117 * index, 779, 106, 57);
        }
        NavigationDial = new(_localization, _package.Material("selector"), "devicePanel.navigation", true)
        { Width = 83, Height = 83 };
        NavigationDial.Rotated += NavigateControls;
        NavigationDial.Pressed += ActivateNavigationSelection;
        _poweredControls.Add(NavigationDial);
        PlaceOnEnclosure(shell, NavigationDial, 857, 753, 83, 83);
        Defibrillator.StateRefreshed += RefreshTherapyIndicators;
        Energy.SelectionChanged += (_, _) => RefreshTherapyIndicators();
        ApplyPacing.PropertyChanged += (_, _) => RefreshPacingSoftKeys();
        StopPacing.PropertyChanged += (_, _) => RefreshPacingSoftKeys();
        SetTwelveLeadVisible(TwelveLeadVisible);
        SetOperatingMode(OperatingMode);
        SetAlarmState(_deviceAlarmLevel);
        shell.DetachedFromVisualTree += (_, _) => { ClearWaveformFreeze(); ReleasePacingQuarter(); };
        return new ThemeVariantScope
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Child = new Border { Background = Brush.Parse("#161E22"), Padding = new Thickness(10), Child = new Viewbox { Stretch = Stretch.Uniform, Child = shell } }
        };
    }

    private void CreateModeSelector(Canvas shell)
    {
        PlaceOnEnclosure(shell, CreatePanelText("1", 26, _package.Material("shell").Foreground, FontWeight.Bold), 938, 96, 30, 35);
        ModeSelector = new(_localization, _package.Material("selector"), "devicePanel.modeSelector", false);
        ModeSelector.ConfigurePositions(ModeSelectorAngles, Array.IndexOf(ModeSelectorOrder, OperatingMode));
        ModeSelector.SelectedPositionRequested += index => OperatingModeRequested?.Invoke(ModeSelectorOrder[index]);
        const double centerX = 1040, centerY = 249;
        PlaceOnEnclosure(shell, ModeSelector, centerX - 53, centerY - 53, 106, 106);
        double[] widths = [94, 48, 112, 64, 112];
        for (int index = 0; index < ModeSelectorOrder.Length; index++)
        {
            var mode = ModeSelectorOrder[index];
            double radians = ModeSelectorAngles[index] * Math.PI / 180;
            double x = Math.Sin(radians), y = -Math.Cos(radians);
            shell.Children.Add(new Avalonia.Controls.Shapes.Line
            {
                StartPoint = new Point(centerX + x * 59, centerY + y * 59),
                EndPoint = new Point(centerX + x * 65, centerY + y * 65),
                Stroke = _package.Material("shell").Foreground,
                StrokeThickness = 2,
                IsHitTestVisible = false
            });
            var choice = new Button
            {
                Content = CreatePanelCaption(ModeCaptionKey(mode), 13, _package.Material("shell").Foreground),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Template = new FuncControlTemplate<Button>((owner, _) => new ContentPresenter
                {
                    Content = owner.Content,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center
                })
            };
            _localization.Bind(choice, AutomationProperties.NameProperty, ModeCaptionKey(mode));
            choice.Click += (_, _) => OperatingModeRequested?.Invoke(mode);
            _modeChoices.Add(choice);
            PlaceOnEnclosure(shell, choice, centerX + x * 90 - widths[index] / 2, centerY + y * 90 - 18, widths[index], 36);
        }
    }

    private void CreateStatusIndicators(Canvas shell)
    {
        var power = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        power.Children.Add(CreateIndicatorLamp("#6DB65D", 5));
        power.Children.Add(CreatePanelCaption("devicePanel.acPower", 11, _package.Material("shell").Foreground));
        power.Children.Add(CreateIndicatorLamp("#69756A", 5));
        power.Children.Add(CreatePanelCaption("devicePanel.battery", 11, _package.Material("shell").Foreground));
        _readyLamp = CreateIndicatorLamp("#65B85D", 5);
        power.Children.Add(_readyLamp);
        power.Children.Add(CreatePanelCaption("devicePanel.ready", 11, _package.Material("shell").Foreground));
        PlaceOnEnclosure(shell, power, 98, 787, 278, 28);
        var grille = new StackPanel { Spacing = 4 };
        for (int index = 0; index < 7; index++)
        { grille.Children.Add(new Border { Width = 72 - Math.Abs(3 - index) * 8, Height = 3, Background = Brush.Parse("#6D7B70"), CornerRadius = new CornerRadius(2) }); }
        PlaceOnEnclosure(shell, grille, 1002, 727, 90, 48);
    }

    private void UpdateSoftKeys()
    {
        if (_softKeys.Count == 0) { return; }
        string identity = $"{OperatingMode}:{Defibrillator.SyncLit}:{Defibrillator.CurrentAedPhase is AedPhase.Cpr or AedPhase.Suspended}:{_pacingRunning}:{WaveformsFrozen}";
        if (_softKeyStateIdentity == identity) { return; }
        _softKeyStateIdentity = identity;
        _softKeyActions.Clear();
        (string Key, Action Action)[] actions = OperatingMode switch
        {
            DeviceOperatingMode.Manual =>
            [
                (Defibrillator.SyncLit ? "devicePanel.exitSync" : "skin.sync", () => ActivateControlButton(Defibrillator.Sync, _softKeys[0])),
                ("skin.disarm", () => ActivateControlButton(Defibrillator.Disarm, _softKeys[1])),
                ("bedside.menu", OpenPanelMenu)
            ],
            DeviceOperatingMode.Aed =>
            [
                (Defibrillator.CurrentAedPhase is AedPhase.Cpr or AedPhase.Suspended ? "devicePanel.aedResume" : "devicePanel.aedPause", Defibrillator.RunAedSoftCommand),
                ("skin.disarm", () => ActivateControlButton(Defibrillator.Disarm, _softKeys[1])),
                ("skin.alarms", () => ActivateControlButton(AlarmsButton, _softKeys[2]))
            ],
            DeviceOperatingMode.Pacer =>
            [
                (_pacingRunning ? "skin.pacingStop" : "skin.pacingStart", () => ActivateControlButton(_pacingRunning ? StopPacing : ApplyPacing, _softKeys[0])),
                ("devicePanel.pacerSettings", OpenPacingSettings),
                ("devicePanel.pacingQuarter", () => { })
            ],
            _ =>
            [
                (WaveformsFrozen ? "devicePanel.unfreeze" : "devicePanel.freeze", ToggleWaveformFreeze),
                ("devicePanel.trends", OpenTrends),
                ("devicePanel.alarmReset", () =>
                {
                    if (AlarmResetRequested is not null) { AlarmResetRequested.Invoke(); }
                    else { ActivateControlButton(AlarmsButton, _softKeys[2]); }
                })
            ]
        };
        for (int index = 0; index < actions.Length; index++)
        {
            var (key, action) = actions[index];
            _localization.Bind(_softKeyLabels[index], TextBlock.TextProperty, key);
            _localization.Bind(_softKeys[index], AutomationProperties.NameProperty, key);
            _softKeyActions.Add(action);
        }
    }

    private void RefreshTherapyIndicators()
    {
        if (_energyCaption is null) { return; }
        bool manual = OperatingMode == DeviceOperatingMode.Manual;
        bool therapy = DeviceTherapyEnabled;
        int energy = Energy.SelectedItem is int value ? value : 0;
        if (_shownEnergyJoules != energy)
        {
            _shownEnergyJoules = energy;
            _localization.Bind(_energyCaption, TextBlock.TextProperty, "devicePanel.energyValue", energy);
        }
        _energyCaption.IsVisible = therapy;
        Defibrillator.Status.IsVisible = therapy;
        PacingStatus.IsVisible = OperatingMode == DeviceOperatingMode.Pacer;
        _syncLamp!.IsVisible = manual && Defibrillator.SyncLit;
        _syncLamp.Background = Brush.Parse(Defibrillator.SyncLit ? "#2A9D63" : "#26382D");
        _chargeLamp!.Background = Brush.Parse(therapy && Defibrillator.ChargeLit ? "#FFDE50" : "#67572A");
        Defibrillator.Charge.IsEnabled &= therapy;
        Defibrillator.Shock.IsEnabled &= therapy;
        Defibrillator.Sync.IsEnabled &= manual;
        Defibrillator.LowerEnergy.IsEnabled &= manual;
        Defibrillator.HigherEnergy.IsEnabled &= manual;
        Defibrillator.Shock.Background = Defibrillator.ShockLit && therapy ? _package.Material("shock").PressedFill : _package.Material("shock").Fill;
        Defibrillator.Charge.Background = _package.Material("charge").Fill;
        int progress = Math.Max(0, Defibrillator.ChargeProgressPermille);
        _chargeProgress!.Value = progress;
        _chargeProgress.IsVisible = therapy && progress > 0;
        _chargeProgressCaption!.IsVisible = _chargeProgress.IsVisible;
        if (_shownChargeProgressPermille != progress)
        {
            _shownChargeProgressPermille = progress;
            _localization.Bind(_chargeProgressCaption, TextBlock.TextProperty, "defib.chargeProgress", progress / 10);
        }
        UpdateSoftKeys();
        if (OperatingMode == DeviceOperatingMode.Manual)
        {
            _softKeys[0].IsEnabled = Defibrillator.Sync.IsEnabled;
            _softKeys[1].IsEnabled = Defibrillator.Disarm.IsEnabled;
        }
        else if (OperatingMode == DeviceOperatingMode.Aed)
        {
            _softKeys[0].IsEnabled = Defibrillator.AedSoftAvailable;
            _softKeys[1].IsEnabled = Defibrillator.Disarm.IsEnabled;
        }
        else if (OperatingMode == DeviceOperatingMode.Pacer) { RefreshPacingSoftKeys(); }
    }

    private void RefreshPacingSoftKeys()
    {
        bool running = _shownPacing?.Running == true;
        _pacingRunning = running;
        UpdateSoftKeys();
        if (OperatingMode == DeviceOperatingMode.Pacer && _softKeys.Count == 3)
        {
            _softKeys[0].IsEnabled = running ? StopPacing.IsEnabled : ApplyPacing.IsEnabled;
            // A queued 4:1 transition temporarily disables Apply/Stop. Keep the
            // held key alive until its release; pending work is owned by the host.
            _softKeys[2].IsEnabled = running;
            if (!_softKeys[2].IsEnabled) { ReleasePacingQuarter(); }
        }
    }

    private void CycleDisplayedLead()
    {
        if (_leadChoices.Count == 0) { return; }
        int selected = _leadChoices.FindIndex(choice => choice.IsChecked == true);
        _leadChoices[(selected + 1) % _leadChoices.Count].IsChecked = true;
    }

    private void ToggleWaveformFreeze()
    {
        if (WaveformsFrozen) { ClearWaveformFreeze(); UpdateSoftKeys(); return; }
        var source = TwelveLeadVisible ? _twelveLeadWaveforms : _waveforms;
        if (source is null || source.Bounds.Width <= 0 || source.Bounds.Height <= 0) { return; }
        var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(source.Bounds.Width), (int)Math.Ceiling(source.Bounds.Height)), new Vector(96, 96));
        try { bitmap.Render(source); }
        catch { bitmap.Dispose(); throw; }
        _frozenWaveformBitmap = bitmap;
        _frozenWaveformImage!.Source = bitmap;
        _frozenWaveformImage.IsVisible = true;
        _frozenWaveformCaption!.IsVisible = true;
        UpdateSoftKeys();
    }

    private void ClearWaveformFreeze()
    {
        if (_frozenWaveformImage is not null) { _frozenWaveformImage.Source = null; _frozenWaveformImage.IsVisible = false; }
        _frozenWaveformBitmap?.Dispose();
        _frozenWaveformBitmap = null;
        if (_frozenWaveformCaption is not null) { _frozenWaveformCaption.IsVisible = false; }
    }

    private void OpenTrends()
    {
        if (_trends is null) { return; }
        CloseScreenOverlay();
        // The history is a drawing control with no intrinsic measured height.
        _trends.MinHeight = 140;
        OpenScreenOverlay("devicePanel.trends", _trends);
    }

    private void OpenPanelMenu()
    {
        CloseScreenOverlay();
        var choices = new StackPanel { Spacing = 8 };
        _menuChoices.Clear();
        AddMenuChoice(choices, "skin.alarms", button => ActivateControlButton(AlarmsButton, button));
        AddMenuChoice(choices, "skin.ecg", _ => { SetTwelveLeadVisible(!TwelveLeadVisible); CloseScreenOverlay(); });
        AddMenuChoice(choices, "skin.nibpAuto", button => ActivateControlButton(NibpAutoButton, button));
        AddMenuChoice(choices, "skin.print", _ => PrintRequested?.Invoke());
        AddMenuChoice(choices, "devicePanel.settings", _ => MenuRequested?.Invoke());
        OpenScreenOverlay("bedside.menu", choices, true);
    }

    private void OpenPacingSettings()
    {
        // Release controls from a previous parameter form before reopening it.
        if (TherapyArea is not null) { return; }
        CloseScreenOverlay();
        var form = new StackPanel { Spacing = 12 };
        form.Children.Add(Field("skin.pacingType", PacingType));
        form.Children.Add(Field("skin.pacingRate", Rate));
        form.Children.Add(Field("skin.pacingCurrent", Current));
        form.Children.Add(ApplyPacing);
        form.Children.Add(StopPacing);
        TherapyArea = new Grid();
        TherapyArea.Children.Add(form);
        OpenScreenOverlay("devicePanel.pacerSettings", TherapyArea);
    }

    private void OpenScreenOverlay(string titleKey, Control content, bool preserveChoices = false)
    {
        if (!preserveChoices) { _menuChoices.Clear(); }
        if (_screenOverlay!.IsVisible == false)
        {
            _overlayReturnFocus = TopLevel.GetTopLevel(_screenOverlay)?.FocusManager?.GetFocusedElement() as Control;
        }
        var panel = new Grid { RowDefinitions = new("Auto,*"), RowSpacing = 8 };
        var heading = new Grid { ColumnDefinitions = new("*,44"), ColumnSpacing = 8 };
        var caption = CreatePanelCaption(titleKey, 18, Brushes.White);
        caption.HorizontalAlignment = HorizontalAlignment.Left;
        heading.Children.Add(caption);
        var close = ToolbarButton("devicePanel.close");
        close.Content = "×";
        close.MinHeight = 44;
        close.Padding = new Thickness(2);
        close.Click += (_, _) => CloseScreenOverlay();
        Grid.SetColumn(close, 1);
        heading.Children.Add(close);
        panel.Children.Add(heading);
        var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        panel.Children.Add(scroll);
        _screenOverlay!.Child = panel;
        _screenOverlay.IsVisible = true;
        _navigationIndex = 0;
        panel.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) { return; }
            CloseScreenOverlay();
            args.Handled = true;
        };
        var first = content.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(control => control.Focusable && control.IsEffectivelyEnabled && control.IsEffectivelyVisible);
        if (first?.Focus() != true) { close.Focus(); }
    }

    private void CloseScreenOverlay()
    {
        if (_screenOverlay is null) { return; }
        bool restoreFocus = _screenOverlay.IsKeyboardFocusWithin;
        HighlightNavigationTarget(null);
        // Unparent shared trend / pacing controls so the next open can reuse them.
        if (_screenOverlay.Child is Grid grid && grid.Children.OfType<ScrollViewer>().FirstOrDefault() is { } scroll)
        {
            if (scroll.Content == TherapyArea && TherapyArea?.Children.FirstOrDefault() is StackPanel form)
            {
                PacingType.IsDropDownOpen = false;
                Rate.ClearValue(TemplatedControl.BorderBrushProperty);
                Current.ClearValue(TemplatedControl.BorderBrushProperty);
                foreach (var field in form.Children.OfType<StackPanel>()) { field.Children.Clear(); }
                form.Children.Clear();
                TherapyArea.Children.Clear();
                TherapyArea = null;
            }
            scroll.Content = null;
        }
        _screenOverlay.Child = null;
        _screenOverlay.IsVisible = false;
        _menuChoices.Clear();
        _navigationIndex = 0;
        _editingPacingParameter = false;
        if (restoreFocus && _overlayReturnFocus is { IsEffectivelyEnabled: true, IsEffectivelyVisible: true } previous)
        {
            previous.Focus();
        }
        _overlayReturnFocus = null;
    }

    private void AddMenuChoice(Panel panel, string key, Action<Button> action)
    {
        var button = ToolbarButton(key);
        button.Click += (_, _) => action(button);
        panel.Children.Add(button);
        _menuChoices.Add(button);
    }

    private void NavigateControls(int direction)
    {
        if (TherapyArea is not null)
        {
            Control[] parameters = [PacingType, Rate, Current, ApplyPacing, StopPacing];
            var selected = parameters[Math.Clamp(_navigationIndex, 0, parameters.Length - 1)];
            if (_editingPacingParameter)
            {
                if (selected is NumericUpDown number)
                { number.Value = Math.Clamp((number.Value ?? number.Minimum) + direction * number.Increment, number.Minimum, number.Maximum); }
                else if (selected == PacingType)
                { PacingType.SelectedIndex = Math.Clamp(PacingType.SelectedIndex + direction, 0, PacingType.ItemCount - 1); }
                return;
            }
            SelectNavigationTarget(parameters, direction);
            NavigationDial!.SetAngle(_navigationIndex * 45);
            return;
        }
        var choices = _menuChoices.Count > 0 ? _menuChoices : _softKeys;
        if (choices.Count == 0) { return; }
        SelectNavigationTarget(choices, direction);
        NavigationDial!.SetAngle(_navigationIndex * 45);
    }

    private void SelectNavigationTarget(IReadOnlyList<Control> choices, int direction)
    {
        for (int step = 1; step <= choices.Count; step++)
        {
            int index = (_navigationIndex + direction * step + choices.Count) % choices.Count;
            if (!choices[index].IsEffectivelyEnabled) { continue; }
            _navigationIndex = index;
            HighlightNavigationTarget((TemplatedControl)choices[index]);
            return;
        }
        HighlightNavigationTarget(null);
    }

    private void HighlightNavigationTarget(TemplatedControl? target)
    {
        if (!ReferenceEquals(_navigationTarget, target))
        {
            if (_navigationTarget is { } previous)
            {
                previous.BorderBrush = _navigationBorderBrush;
                previous.BorderThickness = _navigationBorderThickness;
            }
            _navigationTarget = target;
            _navigationBorderBrush = target?.BorderBrush;
            _navigationBorderThickness = target?.BorderThickness ?? default;
        }
        if (target is null) { return; }
        target.BorderBrush = Brush.Parse(_editingPacingParameter ? "#F6CE60" : "#59A88D");
        target.BorderThickness = new Thickness(2);
    }

    private void ActivateNavigationSelection()
    {
        if (TherapyArea is not null)
        {
            Control[] parameters = [PacingType, Rate, Current, ApplyPacing, StopPacing];
            var parameter = parameters[Math.Clamp(_navigationIndex, 0, parameters.Length - 1)];
            if (parameter is Button button) { ActivateControlButton(button, button); }
            else
            {
                _editingPacingParameter = !_editingPacingParameter;
                HighlightNavigationTarget((TemplatedControl)parameter);
            }
            return;
        }
        var choices = _menuChoices.Count > 0 ? _menuChoices : _softKeys;
        if (choices.Count == 0) { return; }
        var selected = choices[Math.Clamp(_navigationIndex, 0, choices.Count - 1)];
        ActivateControlButton(selected, selected);
    }

    private static void ActivateControlButton(Button button, Control anchor)
    {
        if (!button.IsEnabled) { return; }
        if (button.Flyout is { } flyout) { flyout.ShowAt(anchor); }
        else { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    }

    private Grid CreateHardwareKey(Button button, string symbol, string labelKey)
    {
        ApplyButtonMaterial(button, "key");
        // Keep the original TextBlock: NIBP and audio-pause refresh replace its text.
        if (button.Content is TextBlock original) { original.IsVisible = false; }
        button.MinHeight = 36;
        button.Margin = new Thickness(7, 0);
        var icon = CreateHardwareIcon(symbol);
        icon.IsHitTestVisible = false;
        var key = new Panel();
        key.Children.Add(button);
        key.Children.Add(icon);
        var panel = new Grid { RowDefinitions = new("*,20") };
        panel.Children.Add(key);
        var label = CreatePanelCaption(labelKey, 10, _package.Material("shell").Foreground);
        Grid.SetRow(label, 1);
        panel.Children.Add(label);
        _localization.Bind(button, AutomationProperties.NameProperty, labelKey);
        _poweredControls.Add(panel);
        return panel;
    }

    private Control CreateHardwareIcon(string symbol)
    {
        string? path = symbol switch
        {
            "◉" => "M 3,11 L 7,8 L 14,16 L 10,19 Z M 6,9 L 12,17 M 14,17 C 20,20 22,16 19,13 L 16,10 M 18,3 A 3,3 0 1 0 18,9 A 3,3 0 1 0 18,3 M 18,6 L 19,5",
            "♩" => "M 5,17 H 19 L 17,14 V 10 C 17,3 7,3 7,10 V 14 Z M 10,20 C 10,22 14,22 14,20 M 12,3 V 2 M 3,3 L 21,21",
            "⚑" => "M 5,22 V 3 M 5,4 C 10,0 14,8 20,4 V 14 C 14,18 10,10 5,14",
            "☷" => "M 3,4 H 21 V 20 H 3 Z M 6,8 H 18 M 6,12 H 18 M 6,16 H 18",
            "↕" => "M 4,3 V 21 M 1,6 L 4,3 L 7,6 M 1,18 L 4,21 L 7,18 M 10,12 H 13 L 15,5 L 18,19 L 20,12 H 23",
            _ => null
        };
        if (path is null) { return CreatePanelText(symbol, 18, _package.Material("key").Foreground, FontWeight.Bold); }
        return new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path),
            Stroke = _package.Material("key").Foreground,
            StrokeThickness = 1.5,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = 24,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private void ApplyButtonMaterial(Button button, string key)
    {
        var material = _package.Material(key);
        button.Background = material.Fill;
        button.Foreground = material.Foreground;
        button.BorderBrush = material.Border;
        button.BorderThickness = new Thickness(material.BorderThickness);
        button.CornerRadius = new CornerRadius(material.CornerRadius);
        button.Padding = new Thickness(8, 4);
        button.MinHeight = 0;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.VerticalAlignment = VerticalAlignment.Stretch;
        button.Template = new FuncControlTemplate<Button>((owner, _) =>
        {
            var presenter = new ContentPresenter
            {
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            presenter.Bind(ContentPresenter.ContentProperty, new Binding(nameof(Button.Content)) { Source = owner });
            var border = new Border { Child = presenter };
            border.Bind(Border.CornerRadiusProperty, new Binding(nameof(Button.CornerRadius)) { Source = owner });
            border.Bind(Border.BorderBrushProperty, new Binding(nameof(Button.BorderBrush)) { Source = owner });
            border.Bind(Border.BorderThicknessProperty, new Binding(nameof(Button.BorderThickness)) { Source = owner });
            var focus = new Border
            {
                BorderBrush = material.Foreground,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(Math.Max(0, material.CornerRadius - 3)),
                Margin = new Thickness(3),
                IsHitTestVisible = false
            };
            var face = new Panel();
            face.Children.Add(border);
            face.Children.Add(focus);
            void Refresh()
            {
                bool enabled = owner.IsEffectivelyEnabled;
                border.Background = enabled && (owner.IsPressed || owner.IsPointerOver) ? material.PressedFill : owner.Background;
                border.Opacity = enabled ? 1 : .55;
                focus.IsVisible = enabled && owner.IsFocused;
            }
            owner.PropertyChanged += (_, _) => Refresh();
            Refresh();
            return face;
        });
    }

    private TextBlock CreatePanelCaption(string key, double size, IBrush foreground)
    {
        var text = CreatePanelText("", size, foreground);
        text.TextWrapping = TextWrapping.Wrap;
        _localization.Bind(text, TextBlock.TextProperty, key);
        return text;
    }

    private static TextBlock CreatePanelText(string text, double size, IBrush foreground, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = foreground,
        FontWeight = weight ?? FontWeight.Normal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center
    };

    private static Border CreateIndicatorLamp(string color, double radius) => new()
    {
        Width = 12,
        Height = 10,
        CornerRadius = new CornerRadius(radius),
        Background = Brush.Parse(color),
        BorderBrush = Brush.Parse("#536554"),
        BorderThickness = new Thickness(.7),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static void PlaceOnEnclosure(Canvas canvas, Control control, double left, double top, double width, double height)
    {
        control.Width = width;
        control.Height = height;
        Canvas.SetLeft(control, left);
        Canvas.SetTop(control, top);
        canvas.Children.Add(control);
    }

    private static string ModeCaptionKey(DeviceOperatingMode mode) => mode switch
    {
        DeviceOperatingMode.Off => "devicePanel.modeOff",
        DeviceOperatingMode.Monitor => "devicePanel.modeMonitor",
        DeviceOperatingMode.Manual => "devicePanel.modeManual",
        DeviceOperatingMode.Aed => "skin.aed",
        DeviceOperatingMode.Pacer => "skin.pacer",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
