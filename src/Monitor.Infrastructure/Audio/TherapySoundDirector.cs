// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;

namespace Monitor.Infrastructure.Audio;

public enum TherapySoundPhase
{
    Silent,
    Analyzing,
    Charging,
    Ready,
    CprShock,
    CprNoShock,
    Cancelled
}
public sealed record TherapySoundRequest(ulong Revision, TherapySoundPhase Phase, string Locale,
    bool Automated, int ChargeProgressPermille, int CprElapsedMilliseconds)
{
    public bool Announce { get; init; }
    public bool EnteringAed { get; init; }
    public bool AfterCpr { get; init; }
    public bool RhythmChanged { get; init; }
    public bool Reanalyzing { get; init; }
    public void Validate()
    {
        if (!Enum.IsDefined(Phase) || Locale is not ("en" or "zh-CN") || ChargeProgressPermille is < 0 or > 1000 ||
            CprElapsedMilliseconds is < 0 or > 120000)
        { throw new ArgumentException("TherapySound.InvalidRequest"); }
    }
}

// UI/scheduler owner. Commands are snapshots, not a queue of delayed announcements.
public sealed class TherapySoundDirector
{
    private AutomatedExternalDefibrillator? _owner;
    private AedPhase _previousAed;
    private TherapySoundRequest? _current;
    private ulong _revision;

    public TherapySoundRequest Update(ManualDefibrillator device, AutomatedExternalDefibrillator aed, string locale, bool active)
    {
        if (locale is not ("en" or "zh-CN")) { throw new ArgumentException("TherapySound.InvalidLocale", nameof(locale)); }
        bool replaced = !ReferenceEquals(_owner, aed);
        var previous = replaced ? AedPhase.Off : _previousAed;
        var phase = !active ? TherapySoundPhase.Silent : aed.Phase switch
        {
            AedPhase.Analyzing => TherapySoundPhase.Analyzing,
            AedPhase.Charging => TherapySoundPhase.Charging,
            AedPhase.ShockAdvised => TherapySoundPhase.Ready,
            AedPhase.Cpr => aed.Advice == AedAdvice.Shock ? TherapySoundPhase.CprShock : TherapySoundPhase.CprNoShock,
            AedPhase.Suspended when previous is AedPhase.Charging or AedPhase.ShockAdvised ||
                previous == AedPhase.Suspended && _current?.Phase == TherapySoundPhase.Cancelled => TherapySoundPhase.Cancelled,
            AedPhase.Off when device.State.Energy == EnergyState.Charging => TherapySoundPhase.Charging,
            AedPhase.Off when device.State.Energy == EnergyState.Ready => TherapySoundPhase.Ready,
            _ => TherapySoundPhase.Silent
        };
        bool changed = replaced || _current is null || _current.Phase != phase || _current.Automated != aed.Enabled;
        bool languageChanged = _current?.Locale != locale;
        if (changed || languageChanged)
        {
            _current = new(++_revision, phase, locale, aed.Enabled, 0, 0)
            {
                Announce = changed && phase != TherapySoundPhase.Silent,
                EnteringAed = previous == AedPhase.Off && aed.Enabled,
                AfterCpr = previous == AedPhase.Cpr,
                RhythmChanged = previous is AedPhase.Charging or AedPhase.ShockAdvised,
                Reanalyzing = previous == AedPhase.Suspended
            };
        }
        _owner = aed;
        _previousAed = active ? aed.Phase : AedPhase.Off;
        _current = _current! with
        {
            ChargeProgressPermille = device.ChargeProgressPermille,
            CprElapsedMilliseconds = (int)(aed.CprElapsedNs / 1_000_000)
        };
        return _current;
    }
}
