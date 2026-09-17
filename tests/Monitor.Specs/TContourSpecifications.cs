// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class TContourSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000, 80_000_000, 240_000_000, 4_000_000_000, 2_000_000_000);
    public static Specification[] All =>
    [
        new(nameof(TContoursReachOnlySelectedLeadSupport), TContoursReachOnlySelectedLeadSupport),
        new(nameof(TContoursRestoreAndRetainU), TContoursRestoreAndRetainU),
        new(nameof(TContoursRejectInvalidAndConflictingPlans), TContoursRejectInvalidAndConflictingPlans),
    ];
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
    private static void TContoursRestoreAndRetainU()
    {
        var u = new EcgUWavePlan(30_000_000, 120_000_000, [0, 0, 0, 0, 20, 40, 60, 20, 20, 20]);
        var electrodes = TextbookElectrodeReference.CreateElectrodes(u, atrial: EcgAtrialIllustration.LeftAtrialAbnormality, tContour: new(63, EcgTContourShape.NegativePositive, 4000));
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
        foreach (var plan in new EcgTContourPlan[] { new(0, EcgTContourShape.Notched, 300), new(64, EcgTContourShape.Notched, 300), new(1, (EcgTContourShape)0, 300), new(1, EcgTContourShape.Notched, 0), new(1, EcgTContourShape.Notched, 4001) })
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
