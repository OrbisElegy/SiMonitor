// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Specs;

internal static class SeededCo2Specifications
{
    public static Specification[] All => [new(nameof(SeededCo2PreservesBreathingAndRecovery), SeededCo2PreservesBreathingAndRecovery)];
    private static void SeededCo2PreservesBreathingAndRecovery()
    {
        string seed = new('1', 64);
        var pressure = new SeededExpirationPressure(40, 500, seed);
        var config = PhysiologyIllustrationConfiguration.Default with { SeededCo2 = pressure, BreathPeriodMilliseconds = 1000, InspirationMilliseconds = 500 };
        var channel = config.ResolveCapnogram().CreateChannel(config.ResolvePlan(), Guid.NewGuid(), 0);
        var gains = channel.Bands[0].ExpirationCycleGainsPermille!;
        Check.That(gains.Count == 64 && gains[0] == 1000 && gains.All(g => g is >= 875 and <= 1125), "64 bounded breath targets start at nominal");
        var same = new SeededExpirationPressure(40, 500, seed);
        var other = config with { SeededCo2 = new(40, 500, new string('2', 64)) };
        Check.That(pressure.PreparedState == same.PreparedState && !gains.SequenceEqual(other.ResolveCapnogram().CreateChannel(other.ResolvePlan(), Guid.NewGuid(), 0).Bands[0].ExpirationCycleGainsPermille!), "named seed stream reproduces and distinct seeds differ");
        void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "invalid pressure configuration rejects before generation");
        }
        Reject(() => _ = new SeededExpirationPressure(80, 1, seed));
        Reject(() => _ = new SeededExpirationPressure(5, 1, seed));
        Reject(() => _ = new SeededExpirationPressure(40, 501, seed));
        Reject(() => _ = new SeededExpirationPressure(40, 500, "invalid"));
        Reject(() => PhysiologyIllustrationSource.Create(config with { RespiratoryPattern = RespiratoryPattern.CheyneStokesIllustration }));
        Reject(() => PhysiologyIllustrationSource.Create(config with { RespiratoryActivity = RespiratoryActivity.Absent }));
        Reject(() => PhysiologyIllustrationSource.Create(config with { Co2EndExpiratoryMmHg = 50 }));
        var source = PhysiologyIllustrationSource.Create(config);
        var repeat = PhysiologyIllustrationSource.Create(config with { SeededCo2 = same });
        var plain = PhysiologyIllustrationSource.Create(config with { SeededCo2 = null });
        var zero = PhysiologyIllustrationSource.Create(config with { SeededCo2 = new(40, 0, seed) });
        var measurement = LiveWaveformMeasurements.CreateIllustration(); List<int> measured = [];
        for (int step = 1; step <= 370; step++)
        {
            long time = step * 200_000_000L;
            var a = source.AdvanceTo(time, 50, 1, 100);
            var b = repeat.AdvanceTo(time, 50, 1, 100);
            var c = plain.AdvanceTo(time, 50, 1, 100);
            var d = zero.AdvanceTo(time, 50, 1, 100);
            Check.That(a.Count == b.Count && a.Count == c.Count && c.Count == d.Count, "sampling schedule unchanged");
            for (int i = 0; i < a.Count; i++)
            {
                Check.That(a[i].SequenceEqual(b[i]) && c[i].SequenceEqual(d[i]), "same seed restore and zero spread retain exact bytes");
                var changed = WaveformEnvelopeCodec.Decode(a[i]); var original = WaveformEnvelopeCodec.Decode(c[i]);
                foreach (var plane in changed.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(4)))
                { Check.That(plane.Samples.SequenceEqual(original.Planes.Single(p => p.ChannelId == plane.ChannelId).Samples), "CO2 does not alter Resp, ECG or perfusion"); }
                var reading = measurement.Consume(a[i]);
                if (step > 40)
                {
                    Check.That(reading.Capnography.EndTidalCentiMmHg.Value is >= 3400 and <= 4600 &&
                        reading.Capnography.RespirationsMilliPerMinute.Value is >= 59000 and <= 61000,
                        "sample-derived EtCO2 follows variation while RR retains source clock");
                    measured.Add(reading.Capnography.EndTidalCentiMmHg.Value!.Value);
                }
            }
            if (step == 123) { repeat = PhysiologyWaveformGroup.Restore(repeat.CaptureState()); }
        }
        Check.That(measured.Max() - measured.Min() > 500, "measured EtCO2 changes over multiple breaths including64-cycle wrap");
    }
}
