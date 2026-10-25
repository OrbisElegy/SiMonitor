// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Monitor.Application.Measurements;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Infrastructure.Audio;
using Monitor.Simulation.Therapy;

namespace Monitor.Desktop;

internal static class DefibrillatorSmokeChecks
{
    private static readonly int[] CustomAedSequence = [100, 200, 300];

    private static void VerifyAedSequenceEditor()
    {
        var editor = new DefibrillatorSettingsEditor(new DesktopLocalization("en"));
        int[] skinSteps = [100, 200, 360];
        editor.SetSkinDefaults(DefibrillatorConfiguration.Default, skinSteps, false,
            DefibrillatorConfiguration.Default with { AedEnergySequenceJoules = CustomAedSequence });
        Require(editor.AedSteps.IsEffectivelyEnabled && editor.Read().AedEnergySequenceJoules!.SequenceEqual(skinSteps),
            "custom AED targets remain editable on fixed profiles and map to the new skin table");
        string path = Path.Combine(Path.GetTempPath(), "monitor-aed-sequence-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var window = new DesignPreviewWindow(path);
            try
            {
                window.Settings.Defibrillator.AedSteps.Text = "100, 200, 300";
                window.RestartSettings();
                Require(window.Defibrillator.Configuration.AedEnergySequenceJoules!.SequenceEqual(CustomAedSequence), "applied sequence reaches the device");
                window.Settings.Defibrillator.AedSteps.Text = "300, 200";
                Require(window.Settings.Defibrillator.Error.IsVisible, "decreasing sequence reports inline validation");
                var original = window.Session;
                window.RestartSettings();
                Require(ReferenceEquals(original, window.Session), "invalid sequence cannot replace the session");
            }
            finally { window.Close(); }
            var reopened = new DesignPreviewWindow(path);
            try
            {
                Require(reopened.Settings.Defibrillator.AedSteps.Text == "100, 200, 300", "applied sequence survives reopening despite invalid unsaved draft");
                reopened.Settings.Defibrillator.AedSteps.Text = "100, 100, 200";
                Require(!reopened.Settings.Defibrillator.Error.IsVisible, "repeated steps are valid");
                reopened.Settings.Defibrillator.AedSteps.Text = "";
                Require(reopened.Settings.Defibrillator.Read().AedEnergySequenceJoules is null, "blank restores the device protocol");
            }
            finally { reopened.Close(); }
        }
        finally { File.Delete(path); }
    }

    internal static void Verify()
    {
        VerifyAedSequenceEditor();
        VerifyEnergyDefaults();
        VerifyFixedHardwareLabels();
        long now = 0;
        var window = new DesignPreviewWindow(safetyClock: () => now);
        window.Show();
        ((GenericMonitorSkin)window.MonitorTrace.Skin!).ShowTherapy.IsChecked = true;
        try
        {
            window.Settings.EcgSelection = 21;
            window.Settings.ElectricalConversion.Enabled.IsChecked = true;
            window.Settings.ElectricalConversion.Biphasic.Value = 150;
            window.Settings.Defibrillator.UseSkinEnergySteps.IsChecked = false;
            window.Settings.Defibrillator.Steps.Text = "5, 20, 50, 100, 200, 360";
            window.Settings.Defibrillator.ChargeSeconds.Value = 1;
            window.RestartSettings();
            for (int i = 0; i < 200; i++) { window.Pulse(window.ActiveTimer, 50_000_000); }
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            var controls = skin.Defibrillator;
            void Tick(long elapsed)
            {
                now += elapsed;
                window.Pulse(window.ActiveTimer, 50_000_000);
            }
            void Charge()
            {
                Click(controls.Charge);
                Tick(1_000_000_000);
                Require(window.Defibrillator.State.Attempt == DefibrillationAttemptState.Charged, "charge reaches ready");
            }
            Require((int)controls.Energy.SelectedItem! == 100, "a missing energy is normalized to a supported step");
            skin.Rate.Value = null;
            Click(controls.HigherEnergy);
            Require((int)controls.Energy.SelectedItem! == 200 && window.Defibrillator.EnergyJoules == 200,
                "nonlinear energy selection reaches the command owner even with an invalid pacing draft");
            skin.Rate.Value = 70;
            Click(controls.Aed);
            Require(window.Aed.Phase == AedPhase.Analyzing && window.Defibrillator.State.Energy == EnergyState.Idle,
                "AED starts analysis without authorizing a shock");
            Click(controls.Aed);
            Click(controls.Charge);
            Tick(500_000_000);
            Require(controls.ChargeProgressPermille == 500 && controls.ChargeLit && !controls.Shock.IsEnabled &&
                !controls.Children.OfType<ProgressBar>().Any() && ReferenceEquals(controls.Charge.Content, controls.ChargeFill),
                "charge progress is contained in the button without a separate bar");
            var fill = (LinearGradientBrush)controls.ChargeFill.Background!;
            Require(fill.StartPoint == RelativePoint.TopLeft && fill.EndPoint == new RelativePoint(1, 0, RelativeUnit.Relative) &&
                fill.GradientStops[1].Offset == .5 && fill.GradientStops[2].Offset == .5,
                "the charging fill has a sharp left-to-right boundary at the actual fraction");
            Capture(window, "generic-defibrillator-charging.png");
            Click(controls.LowerEnergy);
            Require(window.Defibrillator.State.Energy == EnergyState.Idle && !controls.ChargeLit && controls.ChargeProgressPermille == 0, "changing energy cancels charge");
            Click(controls.HigherEnergy);
            Charge();
            bool firstFlash = controls.ShockLit;
            Tick(400_000_000);
            Require(controls.ChargeLit && controls.ChargeProgressPermille == 1000 && controls.ShockLit != firstFlash && controls.Shock.IsEnabled,
                "ready shock flashes while the charge button stays fully filled");
            Capture(window, "generic-defibrillator-ready.png");
            Click(controls.Shock);
            Require(window.Defibrillator.State.Attempt == DefibrillationAttemptState.Charged, "click alone never delivers");
            KeyEvent(controls.Shock, keyDown: true);
            Tick(250_000_000);
            KeyEvent(controls.Shock, keyDown: false);
            Tick(500_000_000);
            Require(window.Defibrillator.State.Energy == EnergyState.Idle && window.Session.ElectricalTherapy!.TemplateId == "ecgTemplate.t021",
                "short keyboard hold then release cancels without conversion");
            Charge();
            DesktopViewportSmokeChecks.Layout(window);
            var waveformBounds = window.MonitorTrace.Bounds;
            var toolbarPosition = skin.LayoutButton.TranslatePoint(default, window.MonitorView);
            KeyEvent(controls.Shock, keyDown: true);
            Tick(500_000_000);
            KeyEvent(controls.Shock, keyDown: false);
            Require(window.Defibrillator.State.Attempt == DefibrillationAttemptState.Delivered && !controls.ChargeLit &&
                window.Session.PendingSourceTimeNs is not null, "held asynchronous shock schedules conversion and extinguishes charge");
            DesktopViewportSmokeChecks.Layout(window);
            Require(window.MonitorTrace.Bounds == waveformBounds &&
                skin.LayoutButton.TranslatePoint(default, window.MonitorView) == toolbarPosition,
                "shock delivery preserves waveform space and toolbar position without a feedback row");
            for (int i = 0; i < 20; i++) { Tick(50_000_000); }
            Require(window.Session.ElectricalTherapy!.TemplateId == "ecgTemplate.t000", "delivery plays the prepared sinus waveform");
            DesktopViewportSmokeChecks.Layout(window);
            Require(window.MonitorTrace.Bounds == waveformBounds &&
                skin.LayoutButton.TranslatePoint(default, window.MonitorView) == toolbarPosition,
                "completed conversion preserves the monitor layout");
            Click(controls.Sync);
            Require(controls.SyncLit, "sync has its own enabled indicator");
            Charge();
            window.Pause();
            Require(window.Defibrillator.State.Energy == EnergyState.Idle && !controls.Shock.IsEnabled, "pause disarms charged energy");
            window.Start();
            Charge();
            window.SelectPage(2);
            Require(window.Defibrillator.State.Energy == EnergyState.Idle, "leaving the monitor cancels pending therapy");
            window.Settings.Tabs.SelectedIndex = 6;
            window.Settings.SectionPages[6].SelectedSection = 3;
            Capture(window, "defibrillator-configuration.png");
            var editor = window.Settings.Defibrillator;
            editor.UseSkinEnergySteps.IsChecked = false;
            editor.Steps.Text = "200, 50, 50";
            Require(editor.Error.IsVisible, "invalid energy sequence has an immediate inline error");
            var previous = window.Defibrillator;
            window.ApplySettings();
            Require(ReferenceEquals(previous, window.Defibrillator), "invalid configuration cannot replace the command owner");
            var locked = DefibrillatorConfiguration.Default with { Waveform = DefibrillationWaveformKind.MonophasicTruncatedExponential };
            editor.SetDeviceOverride(locked);
            editor.UseSkinEnergySteps.IsChecked = true;
            editor.Steps.Text = "not a valid editable draft";
            Require(!editor.Steps.IsEffectivelyEnabled && editor.Read().Waveform == locked.Waveform &&
                editor.Read().EnergyStepsJoules.SequenceEqual(locked.EnergyStepsJoules), "selected skin defaults ignore a programmatically changed disabled draft");
            editor.SetDeviceOverride(null);
            editor.Restore(DefibrillatorConfiguration.Default);
            window.Localization.Select("en");
            Require(editor.Error.IsVisible == false, "restored valid profile clears validation");
        }
        finally { window.Close(); }
    }

    private static void VerifyFixedHardwareLabels()
    {
        var localization = new DesktopLocalization("zh-CN");
        var configuration = DefibrillatorConfiguration.Default with { ChargeDurationMilliseconds = 1000 };
        List<DefibrillatorCommand> commands = [];
        var controls = new DefibrillatorControls(localization, configuration, 200, commands.Add);
        controls.UseFixedHardwareLabels();
        controls.UseFixedHardwareLabels();
        var device = new ManualDefibrillator(Guid.NewGuid(), configuration, 200);
        device.BeginCharge(0);
        _ = device.Tick(500_000_000, 500_000_000, []);
        int screenProgress = -1;
        controls.StateRefreshed += () => screenProgress = controls.ChargeProgressPermille;
        controls.Refresh(device, 500_000_000, available: true);
        Require(controls.Charge.Content is string caption && caption == localization.Get("skin.charge") &&
            controls.ChargeFill.Background is null && controls.ChargeProgressPermille == 500 && screenProgress == 500,
            "hardware charge uses the button caption and exposes progress to the screen after state refresh");
        var aed = new AutomatedExternalDefibrillator(device);
        aed.Enable(500_000_000, 500_000_000);
        controls.Refresh(device, 500_000_000, available: true, aed);
        Require(!controls.Charge.IsEnabled, "hardware charge is unavailable during AED analysis");
        aed.Suspend(500_000_000);
        controls.Refresh(device, 500_000_000, available: true, aed);
        Click(controls.Charge);
        Require(!controls.Charge.IsEnabled && commands.Count == 0 &&
            Equals(controls.Charge.Content, localization.Get("skin.charge")) &&
            AutomationProperties.GetName(controls.Charge) == localization.Get("skin.charge"),
            "suspended AED cannot relabel or repurpose the hardware charge key");
        controls.RunAedSoftCommand();
        Require(commands.Count == 1 && commands[0] == DefibrillatorCommand.ResumeAedAnalysis,
            "the AED screen soft key retains explicit reanalysis");
        var generic = new DefibrillatorControls(localization, configuration, 200, null);
        generic.Refresh(device, 500_000_000, available: true, aed);
        Require(generic.Charge.IsEnabled && ReferenceEquals(generic.Charge.Content, generic.ChargeFill) &&
            ((TextBlock)generic.ChargeFill.Child!).Text == localization.Get("aed.analyze"),
            "generic controls retain their contextual reanalysis caption and button progress");
        localization.Select("en");
        Require(Equals(controls.Charge.Content, localization.Get("skin.charge")) &&
            AutomationProperties.GetName(controls.Charge) == localization.Get("skin.charge"),
            "hardware charge lettering and accessibility follow language changes without changing function");
        aed.Disable(500_000_000);
        controls.Refresh(device, 500_000_000, available: true, aed);
        Click(controls.Charge);
        Require(controls.Charge.IsEnabled && commands[^1] == DefibrillatorCommand.Charge,
            "leaving AED restores manual charging on the same fixed hardware key");
    }

    private static void VerifyEnergyDefaults()
    {
        var editor = new DefibrillatorSettingsEditor(new DesktopLocalization("en"));
        int[] skinSteps = [5, 30, 100, 200, 360];
        int[] expectedSkinSteps = [5, 30, 100, 200, 360];
        int[] manualSteps = [3, 9, 90, 270];
        int[] expectedManualSteps = [3, 9, 90, 270];
        int[] expectedDraftSteps = [7, 13, 44, 220];
        var editable = new DefibrillatorConfiguration(manualSteps,
            DefibrillationWaveformKind.BiphasicTruncatedExponential, 1500, 45)
        { EcgRecoveryMilliseconds = 600 };
        var profile = DefibrillatorConfiguration.Default with
        {
            Waveform = DefibrillationWaveformKind.MonophasicTruncatedExponential,
            ChargeDurationMilliseconds = 7000,
            AutoDisarmSeconds = 60,
            EcgRecoveryMilliseconds = 2500
        };
        Require(editor.UseSkinEnergySteps.IsChecked == true && !editor.CustomEnergySteps,
            "a fresh editor follows skin energy defaults");
        editor.SetSkinDefaults(profile, skinSteps, customEnergySteps: false, editable);
        skinSteps[0] = 1;
        manualSteps[0] = 1;
        Require(!editor.Steps.IsEffectivelyEnabled && editor.UseSkinEnergySteps.IsEffectivelyEnabled &&
            editor.Read().EnergyStepsJoules.SequenceEqual(expectedSkinSteps),
            "skin defaults are snapshotted independently of the behavior profile and source array");
        editor.Steps.Text = "invalid disabled text";
        Require(!editor.Error.IsVisible && editor.Read().EnergyStepsJoules.SequenceEqual(expectedSkinSteps),
            "programmatic writes to the disabled energy field cannot replace selected skin defaults");

        editor.UseSkinEnergySteps.IsChecked = false;
        var custom = editor.Read();
        Require(editor.CustomEnergySteps && editor.Steps.IsEffectivelyEnabled &&
            !editor.Waveform.IsEffectivelyEnabled && !editor.ChargeSeconds.IsEffectivelyEnabled &&
            !editor.AutoDisarmSeconds.IsEffectivelyEnabled && !editor.EcgRecoverySeconds.IsEffectivelyEnabled &&
            custom.EnergyStepsJoules.SequenceEqual(expectedManualSteps),
            "manual energy stays editable and overrides the profile while only behavior fields are locked");
        Require(custom.Waveform == profile.Waveform && custom.ChargeDurationMilliseconds == 7000 &&
            custom.AutoDisarmSeconds == 60 && custom.EcgRecoveryMilliseconds == 2500,
            "manual energy selection preserves the device's waveform and timing constraints");
        editor.ChargeSeconds.Value = 2;
        Require(editor.Read().ChargeDurationMilliseconds == 7000,
            "programmatic changes cannot bypass a disabled behavior field");

        const string draft = "7; 13, 44，220";
        editor.Steps.Text = draft;
        Require(editor.Read().EnergyStepsJoules.SequenceEqual(expectedDraftSteps),
            "the manual table drives the effective profile");
        editor.UseSkinEnergySteps.IsChecked = true;
        Require(editor.Read().EnergyStepsJoules.SequenceEqual(expectedSkinSteps) && !editor.Error.IsVisible,
            "reselecting defaults restores the skin table immediately");
        editor.UseSkinEnergySteps.IsChecked = false;
        Require(editor.Steps.Text == draft && editor.Read().EnergyStepsJoules.SequenceEqual(expectedDraftSteps),
            "switching back restores the exact manual draft rather than replacing it with skin defaults");

        const string invalidDraft = "220, 44, 44";
        editor.Steps.Text = invalidDraft;
        bool rejected = false;
        try { _ = editor.Read(); }
        catch (ArgumentException) { rejected = true; }
        Require(editor.Error.IsVisible && rejected, "an invalid manual table is rejected before it reaches a command owner");
        editor.UseSkinEnergySteps.IsChecked = true;
        Require(!editor.Error.IsVisible && editor.Read().EnergyStepsJoules.SequenceEqual(expectedSkinSteps),
            "an unused invalid draft does not block valid skin defaults");
        editor.UseSkinEnergySteps.IsChecked = false;
        Require(editor.Error.IsVisible && editor.Steps.Text == invalidDraft,
            "returning to an invalid manual draft preserves the input and restores its inline error");

        editor.SetSkinDefaults(null, expectedSkinSteps, customEnergySteps: true, editable);
        Require(editor.Steps.IsEffectivelyEnabled && editor.Waveform.IsEffectivelyEnabled &&
            editor.ChargeSeconds.IsEffectivelyEnabled && editor.AutoDisarmSeconds.IsEffectivelyEnabled &&
            editor.EcgRecoverySeconds.IsEffectivelyEnabled && editor.Read().ChargeDurationMilliseconds == 1500 &&
            editor.Read().Waveform == editable.Waveform && !editor.Error.IsVisible,
            "removing the device profile restores editable behavior fields and a valid saved manual configuration");
    }

    internal static void VerifyPostShock()
    {
        long safetyNs = 0;
        var window = new DesignPreviewWindow(safetyClock: () => safetyNs);
        window.Show();
        ((GenericMonitorSkin)window.MonitorTrace.Skin!).ShowTherapy.IsChecked = true;
        try
        {
            window.Settings.EcgSelection = 21;
            window.Settings.ElectricalConversion.Enabled.IsChecked = true;
            window.Settings.ElectricalConversion.Biphasic.Value = 150;
            window.Settings.ElectricalConversion.PostShockPause.Value = 2;
            window.Settings.Defibrillator.EcgRecoverySeconds.Value = 0.6m;
            window.Settings.Defibrillator.ChargeSeconds.Value = 0.1m;
            window.RestartSettings();
            var skin = (GenericMonitorSkin)window.MonitorTrace.Skin!;
            skin.Energy.SelectedItem = 200;
            window.Settings.Sound.HeartbeatEnabled.IsChecked = true;
            window.Settings.Sound.BeatSource.SelectedIndex = 0;
            int liveHeartbeatCues = 0;
            void Advance(int frames)
            {
                for (int i = 0; i < frames; i++)
                {
                    safetyNs += 50_000_000;
                    long? previousCue = window.Settings.Sound.LastHeartbeatConfirmedAtNs;
                    window.Pulse(window.ActiveTimer, 50_000_000);
                    if (window.Settings.Sound.LastHeartbeatConfirmedAtNs is { } cue && cue != previousCue)
                    {
                        liveHeartbeatCues++;
                        Require(window.Session.SimulationTimeNs - cue <= 50_000_000 &&
                            window.Session.BeatEvidence!.EcgBeats.Any(beat => beat.ConfirmedAtNs == cue),
                            "audible QRS and measured ECG share this frame's confirmed beat");
                    }
                }
            }
            Advance(200);
            Require(window.Session.Measurements!.SampleTimeNs == window.Session.SimulationTimeNs &&
                Enumerable.Range(0, window.Session.Display.Slots.Count).All(row => window.Session.Ranges.RowCycle(row) == 1),
                "all monitor rows use the same live time and sweep boundary before charging");
            Click(skin.Defibrillator.Charge);
            Advance(3);
            KeyEvent(skin.Defibrillator.Shock, keyDown: true);
            Advance(9);
            long numericBucket = window.Session.SimulationTimeNs / 200_000_000;
            Require(window.Session.Measurements!.HeartRate.Status is WaveformMeasurementStatus.Valid or WaveformMeasurementStatus.Uncountable &&
                window.MonitorView.NumericTexts[0] != "---", "the pre-shock ECG has its current measured or uncountable readout");
            Advance(1);
            KeyEvent(skin.Defibrillator.Shock, keyDown: false);
            Require(window.Session.ShockArtifacts.Count == 1, "a confirmed native shock reaches the acquired ECG path");
            Require(window.Session.SimulationTimeNs / 200_000_000 == numericBucket &&
                window.Session.Measurements!.HeartRate.Status == WaveformMeasurementStatus.PoorSignal &&
                window.MonitorView.NumericTexts[0] == "---" &&
                window.MonitorView.ActiveNotices.Any(notice => notice.Id == "measurement:HR:PoorSignal"),
                "shock validity changes clear the number and update notices within the current numeric refresh bucket");
            var artifact = window.Session.ShockArtifacts[0];
            long shockFrontier = window.Session.SimulationTimeNs;
            Require(shockFrontier == artifact.DeliveredAtSimTimeNs, "the shock is anchored at the live ECG sweep position");
            Advance(1);
            Require(window.Session.Samples(0, shockFrontier, window.Session.SimulationTimeNs)
                .Any(sample => Math.Abs(sample.Value) > 3000), "the next rendered frame contains the shock on the unified acquisition timeline");
            Require(Enumerable.Range(0, 7).All(channel => window.Session.Samples(channel, shockFrontier, window.Session.SimulationTimeNs).Any()),
                "the shock frame advances ECG, PLETH and all pressure channels together");
            Capture(window, "generic-post-shock-immediate.png");
            Advance(7);
            Require(window.Session.Configuration.CardiacActivity == Monitor.Simulation.Physiology.CardiacActivity.Absent &&
                window.Session.PendingSourceTimeNs is not null, "the configured cardiac pause precedes sinus recovery");
            Capture(window, "generic-post-shock-recovery.png");
            Advance(150);
            Require(liveHeartbeatCues >= 4, "post-shock sinus resumes real-time QRS cues");
            window.Pause();
            Require(window.Session.ElectricalTherapy!.TemplateId == "ecgTemplate.t000" &&
                window.Session.Samples(0, artifact.DeliveredAtSimTimeNs, artifact.RecoveryEndSimTimeNs).Any(sample => Math.Abs(sample.Value) > 3000),
                "sinus recovery retains the actual shock artifact in waveform history");
            Capture(window, "generic-post-shock-sinus.png");
            window.SelectPage(2);
            window.Settings.OpenAdvanced(0);
            Require(window.Settings.ElectricalConversion.PostShockPause.Value == 2, "conversion retains the per-template pause draft");
            window.Settings.ElectricalConversion.PostShockPause.Value = null;
            Require(window.Settings.ElectricalConversion.Error.IsVisible, "missing pause input is rejected visibly");
        }
        finally { window.Close(); }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal static void VerifyAed()
    {
        long safetyNs = 0;
        var window = new DesignPreviewWindow(safetyClock: () => safetyNs, startAudioOutput: false);
        window.Show();
        ((GenericMonitorSkin)window.MonitorTrace.Skin!).ShowTherapy.IsChecked = true;
        try
        {
            window.Settings.EcgSelection = 21;
            window.Settings.ElectricalConversion.Enabled.IsChecked = false;
            window.Settings.AedConversion.Enabled.IsChecked = true;
            window.Settings.AedConversion.Biphasic.Value = 150;
            window.Settings.AedConversion.PostShockPause.Value = 2;
            window.Settings.Defibrillator.ChargeSeconds.Value = .1m;
            window.RestartSettings();
            // This fixture advances both clocks explicitly; rendering must not
            // let a wall-clock dispatcher tick advance only simulation time.
            window.ActiveTimer!.Stop();
            // Settle native startup and layout before beginning the AED workflow.
            // Later snapshots only arrange content: ShowActivated cannot prevent
            // queued focus notifications from other native smoke processes.
            DesktopViewportSmokeChecks.Layout(window);
            void Advance(int frames)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    safetyNs += 50_000_000;
                    window.Pulse(window.ActiveTimer, 50_000_000);
                }
            }
            Advance(200);
            var controls = ((GenericMonitorSkin)window.MonitorTrace.Skin!).Defibrillator;
            controls.Energy.SelectedItem = 200;
            Click(controls.Sync);
            Click(controls.Aed);
            Require(window.Aed.Phase == AedPhase.Analyzing && !controls.SyncLit && !controls.Sync.IsEnabled &&
                !controls.Energy.IsEnabled && !controls.Shock.IsEnabled, "AED clears sync and locks manual therapy controls");
            Require(controls.Status.Text == window.Localization.Format("aed.phase.Analyzing", 8), "analysis prompt includes its countdown");
            Require(window.Settings.Sound.PublishedTherapy is { Phase: TherapySoundPhase.Analyzing, EnteringAed: true },
                "AED entry publishes adult and analysis speech once");
            Advance(162);
            Require(window.Aed.Phase == AedPhase.ShockAdvised && controls.Shock.IsEnabled && controls.ChargeLit,
                "live VF automatically charges AED");
            Require(controls.Status.Text == window.Localization.Get("aed.phase.ShockAdvised"), "shock advice text remains visible without countdown arguments");
            window.Localization.Select("en");
            Require(controls.Status.Text == window.Localization.Get("aed.phase.ShockAdvised"), "active AED prompt follows language changes");
            Require(window.Settings.Sound.PublishedTherapy is { Phase: TherapySoundPhase.Ready, Locale: "en", Announce: false },
                "language change withdraws the old voice without replaying ready history");
            window.Localization.Select("zh-CN");
            long capturedSimulationTimeNs = window.Session.SimulationTimeNs;
            var capturedPhase = window.Aed.Phase;
            // Reproduce focus loss queued during capture on every platform,
            // without depending on other shards or the host window manager.
            bool deactivationDelivered = false;
            Dispatcher.UIThread.Post(() =>
            {
                deactivationDelivered = true;
                NotifyDeactivation(window);
            });
            Capture(window, "generic-aed-shock-advised.png", processPendingEvents: false);
            Require(window.Session.SimulationTimeNs == capturedSimulationTimeNs,
                $"rendering an AED snapshot preserves manually advanced time ({capturedSimulationTimeNs} -> {window.Session.SimulationTimeNs})");
            Require(window.Aed.Phase == capturedPhase,
                $"rendering an AED snapshot preserves advice without native focus interference ({capturedPhase} -> {window.Aed.Phase})");
            Require(!deactivationDelivered, "snapshot layout leaves unrelated dispatcher input pending");
            Dispatcher.UIThread.RunJobs();
            Require(deactivationDelivered && window.Aed.Phase == AedPhase.Suspended &&
                window.Defibrillator.State.Energy == EnergyState.Idle && !controls.Shock.IsEnabled &&
                window.Settings.Sound.PublishedTherapy?.Phase == TherapySoundPhase.Cancelled,
                "dispatching deactivation clears AED advice and charge and publishes cancellation");
            Click(controls.Charge);
            Advance(162);
            Require(window.Aed.Phase == AedPhase.ShockAdvised && controls.Shock.IsEnabled && controls.ChargeLit,
                "AED advice resumes only after explicit reanalysis following deactivation");
            Click(controls.Shock);
            Require(window.Session.ShockArtifacts.Count == 0, "an ordinary click cannot discharge AED");
            Require(window.Settings.Sound.TherapyRelayCount == 0, "ready and button clicks do not emit a discharge relay");
            KeyEvent(controls.Shock, true);
            Advance(4);
            KeyEvent(controls.Shock, false);
            Require(window.Aed.Phase == AedPhase.Suspended && window.Defibrillator.State.Energy == EnergyState.Idle &&
                controls.Charge.IsEnabled, "a short hold disarms and offers reanalysis");
            Click(controls.Charge);
            Advance(162);
            KeyEvent(controls.Shock, true);
            Advance(10);
            KeyEvent(controls.Shock, false);
            Require(window.Aed.Phase == AedPhase.Cpr && window.Session.ShockArtifacts.Count == 1 &&
                !controls.Shock.IsEnabled && window.Session.PendingSourceTimeNs is not null,
                "held AED shock uses the common artifact and conversion path exactly once");
            Require(window.Settings.Sound.TherapyRelayCount == 1, "only actual AED delivery emits the discharge relay");
            Require(window.Settings.Sound.PublishedTherapy is { Phase: TherapySoundPhase.CprShock, Announce: true } &&
                !window.Settings.ElectricalConversion.Enabled.IsChecked.GetValueOrDefault(),
                "AED delivery publishes its CPR sequence and converts with manual conversion disabled");
            Advance(50);
            Require(window.Aed.Phase == AedPhase.Cpr && window.Session.ElectricalTherapy!.TemplateId == "ecgTemplate.t000",
                "post-shock quiet interval and sinus transition preserve the CPR cycle");
            Capture(window, "generic-aed-cpr.png", processPendingEvents: false);
            Advance(2350);
            Require(window.Aed.Phase == AedPhase.Analyzing, "120 seconds of CPR starts another analysis");
            Advance(180);
            Require(window.Aed.Phase == AedPhase.Cpr && window.Aed.Advice == AedAdvice.NoShock &&
                window.Defibrillator.State.DeliveredCount == 1, "converted sinus gets no-shock advice without a second discharge: " +
                window.Aed.Phase + "/" + window.Aed.Advice + " " + window.Session.EcgEvidence);
            window.Pause();
            Require(window.Aed.Phase == AedPhase.Suspended && !controls.Charge.IsEnabled, "pause suspends the complete AED workflow");
            Require(window.Settings.Sound.PublishedTherapy?.Phase == TherapySoundPhase.Silent, "pause removes all therapy sound intent");
            window.Start();
            window.ActiveTimer!.Stop();
            Click(controls.Charge);
            Require(window.Aed.Phase == AedPhase.Analyzing, "resume requires explicit reanalysis");
            window.SelectPage(2);
            Require(window.Aed.Phase == AedPhase.Suspended, "leaving the monitor invalidates AED analysis");
            window.Settings.EcgSelection = 72;
            window.RestartSettings();
            window.ActiveTimer!.Stop();
            window.SelectPage(0);
            Require(!window.Aed.Enabled && window.Defibrillator.State.Energy == EnergyState.Idle,
                "template/configuration replacement resets the AED owner");
            controls = ((GenericMonitorSkin)window.MonitorTrace.Skin!).Defibrillator;
            Click(controls.Aed);
            Advance(280);
            Require(window.Aed.Phase == AedPhase.Cpr && window.Aed.Advice == AedAdvice.NoShock && !controls.Shock.IsEnabled,
                "asystole gives no-shock advice");
            Click(controls.Aed);
            Require(!window.Aed.Enabled && controls.Energy.IsEnabled && controls.Sync.IsEnabled,
                "leaving AED restores manual controls without retaining charge");
        }
        finally { window.Close(); }
    }

    internal static void VerifySynchronizationMarkers()
    {
        long safetyNs = 0;
        var window = new DesignPreviewWindow(safetyClock: () => safetyNs);
        window.Show();
        ((GenericMonitorSkin)window.MonitorTrace.Skin!).ShowTherapy.IsChecked = true;
        try
        {
            window.Settings.EcgSelection = 0;
            window.Settings.Defibrillator.ChargeSeconds.Value = .1m;
            window.RestartSettings();
            void Advance(int frames)
            {
                for (int frame = 0; frame < frames; frame++)
                {
                    safetyNs += 50_000_000;
                    window.Pulse(window.ActiveTimer, 50_000_000);
                }
            }
            Advance(160);
            var controls = ((GenericMonitorSkin)window.MonitorTrace.Skin!).Defibrillator;
            Click(controls.Sync);
            Require(window.MonitorTrace.SynchronizationPeaks.Count == 0, "enabling sync does not backfill historical QRS markers");
            long enabledAt = window.Session.SimulationTimeNs;
            Advance(60);
            long[] peaks = window.MonitorTrace.SynchronizationPeaks.ToArray();
            Require(peaks.Length >= 3 && peaks.All(peak => peak >= enabledAt) &&
                window.Session.SimulationTimeNs - peaks[^1] < 1_000_000_000,
                "sync markers follow confirmed live QRS without the old acquisition delay");
            Capture(window, "generic-defibrillator-synchronized.png");
            Click(controls.Charge);
            Advance(3);
            KeyEvent(controls.Shock, true);
            Advance(10);
            Require(window.Defibrillator.State.Attempt == DefibrillationAttemptState.AwaitingSync && controls.ChargeProgressPermille == 1000,
                "sync hold waits for the next marked QRS while retaining full charge");
            for (int frame = 0; frame < 30 && window.Defibrillator.State.Attempt != DefibrillationAttemptState.Delivered; frame++) { Advance(1); }
            Require(window.Defibrillator.State.Attempt == DefibrillationAttemptState.Delivered && controls.ChargeProgressPermille == 0,
                "a live QRS authorizes the actual synchronized delivery and clears the button fill");
            KeyEvent(controls.Shock, false);
            Click(controls.Sync);
            Require(window.MonitorTrace.SynchronizationPeaks.Count == 0, "disabling sync removes all triangles immediately");
            foreach (int template in new[] { 21, 72 })
            {
                window.Settings.EcgSelection = template;
                window.RestartSettings();
                Advance(100);
                controls = ((GenericMonitorSkin)window.MonitorTrace.Skin!).Defibrillator;
                Click(controls.Sync);
                Advance(40);
                Require(window.MonitorTrace.SynchronizationPeaks.Count == 0, "settled VF and asystole have no QRS lock markers");
            }
        }
        finally { window.Close(); }
    }
    private static void KeyEvent(Button button, bool keyDown) => button.RaiseEvent(new KeyEventArgs
    { RoutedEvent = keyDown ? InputElement.KeyDownEvent : InputElement.KeyUpEvent, Key = Key.Space });

    // Invoke the same locked-framework notification used by native focus loss,
    // without competing with other smoke processes for the desktop foreground.
    private static void NotifyDeactivation(Window window)
    {
        var handler = typeof(WindowBase).GetMethod("HandleDeactivated",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Locked framework deactivation handler is unavailable.");
        handler.Invoke(window, null);
    }

    private static void Capture(Window window, string name, bool processPendingEvents = true)
    {
        DesktopViewportSmokeChecks.Layout(window, 1440, 940, processPendingEvents);
        using var image = new RenderTargetBitmap(new PixelSize(1440, 940), new Vector(96, 96));
        image.Render((Control)window.Content!);
        Directory.CreateDirectory("artifacts");
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Defibrillator: " + message); } }
}
