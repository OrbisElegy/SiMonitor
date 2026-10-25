// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Monitor.Application.Presentation;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class EcgAdvancedSmokeChecks
{
    internal static void VerifyCatalogue()
    {
        var editor = new EcgAdvancedParameterEditor(new DesktopLocalization());
        for (int index = 0; index < DesignPreviewSettings.EcgChoiceCount; index++)
        {
            var preset = DesignPreviewWindow.ResolveStyle(index, 0, 0);
            editor.Reset(preset.Ecg, preset.Physiology.IllustrateAfSystemicPulseDeficit);
            Require(editor.Read(preset.Ecg) == preset.Ecg, $"template {index} retains its exact defaults");
            bool flexible = index is 0 or 10 or 11 or 12 or 15 or 73 or 74 or >= 99 and <= 164;
            Require(editor.Groups["timing"].IsVisible == flexible, $"template {index} exposes only supported timing");
            Require(editor.Groups["pWave"].IsVisible == (flexible && index is not (>= 99 and <= 101)), $"template {index} P components");
            Require(editor.Groups["stT"].IsVisible == (index is 0 or 10 or 11 or 12 or 15 or 73 or >= 99 and <= 101), $"template {index} ST/T components");
            Require(editor.Groups["uWave"].IsVisible == (flexible && index != 74), $"template {index} U wave");
            Require(editor.Groups["escape"].IsVisible == (index is 18 or 19), $"template {index} escape controls");
            Require(editor.Groups["fibrillation"].IsVisible == (index is 6 or 7 or >= 66 and <= 71), $"template {index} AF controls");
            Require(editor.Groups.Values.All(page => page.IsEnabled == page.IsVisible) &&
                (!editor.IsVisible || editor.SelectedItem is TabItem { IsVisible: true }),
                $"template {index} selects a supported page and excludes hidden pages from navigation");
            // Hidden numeric drafts cannot affect fixed examples or disabled options.
            foreach (var field in editor.Numbers.Values) { field.Value = null; }
            if (!flexible && index is not (18 or 19))
            { Require(editor.Read(preset.Ecg) == preset.Ecg, $"template {index} ignores unsupported numeric drafts"); }
        }

        foreach (int index in new[] { 0, 10, 15, 18, 19, 74, 99, 102, 107, 115, 137, 75, 165, 180, 6 })
        {
            var preset = DesignPreviewWindow.ResolveStyle(index, 0, 0);
            editor.Reset(preset.Ecg, preset.Physiology.IllustrateAfSystemicPulseDeficit);
            if (editor.Capabilities.Placement) { editor.Choices["limbPlacement"].SelectedIndex = 2; }
            if (editor.Capabilities.Timing && index != 102)
            {
                editor.Choices["qtMethod"].SelectedIndex = 2;
                editor.Flags["customTiming"].IsChecked = true;
                editor.Numbers["pDuration"].Value = 80;
            }
            if (editor.Capabilities.PWave)
            {
                editor.Flags["customP"].IsChecked = true;
                editor.Numbers["pEarly"].Value = 200;
            }
            if (editor.Capabilities.StT)
            {
                editor.Numbers["jPoint"].Value = 80;
                editor.Numbers["tScale2"].Value = -1;
                editor.Flags["fusionEnabled"].IsChecked = true;
                editor.Flags["fusionLead1"].IsChecked = true;
                editor.Numbers["tScale1"].Value = null;
            }
            if (editor.Capabilities.UWave)
            {
                editor.Flags["uEnabled"].IsChecked = true;
                editor.Numbers["uAmplitude2"].Value = 80;
                editor.Numbers["uDelay"].Value = 120;
                editor.Numbers["uDuration"].Value = 30;
            }
            if (editor.Capabilities.Escape)
            {
                editor.Numbers["escapePeriod"].Value = index == 18 ? 1400 : 2200;
                editor.Numbers["escapeOffset"].Value = 500;
            }
            if (editor.Capabilities.Fibrillation) { editor.Flags["afAberrancy"].IsChecked = true; }
            var ecg = editor.Read(preset.Ecg);
            var physiology = DesignPreviewSettings.WithAuthoredEcg(editor.ReadPhysiology(preset.Physiology, ecg), ecg);
            VerifySources(physiology, ecg, $"edited template {index}");
        }
        editor.Reset(ProjectedEcgDemoConfiguration.Default, false);
        editor.Flags["fusionEnabled"].IsChecked = true;
        foreach (string key in new[] { "jPoint", "stEnd", "stArch" }) { editor.Numbers[key].Value = null; }
        for (int i = 1; i <= 6; i++)
        {
            editor.Flags["fusionLead" + i].IsChecked = true;
            editor.Numbers["tScale" + i].Value = null;
        }
        var fused = editor.Read(ProjectedEcgDemoConfiguration.Default);
        editor.Flags["customTPeak"].IsChecked = true;
        editor.Numbers["tPeakPosition"].Value = 65;
        var reshaped = editor.Read(ProjectedEcgDemoConfiguration.Default);
        var first = ElectrodeSignalGenerator.Restore(ProjectedEcgDemoSource.Create(fused).CaptureState().Generator).GenerateBefore(800_000_000, 250, 100);
        var second = ElectrodeSignalGenerator.Restore(ProjectedEcgDemoSource.Create(reshaped).CaptureState().Generator).GenerateBefore(800_000_000, 250, 100);
        Require(first.Zip(second).Any(pair => pair.First.MicrovoltValues[1] != pair.Second.MicrovoltValues[1]),
            "global T peak editing remains effective on limb leads when every chest lead is fused");
        Require(first.Zip(second).All(pair => pair.First.MicrovoltValues.Skip(6).SequenceEqual(pair.Second.MicrovoltValues.Skip(6))),
            "fused chest contours ignore dormant ST and T inputs");
    }

    internal static void VerifyApplyAndPersistence()
    {
        string path = Path.Combine(Path.GetTempPath(), "monitor-advanced-ecg-" + Guid.NewGuid().ToString("N") + ".json");
        var window = new DesignPreviewWindow(path);
        window.Show();
        try
        {
            window.Pause();
            window.SelectPage(2);
            var settings = window.Settings;
            var editor = settings.EcgParameters;
            settings.ApplyDelaySeconds.Value = 0;
            editor.Choices["qtMethod"].SelectedIndex = 2;
            editor.Numbers["prInterval"].Value = 180;
            editor.Numbers["qrsDuration"].Value = 100;
            editor.Numbers["tScale1"].Value = -1.5m;
            editor.Flags["uEnabled"].IsChecked = true;
            editor.Numbers["uAmplitude3"].Value = 120;
            editor.Choices["limbPlacement"].SelectedIndex = 1;
            var before = window.Session;
            Require(before.Configuration.EcgDefinition is null, "editing leaves the running source unchanged");
            window.RestartSettings();
            Require(!ReferenceEquals(before, window.Session) && window.Session.Configuration.EcgDefinition is not null &&
                !settings.ShapeEditStatus.IsVisible, "restart accepts the complete ECG draft");
            var ecg = settings.ReadEcgParameters(ProjectedEcgDemoConfiguration.Default);
            VerifySources(window.Session.Configuration, ecg, "applied advanced parameters");
            Require(!window.PreferenceNotice.IsVisible && File.Exists(path), "advanced parameters persist through the real apply path");
            var reopened = new DesignPreviewWindow(path);
            try
            {
                Require(!reopened.PreferenceNotice.IsVisible && reopened.Settings.ReadEcgParameters(ProjectedEcgDemoConfiguration.Default) == ecg,
                    "reopening restores all advanced fields");
                VerifySources(reopened.Session.Configuration, ecg, "restored advanced parameters");
            }
            finally { reopened.Close(); }

            before = window.Session;
            string saved = File.ReadAllText(path);
            editor.Numbers["prInterval"].Value = 20;
            Require(settings.ShapeEditStatus.IsVisible && settings.ShapeEditStatus.Text == window.Localization.Get("ecgAdvanced.invalidTiming"),
                "inconsistent intervals show an actionable draft error");
            window.RestartSettings();
            Require(ReferenceEquals(before, window.Session) && File.ReadAllText(path) == saved, "invalid timing preserves the running source and saved preferences");
            editor.Choices["qtMethod"].SelectedIndex = 0;
            editor.Flags["uEnabled"].IsChecked = false;
            editor.Numbers["uDelay"].Value = null;
            settings.CardiacRateEnabled.IsChecked = true;
            settings.HeartRate.Value = 80;
            window.RestartSettings();
            Require(!ReferenceEquals(before, window.Session) && window.Session.Configuration.RateAdjustment is not null && window.Session.Configuration.SeededRate is null,
                "custom morphology uses the supported rate-adjustment path");
            var adjusted = settings.ReadEcgParameters(ProjectedEcgDemoConfiguration.Default) with { RateAdjustment = settings.ReadCardiacRate() };
            VerifySources(window.Session.Configuration, adjusted, "advanced morphology with adjusted rate");

            settings.CardiacRateEnabled.IsChecked = false;
            settings.EcgSelection = 6;
            editor.Flags["afAberrancy"].IsChecked = true;
            editor.Flags["afPulseDeficit"].IsChecked = true;
            window.RestartSettings();
            Require(window.Session.Configuration.IllustrateAfAberrancy && window.Session.Configuration.IllustrateAfSystemicPulseDeficit,
                "AF options reach ECG and systemic perfusion together");
            settings.EcgSelection = 137;
            settings.InfarctionParameters.ComponentsEnabled.IsChecked = true;
            settings.InfarctionParameters.ContributionLoss.IsChecked = true;
            settings.InfarctionParameters.ContributionAmount.Value = 60;
            window.RestartSettings();
            Require(window.Session.Configuration.Infarction?.Components?.ContributionLoss?.LossPermille == 600,
                "regional QRS contribution removal reaches the source");
            var snapshot = settings.CaptureGenerator();
            settings.EcgSelection = 0;
            settings.RestoreGenerator(JsonSerializer.Deserialize<MonitorGeneratorPreferences>(JsonSerializer.Serialize(snapshot))!);
            Require(settings.InfarctionParameters.ContributionAmount.Value == 60 && settings.InfarctionParameters.ContributionLoss.IsChecked == true,
                "QRS contribution parameters survive serialization");

            settings.EcgSelection = 0;
            settings.RestoreGenerator(settings.CaptureGenerator() with { EcgAdvanced = null });
            Require(editor.Read(ProjectedEcgDemoConfiguration.Default) == ProjectedEcgDemoConfiguration.Default,
                "older preferences without advanced ECG data restore template defaults");
            settings.OpenAdvanced(0);
            VerifyPages(window);
            editor.SelectedItem = editor.Groups["timing"];
            editor.Choices["qtMethod"].SelectedIndex = 2;
            DesktopViewportSmokeChecks.Layout(window, 960, 640);
            Require(editor.Bounds.Width > 180 && editor.Bounds.Right <= settings.Bounds.Width, "advanced controls fit the minimum window");
            Directory.CreateDirectory("artifacts/ecg-advanced");
            using var image = new RenderTargetBitmap(new PixelSize(960, 640), new Vector(96, 96));
            image.Render((Control)window.Content!);
            image.Save("artifacts/ecg-advanced/settings.png", PngBitmapEncoderOptions.Default);
        }
        finally
        {
            window.Close();
            File.Delete(path);
        }
    }

    private static void VerifyPages(DesignPreviewWindow window)
    {
        var settings = window.Settings;
        var editor = settings.EcgParameters;
        var session = window.Session;
        editor.SelectedItem = editor.Groups["timing"];
        editor.Choices["qtMethod"].SelectedIndex = 2;
        editor.Numbers["prInterval"].Value = 180;
        editor.SelectedItem = editor.Groups["stT"];
        editor.Numbers["jPoint"].Value = 80;
        var reset = ((StackPanel)editor.Groups["stT"].Content!).Children.OfType<Button>()
            .Single(button => button.Content as string == window.Localization.Get("advanced.resetPage"));
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(editor.Numbers["jPoint"].Value == 0 && editor.Numbers["prInterval"].Value == 180 &&
            editor.Choices["qtMethod"].SelectedIndex == 2, "reset restores only the current waveform page");
        settings.OpenAdvanced(1);
        settings.OpenAdvanced(0);
        Require(ReferenceEquals(editor.SelectedItem, editor.Groups["stT"]) && editor.Numbers["prInterval"].Value == 180,
            "leaving and returning preserves the selected ECG page and other drafts");

        foreach (int template in new[] { 6, 18, 21, 107, 137, 72, 74, 0 })
        {
            settings.EcgSelection = template;
            Require(editor.SelectedItem is TabItem { IsVisible: true, IsEnabled: true },
                "template changes always leave a supported page selected");
            foreach (var content in new Control[] { settings.TContourParameters, settings.InfarctionParameters,
                settings.ElectricalConversion, settings.AedConversion })
            {
                var page = editor.Items.Cast<TabItem>().Single(page => ReferenceEquals(page.Content, content));
                Require(page.IsVisible == content.IsVisible && page.IsEnabled == page.IsVisible,
                    "supplementary pages follow their template support");
                if (page.IsVisible) { editor.SelectedItem = page; }
            }
        }
        Require(ReferenceEquals(session, window.Session), "page navigation and resets never apply the draft");
        editor.SelectedItem = editor.Groups["timing"];
        foreach (int locale in new[] { 0, 2, 1 })
        {
            settings.Language.SelectedIndex = locale;
            foreach (int width in new[] { 1440, 960 })
            {
                DesktopViewportSmokeChecks.Layout(window, width, 640);
                foreach (var page in editor.Items.Cast<TabItem>().Where(page => page.IsVisible))
                {
                    var origin = page.TranslatePoint(default, editor)!.Value;
                    Require(page.Bounds.Width > 0 && origin.X >= 0 && origin.X + page.Bounds.Width <= editor.Bounds.Width + 1,
                        "ECG page headers wrap inside the available width in each language");
                }
                var scroll = editor.GetVisualAncestors().OfType<ScrollViewer>().First();
                Require(scroll.Extent.Width <= scroll.Viewport.Width + 1, "ECG pages do not introduce horizontal scrolling");
            }
        }
    }

    private static void VerifySources(PhysiologyDemoConfiguration physiology, ProjectedEcgDemoConfiguration ecg, string message)
    {
        var live = LiveEcgIllustrationSource.Create(physiology);
        var projected = ElectrodeSignalGenerator.Restore(ProjectedEcgDemoSource.Create(ecg).CaptureState().Generator);
        var session = new LocalMonitorPreviewSession(physiology, MonitorDisplayConfiguration.Default(), enableTwelveLead: true);
        session.DiscardStartup();
        for (long time = 50_000_000; time <= 1_000_000_000; time += 50_000_000)
        {
            var actual = live.GenerateBefore(time, 50, 100);
            var expected = projected.GenerateBefore(time, 50, 100);
            Require(actual.Count == expected.Count && actual.Zip(expected).All(pair => pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
                message + ": live and paper agree on every lead");
            session.Advance(50_000_000);
        }
        Require(session.Samples(0, 0, long.MaxValue).SequenceEqual(session.EcgSamples(EcgLead.II, 0, long.MaxValue)),
            message + ": acquisition and live II agree");
        Require(SinusIllustrationConfiguration.Apply(physiology).EcgDefinition is null, "conversion clears custom morphology");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException("Advanced ECG: " + message); }
    }
}
