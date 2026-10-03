// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class QuinidineSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(QuinidineSeparatesLongQtFromProminentU), QuinidineSeparatesLongQtFromProminentU),
        new(nameof(QuinidineWideQrsRetainsEventsAndSeparatesQtFromU), QuinidineWideQrsRetainsEventsAndSeparatesQtFromU),
        new(nameof(QuinidineNotchChangesOnlyP), QuinidineNotchChangesOnlyP),
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
    private static void QuinidineWideQrsRetainsEventsAndSeparatesQtFromU()
    {
        foreach (var mode in new[] { QuinidineIllustration.WideQrsLowT, QuinidineIllustration.WideQrsInvertedT })
        {
            var timing = QuinidineEffectReference.ResolveTiming(mode);
            Check.That(timing.QrsDurationNs == 140_000_000 && timing.QtIntervalNs == 560_000_000 && QuinidineEffectReference.ResolveQuIntervalNs(mode) == 790_000_000, "authored wide QRS, longer QT and separate QU");
            var source = QuinidineEffectReference.CreateElectrodes(mode);
            var oldMode = mode == QuinidineIllustration.WideQrsLowT ? QuinidineIllustration.LowT : QuinidineIllustration.InvertedT;
            var baseline = QuinidineEffectReference.CreateElectrodes(oldMode);
            for (int i = 0; i < 10; i++)
            {
                Check.That(source[i].Bands[1].DurationNs == 140_000_000, "QRS support widens for every electrode");
                Check.That(source[i].Bands.Zip(baseline[i].Bands).All(p => p.First.TableQ32.SequenceEqual(p.Second.TableQ32)), "timing-only variant retains all component shapes and gains");
            }
            var plan = QuinidineEffectReference.CreatePlan(mode);
            Check.That(plan == QuinidineEffectReference.CreatePlan(oldMode), "no inferred event or mechanical impairment");
            var a = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, source).GenerateBefore(1_000_000_000, 250, 100);
            Check.That(a.Where(s => s.Tick.SimTimeNs is >= 288_000_000 and < 328_000_000).Any(s => s.MicrovoltValues.Any(v => v != 0)), "wide QRS has actual late samples");
            Check.That(a.Where(s => s.Tick.SimTimeNs is >= 340_000_000 and < 580_000_000).All(s => s.MicrovoltValues.All(v => v == 0)), "ST gap follows widened QRS");
            var t = a.Where(s => s.Tick.SimTimeNs is >= 580_000_000 and < 760_000_000).ToArray();
            Check.That(t.Max(s => Math.Abs(s.MicrovoltValues[1])) is >= 48 and <= 49 && t.All(s => mode == QuinidineIllustration.WideQrsLowT ? s.MicrovoltValues[1] >= 0 : s.MicrovoltValues[1] <= 0), "longer QT retains chosen low T polarity");
            Check.That(a.Where(s => s.Tick.SimTimeNs is >= 760_000_000 and < 790_000_000).All(s => s.MicrovoltValues.All(v => v == 0)), "T ends before U onset");
            Check.That(a.Where(s => s.Tick.SimTimeNs is >= 790_000_000 and < 990_000_000).Max(s => s.MicrovoltValues[7]) > 295, "prominent U follows longer QT");
            Check.That(a.Where(s => s.Tick.SimTimeNs >= 992_000_000).All(s => s.MicrovoltValues.All(v => v == 0)), "U ends before next atrial cycle");
        }
    }
    private static void QuinidineNotchChangesOnlyP()
    {
        foreach (var mode in Enum.GetValues<QuinidineIllustration>().Where(m => m != QuinidineIllustration.Reference))
        {
            var normal = QuinidineEffectReference.CreateElectrodes(mode);
            var notched = QuinidineEffectReference.CreateElectrodes(mode, true);
            for (int i = 0; i < 10; i++)
            {
                Check.That(normal[i].Bands.Count == notched[i].Bands.Count, "no duplicate atrial component");
                Check.That(normal[i].Bands[0] with { TableQ32 = notched[i].Bands[0].TableQ32 } == notched[i].Bands[0], "P timing and event binding unchanged");
                Check.That(normal[i].Bands.Skip(1).Zip(notched[i].Bands.Skip(1)).All(p => p.First.TableQ32.SequenceEqual(p.Second.TableQ32) && p.First with { TableQ32 = p.Second.TableQ32 } == p.Second), "QRS/T/U completely unchanged");
            }
            var plan = QuinidineEffectReference.CreatePlan(mode);
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
        foreach (var mode in new[] { QuinidineIllustration.Reference, (QuinidineIllustration)99 })
        {
            try { QuinidineEffectReference.CreateElectrodes(mode); }
            catch (EventWaveformException e) when (e.ReasonCode == "Quinidine.InvalidMode") { continue; }
            throw new InvalidOperationException("Unsupported source mode accepted");
        }
    }

}
