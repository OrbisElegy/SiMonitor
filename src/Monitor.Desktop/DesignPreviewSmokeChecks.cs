// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Monitor.Application.Measurements;
using Monitor.Application.Presentation;

namespace Monitor.Desktop;

internal static class DesignPreviewSmokeChecks
{
    // These fixtures compare the selected template's native perfusion. Pressure
    // overrides and their enabled startup defaults have separate interaction checks.
    private static DesignPreviewWindow CreateTemplateWindow()
    {
        var window = new DesignPreviewWindow();
        window.Settings.AbpTargetEnabled.IsChecked = false;
        window.Settings.PaTargetEnabled.IsChecked = false;
        window.RestartSettings();
        window.Pause();
        return window;
    }

    private static void VerifyActionFooter(DesignPreviewWindow window)
    {
        var settings = window.Settings;
        var arrows = settings.ApplyDelaySeconds.GetVisualDescendants().OfType<RepeatButton>().ToArray();
        var increase = arrows.Single(b => b.Name == "PART_IncreaseButton");
        var decrease = arrows.Single(b => b.Name == "PART_DecreaseButton");
        Point Position(Control control) => control.TranslatePoint(default, settings)!.Value;
        Require(Position(increase).X == Position(decrease).X && Position(increase).Y < Position(decrease).Y,
            "delay stepper uses vertically stacked arrows beside its editable value");
        increase.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(settings.ApplyDelaySeconds.Value == 3.1m, "stepper retains decimal increment semantics");
        decrease.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(settings.ApplyDelaySeconds.Value == 3m, "stepper decrements without losing the typed value");
        settings.ApplyDelaySeconds.Value = 60;
        Require(!increase.IsEnabled && decrease.IsEnabled, "upper bound disables increment");
        settings.ApplyDelaySeconds.Value = 0;
        Require(increase.IsEnabled && !decrease.IsEnabled, "lower bound disables decrement");
        settings.ApplyDelaySeconds.Value = 3;
        Require(Position(settings.Apply).X > Position(settings.Restart).X &&
            Position(settings.Restart).X > Position(settings.Run).X &&
            Position(settings.ResetAll).X + settings.ResetAll.Bounds.Width + 24 < Position(settings.Run).X,
            "primary action trails secondary actions and destructive reset is separated");
        Require(Position(settings.ApplyDelaySeconds).Y + settings.ApplyDelaySeconds.Bounds.Height <= Position(settings.Apply).Y &&
            new[] { settings.Apply, settings.Restart, settings.Run, settings.ResetAll }.All(b => b.Bounds.Height >= 44),
            "delay has its own labeled row and actions retain consistent hit targets");
    }

    private static void VerifyUiRefinement()
    {
        var groups = Enumerable.Range(0, DesignPreviewSettings.EcgChoiceCount).GroupBy(EcgChooserGroups.For).ToArray();
        Require(groups.Sum(g => g.Count()) == 165 && groups.All(g => g.Count() <= 10) &&
            groups.Select(g => g.Key).ToHashSet().SetEquals(EcgChooserGroups.Ordered) && EcgChooserGroups.Ordered.Distinct().Count() == EcgChooserGroups.Ordered.Count, "all ECG presets have bounded explicit groups");
        Require(EcgChooserGroups.For(2) != EcgChooserGroups.For(26) && EcgChooserGroups.For(26) != EcgChooserGroups.For(21) &&
            EcgChooserGroups.For(75) != EcgChooserGroups.For(87), "ectopy, tachycardia, fibrillation and drug groups stay distinct");
        var window = new DesignPreviewWindow { Width = 1000, Height = 720 }; window.Show();
        try
        {
            window.SelectPage(2); Capture(window, "ui-refine-home.png");
            VerifyActionFooter(window);
            Require(window.Title == (ProductIdentity.DevelopmentFeatures
                ? window.Localization.Format("shell.developmentTitle", ProductIdentity.Name, ProductIdentity.Version)
                : ProductIdentity.WindowTitle), "formal product name used in title");
            void Click(Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var ecg = window.Settings.TemplatePages[0];
            Require(window.Settings.TemplateSignals.SelectedIndex == 0 && ecg.ActiveGroup == "窦性心律" && ecg.Compact &&
                ecg.CompactGroups.IsEffectivelyVisible &&
                !(ecg.Groups.GetVisualAncestors().Contains(window.Settings) && ecg.Groups.GetVisualAncestors().All(ancestor => ancestor.IsVisible)),
                "generation opens on the ECG tab at the current group; narrow pages use the group selector");
            ecg.CompactGroups.SelectedIndex = EcgChooserGroups.Ordered.ToList().IndexOf("室性早搏");
            Capture(window, "ui-refine-pvc-top.png");
            Require(ecg.ActiveGroup == "室性早搏" && ecg.CardButtons.Count() == 10, "compact group selector shows the chosen group's cards");
            var viewer = ecg.CardButtons.First().GetVisualAncestors().OfType<ScrollViewer>().First();
            var advanced = ecg.Advanced;
            var root = (Control)window.Content!;
            var toolsPosition = (advanced.TranslatePoint(default, root), ecg.CompactGroups.TranslatePoint(default, root));
            viewer.Offset = new Vector(0, viewer.Extent.Height);
            Capture(window, "ui-refine-pvc-scrolled.png");
            Require(viewer.Offset.Y > 100 && (advanced.TranslatePoint(default, root), ecg.CompactGroups.TranslatePoint(default, root)) == toolsPosition,
                "group selector and advanced action stay fixed while cards scroll");
            var first = ecg.CardButtons.First();
            ecg.Search.Focus(); first.Focus(); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(viewer.Offset.Y > 100, "programmatic focus restoration does not jump to first card");
            ecg.Search.Focus(); first.Focus(Avalonia.Input.NavigationMethod.Tab); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(viewer.Offset.Y < 50, "keyboard navigation still reveals focused card");
            Click(advanced); window.Settings.SectionPages[6].SelectedSection = 1;
            Capture(window, "ui-refine-resp-compact.png");
            Require(!window.Settings.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "呼吸 · 规则呼吸"), "redundant respiration heading removed");
            var selectors = window.Settings.GetVisualDescendants().OfType<ComboBox>().Where(c => c.IsVisible &&
                AutomationProperties.GetName(c) is "设置分类" or "高级参数参数组").ToArray();
            Require(selectors.Length == 2 && Math.Abs(selectors[0].Bounds.Width - selectors[1].Bounds.Width) < 1 &&
                Math.Abs(selectors[0].TranslatePoint(default, root)!.Value.X - selectors[1].TranslatePoint(default, root)!.Value.X) < 1,
                "compact navigation selectors align and share width");
            window.Settings.Tabs.SelectedIndex = 1; Capture(window, "ui-refine-return-generator-root.png");
            Require(ecg.ActiveGroup == "室性早搏" && ecg.CardButtons.Count() == 10,
                "reentering generation keeps the browsed group");
            window.Settings.Tabs.SelectedIndex = 6;
            window.Width = 1440; window.Height = 940; Capture(window, "ui-refine-resp-wide.png");
            string? initialEcg = window.Settings.AppliedEcgParameters.Text;
            string? initialRespiration = window.Settings.AppliedRespirationParameters.Text;
            window.Settings.Tabs.SelectedIndex = 1;
            window.Settings.EcgSelection = 148;
            window.Settings.RespSignalAmplitude.Value = -650;
            window.Settings.OpenAdvanced(0);
            Capture(window, "ui-refine-ecg-editor.png");
            Require(window.Settings.InfarctionParameters.IsVisible && window.Settings.InfarctionParameters.Parent is not null &&
                window.Settings.ShapeEditSummary.Parent is null &&
                window.Settings.AppliedEcgParameters.Text == initialEcg &&
                window.Settings.AppliedRespirationParameters.Text == initialRespiration,
                "ECG editor omits parameter summary and drafts do not alter applied overview");
            window.RestartSettings();
            string? appliedEcg = window.Settings.AppliedEcgParameters.Text;
            string? appliedRespiration = window.Settings.AppliedRespirationParameters.Text;
            Require(appliedEcg != initialEcg && appliedRespiration!.Contains("-650", StringComparison.Ordinal),
                "successful apply publishes accepted ECG and respiratory parameters together");
            window.Settings.InfarctionParameters.Delay.Value = null;
            window.Settings.RespSignalAmplitude.Value = 350;
            var session = window.Session;
            window.RestartSettings();
            window.Settings.SectionPages[6].SelectedSection = 3;
            Require(ReferenceEquals(session, window.Session) && window.Settings.AppliedEcgParameters.Text == appliedEcg &&
                window.Settings.AppliedRespirationParameters.Text == appliedRespiration,
                "failed apply and overview navigation retain the active parameter snapshot");
            Capture(window, "ui-refine-applied-parameters-wide.png");
            window.Width = 960; Capture(window, "ui-refine-applied-parameters-compact.png");
            window.ResetAllSettings();
            Require(window.Settings.AppliedEcgParameters.Text == initialEcg &&
                window.Settings.AppliedRespirationParameters.Text == initialRespiration,
                "full reset restores the applied parameter overview");
        }
        finally { window.Close(); }
    }
    private static void VerifyVisibleVariation()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.RateVariation.Value = 5;
            window.Settings.OpticalEnabled.IsChecked = true;
            window.Settings.OpticalVariation.Value = 2.5m;
            window.Settings.OpticalTarget.Value = 97.5m;
            window.RestartSettings();
            HashSet<string> hr = [], pr = [], spo2 = [];
            for (int i = 0; i < 450; i++)
            {
                window.Pulse(window.ActiveTimer, 200_000_000);
                if (i < 100) { continue; }
                hr.Add(window.MonitorView.NumericTexts[0]);
                spo2.Add(window.MonitorView.NumericTexts[1]);
                pr.Add(window.MonitorView.NumericBlocks[3].Text!);
            }
            Require(hr.Count >= 3 && spo2.Count >= 2 && pr.Count >= 3 && !hr.Contains("---") && !spo2.Contains("---"),
                "90-second native displayed HR/PR/SpO2 visibly vary at5%/2.5 percentage points");
        }
        finally { window.Close(); }
    }
    private static void VerifySeededVitals(DesignPreviewWindow window)
    {
        var live = window.Session; string? oldSeed = window.Settings.RateSeed.Text;
        window.Settings.GenerateSeed.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        string generated = window.Settings.RateSeed.Text!;
        Require(generated.Length == 64 && generated.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') && generated != oldSeed && ReferenceEquals(live, window.Session), "OS seed button changes draft only with256-bit lowercase hex");
        window.Settings.CardiacRateEnabled.IsChecked = true;
        window.Settings.HeartRate.Value = 158;
        window.Settings.RateVariation.Value = 5;
        window.Settings.RateSeed.Text = new string('1', 64);
        window.Settings.RespiratoryRate.Value = 20;
        window.Settings.EtCo2Target.Value = 50;
        var previous = window.Session;
        window.RestartSettings();
        Require(!ReferenceEquals(previous, window.Session), "seeded high-rate source and paper capture apply together");
        for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
        var reading = window.Session.Measurements!;
        Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
            Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - 158000) < 12000,
            "native monitor measures seeded high heart rate from acquisition");
        Require(reading.Capnography.EndTidalCentiMmHg.Value is >= 4900 and <= 5100 &&
            reading.Capnography.RespirationsMilliPerMinute.Value is >= 19500 and <= 20500,
            "RR and EtCO2 changes reach sampled CO2 measurements");
        var samples = window.Session.Samples(0, 0, 20_000_000_000).ToArray();
        window.RestartSettings();
        for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
        Require(samples.SequenceEqual(window.Session.Samples(0, 0, 20_000_000_000)),
            "same applied seed reproduces acquired ECG samples");
        previous = window.Session;
        var timer = window.ActiveTimer;
        window.Settings.RateSeed.Text = "invalid";
        window.RestartSettings();
        Require(ReferenceEquals(previous, window.Session) && ReferenceEquals(timer, window.ActiveTimer),
            "invalid seed cannot replace live session or timer");
        window.Settings.RateSeed.Text = new string('1', 64);
        window.Settings.EcgSelection = 1;
        window.RestartSettings();
        Require(ReferenceEquals(previous, window.Session), "unsupported rhythm retains live state");
        window.Settings.EcgSelection = 0;
        window.Settings.OpticalVariation.Value = 2.5m;
        window.RestartSettings();
        Require(!ReferenceEquals(previous, window.Session), "98% plus2.5pp now applies within bounded source range");
        previous = window.Session;
        window.Settings.OpticalVariation.Value = 2;
        window.Settings.OpticalTarget.Value = 95;
        window.Settings.EtCo2Variation.Value = 5;
        window.Settings.RespirationSelection = 1;
        window.RestartSettings();
        Require(ReferenceEquals(previous, window.Session), "seeded CO2 rejects depth-response breathing without replacing live session");
        window.Settings.RespirationSelection = 0;
        window.RestartSettings();
        Require(!ReferenceEquals(previous, window.Session), "seeded optical target applies with cardiac variation");
        for (int i = 0; i < 700; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
        Require(window.Session.Measurements!.Capnography.EndTidalCentiMmHg.Value is >= 4400 and <= 5600,
            "native CO2 variation reaches independent measured value");
        var saturation = window.Session.Measurements!.SpO2;
        Require(saturation.Status == WaveformMeasurementStatus.Valid && saturation.SaturationMilliPercent is >= 92500 and <= 97500,
            "native monitor receives independently measured seeded optical variation");
        foreach (int channel in new[] { 3, 5 })
        {
            var display = MonitorDisplayConfiguration.Default();
            var pressureView = new LiveMonitorView(new LiveMonitorTrace(new LocalMonitorPreviewSession(PhysiologyDemoConfiguration.Default,
                new MonitorDisplayConfiguration(display.Skin, display.Slots.Select(slot => slot with { Channel = channel }).ToArray()))));
            pressureView.RefreshReadings(window.Session.Measurements!);
            Require(pressureView.NumericBlocks[1].Text!.StartsWith("SYS/DIA ", StringComparison.Ordinal) && !pressureView.NumericBlocks[1].Text!.Contains("---", StringComparison.Ordinal),
                "pressure secondary line presents waveform-derived systolic/diastolic values");
        }
        int? oldAbp = window.Session.Measurements!.AbpMean.MeanCentiMmHg;
        int? oldPa = window.Session.Measurements!.PaMean.MeanCentiMmHg;
        window.Settings.AbpTargetEnabled.IsChecked = false;
        window.Settings.PaTargetEnabled.IsChecked = false;
        window.Settings.AbpPulseGain.Value = 1.5m;
        window.Settings.PaPulseGain.Value = .5m;
        window.RestartSettings();
        for (int i = 0; i < 700; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
        Require(window.Session.Measurements!.AbpMean.MeanCentiMmHg > oldAbp && window.Session.Measurements!.PaMean.MeanCentiMmHg < oldPa,
            "ABP and PA controls change waveform-derived means independently");
        previous = window.Session;
        window.Settings.AbpPulseGain.Value = null;
        window.RestartSettings();
        Require(ReferenceEquals(previous, window.Session), "missing pressure gain preserves live session");
        window.Settings.AbpPulseGain.Value = 1.5m;
        int? oldMean = window.Session.Measurements!.CvpMean.MeanCentiMmHg;
        previous = window.Session;
        window.Settings.CvpBaseline.Value = null;
        window.RestartSettings();
        Require(ReferenceEquals(previous, window.Session), "missing CVP baseline preserves active source");
        window.Settings.CvpBaseline.Value = 12;
        window.RestartSettings();
        Require(!ReferenceEquals(previous, window.Session), "CVP baseline applies");
        for (int i = 0; i < 700; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
        Require(oldMean.HasValue && window.Session.Measurements!.CvpMean.MeanCentiMmHg == oldMean + 600,
            "native CVP mean derives shifted waveform, with identical seeded phase");
        var oldEcg = window.Session.Samples(0, 0, 35_000_000_000).ToArray();
        var oldResp = window.Session.Samples(1, 0, 35_000_000_000).ToArray();
        var oldCo2 = window.Session.Samples(4, 0, 35_000_000_000).ToArray();
        var oldCvp = window.Session.Samples(6, 0, 35_000_000_000).ToArray();
        window.Settings.InspirationPercent.Value = 33;
        Require(window.Settings.ReadBreathingTiming() == (3000, 990) && window.Settings.BreathingTiming.Text!.Contains("2010", StringComparison.Ordinal),
            "draft inspiration percentage previews actual millisecond timing");
        previous = window.Session;
        window.RestartSettings();
        Require(!ReferenceEquals(previous, window.Session), "changed inspiration applies");
        for (int i = 0; i < 700; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
        Require(oldEcg.SequenceEqual(window.Session.Samples(0, 0, 35_000_000_000)) &&
            !oldResp.SequenceEqual(window.Session.Samples(1, 0, 35_000_000_000)) &&
            !oldCo2.SequenceEqual(window.Session.Samples(4, 0, 35_000_000_000)) &&
            !oldCvp.SequenceEqual(window.Session.Samples(6, 0, 35_000_000_000)),
            "inspiration timing moves all respiratory components while preserving cardiac samples");
        Require(window.Session.Measurements!.Capnography.RespirationsMilliPerMinute.Value is >= 19500 and <= 20500,
            "independent CO2 RR retains breathing frequency with new ratio");
        previous = window.Session; timer = window.ActiveTimer;
        window.Settings.RespiratoryRate.Value = 60;
        foreach (decimal? invalid in new decimal?[] { 10, 62.5m, 90, null })
        {
            window.Settings.InspirationPercent.Value = invalid;
            window.RestartSettings();
            Require(ReferenceEquals(previous, window.Session) && ReferenceEquals(timer, window.ActiveTimer) &&
                window.Settings.Status.Text!.Contains("375", StringComparison.Ordinal), "invalid timing preserves live source and gives actionable constraint");
        }
        window.Settings.InspirationPercent.Value = 20;
        Require(window.Settings.ReadBreathingTiming() == (1000, 200), "exact minimum inspiration accepted");
        window.Settings.RespiratoryRate.Value = 20;
        window.Settings.InspirationPercent.Value = 33;
        window.SelectPage(2); window.Settings.Tabs.SelectedIndex = 5;
        Capture(window, "ui-preview-seeded-vitals.png");
    }
    private static void VerifyVariationRejectionMessages()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            var live = window.Session; var timer = window.ActiveTimer;
            void Reject(string text)
            {
                window.RestartSettings();
                Require(ReferenceEquals(live, window.Session) && ReferenceEquals(timer, window.ActiveTimer) &&
                    window.Settings.Status.Text!.Contains(text, StringComparison.Ordinal), "variation rejection names corrective action and preserves live state");
            }
            foreach (int source in Enumerable.Range(0, 3))
            {
                window.Settings.CardiacRateEnabled.IsChecked = source == 0;
                window.Settings.EtCo2Variation.Value = source == 1 ? 1 : 0;
                window.Settings.OpticalEnabled.IsChecked = source == 2;
                window.Settings.OpticalVariation.Value = source == 2 ? 1 : 0;
                foreach (string seed in new[] { "", new string('a', 63), new string('A', 64), new string('g', 64) })
                { window.Settings.RateSeed.Text = seed; Reject("64 个小写十六进制字符"); }
            }
            window.Settings.RateSeed.Text = new string('1', 64);
            window.Settings.CardiacRateEnabled.IsChecked = true;
            window.Settings.EcgSelection = 19; Reject("20–40 bpm");
            window.Settings.EcgSelection = 0; window.Settings.HeartRate.Value = null; Reject("心率目标");
            window.Settings.HeartRate.Value = 75;
            window.Settings.EjectionSelection = 0; window.Settings.CardiacRateEnabled.IsChecked = false;
            window.Settings.OpticalEnabled.IsChecked = false; window.Settings.EtCo2Variation.Value = 1;
            foreach (int target in new[] { 5, 80 })
            { window.Settings.EtCo2Target.Value = target; Reject("5–80 mmHg"); }
            window.Settings.EtCo2Target.Value = 40;
            foreach (int pattern in new[] { 1, 2, 3 })
            { window.Settings.RespirationSelection = pattern; Reject("仅支持规则呼吸"); }
            window.Settings.EtCo2Variation.Value = 0; window.Settings.RateSeed.Text = "invalid dormant seed";
            window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "turning variation off repairs conflict without requiring unused seed");
            window.Settings.RespirationSelection = 0; window.Settings.EtCo2Target.Value = 6;
            window.Settings.EtCo2Variation.Value = 1; window.Settings.RateSeed.Text = new string('1', 64);
            live = window.Session; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "exact lower excursion boundary applies after correction");
            window.Settings.EtCo2Target.Value = 79; live = window.Session; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "exact upper excursion boundary applies after correction");
        }
        finally { window.Close(); }
    }
    private static void VerifyVitalInputPrecision()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.OpticalEnabled.IsChecked = true;
            window.RestartSettings();
            var live = window.Session; var timer = window.ActiveTimer;
            foreach (var (field, invalid, valid, name) in new[]
            {
                (window.Settings.HeartRate, 75.5m, 75m, "心率目标"),
                (window.Settings.RateVariation, .15m, .1m, "心搏周期波动"),
                (window.Settings.EtCo2Variation, .015m, .01m, "CO₂ 逐呼吸波动"),
                (window.Settings.AbpPulseGain, 1.0005m, 1.001m, "ABP 脉搏分量倍率"),
                (window.Settings.PaPulseGain, 1.0005m, 1.001m, "PA 脉搏分量倍率"),
                (window.Settings.CvpBaseline, 6.005m, 6.01m, "CVP 基线"),
                (window.Settings.OpticalTarget, 98.0005m, 98.001m, "SpO₂ 目标"),
                (window.Settings.OpticalVariation, .0005m, .001m, "SpO₂ 波动幅度"),
                (window.Settings.OpticalModulation, 1.0005m, 1.001m, "光学脉动幅度")
            })
            {
                decimal? original = field.Value;
                foreach (decimal? value in new decimal?[] { invalid, null })
                {
                    field.Value = value; window.RestartSettings();
                    Require(ReferenceEquals(live, window.Session) && ReferenceEquals(timer, window.ActiveTimer) &&
                        window.Settings.Status.Text!.Contains(name, StringComparison.Ordinal) && window.Settings.Status.Text.Contains("最小单位", StringComparison.Ordinal),
                        "unsupported vital precision or missing value rejects without silent truncation");
                }
                field.Value = valid; window.RestartSettings();
                Require(!ReferenceEquals(live, window.Session), "exact source precision remains supported");
                live = window.Session; timer = window.ActiveTimer; field.Value = original;
            }
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.OpticalEnabled.IsChecked = false;
            window.Settings.HeartRate.Value = null; window.Settings.RateVariation.Value = null;
            window.Settings.OpticalTarget.Value = null; window.Settings.OpticalVariation.Value = null; window.Settings.OpticalModulation.Value = null;
            window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "disabled cardiac and optical sources ignore dormant invalid drafts");
            live = window.Session;
            window.Settings.OpticalEnabled.IsChecked = true; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session) && window.Settings.Status.Text!.Contains("SpO₂ 目标", StringComparison.Ordinal),
                "reenabling source restores field validation");
        }
        finally { window.Close(); }
    }
    private static void VerifyRespirationPageReset()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            window.Settings.RespirationSelection = 2;
            window.Settings.RespiratoryRate.Value = 20; window.Settings.EtCo2Target.Value = 45;
            window.Settings.RespSignalAmplitude.Value = -400; window.Settings.RespCardiacArtifact.Value = 120;
            window.Settings.Co2Baseline.Value = 5; window.Settings.Co2CustomPlateau.IsChecked = true;
            window.Settings.Co2PlateauStart.Value = 32.25m; window.Settings.Co2Rise.Value = 700;
            window.Settings.Co2TransportDelay.Value = 600; window.Settings.Co2DispersionStep.Value = 150;
            window.RestartSettings();
            for (int i = 0; i < 160; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            var live = window.Session; var timer = window.ActiveTimer; long frontier = live.FrontierNs;
            window.SelectPage(2); window.Settings.OpenAdvanced(1);
            void Reset(int group)
            {
                window.Settings.RespirationGroups.SelectedIndex = group;
                window.Settings.ResetRespirationPage.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Require(ReferenceEquals(live, window.Session) && ReferenceEquals(timer, window.ActiveTimer) && live.FrontierNs == frontier &&
                    window.Settings.RespirationSelection == 2 && window.Settings.RespiratoryRate.Value == 20 && window.Settings.EtCo2Target.Value == 45 &&
                    window.Settings.Status.Text!.Contains("尚未应用", StringComparison.Ordinal), "reset only edits draft and preserves template, vitals and session");
            }
            window.Settings.Co2PlateauStart.Value = null;
            Reset(2);
            Require(window.Settings.ReadCo2Response() == (0, 0) && window.Settings.Co2Baseline.Value == 5 &&
                window.Settings.Co2PlateauStart.Value is null && window.Settings.RespSignalAmplitude.Value == -400,
                "transport reset leaves other groups including invalid drafts intact");
            Reset(1);
            Require(window.Settings.ReadCo2Timing() == (125, 250, 200) && window.Settings.ReadCo2Levels() == (0, 45, null) &&
                !window.Settings.Co2PlateauStart.IsEnabled && window.Settings.RespSignalAmplitude.Value == -400,
                "shape reset clears overrides and invalid input while preserving gas target and signal");
            window.Settings.RespCardiacArtifact.Value = null; Reset(0);
            Require(window.Settings.ReadRespirationSignal() == (1000, 0), "signal reset repairs invalid signal draft");
            window.RestartSettings(); Require(!ReferenceEquals(live, window.Session), "explicit application activates reset drafts");
            var (period, inspiration) = window.Settings.ReadBreathingTiming();
            var config = DesignPreviewWindow.ResolveStyle(0, 2, 0).Physiology with
            { BreathPeriodMilliseconds = period, InspirationMilliseconds = inspiration, Co2EndExpiratoryMmHg = 45 };
            var reference = new LocalMonitorPreviewSession(config, window.Session.Display);
            reference.DiscardStartup();
            for (int i = 0; i < 200; i++) { window.Pulse(window.ActiveTimer, 50_000_000); reference.Advance(50_000_000); }
            for (int channel = 0; channel < 7; channel++)
            { Require(window.Session.Samples(channel, 0, reference.FrontierNs).SequenceEqual(reference.Samples(channel, 0, reference.FrontierNs)), "applied reset recovers source with retained template and vitals"); }
            window.Settings.RespirationSelection = 3; window.Settings.RespSignalAmplitude.Value = -400;
            live = window.Session; timer = window.ActiveTimer; frontier = live.FrontierNs;
            window.Settings.ResetRespirationPage.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Require(!window.Settings.RespSignalAmplitude.IsEnabled && window.Settings.RespSignalAmplitude.Value == 1000 &&
                ReferenceEquals(live, window.Session), "signal reset retains absent respiration applicability");
        }
        finally { window.Close(); }
    }
    private static void VerifyCo2LevelEditing()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            Require(window.Settings.ReadCo2Levels() == (0, 40, null), "default CO2 levels retain reference ratio");
            foreach (int pattern in Enumerable.Range(0, 4))
            {
                window.Settings.RespirationSelection = pattern;
                foreach (bool custom in new[] { true, false })
                {
                    window.Settings.Co2CustomPlateau.IsChecked = custom;
                    window.Settings.Co2Baseline.Value = custom ? 5 : 0;
                    window.Settings.Co2PlateauStart.Value = custom ? 32.25m : null;
                    var previous = window.Session; window.RestartSettings();
                    Require(!ReferenceEquals(previous, window.Session) && window.Settings.Co2PlateauStart.IsEnabled == custom,
                        "CO2 level edits apply and disabled plateau ignores dormant invalid draft");
                    var (period, inspiration) = window.Settings.ReadBreathingTiming();
                    var baseline = DesignPreviewWindow.ResolveStyle(0, pattern, 0).Physiology with
                    { BreathPeriodMilliseconds = period, InspirationMilliseconds = inspiration };
                    var original = new LocalMonitorPreviewSession(baseline, window.Session.Display);
                    original.DiscardStartup();
                    var expected = new LocalMonitorPreviewSession(baseline with
                    { Co2BaselineMmHg = custom ? 5 : 0, Co2PlateauStartCentiMmHg = custom ? 3225 : null }, window.Session.Display);
                    expected.DiscardStartup();
                    for (int i = 0; i < 200; i++)
                    { window.Pulse(window.ActiveTimer, 50_000_000); expected.Advance(50_000_000); original.Advance(50_000_000); }
                    for (int channel = 0; channel < 7; channel++)
                    {
                        var actual = window.Session.Samples(channel, 0, expected.FrontierNs);
                        Require(actual.SequenceEqual(expected.Samples(channel, 0, expected.FrontierNs)), "CO2 levels match acquired source");
                        Require(actual.SequenceEqual(original.Samples(channel, 0, expected.FrontierNs)) == (channel != 4 || !custom),
                            "CO2 levels isolate gas channel and restore exact reference ratio");
                    }
                }
            }
            window.Settings.RespirationSelection = 0;
            window.Settings.Co2CustomPlateau.IsChecked = true;
            var live = window.Session; var timer = window.ActiveTimer;
            foreach (decimal? value in new decimal?[] { null, 40.01m, 32.251m })
            {
                window.Settings.Co2PlateauStart.Value = value; window.RestartSettings();
                Require(ReferenceEquals(live, window.Session) && ReferenceEquals(timer, window.ActiveTimer) &&
                    window.Settings.Status.Text!.Contains("平台起始", StringComparison.Ordinal), "invalid active plateau rejects atomically");
            }
            window.Settings.Co2PlateauStart.Value = 4.99m; window.Settings.Co2Baseline.Value = 5;
            window.RestartSettings(); Require(ReferenceEquals(live, window.Session), "plateau below baseline rejects");
            window.Settings.Co2CustomPlateau.IsChecked = false;
            foreach (var field in new[] { window.Settings.Co2Baseline, window.Settings.EtCo2Target })
            {
                decimal? original = field.Value;
                foreach (decimal? value in new decimal?[] { null, 5.5m })
                {
                    field.Value = value; window.RestartSettings();
                    Require(ReferenceEquals(live, window.Session), "pressure integer inputs do not silently truncate");
                }
                field.Value = original;
            }
            window.Settings.Co2Baseline.Value = 41; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "baseline exceeding target rejects");
            window.Settings.Co2Baseline.Value = 5; window.Settings.EtCo2Variation.Value = 1;
            window.RestartSettings();
            Require(ReferenceEquals(live, window.Session) && window.Settings.Status.Text!.Contains("基线为 0", StringComparison.Ordinal),
                "nonzero baseline and seeded variation give actionable conflict");
            window.Settings.Co2Baseline.Value = 0; window.Settings.Co2CustomPlateau.IsChecked = true;
            window.Settings.Co2PlateauStart.Value = 40; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "flat reference plateau supports zero-baseline seeded response");
            window.Settings.EtCo2Variation.Value = 0; window.Settings.Co2Baseline.Value = 5;
            window.Settings.Co2PlateauStart.Value = 5; window.RestartSettings();
            Require(window.Settings.Status.Text!.StartsWith("已从头开始", StringComparison.Ordinal), "plateau equal to baseline accepted");
            window.Settings.Co2PlateauStart.Value = 32.25m; window.RestartSettings();
            window.SelectPage(2); window.Settings.OpenAdvanced(1);
            window.Settings.RespirationGroups.SelectedIndex = 1;
            Capture(window, "ui-preview-co2-level-editor.png");
            window.Width = 960; Capture(window, "ui-preview-co2-level-editor-compact.png");
        }
        finally { window.Close(); }
    }
    private static void VerifyCo2TimingEditing()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            Require(window.Settings.ReadCo2Timing() == (125, 250, 200), "CO2 timing defaults preserve reference");
            foreach (int pattern in Enumerable.Range(0, 4))
            {
                window.Settings.RespirationSelection = pattern;
                foreach (var timing in new[] { (DeadSpace: 200, Rise: 700, Fall: 300), (DeadSpace: 125, Rise: 250, Fall: 200) })
                {
                    window.Settings.Co2DeadSpace.Value = timing.DeadSpace;
                    window.Settings.Co2Rise.Value = timing.Rise; window.Settings.Co2Fall.Value = timing.Fall;
                    var (period, inspiration) = window.Settings.ReadBreathingTiming();
                    var baseline = DesignPreviewWindow.ResolveStyle(0, pattern, 0).Physiology with
                    { BreathPeriodMilliseconds = period, InspirationMilliseconds = inspiration };
                    var live = window.Session; window.RestartSettings();
                    Require(!ReferenceEquals(live, window.Session), "CO2 shape timing applies to all respiratory templates");
                    var expected = new LocalMonitorPreviewSession(baseline with
                    { Co2DeadSpaceMilliseconds = timing.DeadSpace, Co2RiseMilliseconds = timing.Rise, Co2FallMilliseconds = timing.Fall }, window.Session.Display);
                    expected.DiscardStartup();
                    var original = new LocalMonitorPreviewSession(baseline, window.Session.Display);
                    original.DiscardStartup();
                    for (int i = 0; i < 240; i++)
                    { window.Pulse(window.ActiveTimer, 50_000_000); expected.Advance(50_000_000); original.Advance(50_000_000); }
                    for (int channel = 0; channel < 7; channel++)
                    {
                        var actual = window.Session.Samples(channel, 0, expected.FrontierNs);
                        Require(actual.SequenceEqual(expected.Samples(channel, 0, expected.FrontierNs)), "CO2 timing matches acquired shared model");
                        Require(actual.SequenceEqual(original.Samples(channel, 0, expected.FrontierNs)) ==
                            (channel != 4 || pattern == 3 || timing == (125, 250, 200)), "CO2 timing isolates gas channel and restores exact defaults");
                    }
                }
            }
            window.Settings.RespirationSelection = 0;
            window.Settings.RespiratoryRate.Value = 60; window.Settings.InspirationPercent.Value = 10;
            window.Settings.Co2Fall.Value = 100;
            window.RestartSettings();
            Require(window.Settings.ReadBreathingTiming() == (1000, 100) && window.Settings.Status.Text!.StartsWith("已从头开始", StringComparison.Ordinal),
                "shorter CO2 fall permits valid inspiration previously rejected by fixed200ms limit");
            var session = window.Session; var timer = window.ActiveTimer;
            window.Settings.Co2Fall.Value = 101; window.RestartSettings();
            Require(ReferenceEquals(session, window.Session) && window.Settings.Status.Text!.Contains("101", StringComparison.Ordinal) &&
                window.Settings.BreathingTiming.Text!.Contains("101", StringComparison.Ordinal), "fall limit updates summary and rejection");
            window.Settings.Co2Fall.Value = 100;
            window.Settings.Co2DeadSpace.Value = 200; window.Settings.Co2Rise.Value = 700; window.RestartSettings();
            Require(ReferenceEquals(session, window.Session) && window.Settings.Status.Text!.Contains("900", StringComparison.Ordinal),
                "expiration equal to dead space plus rise rejects with current constraint");
            foreach (var field in new[] { window.Settings.Co2DeadSpace, window.Settings.Co2Rise, window.Settings.Co2Fall })
            {
                decimal? previous = field.Value;
                foreach (decimal? invalid in new decimal?[] { null, 1.5m })
                {
                    field.Value = invalid; window.RestartSettings();
                    Require(ReferenceEquals(session, window.Session) && ReferenceEquals(timer, window.ActiveTimer) &&
                        window.Settings.Status.Text!.Contains("1–10000", StringComparison.Ordinal), "invalid CO2 timing retains session and timer");
                }
                field.Value = previous;
            }
            window.Settings.Co2Rise.Value = 699; window.RestartSettings();
            Require(!ReferenceEquals(session, window.Session), "expiration with one millisecond plateau applies");
            window.Settings.RespiratoryRate.Value = 16; window.Settings.InspirationPercent.Value = 50;
            window.Settings.Co2Rise.Value = 700; window.Settings.Co2Fall.Value = 300;
            window.RestartSettings(); window.SelectPage(2); window.Settings.OpenAdvanced(1);
            window.Settings.RespirationGroups.SelectedIndex = 1;
            Capture(window, "ui-preview-co2-timing-editor.png");
            window.Width = 960; Capture(window, "ui-preview-co2-timing-editor-compact.png");
        }
        finally { window.Close(); }
    }
    private static void VerifyCo2ResponseEditing()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            Require(window.Settings.ReadCo2Response() == (0, 0), "CO2 response defaults preserve source");
            foreach (int pattern in Enumerable.Range(0, 4))
            {
                window.Settings.RespirationSelection = pattern;
                var (period, inspiration) = window.Settings.ReadBreathingTiming();
                var baseline = DesignPreviewWindow.ResolveStyle(0, pattern, 0).Physiology with
                { BreathPeriodMilliseconds = period, InspirationMilliseconds = inspiration };
                foreach (var response in new[] { (Delay: 600, Dispersion: 0), (Delay: 600, Dispersion: 150), (Delay: 0, Dispersion: 0) })
                {
                    window.Settings.Co2TransportDelay.Value = response.Delay;
                    window.Settings.Co2DispersionStep.Value = response.Dispersion;
                    var previous = window.Session; window.RestartSettings();
                    Require(!ReferenceEquals(previous, window.Session), "valid CO2 response applies across respiratory templates");
                    var expected = new LocalMonitorPreviewSession(baseline with
                    { Co2TransportDelayMilliseconds = response.Delay, Co2DispersionStepMilliseconds = response.Dispersion }, window.Session.Display);
                    expected.DiscardStartup();
                    var original = new LocalMonitorPreviewSession(baseline, window.Session.Display);
                    original.DiscardStartup();
                    for (int i = 0; i < 240; i++)
                    { window.Pulse(window.ActiveTimer, 50_000_000); expected.Advance(50_000_000); original.Advance(50_000_000); }
                    long end = expected.FrontierNs;
                    for (int channel = 0; channel < 7; channel++)
                    {
                        var actual = window.Session.Samples(channel, 0, end);
                        Require(actual.SequenceEqual(expected.Samples(channel, 0, end)), "CO2 response uses shared acquired source");
                        bool unchanged = actual.SequenceEqual(original.Samples(channel, 0, end));
                        Require(unchanged == (channel != 4 || pattern == 3 || response == (0, 0)),
                            "CO2 response isolates gas channel; zero settings restore exact source");
                    }
                }
            }
            var live = window.Session; var timer = window.ActiveTimer;
            foreach (var field in new[] { window.Settings.Co2TransportDelay, window.Settings.Co2DispersionStep })
            {
                foreach (decimal? invalid in new decimal?[] { null, .5m })
                {
                    field.Value = invalid; window.RestartSettings();
                    Require(ReferenceEquals(live, window.Session) && ReferenceEquals(timer, window.ActiveTimer) &&
                        window.Settings.Status.Text!.Contains("管路延迟", StringComparison.Ordinal),
                        "invalid CO2 response preserves session and timer with actionable error");
                }
                field.Value = 0;
            }
            window.Settings.Co2TransportDelay.Value = 5000; window.Settings.Co2DispersionStep.Value = 500;
            window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "CO2 response UI upper bounds accepted");
            window.Settings.RespirationSelection = 0;
            window.Settings.Co2TransportDelay.Value = 600; window.Settings.Co2DispersionStep.Value = 150;
            window.RestartSettings(); window.SelectPage(2); window.Settings.OpenAdvanced(1);
            window.Settings.RespirationGroups.SelectedIndex = 2;
            Capture(window, "ui-preview-co2-response-editor.png");
            window.Width = 960; Capture(window, "ui-preview-co2-response-editor-compact.png");
        }
        finally { window.Close(); }
    }
    private static void VerifyRespirationSignalEditing()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int pattern in Enumerable.Range(0, 4))
            {
                window.Settings.RespirationSelection = pattern;
                window.Settings.RespSignalAmplitude.Value = -400; window.Settings.RespCardiacArtifact.Value = 120;
                window.RestartSettings();
                var (period, inspiration) = window.Settings.ReadBreathingTiming();
                var baseline = DesignPreviewWindow.ResolveStyle(0, pattern, 0).Physiology with { BreathPeriodMilliseconds = period, InspirationMilliseconds = inspiration };
                var expected = new LocalMonitorPreviewSession(baseline with { RespAmplitudeCounts = pattern == 3 ? 1000 : -400, RespCardiacArtifactCounts = 120 }, window.Session.Display);
                expected.DiscardStartup();
                var original = new LocalMonitorPreviewSession(baseline, window.Session.Display);
                original.DiscardStartup();
                for (int i = 0; i < 200; i++) { window.Pulse(window.ActiveTimer, 50_000_000); expected.Advance(50_000_000); original.Advance(50_000_000); }
                long end = expected.FrontierNs;
                for (int channel = 0; channel < 7; channel++)
                {
                    Require(window.Session.Samples(channel, 0, end).SequenceEqual(expected.Samples(channel, 0, end)), "RESP signal edits match acquired shared source");
                    bool same = window.Session.Samples(channel, 0, end).SequenceEqual(original.Samples(channel, 0, end));
                    Require(same == (channel != 1), "RESP signal editing changes only impedance, retaining CO2 and all other channels");
                }
                Require(window.Settings.RespSignalAmplitude.IsEnabled == (pattern != 3), "absent respiration disables only respiratory signal amplitude");
            }
            window.Settings.RespirationSelection = 0;
            var live = window.Session;
            foreach (decimal? invalid in new decimal?[] { null, 1.5m })
            {
                window.Settings.RespSignalAmplitude.Value = invalid; window.RestartSettings();
                Require(ReferenceEquals(live, window.Session), "incomplete or fractional RESP amplitude rejects atomically");
            }
            window.Settings.RespSignalAmplitude.Value = 0; window.Settings.RespCardiacArtifact.Value = 0; window.RestartSettings();
            for (int i = 0; i < 160; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Require(window.Session.Samples(1, 0, window.Session.FrontierNs).All(s => s.Value == 0) &&
                window.Session.Samples(4, 0, window.Session.FrontierNs).Any(s => s.Value > 0), "zero impedance signal is not apnea or absent CO2");
            window.Settings.RespCardiacArtifact.Value = null; live = window.Session; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session) && window.Settings.Status.Text!.Contains("心源性干扰", StringComparison.Ordinal), "missing cardiac artifact gives actionable rejection");
            window.Settings.RespCardiacArtifact.Value = 120; window.Settings.RespSignalAmplitude.Value = -400;
            window.SelectPage(2); window.Settings.OpenAdvanced(1);
            Require(window.Settings.SectionPages[6].SelectedSection == 1, "respiration style opens respiration advanced group directly");
            live = window.Session;
            window.Settings.OpenAdvanced(0);
            Require(window.Settings.SectionPages[6].SelectedSection == 0, "ECG style opens ECG advanced group");
            window.Settings.OpenAdvanced(3);
            Require(window.Settings.SectionPages[6].SelectedSection == 2, "ejection style opens ejection advanced group");
            window.Settings.SectionPages[6].SelectedSection = 3;
            window.Settings.Tabs.SelectedIndex = 1; window.Settings.OpenAdvanced(1);
            Require(ReferenceEquals(live, window.Session) && window.Settings.RespSignalAmplitude.Value == -400 && window.Settings.RespCardiacArtifact.Value == 120,
                "advanced group navigation and return preserve drafts without applying");
            window.Settings.Co2CustomPlateau.IsChecked = true; window.Settings.Co2PlateauStart.Value = 32.25m;
            window.Settings.Co2Rise.Value = 700; window.Settings.Co2TransportDelay.Value = 600;
            window.Settings.RespirationGroups.SelectedIndex = 1;
            window.Settings.RespirationGroups.SelectedIndex = 2;
            window.Settings.Tabs.SelectedIndex = 1; window.Settings.OpenAdvanced(1);
            Require(window.Settings.RespirationGroups.SelectedIndex == 2 && ReferenceEquals(live, window.Session) &&
                window.Settings.Co2CustomPlateau.IsChecked == true && window.Settings.Co2PlateauStart.Value == 32.25m &&
                window.Settings.Co2Rise.Value == 700 && window.Settings.Co2TransportDelay.Value == 600 &&
                window.Settings.RespSignalAmplitude.Value == -400 && window.Settings.RespCardiacArtifact.Value == 120,
                "respiration subpages retain selection and all signal/shape/response drafts without applying");
            window.Settings.RespirationGroups.SelectedIndex = 0;
            Capture(window, "ui-preview-resp-signal-editor.png");
            window.Width = 960; Capture(window, "ui-preview-resp-signal-editor-compact.png");
            Require(window.Settings.SectionPages[6].Compact, "advanced groups use existing compact Fluent navigation");
        }
        finally { window.Close(); }
    }
    private static void VerifySoundSettings()
    {
        VerifyOutputFaultNotice();
        long authority = 0;
        var timed = new SoundSettingsPanel(authorityNow: () => authority);
        var pitchMeasurement = new Monitor.Application.Measurements.LiveMeasurementSnapshot(0, null!, null!, null!, null!,
            new(Monitor.Application.Measurements.WaveformMeasurementStatus.Valid, 85000, null, 0), null!, null!, null!);
        timed.UpdateAlarm(MonitorNoticeLevel.Warning, new(), measurement: pitchMeasurement);
        Require(timed.PitchSource.SelectedIndex == 1 && timed.BeatPitchPercent == 85, "selected A pitch derives from measured saturation");
        timed.PitchSource.SelectedIndex = 0;
        Require(timed.BeatPitchPercent == 97, "fixed pitch independent of selected beat source");
        timed.PitchSource.SelectedIndex = 1;
        timed.UpdateAlarm(MonitorNoticeLevel.Warning, new());
        Require(timed.BeatPitchPercent == 97, "missing measurement uses neutral pitch");
        Require(timed.BeatSource.SelectedIndex == 0, "default beat source remains ECG");
        timed.BeatSource.SelectedIndex = 2;
        Require(timed.BeatSourceLabel.Contains("等待有效信号", StringComparison.Ordinal), "auto without valid measurements waits visibly");
        timed.BeatSource.SelectedIndex = 0;
        Require(timed.BeatSourceLabel == "心搏音源：ECG", "manual selection shows effective source");
        Require(timed.HeartbeatVolume.Value == 100 && timed.EffectiveHeartbeatVolume == 50, "default heartbeat uses original gain under master volume");
        var alarmBeforeBeatVolume = timed.PublishedAlarm;
        timed.HeartbeatVolume.Value = 0;
        Require(timed.EffectiveHeartbeatVolume == 0 && timed.PublishedAlarm == alarmBeforeBeatVolume, "muting routine beats preserves alarm request");
        timed.Volume.Value = 40; timed.HeartbeatVolume.Value = 25;
        Require(timed.EffectiveHeartbeatVolume == 10 && timed.BeatPitchPercent == 97, "relative beat volume multiplies master without changing pitch");
        timed.HeartbeatVolume.Value = 100; timed.Volume.Value = 50;
        timed.StartAudioPause(120);
        timed.BeatSource.SelectedIndex = 1;
        Require(timed.PublishedAlarm is null && timed.AudioPauseText.Contains("120s", StringComparison.Ordinal), "pulse source selection preserves independent audio pause");
        Require(timed.PublishedAlarm is null && timed.AudioPauseText.Contains("120s", StringComparison.Ordinal), "pause suppresses alarm request and shows countdown");
        timed.UpdateAlarm(MonitorNoticeLevel.Critical, new());
        Require(timed.PublishedAlarm is null && timed.HeartbeatEnabled.IsChecked == true, "new critical condition is retained while heartbeat preference stays independent");
        authority = 60_000_000_000; timed.PauseMonitor(); timed.RefreshAudioPause();
        Require(timed.AudioPauseText.Contains("60s", StringComparison.Ordinal), "simulation pause does not freeze audio deadline");
        authority = 120_000_000_000; timed.RefreshAudioPause();
        Require(timed.AudioPauseText == "" && timed.PublishedAlarm is null, "expiry does not restart a paused simulation");
        timed.UpdateAlarm(MonitorNoticeLevel.Critical, new());
        Require(timed.PublishedAlarm?.Level == MonitorNoticeLevel.Critical, "current critical resumes instead of old warning");
        timed.StartAudioPause(10); timed.UpdateAlarm(null, new());
        timed.ResumeAlarmAudio.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(timed.PublishedAlarm is null && !timed.ResumeAlarmAudio.IsEnabled, "cleared alarm is not replayed by explicit resume");
        timed.Close();
        var completion = new TaskCompletionSource<Monitor.Infrastructure.Audio.SoundPreviewResult>();
        CancellationToken token = default; int calls = 0;
        var panel = new SoundSettingsPanel((volume, cancellation) =>
        { Require(volume == 50, "volume passed to output"); calls++; token = cancellation; return completion.Task; });
        Require(!panel.Muted && panel.HeartbeatEnabled.IsChecked == true &&
            new MonitorAlertSettings().CriticalInterval.Value == 1.5m, "monitor sound defaults to unmuted");
        var devices = new List<Monitor.Infrastructure.Audio.AudioOutputDeviceInfo> { new("test-speaker", "Test speakers") };
        var controls = new SoundSettingsPanel(enumerateDevices: () => devices);
        var alertSettings = new MonitorAlertSettings();
        Require(controls.OutputDevice.SelectedIndex == 0 && controls.OutputDevice.ItemCount == 2 &&
            controls.Children.OfType<CheckBox>().Single() == controls.HeartbeatEnabled,
            "system default selected and no master enable checkbox remains");
        controls.Volume.Value = 73;
        controls.Mute.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(controls.Muted && controls.Volume.Value == 73 && controls.EffectiveHeartbeatVolume == 0,
            "speaker mute preserves slider volume and silences heartbeat");
        controls.OutputDevice.SelectedIndex = 1;
        var soundPreferences = controls.CapturePreferences(alertSettings);
        devices.Clear(); controls.RefreshDevices();
        Require(controls.CapturePreferences(alertSettings) == soundPreferences && controls.OutputDevice.ItemCount == 2,
            "removed selected device remains selected without falling back to default");
        controls.Mute.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(!controls.Muted && controls.Volume.Value == 73 && controls.EffectiveHeartbeatVolume == 73,
            "speaker unmute restores volume");
        controls.Volume.Value = 0;
        controls.Mute.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Require(controls.Volume.Value == 73 && !controls.Muted, "speaker restores last nonzero slider volume");
        controls.RestorePreferences(soundPreferences, alertSettings);
        Require(controls.Muted && controls.CapturePreferences(alertSettings) == soundPreferences,
            "device and mute restore together with volume");
        controls.Volume.Value = 42;
        Require(!controls.Muted && controls.EffectiveHeartbeatVolume == 42, "raising the slider unmutes");
        controls.Close();
        var pending = panel.PreviewAsync();
        Require(!panel.Audition.IsEnabled && panel.Volume.IsEnabled && panel.Stop.IsEnabled, "preview keeps volume available and prevents duplicate auditions");
        panel.PreviewAsync().GetAwaiter().GetResult();
        Require(calls == 1, "double click does not start second output");
        panel.StopPreview(); Require(token.IsCancellationRequested, "stop requests background cancellation");
        completion.SetResult(Monitor.Infrastructure.Audio.SoundPreviewResult.Stopped);
        Require(pending.IsCompleted && panel.Audition.IsEnabled && !panel.Stop.IsEnabled, "joined output restores controls");
        Require(panel.Audition.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Center && panel.Stop.MinHeight == 44,
            "sound controls preserve centered accessible target sizes");
        var failed = new SoundSettingsPanel((_, _) => Task.FromResult(Monitor.Infrastructure.Audio.SoundPreviewResult.StopFailed));
        failed.PreviewAsync().GetAwaiter().GetResult();
        Require(!failed.Audition.IsEnabled, "failed join blocks new UI playback");
        panel.Close(); panel.PreviewAsync().GetAwaiter().GetResult(); Require(calls == 1, "closed view cannot replay");
        var late = new TaskCompletionSource<Monitor.Infrastructure.Audio.SoundPreviewResult>();
        var closing = new SoundSettingsPanel((_, cancellation) => { token = cancellation; return late.Task; });
        var closingTask = closing.PreviewAsync(); closing.Close();
        Require(token.IsCancellationRequested, "window close cancels active output");
        string? closingStatus = closing.Status.Text;
        late.SetResult(Monitor.Infrastructure.Audio.SoundPreviewResult.Completed);
        Require(closingTask.IsCompleted && closing.Status.Text == closingStatus && !closing.Audition.IsEnabled,
            "late completion cannot revive a closed sound page");
        var unavailable = new SoundSettingsPanel((_, _) => Task.FromResult(Monitor.Infrastructure.Audio.SoundPreviewResult.Unavailable));
        unavailable.PreviewAsync().GetAwaiter().GetResult();
        Require(unavailable.Audition.IsEnabled && unavailable.Status.Text!.Contains("不可用", StringComparison.Ordinal),
            "missing output is visible and retryable");
    }
    private static void VerifyOutputFaultNotice()
    {
        var result = Monitor.Infrastructure.Audio.SoundPreviewResult.Unavailable;
        var sound = new SoundSettingsPanel((_, _) => Task.FromResult(result));
        var snapshot = LiveWaveformMeasurements.CreateIllustration().Read(0);
        var view = new LiveMonitorView(new LiveMonitorTrace(new LocalMonitorPreviewSession(PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default())));
        view.AdditionalNotices = _ => sound.OutputNotice is { } notice ? [notice] : [];
        int updates = 0;
        sound.OutputNoticeChanged += () => { updates++; view.RefreshReadings(snapshot); };
        Require(sound.OutputNotice is null, "untried opt-in output does not invent a fault");
        sound.PreviewAsync().GetAwaiter().GetResult();
        Require(sound.OutputNotice is { Level: MonitorNoticeLevel.Notice, Audible: false, Numeric: null } &&
            view.Notice.Text!.Contains("声音输出不可用", StringComparison.Ordinal) && view.HighestNotice == MonitorNoticeLevel.Info,
            "failed output reaches monitor banner immediately without sounding its own failure");
        Require(view.NumericTexts.All(t => t == "---"), "system output fault never fabricates or changes patient readings");
        result = Monitor.Infrastructure.Audio.SoundPreviewResult.Stopped;
        sound.PreviewAsync().GetAwaiter().GetResult();
        Require(sound.OutputNotice is not null && updates == 1, "cancelled retry does not prove recovery");
        result = Monitor.Infrastructure.Audio.SoundPreviewResult.Completed;
        sound.PreviewAsync().GetAwaiter().GetResult();
        Require(sound.OutputNotice is null && updates == 2 && !view.ActiveNotices.Any(n => n.Id == "audio-output"), "successful audition clears fault without a simulation tick");
        result = Monitor.Infrastructure.Audio.SoundPreviewResult.Interrupted;
        sound.PreviewAsync().GetAwaiter().GetResult();
        Require(sound.OutputNotice!.Text.Contains("已中断", StringComparison.Ordinal), "interruption is distinct from unavailable output");
        view.AdditionalNotices = _ => [sound.OutputNotice!, new("patient", MonitorNoticeLevel.Critical, "ECG HR 极高") { Numeric = MonitorNumeric.HeartRate }];
        view.RefreshReadings(snapshot);
        Require(view.HighestNotice == MonitorNoticeLevel.Critical, "silent output notice does not suppress independent patient alarm severity");
        result = Monitor.Infrastructure.Audio.SoundPreviewResult.StopFailed;
        sound.PreviewAsync().GetAwaiter().GetResult();
        Require(sound.OutputNotice!.Text.Contains("重新启动", StringComparison.Ordinal) && !sound.Audition.IsEnabled,
            "failed release stays visible and blocks reopening");
        sound.Close();
    }
    private static void VerifyHeartRateLimits(LiveMeasurementSnapshot snapshot)
    {
        VerifySaturationLimits(snapshot);
        var settings = new MonitorAlertSettings();
        MonitorNotice[] At(int rate, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid) =>
            settings.Notices(snapshot with { HeartRate = new(status, rate, null) }).ToArray();
        Require(At(30000).Length == 0, "HR thresholds remain opt-in");
        settings.HeartRateEnabled.IsChecked = true;
        foreach (var (rate, level, text) in new[]
        {
            (39999, MonitorNoticeLevel.Critical, "ECG HR 极低"), (40000, MonitorNoticeLevel.Warning, "ECG HR 低"),
            (49999, MonitorNoticeLevel.Warning, "ECG HR 低"), (120001, MonitorNoticeLevel.Warning, "ECG HR 高"),
            (180000, MonitorNoticeLevel.Warning, "ECG HR 高"), (180001, MonitorNoticeLevel.Critical, "ECG HR 极高")
        })
        {
            var notices = At(rate);
            Require(notices.Length == 1 && notices[0].Level == level && notices[0].Text == text,
                "measured HR uses strict limits and correct low/high severity");
        }
        foreach (int rate in new[] { 50000, 75000, 120000 })
        { Require(At(rate).Length == 0, "normal range and warning boundaries clear HR alarm"); }
        foreach (var status in Enum.GetValues<WaveformMeasurementStatus>().Where(s => s != WaveformMeasurementStatus.Valid))
        { Require(At(30000, status).Length == 0, "invalid HR cannot become bradycardia alarm"); }
        settings.CriticalLowHeartRate.Value = 50;
        Require(At(30000).Single().Id == "hr-settings", "equal low thresholds reject without patient alarm");
        settings.CriticalLowHeartRate.Value = 40; settings.WarningLowHeartRate.Value = 121;
        Require(At(30000).Single().Id == "hr-settings", "crossed low/high thresholds reject");
        settings.WarningLowHeartRate.Value = 50; settings.CriticalHeartRate.Value = 120;
        Require(At(200000).Single().Id == "hr-settings", "equal upper thresholds reject");
    }
    private static void VerifySaturationLimits(LiveMeasurementSnapshot snapshot)
    {
        var settings = new MonitorAlertSettings();
        LiveMeasurementSnapshot At(int? value, WaveformMeasurementStatus status = WaveformMeasurementStatus.Valid) =>
            snapshot with { SpO2 = new(status, value, null, snapshot.SampleTimeNs) };
        Require(!settings.Notices(At(80000)).Any(), "SpO2 alarm remains opt-in");
        settings.SpO2Enabled.IsChecked = true;
        Require(settings.Notices(At(80000)).Single().Level == MonitorNoticeLevel.Critical &&
            settings.Notices(At(90000)).Single().Numeric == MonitorNumeric.SpO2, "SpO2 controls feed real severity and numeric binding");
        settings.CriticalSpO2.Value = 80; settings.WarningSpO2.Value = 90;
        Require(!settings.Notices(At(90000)).Any() && settings.Notices(At(80000)).Single().Level == MonitorNoticeLevel.Warning,
            "edited thresholds use strict bounds");
        settings.CriticalSpO2.Value = 90;
        Require(settings.Notices(At(80000)).Single().Id == "spo2-settings", "crossed UI limits report configuration error only");
        settings.CriticalSpO2.Value = 85; settings.WarningSpO2.Value = 92;
        var session = new LocalMonitorPreviewSession(PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default());
        var view = new LiveMonitorView(new LiveMonitorTrace(session)) { AdditionalNotices = settings.Notices };
        view.RefreshReadings(At(80000)); view.RefreshNumericHighlights(0);
        Require(view.NumericTexts[1] == "80" && (LiveMonitorView.NumericBackground(view.NumericBlocks[2]) as Avalonia.Media.ISolidColorBrush)?.Color == Avalonia.Media.Color.Parse("#ffb51f2c") &&
            view.HighestNotice == MonitorNoticeLevel.Critical, "measured low SpO2 drives displayed number, red backing and audio severity");
        view.RefreshReadings(At(null, WaveformMeasurementStatus.PoorSignal)); view.RefreshNumericHighlights(0);
        Require(view.NumericTexts[1] == "---" && view.HighestNotice == MonitorNoticeLevel.Info,
            "poor optics clear physiological severity and keep technical info");
        view.RefreshReadings(At(98000)); view.RefreshNumericHighlights(0);
        Require(view.NumericTexts[1] == "98" && view.HighestNotice != MonitorNoticeLevel.Critical,
            "normal recovery does not latch old low saturation");
    }
    internal static void Verify()
    {
        // Each registration runs on its process-owned Avalonia dispatcher.
        NativeSmokePartition.Run(LocalizationSmokeChecks.Verify);
        NativeSmokePartition.Run(LocalizationSmokeChecks.VerifyTemplateCatalog);
        NativeSmokePartition.Run(LocalizationSmokeChecks.VerifyHelpCatalog);
        NativeSmokePartition.Run(LocalizationSmokeChecks.VerifyPausedMonitorLanguage);
        NativeSmokePartition.Run(LocalizationSmokeChecks.VerifyRetiredMonitorCollection);
        NativeSmokePartition.Run(Ecg12MeasurementSmokeChecks.Verify);
        NativeSmokePartition.Run(VerifyVisibleVariation);
        NativeSmokePartition.Run(VerifyStableSlowContours);
        NativeSmokePartition.Run(VerifyRespirationOverview);
        NativeSmokePartition.Run(VerifyPrebuiltStyles);
        NativeSmokePartition.Run(VerifyAtrialProductStyles);
        NativeSmokePartition.Run(VerifyAfVariantProductStyles);
        NativeSmokePartition.Run(VerifyStandstillProductStyles);
        NativeSmokePartition.Run(VerifyAdvancedTemplateDescriptions);
        NativeSmokePartition.Run(VerifyHyperkalemiaProductStyle);
        NativeSmokePartition.Run(VerifyHypokalemiaProductStyles);
        NativeSmokePartition.Run(VerifyCalciumProductStyles);
        NativeSmokePartition.Run(VerifyDigitalisProductStyles);
        NativeSmokePartition.Run(VerifyQuinidineProductStyles);
        NativeSmokePartition.Run(VerifyHyperkalemiaFusionProductStyle);
        NativeSmokePartition.Run(VerifyAtrialShapeProductStyles);
        NativeSmokePartition.Run(VerifyVentricularShapeProductStyles);
        NativeSmokePartition.Run(VerifyTContourProductStyles);
        NativeSmokePartition.Run(VerifyRegionalInfarctionProductStyles);
        NativeSmokePartition.Run(VerifyPrematureSupraventricularProductStyles);
        NativeSmokePartition.Run(VerifyPvcGroupProductStyles);
        NativeSmokePartition.Run(VerifyBundleBlockProductStyles);
        NativeSmokePartition.Run(VerifyPreexcitationProductStyles);
        NativeSmokePartition.Run(VerifyBlockProductStyles);
        NativeSmokePartition.Run(VerifyDisorganizedProductStyles);
        NativeSmokePartition.Run(VerifySvtProductStyles);
        NativeSmokePartition.Run(VerifyVtProductStyles);
        NativeSmokePartition.Run(VerifyAutomaticRhythmProductStyles);
        NativeSmokePartition.Run(VerifyRespirationSignalEditing);
        NativeSmokePartition.Run(VerifyCo2ResponseEditing);
        NativeSmokePartition.Run(VerifyCo2TimingEditing);
        NativeSmokePartition.Run(VerifyCo2LevelEditing);
        NativeSmokePartition.Run(VerifyRespirationPageReset);
        NativeSmokePartition.Run(ManualVitalSmokeChecks.Verify);
        NativeSmokePartition.Run(VerifyVitalInputPrecision);
        NativeSmokePartition.Run(VerifyVariationRejectionMessages);
        NativeSmokePartition.Run(VerifyIntegratedWindow);
        NativeSmokePartition.Run(DisplayPreferenceSmokeChecks.Verify);
        NativeSmokePartition.Run(GeneratorPreferenceSmokeChecks.Verify);
        NativeSmokePartition.Run(() => { CardiacRateSmokeChecks.Verify(); SettingsApplySmokeChecks.Verify(); });
        NativeSmokePartition.Run(PressureTargetSmokeChecks.Verify);
        NativeSmokePartition.Run(PressureTargetSmokeChecks.VerifyDriftApply);
        NativeSmokePartition.Run(VerifyUiRefinement);
        NativeSmokePartition.Run(SettingsNavigationSmokeChecks.Verify);
        NativeSmokePartition.Run(MonitorContinuationSmokeChecks.Verify);
        NativeSmokePartition.Run(MonitorContinuationSmokeChecks.VerifyCvpApply);
        NativeSmokePartition.Run(PerfusionAuditSmokeChecks.Verify);
        NativeSmokePartition.Run(DefaultResetSmokeChecks.Verify);
        NativeSmokePartition.Run(PressureAlarmSmokeChecks.Verify);
        NativeSmokePartition.Run(PrimaryAlarmConfirmationSmokeChecks.Verify);
        NativeSmokePartition.Run(NoExpirationAlarmSmokeChecks.Verify);
        NativeSmokePartition.Run(AlarmLifecycleSmokeChecks.Verify);
        NativeSmokePartition.Run(AlarmNotificationSmokeChecks.Verify);
        NativeSmokePartition.Run(NotificationSoundSmokeChecks.Verify);
        NativeSmokePartition.Run(NotificationSoundSmokeChecks.VerifyMixedNotices);
        NativeSmokePartition.Run(NotificationSettingsSmokeChecks.Verify);
        NativeSmokePartition.Run(AlarmAttentionPresentationSmokeChecks.Verify);
        NativeSmokePartition.Run(EcgAlarmSmokeChecks.Verify);
        NativeSmokePartition.Run(EcgAlarmSettingsSmokeChecks.Verify);
        NativeSmokePartition.Run(EcgMonitoringAlarmSmokeChecks.Verify);
        NativeSmokePartition.Run(EcgMonitoringAlarmSmokeChecks.VerifyRepolarizationRecovery);
        NativeSmokePartition.Run(EcgMonitoringAlarmSmokeChecks.VerifyLongQtRonT);
        NativeSmokePartition.Run(EcgMonitoringAlarmSmokeChecks.VerifyAberrantAtrialBeats);
        EcgTemplateDetectionSmokeChecks.Register();
        NativeSmokePartition.Run(DeepOxygenationSmokeChecks.Verify);
        NativeSmokePartition.Run(RealtimeOxygenationSmokeChecks.Verify);
        NativeSmokePartition.Run(OxygenationDefaultsSmokeChecks.Verify);
    }
    private static void VerifyIntegratedWindow()
    {
        var launched = MonitorApp.CreateLaunchWindow([], persistDisplay: false);
        Require(launched is DesignPreviewWindow, "no-argument launch enters the integrated monitor");
        var window = (DesignPreviewWindow)launched; window.Show();
        DesktopViewportSmokeChecks.Layout(window);
        try
        {
            window.Settings.Sound.StartAudioPause(120);
            Require(window.MonitorView.AudioPauseStatus.Text!.Contains("报警声音暂停", StringComparison.Ordinal), "pause state is visible outside rotating patient messages");
            window.Settings.Sound.ResumeAlarmAudio.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Require(window.MonitorView.AudioPauseStatus.Text == "", "explicit resume clears persistent header");
            Require(window.MonitorView.NumericTexts.All(t => t == "---"), "no configured targets displayed before acquisition");
            Require(window.Title!.Contains("Standalone", StringComparison.Ordinal) && !window.Settings.Sound.Muted,
                "standalone starts with explicit sound opt-in and truthful development title");
            var timer = window.ActiveTimer;
            for (int i = 0; i < 150; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.Page == 0 && window.Session.FrontierNs > 0 && window.Settings.Parent is null, "live monitor without settings controls");
            Require(window.MonitorView.NumericTexts[0] == "75" && window.MonitorView.NumericTexts[1] == "---", "sample-derived HR visible, absent optical source not invented");
            VerifyGapAndCalibration(window.MonitorTrace);
            Capture(window, "ui-preview-monitor.png");
            double wideMonitor = window.MonitorTrace.Bounds.Width;
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Capture(window, "ui-preview-monitor-compact.png");
            Require(window.MonitorTrace.Bounds.Width < wideMonitor && window.Session.Display.Slots.Count == 5, "monitor resizes but skin row count stays fixed");
            DesktopViewportSmokeChecks.Layout(window);
            for (int i = 0; i < 450; i++) { window.Pulse(timer, 50_000_000); }
            Capture(window, "ui-preview-monitor-auto.png");
            Require(window.MonitorView.NumericTexts[4] == "16" && window.MonitorView.NumericTexts[3] == "40", "independent RESP rate and CO2 amplitude reach actual visible labels");
            var liveReading = window.Session.Measurements!;
            VerifyNumericAlarmHighlights(liveReading);
            VerifyHeartRateLimits(liveReading);
            window.Settings.Alerts.NoExpirationEnabled.IsChecked = true;
            var absent = liveReading with
            {
                SampleTimeNs = 20_000_000_000,
                Capnography = liveReading.Capnography with
                { Activity = new(Monitor.Application.Measurements.WaveformMeasurementStatus.Valid, 0, null, 20_000_000_000) }
            };
            Require(window.Settings.Alerts.Notices(absent).Any(n => n.Id == "co2-no-expiration" && n.Level == MonitorNoticeLevel.Critical), "CO2 absence control projects real condition into alarm path");
            window.Settings.Alerts.NoExpirationEnabled.IsChecked = false;
            Require(window.Settings.Alerts.Notices(absent).All(n => n.Id != "co2-no-expiration"), "CO2 absence remains opt-in");
            VerifyAdditionalLimits(liveReading);
            var seven = new LiveMonitorView(new LiveMonitorTrace(new LocalMonitorPreviewSession(
                PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default(MonitorSkin.SevenRows))));
            seven.RefreshReadings(liveReading);
            string Mean(MeanPressureReading reading) => ((decimal)reading.MeanCentiMmHg! / 100).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            Require(seven.NumericTexts[2] == Mean(liveReading.AbpMean) && seven.NumericTexts[5] == Mean(liveReading.PaMean) &&
                seven.NumericTexts[6] == Mean(liveReading.CvpMean), "all three pressure channels bind independent measured means");
            _ = Raster(seven, 700, 480);
            window.MonitorView.RefreshReadings(liveReading with
            {
                HeartRate = new(WaveformMeasurementStatus.Uncountable, null, null),
                Capnography = new(new(WaveformMeasurementStatus.PoorSignal, null, null), liveReading.Capnography.RespirationsMilliPerMinute)
            });
            Require(window.MonitorView.NumericTexts[0] == "-?-" && window.MonitorView.NumericTexts[3] == "---" &&
                window.MonitorView.ActiveNotices.Any(n => n.Text.Contains("信号质量不足", StringComparison.Ordinal)), "invalid values and technical status replace digits without preset classification");
            window.MonitorView.RefreshReadings(liveReading);
            window.Settings.Alerts.HeartRateEnabled.IsChecked = true;
            window.Settings.Alerts.WarningHeartRate.Value = 60;
            window.Settings.Alerts.CriticalHeartRate.Value = 70;
            window.MonitorView.RefreshReadings(liveReading);
            Require(window.MonitorView.HighestNotice == MonitorNoticeLevel.Critical && window.MonitorView.Notice.Text == "ECG HR 极高（00:00）",
                "valid measured HR triggers the configured critical threshold and one banner");
            window.Settings.Sound.StartAudioPause(120);
            for (int i = 0; i < 20; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Require(window.MonitorView.Notice.Text == "ECG HR 极高（00:01）", "audio pause preserves critical elapsed counter");
            Capture(window, "ui-preview-critical.png");
            var region = window.MonitorView.NoticeRegion;
            var center = region.TranslatePoint(new Point(region.Bounds.Width / 2, 0), window.MonitorView)!.Value;
            // Star-column layout rounding may shift the center by one layout unit.
            Require(Math.Abs(center.X - window.MonitorView.Bounds.Width / 2) <= 1 && region.Bounds.Width > 0 && region.Bounds.Height > 0 &&
                window.MonitorView.Notice.FontSize > 0 && window.MonitorView.Notice.FontWeight == Avalonia.Media.FontWeight.Bold &&
                region.Bounds.Width < window.MonitorView.Bounds.Width * .7 && window.MonitorView.Notice.TextAlignment == Avalonia.Media.TextAlignment.Center,
                $"bounded alarm region and its text are centered independently of side labels: center={center.X}, view={window.MonitorView.Bounds.Width}, region={region.Bounds.Width}, alignment={window.MonitorView.Notice.TextAlignment}");
            var wideNoticeSize = region.Bounds.Size; double wideNoticeFont = window.MonitorView.Notice.FontSize;
            DesktopViewportSmokeChecks.Layout(window, 1000, 720); Capture(window, "ui-preview-critical-compact.png");
            Require(region.Bounds.Width < wideNoticeSize.Width && region.Bounds.Height < wideNoticeSize.Height && window.MonitorView.Notice.FontSize < wideNoticeFont,
                "notice width, height and typography scale with monitor viewport");
            var compactCenter = region.TranslatePoint(new Point(region.Bounds.Width / 2, 0), window.MonitorView)!.Value;
            Require(Math.Abs(compactCenter.X - window.MonitorView.Bounds.Width / 2) <= 1, "scaled notice remains centered");
            DesktopViewportSmokeChecks.Layout(window); Capture(window, "ui-preview-critical.png");
            window.Settings.Sound.ResumeAlarmAudio.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.Settings.Alerts.HeartRateEnabled.IsChecked = false;
            foreach (int level in new[] { 1, 2, 3, 4 })
            {
                window.Settings.Alerts.TestLevel.SelectedIndex = level;
                window.MonitorView.RefreshReadings(liveReading);
                Require(window.MonitorView.HighestNotice == (MonitorNoticeLevel)(level - 1), "explicit four-level test is available without hardware output");
            }
            window.Settings.Alerts.TestLevel.SelectedIndex = 0;
            window.MonitorView.RefreshReadings(liveReading);
            window.SelectPage(1); Capture(window, "ui-preview-paper.png");
            window.Settings.PaperLayout.SelectedIndex = 1; window.SelectPage(1);
            Capture(window, "ui-preview-paper-six-rows.png");
            VerifySixRowPaper(window.CurrentPaper!);
            window.Settings.PaperLayout.SelectedIndex = 0; window.SelectPage(1);
            VerifyPaperEnd(window.CurrentPaper!);
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var root = (Control)window.Content!;
            double wideScale = window.CurrentPaper!.TransformToVisual(root)!.Value.M11;
            Require(window.CurrentPaper!.BlockCount == 55 && window.Settings.Parent is null, "complete paper snapshot with no settings controls");
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Capture(window, "ui-preview-compact.png");
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            double narrowScale = window.CurrentPaper!.TransformToVisual(root)!.Value.M11;
            Require(narrowScale < wideScale, "paper including waves and calibration scales with the viewport");
            DesktopViewportSmokeChecks.Layout(window); window.SelectPage(2);
            Capture(window, "ui-preview-settings.png");
            Require(window.Settings.Apply.Classes.Contains("accent"), "primary action uses Fluent accent state styling");
            var mainNavigation = window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "主导航");
            Require(mainNavigation.SelectedIndex == 2, "main navigation exposes Fluent list selection semantics");
            Require(!mainNavigation.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text is "›" or "✓"), "main navigation uses no chevrons or checkmarks");
            Require(window.Settings.Tabs.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Text == "›") == window.Settings.Tabs.ItemCount &&
                !window.Settings.Tabs.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains('✓') == true), "settings chevrons persist independently of selection");
            Require(window.Settings.Tabs.Background == Avalonia.Media.Brushes.Transparent && window.Settings.Tabs.Bounds.Height < window.Settings.Bounds.Height * .75,
                "settings navigation is a compact transparent list rather than a full-height slab");
            var helpSession = window.Session;
            window.SelectPage(3); Capture(window, "ui-preview-help.png");
            Require(window.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Text?.Contains("报警声音始终对应当前最高") == true), "help contains relocated alarm instructions");
            Require(window.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Text?.Contains("模板说明中列出的") == true), "help catalog includes advanced instructions before visiting advanced settings");
            var helpSearch = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "搜索帮助");
            helpSearch.Text = "呼气末 随机"; Capture(window, "ui-preview-help-search.png");
            var found = window.GetVisualDescendants().OfType<SelectableTextBlock>().ToArray();
            Require(found.Length == 1 && found[0].Text!.Contains("一次呼气内保持同一生成目标", StringComparison.Ordinal), "multiple search words intersect and retain matching help");
            helpSearch.Text = "不存在的说明xyz"; Capture(window, "ui-preview-help-empty.png");
            Require(!window.GetVisualDescendants().OfType<SelectableTextBlock>().Any() && window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("没有匹配说明", StringComparison.Ordinal) == true), "empty search explains how to recover");
            helpSearch.Text = "";
            window.SelectPage(4); Capture(window, "ui-preview-about.png");
            var legal = window.GetVisualDescendants().OfType<TextBox>().Single(t => AutomationProperties.GetName(t) == "许可文档正文");
            Require(legal.IsReadOnly && legal.Text?.Contains("GNU AFFERO GENERAL PUBLIC LICENSE") == true, "about embeds offline project license text");
            var documents = window.GetVisualDescendants().OfType<ComboBox>().Single(t => AutomationProperties.GetName(t) == "开源许可与依赖文档");
            Require(documents.ItemCount == 2 && ReferenceEquals(helpSession, window.Session), "own license documents and informational navigation preserve running session");
            window.GetVisualDescendants().OfType<SettingsSections>().Single().Sections.SelectedIndex = 1;
            Capture(window, "ui-preview-about-components.png");
            Require(window.GetVisualDescendants().OfType<ComboBox>().Single(t => AutomationProperties.GetName(t) == "开源许可与依赖文档").ItemCount >= 3,
                "third party documents occupy a separate page");
            window.SelectPage(2); Capture(window, "ui-preview-settings.png");
            var templates = window.Settings.TemplatePages[0];
            Button Card(DesignPreviewSettings.TemplatePage page, string name) => page.CardButtons.Single(button => AutomationProperties.GetName(button)?.StartsWith(name + "，", StringComparison.Ordinal) == true);
            Require(!templates.Compact && templates.Groups.IsEffectivelyVisible && templates.Groups.ItemCount == EcgChooserGroups.Ordered.Count &&
                templates.CardButtons.Count() == 3 && AutomationProperties.GetName((ListBoxItem)templates.Groups.Items[0]!) == "窦性心律，含当前选择",
                "wide ECG tab lists every group beside the current group's previews");
            Capture(window, "ui-preview-chooser.png");
            Require(window.Settings.PreviewCacheCount >= 3, "grouped choices own distinct cached source configurations");
            int navigationCacheCount = window.Settings.PreviewCacheCount;
            templates.Groups.SelectedIndex = EcgChooserGroups.Ordered.ToList().IndexOf("心房颤动");
            Capture(window, "ui-preview-waveform-groups.png");
            Require(templates.ActiveGroup == "心房颤动" && templates.CardButtons.Count() == 8 &&
                window.Settings.PreviewCacheCount <= navigationCacheCount + 8, "group browsing only builds previews for visible cards");
            templates.Groups.SelectedIndex = 0;
            window.UpdateLayout();
            var candidate = Card(templates, "窦性停搏（无逸搏）");
            var candidateSize = candidate.Bounds.Size;
            var unchanged = window.Session;
            candidate.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Capture(window, "ui-preview-chooser-selected.png");
            Require(window.Settings.EcgSelection == 1 && ReferenceEquals(unchanged, window.Session), "candidate updates draft preview without replacing live source");
            var selectedCandidate = templates.CardButtons.Single(button => AutomationProperties.GetName(button) == "窦性停搏（无逸搏），已选择");
            Require(selectedCandidate.Bounds.Size == candidateSize && candidateSize == new Size(238, 162), "candidate sizes stay equal before and after selection");
            Require(selectedCandidate.IsFocused, "keyboard focus follows rebuilt selected candidate");
            Require(templates.Selection.Text == "当前选择：窦性停搏（无逸搏） · 应用后生效", "tab header row names the draft selection");
            DesktopViewportSmokeChecks.Layout(window, 1000, 720); Capture(window, "ui-preview-waveform-compact.png");
            Require(selectedCandidate.TranslatePoint(default, root)!.Value.X + selectedCandidate.Bounds.Width < root.Bounds.Width, "leaf preview fits compact window");
            DesktopViewportSmokeChecks.Layout(window);
            Require(!window.Settings.GetVisualDescendants().OfType<Expander>().Any(), "redundant twelve-lead chooser expansion is removed");
            templates.Search.Text = "下壁 陈旧";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Capture(window, "ui-preview-template-search.png");
            Require(templates.CardButtons.Count() == 3 && templates.Results.Text == "找到 3 个模板" && templates.Groups.SelectedIndex < 0,
                "search matches every keyword across groups and clears the group highlight");
            templates.Search.Text = "室";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(templates.CardButtons.Count() == DesignPreviewSettings.TemplateSearchLimit &&
                templates.Results.Text!.Contains("显示前 12 个", StringComparison.Ordinal), "broad searches stay bounded and ask for a narrower name");
            templates.Search.Text = "不存在的模板";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(!templates.CardButtons.Any() && templates.Results.Text == "没有匹配的模板，请更换关键词。", "empty search explains how to recover");
            templates.Search.Text = "";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Require(templates.ActiveGroup == "窦性心律" && templates.Groups.SelectedIndex == 0 && !templates.Results.IsVisible &&
                window.Settings.EcgSelection == 1 && ReferenceEquals(unchanged, window.Session), "clearing search returns to the browsed group and keeps the draft");
            window.Settings.TemplateSignals.SelectedIndex = 1;
            Capture(window, "ui-preview-respiration.png");
            var respiration = window.Settings.TemplatePages[1];
            Require(!respiration.Browsable && respiration.CardButtons.Count() == 4 && respiration.Search.Parent is null,
                "short catalogues show every template under group headings");
            Card(respiration, "潮式呼吸").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Require(window.Settings.RespirationSelection == 1 && ReferenceEquals(unchanged, window.Session), "respiration card changes only the draft");
            window.Settings.RespirationSelection = 0;
            window.Settings.TemplateSignals.SelectedIndex = 0;
            templates.ShowGroup("室性早搏");
            Card(templates, "单形室早").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.Settings.TemplateSignals.SelectedIndex = 2;
            var ejection = window.Settings.TemplatePages[2];
            Require(Card(ejection, "早搏弱射血（需室早）").IsEnabled && !Card(ejection, "2:1漏搏（需窦性参考）").IsEnabled,
                "switching signal tabs refreshes ejection compatibility after changing the ECG draft");
            Card(ejection, "早搏弱射血（需室早）").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.Settings.TemplateSignals.SelectedIndex = 0;
            Require(templates.CardButtons.Count(button => button.IsEnabled) == 1 && Card(templates, "单形室早").IsEnabled,
                "returning to ECG disables cards incompatible with the new ejection draft");
            window.Settings.TemplateSignals.SelectedIndex = 2;
            Card(ejection, "随当前节律").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            window.Settings.TemplateSignals.SelectedIndex = 0;
            Require(templates.CardButtons.All(button => button.IsEnabled) && ReferenceEquals(unchanged, window.Session),
                "restoring rhythm-dependent ejection re-enables ECG cards without applying drafts");
            window.Settings.EcgSelection = 0;
            window.Settings.Tabs.SelectedIndex = 2; Capture(window, "ui-preview-display.png");
            Require(AutomationProperties.GetName(window.Settings.Slots[0].Speed) == "第1行扫描速度，相对毫米每秒", "speed control has a contextual accessibility name");
            Require(window.Settings.Slots[0].Channel.Bounds.Height > 0, "display tab content has completed layout");
            DesktopViewportSmokeChecks.Layout(window, 1000, 720); Capture(window, "ui-preview-display-compact.png");
            DesktopViewportSmokeChecks.Layout(window);
            window.Settings.Tabs.SelectedIndex = 3; Capture(window, "ui-preview-audio.png");
            var settingsSession = window.Session;
            var alarmGroups = window.Settings.SectionPages[4];
            string?[] alarmLabels = alarmGroups.Sections.Items.Cast<ListBoxItem>().Select(AutomationProperties.GetName).ToArray();
            Require(alarmLabels.Contains("EtCO₂，已关闭") && alarmLabels.Contains("PR · PLETH，已关闭") && alarmLabels.Contains("ABP 平均压，已关闭") &&
                !alarmLabels.Contains("其他测量参数") && !alarmLabels.Contains("CO₂ 呼吸检测"), "measurement alarms are peer navigation entries with their state");
            var alarmHeaders = alarmGroups.Sections.Items.Cast<ListBoxItem>().Where(item => !item.IsEnabled).ToArray();
            Require(alarmHeaders.Select(item => ((TextBlock)item.Content!).Text).SequenceEqual(["测量参数", "提示与声音"]) &&
                alarmHeaders.All(item => !item.Focusable) && alarmGroups.DetailFor(0) == "开",
                "alarm navigation separates measurements from shared prompt and sound settings");
            window.Settings.Alerts.NoExpirationEnabled.IsChecked = true;
            Require(alarmGroups.DetailFor(window.Settings.Alerts.Parameters.Select(p => p.Numeric).ToList().IndexOf(MonitorNumeric.EtCo2)) == "开",
                "EtCO2 state includes the no-expiration alarm");
            window.Settings.Alerts.NoExpirationEnabled.IsChecked = false;
            window.Settings.Tabs.SelectedIndex = 4;
            alarmGroups.SelectedSection = 0;
            var heartRateGroups = window.Settings.Alerts.HeartRateConfirmation;
            var originalConfirmation = heartRateGroups.Read();
            Require(string.Join(" / ", heartRateGroups.Groups.Items.Cast<TabItem>().Select(item => item.Header?.ToString())) ==
                "阈值 / 触发确认 / 恢复确认 / 声音 / ECG 检测项", "alarm parameters use the existing advanced-page tab pattern");
            heartRateGroups.Groups.SelectedIndex = 1;
            heartRateGroups.Fields[0].Value = 1.25m;
            Capture(window, "ui-preview-alarm-trigger-groups.png");
            Require(heartRateGroups.Fields.Where((_, index) => index % 2 == 0).All(field => field.GetVisualAncestors().Contains(window.Settings)) &&
                heartRateGroups.Fields.Where((_, index) => index % 2 == 1).All(field => !field.GetVisualAncestors().Contains(window.Settings)) &&
                !window.Settings.Alerts.WarningHeartRate.GetVisualAncestors().Contains(window.Settings),
                "trigger page only displays trigger inputs, without the threshold and recovery stack");
            heartRateGroups.Groups.SelectedIndex = 2;
            heartRateGroups.Fields[1].Value = .75m;
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Capture(window, "ui-preview-alarm-recovery-compact.png");
            Require(heartRateGroups.Fields[1].GetVisualAncestors().Contains(window.Settings) &&
                window.Settings.Alerts.HeartRateEnabled.GetVisualAncestors().Contains(window.Settings),
                "compact recovery page keeps its inputs and alarm switch accessible");
            var alarmScroll = heartRateGroups.GetVisualAncestors().OfType<ScrollViewer>().First();
            Require(alarmScroll.Extent.Width <= alarmScroll.Viewport.Width + 1 &&
                alarmScroll.Extent.Height <= alarmScroll.Viewport.Height + 100,
                $"grouped alarm pages avoid horizontal scrolling and a long vertical stack in compact layout: extent {alarmScroll.Extent}, viewport {alarmScroll.Viewport}");
            alarmGroups.SelectedSection = 1;
            alarmGroups.SelectedSection = 0;
            Require(heartRateGroups.Groups.SelectedIndex == 2 && heartRateGroups.Read().CriticalLow == new BoundaryConfirmationTiming(1250, 750) &&
                ReferenceEquals(settingsSession, window.Session), "alarm navigation preserves edited values, selected subgroup and running session");
            heartRateGroups.Restore(originalConfirmation);
            heartRateGroups.Groups.SelectedIndex = 0;
            alarmGroups.SelectedSection = window.Settings.Alerts.Parameters.Select(p => p.Numeric).ToList().IndexOf(MonitorNumeric.EtCo2);
            var co2Groups = window.Settings.Alerts.AdditionalLimits.Editors[MonitorNumeric.EtCo2].Confirmation.Groups;
            co2Groups.SelectedIndex = 3;
            Capture(window, "ui-preview-alarm-co2-absence-group.png");
            Require(window.Settings.Alerts.NoExpirationEnabled.GetVisualAncestors().Contains(window.Settings) &&
                window.Settings.Alerts.NoExpirationSeconds.GetVisualAncestors().Contains(window.Settings) &&
                window.Settings.Alerts.NoExpirationTriggerSeconds.GetVisualAncestors().Contains(window.Settings) &&
                window.Settings.Alerts.NoExpirationRecoverySeconds.GetVisualAncestors().Contains(window.Settings) &&
                !window.Settings.Alerts.AdditionalLimits.Editors[MonitorNumeric.EtCo2].CriticalLow.GetVisualAncestors().Contains(window.Settings),
                "CO2 absence has its own subgroup within EtCO2");
            var absenceScroll = window.Settings.Alerts.NoExpirationSeconds.GetVisualAncestors().OfType<ScrollViewer>().First();
            Require(absenceScroll.Extent.Width <= absenceScroll.Viewport.Width + 1 &&
                absenceScroll.Extent.Height <= absenceScroll.Viewport.Height + 100,
                $"CO2 condition and confirmation fit the compact group: extent {absenceScroll.Extent}, viewport {absenceScroll.Viewport}");
            co2Groups.SelectedIndex = 0;
            alarmGroups.SelectedSection = 0;
            DesktopViewportSmokeChecks.Layout(window);
            window.Settings.Tabs.SelectedIndex = 3;
            var soundGroups = window.Settings.SectionPages[3];
            soundGroups.Sections.SelectedIndex = 1;
            window.Settings.Sound.HeartbeatVolume.Value = 62;
            Capture(window, "ui-preview-settings-hierarchy.png");
            Require(window.Settings.Sound.HeartbeatVolume.GetVisualAncestors().Contains(window.Settings), "selected parameter group is visible");
            Require(!window.Settings.Sound.PauseSeconds.GetVisualAncestors().Contains(window.Settings), "unselected group is progressively hidden");
            window.Settings.Tabs.SelectedIndex = 4; window.Settings.Tabs.SelectedIndex = 3;
            Require(soundGroups.Sections.SelectedIndex == 1 && window.Settings.Sound.HeartbeatVolume.Value == 62 && ReferenceEquals(settingsSession, window.Session), "navigation preserves section, edits and running simulation");
            DesktopViewportSmokeChecks.Layout(window, 1000, 720);
            Capture(window, "ui-preview-settings-hierarchy-compact.png");
            Require(window.Settings.CompactNavigation && soundGroups.Compact, "narrow windows collapse navigation columns instead of squeezing parameter cards");
            Require(soundGroups.Bounds.Width > 680 && window.Settings.Sound.HeartbeatVolume.TranslatePoint(default, window.Settings)!.Value.X < 100, "compact details reclaim both hidden navigation columns");
            Require(window.Settings.Sound.HeartbeatVolume.Bounds.Width > 0 && window.Settings.Apply.Bounds.Height >= 44, "compact layout retains parameter access and apply action");
            DesktopViewportSmokeChecks.Layout(window);
            window.Settings.Sound.HeartbeatVolume.Value = 100;
            soundGroups.Sections.SelectedIndex = 0;
            VerifySoundSettings();
            window.Settings.Tabs.SelectedIndex = 4; Capture(window, "ui-preview-alarms.png");
            window.Settings.Tabs.SelectedIndex = 5; Capture(window, "ui-preview-vitals.png");
            window.Settings.Tabs.SelectedIndex = 6;
            window.Settings.SectionPages[6].SelectedSection = 3;
            Capture(window, "ui-preview-advanced.png");
            Require(window.Settings.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("窦性参考", StringComparison.Ordinal) == true), "applied parameter page shows the active style");
            Require(window.Settings.Parent is not null && window.Settings.Tabs.ItemCount == 7, "general preferences and all six simulation categories remain available");
            var source = window.Session;
            window.Settings.Slots[0].Minimum.Text = "NaN";
            window.RestartSettings();
            Require(ReferenceEquals(window.Session, source) && ReferenceEquals(window.ActiveTimer, timer), "invalid settings preserve live session and timer");
            window.Settings.Skin.SelectedIndex = 0;
            window.Settings.OpticalEnabled.IsChecked = true;
            window.Settings.OpticalTarget.Value = 98;
            window.Settings.Slots[0].Auto.IsChecked = false;
            window.Settings.Slots[0].Minimum.Text = "-0.01";
            window.Settings.Slots[0].Maximum.Text = "0.01";
            window.Settings.Slots[1].Speed.SelectedIndex = 2;
            window.Settings.Slots[2].Speed.SelectedIndex = 0;
            window.RestartSettings();
            Require(!ReferenceEquals(source, window.Session) && window.Session.Display.Slots.Count == 3, "apply changes fixed skin and restarts");
            Require(window.MonitorView.NumericTexts.All(t => t == "---"), "apply clears previous numeric readings until newly acquired");
            window.Pulse(timer, 50_000_000);
            Require(window.Session.SimulationTimeNs == 0, "callbacks from old run are fenced");
            timer = window.ActiveTimer;
            for (int i = 0; i < 150; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.MonitorView.NumericTexts[1] == "98", "explicit optical source reaches measured on-screen SpO2");
            window.SelectPage(0); Capture(window, "ui-preview-optical-pi.png");
            Require(window.MonitorView.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("PI ", StringComparison.Ordinal) == true && !t.Text.Contains("---", StringComparison.Ordinal)), "measured PI is visible beside saturation");
            List<double> levels = [];
            for (int i = 0; i < 16; i++) { window.Pulse(timer, 50_000_000); levels.Add(window.MonitorView.PulseLevels[0]); }
            Require(levels.Max() - levels.Min() > .5, "perfusion bar follows sampled pulse excursion");
            VerifyClamping(window.MonitorTrace);
            for (int i = 0; i < 300; i++) { window.Pulse(timer, 50_000_000); }
            Require(window.Session.Ranges.RowCycle(0) == 2 && window.Session.Ranges.RowCycle(1) == 4 && window.Session.Ranges.RowCycle(2) == 1, "native sweep wraps twice with boundary range updates");
            window.SelectPage(0); Capture(window, "ui-preview-monitor-wrap.png");
            window.Pause(); long paused = window.Session.SimulationTimeNs;
            string[] held = window.MonitorView.NumericTexts.ToArray();
            window.Pulse(timer, 50_000_000); Require(window.Session.SimulationTimeNs == paused, "pause holds simulation");
            Require(held.SequenceEqual(window.MonitorView.NumericTexts), "paused numerics hold with patient time");
            window.Start(); window.SelectPage(0); Require(window.Session.SimulationTimeNs == paused, "navigation/resume never regenerates history");
            window.Settings.AbpTargetEnabled.IsChecked = false;
            window.Settings.PaTargetEnabled.IsChecked = false;
            foreach (var choice in new[] { (1, 1, 0), (2, 2, 1), (0, 0, 2), (0, 3, 3), (3, 0, 0), (4, 0, 0), (5, 0, 0) })
            {
                source = window.Session;
                window.Settings.EcgSelection = choice.Item1;
                window.Settings.RespirationSelection = choice.Item2;
                window.Settings.EjectionSelection = choice.Item3;
                window.RestartSettings();
                Require(!ReferenceEquals(source, window.Session), "supported rhythm/breathing/ejection combination applies");
            }
            window.Settings.Skin.SelectedIndex = 2;
            window.Settings.EcgSelection = window.Settings.RespirationSelection = window.Settings.EjectionSelection = 0;
            window.RestartSettings(); window.SelectPage(0);
            for (int i = 0; i < 240; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            Capture(window, "ui-preview-perfusion.png");
            VerifySeededVitals(window);
        }
        finally { window.Close(); }
        Console.WriteLine("ok: responsive paper/live monitor, fixed skin slots, clipping, settings separation and timer lifecycle");
    }
    private static void VerifyRespirationOverview()
    {
        var regular = StylePreviewCatalog.Respiration(0);
        var tidal = StylePreviewCatalog.Respiration(1);
        var intermittent = StylePreviewCatalog.Respiration(2);
        var absent = StylePreviewCatalog.Respiration(3);
        Require(tidal[^1].TimeNs >= DesignPreviewSettings.RespirationPreviewDurationNs - 40_000_000,
            "respiration thumbnail includes the entire eleven-breath pattern");
        double Peak((long TimeNs, double Value)[] samples, int breath) => samples.Where(s => s.TimeNs >= breath * 3_750_000_000L && s.TimeNs < (breath + 1) * 3_750_000_000L).Max(s => Math.Abs(s.Value));
        Require(Peak(tidal, 4) > Peak(tidal, 0) * 4 && Peak(tidal, 8) < Peak(tidal, 4) / 4 && Peak(tidal, 9) == 0 && Peak(tidal, 10) == 0,
            "cached tidal overview shows crescendo, decrescendo and the entire pause");
        Require(Peak(regular, 9) > 0 && Peak(intermittent, 3) == 0 && Peak(intermittent, 5) > 0 && absent.All(s => s.Value == 0),
            "all four respiratory choices remain distinguishable from actual source data");
    }
    private static void VerifyAtrialProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            window.Settings.AbpTargetEnabled.IsChecked = false;
            window.Settings.PaTargetEnabled.IsChecked = false;
            foreach (int choice in new[] { 6, 7, 8, 9, 37, 38, 39, 8 })
            {
                var previous = window.Session; window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "atrial product preset applies atomically");
                var rates = new HashSet<int>();
                for (int i = 0; i < 400; i++)
                {
                    window.Pulse(window.ActiveTimer, 50_000_000);
                    if (i >= 200 && window.Session.Measurements!.HeartRate.MilliBeatsPerMinute is int rate) { rates.Add(rate); }
                }
                Require(window.Session.Measurements!.HeartRate.Status == WaveformMeasurementStatus.Valid, "atrial product ECG reaches measured HR");
                if (choice >= 8)
                {
                    if (choice == 39)
                    {
                        Require(rates.Count > 1 && rates.All(rate => rate is >= 25000 and <= 150000),
                            "seeded variable flutter HR fluctuates within the 2–12 atrial-cycle conduction bounds");
                    }
                    else
                    {
                        int expected = choice switch { 8 => 150000, 9 => 75000, 37 => 300000, _ => 100000 };
                        Require(rates.Count > 0 && rates.All(rate => Math.Abs(rate - expected) <= 1000),
                            "flutter HR follows acquired conducted QRS including one-to-one and three-to-one");
                    }
                    var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                    int ratio = choice switch { 9 => 4, 37 => 1, 38 => 3, _ => 2 };
                    Require(pair.Physiology.VentricularConductionRatio == ratio && pair.Ecg.VentricularConductionRatio == ratio &&
                        pair.Physiology.ConductionPattern == pair.Ecg.ConductionPattern &&
                        (pair.Ecg.ConductionPattern == Monitor.Simulation.Physiology.AvConductionPattern.VariableAtrialFlutterIllustration) == (choice == 39),
                        "monitor and paper share fixed or variable flutter conduction and reset old pattern");
                    Require(window.Session.Measurements.AbpMean.Status == WaveformMeasurementStatus.Valid,
                        "flutter pressure remains sample-derived and available");
                    Require(window.Session.Measurements.AbpMean.MeanCentiMmHg is > 7000 and < 15000,
                        "all flutter product examples use filling-limited pressure input");
                    if (choice == 37)
                    {
                        Require(window.Session.Measurements.AbpMean.MeanCentiMmHg is > 7500 and < 9000,
                        $"one-to-one flutter reuses corrected perfusion: {window.Session.Measurements.AbpMean}");
                    }
                    var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                    var cached = StylePreviewCatalog.Get(choice, 0, 0);
                    long duration = StylePreviewCatalog.DurationNs(choice);
                    Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                        cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                        "flutter card uses the selected waveform and its perfusion source");
                    if (choice == 39)
                    { Require(duration == 3_600_000_000, "variable flutter preview spans two complete conduction groups"); }
                }
                window.SelectPage(1); Capture(window, $"ui-preview-atrial-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "atrial product paper contains complete twelve-lead acquisition");
                window.SelectPage(0);
            }
            var live = window.Session; window.Settings.EjectionSelection = 1; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incompatible ventricular-premature ejection cannot replace atrial session");
        }
        finally { window.Close(); }
    }
    private static void VerifyAtrialShapeProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 98, 99, 100, 101, 99, 0 })
            {
                var previous = window.Session; window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "atrial shape replaces preceding compound/rhythm source");
                if (choice == 98) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require((int)pair.Physiology.AtrialShape == (choice == 0 ? 0 : choice - 98) &&
                    pair.Physiology.AtrialShape == pair.Ecg.Atrial && !pair.Ecg.HyperkalemiaFusion,
                    "monitor and paper share atrial shape and clear prior fusion");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid && reading.HeartRate.MilliBeatsPerMinute == 75000 &&
                    reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "atrial shape preserves sample-derived electrical and mechanical measurements");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0); var cached = StylePreviewCatalog.Get(choice, 0, 0);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)),
                    "atrial shape card is actual selected acquired ECG");
                Require(EcgTemplateSummary.Describe(pair.Ecg).Contains($"P {(choice is 99 or 101 ? 140 : 100)} ms", StringComparison.Ordinal),
                    "advanced summary reports resolved P duration");
                window.SelectPage(1); Capture(window, $"ui-preview-atrial-shape-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "atrial shape paper retains full twelve-lead record"); window.SelectPage(0);
            }
            window.Settings.EcgSelection = 101; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "atrial shape permits independent no-ejection setting");
        }
        finally { window.Close(); }
    }

    private static void VerifyVentricularShapeProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 101, 102, 103, 104, 105, 106, 102, 0 })
            {
                var previous = window.Session; window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "ventricular shape replaces preceding compound/rhythm source");
                if (choice == 101) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require((int)pair.Physiology.VentricularShape == (choice == 0 ? 0 : choice - 101) &&
                    pair.Physiology.VentricularShape == pair.Ecg.Ventricular && !pair.Ecg.HyperkalemiaFusion,
                    "monitor and paper share ventricular shape and clear prior fusion");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid && reading.HeartRate.MilliBeatsPerMinute == 75000 &&
                    reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "ventricular shape preserves sample-derived electrical and mechanical measurements");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0); var cached = StylePreviewCatalog.Get(choice, 0, 0);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)),
                    "ventricular shape card is actual selected acquired ECG");
                Require(EcgTemplateSummary.Describe(pair.Ecg).Contains($"QRS {(choice is 102 or 104 ? 100 : 80)} ms", StringComparison.Ordinal),
                    "advanced summary reports resolved QRS duration");
                window.SelectPage(1); Capture(window, $"ui-preview-ventricular-shape-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "ventricular shape paper retains full twelve-lead record"); window.SelectPage(0);
            }
            window.Settings.EcgSelection = 106; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "ventricular shape permits independent no-ejection setting");
        }
        finally { window.Close(); }
    }

    private static void VerifyTContourProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 106, 107, 108, 109, 110, 111, 112, 113, 114, 0 })
            {
                var previous = window.Session; window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "T contour replaces preceding compound/rhythm source");
                if (choice == 106) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.TContour == pair.Ecg.TContour &&
                    pair.Ecg.TContour == (choice == 0 ? null : TContourProductPreset.Create(choice - 107)) &&
                    pair.Ecg.Ventricular == Monitor.Simulation.Physiology.EcgVentricularIllustration.Reference,
                    "monitor and paper share contour and clear prior ventricular shape");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid && reading.HeartRate.MilliBeatsPerMinute == 75000 &&
                    reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "T contour preserves sample-derived electrical and mechanical measurements");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0); var cached = StylePreviewCatalog.Get(choice, 0, 0);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)),
                    "T contour card is actual selected acquired ECG");
                if (choice != 0)
                {
                    string description = EcgTemplateSummary.Describe(pair.Ecg);
                    Require(description.Contains("T 目标 II", StringComparison.Ordinal), "summary identifies contour target");
                    if (choice is 107 or 108) { Require(description.Contains(choice == 107 ? "过零 35%" : "过零 65%", StringComparison.Ordinal), "summary shows noncentral crossing"); }
                }
                window.SelectPage(1); Capture(window, $"ui-preview-t-contour-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "T contour paper retains full twelve-lead record"); window.SelectPage(0);
            }
            window.Settings.EcgSelection = 114; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "T contour permits independent no-ejection setting");
            window.Settings.EjectionSelection = 0;
            window.Settings.EcgSelection = 107;
            window.SelectPage(2); window.Settings.Tabs.SelectedIndex = 6;
            var editor = window.Settings.TContourParameters;
            Require(editor.IsVisible && editor.Parent is not null, "compatible contour editor is reachable in advanced settings");
            editor.Peak.Value = 450; editor.SecondPeak.Value = 150; editor.Crossing.Value = 25;
            Require(window.Settings.ShapeEditStatus.Text!.Contains("待应用", StringComparison.Ordinal) &&
                window.Settings.ShapeEditSummary.Text!.Contains("峰幅 450", StringComparison.Ordinal) && window.Settings.ShapeEditSummary.Text.Contains("过零 25%", StringComparison.Ordinal), "draft summary follows contour fields before application");
            window.RestartSettings();
            Require(window.Settings.ShapeEditStatus.Text!.Contains("已应用", StringComparison.Ordinal), "successful contour application marks only accepted shape applied");
            var edited = editor.Read(TContourProductPreset.Create(0));
            Require(edited is { PeakMicrovolts: 450, SecondPeakMicrovolts: 150, CrossingPositionPermille: 250 }, "advanced contour preserves independent lobes and crossing");
            var (period, inspiration) = window.Settings.ReadBreathingTiming();
            var baseline = DesignPreviewWindow.ResolveStyle(107, 0, 0).Physiology with { BreathPeriodMilliseconds = period, InspirationMilliseconds = inspiration };
            var expected = new LocalMonitorPreviewSession(baseline with { TContour = edited }, window.Session.Display);
            expected.DiscardStartup();
            var original = new LocalMonitorPreviewSession(baseline, window.Session.Display);
            original.DiscardStartup();
            for (int i = 0; i < 100; i++)
            {
                window.Pulse(window.ActiveTimer, 50_000_000);
                expected.Advance(50_000_000); original.Advance(50_000_000);
            }
            long end = window.Session.FrontierNs;
            Require(window.Session.Samples(0, 0, end).SequenceEqual(expected.Samples(0, 0, end)) &&
                !window.Session.Samples(0, 0, end).SequenceEqual(original.Samples(0, 0, end)), "edited contour reaches acquired monitor signal");
            for (int channel = 1; channel < 7; channel++)
            { Require(window.Session.Samples(channel, 0, end).SequenceEqual(original.Samples(channel, 0, end)), "contour editing preserves non-ECG channels"); }
            window.SelectPage(1); Capture(window, "ui-preview-t-contour-edited-paper.png");
            foreach (int target in Enumerable.Range(0, 7))
            {
                editor.Target.SelectedIndex = target;
                for (int i = 0; i < 6; i++) { editor.ChestLeads[i].IsChecked = i is 1 or 3; }
                var plan = editor.Read(TContourProductPreset.Create(0))!;
                Require((int)plan.Target == target && (target != 0 || plan.ChestMask == 10), "target editor preserves exact chest selection or limb target");
                if (target == 0)
                { Require(EcgTemplateSummary.Describe(DesignPreviewWindow.ResolveStyle(107, 0, 0).Ecg with { TContour = plan }).Contains("V2、V4", StringComparison.Ordinal), "summary identifies selected chest leads"); }
                live = window.Session; window.RestartSettings();
                Require(!ReferenceEquals(live, window.Session), "each target applies through shared monitor and paper path");
                var targetSource = new LocalMonitorPreviewSession(baseline with { TContour = plan }, window.Session.Display);
                targetSource.DiscardStartup();
                for (int i = 0; i < 60; i++) { window.Pulse(window.ActiveTimer, 50_000_000); targetSource.Advance(50_000_000); }
                for (int channel = 0; channel < 7; channel++)
                { Require(window.Session.Samples(channel, 0, window.Session.FrontierNs).SequenceEqual(targetSource.Samples(channel, 0, targetSource.FrontierNs)), "selected target retains projected acquisition parity across channels"); }
            }
            editor.Target.SelectedIndex = 0;
            foreach (var lead in editor.ChestLeads) { lead.IsChecked = false; }
            live = window.Session; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session) && window.Settings.Status.Text!.Contains("至少选择一个胸导联", StringComparison.Ordinal) &&
                window.Settings.ShapeEditStatus.Text!.Contains("输入不完整或无效", StringComparison.Ordinal), "empty chest selection rejects atomically with actionable message and invalid draft state");
            editor.ChestLeads[1].IsChecked = true;
            window.SelectPage(2); window.Settings.Tabs.SelectedIndex = 1; window.Settings.Tabs.SelectedIndex = 6;
            Capture(window, "ui-preview-t-contour-target-editor.png");
            live = window.Session; editor.Crossing.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session) && window.Settings.ShapeEditStatus.Text!.Contains("输入不完整或无效", StringComparison.Ordinal), "missing contour parameter rejects without replacing session or showing stale summary");
            window.Settings.EcgSelection = 108;
            Require(editor.Crossing.Value == 65 && editor.Peak.Value == 300 && editor.SecondPeak.Value == 200 && editor.Target.SelectedIndex == 2 &&
                editor.ChestLeads.Select((lead, index) => (lead.IsChecked == true) == (index == 0)).All(matches => matches), "switching contour resets draft and target to selected template");
            window.Settings.EcgSelection = 0; window.RestartSettings();
            Require(!editor.IsVisible && !ReferenceEquals(live, window.Session), "incompatible template ignores stale contour edits");
        }
        finally { window.Close(); }
    }

    private static void VerifyRegionalInfarctionProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in Enumerable.Range(114, 51).Append(115).Append(125).Append(135).Append(0))
            {
                var previous = window.Session; window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "regional snapshot replaces preceding compound/rhythm source");
                if (choice == 114) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.Infarction == pair.Ecg.Infarction &&
                    pair.Ecg.Infarction == (choice == 0 ? null : InfarctionProductPreset.Create((choice - 115) % 10, (Monitor.Simulation.Physiology.InfarctionTerritory)((choice - 115) / 10 + 1))) &&
                    pair.Ecg.TContour is null,
                    "monitor and paper share contour and clear prior T contour");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid && reading.HeartRate.MilliBeatsPerMinute == 75000 &&
                    reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "regional snapshot preserves sample-derived electrical and mechanical measurements");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0); var cached = StylePreviewCatalog.Get(choice, 0, 0);
                if (choice is >= 125 and <= 164)
                {
                    Require(cached.Lead == (choice < 135 ? Monitor.Simulation.Physiology.EcgLead.I : choice < 145 ? Monitor.Simulation.Physiology.EcgLead.V2 : choice < 155 ? Monitor.Simulation.Physiology.EcgLead.V4 : Monitor.Simulation.Physiology.EcgLead.V3), "regional cards identify the affected lead");
                    long from = source.FrontierNs - StylePreviewCatalog.DurationNs(choice), to = source.FrontierNs;
                    var projected = StylePreviewCatalog.CreateProjectedPreview(pair.Ecg, cached.Lead, from, to);
                    var baseline = StylePreviewCatalog.CreateProjectedPreview(ProjectedEcgDemoConfiguration.Default, cached.Lead, from, to);
                    Require(cached.Ecg.SequenceEqual(projected) && cached.Ecg.Length == 750 &&
                        cached.Ecg.Zip(baseline).Any(p => Math.Abs(p.First.Value - p.Second.Value) > 50),
                        "regional card contains complete actual paper-lead samples and visible regional changes");
                    Require(cached.Abp.SequenceEqual(source.Samples(3, from, to)), "preview lead choice does not alter perfusion preview");
                }
                else
                {
                    Require(cached.Lead == Monitor.Simulation.Physiology.EcgLead.II &&
                        cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)),
                        "ordinary cards retain sampled monitor II");
                }
                if (choice != 0)
                {
                    string description = EcgTemplateSummary.Describe(pair.Ecg);
                    Require(description.Contains(InfarctionProductPreset.TerritoryName(pair.Ecg.Infarction!.Territory) + "独立快照", StringComparison.Ordinal) && description.Contains("不随模拟时间演变", StringComparison.Ordinal), "summary identifies independent snapshot");
                    if ((choice - 115) % 10 is 1 or 2)
                    { Require(description.Contains("ST–T 融合", StringComparison.Ordinal) && !description.Contains("T 180 ms", StringComparison.Ordinal), "fused contour omits independently measured T duration"); }
                    if ((choice - 115) % 10 == 1) { Require(description.Contains("局部 QRS 96 ms", StringComparison.Ordinal), "hyperacute local widening is described accurately"); }
                }
                window.SelectPage(1); Capture(window, $"ui-preview-regional-infarction-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "regional snapshot paper retains full twelve-lead record"); window.SelectPage(0);
            }
            window.Settings.EcgSelection = 164; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "regional snapshot permits independent no-ejection setting");
            window.Settings.EjectionSelection = 0;
            foreach (int choice in new[] { 118, 128, 136, 148, 164 })
            {
                window.Settings.EcgSelection = choice;
                var editor = window.Settings.InfarctionParameters;
                editor.Delay.Value = 80;
                for (int i = 0; i < 6; i++) { editor.ChestLeads[i].IsChecked = i is 1 or 3; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                var edited = editor.Read(pair.Ecg.Infarction)!;
                Require(edited.RepolarizationDelayNs == 80_000_000 && edited.Stage == pair.Ecg.Infarction!.Stage &&
                    (choice < 135 ? edited.Territory == pair.Ecg.Infarction.Territory : edited.Territory == Monitor.Simulation.Physiology.InfarctionTerritory.CustomChest && edited.ChestMask == 10), "regional editor preserves snapshot and limb territory or custom chest mask");
                live = window.Session; window.RestartSettings();
                Require(!ReferenceEquals(live, window.Session) && window.Settings.ShapeEditStatus.Text!.Contains("已应用", StringComparison.Ordinal) &&
                    window.Settings.ShapeEditSummary.Text!.Contains("局部 QT 440 ms", StringComparison.Ordinal), "regional delay and mask apply atomically with accepted extended QT summary");
                var (period, inspiration) = window.Settings.ReadBreathingTiming();
                var source = new LocalMonitorPreviewSession(pair.Physiology with { Infarction = edited, BreathPeriodMilliseconds = period, InspirationMilliseconds = inspiration }, window.Session.Display);
                source.DiscardStartup();
                for (int i = 0; i < 60; i++) { window.Pulse(window.ActiveTimer, 50_000_000); source.Advance(50_000_000); }
                for (int channel = 0; channel < 7; channel++)
                { Require(window.Session.Samples(channel, 0, window.Session.FrontierNs).SequenceEqual(source.Samples(channel, 0, source.FrontierNs)), "edited region reaches shared acquired source"); }
                if (choice >= 135)
                {
                    foreach (var lead in Enum.GetValues<Monitor.Simulation.Physiology.EcgLead>())
                    {
                        var actual = StylePreviewCatalog.CreateProjectedPreview(pair.Ecg with { Infarction = edited }, lead, 0, 800_000_000);
                        var baseline = StylePreviewCatalog.CreateProjectedPreview(ProjectedEcgDemoConfiguration.Default, lead, 0, 800_000_000);
                        bool changed = !actual.SequenceEqual(baseline);
                        Require(changed == (lead is Monitor.Simulation.Physiology.EcgLead.V2 or Monitor.Simulation.Physiology.EcgLead.V4), "custom infarction changes only selected chest leads");
                    }
                    Require(EcgTemplateSummary.Describe(pair.Ecg with { Infarction = edited }).Contains("V2、V4", StringComparison.Ordinal), "custom region summary names selected leads");
                }
            }
            foreach (int choice in new[] { 116, 126, 136 })
            {
                window.Settings.EcgSelection = choice;
                var controls = window.Settings.InfarctionParameters;
                controls.ComponentsEnabled.IsChecked = true;
                controls.Necrosis.SelectedIndex = 2; controls.QrsWeight.Value = 60;
                controls.ReferenceT.IsChecked = false; controls.TPeak.Value = -500;
                controls.JPoint.Value = 200; controls.StEnd.Value = 100; controls.StArch.Value = 150;
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                var plan = controls.Read(pair.Ecg.Infarction)!;
                Require(plan.Components is
                {
                    Necrosis: Monitor.Simulation.Physiology.NecrosisIllustrationShape.QS, QrsTemplatePermille: 600,
                    TPeakMicrovolts: -500, JMicrovolts: 200, StEndMicrovolts: 100, StArchMicrovolts: 150
                }, "independent infarction components preserve all authored controls");
                window.RestartSettings();
                Require(window.Settings.ShapeEditStatus.Text!.Contains("已应用", StringComparison.Ordinal) && window.Settings.ShapeEditSummary.Text!.Contains("独立分量", StringComparison.Ordinal) &&
                    !window.Settings.ShapeEditSummary.Text.Contains("ST–T 融合", StringComparison.Ordinal), "component mode summary does not claim stage fusion");
                var (period, inspiration) = window.Settings.ReadBreathingTiming();
                var config = pair.Physiology with { Infarction = plan, BreathPeriodMilliseconds = period, InspirationMilliseconds = inspiration };
                var expected = new LocalMonitorPreviewSession(config, window.Session.Display);
                expected.DiscardStartup();
                var stage = new LocalMonitorPreviewSession(config with { Infarction = pair.Physiology.Infarction }, window.Session.Display);
                stage.DiscardStartup();
                for (int i = 0; i < 60; i++) { window.Pulse(window.ActiveTimer, 50_000_000); expected.Advance(50_000_000); stage.Advance(50_000_000); }
                for (int channel = 0; channel < 7; channel++)
                {
                    Require(window.Session.Samples(channel, 0, expected.FrontierNs).SequenceEqual(expected.Samples(channel, 0, expected.FrontierNs)), "component editing reaches acquired samples");
                    if (channel > 0) { Require(expected.Samples(channel, 0, expected.FrontierNs).SequenceEqual(stage.Samples(channel, 0, stage.FrontierNs)), "component editing preserves mechanical and respiratory channels"); }
                }
                live = window.Session; controls.JPoint.Value = null; window.RestartSettings();
                Require(ReferenceEquals(live, window.Session) && window.Settings.Status.Text!.Contains("ST／T", StringComparison.Ordinal), "incomplete independent components reject atomically");
                controls.ComponentsEnabled.IsChecked = false; window.RestartSettings();
                Require(!ReferenceEquals(live, window.Session) && controls.Read(pair.Ecg.Infarction) == pair.Ecg.Infarction, "disabling component editing exactly restores original fusion stage despite stale fields");
            }
            var zoneEditor = window.Settings.InfarctionParameters;
            zoneEditor.ComponentsEnabled.IsChecked = true; zoneEditor.SeparateRegions.IsChecked = true;
            zoneEditor.IschemiaRegion.SelectedIndex = 10; zoneEditor.InjuryRegion.SelectedIndex = 2; zoneEditor.NecrosisRegion.SelectedIndex = 11;
            zoneEditor.ReferenceT.IsChecked = false; zoneEditor.TPeak.Value = -400;
            zoneEditor.JPoint.Value = 200; zoneEditor.StEnd.Value = 100; zoneEditor.StArch.Value = 150;
            zoneEditor.Necrosis.SelectedIndex = 1; zoneEditor.QrsWeight.Value = 60; zoneEditor.Delay.Value = 80;
            var zonePair = DesignPreviewWindow.ResolveStyle(window.Settings.EcgSelection, 0, 0);
            var zones = zoneEditor.ReadZones(zonePair.Ecg.Infarction)!;
            Require(zones.Ischemia.Territory == Monitor.Simulation.Physiology.InfarctionTerritory.Inferior && zones.Injury.ChestMask == 2 &&
                zones.Necrosis.Territory == Monitor.Simulation.Physiology.InfarctionTerritory.Lateral, "separate component selectors retain different regions");
            window.RestartSettings();
            Require(window.Settings.ShapeEditStatus.Text!.Contains("已应用", StringComparison.Ordinal) && window.Settings.ShapeEditSummary.Text!.Contains("缺血：下壁", StringComparison.Ordinal), "zone summary and applied status identify separate regions");
            var zoneTiming = window.Settings.ReadBreathingTiming();
            var zoneSource = new LocalMonitorPreviewSession(zonePair.Physiology with { Infarction = null, Zones = zones, BreathPeriodMilliseconds = zoneTiming.Item1, InspirationMilliseconds = zoneTiming.Item2 }, window.Session.Display);
            zoneSource.DiscardStartup();
            for (int i = 0; i < 60; i++) { window.Pulse(window.ActiveTimer, 50_000_000); zoneSource.Advance(50_000_000); }
            for (int channel = 0; channel < 7; channel++)
            { Require(window.Session.Samples(channel, 0, zoneSource.FrontierNs).SequenceEqual(zoneSource.Samples(channel, 0, zoneSource.FrontierNs)), "zone edits reach shared acquired monitor channels"); }
            window.SelectPage(2); window.Settings.Tabs.SelectedIndex = 6;
            var acceptedZones = zoneEditor.ReadZones(zonePair.Ecg.Infarction);
            live = window.Session;
            for (int group = 0; group < 4; group++)
            {
                zoneEditor.Groups.SelectedIndex = group;
                Require(ReferenceEquals(live, window.Session) && zoneEditor.ReadZones(zonePair.Ecg.Infarction) == acceptedZones,
                    "parameter group navigation preserves draft and running session");
                Capture(window, $"ui-preview-infarction-group-{group}.png");
            }
            window.Width = 960; zoneEditor.Groups.SelectedIndex = 0;
            Capture(window, "ui-preview-infarction-zones.png");
            Require(zoneEditor.Groups.Bounds.Width > 0 && zoneEditor.Groups.Bounds.Width <= window.Width, "group navigation fits compact window");
            window.Width = 1440;
            live = window.Session; zoneEditor.IschemiaRegion.SelectedIndex = -1; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "missing zone choice rejects atomically");
            zoneEditor.SeparateRegions.IsChecked = false; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session) && zoneEditor.ReadZones(zonePair.Ecg.Infarction) is null, "disabling separate zones restores shared region and ignores hidden invalid selection");
            zoneEditor.SeparateRegions.IsChecked = true;
            zoneEditor.IschemiaRegion.SelectedIndex = zoneEditor.InjuryRegion.SelectedIndex = zoneEditor.NecrosisRegion.SelectedIndex = 0;
            zoneEditor.Delay.Value = zoneEditor.TPeak.Value = zoneEditor.JPoint.Value = zoneEditor.StEnd.Value = zoneEditor.StArch.Value = zoneEditor.QrsWeight.Value = null;
            zoneEditor.Necrosis.SelectedIndex = -1;
            live = window.Session; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session) && zoneEditor.Groups.Items.OfType<TabItem>().Skip(1).All(page => !page.IsEnabled),
                "disabled zones ignore dormant invalid parameters and disable their parameter groups");
            var inactive = zoneEditor.ReadZones(zonePair.Ecg.Infarction)!;
            Require(inactive.Components == new Monitor.Simulation.Physiology.EcgInfarctionComponents() && inactive.RepolarizationDelayNs == 0,
                "disabled zones contribute neutral parameters without overwriting drafts");
            // Region editing uses electrode-projected II, whereas the ordinary
            // sinus preview uses its original scalar reference table.
            var reference = new LocalMonitorPreviewSession(DesignPreviewWindow.ResolveStyle(0, 0, 0).Physiology with
            {
                Infarction = new(0, Monitor.Simulation.Physiology.InfarctionIllustrationStage.None),
                BreathPeriodMilliseconds = zoneTiming.Item1,
                InspirationMilliseconds = zoneTiming.Item2
            }, window.Session.Display);
            reference.DiscardStartup();
            for (int i = 0; i < 60; i++) { window.Pulse(window.ActiveTimer, 50_000_000); reference.Advance(50_000_000); }
            for (int channel = 0; channel < 7; channel++)
            { Require(window.Session.Samples(channel, 0, reference.FrontierNs).SequenceEqual(reference.Samples(channel, 0, reference.FrontierNs)), "all closed regions produce the unmodified electrode-reference acquisition"); }
            live = window.Session;
            foreach (var region in new[] { zoneEditor.IschemiaRegion, zoneEditor.InjuryRegion, zoneEditor.NecrosisRegion })
            {
                region.SelectedIndex = 2; window.RestartSettings();
                Require(ReferenceEquals(live, window.Session) && zoneEditor.Delay.Value is null && zoneEditor.QrsWeight.Value is null,
                    "reenabled region validates its preserved invalid draft instead of applying neutral data");
                region.SelectedIndex = 0;
            }
            zoneEditor.InjuryRegion.SelectedIndex = 2;
            zoneEditor.JPoint.Value = 200; zoneEditor.StEnd.Value = 100; zoneEditor.StArch.Value = 150;
            window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session) && zoneEditor.ReadZones(zonePair.Ecg.Infarction)!.Components.JMicrovolts == 200,
                "valid injury-only edits apply while other dormant drafts remain invalid");
            zoneEditor.Groups.SelectedIndex = 2; zoneEditor.InjuryRegion.SelectedIndex = 0;
            Require(zoneEditor.Groups.SelectedIndex == 0, "closing the displayed region returns navigation to region selection");
            Capture(window, "ui-preview-infarction-inactive-zones.png");
            window.Settings.EcgSelection = 164; window.RestartSettings();
            Require(zoneEditor.SeparateRegions.IsChecked != true, "template selection clears separate zone mode");
            Require(zoneEditor.Groups.SelectedIndex == 0 && zoneEditor.Groups.Items.OfType<TabItem>().Skip(2).All(page => !page.IsEnabled),
                "stage mode resets group selection and disables independent ST/QRS editing");
            Require(window.Settings.InfarctionParameters.ComponentsEnabled.IsChecked != true && window.Settings.InfarctionParameters.JPoint.Value == 0, "switching stages clears component overrides");
            window.SelectPage(2); window.Settings.Tabs.SelectedIndex = 6;
            Require(window.Settings.InfarctionParameters.Parent is not null, "infarction advanced editor is reachable");
            Capture(window, "ui-preview-infarction-advanced.png");
            var infarctionEditor = window.Settings.InfarctionParameters;
            live = window.Session;
            foreach (var lead in infarctionEditor.ChestLeads) { lead.IsChecked = false; }
            window.RestartSettings();
            Require(ReferenceEquals(live, window.Session) && window.Settings.Status.Text!.Contains("至少选择一个胸导联", StringComparison.Ordinal), "empty infarction mask preserves running session");
            infarctionEditor.ChestLeads[1].IsChecked = true;
            Require(window.Settings.ShapeEditStatus.Text!.Contains("待应用", StringComparison.Ordinal), "correcting region input restores a pending draft rather than stale accepted state");
            foreach (decimal? delay in new decimal?[] { null, 1.5m, 500 })
            {
                infarctionEditor.Delay.Value = delay; window.RestartSettings();
                Require(ReferenceEquals(live, window.Session) && !window.Settings.ShapeEditStatus.Text!.Contains("· 已应用", StringComparison.Ordinal), "missing, fractional or cycle-overlapping delay rejects atomically without marking draft applied");
            }
            window.Settings.EcgSelection = 135;
            Require(infarctionEditor.Delay.Value == 0 && infarctionEditor.ChestLeads.Select((lead, i) => (lead.IsChecked == true) == (i < 3)).All(matches => matches), "new chest template restores mask and delay defaults");
            window.Settings.EcgSelection = 0; window.RestartSettings();
            Require(!infarctionEditor.IsVisible && !ReferenceEquals(live, window.Session), "reference template clears infarction editing");
        }
        finally { window.Close(); }
    }

    private static void VerifyHyperkalemiaFusionProductStyle()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 97, 98, 77, 98, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "fusion preset atomically replaces and clears preceding morphology");
                if (choice == 97) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.HyperkalemiaFusion == (choice == 98) && pair.Ecg.HyperkalemiaFusion == (choice == 98),
                    "monitor and paper share exactly the selected compound contour");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                if (choice == 98)
                {
                    Require(reading.HeartRate.Status == WaveformMeasurementStatus.Uncountable && reading.HeartRate.MilliBeatsPerMinute is null &&
                        window.MonitorView.NumericTexts[0] == "-?-", "fusion shows actual uncountable detector state, not configured rate");
                    Require(EcgTemplateSummary.Describe(pair.Ecg).Contains("独立 QRS／T／QT 不适用", StringComparison.Ordinal),
                        "advanced fusion summary omits construction subdivisions");
                }
                else { Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid, "ordinary QRS measurement recovers after fusion"); }
                Require(reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "independent mechanical measurements remain available during fused ECG");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0); var cached = StylePreviewCatalog.Get(choice, 0, 0);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)),
                    "fusion card contains actual source samples");
                window.SelectPage(1); Capture(window, $"ui-preview-high-k-fusion-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "fusion paper retains complete twelve-lead record"); window.SelectPage(0);
            }
            window.Settings.EcgSelection = 98; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "fusion supports independent disabled ejection");
        }
        finally { window.Close(); }
    }

    private static void VerifyQuinidineProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 89, 90, 91, 92, 93, 94, 95, 96, 97, 90, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "quinidine preset atomically replaces previous morphology");
                if (choice == 89) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                int mode = choice == 0 ? 0 : (choice - 90) % 4 + 1;
                Require((int)pair.Physiology.Quinidine == mode && (int)pair.Ecg.Quinidine == mode &&
                    pair.Physiology.QuinidineNotchedP == (choice >= 94) && pair.Ecg.QuinidineNotchedP == (choice >= 94) &&
                    !pair.Physiology.DigitalisEffect && !pair.Ecg.DigitalisEffect,
                    "monitor/paper share mode and P notch and clear preceding digitalis effect");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - (choice == 0 ? 75000 : 60000)) < 1000 &&
                    reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "notched P, wide QRS and late U retain sample-derived rates");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0); var cached = StylePreviewCatalog.Get(choice, 0, 0);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)),
                    "quinidine preview is the selected acquired shape");
                if (choice != 0)
                {
                    string summary = EcgTemplateSummary.Describe(pair.Ecg);
                    Require(summary.Contains($"QT {(mode >= 3 ? 560 : 480)} ms", StringComparison.Ordinal) &&
                        summary.Contains($"QU {(mode >= 3 ? 790 : 710)} ms", StringComparison.Ordinal) &&
                        summary.Contains("（切迹）", StringComparison.Ordinal) == (choice >= 94), "summary distinguishes QT, QU and P notch");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-quinidine-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "quinidine paper retains full twelve-lead record"); window.SelectPage(0);
            }
            window.Settings.EcgSelection = 97; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "quinidine retains independent no-ejection control");
        }
        finally { window.Close(); }
    }

    private static void VerifyDigitalisProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 86, 87, 88, 89, 87, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "digitalis preset replaces previous morphology atomically");
                if (choice == 86) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.DigitalisEffect == (choice != 0) && pair.Ecg.DigitalisEffect == (choice != 0) &&
                    pair.Physiology.DigitalisShape == pair.Ecg.DigitalisShape &&
                    (int)pair.Ecg.DigitalisShape == (choice == 0 ? 0 : choice - 87) &&
                    pair.Physiology.Calcium == Monitor.Simulation.Physiology.CalciumIllustration.Reference,
                    "monitor and paper share the exact digitalis shape and clear previous calcium mode");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - (choice == 0 ? 75000 : 60000)) < 1000 &&
                    reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "joined QRS/ST-T preserves measured cardiac and perfusion rates");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0); var cached = StylePreviewCatalog.Get(choice, 0, 0);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)),
                    "digitalis card uses actual selected joined contour");
                if (choice != 0)
                {
                    string summary = EcgTemplateSummary.Describe(pair.Ecg);
                    Require(summary.Contains("QT 320 ms", StringComparison.Ordinal) && summary.Contains("T 时限不单独标注", StringComparison.Ordinal),
                        "digitalis summary omits construction T duration");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-digitalis-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "digitalis paper has complete twelve-lead record"); window.SelectPage(0);
            }
            window.Settings.EcgSelection = 87; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "digitalis permits independent disabled ejection");
        }
        finally { window.Close(); }
    }

    private static void VerifyCalciumProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 80, 82, 83, 84, 85, 86, 82, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "calcium preset atomically replaces previous electrolyte morphology");
                if (choice == 80) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                var mode = choice == 0 ? Monitor.Simulation.Physiology.CalciumIllustration.Reference : (Monitor.Simulation.Physiology.CalciumIllustration)(choice - 81);
                Require(pair.Physiology.Calcium == mode && pair.Ecg.Calcium == mode &&
                    !pair.Physiology.HypokalemiaTuFusion && !pair.Ecg.HypokalemiaTuFusion,
                    "monitor/paper share calcium mode and clear preceding T-U fusion");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - (choice == 0 ? 75000 : 60000)) < 1000 &&
                    reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "calcium templates retain sample-derived electrical and mechanical measurements");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)),
                    "calcium card uses actual selected acquired samples");
                if (choice != 0)
                {
                    int qt = choice == 82 ? 300 : choice == 84 ? 260 : 460;
                    Require(EcgTemplateSummary.Describe(pair.Ecg).Contains($"QT {qt} ms", StringComparison.Ordinal),
                        "advanced page reports resolved shortened/prolonged QT");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-calcium-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "calcium paper retains full twelve-lead snapshot");
                window.SelectPage(0);
            }
            window.Settings.EcgSelection = 84; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "calcium template retains independent no-ejection option");
        }
        finally { window.Close(); }
    }

    private static void VerifyHypokalemiaProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 77, 78, 79, 80, 81, 78, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "low-potassium preset replaces prior cardiac morphology atomically");
                if (choice == 77) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.HypokalemiaRepolarization == (choice >= 78) && pair.Ecg.HypokalemiaRepolarization == (choice >= 78) &&
                    pair.Physiology.HypokalemiaInvertedT == (choice == 79) && pair.Ecg.HypokalemiaInvertedT == (choice == 79) &&
                    pair.Physiology.HypokalemiaTuFusion == (choice == 80) && pair.Ecg.HypokalemiaTuFusion == (choice == 80) &&
                    pair.Physiology.HypokalemiaConduction == (choice == 81) && pair.Ecg.HypokalemiaConduction == (choice == 81) &&
                    !pair.Physiology.HyperkalemiaRepolarization && !pair.Ecg.HyperkalemiaRepolarization,
                    "monitor/paper flags match exactly and clear preceding high-potassium shape");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - (choice == 0 ? 75000 : 60000)) < 1000 &&
                    reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "low-potassium ECG and mechanical measurements remain sample-derived");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)),
                    "low-potassium card uses selected acquired ECG");
                if (choice >= 78)
                {
                    string summary = EcgTemplateSummary.Describe(pair.Ecg);
                    Require(summary.Contains("QU 650 ms", StringComparison.Ordinal) &&
                        (choice != 80 || summary.Contains("QT 不单独标注", StringComparison.Ordinal)), "T-U fusion does not claim measurable QT");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-low-potassium-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "low-potassium paper retains complete twelve-lead snapshot");
                window.SelectPage(0);
            }
            window.Settings.EcgSelection = 80; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "low-potassium template permits independent absent ejection");
        }
        finally { window.Close(); }
    }

    private static void VerifyHyperkalemiaProductStyle()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 75, 76, 77, 74, 75, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "high-T preset replaces incompatible rhythm atomically");
                if (choice is 21 or 74) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.HyperkalemiaRepolarization == (choice >= 75) &&
                    pair.Ecg.HyperkalemiaRepolarization == (choice >= 75), "monitor and paper share high-T selection and reset");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - (choice >= 76 ? 60000 : 75000)) < 1000,
                    $"high T is not counted as another heartbeat: {reading.HeartRate}");
                Require(reading.PulseRate.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    "repolarization preserves sampled mechanical measurements");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                long end = source.FrontierNs, start = end - StylePreviewCatalog.DurationNs(choice);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, start, end)) && cached.Abp.SequenceEqual(source.Samples(3, start, end)),
                    "high-T preview uses actual selected source samples");
                if (choice == 75)
                {
                    var normal = DesignPreviewWindow.CreateStylePreview(0, 0, 0);
                    Require(!cached.Ecg.SequenceEqual(normal.Samples(0, start, end)), "high-T card differs from sinus reference");
                    foreach (int channel in Enumerable.Range(1, 6))
                    { Require(source.Samples(channel, start, end).SequenceEqual(normal.Samples(channel, start, end)), "no invented non-ECG potassium effect"); }
                    Require(EcgTemplateSummary.Describe(pair.Ecg).Contains("QT 300 ms", StringComparison.Ordinal), "advanced summary uses high-T resolved timing");
                }
                if (choice >= 76)
                {
                    Require(pair.Physiology.ResolvePlan() == Monitor.Simulation.Physiology.HyperkalemiaConductionReference.CreatePlan(choice == 77),
                        "conduction template shares authored atrial/ventricular electrical and mechanical clocks");
                    string summary = EcgTemplateSummary.Describe(pair.Ecg);
                    Require(summary.Contains("QRS 140 ms", StringComparison.Ordinal) && summary.Contains("QT 440 ms", StringComparison.Ordinal) &&
                        (choice != 77 || summary.StartsWith("无 P 波；PR 不适用", StringComparison.Ordinal)),
                        "conduction summary reports resolved timing and absent-P semantics");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-high-t-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "high-T paper retains complete twelve-lead snapshot");
                window.SelectPage(0);
            }
            window.Settings.EcgSelection = 75; window.RestartSettings(); var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 3; window.RestartSettings();
            Require(!ReferenceEquals(live, window.Session), "independent disabled ejection remains supported with high T");
        }
        finally { window.Close(); }
    }

    private static void VerifyAdvancedTemplateDescriptions()
    {
        for (int choice = 0; choice < DesignPreviewSettings.EcgChoiceCount; choice++)
        {
            var config = DesignPreviewWindow.ResolveStyle(choice, 0, 0).Ecg;
            string summary = EcgTemplateSummary.Describe(config);
            Require(!string.IsNullOrWhiteSpace(summary) && !summary.Contains("QTc", StringComparison.Ordinal),
                "advanced authored intervals never label a construction value as QTc");
            if (choice is 6 or 7 or 8 or 9 or 37 or 38 or 39 or >= 66 and <= 71)
            { Require(summary.Contains("PR 不适用", StringComparison.Ordinal), "AF/flutter hides construction P/PR"); }
            if (choice is >= 10 and <= 12)
            { Require(summary.Contains("PR 逐搏延长", StringComparison.Ordinal), "Wenckebach does not show a fixed PR"); }
            if (choice is 18 or 19 or >= 26 and <= 30 or >= 32 and <= 35)
            { Require(summary.Contains("无固定 PR", StringComparison.Ordinal), "independent atrial and ventricular clocks have no fixed PR"); }
        }
        Require(EcgTemplateSummary.Describe(DesignPreviewWindow.ResolveStyle(19, 0, 0).Ecg).Contains("QT 480 ms", StringComparison.Ordinal),
            "ventricular escape reports resolved QT rather than its QTc input");
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            window.SelectPage(2);
            foreach (int choice in new[] { 72, 74, 73, 20, 0 })
            {
                window.Settings.Tabs.SelectedIndex = 1; window.Settings.EcgSelection = choice;
                window.RestartSettings();
                window.Settings.OpenAdvanced(0);
                window.Settings.SectionPages[6].SelectedSection = 3;
                Capture(window, $"ui-preview-advanced-{choice}.png");
                string?[] text = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Require(text.Any(t => t?.Contains(EcgTemplateSummary.Describe(DesignPreviewWindow.ResolveStyle(choice, 0, 0).Ecg), StringComparison.Ordinal) == true),
                    "applied parameter page renders the accepted waveform semantics");
                window.Settings.OpenAdvanced(3);
                Capture(window, $"ui-preview-advanced-ejection-{choice}.png");
                text = window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
                Require(text.Contains("当前无有效射血，不提供射血强度编辑。") == (choice != 0),
                    "advanced page derives absent ejection from rhythm as well as ejection selection");
            }
            var live = window.Session;
            window.Settings.Tabs.SelectedIndex = 1; window.Settings.EcgSelection = 74; window.Settings.EjectionSelection = 1;
            window.Settings.OpenAdvanced(3); Capture(window, "ui-preview-advanced-incompatible.png");
            Require(window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("当前组合不兼容：", StringComparison.Ordinal) == true) &&
                ReferenceEquals(live, window.Session), "browsing incompatible advanced settings explains conflict without mutating live session");
        }
        finally { window.Close(); }
    }

    private static void VerifyStandstillProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 72, 73, 74, 72, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "standstill/PEA replaces previous rhythm atomically");
                if (choice == 21) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                bool absent = choice == 72, noPulse = choice is 72 or 73 or 74;
                Require(pair.Physiology.CardiacActivity == pair.Ecg.CardiacActivity &&
                    (pair.Ecg.CardiacActivity == Monitor.Simulation.Physiology.CardiacActivity.Absent) == absent &&
                    pair.Physiology.VentricularMechanicalEnabled == (choice != 73),
                    "standstill removes electrical activity while PEA retains organized ECG");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == (absent ? WaveformMeasurementStatus.Stale : choice == 74 ? WaveformMeasurementStatus.Uncountable : WaveformMeasurementStatus.Valid),
                    $"sampled flat ECG is stale, P-only is uncountable and PEA retains HR: {choice}/{reading.HeartRate}");
                Require(noPulse ? reading.PulseRate.Status == WaveformMeasurementStatus.Stale : reading.PulseRate.Status == WaveformMeasurementStatus.Valid,
                    "sampled pulse rate follows mechanical activity independently of ECG");
                long end = window.Session.FrontierNs, start = end - 3_000_000_000;
                foreach (int channel in new[] { 2, 3, 5 })
                {
                    double[] values = window.Session.Samples(channel, start, end).Select(p => p.Value).ToArray();
                    Require(values.Zip(values.Skip(1)).Any(p => p.Second > p.First) == !noPulse,
                        $"standstill/PEA retain runoff without new Pleth/ABP/PA upstrokes: {choice}/{channel}");
                }
                foreach (int channel in new[] { 1, 4 })
                {
                    Require(window.Session.Samples(channel, start, end).Select(p => p.Value).Distinct().Count() > 1,
                        "respiration and CO2 remain independently configured");
                }
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                long duration = StylePreviewCatalog.DurationNs(choice);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                    cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                    "standstill/PEA cards retain actual electrical and mechanical samples");
                Require(cached.Ecg.All(p => p.Value == 0) == absent, "only standstill has flat ECG");
                if (noPulse)
                {
                    var live = window.Session;
                    window.Settings.EjectionSelection = 1; window.RestartSettings();
                    Require(ReferenceEquals(live, window.Session), "PVC-only ejection rejects without replacing standstill/PEA");
                    window.Settings.EjectionSelection = 0;
                }
                window.SelectPage(1); Capture(window, $"ui-preview-standstill-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "standstill/PEA paper retains full timestamped twelve-lead record");
                window.SelectPage(0);
            }
        }
        finally { window.Close(); }
    }

    private static void VerifyAfVariantProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 66, 67, 68, 69, 70, 71, 6 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "AF variants replace VF and clear preceding aberrancy/deficit state");
                if (choice == 21) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                bool aberrant = choice is 66 or 67 or 70 or 71;
                bool deficit = choice >= 68;
                Require(pair.Physiology.IllustrateAfAberrancy == aberrant && pair.Ecg.IllustrateAfAberrancy == aberrant &&
                    pair.Physiology.IllustrateAfSystemicPulseDeficit == deficit &&
                    pair.Physiology.ConductionPattern == pair.Ecg.ConductionPattern,
                    "monitor and paper share AF electrical variant while deficit is mechanical only");
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                Require(window.Session.Measurements!.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    window.Session.Measurements.AbpMean.Status == WaveformMeasurementStatus.Valid,
                    $"AF variants retain sample-derived HR and pressure choice={choice}");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                long duration = StylePreviewCatalog.DurationNs(choice);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                    cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                    "AF preview uses actual selected electrical and mechanical samples");
                if (choice >= 66)
                {
                    Require(duration == 12_000_000_000, "AF variant preview includes the authored early long-short examples");
                    var reference = DesignPreviewWindow.CreateStylePreview(choice % 2 == 0 ? 6 : 7, 0, 0);
                    while (reference.SimulationTimeNs < source.SimulationTimeNs) { reference.Advance(50_000_000); }
                    long start = source.FrontierNs - duration, end = source.FrontierNs;
                    Require(cached.Ecg.SequenceEqual(reference.Samples(0, start, end)) == !aberrant,
                        $"aberrancy changes ECG while mechanical deficit does not: choice={choice}, samples={cached.Ecg.Length}/{reference.Samples(0, start, end).Count()}, frontier={source.FrontierNs}/{reference.FrontierNs}");
                    Require(cached.Abp.SequenceEqual(reference.Samples(3, start, end)) == !deficit &&
                        source.Samples(2, start, end).SequenceEqual(reference.Samples(2, start, end)) == !deficit,
                        "systemic deficit changes ABP and Pleth independently of aberrancy");
                    Require(source.Samples(5, start, end).SequenceEqual(reference.Samples(5, start, end)),
                        "systemic deficit does not remove authored pulmonary ejection");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-af-variant-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "AF variant has complete twelve-lead snapshot");
                window.SelectPage(0);
            }
            var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 1; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "PVC-only weak ejection rejects for AF");
        }
        finally { window.Close(); }
    }
    private static void VerifyPreexcitationProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 59, 60, 61, 62, 63, 64, 65, 59, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "preexcitation presets replace VF and reset preceding PR/delta state");
                if (choice == 21) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.Wpw == (choice is >= 59 and <= 62) && pair.Ecg.Wpw == pair.Physiology.Wpw &&
                    pair.Physiology.WpwNegativeV1 == (choice is 60 or 62) && pair.Ecg.WpwNegativeV1 == pair.Physiology.WpwNegativeV1 &&
                    pair.Physiology.WpwSmallerDelta == (choice is 61 or 62) && pair.Ecg.WpwSmallerDelta == pair.Physiology.WpwSmallerDelta &&
                    pair.Physiology.ShortPr == (choice == 63) && pair.Ecg.ShortPr == pair.Physiology.ShortPr &&
                    pair.Physiology.NormalPrDelta == (choice is 64 or 65) && pair.Ecg.NormalPrDelta == pair.Physiology.NormalPrDelta &&
                    pair.Physiology.ProlongedPrDelta == (choice == 65) && pair.Ecg.ProlongedPrDelta == pair.Physiology.ProlongedPrDelta,
                    "monitor/paper retain exactly the selected preexcitation variant");
                Require(pair.Ecg.PrIntervalMilliseconds == (choice == 65 ? 240 : choice is >= 59 and <= 63 ? 100 : 160) &&
                    pair.Ecg.QrsDurationMilliseconds == (choice is 61 or 62 ? 110 : choice is 59 or 60 or 64 or 65 ? 140 : 80),
                    "paper uses the authored PR and QRS for each delta variant");
                for (int i = 0; i < 320; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - 75000) <= 5000,
                    $"preexcitation measured HR choice={choice}: {reading.HeartRate}");
                Require(reading.AbpMean.Status == WaveformMeasurementStatus.Valid, "preexcitation existing ejection reaches pressure measurement");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                long duration = StylePreviewCatalog.DurationNs(choice);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                    cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                    "preexcitation preview matches selected source and mechanical timing");
                window.SelectPage(1); Capture(window, $"ui-preview-preexcitation-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "preexcitation has full twelve-lead snapshot");
                window.SelectPage(0);
            }
            window.Settings.EcgSelection = 59; window.RestartSettings();
            var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 2; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
        }
        finally { window.Close(); }
    }
    private static void VerifyBundleBlockProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 53, 54, 55, 56, 57, 58, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "bundle presets replace VF and clear preceding morphology");
                if (choice == 21) { continue; }
                var mode = choice == 0 ? Monitor.Simulation.Physiology.EcgBundleBlockIllustration.Reference
                    : (Monitor.Simulation.Physiology.EcgBundleBlockIllustration)(choice - 52);
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology == BundleBlockPreset.Physiology(mode) && pair.Ecg == BundleBlockPreset.Ecg(mode),
                    "monitor and paper use the same established bundle/fascicular preset");
                for (int i = 0; i < 320; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - 75000) <= 5000,
                    $"bundle morphology retains measured conducted rhythm choice={choice}: {reading.HeartRate}");
                Require(reading.AbpMean.Status == WaveformMeasurementStatus.Valid, "bundle illustration retains sampled pressure");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                long duration = StylePreviewCatalog.DurationNs(choice);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                    cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                    "bundle card shows actual selected source samples");
                if (choice != 0)
                {
                    var reference = StylePreviewCatalog.Get(0, 0, 0);
                    Require(!cached.Ecg.SequenceEqual(reference.Ecg) && cached.Abp.SequenceEqual(reference.Abp),
                        "electrical conduction morphology changes without inventing a new pump model");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-bundle-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "bundle paper contains full twelve-lead snapshot");
                window.SelectPage(0);
            }
            window.Settings.EcgSelection = 53; window.RestartSettings();
            var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 1; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "PVC-only weak ejection rejects for isolated conduction morphology");
        }
        finally { window.Close(); }
    }
    private static void VerifyPvcGroupProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 44, 45, 46, 47, 48, 49, 50, 51, 52, 2 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "PVC groups replace VF and previous grouped timing");
                if (choice == 21) { continue; }
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                var expected = choice switch
                {
                    44 => Monitor.Simulation.Physiology.AvConductionPattern.VentricularBigeminyIllustration,
                    45 => Monitor.Simulation.Physiology.AvConductionPattern.VentricularTrigeminyIllustration,
                    46 => Monitor.Simulation.Physiology.AvConductionPattern.VentricularCoupletIllustration,
                    47 => Monitor.Simulation.Physiology.AvConductionPattern.InterpolatedPvcIllustration,
                    48 => Monitor.Simulation.Physiology.AvConductionPattern.PolymorphicPvcIllustration,
                    49 => Monitor.Simulation.Physiology.AvConductionPattern.MultifocalPvcIllustration,
                    50 => Monitor.Simulation.Physiology.AvConductionPattern.PolymorphicVentricularCoupletIllustration,
                    51 => Monitor.Simulation.Physiology.AvConductionPattern.RonTLongQtPvcIllustration,
                    52 => Monitor.Simulation.Physiology.AvConductionPattern.ShortCoupledRonTPvcIllustration,
                    _ => Monitor.Simulation.Physiology.AvConductionPattern.PrematureVentricularIllustration
                };
                Require(pair.Physiology.ConductionPattern == expected && pair.Ecg.ConductionPattern == expected,
                    "monitor and paper use the selected PVC schedule");
                if (choice == 47) { Require(pair.Ecg.HeartRateBpm == 60, "interpolated example retains its existing sinus grid"); }
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid && reading.HeartRate.MilliBeatsPerMinute is > 40000 and < 110000,
                    $"PVC grouped rate is sample-derived choice={choice}: {reading.HeartRate}");
                Require(reading.AbpMean.Status == WaveformMeasurementStatus.Valid, "PVC weighted ejection reaches pressure measurement");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                long duration = StylePreviewCatalog.DurationNs(choice);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                    cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                    "PVC cached electrical and weighted mechanical samples match the selected live source");
                if (choice >= 44)
                {
                    long group = choice switch { 44 => 1_600_000_000, 45 => 2_400_000_000, 46 or 50 => 4_000_000_000, 48 or 49 => 6_400_000_000, 51 or 52 => 3_200_000_000, _ => 3_000_000_000 };
                    Require(duration == 2 * group, "PVC preview covers two full groups including recovery interval");
                    var plan = pair.Physiology.ResolvePlan();
                    int beats = Monitor.Simulation.Physiology.RegularPhysiologyTimeline.Start(plan).AdvanceBefore(group, 100)
                        .Count(e => e.Kind == Monitor.Simulation.Physiology.PhysiologyCycleEventKind.VentricularMechanical);
                    Require(beats == (choice == 44 ? 2 : choice == 45 ? 3 : choice is 46 or 50 ? 5 : choice is 48 or 49 ? 8 : 4),
                        "PVC grouping preserves authored mechanical event count");
                }
                if (choice >= 48)
                {
                    int baselineChoice = choice == 50 ? 46 : 2;
                    var baseline = DesignPreviewWindow.CreateStylePreview(baselineChoice, 0, 0);
                    while (baseline.SimulationTimeNs < source.SimulationTimeNs) { baseline.Advance(50_000_000); }
                    Require(!cached.Ecg.SequenceEqual(baseline.Samples(0, source.FrontierNs - duration, source.FrontierNs)),
                        "diverse and R-on-T cards contain their selected morphology/timing");
                    if (choice is 48 or 50 or 51)
                    {
                        Require(cached.Abp.SequenceEqual(baseline.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                        "electrical-only variant retains the existing matched mechanical schedule");
                    }
                    if (choice is 49 or 52)
                    {
                        Require(!cached.Abp.SequenceEqual(baseline.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                        "changed coupling or absent short-coupled ejection reaches the pressure preview");
                    }
                    if (choice == 52)
                    {
                        Require(Monitor.Simulation.Physiology.PrematureBeatPerfusion.GainPermille(expected, 3) == 0,
                        "short-coupled R-on-T keeps its authored non-ejecting premature beat");
                    }
                }
                window.SelectPage(1); Capture(window, $"ui-preview-pvc-group-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "PVC paper contains complete twelve-lead capture");
                window.SelectPage(0);
            }
            var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false; window.Settings.EjectionSelection = 2; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
        }
        finally { window.Close(); }
    }
    private static void VerifyPrematureSupraventricularProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 40, 41, 4, 42, 43, 5 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "PAC/PJC presets replace VF and preceding variant atomically");
                if (choice == 21) { continue; }
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    reading.HeartRate.MilliBeatsPerMinute is > 40000 and < 110000,
                    $"PAC/PJC uses sample-derived ventricular rate choice={choice}: {reading.HeartRate}");
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                var expected = choice switch
                {
                    40 => Monitor.Simulation.Physiology.AvConductionPattern.BlockedPrematureAtrialIllustration,
                    41 => Monitor.Simulation.Physiology.AvConductionPattern.AberrantPrematureAtrialIllustration,
                    42 => Monitor.Simulation.Physiology.AvConductionPattern.PrematureJunctionalAfterQrsIllustration,
                    43 => Monitor.Simulation.Physiology.AvConductionPattern.PrematureJunctionalOverlappingIllustration,
                    4 => Monitor.Simulation.Physiology.AvConductionPattern.PrematureAtrialIllustration,
                    _ => Monitor.Simulation.Physiology.AvConductionPattern.PrematureJunctionalIllustration
                };
                Require(pair.Physiology.ConductionPattern == expected && pair.Ecg.ConductionPattern == expected,
                    "paper and monitor retain exactly the selected P-prime/conduction variant");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                long duration = StylePreviewCatalog.DurationNs(choice);
                Require(duration == (choice is 4 or 40 or 41 ? 6_200_000_000 : 6_400_000_000),
                    "PAC/PJC previews show two complete early-beat/pause groups");
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                    cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                    "PAC/PJC preview contains actual selected ECG and weighted ejection");
                if (choice >= 40)
                {
                    var reference = StylePreviewCatalog.Get(choice <= 41 ? 4 : 5, 0, 0);
                    Require(!cached.Ecg.SequenceEqual(reference.Ecg), "variant is visibly different from the base electrical morphology");
                    Require(cached.Abp.SequenceEqual(reference.Abp) == (choice != 40),
                        "blocked PAC loses an ejection; aberrancy and retrograde P position preserve existing ventricular perfusion");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-premature-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "PAC/PJC paper has complete twelve-lead capture");
                window.SelectPage(0);
            }
            var live = window.Session;
            window.Settings.EjectionSelection = 1; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "PVC-only weak-ejection option cannot replace PJC session");
            window.Settings.EjectionSelection = 0; window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
        }
        finally { window.Close(); }
    }
    private static void VerifyAutomaticRhythmProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 31, 32, 33, 34, 35, 36, 33 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "automatic rhythm clears prior disorganization and variant state");
                if (choice == 21) { continue; }
                for (int i = 0; i < 400; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                int expected = choice is 31 or 32 ? 100000 : choice == 36 ? 50000 : 80000;
                Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                    Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - expected) <= 5000,
                    $"automatic rhythm acquired rate choice={choice}: {reading.HeartRate}");
                Require(reading.AbpMean.Status == WaveformMeasurementStatus.Valid &&
                    reading.AbpMean.MeanCentiMmHg is > 5000 and < 18000,
                    "automatic rhythm retains sampled perfusion");
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.Aar == (choice == 31) && pair.Ecg.Aar == pair.Physiology.Aar &&
                    pair.Physiology.Ajr == (choice == 32) && pair.Ecg.Ajr == pair.Physiology.Ajr &&
                    pair.Physiology.Aivr == (choice is >= 33 and <= 35) && pair.Ecg.Aivr == pair.Physiology.Aivr &&
                    pair.Physiology.AivrFusion == (choice == 34) && pair.Ecg.AivrFusion == pair.Physiology.AivrFusion &&
                    pair.Physiology.AivrCapture == (choice == 35) && pair.Ecg.AivrCapture == pair.Physiology.AivrCapture &&
                    pair.Physiology.AtrialEscape == (choice == 36) && pair.Ecg.AtrialEscape == pair.Physiology.AtrialEscape,
                    "monitor and paper resolve exactly the selected automatic rhythm");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                long duration = StylePreviewCatalog.DurationNs(choice);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                    cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                    "automatic rhythm cached samples match live electrical and perfusion source");
                if (choice is 34 or 35)
                {
                    Require(duration == 12_000_000_000, "AIVR preview includes the full sixteen-beat group");
                    var regular = DesignPreviewWindow.CreateStylePreview(33, 0, 0);
                    while (regular.SimulationTimeNs < source.SimulationTimeNs) { regular.Advance(50_000_000); }
                    Require(!cached.Ecg.SequenceEqual(regular.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                        cached.Abp.SequenceEqual(regular.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                        "fusion/capture preview differs electrically while retaining existing fixed perfusion");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-automatic-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "automatic rhythm paper has complete continuous acquisition");
                window.SelectPage(0);
            }
            var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
            window.Settings.CardiacRateEnabled.IsChecked = false;
            window.Settings.EjectionSelection = 1; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "PVC-only ejection rejects atomically for automatic rhythms");
        }
        finally { window.Close(); }
    }
    private static void VerifyVtProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 26, 27, 28, 29, 30, 26 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "VT preset can replace VF and clear fusion/capture state");
                if (choice == 21) { continue; }
                for (int i = 0; i < 320; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                if (choice == 30)
                {
                    // User accepts morphology-dependent single-lead fluctuations;
                    // never replace measured HR with the authored ventricular rate.
                    var independent = new LocalMonitorPreviewSession(DesignPreviewWindow.ResolveStyle(choice, 0, 0).Physiology,
                        MonitorDisplayConfiguration.Default(), enableMeasurements: true);
                    independent.DiscardStartup();
                    while (independent.SimulationTimeNs < window.Session.SimulationTimeNs) { independent.Advance(50_000_000); }
                    Require(reading.HeartRate.Status != WaveformMeasurementStatus.Uncountable &&
                        reading.HeartRate == independent.Measurements!.HeartRate,
                        "twisting product retains sample-derived rate and quality without forcing preset HR");
                }
                else
                {
                    Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid &&
                        Math.Abs(reading.HeartRate.MilliBeatsPerMinute!.Value - 160000) <= 5000,
                        $"VT product rate choice={choice}: {reading.HeartRate}");
                }
                Require(reading.AbpMean.Status == WaveformMeasurementStatus.Valid && reading.AbpMean.MeanCentiMmHg is > 6000 and < 16000,
                    "VT product uses filling-limited pressure input");
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.Vt && pair.Ecg.Vt && pair.Physiology.VtFusion == pair.Ecg.VtFusion &&
                    pair.Physiology.VtCapture == pair.Ecg.VtCapture && pair.Physiology.VtBidirectional == pair.Ecg.VtBidirectional &&
                    pair.Physiology.VtTwisting == pair.Ecg.VtTwisting, "monitor and paper share VT variant");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                long duration = StylePreviewCatalog.DurationNs(choice);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - duration, source.FrontierNs)) &&
                    cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                    "VT preview samples match selected electrical and perfusion source");
                if (choice > 26)
                {
                    Require(duration == (choice <= 28 ? 12_000_000_000 : 3_000_000_000), "VT previews cover the full authored morphology group");
                    var regular = DesignPreviewWindow.CreateStylePreview(26, 0, 0);
                    while (regular.SimulationTimeNs < source.SimulationTimeNs) { regular.Advance(50_000_000); }
                    Require(!cached.Ecg.SequenceEqual(regular.Samples(0, source.FrontierNs - duration, source.FrontierNs)),
                        "long preview actually contains the selected variant");
                    if (choice >= 29)
                    {
                        Require(cached.Abp.SequenceEqual(regular.Samples(3, source.FrontierNs - duration, source.FrontierNs)),
                            "rotating electrical vector does not invert or modulate the fixed perfusion preset");
                    }
                }
                window.SelectPage(1); Capture(window, $"ui-preview-vt-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "VT paper capture is complete");
                window.SelectPage(0);
            }
            var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
        }
        finally { window.Close(); }
    }
    private static void VerifySvtProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 21, 23, 24, 25, 23 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "SVT presets replace prior disorganized and bundle state");
                if (choice == 21) { continue; }
                for (int i = 0; i < 240; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var rate = window.Session.Measurements!.HeartRate;
                Require(rate.Status == WaveformMeasurementStatus.Valid && Math.Abs(rate.MilliBeatsPerMinute!.Value - 200000) <= 1000,
                    "narrow and broad SVT produce acquired 200 bpm");
                var pressure = window.Session.Measurements.AbpMean;
                Require(pressure.Status == WaveformMeasurementStatus.Valid && pressure.MeanCentiMmHg is > 6000 and < 13000 &&
                    pressure.Pulse is { Status: WaveformMeasurementStatus.Valid, SystolicCentiMmHg: < 13000 },
                    "SVT pressure is measured from filling-limited samples without near300mmHg accumulation");
                var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                Require(pair.Physiology.Svt && pair.Ecg.Svt && pair.Physiology.SvtRbbb == pair.Ecg.SvtRbbb &&
                    pair.Physiology.SvtLbbb == pair.Ecg.SvtLbbb, "SVT monitor and paper use identical bundle variant");
                var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                var cached = StylePreviewCatalog.Get(choice, 0, 0);
                Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - 3_000_000_000, source.FrontierNs)) &&
                    cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - 3_000_000_000, source.FrontierNs)),
                    "SVT cached electrical and perfusion samples match source");
                window.SelectPage(1); Capture(window, $"ui-preview-svt-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "SVT twelve-lead capture is complete");
                window.SelectPage(0);
            }
            var live = window.Session;
            window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
        }
        finally { window.Close(); }
    }
    private static void VerifyDisorganizedProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            foreach (int choice in new[] { 20, 10, 21, 18, 22, 0 })
            {
                var previous = window.Session;
                window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "disorganized and conducted presets can replace each other");
                for (int i = 0; i < 240; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                var reading = window.Session.Measurements!;
                if (choice >= 20)
                {
                    Require(reading.HeartRate.Status == WaveformMeasurementStatus.Uncountable &&
                        reading.HeartRate.MilliBeatsPerMinute is null && window.Session.DetectedBeats.Count == 0,
                        "disorganized ECG is uncountable and emits no heartbeat cues");
                    var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
                    Require(!pair.Physiology.VentricularMechanicalEnabled && pair.Physiology.ConductionPattern == pair.Ecg.ConductionPattern,
                        "monitor and paper share disorganized pattern without effective ejection");
                    var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
                    var cached = StylePreviewCatalog.Get(choice, 0, 0);
                    Require(cached.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - 3_000_000_000, source.FrontierNs)) &&
                        cached.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - 3_000_000_000, source.FrontierNs)),
                        "disorganized preview matches actual electrical and mechanical samples");
                    window.SelectPage(1); Capture(window, $"ui-preview-disorganized-{choice}-paper.png");
                    Require(window.CurrentPaper!.BlockCount == 55, "disorganized twelve-lead capture is complete");
                    window.SelectPage(0);
                }
                else { Require(reading.HeartRate.Status == WaveformMeasurementStatus.Valid, "organized rate recovers after disorganized preset"); }
            }
        }
        finally { window.Close(); }
    }
    private static void VerifyBlockProductStyles()
    {
        var window = CreateTemplateWindow(); window.Show();
        try
        {
            // Alternate independent escape clocks and conducted rhythms to catch stale state.
            foreach (int choice in new[] { 19, 10, 18, 11, 12, 13, 14, 15, 16, 17, 10 })
            {
                var previous = window.Session; window.Settings.EcgSelection = choice; window.RestartSettings();
                Require(!ReferenceEquals(previous, window.Session), "block presets reset incompatible prior rhythm state atomically");
                for (int i = 0; i < 240; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
                Require(window.Session.Measurements!.HeartRate.Status == WaveformMeasurementStatus.Valid, "block ECG produces measured ventricular rate");
                if (choice >= 18)
                {
                    int expected = choice == 18 ? 50000 : 30000;
                    Require(Math.Abs(window.Session.Measurements.HeartRate.MilliBeatsPerMinute!.Value - expected) <= 1000, "complete block follows independent escape rate");
                }
                window.SelectPage(1); Capture(window, $"ui-preview-block-{choice}-paper.png");
                Require(window.CurrentPaper!.BlockCount == 55, "block twelve-lead capture is complete");
                window.SelectPage(0);
            }
            var live = window.Session; window.Settings.CardiacRateEnabled.IsChecked = true; window.Settings.HeartRate.Value = null; window.RestartSettings();
            Require(ReferenceEquals(live, window.Session), "incomplete rate draft is rejected without replacing the active waveform");
        }
        finally { window.Close(); }
    }
    private static void VerifyPrebuiltStyles()
    {
        int count = 0;
        for (int ecg = 0; ecg < DesignPreviewSettings.EcgChoiceCount; ecg++)
            for (int resp = 0; resp < 4; resp++)
                for (int ejection = 0; ejection < 4; ejection++)
                {
                    bool valid = true;
                    try { DesignPreviewWindow.ResolveStyle(ecg, resp, ejection); } catch (ArgumentException) { valid = false; }
                    if (valid)
                    {
                        var data = StylePreviewCatalog.Get(ecg, resp, ejection); count++;
                        Require(data.Ecg.Length == StylePreviewCatalog.DurationNs(ecg) / 4_000_000 && data.Abp.Length == StylePreviewCatalog.DurationNs(ecg) / 8_000_000 && ReferenceEquals(data, StylePreviewCatalog.Get(ecg, resp, ejection)),
                            "every valid combination loads shared prebuilt samples without simulation");
                    }
                    else
                    {
                        bool rejected = false;
                        try { StylePreviewCatalog.Get(ecg, resp, ejection); } catch (ArgumentException) { rejected = true; }
                        Require(rejected, "invalid combinations remain disabled in prebuilt catalog");
                    }
                }
        Require(count == StylePreviewCatalog.CombinationCount, "catalog covers all current compatible combinations");
        var reference = DesignPreviewWindow.CreateStylePreview(2, 1, 1);
        var cached = StylePreviewCatalog.Get(2, 1, 1);
        Require(cached.Abp.SequenceEqual(reference.Samples(3, reference.FrontierNs - 3_000_000_000, reference.FrontierNs)) &&
            cached.Ecg.SequenceEqual(reference.Samples(0, reference.FrontierNs - 3_000_000_000, reference.FrontierNs)), "build-time samples match the actual selected physiology");
        for (int choice = 6; choice < 10; choice++)
        {
            var atrial = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
            var asset = StylePreviewCatalog.Get(choice, 0, 0);
            Require(asset.Ecg.SequenceEqual(atrial.Samples(0, atrial.FrontierNs - 3_000_000_000, atrial.FrontierNs)) &&
                asset.Abp.SequenceEqual(atrial.Samples(3, atrial.FrontierNs - 3_000_000_000, atrial.FrontierNs)), "atrial rhythm cached ECG and pressure match live authoring");
            var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
            Require(pair.Physiology.ConductionPattern == pair.Ecg.ConductionPattern && pair.Physiology.VentricularConductionRatio == pair.Ecg.VentricularConductionRatio,
                "monitor and twelve-lead share atrial rhythm and conduction ratio");
        }
        for (int choice = 10; choice < 20; choice++)
        {
            var source = DesignPreviewWindow.CreateStylePreview(choice, 0, 0);
            var cachedBlock = StylePreviewCatalog.Get(choice, 0, 0);
            Require(cachedBlock.Ecg.SequenceEqual(source.Samples(0, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)) &&
                cachedBlock.Abp.SequenceEqual(source.Samples(3, source.FrontierNs - StylePreviewCatalog.DurationNs(choice), source.FrontierNs)), "block previews match actual ECG and ejection waveforms");
            var pair = DesignPreviewWindow.ResolveStyle(choice, 0, 0);
            Require(pair.Physiology.ConductionPattern == pair.Ecg.ConductionPattern && pair.Physiology.ConductedBeatsPerGroup == pair.Ecg.ConductedBeatsPerGroup &&
                pair.Physiology.IndependentVentricularPeriodMilliseconds == pair.Ecg.IndependentVentricularPeriodMilliseconds,
                "monitor and paper share block schedule and escape clock");
        }
        Require(StylePreviewCatalog.Respiration(1).SequenceEqual(DesignPreviewWindow.CreateRespirationPreview(1)), "long respiration asset preserves full authored cycle");
    }
    private static void VerifyAdditionalLimits(LiveMeasurementSnapshot snapshot)
    {
        var settings = new MonitorAlertSettings();
        var extra = settings.AdditionalLimits;
        Require(!settings.Notices(snapshot).Any(), "all real limits remain opt-in");
        for (int i = 0; i < MeasuredLimitNotice.Descriptors.Count; i++)
        {
            var d = MeasuredLimitNotice.Descriptors[i];
            var editor = extra.Editors[d.Numeric];
            Require(editor.Parent is not null && editor.CriticalLow.Value == (decimal)d.TeachingDefaults.CriticalLow! / d.Divisor,
                "each selected editor exposes correctly scaled units");
            editor.Enabled.IsChecked = true;
            editor.CriticalLow.Value = d.Minimum < 0 ? -10 : 0;
            editor.WarningLow.Value = d.Minimum < 0 ? -5 : .25m;
            editor.WarningHigh.Value = .5m; editor.CriticalHigh.Value = 1;
        }
        Require(extra.Editors[MonitorNumeric.RespirationRate].CriticalHigh.Value == 1, "switching editor retains prior limits");
        var notices = settings.Notices(snapshot).ToArray();
        Require(notices.Length == 4 && notices.All(n => n.Numeric is not (MonitorNumeric.AbpMean or MonitorNumeric.PaMean or MonitorNumeric.CvpMean)),
            "pressure confirmation does not delay the other measured limit channels");
        long start = snapshot.SampleTimeNs;
        for (long elapsed = 200_000_000; elapsed <= PressureLimitNotice.HighConfirmationNs; elapsed += 200_000_000)
        {
            snapshot = snapshot with { SampleTimeNs = start + elapsed };
            notices = settings.Notices(snapshot).ToArray();
            if (elapsed < PressureLimitNotice.HighConfirmationNs)
            { Require(notices.Length == 4, "pressure remains pending before its confirmation time"); }
        }
        Require(notices.Length == 7 && notices.All(n => n.Level == MonitorNoticeLevel.Critical) && notices.Select(n => n.Id).Distinct().Count() == 7,
            "all enabled sampled measurements coexist with distinct IDs");
        var view = new LiveMonitorView(new LiveMonitorTrace(new LocalMonitorPreviewSession(
            PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default(MonitorSkin.SevenRows))))
        { AdditionalNotices = settings.Notices };
        view.RefreshReadings(snapshot); view.RefreshNumericHighlights(0);
        var co2Rate = view.NumericBlocks.Single(b => b.IsVisible && AutomationProperties.GetName(b) == "RR · CO₂，次/分");
        Require((LiveMonitorView.NumericBackground(co2Rate) as Avalonia.Media.ISolidColorBrush)?.Color == Avalonia.Media.Color.Parse("#ffb51f2c") &&
            view.HighestNotice == MonitorNoticeLevel.Critical, "secondary CO2 rate binds flashing and sound severity");
        var invalid = snapshot with { Capnography = new(new(WaveformMeasurementStatus.PoorSignal, null, null), new(WaveformMeasurementStatus.PoorSignal, null, null)) };
        view.RefreshReadings(invalid); view.RefreshNumericHighlights(0);
        Require((LiveMonitorView.NumericBackground(co2Rate) as Avalonia.Media.ISolidColorBrush)?.Color == Avalonia.Media.Colors.Transparent &&
            view.ActiveNotices.Any(n => n.Numeric == MonitorNumeric.RespirationRate && n.Level == MonitorNoticeLevel.Critical),
            "CO2 failure clears its highlight while independent RESP condition stays");
        foreach (var editor in extra.Editors.Values) { editor.Enabled.IsChecked = false; }
        Require(!settings.Notices(snapshot).Any(), "disabling limits clears conditions without editing samples");
        extra.Editors[MonitorNumeric.AbpMean].Enabled.IsChecked = true;
        Require(!settings.Notices(snapshot).Any(), "reenabling pressure cannot reuse the old alarm");
    }
    private static void VerifyNumericAlarmHighlights(LiveMeasurementSnapshot snapshot)
    {
        var view = new LiveMonitorView(new LiveMonitorTrace(new LocalMonitorPreviewSession(PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default())));
        bool noticeColor = true; view.NoticeColorEnabled = () => noticeColor;
        MonitorNotice[] notices = [new("hr", MonitorNoticeLevel.Warning, "ECG HR 高") { Numeric = MonitorNumeric.HeartRate },
            new("pr", MonitorNoticeLevel.Notice, "PR 测试") { Numeric = MonitorNumeric.PulseRate }];
        view.AdditionalNotices = _ => notices;
        Avalonia.Media.Color Color(Avalonia.Media.IBrush? brush) => (brush as Avalonia.Media.ISolidColorBrush)?.Color ?? default;
        view.RefreshReadings(snapshot); view.RefreshNumericHighlights(0);
        var hr = view.NumericBlocks[0]; var spo2 = view.NumericBlocks[2]; var pr = view.NumericBlocks[3];
        Require(Color(LiveMonitorView.NumericBackground(hr)) == Avalonia.Media.Color.Parse("#fff2c94c") && Color(hr.Foreground) == Avalonia.Media.Color.Parse("#ff000000"), "warning numeric uses yellow backing with black high-contrast text");
        Require(Color(LiveMonitorView.NumericBackground(pr)) == Avalonia.Media.Color.Parse("#ff145aa3") && Color(pr.Foreground) == Avalonia.Media.Color.Parse("#ffffffff") && Color(LiveMonitorView.NumericBackground(spo2)) == Avalonia.Media.Colors.Transparent,
            "notice highlights its own secondary numeric, not unrelated SpO2 or banner-selected HR");
        Require(view.NoticeSurface.CornerRadius == ((Border)hr.Parent!).CornerRadius && view.NoticeSurface.CornerRadius == new CornerRadius(0),
            "banner and numeric use matching square highlight surfaces");
        Require(Color(view.NoticeSurface.Background) == Avalonia.Media.Color.Parse("#fff2c94c"), "warning banner lights with numeric phase");
        view.RefreshNumericHighlights(500_000_000);
        Require(Color(view.NoticeSurface.Background) == Avalonia.Media.Colors.Transparent && !string.IsNullOrEmpty(view.Notice.Text), "banner off phase keeps its text readable");
        Require(Color(LiveMonitorView.NumericBackground(hr)) == Avalonia.Media.Colors.Transparent && Color(hr.Foreground) == Avalonia.Media.Color.Parse(LiveMonitorTrace.Colors[0]), "half-cycle restores normal channel color");
        view.RefreshNumericHighlights(1_000_000_000);
        Require(Color(LiveMonitorView.NumericBackground(hr)) == Avalonia.Media.Color.Parse("#fff2c94c"), "one-second cycle repeats");
        noticeColor = false; view.RefreshNumericHighlights(1_000_000_000);
        Require(Color(LiveMonitorView.NumericBackground(pr)) == Avalonia.Media.Colors.Transparent && Color(LiveMonitorView.NumericBackground(hr)) == Avalonia.Media.Color.Parse("#fff2c94c") && view.ActiveNotices.Any(n => n.Id == "pr"), "Notice switch removes color without removing condition or Warning highlighting");
        notices = [new("hr", MonitorNoticeLevel.Critical, "ECG HR 极高") { Numeric = MonitorNumeric.HeartRate }];
        view.RefreshReadings(snapshot); view.RefreshNumericHighlights(0);
        Require(Color(LiveMonitorView.NumericBackground(hr)) == Avalonia.Media.Color.Parse("#ffb51f2c") && Color(hr.Foreground) == Avalonia.Media.Color.Parse("#ffffffff"), "critical red backing uses white text");
        view.RefreshNumericHighlights(500_000_000);
        Require(Color(view.NoticeSurface.Background) == Avalonia.Media.Colors.Transparent, "critical banner also flashes");
        view.RefreshReadings(snapshot with { SpO2 = new(WaveformMeasurementStatus.Valid, 99000, 500000, snapshot.SampleTimeNs) { PerfusionMilliPercent = 200 } });
        Require(spo2.Text == "99?", "reportable low perfusion displays question suffix");
        view.RefreshReadings(snapshot with { SpO2 = new(WaveformMeasurementStatus.PoorSignal, null, null, snapshot.SampleTimeNs) { PerfusionMilliPercent = 20 } });
        Require(spo2.Text == "---", "unreportable saturation retains placeholder");
        notices = []; view.RefreshReadings(snapshot); view.RefreshNumericHighlights(0);
        Require(Color(LiveMonitorView.NumericBackground(hr)) == Avalonia.Media.Colors.Transparent, "cleared alarms immediately clear numeric highlighting");
    }
    private static void VerifyStableSlowContours()
    {
        var slots = MonitorDisplayConfiguration.Default(MonitorSkin.SevenRows).Slots.Select(s => s with { Automatic = false, SpeedTenthsMmPerSecond = 125 }).ToArray();
        var session = new LocalMonitorPreviewSession(PhysiologyDemoConfiguration.Default, new(MonitorSkin.SevenRows, slots));
        var trace = new LiveMonitorTrace(session);
        while (session.SimulationTimeNs < 6_000_000_000) { session.Advance(50_000_000); }
        for (int step = 0; step < 15; step++)
        {
            byte[] before = Raster(trace, 1000, 700);
            int right = (int)(132 + 850 * session.FrontierNs / 20_000_000_000d) - 3;
            session.Advance(200_000_000);
            byte[] after = Raster(trace, 1000, 700);
            foreach (int row in Enumerable.Range(0, slots.Length).Where(i => slots[i].Channel != 0))
                for (int y = row * 100 + 10; y < row * 100 + 90; y++)
                    for (int x = 134; x < right; x++)
                    {
                        int offset = (y * 1000 + x) * 4;
                        Require(before.AsSpan(offset, 4).SequenceEqual(after.AsSpan(offset, 4)),
                            $"new slow-sweep CO2/ABP/PA samples never repaint visible slopes: step {step}, row {row}, pixel {x}/{y}, frontier {session.FrontierNs}, source end {session.Blocks[^1].StartSimTimeNs + 200_000_000}");
                    }
        }
    }
    private static void VerifyGapAndCalibration(LiveMonitorTrace trace)
    {
        byte[] data = Raster(new LiveMonitorTrace(new LocalMonitorPreviewSession(PhysiologyDemoConfiguration.Default, MonitorDisplayConfiguration.Default())), 800, 600);
        bool Green(int x, int y) => data[(y * 800 + x) * 4 + 1] > 130 && data[(y * 800 + x) * 4 + 1] > data[(y * 800 + x) * 4 + 2] * 1.3;
        double gutter = 0;
        int calibrationX = (int)Math.Round(132 + 650 * .2 / 10);
        int calibrationPixels = Enumerable.Range(9, 100).Count(y => Green(calibrationX, y) || Green(calibrationX - 1, y));
        Require(Math.Abs(calibrationPixels - 100 * 1000 / 2700d) < 4, "ECG calibration has a true 1mV height at current range");
        double baselineY = 9 + 100 * (1 - 1200 / 2700d);
        int[] inkRows = Enumerable.Range(9, 100).Where(y => Green(calibrationX, y) || Green(calibrationX - 1, y)).ToArray();
        Require(Math.Abs((inkRows[0] + inkRows[^1]) / 2d - baselineY) < 2,
            "monitor calibration is centered on baseline, from minus to plus 0.5mV");
        data = Raster(trace, 800, 600);
        double phase = trace.Session.FrontierNs % MonitorDisplayConfiguration.SweepDurationNs / (double)MonitorDisplayConfiguration.SweepDurationNs;
        int headX = (int)(132 + gutter + phase * (650 - gutter));
        Require(Enumerable.Range(10, 95).Count(y => Green(headX, y) || Green(headX + 1, y)) < 12, "no vertical sweep cursor");
        Require(!Enumerable.Range(10, 95).Any(y => Green(headX + 4, y)), "erase gap contains no trace");
    }
    private static void VerifySixRowPaper(DesignPreviewTrace paper)
    {
        Require(paper.SixRows && paper.LongDurationNs == 10_300_000_000, "six-by-two paper retains five-second short leads and aligned long-II");
        Require(paper.ColumnStartNs(1) == 5_300_000_000, "second calibration masks300ms instead of shifting paper time");
        byte[] data = Raster(paper, 1124, 956);
        bool Dark(int x, int y) => data[(y * 1124 + x) * 4] < 160 && data[(y * 1124 + x) * 4 + 1] < 160;
        for (int column = 0; column < 2; column++)
            for (int row = 0; row < 6; row++)
            {
                int x = 58 + column * 530, baseline = 136 + row * 120;
                Require(Enumerable.Range(baseline - 39, 38).Count(y => Dark(x, y) || Dark(x - 1, y)) > 30, "all six rows have independent paper calibration");
            }
        Require(Enumerable.Range(855, 90).Any(y => Dark(1090, y)), "six-row long-II reaches the grid right edge");
    }
    private static void VerifyPaperEnd(DesignPreviewTrace paper)
    {
        Require(Enumerable.Range(0, 4).Select(paper.ColumnStartNs).SequenceEqual(new long[] { 0, 2_800_000_000, 5_600_000_000, 8_400_000_000 }), "short leads retain the common long-II time axis across calibration masks");
        byte[] data = Raster(paper, 1184, 596);
        bool Dark(int x, int y) => data[(y * 1184 + x) * 4] < 160 && data[(y * 1184 + x) * 4 + 1] < 160;
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 3; row++)
            {
                int x = 58 + column * 280, baseline = 136 + row * 120;
                Require(Enumerable.Range(baseline - 39, 38).Count(y => Dark(x, y) || Dark(x - 1, y)) > 30, "each ECG lead has an independent 1mV marker");
                Require(Enumerable.Range(x - 19, 18).Count(px => Dark(px, baseline - 40) || Dark(px, baseline - 41)) >= 17,
                    "paper calibration has a 200ms plateau, not a monitor line");
                Require(Enumerable.Range(baseline - 39, 38).Count(y => Dark(x - 20, y) || Dark(x - 21, y)) > 30,
                    "paper calibration has an independent rising edge");
            }
        Require(Enumerable.Range(39, 18).Count(x => Dark(x, 496) || Dark(x, 495)) >= 17, "long II has its own square calibration");
        Require(Enumerable.Range(500, 70).Any(y => Dark(1150, y)), "long lead reaches within one sample of paper grid right edge");
    }
    private static byte[] Raster(Control control, int width, int height)
    {
        control.Measure(new Size(width, height)); control.Arrange(new Rect(0, 0, width, height));
        using var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)); image.Render(control);
        using var pixels = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = pixels.Lock(); image.CopyPixels(buffer);
        byte[] data = new byte[width * height * 4];
        for (int y = 0; y < height; y++) { Marshal.Copy(buffer.Address + y * buffer.RowBytes, data, y * width * 4, width * 4); }
        return data;
    }
    private static void VerifyClamping(LiveMonitorTrace trace)
    {
        byte[] data = Raster(trace, 800, 360);
        bool Green(int x, int y) => data[(y * 800 + x) * 4 + 1] > 130 && data[(y * 800 + x) * 4 + 1] > data[(y * 800 + x) * 4 + 2] * 1.3;
        Require(Enumerable.Range(145, 290).Count(x => Green(x, 9) || Green(x, 10)) > 20, "overrange ECG flattens to upper edge");
        Require(!Enumerable.Range(140, 600).Any(x => Green(x, 116) || Green(x, 119) || Green(x, 123)), "trace never invades row separator or next row");
    }
    private static void Capture(DesignPreviewWindow window, string name)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Directory.CreateDirectory("artifacts");
        var root = (Control)window.Content!;
        double width = double.IsNaN(root.Width) ? window.Width : root.Width;
        double height = double.IsNaN(root.Height) ? window.Height : root.Height;
        using var image = new RenderTargetBitmap(new PixelSize((int)width, (int)height), new Vector(96, 96));
        root.InvalidateMeasure();
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height));
        image.Render(root); image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }
    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Design preview: " + message); } }
}
