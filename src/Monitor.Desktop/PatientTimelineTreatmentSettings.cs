// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal sealed class PatientTimelineTreatmentSettings : StackPanel
{
    private readonly DesktopLocalization _localization;
    private readonly StackPanel _fields = new() { Spacing = 10 };
    private string?[] _ids = [null];
    private string?[] _savedRoutes = [null, null, null];
    private (string Id, string Name)[]? _choices;
    private bool _refreshing;
    internal event Action? Changed;
    internal CheckBox Override { get; } = new();
    internal CheckBox AllowShock { get; } = new() { IsChecked = true };
    internal CheckBox AllowPacing { get; } = new() { IsChecked = true };
    internal ComboBox[] Routes { get; } = [Choice(), Choice(), Choice()];

    internal PatientTimelineTreatmentSettings(DesktopLocalization localization)
    {
        _localization = localization;
        Spacing = 10;
        localization.Bind(Override, ContentControl.ContentProperty, "timeline.nodeTreatment");
        localization.Bind(AllowShock, ContentControl.ContentProperty, "timeline.allowShock");
        localization.Bind(AllowPacing, ContentControl.ContentProperty, "timeline.allowPacing");
        Children.Add(Override);
        _fields.Children.Add(AllowShock);
        _fields.Children.Add(AllowPacing);
        string[] keys = ["timeline.shockSuccess", "timeline.shockFailure", "timeline.pacingStarted"];
        for (int index = 0; index < Routes.Length; index++)
        {
            var label = new Label { Target = Routes[index], Padding = new Thickness(0) };
            localization.Bind(label, ContentControl.ContentProperty, keys[index]);
            localization.Bind(Routes[index], AutomationProperties.NameProperty, keys[index]);
            _fields.Children.Add(label);
            _fields.Children.Add(Routes[index]);
            Routes[index].SelectionChanged += (_, _) => NotifyChanged();
        }
        Children.Add(_fields);
        Override.IsCheckedChanged += (_, _) => NotifyChanged();
        AllowShock.IsCheckedChanged += (_, _) => NotifyChanged();
        AllowPacing.IsCheckedChanged += (_, _) => NotifyChanged();
        SetTimelines([]);
        NotifyChanged();
    }

    internal PatientTimelineTreatmentPolicy? Read() => Override.IsChecked == true
        ? new(AllowShock.IsChecked == true, AllowPacing.IsChecked == true, RouteId(0), RouteId(1), RouteId(2)) : null;

    internal void Restore(PatientTimelineTreatmentPolicy? policy)
    {
        _refreshing = true;
        Override.IsChecked = policy is not null;
        AllowShock.IsChecked = policy?.AllowShock ?? true;
        AllowPacing.IsChecked = policy?.AllowPacing ?? true;
        _savedRoutes = [policy?.ShockSuccessTimelineId, policy?.ShockFailureTimelineId, policy?.PacingTimelineId];
        for (int index = 0; index < Routes.Length; index++)
        { Routes[index].SelectedIndex = Array.IndexOf(_ids, _savedRoutes[index]); }
        _refreshing = false;
        NotifyChanged();
    }

    internal void SetTimelines(IReadOnlyList<(string Id, string Name)> timelines)
    {
        if (_choices is not null && _choices.SequenceEqual(timelines)) { return; }
        _choices = timelines.ToArray();
        string?[] selected = Enumerable.Range(0, 3).Select(RouteId).ToArray();
        _refreshing = true;
        _ids = timelines.Select(item => (string?)item.Id).Prepend(null).ToArray();
        for (int index = 0; index < Routes.Length; index++)
        {
            _localization.SetChoices(Routes[index], timelines.Select(item =>
                (Func<Monitor.Application.Localization.ITextLocalizer, string>)(_ => item.Name))
                .Prepend(text => text.GetString("timeline.continueCurrent")));
            Routes[index].SelectedIndex = Array.IndexOf(_ids, selected[index]);
        }
        _savedRoutes = selected;
        _refreshing = false;
    }

    private string? RouteId(int index) => Routes[index].SelectedIndex is var selected && selected >= 0 && selected < _ids.Length
        ? _ids[selected] : _savedRoutes[index];

    private void NotifyChanged()
    {
        _fields.IsVisible = Override.IsChecked == true;
        if (_refreshing) { return; }
        _savedRoutes = Enumerable.Range(0, 3).Select(RouteId).ToArray();
        Changed?.Invoke();
    }

    private static ComboBox Choice() => new() { MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
}
