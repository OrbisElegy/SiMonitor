// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class EcgAdjustmentSmokeChecks
{
    internal static void Verify()
    {
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            var settings = window.Settings;
            Require(!settings.EcgJPoint.IsEnabled && settings.ReadReferenceRepolarization() is null, "the adjustment starts off");
            window.RestartSettings();
            var reference = Samples(window, EcgLead.V2);
            var unaffected = Samples(window, EcgLead.I);

            settings.EcgRegion.SelectedIndex = 7;
            settings.EcgJPoint.Value = 200;
            settings.EcgStEnd.Value = 300;
            Require(settings.EcgJPoint.IsEnabled && !settings.EcgTPeak.IsEnabled, "choosing a region enables the ST fields");
            window.RestartSettings();
            var raised = Samples(window, EcgLead.V2);
            Require(raised.Length == reference.Length && raised.Zip(reference).Max(pair => pair.First - pair.Second) >= 100 &&
                Samples(window, EcgLead.I).SequenceEqual(unaffected), "V1–V3 ST elevation raises V2 and leaves lead I unchanged");

            var session = window.Session;
            settings.CardiacRateEnabled.IsChecked = true;
            window.ApplySettings();
            Require(ReferenceEquals(session, window.Session) && settings.Status.Text == window.Localization.Get("validation.ecgAdjustment"),
                "heart-rate adjustment with an ST change is reported without applying");
            settings.CardiacRateEnabled.IsChecked = false;
            settings.EcgSelection = 2;
            window.ApplySettings();
            Require(ReferenceEquals(session, window.Session) && settings.Status.Text == window.Localization.Get("validation.ecgAdjustment"),
                "templates other than the sinus reference reject the ST change");
            settings.EcgSelection = 0;

            var saved = settings.CaptureGenerator();
            Require(saved.Choices["EcgRegion"] == 7 && saved.Numbers["EcgStEnd"] == 300, "the ST change is saved with the generator preferences");
            settings.EcgRegion.SelectedIndex = 0;
            window.RestartSettings();
            Require(Samples(window, EcgLead.V2).SequenceEqual(reference), "turning the region off restores the reference shape");
        }
        finally { window.Close(); }
    }

    private static short[] Samples(DesignPreviewWindow window, EcgLead lead)
    {
        window.SelectPage(1);
        Guid channel = ProjectedEcgDemoSource.ChannelId(lead);
        return window.CurrentPaper!.Blocks.SelectMany(block => block.Planes).Where(plane => plane.ChannelId == channel)
            .SelectMany(plane => plane.Samples).ToArray();
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("ECG adjustment: " + message); } }
}
