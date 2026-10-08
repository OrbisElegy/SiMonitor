// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Monitor.Application.Presentation;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Infrastructure.Preferences;
using Monitor.Simulation.Therapy;

namespace Monitor.Desktop;

internal static class ElectricalConversionSmokeChecks
{
    internal static void Verify()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-conversion-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        try
        {
            var settings = window.Settings;
            foreach (var descriptor in EcgElectricalTherapy.Descriptors)
            {
                int index = int.Parse(descriptor.TemplateId.AsSpan("ecgTemplate.t".Length), System.Globalization.CultureInfo.InvariantCulture);
                settings.EcgSelection = index;
                var editor = settings.ElectricalConversion;
                Require(editor.IsVisible && editor.Enabled.IsChecked != true, "eligible template exposes disabled teaching response by default");
                editor.Enabled.IsChecked = true;
                editor.Monophasic.Value = 250 + index;
                editor.Biphasic.Value = 100 + index;
                var config = DesignPreviewWindow.ResolveStyle(index, 0, 0).Physiology;
                var mode = descriptor.Requirement == ElectricalShockRequirement.Unsynchronized ? DefibrillationMode.ManualAsynchronous : DefibrillationMode.ManualSynchronized;
                Require(EcgElectricalTherapy.Evaluate(editor.Profile(descriptor.TemplateId), config, DefibrillationWaveformKind.RectilinearBiphasic, mode, 500).Outcome == ElectricalConversionOutcome.Eligible,
                    "catalogue agrees with actual product source " + descriptor.TemplateId);
            }
            Require(settings.ElectricalConversion.Capture().Count == 24, "all template values retained independently");
            settings.EcgSelection = 21;
            settings.OpenAdvanced(0);
            window.SelectPage(2);
            DesktopViewportSmokeChecks.Layout(window);
            Require(settings.ElectricalConversion.Bounds.Height > 0 && settings.ElectricalConversion.Monophasic.Value == 271 && settings.ElectricalConversion.Biphasic.Value == 121,
                "advanced page restores the selected template values");
            Directory.CreateDirectory("artifacts");
            using (var image = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96)))
            {
                image.Render((Control)window.Content!);
                image.Save(Path.Combine("artifacts", "ui-electrical-conversion.png"), PngBitmapEncoderOptions.Default);
            }
            settings.ElectricalConversion.Biphasic.Value = 121.5m;
            Require(settings.ElectricalConversion.Error.IsVisible, "fractional threshold is visibly rejected");
            var before = window.Session;
            window.ApplySettings();
            Require(ReferenceEquals(before, window.Session) && before.PendingSourceTimeNs is null, "invalid threshold leaves accepted source and file unchanged");
            settings.ElectricalConversion.Biphasic.Value = 121;
            window.RestartSettings();
            window.Pause();
            Require(!window.PreferenceNotice.IsVisible && window.Session.ElectricalTherapy?.Settings.BiphasicThresholdJoules == 121, "apply publishes response with the source and saves full bank");
            settings.ElectricalConversion.Biphasic.Value = 5;
            Require(window.Session.ElectricalTherapy?.Settings.BiphasicThresholdJoules == 121, "unapplied editor cannot change live therapy response");
            settings.ElectricalConversion.Biphasic.Value = 121;
            var sinus = new LocalMonitorPreviewSession(DesignPreviewWindow.ResolveStyle(0, 0, 0).Physiology, window.Session.Display, true,
                electricalTherapy: new(EcgElectricalTherapy.SinusTemplateId, ElectricalConversionSettings.Default));
            var conversion = window.Session.ApplyElectricalShock(new(1, window.Session.SimulationTimeNs,
                DefibrillationWaveformKind.RectilinearBiphasic, DefibrillationMode.ManualAsynchronous, 122), sinus);
            Require(conversion.Outcome == ElectricalConversionOutcome.ConversionScheduled, "applied template response schedules automatic conversion");
            window.Session.Advance(50_000_000);
            Require(window.Session.ElectricalTherapy?.TemplateId == EcgElectricalTherapy.SinusTemplateId, "automatic source switch preserves live session");
            settings.EcgSelection = 0;
            Require(!settings.ElectricalConversion.IsVisible, "sinus hides unsupported conversion settings");
            var saved = settings.CaptureGenerator();
            var store = new DisplayPreferenceStore(path);
            var prefs = store.Load(out bool rejected);
            Require(!rejected && store.Save(prefs with { Generator = saved }), "save current sinus template without discarding shockable settings");
            var reloaded = new DesignPreviewWindow(path);
            try
            {
                Require(!reloaded.PreferenceNotice.IsVisible && reloaded.Settings.EcgSelection == 0, "sinus selection and complete bank reload");
                reloaded.Settings.EcgSelection = 21;
                Require(reloaded.Settings.ElectricalConversion.Biphasic.Value == 121 && reloaded.Settings.ElectricalConversion.Monophasic.Value == 271 &&
                    reloaded.Settings.ElectricalConversion.Enabled.IsChecked == true, "original VF response survives sinus switch and restart");
                reloaded.Settings.EcgSelection = 6;
                Require(reloaded.Settings.ElectricalConversion.Biphasic.Value == 106, "other template response remains distinct");
                foreach (int index in new[] { 0, 1, 2, 31, 33, 72, 73, 74, 165, 175 })
                {
                    reloaded.Settings.EcgSelection = index;
                    Require(!reloaded.Settings.ElectricalConversion.IsVisible, "nonshockable template has no response editor");
                }
            }
            finally { reloaded.Close(); }
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) { throw new InvalidOperationException(message); }
    }
}
