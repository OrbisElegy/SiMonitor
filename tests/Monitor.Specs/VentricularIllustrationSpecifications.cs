// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class VentricularIllustrationSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(LeftVentricularExampleMeetsProjectedConstraints), LeftVentricularExampleMeetsProjectedConstraints),
        new(nameof(VentricularExamplePreservesAtrialAndUAndRecovers), VentricularExamplePreservesAtrialAndUAndRecovers),
        new(nameof(VentricularExampleRejectsConflictsAndInvalidTiming), VentricularExampleRejectsConflictsAndInvalidTiming),
    ];
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
        var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes(u, atrial: EcgAtrialIllustration.BiatrialAbnormality,
                ventricular: EcgVentricularIllustration.LeftHypertrophyWithStrain));
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
    private static void VentricularExampleRejectsConflictsAndInvalidTiming()
    {
        void Reject(Action action, string reason)
        {
            try { action(); }
            catch (EventWaveformException e) { Check.That(e.ReasonCode == reason, "stable ventricular reason"); return; }
            throw new InvalidOperationException("Invalid ventricular illustration accepted.");
        }
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
