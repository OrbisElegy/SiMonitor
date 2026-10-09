// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Controls;
using Avalonia.Media;
using Monitor.Application.Therapy;

namespace Monitor.Desktop;

internal sealed class PacingPermissionEditor : UserControl
{
    private readonly Dictionary<string, bool> _permissions = new(StringComparer.Ordinal);
    private readonly DesktopLocalization _localization;
    private readonly TextBlock _template = new() { TextWrapping = TextWrapping.Wrap };
    private string _templateId = EcgElectricalTherapy.SinusTemplateId;
    private bool _syncing;
    internal CheckBox Allowed { get; } = new();

    internal PacingPermissionEditor(DesktopLocalization localization)
    {
        _localization = localization;
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(DesktopInformationPages.Help("pacing"));
        var heading = new TextBlock { FontSize = 18, FontWeight = FontWeight.SemiBold };
        localization.Bind(heading, TextBlock.TextProperty, "pacingPermission.title");
        panel.Children.Add(heading);
        panel.Children.Add(_template);
        localization.Bind(Allowed, ContentControl.ContentProperty, "pacingPermission.allowed");
        panel.Children.Add(Allowed);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap };
        localization.Bind(hint, TextBlock.TextProperty, "pacingPermission.applyHint");
        panel.Children.Add(hint);
        Content = panel;
        Allowed.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing) { _permissions[_templateId] = Allowed.IsChecked == true; }
        };
        Select(_templateId);
    }

    internal void Select(string templateId)
    {
        if (!EcgPacingPermissions.TemplateIds.Contains(templateId, StringComparer.Ordinal))
        { throw new ArgumentException("Pacing.InvalidTemplatePermissions", nameof(templateId)); }
        _templateId = templateId;
        _syncing = true;
        Allowed.IsChecked = Allows(templateId);
        _syncing = false;
        _localization.Bind(_template, TextBlock.TextProperty, text => text.GetString(templateId));
    }

    internal bool Allows(string templateId) => _permissions.GetValueOrDefault(templateId);
    internal IReadOnlyDictionary<string, bool> Capture() => EcgPacingPermissions.Snapshot(_permissions);
    internal void Restore(IReadOnlyDictionary<string, bool> permissions)
    {
        var owned = EcgPacingPermissions.Snapshot(permissions);
        _permissions.Clear();
        foreach (var (key, value) in owned) { _permissions.Add(key, value); }
        Select(_templateId);
    }
}
