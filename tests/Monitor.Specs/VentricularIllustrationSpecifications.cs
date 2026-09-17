// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VentricularIllustrationSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(RightVentricularExampleMeetsProjectedConstraints), RightVentricularExampleMeetsProjectedConstraints),
        new(nameof(BiventricularExampleCombinesSignsWithoutSumming), BiventricularExampleCombinesSignsWithoutSumming),
        new(nameof(RightVentricularVariantsMeetProjectedConstraints), RightVentricularVariantsMeetProjectedConstraints),
        new(nameof(LeftVentricularExampleMeetsProjectedConstraints), LeftVentricularExampleMeetsProjectedConstraints),
        new(nameof(VentricularExamplePreservesAtrialAndUAndRecovers), VentricularExamplePreservesAtrialAndUAndRecovers),
        new(nameof(VentricularExampleRejectsConflictsAndInvalidTiming), VentricularExampleRejectsConflictsAndInvalidTiming),
    ];
    private static void RightVentricularExampleMeetsProjectedConstraints()
    {
        var electrodes = TextbookElectrodeReference.CreateElectrodes(ventricular: EcgVentricularIllustration.RightHypertrophyWithStrain);
        var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        int[] Qrs(EcgLead lead) => samples.Skip(40).Take(20).Select(s => (int)s.MicrovoltValues[(int)lead]).ToArray();
        Check.That(electrodes.All(e => e.Bands[1].DurationNs == 80_000_000), "right QRS is 80ms");
        Check.That(Qrs(EcgLead.V1).Max() >= -Qrs(EcgLead.V1).Min() && Qrs(EcgLead.V5).Max() <= -Qrs(EcgLead.V5).Min(), "V1 Rs and V5 rS");
        Check.That(Qrs(EcgLead.V1).Max() - Qrs(EcgLead.V5).Min() > 1050 && Qrs(EcgLead.AVR).Max() > 500 && Qrs(EcgLead.AVR).Max() > -Qrs(EcgLead.AVR).Min(), "right chest and aVR voltage criteria");
        // Signed QRS areas: I<0 and II>0 constrain the frontal axis to 90..150 degrees.
        Check.That(Qrs(EcgLead.I).Sum() < 0 && Qrs(EcgLead.II).Sum() > 0 && Qrs(EcgLead.AVF).Sum() > 0, "rightward integrated QRS axis");
        foreach (var lead in new[] { EcgLead.V1, EcgLead.V2 })
        {
            Check.That(samples[70].MicrovoltValues[(int)lead] < -50 && samples[80].MicrovoltValues[(int)lead] < samples[70].MicrovoltValues[(int)lead], "right chest ST depression");
            Check.That(samples.Skip(90).Take(40).Min(s => s.MicrovoltValues[(int)lead]) < -200, "right chest T inversion");
        }
        foreach (var sample in samples)
        { Check.That(sample.ExactLeads[EcgLead.I].Numerator + sample.ExactLeads[EcgLead.III].Numerator == sample.ExactLeads[EcgLead.II].Numerator, "right projection identity"); }
    }
    private static void BiventricularExampleCombinesSignsWithoutSumming()
    {
        var electrodes = TextbookElectrodeReference.CreateElectrodes(ventricular: EcgVentricularIllustration.BiventricularCombinedSigns);
        var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        int[] Qrs(EcgLead lead) => samples.Skip(40).Take(25).Select(s => (int)s.MicrovoltValues[(int)lead]).ToArray();
        Check.That(Qrs(EcgLead.V1).Max() > -Qrs(EcgLead.V1).Min() && Qrs(EcgLead.V5).Max() > -Qrs(EcgLead.V5).Min() && Qrs(EcgLead.V5).Max() > 2500, "V1 and high V5 both R-dominant");
        Check.That(Qrs(EcgLead.I).Sum() < 0 && Qrs(EcgLead.II).Sum() > 0, "rightward QRS area axis remains");
        for (int i = 65; i < samples.Count; i++)
        { Check.That(samples[i].MicrovoltValues.SequenceEqual(baseline[i].MicrovoltValues), "combined example retains reference ST/T"); }
        var left = TextbookElectrodeReference.CreateElectrodes(ventricular: EcgVentricularIllustration.LeftHypertrophyWithStrain);
        var right = TextbookElectrodeReference.CreateElectrodes(ventricular: EcgVentricularIllustration.RightHypertrophyWithStrain);
        Check.That(!electrodes[8].Bands[1].TableQ32.SequenceEqual(left[8].Bands[1].TableQ32.Zip(right[8].Bands[1].TableQ32, (l, r) => l + r)), "independent authored target is not LVH plus RVH");
        foreach (var sample in samples)
        { Check.That(sample.ExactLeads[EcgLead.I].Numerator + sample.ExactLeads[EcgLead.III].Numerator == sample.ExactLeads[EcgLead.II].Numerator, "combined electrode projection identity"); }
    }
    private static void RightVentricularVariantsMeetProjectedConstraints()
    {
        foreach (var mode in new[] { EcgVentricularIllustration.SevereRightQr, EcgVentricularIllustration.PulmonaryHeartSigns })
        {
            var electrodes = TextbookElectrodeReference.CreateElectrodes(ventricular: mode);
            var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
            int[] Qrs(EcgLead lead) => samples.Skip(40).Take(20).Select(s => (int)s.MicrovoltValues[(int)lead]).ToArray();
            Check.That(Qrs(EcgLead.I).Sum() < 0 && Qrs(EcgLead.II).Sum() > 0, "variant QRS area axis is rightward");
            if (mode == EcgVentricularIllustration.SevereRightQr)
            {
                int[] v1 = Qrs(EcgLead.V1);
                Check.That(v1.Take(4).Min() < -100 && v1.Skip(4).Max() > 1300 && v1.Max() - Qrs(EcgLead.V5).Min() > 1200, "initial q then tall R with severe voltage combination");
                Check.That(-Qrs(EcgLead.I).Sum() > Qrs(EcgLead.AVF).Sum() && Qrs(EcgLead.AVF).Sum() > 0, "conservative right-axis bound exceeds 110 degrees");
                foreach (var lead in new[] { EcgLead.V1, EcgLead.V2 })
                { Check.That(samples[70].MicrovoltValues[(int)lead] < -50 && samples.Skip(90).Take(40).Min(s => s.MicrovoltValues[(int)lead]) < -200, "qR variant retains right chest ST/T changes"); }
            }
            else
            {
                foreach (var lead in new[] { EcgLead.V1, EcgLead.V2, EcgLead.V3, EcgLead.V4, EcgLead.V5, EcgLead.V6 })
                { Check.That(Qrs(lead).Max() > 0 && Qrs(lead).Max() < -Qrs(lead).Min(), "all six chest leads remain rS"); }
                Check.That(Qrs(EcgLead.I).Max() - Qrs(EcgLead.I).Min() < 500, "low-voltage I, not a global low-voltage assertion");
                var reference = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
                Check.That(samples.Skip(60).Zip(reference.Skip(60)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "pulmonary-heart illustration retains reference ST/T");
            }
            foreach (var sample in samples)
            { Check.That(sample.ExactLeads[EcgLead.I].Numerator + sample.ExactLeads[EcgLead.III].Numerator == sample.ExactLeads[EcgLead.II].Numerator, "variant projection identity"); }
        }
    }
    private static void LeftVentricularExampleMeetsProjectedConstraints()
    {
        var electrodes = TextbookElectrodeReference.CreateElectrodes(ventricular: EcgVentricularIllustration.LeftHypertrophyWithStrain);
        Check.That(electrodes.All(e => e.Bands[1].DurationNs == 100_000_000), "QRS authored duration is 100ms");
        var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        int[] Qrs(EcgLead lead) => samples.Skip(40).Take(26).Select(x => (int)x.MicrovoltValues[(int)lead]).ToArray();
        Check.That(Qrs(EcgLead.V5).Max() > 2500 && Qrs(EcgLead.V6).Max() > 2500 &&
            Qrs(EcgLead.V5).Max() - Qrs(EcgLead.V1).Min() > 4000, "chest voltage criteria hold after acquisition");
        Check.That(Qrs(EcgLead.I).Max() > 1500 && Qrs(EcgLead.V1).Min() < -2000 && Qrs(EcgLead.V2).Min() < -2000,
            "high I R and deep right chest S");
        foreach (var lead in new[] { EcgLead.I, EcgLead.AVL, EcgLead.V5, EcgLead.V6 })
        {
            int At(int ms) => samples[ms / 4].MicrovoltValues[(int)lead];
            Check.That(At(280) < -50 && At(360) < At(280), "lateral ST descends below -0.05mV");
            Check.That(samples.Skip(95).Take(45).Min(x => x.MicrovoltValues[(int)lead]) < -200, "lateral T is inverted");
        }
        Check.That(samples.Skip(95).Take(45).Max(x => x.MicrovoltValues[(int)EcgLead.V1]) > 250, "S-dominant V1 retains upright T");
        foreach (var sample in samples)
        {
            var p = sample.ExactLeads;
            Check.That(p[EcgLead.I].Numerator + p[EcgLead.III].Numerator == p[EcgLead.II].Numerator, "Einthoven relation stays exact");
        }
    }
    private static void VentricularExamplePreservesAtrialAndUAndRecovers()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 20, 40, 60, 20, 20, 20]);
        var reference = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes(u, atrial: EcgAtrialIllustration.BiatrialAbnormality)).GenerateBefore(1_600_000_000, 400, 100);
        foreach (var mode in new[] { EcgVentricularIllustration.LeftHypertrophyWithStrain, EcgVentricularIllustration.RightHypertrophyWithStrain, EcgVentricularIllustration.BiventricularCombinedSigns, EcgVentricularIllustration.SevereRightQr, EcgVentricularIllustration.PulmonaryHeartSigns })
        {
            var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(u, atrial: EcgAtrialIllustration.BiatrialAbnormality,
                    ventricular: mode));
            var first = source.GenerateBefore(400_000_000, 100, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var tail = source.GenerateBefore(1_600_000_000, 300, 100);
            var recovered = restored.GenerateBefore(1_600_000_000, 300, 100);
            Check.That(tail.Zip(recovered).All(x => x.First.MicrovoltValues.SequenceEqual(x.Second.MicrovoltValues)), "all projected samples recover");
            var all = first.Concat(tail).ToArray();
            for (int i = 0; i < all.Length; i++)
            {
                if (i % 200 < 40 || i % 200 >= 140)
                { Check.That(all[i].MicrovoltValues.SequenceEqual(reference[i].MicrovoltValues), "P, PR baseline, U and diastolic samples stay unchanged"); }
            }
        }
    }
    private static void VentricularExampleRejectsConflictsAndInvalidTiming()
    {
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (EventWaveformException e) { Check.That(e.ReasonCode == reason, "stable ventricular reason"); return; }
            throw new InvalidOperationException("Invalid ventricular illustration accepted.");
        }
        foreach (var variant in new[] { EcgVentricularIllustration.SevereRightQr, EcgVentricularIllustration.PulmonaryHeartSigns })
        {
            Reject(() => TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { QrsDurationNs = 100_000_000 }, ventricular: variant), "EcgVentricular.InvalidTiming");
            Reject(() => TextbookElectrodeReference.CreateElectrodes(tWave: new(new int[10]), ventricular: variant), "EcgVentricular.ConflictingModes");
        }
        TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { TDurationNs = 280_000_000 }, ventricular: EcgVentricularIllustration.PulmonaryHeartSigns);
        var combined = EcgVentricularIllustration.BiventricularCombinedSigns;
        Reject(() => TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing, ventricular: combined), "EcgVentricular.InvalidTiming");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(stSegment: new(new int[10], new int[10]), ventricular: combined), "EcgVentricular.ConflictingModes");
        TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { QrsDurationNs = 100_000_000, TDurationNs = 260_000_000 }, ventricular: combined);
        var right = EcgVentricularIllustration.RightHypertrophyWithStrain;
        Reject(() => TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { QrsDurationNs = 100_000_000 }, ventricular: right), "EcgVentricular.InvalidTiming");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(tWave: new(new int[10]), ventricular: right), "EcgVentricular.ConflictingModes");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { TDurationNs = 280_000_000 }, ventricular: right), "EcgVentricular.InvalidTiming");
        var mode = EcgVentricularIllustration.LeftHypertrophyWithStrain;
        Reject(() => TextbookElectrodeReference.CreateElectrodes(ventricular: (EcgVentricularIllustration)99), "EcgVentricular.InvalidIllustration");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing, ventricular: mode), "EcgVentricular.InvalidTiming");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { QrsDurationNs = 100_000_000, TDurationNs = 260_000_000 }, ventricular: mode), "EcgVentricular.InvalidTiming");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(tWave: new(new int[10]), ventricular: mode), "EcgVentricular.ConflictingModes");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(stSegment: new(new int[10], new int[10]), ventricular: mode), "EcgVentricular.ConflictingModes");
        Reject(() => TextbookElectrodeReference.CreateElectrodes(infarction: new(0, InfarctionIllustrationStage.None), ventricular: mode), "EcgVentricular.ConflictingModes");
        TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { QrsDurationNs = 100_000_000, TDurationNs = 259_999_999 }, ventricular: mode);
    }
}
