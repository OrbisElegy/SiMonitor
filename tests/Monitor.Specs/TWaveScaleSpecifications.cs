// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class TWaveScaleSpecifications
{
    private static EcgTWaveScalePlan Scales(int chest) => new([1000, 1000, 1000, 1000, chest, chest, chest, chest, chest, chest]);
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(TScaleDefaultsAndComponentIsolation), TScaleDefaultsAndComponentIsolation),
        new(nameof(TScalePreservesProjectionAndIndependentTiming), TScalePreservesProjectionAndIndependentTiming),
        new(nameof(TScaleOwnsInputsAndRejectsInvalidBounds), TScaleOwnsInputsAndRejectsInvalidBounds),
        new(nameof(TScaleCheckpointRecoveryPreservesNativeBytes), TScaleCheckpointRecoveryPreservesNativeBytes),
    ];

    private static void TScaleDefaultsAndComponentIsolation()
    {
        var original = TextbookElectrodeReference.CreateElectrodes();
        foreach (int scale in new[] { -4000, -1000, 0, 1000, 4000 })
        {
            var actual = TextbookElectrodeReference.CreateElectrodes(tWave: Scales(scale));
            for (int electrode = 0; electrode < 10; electrode++)
            {
                for (int band = 0; band < 3; band++)
                {
                    var a = actual[electrode].Bands[band];
                    var b = original[electrode].Bands[band];
                    int factor = electrode < 4 || band != 2 ? 1 : scale / 1000;
                    Check.That(a.Trigger == b.Trigger && a.DelayNs == b.DelayNs && a.DurationNs == b.DurationNs &&
                        a.TableQ32.SequenceEqual(b.TableQ32.Select(value => value * factor)),
                        "T gains change only selected electrode amplitudes and preserve timing/P/QRS");
                }
            }
        }
    }

    private static void TScalePreservesProjectionAndIndependentTiming()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 10, 40, 60, 20, 20, 20]);
        var plan = Plan with { IndependentVentricularPeriodNs = 1_100_000_000, VentricularElectricalOffsetNs = 900_000_000, VentricularMechanicalOffsetNs = 980_000_000 };
        var normal = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(u)).GenerateBefore(2_800_000_000, 700, 100);
        var inverted = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(u, tWave: Scales(-1000))).GenerateBefore(2_800_000_000, 700, 100);
        bool differs = false;
        for (int i = 0; i < normal.Count; i++)
        {
            Check.That(normal[i].Tick == inverted[i].Tick && normal[i].MicrovoltValues.Take(6).SequenceEqual(inverted[i].MicrovoltValues.Take(6)), "limb leads and all sample ticks are unchanged");
            long time = normal[i].Tick.SimTimeNs;
            long age = time - 900_000_000;
            bool tSupport = age >= 0 && age % 1_100_000_000 >= 180_000_000 && age % 1_100_000_000 < 360_000_000;
            if (!tSupport) { Check.That(normal[i].MicrovoltValues.SequenceEqual(inverted[i].MicrovoltValues), "P/QRS/U outside T support are byte-identical"); }
            else { differs |= !normal[i].MicrovoltValues.SequenceEqual(inverted[i].MicrovoltValues); }
            var leads = inverted[i].ExactLeads;
            Check.That(leads[EcgLead.I].Numerator + leads[EcgLead.III].Numerator == leads[EcgLead.II].Numerator &&
                leads[EcgLead.AVR].Numerator + leads[EcgLead.AVL].Numerator + leads[EcgLead.AVF].Numerator == 0, "exact limb identities remain intact");
        }
        Check.That(differs, "T inversion is observable in sampled chest leads");
    }

    private static void TScaleOwnsInputsAndRejectsInvalidBounds()
    {
        int[] scales = [1000, 1000, 1000, 1000, -1000, 0, 2000, 1000, 1000, 1000];
        var electrodes = TextbookElectrodeReference.CreateElectrodes(tWave: new(scales));
        long[] before = electrodes[4].Bands[2].TableQ32.ToArray();
        scales[4] = 4000;
        Check.That(electrodes[4].Bands[2].TableQ32.SequenceEqual(before), "source owns T scale results independently of caller arrays");
        foreach (var invalid in new[] { new EcgTWaveScalePlan(null!), new EcgTWaveScalePlan([1000]), Scales(4001), Scales(-4001) })
        {
            bool rejected = false;
            try { TextbookElectrodeReference.CreateElectrodes(tWave: invalid); }
            catch (EventWaveformException e) { rejected = e.ReasonCode == "EcgTWave.InvalidScale"; }
            Check.That(rejected, "invalid T gains reject before source publication");
        }
    }

    private static void TScaleCheckpointRecoveryPreservesNativeBytes()
    {
        var electrodes = TextbookElectrodeReference.CreateElectrodes(tWave: Scales(-1250));
        var expected = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(1_600_000_000, 400, 100);
        var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes);
        List<ElectrodeSignalSample> actual = [];
        for (int step = 1; step <= 100; step++)
        {
            actual.AddRange(source.GenerateBefore(step * 16_000_000L, 4, 100));
            source = ElectrodeSignalGenerator.Restore(source.CaptureState());
        }
        Check.That(expected.Count == actual.Count && expected.Zip(actual).All(pair => pair.First.Tick == pair.Second.Tick && pair.First.MicrovoltValues.SequenceEqual(pair.Second.MicrovoltValues)),
            "fractional inverted T gain survives every-frame native recovery");
    }
}
