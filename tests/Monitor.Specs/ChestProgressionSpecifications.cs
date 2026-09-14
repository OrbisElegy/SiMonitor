// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class ChestProgressionSpecifications
{
    private static RegularPhysiologyPlan Plan => new(0, 800_000_000, 160_000_000,
        80_000_000, 240_000_000, 3_750_000_000, 1_875_000_000);
    private static ElectrodeSignalGenerator Source() => ElectrodeSignalGenerator.Start(Plan,
        "AcqECGMonitor250@1", 1, TextbookElectrodeReference.CreateElectrodes());
    public static Specification[] All =>
    [
        new(nameof(ChestRAndSProgressThroughASubstantialTransition), ChestRAndSProgressThroughASubstantialTransition),
        new(nameof(ChestWaveOrderAndPeakTimesMatchReference), ChestWaveOrderAndPeakTimesMatchReference),
        new(nameof(ChestReferenceRetainsExactTopologyAndRestore), ChestReferenceRetainsExactTopologyAndRestore),
        new(nameof(ChestProgressionSurvivesAcquisitionAndWire), ChestProgressionSurvivesAcquisitionAndWire),
    ];

    private static void ChestRAndSProgressThroughASubstantialTransition()
    {
        var qrs = Source().GenerateBefore(240_000_000, 60, 100).Where(frame => frame.Tick.SimTimeNs >= 160_000_000).ToArray();
        int[] r = new int[6], s = new int[6];
        for (int index = 0; index < 6; index++)
        {
            r[index] = qrs.Max(frame => (int)frame.MicrovoltValues[(int)EcgLead.V1 + index]);
            s[index] = -qrs.Min(frame => (int)frame.MicrovoltValues[(int)EcgLead.V1 + index]);
        }
        Check.That(Enumerable.Range(0, 4).All(index => r[index] < r[index + 1]) && r[5] < r[4] &&
            s[1] > s[0] && Enumerable.Range(1, 4).All(index => s[index] > s[index + 1]),
            "R rises V1-V5 then falls V6; S is deepest V2 then falls V2-V6");
        Check.That(r[0] < s[0] && r[1] < s[1] && r[4] > s[4] && r[5] > s[5] &&
            r[2] >= 800 && s[2] >= 800 && Math.Abs(r[2] - s[2]) * 100 <= s[2] * 5,
            "V3 transition retains substantial R and S within five percent, rather than a flat QRS");
    }

    private static void ChestWaveOrderAndPeakTimesMatchReference()
    {
        var qrs = Source().GenerateBefore(240_000_000, 60, 100).Where(frame => frame.Tick.SimTimeNs >= 160_000_000).ToArray();
        foreach (EcgLead lead in new[] { EcgLead.V1, EcgLead.V2, EcgLead.V5, EcgLead.V6 })
        {
            var peak = qrs.MaxBy(frame => frame.MicrovoltValues[(int)lead])!;
            long limit = lead is EcgLead.V1 or EcgLead.V2 ? 30_000_000 : 50_000_000;
            Check.That(peak.Tick.SimTimeNs - 160_000_000 <= limit, "R peak respects the reference timing bound");
            int firstR = Array.FindIndex(qrs, frame => frame.MicrovoltValues[(int)lead] > 0);
            Check.That(firstR >= 0 && qrs.Skip(firstR + 1).Any(frame => frame.MicrovoltValues[(int)lead] < 0),
                "S is a negative wave following R");
            if (lead is EcgLead.V1 or EcgLead.V2)
            { Check.That(qrs.Take(firstR).All(frame => frame.MicrovoltValues[(int)lead] == 0), "V1/V2 have no initial q"); }
            else
            {
                int q = -qrs.Take(firstR).Min(frame => (int)frame.MicrovoltValues[(int)lead]);
                Check.That(q > 0 && q * 4 <= peak.MicrovoltValues[(int)lead] &&
                    qrs[firstR].Tick.SimTimeNs - 160_000_000 <= 30_000_000,
                    "left chest initial q remains short and shallow relative to R");
            }
        }
    }

    private static void ChestReferenceRetainsExactTopologyAndRestore()
    {
        var source = Source();
        var first = source.GenerateBefore(201_000_000, 51, 100);
        var second = ElectrodeSignalGenerator.Restore(source.CaptureState()).GenerateBefore(800_000_000, 200, 100);
        var whole = Source().GenerateBefore(800_000_000, 200, 100);
        var split = first.Concat(second).ToArray();
        for (int index = 0; index < whole.Count; index++)
        {
            Check.That(split[index].Tick == whole[index].Tick && split[index].MicrovoltValues.SequenceEqual(whole[index].MicrovoltValues),
                "revised electrode morphology preserves sample boundaries and restored values");
            var leads = whole[index].ExactLeads;
            Int128 i = leads[EcgLead.I].Numerator, ii = leads[EcgLead.II].Numerator;
            Check.That(leads[EcgLead.III].Numerator == ii - i && 2 * leads[EcgLead.AVR].Numerator == -i - ii &&
                2 * leads[EcgLead.AVL].Numerator == 2 * i - ii && 2 * leads[EcgLead.AVF].Numerator == 2 * ii - i,
                "electrode morphology retains exact limb identities before quantization");
        }
    }

    private static void ChestProgressionSurvivesAcquisitionAndWire()
    {
        Guid Id(int index) => Guid.Parse($"00000000-0000-4000-8000-{index + 1:D12}");
        var group = ElectrodeWaveformGroup.Start(Id(20), Id(21), 1, 1, 1, 0, 16, Plan,
            TextbookElectrodeReference.CreateElectrodes(), Enum.GetValues<EcgLead>()
                .Select(lead => new ElectrodeChannelPlan(lead, Id((int)lead), 10, 0)).ToArray());
        var blocks = group.AdvanceTo(436_000_000, 109, 2, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
        var expected = Source().GenerateBefore(400_000_000, 100, 100);
        foreach (EcgLead lead in Enum.GetValues<EcgLead>())
        {
            var actual = blocks.SelectMany(block => block.Planes.Single(plane => plane.ChannelId == Id((int)lead)).Samples);
            Check.That(actual.SequenceEqual(expected.Select(frame => frame.MicrovoltValues[(int)lead])),
                "revised R/S progression reaches delayed decoded blocks without lead substitution");
        }
    }
}
