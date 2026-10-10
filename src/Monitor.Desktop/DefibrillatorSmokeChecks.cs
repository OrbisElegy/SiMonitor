// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Monitor.Application.Therapy;
using Monitor.Domain.Therapy;
using Monitor.Infrastructure.Audio;
using Monitor.Simulation.Therapy;

namespace Monitor.Desktop;

internal static class DefibrillatorSmokeChecks
{
    internal static void Verify()
    {
        long now = 0;
        var window = new DesignPreviewWindow(safetyClock: () => now);
        window.Show();
        try
        {
            window.Settings.EcgSelection = 21;
            window.Settings.ElectricalConversion.Enabled.IsChecked = true;
            window.Settings.ElectricalConversion.Biphasic.Value = 150;
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
            KeyEvent(controls.Shock, keyDown: true);
            Tick(500_000_000);
            KeyEvent(controls.Shock, keyDown: false);
            Require(window.Defibrillator.State.Attempt == DefibrillationAttemptState.Delivered && !controls.ChargeLit &&
                window.Session.PendingSourceTimeNs is not null, "held asynchronous shock schedules conversion and extinguishes charge");
            for (int i = 0; i < 20; i++) { Tick(50_000_000); }
            Require(window.Session.ElectricalTherapy!.TemplateId == "ecgTemplate.t000", "delivery plays the prepared sinus waveform");
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
            window.Settings.SectionPages[6].SelectedSection = 4;
            Capture(window, "defibrillator-configuration.png");
            var editor = window.Settings.Defibrillator;
            editor.Steps.Text = "200, 50, 50";
            Require(editor.Error.IsVisible, "invalid energy sequence has an immediate inline error");
            var previous = window.Defibrillator;
            window.ApplySettings();
            Require(ReferenceEquals(previous, window.Defibrillator), "invalid configuration cannot replace the command owner");
            var locked = DefibrillatorConfiguration.Default with { Waveform = DefibrillationWaveformKind.MonophasicTruncatedExponential };
            editor.SetDeviceOverride(locked);
            editor.Steps.Text = "not a valid editable draft";
            Require(!editor.Steps.IsEffectivelyEnabled && editor.Read().Waveform == locked.Waveform &&
                editor.Read().EnergyStepsJoules.SequenceEqual(locked.EnergyStepsJoules), "device lock holds even if a disabled draft is changed programmatically");
            editor.SetDeviceOverride(null);
            editor.Restore(DefibrillatorConfiguration.Default);
            window.Localization.Select("en");
            Require(editor.Error.IsVisible == false, "restored valid profile clears validation");
        }
        finally { window.Close(); }
    }

    internal static void VerifyPostShock()
    {
        long safetyNs = 0;
        var window = new DesignPreviewWindow(safetyClock: () => safetyNs);
        window.Show();
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
                            window.Session.LiveBeatEvidence!.EcgBeats.Any(beat => beat.ConfirmedAtNs == cue),
                            "audible QRS follows this frame's visible ECG, never a delayed measurement packet");
                    }
                }
            }
            Advance(200);
            Require(window.Session.Display.Slots.All(slot => window.Session.PresentationFrontierNs(slot.Channel) == window.Session.SimulationTimeNs) &&
                Enumerable.Range(0, window.Session.Display.Slots.Count).All(row => window.Session.Ranges.RowCycle(row) == 1),
                "all monitor rows use the same live time and sweep boundary before charging");
            Click(skin.Defibrillator.Charge);
            Advance(3);
            KeyEvent(skin.Defibrillator.Shock, keyDown: true);
            Advance(10);
            KeyEvent(skin.Defibrillator.Shock, keyDown: false);
            Require(window.Session.ShockArtifacts.Count == 1, "a confirmed native shock reaches the acquired ECG path");
            var artifact = window.Session.ShockArtifacts[0];
            long shockFrontier = window.Session.PresentationFrontierNs(0);
            Require(shockFrontier == artifact.DeliveredAtSimTimeNs, "the shock is anchored at the live ECG sweep position");
            Advance(1);
            Require(window.Session.PresentedSamples(0, shockFrontier, window.Session.PresentationFrontierNs(0))
                .Any(sample => Math.Abs(sample.Value) > 3000), "the next rendered frame contains the shock before delayed packets arrive");
            Require(Enumerable.Range(0, 7).All(channel => window.Session.PresentationFrontierNs(channel) == window.Session.SimulationTimeNs &&
                window.Session.PresentedSamples(channel, shockFrontier, window.Session.SimulationTimeNs).Any()),
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
        var window = new DesignPreviewWindow(safetyClock: () => safetyNs);
        window.Show();
        try
        {
            window.Settings.EcgSelection = 21;
            window.Settings.AedConversion.Enabled.IsChecked = true;
            window.Settings.AedConversion.Biphasic.Value = 150;
            window.Settings.AedConversion.PostShockPause.Value = 2;
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
            Capture(window, "generic-aed-shock-advised.png");
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
            Capture(window, "generic-aed-cpr.png");
            Advance(2350);
            Require(window.Aed.Phase == AedPhase.Analyzing, "120 seconds of CPR starts another analysis");
            Advance(180);
            Require(window.Aed.Phase == AedPhase.Cpr && window.Aed.Advice == AedAdvice.NoShock &&
                window.Defibrillator.State.DeliveredCount == 1, "converted sinus gets no-shock advice without a second discharge: " +
                window.Aed.Phase + "/" + window.Aed.Advice + " " + window.Session.LiveEcgEvidence);
            window.Pause();
            Require(window.Aed.Phase == AedPhase.Suspended && !controls.Charge.IsEnabled, "pause suspends the complete AED workflow");
            Require(window.Settings.Sound.PublishedTherapy?.Phase == TherapySoundPhase.Silent, "pause removes all therapy sound intent");
            window.Start();
            Click(controls.Charge);
            Require(window.Aed.Phase == AedPhase.Analyzing, "resume requires explicit reanalysis");
            window.SelectPage(2);
            Require(window.Aed.Phase == AedPhase.Suspended, "leaving the monitor invalidates AED analysis");
            window.Settings.EcgSelection = 72;
            window.RestartSettings();
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

    private static void Capture(Window window, string name)
    {
        DesktopViewportSmokeChecks.Layout(window, 1440, 940);
        using var image = new RenderTargetBitmap(new PixelSize(1440, 940), new Vector(96, 96));
        image.Render((Control)window.Content!);
        Directory.CreateDirectory("artifacts");
        image.Save(Path.Combine("artifacts", name), PngBitmapEncoderOptions.Default);
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Defibrillator: " + message); } }
}
