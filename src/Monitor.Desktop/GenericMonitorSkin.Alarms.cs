// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed partial class GenericMonitorSkin
{
    internal Button AlarmsButton { get; private set; } = null!;
    internal Button SilenceButton { get; private set; } = null!;
    internal AlarmListPanel AlarmList { get; private set; } = null!;
    internal event Action? AlarmsRequested;
    internal event Action? SilenceRequested;
    internal Func<AlarmAttentionJournal, AlarmAttentionSnapshot, bool>? AcknowledgeAlarm { get; set; }
    private string? _shownAudioPause;

    private void AddAlarmControls(Panel toolbar)
    {
        AlarmsButton = ToolbarButton("skin.alarms");
        _localization.Bind(AlarmsButton, AutomationProperties.NameProperty, "skin.alarms");
        AlarmList = new(_localization, (owner, state) => AcknowledgeAlarm?.Invoke(owner, state) == true);
        AlarmsButton.Flyout = new Flyout { Content = AlarmList, Placement = PlacementMode.Top };
        AlarmsButton.Flyout.Opened += (_, _) => AlarmsRequested?.Invoke();
        AlarmsButton.Click += (_, _) => AlarmsRequested?.Invoke();
        toolbar.Children.Add(AlarmsButton);
        SilenceButton = ToolbarButton("skin.silence");
        SilenceButton.Click += (_, _) => SilenceRequested?.Invoke();
        toolbar.Children.Add(SilenceButton);
        RefreshAlarmAudioPause("");
    }

    internal void RefreshAlarmAudioPause(string text)
    {
        if (SilenceButton is null || _shownAudioPause == text) { return; }
        _shownAudioPause = text;
        bool paused = text.Length > 0;
        string key = paused ? "sound.resume" : "skin.silence";
        _localization.Bind((TextBlock)SilenceButton.Content!, TextBlock.TextProperty, key);
        _localization.Bind(SilenceButton, AutomationProperties.NameProperty, key);
        if (paused) { ToolTip.SetTip(SilenceButton, text); }
        else { _localization.Bind(SilenceButton, ToolTip.TipProperty, "sound.pause"); }
        SilenceButton.Background = Brush.Parse(paused ? "#58451C" : "#222C38");
    }
}
