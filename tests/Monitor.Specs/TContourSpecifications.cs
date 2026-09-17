// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class TContourSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(BroadTWidthAmplitudeAndRecovery), BroadTWidthAmplitudeAndRecovery),
        new(nameof(PeakedTIsNarrowerAndPreservesU), PeakedTIsNarrowerAndPreservesU),
        new(nameof(SymmetricInversionRetainsUAndAmplitudeBounds), SymmetricInversionRetainsUAndAmplitudeBounds),
        new(nameof(CrossingTimingMovesSmoothly), CrossingTimingMovesSmoothly),
        new(nameof(LimbContoursPreserveWilsonAndCoupleLeads), LimbContoursPreserveWilsonAndCoupleLeads),
        new(nameof(TContoursReachOnlySelectedLeadSupport), TContoursReachOnlySelectedLeadSupport),
        new(nameof(TContoursRestoreAndRetainU), TContoursRestoreAndRetainU),
        new(nameof(TContoursRejectInvalidAndConflictingPlans), TContoursRejectInvalidAndConflictingPlans),
    ];
    private static void CrossingTimingMovesSmoothly()
    {
        var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        foreach (int position in new[] { 200, 800 })
            foreach (var shape in new[] { EcgTContourShape.PositiveNegative, EcgTContourShape.NegativePositive })
            {
                var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
                    TextbookElectrodeReference.CreateElectrodes(tContour: new(1, shape, 300, CrossingPositionPermille: position))).GenerateBefore(800_000_000, 200, 100);
                int cross = 85 + 45 * position / 1000;
                int sign = shape == EcgTContourShape.PositiveNegative ? 1 : -1;
                long before = samples[cross - 1].MicrovoltValues[6] * sign;
                long at = samples[cross].MicrovoltValues[6] * sign;
                long after = samples[cross + 1].MicrovoltValues[6] * sign;
                Check.That(before > 0 && at == 0 && after < 0 && Math.Abs(before + after) <= 1, "requested crossing has symmetric nonzero slope");
                for (int i = 0; i < samples.Count; i++)
                    for (int lead = 0; lead < 12; lead++)
                        if (lead != 6 || i < 85 || i >= 130)
                        { Check.That(samples[i].ExactLeads[(EcgLead)lead].Numerator == baseline[i].ExactLeads[(EcgLead)lead].Numerator, "crossing preserves unselected leads and outside T"); }
            }
        var legacy = TextbookElectrodeReference.CreateElectrodes(tContour: new(1, EcgTContourShape.PositiveNegative, 300));
        var midpoint = TextbookElectrodeReference.CreateElectrodes(tContour: new(1, EcgTContourShape.PositiveNegative, 300, CrossingPositionPermille: 500));
        var a = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, legacy).GenerateBefore(800_000_000, 200, 100);
        var b = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, midpoint).GenerateBefore(800_000_000, 200, 100);
        Check.That(a.Zip(b).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "explicit midpoint preserves default bytes");
        bool collapsedRejected = false;
        try
        {
            TextbookElectrodeReference.CreateElectrodes(timing: TextbookEcgReference.Timing with { TDurationNs = 1 },
                tContour: new(1, EcgTContourShape.PositiveNegative, 300, CrossingPositionPermille: 200));
        }
        catch (EventWaveformException e) { collapsedRejected = e.ReasonCode == "EcgTContour.InvalidCrossing"; }
        Check.That(collapsedRejected, "collapsed nanosecond intervals are rejected");
        foreach (int value in new[] { 1, 999 })
        { TextbookElectrodeReference.CreateElectrodes(tContour: new(1, EcgTContourShape.PositiveNegative, 300, CrossingPositionPermille: value)); }
        foreach (var plan in new[] { new EcgTContourPlan(1, EcgTContourShape.PositiveNegative, 300, CrossingPositionPermille: 0), new(1, EcgTContourShape.PositiveNegative, 300, CrossingPositionPermille: 1000), new(1, EcgTContourShape.Notched, 300, CrossingPositionPermille: 500), new(1, EcgTContourShape.SymmetricInverted, 300, CrossingPositionPermille: 500), new(1, EcgTContourShape.PeakedUpright, 300, CrossingPositionPermille: 500), new(1, EcgTContourShape.BroadUpright, 300, CrossingPositionPermille: 500) })
        {
            try { TextbookElectrodeReference.CreateElectrodes(tContour: plan); }
            catch (EventWaveformException e) { Check.That(e.ReasonCode == "EcgTContour.InvalidCrossing", "stable crossing rejection"); continue; }
            throw new InvalidOperationException("Invalid crossing accepted.");
        }
    }
    private static void LimbContoursPreserveWilsonAndCoupleLeads()
    {
        var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        foreach (var target in Enum.GetValues<EcgTContourTarget>().Where(t => t != EcgTContourTarget.Chest))
            foreach (var shape in Enum.GetValues<EcgTContourShape>())
                foreach (int? crossing in shape is not (EcgTContourShape.PositiveNegative or EcgTContourShape.NegativePositive) ? new int?[] { null } : new int?[] { null, 200, 800 })
                {
                    var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
                        TextbookElectrodeReference.CreateElectrodes(tContour: new(1, shape, 300, target, crossing)));
                    var first = source.GenerateBefore(400_000_000, 100, 100);
                    var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
                    var tail = source.GenerateBefore(800_000_000, 100, 100);
                    var recovered = restored.GenerateBefore(800_000_000, 100, 100);
                    Check.That(tail.Zip(recovered).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "limb contour recovers mid-T");
                    var samples = first.Concat(tail).ToArray();
                    int lead = (int)target - 1;
                    var t = samples.Skip(85).Take(45).Select(s => (int)s.MicrovoltValues[lead]).ToArray();
                    if (shape == EcgTContourShape.Notched)
                    { Check.That(t.Take(22).Max() > 290 && t.Skip(23).Max() > 290 && t[22] < 200 && t.Min() >= 0, "selected limb has notched target"); }
                    else if (shape == EcgTContourShape.PeakedUpright)
                    { VerifyPeakedUpright(t); }
                    else if (shape == EcgTContourShape.BroadUpright)
                    { VerifyBroadUpright(t); }
                    else if (shape == EcgTContourShape.SymmetricInverted)
                    { VerifySymmetricInversion(t); }
                    else
                    {
                        int sign = shape == EcgTContourShape.PositiveNegative ? 1 : -1;
                        Check.That(t.Max() > 280 && t.Min() < -280 && t[1] * sign > 0, "selected limb has ordered biphasic target");
                    }
                    bool coupled = false;
                    for (int i = 0; i < samples.Length; i++)
                    {
                        var p = samples[i].ExactLeads;
                        Check.That(p[EcgLead.I].Numerator + p[EcgLead.III].Numerator == p[EcgLead.II].Numerator &&
                            p[EcgLead.AVR].Numerator + p[EcgLead.AVL].Numerator + p[EcgLead.AVF].Numerator == 0, "limb identities are exact");
                        for (int l = 0; l < 12; l++)
                        {
                            bool same = p[(EcgLead)l].Numerator == baseline[i].ExactLeads[(EcgLead)l].Numerator;
                            if (l >= 6 || i < 85 || i >= 130) { Check.That(same, "Wilson chest and all non-T samples remain exact"); }
                            else if (l != lead) { coupled |= !same; }
                        }
                    }
                    Check.That(coupled, "other limb leads change with electrode drive");
                }
    }
    private static void TContoursReachOnlySelectedLeadSupport()
    {
        var original = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes()).GenerateBefore(800_000_000, 200, 100);
        foreach (var shape in Enum.GetValues<EcgTContourShape>())
            foreach (int chest in Enumerable.Range(0, 6))
            {
                var samples = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
                    TextbookElectrodeReference.CreateElectrodes(tContour: new(1 << chest, shape, 300))).GenerateBefore(800_000_000, 200, 100);
                int lead = chest + 6;
                var t = samples.Skip(85).Take(45).Select(s => (int)s.MicrovoltValues[lead]).ToArray();
                if (shape == EcgTContourShape.Notched)
                { Check.That(t.Take(22).Max() > 290 && t.Skip(23).Max() > 290 && t[22] < 200 && t.Min() >= 0, "two positive peaks and a notch"); }
                else if (shape == EcgTContourShape.PeakedUpright)
                { VerifyPeakedUpright(t); }
                else if (shape == EcgTContourShape.BroadUpright)
                { VerifyBroadUpright(t); }
                else if (shape == EcgTContourShape.SymmetricInverted)
                { VerifySymmetricInversion(t); }
                else
                {
                    int sign = shape == EcgTContourShape.PositiveNegative ? 1 : -1;
                    Check.That(t[11] * sign > 290 && t[34] * sign < -290, "ordered signed T lobes");
                    Check.That(Math.Abs(t[24] - t[21]) > 50, "baseline crossing has no flat intermediate shoulder");
                }
                for (int i = 0; i < samples.Count; i++)
                    for (int l = 0; l < 12; l++)
                    {
                        if (l != lead || i < 85 || i >= 130)
                        { Check.That(samples[i].MicrovoltValues[l] == original[i].MicrovoltValues[l], "unselected leads and outside T support unchanged"); }
                    }
            }
    }
    private static void SymmetricInversionRetainsUAndAmplitudeBounds()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 20, 40, 60, 20, 20, 20]);
        var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes(u)).GenerateBefore(800_000_000, 200, 100);
        foreach (int amplitude in new[] { 1, 4000 })
        {
            var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(u, tContour: new(63, EcgTContourShape.SymmetricInverted, amplitude)));
            var first = source.GenerateBefore(400_000_000, 100, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var tail = source.GenerateBefore(800_000_000, 100, 100);
            Check.That(tail.Zip(restored.GenerateBefore(800_000_000, 100, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "inverted contour restores with U");
            var samples = first.Concat(tail).ToArray();
            foreach (int lead in Enumerable.Range(6, 6))
            {
                long minimum = samples.Skip(85).Take(45).Min(p => p.MicrovoltValues[lead]);
                Check.That(minimum >= -amplitude && minimum <= -amplitude * 99 / 100, "boundary depth remains bounded and sampled near its target");
            }
            Check.That(samples.Skip(130).Zip(baseline.Skip(130)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "U unchanged by symmetric inversion");
        }
    }
    private static void PeakedTIsNarrowerAndPreservesU()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 20, 40, 60, 20, 20, 20]);
        var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes(u)).GenerateBefore(800_000_000, 200, 100);
        var broad = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes(tContour: new(63, EcgTContourShape.SymmetricInverted, 1000))).GenerateBefore(800_000_000, 200, 100);
        foreach (int amplitude in new[] { 1, 1000, 4000 })
        {
            var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(u, tContour: new(63, EcgTContourShape.PeakedUpright, amplitude)));
            var first = source.GenerateBefore(400_000_000, 100, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var tail = source.GenerateBefore(800_000_000, 100, 100);
            Check.That(tail.Zip(restored.GenerateBefore(800_000_000, 100, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "peaked T restores with U");
            var samples = first.Concat(tail).ToArray();
            foreach (int lead in Enumerable.Range(6, 6))
            {
                long maximum = samples.Skip(85).Take(45).Max(p => p.MicrovoltValues[lead]);
                Check.That(maximum <= amplitude && maximum >= amplitude * 99 / 100, "peaked T amplitude boundaries");
                if (amplitude == 1000)
                {
                    int width = samples.Skip(85).Take(45).Count(p => p.MicrovoltValues[lead] >= 500);
                    int broadWidth = broad.Skip(85).Take(45).Count(p => p.MicrovoltValues[lead] <= -500);
                    Check.That(width < broadWidth, "peak is narrower than rounded symmetric contour at equal amplitude and duration");
                }
            }
            Check.That(samples.Skip(130).Zip(baseline.Skip(130)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "peaked T preserves U");
        }
    }
    private static void VerifyBroadUpright(int[] samples)
    {
        Check.That(samples.Min() == 0 && samples.Max() > 295, "broad upright T has a positive peak without undershoot");
        int peak = Array.IndexOf(samples, samples.Max());
        Check.That(peak == 28, "broad peak occurs near 62.5% rather than midpoint");
        Check.That(samples.Take(peak).Zip(samples.Skip(1).Take(peak)).All(p => p.First <= p.Second) &&
            samples.Skip(peak).Zip(samples.Skip(peak + 1)).All(p => p.First >= p.Second), "broad T has a single peak and smooth limbs");
        Check.That(samples.Count(v => v >= 150) > 14 && samples[20] > samples[36], "broad T has a wider peak and asymmetric flanks");
    }
    private static void BroadTWidthAmplitudeAndRecovery()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 20, 40, 60, 20, 20, 20]);
        var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes(u)).GenerateBefore(800_000_000, 200, 100);
        var narrow = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
            TextbookElectrodeReference.CreateElectrodes(tContour: new(63, EcgTContourShape.PeakedUpright, 1000))).GenerateBefore(800_000_000, 200, 100);
        foreach (int amplitude in new[] { 1, 1000, 4000 })
        {
            var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1,
                TextbookElectrodeReference.CreateElectrodes(u, tContour: new(63, EcgTContourShape.BroadUpright, amplitude)));
            var first = source.GenerateBefore(400_000_000, 100, 100);
            var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
            var tail = source.GenerateBefore(800_000_000, 100, 100);
            Check.That(tail.Zip(restored.GenerateBefore(800_000_000, 100, 100)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "broad T restores with U");
            var samples = first.Concat(tail).ToArray();
            foreach (int lead in Enumerable.Range(6, 6))
            {
                long maximum = samples.Skip(85).Take(45).Max(p => p.MicrovoltValues[lead]);
                Check.That(maximum <= amplitude && maximum >= amplitude * 99 / 100, "broad T amplitude boundaries");
                if (amplitude == 1000)
                {
                    int width = samples.Skip(85).Take(45).Count(p => p.MicrovoltValues[lead] >= 500);
                    int narrowWidth = narrow.Skip(85).Take(45).Count(p => p.MicrovoltValues[lead] >= 500);
                    Check.That(width > narrowWidth, "broad T is wider at equal amplitude and T duration");
                }
            }
            Check.That(samples.Skip(130).Zip(baseline.Skip(130)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "broad T preserves U");
        }
    }
    private static void VerifyPeakedUpright(int[] samples)
    {
        Check.That(samples.Min() == 0 && samples.Max() > 295, "positive peaked T reaches target without undershoot");
        for (int i = 1; i < samples.Length; i++)
        { Check.That(Math.Abs(samples[i] - samples[samples.Length - i]) <= 1, "peaked T limbs remain symmetric"); }
        Check.That(samples.Take(23).Zip(samples.Skip(1).Take(22)).All(p => p.First <= p.Second) &&
            samples.Skip(23).Zip(samples.Skip(24)).All(p => p.First >= p.Second), "single peaked T without shoulders");
        Check.That(samples.Count(v => v >= 150) is >= 10 and <= 14, "narrow half-height width on 4ms sample grid");
    }
    private static void VerifySymmetricInversion(int[] samples)
    {
        Check.That(samples.Max() == 0 && samples.Min() < -295, "negative single peak reaches requested depth");
        for (int i = 1; i < samples.Length; i++)
        { Check.That(Math.Abs(samples[i] - samples[samples.Length - i]) <= 1, "symmetric rising and falling limbs"); }
        Check.That(samples.Take(23).Zip(samples.Skip(1).Take(22)).All(p => p.First >= p.Second) &&
            samples.Skip(23).Zip(samples.Skip(24)).All(p => p.First <= p.Second), "single trough without secondary lobes");
    }
    private static void TContoursRestoreAndRetainU()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 20, 40, 60, 20, 20, 20]);
        var electrodes = TextbookElectrodeReference.CreateElectrodes(u, atrial: EcgAtrialIllustration.LeftAtrialAbnormality, tContour: new(63, EcgTContourShape.NegativePositive, 4000, CrossingPositionPermille: 800));
        var source = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, electrodes);
        source.GenerateBefore(400_000_000, 100, 100);
        var restored = ElectrodeSignalGenerator.Restore(source.CaptureState());
        var actual = source.GenerateBefore(800_000_000, 100, 100);
        var recovered = restored.GenerateBefore(800_000_000, 100, 100);
        Check.That(actual.Zip(recovered).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "recovery preserves all contour samples");
        var baseline = ElectrodeSignalGenerator.Start(Plan, "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes(u, atrial: EcgAtrialIllustration.LeftAtrialAbnormality)).GenerateBefore(800_000_000, 200, 100);
        Check.That(actual.Skip(30).Zip(baseline.Skip(130)).All(p => p.First.MicrovoltValues.SequenceEqual(p.Second.MicrovoltValues)), "U retains original timing and amplitudes");
        TextbookElectrodeReference.CreateElectrodes(tContour: new(1, EcgTContourShape.Notched, 1));
    }
    private static void TContoursRejectInvalidAndConflictingPlans()
    {
        foreach (var plan in new EcgTContourPlan[] { new(0, EcgTContourShape.Notched, 300), new(64, EcgTContourShape.Notched, 300), new(1, (EcgTContourShape)0, 300), new(1, EcgTContourShape.Notched, 0), new(1, EcgTContourShape.Notched, 4001), new(1, EcgTContourShape.Notched, 300, (EcgTContourTarget)99) })
        {
            try { TextbookElectrodeReference.CreateElectrodes(tContour: plan); }
            catch (EventWaveformException e) { Check.That(e.ReasonCode == "EcgTContour.InvalidPlan", "stable invalid contour reason"); continue; }
            throw new InvalidOperationException("Invalid contour accepted.");
        }
        try { TextbookElectrodeReference.CreateElectrodes(tWave: new(new int[10]), tContour: new(1, EcgTContourShape.Notched, 300)); }
        catch (EventWaveformException e) { Check.That(e.ReasonCode == "EcgTContour.ConflictingModes", "conflicting authoring fails"); return; }
        throw new InvalidOperationException("Conflicting contour accepted.");
    }
}
