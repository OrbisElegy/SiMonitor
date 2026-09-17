// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class RightBundleBlockSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(RightBundleBlockHasDelayedRightAndLateralTerminalForces), RightBundleBlockHasDelayedRightAndLateralTerminalForces),
        new(nameof(RightBundleBlockRetainsConductionAndRecovers), RightBundleBlockRetainsConductionAndRecovers),
    ];
    private static void RightBundleBlockHasDelayedRightAndLateralTerminalForces()
    {
        var plan = RightBundleBlockReference.CreatePlan();
        var electrodes = RightBundleBlockReference.CreateElectrodes();
        Check.That(electrodes.All(e => e.Bands[1].DurationNs == 140_000_000), "QRS140ms support");
        var baseline = TextbookElectrodeReference.CreateElectrodes(timing: RightBundleBlockReference.Timing);
        Check.That(electrodes.Zip(baseline).All(p => p.First.Bands[0].TableQ32.SequenceEqual(p.Second.Bands[0].TableQ32)), "P morphology unchanged");
        var samples = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, electrodes).GenerateBefore(800_000_000, 200, 100);
        int V(int index, EcgLead lead) => samples[index].MicrovoltValues[(int)lead];
        foreach (var lead in new[] { EcgLead.V1, EcgLead.V2 })
        {
            Check.That(V(46, lead) > 100 && V(52, lead) < -300 && V(62, lead) > 800, "early r, S and delayed dominant R-prime");
            Check.That(V(82, lead) < -40 && V(120, lead) < -150, "right chest ST depression and inverted T");
        }
        foreach (var lead in new[] { EcgLead.I, EcgLead.V5, EcgLead.V6 })
        {
            Check.That(V(47, lead) > 700 && Enumerable.Range(55, 17).All(i => V(i, lead) < -20), "initial R and wide terminal S exceeding40ms");
            Check.That(V(120, lead) > 100, "lateral upright T opposes terminal S");
        }
        Check.That(Enumerable.Range(41, 11).All(i => V(i, EcgLead.AVR) < 0) && V(47, EcgLead.AVR) < -700 && V(68, EcgLead.AVR) > 200, "aVR QR terminal positive force");
        Check.That(samples.All(s => Math.Abs(s.MicrovoltValues[0] + s.MicrovoltValues[2] - s.MicrovoltValues[1]) <= 1), "limb identity");
        var monitor = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, RightBundleBlockReference.CreateLeadIIBands()).GenerateBefore(800_000_000, 200, 100);
        Check.That(samples.Zip(monitor).All(p => Math.Abs(p.First.MicrovoltValues[1] - p.Second.NormalizedValue) <= 1), "monitor II shares projected source");
    }
    private static void RightBundleBlockRetainsConductionAndRecovers() => VerifyConductionAndRecovery(false);

    internal static void VerifyConductionAndRecovery(bool left)
    {
        var plan = left ? LeftBundleBlockReference.CreatePlan() : RightBundleBlockReference.CreatePlan();
        var narrow = plan with { ConductionPattern = AvConductionPattern.MobitzTwoFourToThreeIllustration };
        var events = RegularPhysiologyTimeline.Start(plan).AdvanceBefore(6_400_000_000, 100);
        Check.That(events.SequenceEqual(RegularPhysiologyTimeline.Start(narrow).AdvanceBefore(6_400_000_000, 100)), "wide morphology leaves fixed PR and dropped mechanical events unchanged");
        var source = ElectrodeSignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, left ? LeftBundleBlockReference.CreateElectrodes() : RightBundleBlockReference.CreateElectrodes());
        source.GenerateBefore(2_400_000_000, 600, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        var samples = source.GenerateBefore(4_000_000_000, 400, 100);
        Check.That(samples.Zip(restored.GenerateBefore(4_000_000_000, 400, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "restore through dropped QRS and resumed group");
        Check.That(samples.Skip(40).Take(100).All(s => s.MicrovoltValues.All(v => v == 0)), "dropped QRS/ST/T leave no residual ventricular morphology");
        var pressurePlan = new VascularPressurePlan(80_000_000, 240_000_000, 2_900_000_000, 8000, 1000, 30000);
        var widePressure = VascularPressureSource.Create(plan, pressurePlan);
        var narrowPressure = VascularPressureSource.Create(narrow, pressurePlan);
        for (long t = 0; t < 6_400_000_000; t += 40_000_000)
        { Check.That(widePressure.EvaluateAt(t) == narrowPressure.EvaluateAt(t), "no invented morphology-to-perfusion feedback"); }
        foreach (var invalid in new[] { plan with { ConductedBeatsPerGroup = 2 }, plan with { VentricularElectricalOffsetNs = 200_000_000 }, plan with { CardiacActivity = CardiacActivity.VentricularOnly } })
        {
            try { RegularPhysiologyTimeline.Start(invalid); }
            catch (PhysiologyTimelineException) { continue; }
            throw new InvalidOperationException("Conflicting bundle-block plan accepted.");
        }
    }
}
