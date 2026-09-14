// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class RespCo2CouplingSmokeChecks
{
    internal static void Verify()
    {
        var source = PhysiologyDemoSource.Create();
        List<WaveformEnvelope> blocks = [];
        for (int step = 1; step <= 60; step++)
        {
            blocks.AddRange(source.AdvanceTo(step * 200_000_000L, 50, 1, 100)
                .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
            source = PhysiologyWaveformGroup.Restore(source.CaptureState());
        }
        var resp = Samples(1, 125, 1);
        var co2 = Samples(4, 100, 100);
        if (blocks.Count != 50 || resp.Count != 1250 || co2.Count != 1000)
        { throw new InvalidOperationException("Resp/CO2 coupling lost shared blocks or native samples after restore."); }
        foreach (long cycle in new[] { 0L, 1L })
        {
            long start = cycle * 3_750_000_000;
            long expiration = start + 1_875_000_000;
            long deadSpaceEnd = expiration + 125_000_000;
            long plateauStart = expiration + 375_000_000;
            long nextInspiration = start + 3_750_000_000;
            long respPeakTick = expiration / 8_000_000 * 8_000_000;
            long respTroughTick = (nextInspiration + 7_999_999) / 8_000_000 * 8_000_000;
            if (resp[respPeakTick] < 999 || resp[respPeakTick - 80_000_000] >= resp[respPeakTick] ||
                resp[respPeakTick + 80_000_000] >= resp[respPeakTick] || co2[deadSpaceEnd] != 0 ||
                co2[deadSpaceEnd + 10_000_000] <= 0 || co2[plateauStart] != 3714 ||
                co2[nextInspiration] != 4000 || co2[nextInspiration + 50_000_000] is <= 0 or >= 4000 ||
                co2[nextInspiration + 200_000_000] != 0 || resp[respTroughTick] > 1 ||
                resp[respTroughTick + 80_000_000] <= resp[respTroughTick])
            { throw new InvalidOperationException("Resp turning points and CO2 phases do not follow the same breath events."); }
        }
        Console.WriteLine("ok: two complete Resp/CO2 cycles share expiration turns, dead space and next-inspiration falls after native decode/restore");

        SortedDictionary<long, short> Samples(int row, uint rate, uint scaleDenominator)
        {
            SortedDictionary<long, short> result = [];
            foreach (var block in blocks)
            {
                var plane = block.Planes.Single(item => item.ChannelId == PhysiologyDemoSource.ChannelId(row));
                if (plane.SampleRateNumerator != rate || plane.SampleRateDenominator != 1 ||
                    plane.ScaleNumerator != 1 || plane.ScaleDenominator != scaleDenominator || plane.OffsetNumerator != 0)
                { throw new InvalidOperationException("Resp/CO2 native rate or affine units changed."); }
                for (int index = 0; index < plane.Samples.Count; index++)
                { result.Add(block.StartSimTimeNs + index * 1_000_000_000L / rate, plane.Samples[index]); }
            }
            return result;
        }
    }
}
