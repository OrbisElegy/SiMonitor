// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class QuinidineSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(QuinidineSeparatesLongQtFromProminentU), QuinidineSeparatesLongQtFromProminentU),
        new(nameof(QuinidineNotchChangesOnlyP), QuinidineNotchChangesOnlyP),
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
    private static void QuinidineNotchChangesOnlyP()
    {
        foreach (var mode in new[] { QuinidineIllustration.LowT, QuinidineIllustration.InvertedT })
        {
            var normal = QuinidineEffectReference.CreateElectrodes(mode);
            var notched = QuinidineEffectReference.CreateElectrodes(mode, true);
            for (int i = 0; i < 10; i++)
            {
                Check.That(normal[i].Bands.Count == notched[i].Bands.Count, "no duplicate atrial component");
                Check.That(normal[i].Bands[0] with { TableQ32 = notched[i].Bands[0].TableQ32 } == notched[i].Bands[0], "P timing and event binding unchanged");
                Check.That(normal[i].Bands.Skip(1).Zip(notched[i].Bands.Skip(1)).All(p => p.First.TableQ32.SequenceEqual(p.Second.TableQ32) && p.First with { TableQ32 = p.Second.TableQ32 } == p.Second), "QRS/T/U completely unchanged");
            }
            var plan = QuinidineEffectReference.CreatePlan();
            var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, notched).GenerateBefore(1_000_000_000, 250, 100);
            var baseline = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, normal).GenerateBefore(1_000_000_000, 250, 100);
            int At(long time, int lead) => a.Single(s => s.Tick.SimTimeNs == time).MicrovoltValues[lead];
            Check.That(At(36_000_000, 1) > At(60_000_000, 1) + 20 && At(84_000_000, 1) > At(60_000_000, 1) + 20 && At(60_000_000, 1) > 0, "rounded positive peaks separated by shallow notch");
            Check.That(At(36_000_000, 3) < At(60_000_000, 3) && At(84_000_000, 3) < At(60_000_000, 3), "aVR polarity follows electrode projection");
            Check.That(a.Zip(baseline).Where(p => p.First.Tick.SimTimeNs >= 120_000_000).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "P-only sample differences");
            Check.That(a.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "notched limb identity");
            var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, QuinidineEffectReference.CreateLeadIIBands(mode, true)).GenerateBefore(1_000_000_000, 250, 100);
            Check.That(a.Zip(ii).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "notched monitorII parity");
        }
    }
    private static void QuinidineRestoresAndRejectsUnknownModes()
    {
        foreach (var mode in new[] { QuinidineIllustration.LowT, QuinidineIllustration.InvertedT })
            foreach (bool notchedP in new[] { false, true })
                foreach (long boundary in new[] { 38_000_000L, 60_000_000, 82_000_000, 118_000_000, 198_000_000, 678_000_000, 708_000_000, 798_000_000, 910_000_000 })
                {
                    var source = ElectrodeSignalGenerator.Start(QuinidineEffectReference.CreatePlan(), "AcqECGMonitor250@1", 1, QuinidineEffectReference.CreateElectrodes(mode, notchedP));
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
