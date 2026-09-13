// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Domain.Presentation;

public enum Ecg12ZoomMode { FitPage, ActualSize, ExplicitScale }
public sealed record Ecg12ZoomState(Ecg12ZoomMode Mode, uint Numerator, uint Denominator);
public sealed record Ecg12ZoomDisplay(Ecg12ZoomState Selection, SystemViewCommandAssessmentPolicy Policy,
    bool CanSelect, string ReasonCode);
public sealed class Ecg12ZoomSelectionException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

// Screen-only intent. Fit-page geometry and approved zoom steps belong to the shell.
// This state neither changes record coordinates nor defines physical print scale.
public sealed class Ecg12ZoomSelection
{
    private SystemViewCommandAssessmentPolicy _policy;

    public Ecg12ZoomSelection(Ecg12ZoomState initial, SystemViewCommandAssessmentPolicy policy)
    {
        Ecg12ZoomState candidate = Normalize(initial);
        UpdatePolicy(policy);
        Selection = candidate;
    }

    public Ecg12ZoomState Selection { get; private set; }

    public void UpdatePolicy(SystemViewCommandAssessmentPolicy policy)
    {
        if (!Enum.IsDefined(policy))
        { throw new Ecg12ZoomSelectionException("Ecg12Zoom.InvalidPolicy", nameof(policy)); }
        _policy = policy;
    }

    public Ecg12ZoomDisplay CaptureDisplay() => new(Selection, _policy,
        _policy == SystemViewCommandAssessmentPolicy.Enabled, _policy switch
        {
            SystemViewCommandAssessmentPolicy.Disabled => "Ecg12Zoom.Disabled",
            SystemViewCommandAssessmentPolicy.CourseLocked => "Ecg12Zoom.CourseLocked",
            _ => "Ecg12Zoom.Enabled",
        });

    public void Select(Ecg12ZoomState selection)
    {
        Ecg12ZoomDisplay current = CaptureDisplay();
        if (!current.CanSelect)
        { throw new Ecg12ZoomSelectionException(current.ReasonCode, nameof(selection)); }
        Selection = Normalize(selection);
    }

    public Ecg12ZoomState CaptureState() => Selection;

    public static Ecg12ZoomSelection Restore(Ecg12ZoomState state,
        SystemViewCommandAssessmentPolicy currentPolicy) => new(state, currentPolicy);

    private static Ecg12ZoomState Normalize(Ecg12ZoomState state)
    {
        if (state is null || !Enum.IsDefined(state.Mode) || state.Numerator == 0 || state.Denominator == 0 ||
            (state.Mode != Ecg12ZoomMode.ExplicitScale && (state.Numerator != 1 || state.Denominator != 1)))
        { throw new Ecg12ZoomSelectionException("Ecg12Zoom.InvalidSelection", nameof(state)); }
        uint a = state.Numerator;
        uint b = state.Denominator;
        while (b != 0) { (a, b) = (b, a % b); }
        return new(state.Mode, state.Numerator / a, state.Denominator / a);
    }
}
