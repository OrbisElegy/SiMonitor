// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;

namespace Monitor.Specs;

internal static class ManualVitalSpecifications
{
    internal static readonly Specification[] All =
    [
        new(nameof(ManualValuesValidateAndRemainIndependent), ManualValuesValidateAndRemainIndependent),
        new(nameof(ManualValuesFollowScheduledSource), ManualValuesFollowScheduledSource)
    ];

    private static void ManualValuesValidateAndRemainIndependent()
    {
        Reject(() => _ = new ManualNibp(120, 80, 121));
        Reject(() => _ = new ManualNibp(120, -1, 90));
        Reject(() => _ = new ManualNibp(301, 80, 90));
        Reject(() => _ = new ManualVitalSigns(temperatureDeciCelsius: 501));
        Reject(() => _ = new ManualCustomVital(" ", 1, ""));
        Reject(() => _ = new ManualCustomVital("a\nb", 1, ""));
        Reject(() => _ = new ManualCustomVital("ICP", 1.001m, "mmHg"));
        var values = new ManualVitalSigns(new(120, 80, 93), 365,
            new("  ICP  ", -1.25m, " mmHg "), new("Other", 0, ""));
        if (values.Custom1!.Name != "ICP" || values.Custom1.Unit != "mmHg" || values.Custom2!.Value != 0)
        { throw new InvalidOperationException("Custom names, units and zero values are retained independently."); }
    }

    private static void ManualValuesFollowScheduledSource()
    {
        var initial = new ManualVitalSigns(new(120, 80, 93), 365);
        var changed = new ManualVitalSigns(new(90, 60, 70), 382, new("ICP", 12, "mmHg"));
        LocalMonitorPreviewSession Create(ManualVitalSigns values) => new(PhysiologyIllustrationConfiguration.Default,
            MonitorDisplayConfiguration.Default(), manualVitals: values);
        var session = Create(initial);
        session.ScheduleSource(Create(changed), 400_000_000);
        session.Advance(200_000_000);
        if (session.ManualVitals != initial) { throw new InvalidOperationException("Manual readings changed before scheduled time."); }
        session.Advance(200_000_000);
        session.Advance(200_000_000);
        if (session.ManualVitals != changed || session.Measurements is not null)
        { throw new InvalidOperationException("Manual readings must apply independently of waveform measurement acquisition."); }
        session.ScheduleSource(Create(ManualVitalSigns.Empty), 0);
        session.Advance(200_000_000);
        if (session.ManualVitals != ManualVitalSigns.Empty) { throw new InvalidOperationException("Disabled manual readings must clear."); }
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid manual value accepted.");
    }
}
