// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class QuinidineSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(QuinidineSeparatesLongQtFromProminentU), QuinidineSeparatesLongQtFromProminentU),
        new(nameof(QuinidineRestoresAndRejectsUnknownModes), QuinidineRestoresAndRejectsUnknownModes),
    ];
    private static void QuinidineSeparatesLongQtFromProminentU()
    {
        var plan = QuinidineEffectReference.CreatePlan();
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(1_000_000_000, 20);
        Check.That(events.Single(e => e.Kind == PhysiologyCycleEventKind.VentricularElectrical).SimTimeNs == 200_000_000 && events.Single(e => e.Kind == PhysiologyCycleEventKind.VentricularMechanical).SimTimeNs == 280_000_000, "longer PR moves shared events with fixed authored mechanical offset");
        var normal = TextbookElectrodeReference.CreateElectrodes(timing: QuinidineEffectReference.Timing);
        foreach (var mode in new[] { QuinidineIllustration.LowT, QuinidineIllustration.InvertedT })
        {
            var source = QuinidineEffectReference.CreateElectrodes(mode);
            for (int i = 0; i < 10; i++)
            {
                Check.That(source[i].Bands[0].DurationNs == 120_000_000 && source[i].Bands[1].DurationNs == 80_000_000, "slightly broader P, original narrow QRS");
                Check.That(source[i].Bands[0].TableQ32.SequenceEqual(normal[i].Bands[0].TableQ32) && source[i].Bands[1].TableQ32.SequenceEqual(normal[i].Bands[1].TableQ32), "reference P/QRS topology");
            }
            var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, source).GenerateBefore(1_000_000_000, 250, 100);
            var t = a.Where(s => s.Tick.SimTimeNs is >= 500_000_000 and < 680_000_000).ToArray();
            Check.That(t.Max(s => Math.Abs(s.MicrovoltValues[1])) is >= 48 and <= 49, "low T amplitude");
            Check.That(t.All(s => mode == QuinidineIllustration.LowT ? s.MicrovoltValues[1] >= 0 : s.MicrovoltValues[1] <= 0), "explicit T polarity");
            Check.That(a.Where(s => s.Tick.SimTimeNs is >= 280_000_000 and < 500_000_000).All(s => s.MicrovoltValues.All(v => v == 0)), "no unrequested ST depression");
            Check.That(a.Where(s => s.Tick.SimTimeNs is >= 680_000_000 and < 710_000_000).All(s => s.MicrovoltValues.All(v => v == 0)), "QT480 ends before U; not QU710");
            Check.That(a.Where(s => s.Tick.SimTimeNs is >= 710_000_000 and < 910_000_000).Max(s => s.MicrovoltValues[7]) > 295, "prominent upright V2 U independent of T direction");
            Check.That(a.Where(s => s.Tick.SimTimeNs >= 912_000_000).All(s => s.MicrovoltValues.All(v => v == 0)), "QU endpoint before next P");
            Check.That(a.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity");
            var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, QuinidineEffectReference.CreateLeadIIBands(mode)).GenerateBefore(1_000_000_000, 250, 100);
            Check.That(a.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitorII parity");
        }
    }
    private static void QuinidineRestoresAndRejectsUnknownModes()
    {
        foreach (var mode in new[] { QuinidineIllustration.LowT, QuinidineIllustration.InvertedT })
            foreach (long boundary in new[] { 118_000_000L, 198_000_000, 678_000_000, 708_000_000, 798_000_000, 910_000_000 })
            {
                var source = ElectrodeSignalGenerator.Start(QuinidineEffectReference.CreatePlan(), "AcqECGMonitor250@1", 1, QuinidineEffectReference.CreateElectrodes(mode));
                source.GenerateBefore(boundary, 250, 100);
                var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                var a = source.GenerateBefore(2_000_000_000, 500, 100);
                var b = restored.GenerateBefore(2_000_000_000, 500, 100);
                Check.That(a.Count == b.Count && a.Zip(b).All(p => p.First.Tick == p.Second.Tick && p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "long QT/U recovery exact");
            }
        foreach (var mode in new[] { QuinidineIllustration.Reference, (QuinidineIllustration)99 })
        {
            try { QuinidineEffectReference.CreateElectrodes(mode); }
            catch (EventWaveformException e) when (e.ReasonCode == "Quinidine.InvalidMode") { continue; }
            throw new InvalidOperationException("Unsupported source mode accepted");
        }
    }
}
