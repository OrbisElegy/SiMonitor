// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

public enum Ecg12Theme { MonitorDarkGreen, PaperGridBlack }
public sealed record Ecg12ThemeState(Ecg12Theme Theme);
public sealed record Ecg12ThemeDisplay(Ecg12Theme Theme, SystemViewCommandAssessmentPolicy Policy,
    bool CanSelect, string ReasonCode);
public sealed class Ecg12ThemeSelectionException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

// Trusted initialization and serialized resolved policy; no waveform or geometry mutation.
public sealed class Ecg12ThemeSelection
{
    private SystemViewCommandAssessmentPolicy _policy;
    private bool _allowLocalSelection;

    public Ecg12ThemeSelection(Ecg12Theme initialTheme, SystemViewCommandAssessmentPolicy policy, bool allowLocalSelection)
    {
        ValidateTheme(initialTheme);
        UpdatePolicy(policy, allowLocalSelection);
        Theme = initialTheme;
    }

    public Ecg12Theme Theme { get; private set; }

    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy, bool allowLocalSelection)
    {
        if (!Enum.IsDefined(policy))
        { throw new Ecg12ThemeSelectionException("Ecg12Theme.InvalidPolicy", nameof(policy)); }
        _policy = policy;
        _allowLocalSelection = allowLocalSelection;
    }

    public Ecg12ThemeDisplay CaptureDisplay()
    {
        string reason = _policy switch
        {
            SystemViewCommandAssessmentPolicy.Disabled => "Ecg12Theme.Disabled",
            SystemViewCommandAssessmentPolicy.CourseLocked => "Ecg12Theme.CourseLocked",
            _ => _allowLocalSelection ? "Ecg12Theme.Enabled" : "Ecg12Theme.LocalSelectionNotAllowed",
        };
        return new(Theme, _policy, _policy == SystemViewCommandAssessmentPolicy.Enabled && _allowLocalSelection, reason);
    }

    public void Select(Ecg12Theme theme)
    {
        Ecg12ThemeDisplay current = CaptureDisplay();
        if (!current.CanSelect) { throw new Ecg12ThemeSelectionException(current.ReasonCode, nameof(theme)); }
        ValidateTheme(theme);
        Theme = theme;
    }

    public Ecg12ThemeState CaptureState() => new(Theme);

    public static Ecg12ThemeSelection Restore(Ecg12ThemeState state,
        SystemViewCommandAssessmentPolicy currentPolicy, bool allowLocalSelection)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new(state.Theme, currentPolicy, allowLocalSelection);
    }

    private static void ValidateTheme(Ecg12Theme theme)
    {
        if (!Enum.IsDefined(theme))
        { throw new Ecg12ThemeSelectionException("Ecg12Theme.InvalidTheme", nameof(theme)); }
    }
}
