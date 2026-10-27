// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;

namespace Monitor.Desktop;

internal enum DefibrillatorCommand
{
    Charge,
    PressShock,
    ReleaseShock,
    Disarm,
    ToggleSync,
    ToggleAed,
    PauseAedForCpr,
    ResumeAedAnalysis
}

internal sealed class DefibrillatorControls : StackPanel
{
    private readonly DesktopLocalization _localization;
    private readonly Action<DefibrillatorCommand>? _command;
    private string? _shownStatus;
    private bool? _shownSync;
    private bool? _shownAed;
    private string? _shownChargeKey;
    private bool _fixedHardwareLabels;
    private bool _holding;
    private bool _pointerHolding;
    private IPointer? _capturedPointer;
    private Key? _heldKey;
    private readonly Border _aedLamp = Lamp();
    private readonly Border _syncLamp = Lamp();
    private readonly Border _chargeFill = new() { CornerRadius = new CornerRadius(4) };
    internal ComboBox Energy { get; } = new() { MinHeight = 36, HorizontalAlignment = HorizontalAlignment.Stretch };
    internal Button LowerEnergy { get; } = ActionButton();
    internal Button HigherEnergy { get; } = ActionButton();
    internal Button Aed { get; } = ActionButton();
    internal Button Charge { get; } = ActionButton();
    internal Button Shock { get; } = ActionButton();
    internal Button Disarm { get; } = ActionButton();
    internal Button Sync { get; } = ActionButton();
    internal int ChargeProgressPermille { get; private set; } = -1;
    internal Border ChargeFill => _chargeFill;
    internal TextBlock Status { get; } = new() { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    internal bool ShockLit { get; private set; }
    internal bool ChargeLit { get; private set; }
    internal bool SyncLit { get; private set; }
    internal bool AedLit { get; private set; }
    internal AedPhase? CurrentAedPhase { get; private set; }
    internal bool AedSoftAvailable { get; private set; }
    internal event Action? StateRefreshed;
    internal void ReleaseHeldInput() => Release();

    internal void RunAedSoftCommand()
    {
        if (!AedSoftAvailable) { return; }
        _command?.Invoke(CurrentAedPhase is AedPhase.Cpr or AedPhase.Suspended
            ? DefibrillatorCommand.ResumeAedAnalysis : DefibrillatorCommand.PauseAedForCpr);
    }

    // A device skin reuses the actual controls, including the held-shock handlers.
    // Removing the generic groups first avoids assigning one control to two parents.
    internal void DetachPanelControls()
    {
        Release();
        foreach (var panel in Children.OfType<Panel>().ToArray()) { panel.Children.Clear(); }
        Children.Clear();
    }

    internal void UseFixedHardwareLabels()
    {
        if (_fixedHardwareLabels) { return; }
        _fixedHardwareLabels = true;
        _shownChargeKey = "skin.charge";
        _localization.Bind(Charge, ContentControl.ContentProperty, "skin.charge");
        _localization.Bind(Charge, AutomationProperties.NameProperty, "skin.charge");
        Charge.Padding = new Thickness(6, 4);
        Charge.HorizontalContentAlignment = HorizontalAlignment.Center;
        Charge.VerticalContentAlignment = VerticalAlignment.Center;
        _chargeFill.Background = null;
        if (CurrentAedPhase is not null) { Charge.IsEnabled = false; }
    }

    internal DefibrillatorControls(DesktopLocalization localization, DefibrillatorConfiguration configuration,
        int selectedEnergyJoules, Action<DefibrillatorCommand>? command)
    {
        _localization = localization;
        _command = command;
        Spacing = 6;
        var owned = configuration.Snapshot();
        Energy.ItemsSource = owned.EnergyStepsJoules;
        Energy.SelectedItem = owned.NearestEnergy(selectedEnergyJoules);
        localization.Bind(Energy, AutomationProperties.NameProperty, "skin.energy");
        LowerEnergy.Content = "−";
        HigherEnergy.Content = "+";
        localization.Bind(LowerEnergy, AutomationProperties.NameProperty, "defib.lowerEnergy");
        localization.Bind(HigherEnergy, AutomationProperties.NameProperty, "defib.higherEnergy");
        LowerEnergy.Click += (_, _) => { if (Energy.SelectedIndex > 0) { Energy.SelectedIndex--; } };
        HigherEnergy.Click += (_, _) => { if (Energy.SelectedIndex < Energy.ItemCount - 1) { Energy.SelectedIndex++; } };
        Energy.SelectionChanged += (_, _) => RefreshEnergyButtons();
        RefreshEnergyButtons();
        Aed.Content = IndicatorCaption(_aedLamp, "skin.aed");
        localization.Bind(Aed, AutomationProperties.NameProperty, "aed.off");
        Aed.Click += (_, _) => command?.Invoke(DefibrillatorCommand.ToggleAed);
        Children.Add(Aed);
        var energyLabel = new TextBlock { Foreground = Brush.Parse("#BAC5D1"), FontSize = 12 };
        localization.Bind(energyLabel, TextBlock.TextProperty, "skin.energy");
        Children.Add(energyLabel);
        var energyRow = new Grid { ColumnDefinitions = new("36,*,36") };
        foreach (var (control, column) in new (Control, int)[] { (LowerEnergy, 0), (Energy, 1), (HigherEnergy, 2) })
        { Grid.SetColumn(control, column); energyRow.Children.Add(control); }
        Children.Add(energyRow);
        foreach (var (button, key) in new[] { (Shock, "skin.shock"), (Disarm, "skin.disarm") })
        { button.HorizontalContentAlignment = HorizontalAlignment.Center; localization.Bind(button, ContentControl.ContentProperty, key); localization.Bind(button, AutomationProperties.NameProperty, key); }
        var chargeCaption = new TextBlock
        { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White };
        localization.Bind(chargeCaption, TextBlock.TextProperty, "skin.charge");
        localization.Bind(Charge, AutomationProperties.NameProperty, "skin.charge");
        _chargeFill.Child = chargeCaption;
        _chargeFill.IsHitTestVisible = false;
        Charge.Content = _chargeFill;
        Charge.Padding = new Thickness(0);
        Charge.VerticalContentAlignment = VerticalAlignment.Stretch;
        Sync.Content = IndicatorCaption(_syncLamp, "skin.sync");
        localization.Bind(Sync, AutomationProperties.NameProperty, "skin.sync");
        localization.Bind(Shock, ToolTip.TipProperty, "defib.holdHint");
        Charge.Click += (_, _) =>
        {
            if (_fixedHardwareLabels && (!Charge.IsEnabled || CurrentAedPhase is not null)) { return; }
            command?.Invoke(DefibrillatorCommand.Charge);
        };
        Disarm.Click += (_, _) => command?.Invoke(DefibrillatorCommand.Disarm);
        Sync.Click += (_, _) => command?.Invoke(DefibrillatorCommand.ToggleSync);
        // Click never authorizes a shock. Both keyboard and pointer must remain held.
        Shock.AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (!Shock.IsEnabled || !args.GetCurrentPoint(Shock).Properties.IsLeftButtonPressed || _holding) { return; }
            args.Handled = true;
            args.Pointer.Capture(Shock);
            _capturedPointer = args.Pointer;
            _pointerHolding = true;
            Hold();
        }, RoutingStrategies.Tunnel);
        Shock.AddHandler(PointerReleasedEvent, (_, args) =>
        {
            if (!_pointerHolding) { return; }
            args.Handled = true;
            Release();
            args.Pointer.Capture(null);
        }, RoutingStrategies.Tunnel);
        Shock.PointerCaptureLost += (_, _) => Release();
        Shock.PointerExited += (_, _) => { if (_pointerHolding) { Release(); } };
        Shock.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (args.Key is not (Key.Space or Key.Enter) || !Shock.IsEnabled) { return; }
            args.Handled = true;
            if (!_holding) { _heldKey = args.Key; Hold(); }
        }, RoutingStrategies.Tunnel);
        Shock.AddHandler(KeyUpEvent, (_, args) =>
        {
            if (args.Key != _heldKey) { return; }
            args.Handled = true;
            Release();
        }, RoutingStrategies.Tunnel);
        Shock.LostFocus += (_, _) => Release();
        Shock.DetachedFromVisualTree += (_, _) => Release();
        DetachedFromVisualTree += (_, _) => Release();
        Children.Add(Charge);
        Children.Add(Shock);
        var bottom = new UniformGrid { Columns = 2 };
        bottom.Children.Add(Disarm);
        bottom.Children.Add(Sync);
        Children.Add(bottom);
        Children.Add(Status);
    }

    private void Hold() { _holding = true; _command?.Invoke(DefibrillatorCommand.PressShock); }
    private void Release()
    {
        bool wasHolding = _holding;
        var pointer = _capturedPointer;
        _capturedPointer = null;
        _holding = false;
        _heldKey = null;
        _pointerHolding = false;
        pointer?.Capture(null);
        if (wasHolding) { _command?.Invoke(DefibrillatorCommand.ReleaseShock); }
    }

    private void RefreshEnergyButtons()
    {
        LowerEnergy.IsEnabled = Energy.SelectedIndex > 0;
        HigherEnergy.IsEnabled = Energy.SelectedIndex >= 0 && Energy.SelectedIndex < Energy.ItemCount - 1;
    }

    internal void Refresh(ManualDefibrillator device, long safetyTimeNs, bool available,
        AutomatedExternalDefibrillator? aed = null, bool aedAvailable = true, bool treatmentAllowed = true)
    {
        bool automated = aed?.Enabled == true;
        CurrentAedPhase = automated ? aed!.Phase : null;
        AedSoftAvailable = automated && available;
        var state = device.State;
        bool ready = state.Energy == EnergyState.Ready;
        bool charging = state.Energy == EnergyState.Charging;
        bool holding = state.Attempt is DefibrillationAttemptState.DischargeRequested or DefibrillationAttemptState.AwaitingSync;
        Aed.IsEnabled = automated || available && aedAvailable;
        Charge.IsEnabled = available && (!automated || !_fixedHardwareLabels && aed!.Phase == AedPhase.Suspended);
        Shock.IsEnabled = available && ready && (!automated || aed!.Phase == AedPhase.ShockAdvised);
        Disarm.IsEnabled = charging || ready || automated && aed!.Phase != AedPhase.Suspended;
        Sync.IsEnabled = available && !automated;
        Energy.IsEnabled = !holding && !automated;
        LowerEnergy.IsEnabled = Energy.IsEnabled && Energy.SelectedIndex > 0;
        HigherEnergy.IsEnabled = Energy.IsEnabled && Energy.SelectedIndex < Energy.ItemCount - 1;
        ChargeLit = ready || charging && device.ChargeProgressPermille > 0;
        SyncLit = state.Mode == DefibrillationMode.ManualSynchronized;
        ShockLit = ready && safetyTimeNs / 400_000_000 % 2 == 0;
        int progress = ready ? 1000 : charging ? device.ChargeProgressPermille : 0;
        if (ChargeProgressPermille != progress)
        {
            ChargeProgressPermille = progress;
            if (!_fixedHardwareLabels)
            {
                double fraction = progress / 1000d;
                var filled = Color.Parse("#9C6510");
                var empty = Color.Parse("#30291B");
                _chargeFill.Background = new LinearGradientBrush
                {
                    StartPoint = RelativePoint.TopLeft,
                    EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                    GradientStops = new GradientStops
                    { new(filled, 0), new(filled, fraction), new(empty, fraction), new(empty, 1) }
                };
            }
        }
        Shock.Background = Brush.Parse(ShockLit ? "#D83B45" : "#562831");
        _syncLamp.Background = Brush.Parse(SyncLit ? "#53F28C" : "#39434B");
        AedLit = automated && (aed!.Phase is AedPhase.Cpr or AedPhase.Suspended || safetyTimeNs / 400_000_000 % 2 == 0);
        _aedLamp.Background = Brush.Parse(!AedLit ? "#39434B" : aed!.Phase == AedPhase.ShockAdvised ? "#F25B65" :
            aed.Phase is AedPhase.WaitingForSignal or AedPhase.Suspended ? "#EDB64D" : "#53F28C");
        string key = !treatmentAllowed ? "timeline.shockDenied" : automated ? aed!.Phase == AedPhase.Cpr ? "aed.cpr." + aed.Advice : "aed.phase." + aed.Phase :
            !available ? "defib.unavailable" : "defib.state." + state.Attempt;
        int seconds = aed?.RemainingSeconds ?? 0;
        string statusIdentity = key + ":" + seconds;
        if (_shownStatus != statusIdentity)
        {
            _shownStatus = statusIdentity;
            _localization.Bind(Status, TextBlock.TextProperty, key,
                automated && aed!.Phase is AedPhase.Analyzing or AedPhase.Cpr ? [seconds] : []);
        }
        if (_shownAed != automated)
        {
            _shownAed = automated;
            _localization.Bind(Aed, AutomationProperties.NameProperty, automated ? "aed.on" : "aed.off");
        }
        string chargeKey = !_fixedHardwareLabels && automated && aed!.Phase == AedPhase.Suspended ? "aed.analyze" : "skin.charge";
        if (_shownChargeKey != chargeKey)
        {
            _shownChargeKey = chargeKey;
            _localization.Bind((TextBlock)_chargeFill.Child!, TextBlock.TextProperty, chargeKey);
            _localization.Bind(Charge, AutomationProperties.NameProperty, chargeKey);
        }
        if (_shownSync != SyncLit)
        {
            _shownSync = SyncLit;
            _localization.Bind(Sync, AutomationProperties.NameProperty, SyncLit ? "defib.syncOn" : "defib.syncOff");
        }
        StateRefreshed?.Invoke();
    }

    private Grid IndicatorCaption(Border lamp, string key)
    {
        var row = new Grid { ColumnDefinitions = new("16,*") };
        row.Children.Add(lamp);
        var caption = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _localization.Bind(caption, TextBlock.TextProperty, key);
        Grid.SetColumn(caption, 1);
        row.Children.Add(caption);
        return row;
    }

    private static Border Lamp() => new()
    { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Brush.Parse("#39434B"), VerticalAlignment = VerticalAlignment.Center };

    private static Button ActionButton() => new()
    {
        MinHeight = 40,
        Padding = new Thickness(6, 4),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Background = Brush.Parse("#222C38"),
        Foreground = Brushes.White,
        CornerRadius = new CornerRadius(5),
        FontSize = 14
    };
}
