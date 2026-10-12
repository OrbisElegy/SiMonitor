// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia;
using Avalonia.Media.Imaging;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class DextrocardiaSmokeChecks
{
    internal static void Verify()
    {
        var pair = DesignPreviewWindow.ResolveStyle(180, 0, 0);
        Require(pair.Physiology.Dextrocardia && pair.Ecg.Dextrocardia && pair.Ecg.Pacing is null &&
            EcgChooserGroups.For(180) == "窦性心律", "catalog selects dextrocardia within sinus rhythms");
        var source = ProjectedEcgDemoSource.Create(pair.Ecg);
        var monitorSource = PhysiologyDemoSource.Create(pair.Physiology);
        var expected = new List<WaveformEnvelope>();
        var monitor = new List<WaveformEnvelope>();
        for (int step = 1; step <= 20; step++)
        {
            expected.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
            monitor.AddRange(monitorSource.AdvanceTo(step * 200_000_000L, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
        }
        // The seven-channel assembler waits for the slower non-ECG channels.
        // Compare complete blocks at matching simulation times.
        Require(monitor.Count > 0 && monitor.All(block => expected.Single(candidate => candidate.StartSimTimeNs == block.StartSimTimeNs)
            .Planes.Single(plane => plane.ChannelId == ProjectedEcgDemoSource.ChannelId(EcgLead.II)).Samples
            .SequenceEqual(block.Planes.Single(plane => plane.ChannelId == PhysiologyDemoSource.ChannelId(0)).Samples)),
            "both acquisition paths agree on monitor II");
        foreach (var invalid in new[] { pair.Ecg with { Vt = true }, pair.Ecg with { Atrial = EcgAtrialIllustration.LeftAtrialAbnormality } })
        {
            bool rejected = false;
            try { ProjectedEcgDemoSource.Create(invalid); }
            catch (ArgumentException) { rejected = true; }
            Require(rejected, "unsupported morphology combinations reject");
        }
        var window = new DesignPreviewWindow();
        window.Show();
        try
        {
            window.Settings.EcgSelection = 180;
            window.RestartSettings();
            window.SelectPage(1);
            DesktopViewportSmokeChecks.Layout(window);
            Require(window.CurrentPaper is not null && window.Settings.AppliedEcgParameters.Text!.Contains("左胸", StringComparison.Ordinal),
                "applied template opens the twelve-lead paper with its positional summary");
            using var bitmap = new RenderTargetBitmap(new PixelSize(1440, 940), new Vector(96, 96));
            bitmap.Render((Visual)window.Content!);
            Directory.CreateDirectory("artifacts/native-smoke");
            bitmap.Save("artifacts/native-smoke/dextrocardia.png", PngBitmapEncoderOptions.Default);
            window.Settings.EcgSelection = 0;
            window.RestartSettings();
            Require(!DesignPreviewWindow.ResolveStyle(window.Settings.EcgSelection, 0, 0).Ecg.Dextrocardia,
                "returning to the sinus reference clears the anatomy selection");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
