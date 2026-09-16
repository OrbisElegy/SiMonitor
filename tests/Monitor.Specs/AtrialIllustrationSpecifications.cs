// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class AtrialIllustrationSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(RightAndBiatrialShapesMeetProjectedConstraints), RightAndBiatrialShapesMeetProjectedConstraints),
        new(nameof(PComponentSeparationValidatesAndPreservesDefaults), PComponentSeparationValidatesAndPreservesDefaults),
        new(nameof(LeftAtrialIllustrationMeetsProjectedConstraints), LeftAtrialIllustrationMeetsProjectedConstraints),
        new(nameof(AtrialIllustrationPreservesVentriclesAndRecovery), AtrialIllustrationPreservesVentriclesAndRecovery),
        new(nameof(AtrialIllustrationRejectsIncompatibleInputs), AtrialIllustrationRejectsIncompatibleInputs),
    ];

    private static void RightAndBiatrialShapesMeetProjectedConstraints()
    {
        foreach (var kind in new[] { EcgAtrialIllustration.RightAtrialAbnormality, EcgAtrialIllustration.BiatrialAbnormality })
        {
            var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(atrial: kind)).GenerateBefore(800_000_000, 200, 100);
            int[] P(EcgLead lead) => samples.Take(36).Select(x => (int)x.MicrovoltValues[(int)lead]).ToArray();
            var ii = P(EcgLead.II);
            var v1 = P(EcgLead.V1);
            Check.That(ii.Max() >= 250, "inferior P reaches at least 0.25mV after acquisition");
            if (kind == EcgAtrialIllustration.RightAtrialAbnormality)
            {
                Check.That(P(EcgLead.III).Max() >= 250 && P(EcgLead.AVF).Max() >= 250 && v1.Max() >= 150 && v1.Min() == 0,
                    "right atrial example has tall inferior P and upright V1");
                Check.That(P(EcgLead.I).All(x => x == 0) && ii.SequenceEqual(P(EcgLead.III)), "fixed P axis is +90 degrees before acquisition wiring");
                Check.That(ii.Skip(25).All(x => x == 0) && ii[1] > 0 && ii[24] > 0, "right P retains 100ms support");
                Check.That(ii.Take(13).Zip(ii.Skip(1).Take(13)).All(x => x.First <= x.Second) &&
                    ii.Skip(13).Zip(ii.Skip(14)).All(x => x.First >= x.Second), "right P has one peak, no double notch");
            }
            else
            {
                Check.That(ii[2] > 0 && ii[33] > 0 && ii[35] == 0, "biatrial P is wider than 120ms");
                Check.That(v1.Max() > 200 && v1.Min() < -150, "biatrial V1 has enlarged positive and negative lobes");
            }
            foreach (var sample in samples)
            {
                var leads = sample.ExactLeads;
                Check.That(leads[EcgLead.I].Numerator + leads[EcgLead.III].Numerator == leads[EcgLead.II].Numerator, "atrial projection identity");
            }
        }
    }

    private static void PComponentSeparationValidatesAndPreservesDefaults()
    {
        var pairs = Enumerable.Repeat<EcgPWaveComponents?>(new(100, -50), 10).ToArray();
        foreach (int separation in new[] { -1, 500, int.MaxValue })
        {
            try { TextbookElectrodeReference.CreateElectrodes(pWave: new(new EcgPWaveComponents?[10], separation)); }
            catch (EventWaveformException e) { Check.That(e.ReasonCode == "EcgPWave.InvalidPlan", "inactive separation validates"); continue; }
            throw new InvalidOperationException("Invalid separation accepted.");
        }
        var original = TextbookElectrodeReference.CreateElectrodes(pWave: new(pairs));
        var explicitDefault = TextbookElectrodeReference.CreateElectrodes(pWave: new(pairs, 375));
        Check.That(original.Zip(explicitDefault).All(x => x.First.Bands.Zip(x.Second.Bands).All(b =>
            b.First.DelayNs == b.Second.DelayNs && b.First.DurationNs == b.Second.DurationNs && b.First.TableQ32.SequenceEqual(b.Second.TableQ32))), "default separation remains byte-equivalent");
        foreach (int separation in new[] { 0, 1, 499 })
        { TextbookElectrodeReference.CreateElectrodes(pWave: new(pairs, separation)); }
    }

    private static void LeftAtrialIllustrationMeetsProjectedConstraints()
    {
        var electrodes = TextbookElectrodeReference.CreateElectrodes(atrial: EcgAtrialIllustration.LeftAtrialAbnormality);
        var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        foreach (var lead in new[] { EcgLead.I, EcgLead.II, EcgLead.AVL })
        {
            var p = samples.Take(36).Select(x => (int)x.MicrovoltValues[(int)lead]).ToArray();
            int first = Enumerable.Range(0, 18).MaxBy(i => p[i]);
            int second = Enumerable.Range(18, 18).MaxBy(i => p[i]);
            Check.That((second - first) * 4 >= 40 && p[17] < Math.Min(p[first], p[second]), "projected P has two peaks separated by at least 40 ms");
            Check.That(p[2] > 0 && p[33] > 0 && p[0] == 0 && p[35] == 0, "P spans more than 120 ms and returns to baseline at 140 ms");
        }
        var v1 = samples.Take(36).Select(x => (int)x.MicrovoltValues[(int)EcgLead.V1]).ToArray();
        int negativeMs = v1.Count(x => x < 0) * 4;
        // uV * ms / 100000 = mm*s at 10 mm/mV. Conservative sampled duration.
        Check.That(v1.Take(17).Max() > 0 && -v1.Min() * negativeMs >= 4000, "V1 positive onset and terminal negative force >=0.04 mm*s");
        foreach (var sample in samples)
        {
            var p = sample.ExactLeads;
            Check.That(p[EcgLead.I].Numerator + p[EcgLead.III].Numerator == p[EcgLead.II].Numerator, "limb projection stays exact");
        }
    }

    private static void AtrialIllustrationPreservesVentriclesAndRecovery()
    {
        var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(1_600_000_000, 400, 100);
        foreach (var kind in new[] { EcgAtrialIllustration.LeftAtrialAbnormality, EcgAtrialIllustration.RightAtrialAbnormality, EcgAtrialIllustration.BiatrialAbnormality })
        {
            var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(atrial: kind));
            var first = source.GenerateBefore(400_000_000, 100, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var tail = source.GenerateBefore(1_600_000_000, 300, 100);
            var recovered = restored.GenerateBefore(1_600_000_000, 300, 100);
            Check.That(tail.Zip(recovered).All(x => x.First.MicrovoltValues.SequenceEqual(x.Second.MicrovoltValues)), "restore retains all projected samples");
            var all = first.Concat(tail).ToArray();
            for (int i = 0; i < all.Length; i++)
            {
                if (i % 200 >= 35)
                { Check.That(all[i].MicrovoltValues.SequenceEqual(baseline[i].MicrovoltValues), "all ventricular samples and PR baseline remain unchanged"); }
            }
        }

    }

    private static void AtrialIllustrationRejectsIncompatibleInputs()
    {
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (EventWaveformException e) { Check.That(e.ReasonCode == reason, "stable rejection reason"); return; }
            throw new InvalidOperationException("Invalid atrial illustration accepted.");
        }
        foreach (var kind in new[] { EcgAtrialIllustration.RightAtrialAbnormality, EcgAtrialIllustration.BiatrialAbnormality })
        {
            Reject(() => TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { PDurationNs = 120_000_000 }, atrial: kind), "EcgAtrial.InvalidTiming");
            Reject(() => TextbookElectrodeReference.CreateElectrodes(pWave: new(new EcgPWaveComponents?[10]), atrial: kind), "EcgAtrial.ConflictingModes");
        }
        Reject(() => TextbookElectrodeReference.CreateElectrodes(atrial: (EcgAtrialIllustration)99), "EcgAtrial.InvalidIllustration");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing, atrial: EcgAtrialIllustration.LeftAtrialAbnormality), "EcgAtrial.InvalidTiming");
        var boundary = TextbookEcgReference.Timing with { PDurationNs = 140_000_000, PrIntervalNs = 227_500_000 };
        Reject(() => TextbookElectrodeReference.CreateElectrodes(timing: boundary, atrial: EcgAtrialIllustration.LeftAtrialAbnormality), "EcgAtrial.InvalidTiming");
        TextbookElectrodeReference.CreateElectrodes(timing: boundary with { PrIntervalNs = 227_499_999 }, atrial: EcgAtrialIllustration.LeftAtrialAbnormality);
        Reject(() => TextbookElectrodeReference.CreateElectrodes(pWave: new(new EcgPWaveComponents?[10]), atrial: EcgAtrialIllustration.LeftAtrialAbnormality), "EcgAtrial.ConflictingModes");
    }
}
