// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class CardiacRateSmokeChecks
{
    internal static void Verify()
    {
        string seed = new('1', 64);
        for (int choice = 0; choice < DesignPreviewSettings.EcgChoiceCount; choice++)
        {
            var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
            var plan = pair.Physiology.ResolvePlan();
            if (!CardiacRateAdjustment.Supports(plan)) { continue; }
            int rate = (int)(60_000_000_000 / (plan.IndependentVentricularPeriodNs ?? plan.HeartPeriodNs) * 9 / 10);
            if (choice == 18) { rate = 45; }
            if (choice == 19) { rate = 27; }
            var adjustment = new CardiacRateAdjustment(rate, plan.IndependentVentricularPeriodNs is null ? null : 65, seed, 50);
            try
            {
                var source = PhysiologyIllustrationSource.Create(pair.Physiology with { RateAdjustment = adjustment });
                var paper = ProjectedEcgDemoSource.Create(pair.Ecg with { RateAdjustment = adjustment });
                var monitorPlan = source.CaptureState().Channels.Single(c => c.ChannelId == PhysiologyIllustrationSource.ChannelId(0)).Generator.Timeline.Plan;
                var paperPlan = paper.CaptureState().Generator.Timeline.Plan;
                var monitorEvents = RegularPhysiologyTimeline.Start(monitorPlan).AdvanceBefore(12_000_000_000, 1000);
                var paperEvents = RegularPhysiologyTimeline.Start(paperPlan).AdvanceBefore(12_000_000_000, 1000);
                Require(monitorEvents.Where(e => e.Kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.VentricularElectrical)
                    .SequenceEqual(paperEvents.Where(e => e.Kind is PhysiologyCycleEventKind.AtrialElectrical or PhysiologyCycleEventKind.VentricularElectrical)),
                    "monitor and twelve-lead share adjusted electrical times");
                for (int step = 1; step <= 50; step++)
                {
                    source.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                    paper.AdvanceTo(step * 200_000_000L, 50, 1, 100);
                }
                var restored = ElectrodeWaveformGroup.Restore(paper.CaptureState());
                var a = paper.AdvanceTo(10_200_000_000, 50, 1, 100);
                var b = restored.AdvanceTo(10_200_000_000, 50, 1, 100);
                Require(a.Count == b.Count && a.Zip(b).All(p => p.First.SequenceEqual(p.Second)), "adjusted paper recovery");
            }
            catch (Exception error) { throw new InvalidOperationException($"Rate adjustment template {choice}", error); }
        }

        foreach (int choice in new[] { 2, 3, 4, 53, 75, 99, 115 })
        {
            var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
            var adjustment = new CardiacRateAdjustment(choice == 2 ? 76 : 100, null, seed, 0);
            try
            {
                _ = PhysiologyIllustrationSource.Create(pair.Physiology with { RateAdjustment = adjustment });
                _ = ProjectedEcgDemoSource.Create(pair.Ecg with { RateAdjustment = adjustment });
            }
            catch (Exception error) { throw new InvalidOperationException($"Faster rate template {choice}", error); }
        }

        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.Settings.EcgSelection = 1;
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.HeartRate.Value = 60;
            window.Settings.PauseDuration.Value = 2700;
            window.SelectPage(2);
            window.Settings.Tabs.SelectedIndex = 5;
            window.Settings.SectionPages[5].SelectedSection = 0;
            DesktopViewportSmokeChecks.Layout(window, 1100, 850);
            Require(window.Settings.HeartRate.IsEffectivelyVisible && window.Settings.HeartRate.IsEnabled &&
                window.Settings.PauseDuration.IsEffectivelyVisible, "sinus arrest exposes normal rate and pause controls in vital signs");
            var beforePause = window.Session;
            window.RestartSettings();
            Require(!ReferenceEquals(beforePause, window.Session), "sinus arrest normal rate and pause apply from vital signs");
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new(1100, 850), new(96, 96)))
            {
                bitmap.Render((Avalonia.Controls.Control)window.Content!);
                Directory.CreateDirectory("artifacts");
                bitmap.Save("artifacts/ui-ecg-sinus-arrest.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            foreach (int choice in new[] { 1, 3, 6, 39 })
            {
                window.Settings.EcgSelection = choice;
                window.Settings.CardiacRateEnabled.IsChecked = false;
                window.Settings.RateVariation.Value = 0;
                window.Settings.RateSeed.Text = seed;
                var pattern = DesignPreviewWindow.ResolveStyle(choice, 0, 0).Physiology.ConductionPattern;
                if (!SeededRhythmSchedule.Supports(pattern)) { continue; }
                window.Settings.ConductionPercent.Value = 40;
                var originalSession = window.Session;
                window.RestartSettings();
                Require(!ReferenceEquals(originalSession, window.Session), "seeded rhythm applies with rate override off");
                for (int step = 0; step < 40; step++) { window.Session.Advance(200_000_000); }
                var original = window.Session.Samples(0, 0, 8_000_000_000).ToArray();
                window.Settings.RateSeed.Text = new string('2', 64);
                window.RestartSettings();
                for (int step = 0; step < 40; step++) { window.Session.Advance(200_000_000); }
                var changed = window.Session.Samples(0, 0, 8_000_000_000).ToArray();
                Require(original.Length > 0 && !original.SequenceEqual(changed), "global seed changes acquired ECG even with rate override off and zero slow variation");
                window.RestartSettings();
                for (int step = 0; step < 40; step++) { window.Session.Advance(200_000_000); }
                Require(changed.SequenceEqual(window.Session.Samples(0, 0, 8_000_000_000)), "same global seed reproduces acquired ECG through settings");
                if (choice == 39)
                {
                    DesktopViewportSmokeChecks.Layout(window, 1100, 850);
                    Require(window.Settings.ConductionPercent.IsEffectivelyVisible, "variable flutter exposes conduction percentage");
                    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new(1100, 850), new(96, 96));
                    bitmap.Render((Avalonia.Controls.Control)window.Content!);
                    bitmap.Save("artifacts/ui-ecg-variable-flutter.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }
            }
            window.Settings.EcgSelection = 19;
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.HeartRate.Value = 40;
            window.Settings.AtrialRate.Value = 100;
            window.SelectPage(2);
            window.Settings.Tabs.SelectedIndex = 5;
            window.Settings.SectionPages[5].SelectedSection = 0;
            DesktopViewportSmokeChecks.Layout(window, 1100, 850);
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new(1100, 850), new(96, 96)))
            {
                bitmap.Render((Avalonia.Controls.Control)window.Content!);
                Directory.CreateDirectory("artifacts");
                bitmap.Save("artifacts/ui-ecg-independent-rates.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
            var old = window.Session;
            window.RestartSettings();
            Require(!ReferenceEquals(old, window.Session), "independent rate settings apply");
            for (int i = 0; i < 180; i++) { window.Session.Advance(200_000_000); }
            Require(Math.Abs(window.Session.Measurements!.HeartRate.MilliBeatsPerMinute!.Value - 40000) < 1000, "HR measures ventricular QRS rate");
            old = window.Session;
            window.Settings.HeartRate.Value = 30;
            window.Settings.ApplyDelaySeconds.Value = 0;
            window.ApplySettings();
            Require(ReferenceEquals(old, window.Session), "Apply continues the existing session");
            for (int i = 0; i < 160; i++) { window.Session.Advance(200_000_000); }
            Require(Math.Abs(window.Session.Measurements!.HeartRate.MilliBeatsPerMinute!.Value - 30000) < 1000, "continued source changes the measured ventricular rate");
            window.Settings.HeartRate.Value = 75;
            window.RestartSettings();
            Require(ReferenceEquals(old, window.Session) && window.Settings.Status.Text!.Contains("20–40", StringComparison.Ordinal), "invalid escape rate preserves live state");
            window.Settings.EcgSelection = 21;
            Require(!window.Settings.HeartRate.IsEnabled && window.Settings.CardiacRateStatus.Text!.Contains("不适用", StringComparison.Ordinal), "VF marks rate not applicable");
            window.RestartSettings();
            Require(!ReferenceEquals(old, window.Session), "unsupported rate is ignored for VF without blocking template selection");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
