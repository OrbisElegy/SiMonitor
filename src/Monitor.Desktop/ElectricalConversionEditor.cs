// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Monitor.Application.Therapy;

namespace Monitor.Desktop;

internal sealed class ElectricalConversionEditor : UserControl
{
    private sealed record Draft(bool Enabled, decimal? Monophasic, decimal? Biphasic, decimal? PauseSeconds);
    private readonly Dictionary<string, Draft> _drafts = new(StringComparer.Ordinal);
    private readonly DesktopLocalization _localization;
    private string? _templateId;
    private bool _syncing;
    private readonly bool _automated;
    internal CheckBox Enabled { get; } = new();
    internal NumericUpDown Monophasic { get; } = Energy();
    internal NumericUpDown Biphasic { get; } = Energy();
    internal NumericUpDown PostShockPause { get; } = new() { Minimum = 0, Maximum = 10, Increment = 0.1m, FormatString = "0.0", Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
    internal TextBlock Error { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
    private readonly TextBlock _templateName = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly TextBlock _mode = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    internal ElectricalConversionEditor(DesktopLocalization localization, bool automated = false)
    {
        _localization = localization;
        _automated = automated;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(DesktopInformationPages.Help("electrical-conversion"));
        var heading = new TextBlock { FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        localization.Bind(heading, TextBlock.TextProperty, automated ? "conversion.aedTitle" : "conversion.title");
        panel.Children.Add(heading);
        panel.Children.Add(_templateName);
        panel.Children.Add(_mode);
        localization.Bind(Enabled, ContentControl.ContentProperty, "conversion.enabled");
        panel.Children.Add(Enabled);
        Add("conversion.monophasic", Monophasic);
        Add("conversion.biphasic", Biphasic);
        Add("conversion.postShockPause", PostShockPause);
        localization.Bind(Error, TextBlock.TextProperty, "conversion.invalid");
        panel.Children.Add(Error);
        Content = panel;
        Enabled.IsCheckedChanged += (_, _) => Changed();
        Monophasic.ValueChanged += (_, _) => Changed();
        Biphasic.ValueChanged += (_, _) => Changed();
        PostShockPause.ValueChanged += (_, _) => Changed();
        Select(EcgElectricalTherapy.SinusTemplateId);

        void Add(string key, NumericUpDown field)
        {
            var row = new StackPanel { Spacing = 6 };
            var label = new Label { Target = field, Padding = new Thickness(0) };
            localization.Bind(label, ContentControl.ContentProperty, key);
            localization.Bind(field, AutomationProperties.NameProperty, key);
            row.Children.Add(label);
            row.Children.Add(field);
            panel.Children.Add(row);
        }
    }

    private static NumericUpDown Energy() => new()
    {
        Minimum = 0,
        Maximum = ElectricalConversionSettings.MaximumEnergyJoules,
        Increment = 1,
        FormatString = "0",
        Width = 180,
        HorizontalAlignment = HorizontalAlignment.Left
    };

    private void Changed()
    {
        if (_syncing) { return; }
        if (_templateId is { } id) { _drafts[id] = new(Enabled.IsChecked == true, Monophasic.Value, Biphasic.Value, PostShockPause.Value); }
        Monophasic.IsEnabled = Biphasic.IsEnabled = PostShockPause.IsEnabled = Enabled.IsChecked == true;
        Error.IsVisible = _templateId is not null && !Valid(new(Enabled.IsChecked == true, Monophasic.Value, Biphasic.Value, PostShockPause.Value));
    }

    internal void Select(string templateId)
    {
        var descriptor = EcgElectricalTherapy.Find(templateId);
        if (_automated && descriptor?.Requirement == ElectricalShockRequirement.Synchronized) { descriptor = null; }
        _templateId = descriptor?.TemplateId;
        IsVisible = descriptor is not null;
        if (descriptor is null) { return; }
        var defaults = ElectricalConversionSettings.Default;
        var draft = _drafts.GetValueOrDefault(templateId) ?? new(defaults.Enabled, defaults.MonophasicThresholdJoules, defaults.BiphasicThresholdJoules, defaults.PostShockPauseMilliseconds / 1000m);
        _syncing = true;
        Enabled.IsChecked = draft.Enabled;
        Monophasic.Value = draft.Monophasic;
        Biphasic.Value = draft.Biphasic;
        PostShockPause.Value = draft.PauseSeconds;
        _syncing = false;
        _localization.Bind(_templateName, TextBlock.TextProperty, text => text.Format("conversion.template", text.GetString(templateId)));
        _localization.Bind(_mode, TextBlock.TextProperty, descriptor.Requirement switch
        {
            ElectricalShockRequirement.Synchronized => "conversion.synchronized",
            ElectricalShockRequirement.Unsynchronized => "conversion.unsynchronized",
            _ => "conversion.ventricular"
        });
        Changed();
    }

    private static bool Valid(Draft draft) => draft.Monophasic is { } mono && draft.Biphasic is { } bi &&
        mono is >= 0 and <= ElectricalConversionSettings.MaximumEnergyJoules && bi is >= 0 and <= ElectricalConversionSettings.MaximumEnergyJoules &&
        mono == decimal.Truncate(mono) && bi == decimal.Truncate(bi) && draft.PauseSeconds is { } pause &&
        pause is >= 0 and <= 10 && pause * 1000 == decimal.Truncate(pause * 1000);

    internal IReadOnlyDictionary<string, ElectricalConversionSettings> Capture()
    {
        if (_drafts.Values.Any(d => !Valid(d))) { throw new ArgumentException("ElectricalConversion.InvalidDraft"); }
        return EcgElectricalTherapy.Snapshot(_drafts.ToDictionary(p => p.Key,
            p => new ElectricalConversionSettings(p.Value.Enabled, (int)p.Value.Monophasic!.Value, (int)p.Value.Biphasic!.Value) { PostShockPauseMilliseconds = checked((int)(p.Value.PauseSeconds!.Value * 1000)) }));
    }

    internal EcgElectricalTherapyProfile Profile(string templateId)
    {
        var settings = Capture();
        return new(templateId, settings.GetValueOrDefault(templateId) ?? ElectricalConversionSettings.Default);
    }

    internal void Restore(IReadOnlyDictionary<string, ElectricalConversionSettings> settings)
    {
        var owned = EcgElectricalTherapy.Snapshot(settings);
        _drafts.Clear();
        foreach (var (key, value) in owned)
        { _drafts.Add(key, new(value.Enabled, value.MonophasicThresholdJoules, value.BiphasicThresholdJoules, value.PostShockPauseMilliseconds / 1000m)); }
        Select(_templateId ?? EcgElectricalTherapy.SinusTemplateId);
    }
}
