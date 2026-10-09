// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;

namespace Monitor.Desktop;

internal static class VentricularFibrillationLongRunSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow("tests/Monitor.Specs/Fixtures/coarse-vf-seed1.json");
        try
        {
            window.Pause();
            if (window.PreferenceNotice.IsVisible || window.Settings.EcgSelection != 21)
            { throw new InvalidOperationException("VF regression configuration was rejected."); }
            int missing = 0;
            for (int step = 0; step < 1500; step++)
            {
                window.Session.Advance(200_000_000);
                window.MonitorView.Refresh();
                foreach (var transition in window.Session.DetectedMonitoringEvents.Where(e =>
                    e.Condition is EcgMonitoringConditions.SuspectedVentricularFibrillation or EcgMonitoringConditions.Asystole))
                { Console.WriteLine($"VF transition {transition}"); }
                if (step > 50)
                {
                    var active = window.Session.Measurements!.EcgMonitoring.ActiveConditions;
                    if ((active & EcgMonitoringConditions.SuspectedVentricularFibrillation) == 0 ||
                        (active & (EcgMonitoringConditions.Asystole | EcgMonitoringConditions.Pause | EcgMonitoringConditions.MissedBeat)) != 0 ||
                        !window.MonitorView.ActiveNotices.Any(n => n.Id == "ecg-vf"))
                    { missing++; }
                }
            }
            if (missing != 0) { throw new InvalidOperationException($"Coarse VF seed 1 dropped for {missing} acquisition frames."); }
        }
        finally { window.Close(); }
    }
}
