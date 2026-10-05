// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PhysiologyChannelSmokeChecks
{
    internal static WaveformEnvelope[] Verify(string name, PhysiologyDemoConfiguration config,
        RegularPhysiologyPlan expectedPlan, IReadOnlyList<EventWaveformBand> ecgBands,
        FixedPerfusionPreset perfusion, bool verifyMechanicalSuppression = false)
    {
        var plan = config.ResolvePlan();
        if (plan != expectedPlan) { throw new InvalidOperationException(name + " physiology plan differs from shared source."); }
        var blocks = Decode(config);
        var normal = Decode(PhysiologyDemoConfiguration.Default);
        foreach (int row in new[] { 1, 4 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException(name + " changed respiration/CO2."); }
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Length != 1500)
        { throw new InvalidOperationException(name + " test must include six seconds of completed raw ECG after acquisition delay."); }
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, ecgBands).GenerateBefore(6_000_000_000, 1500, 100);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException(name + " physiology monitorII mismatch."); }
        var pleth = PlethRunoffSource.Create(plan, perfusion.Pleth);
        var abp = VascularPressureSource.Create(plan, perfusion.Arterial);
        var pa = VascularPressureSource.Create(plan, perfusion.Pulmonary);
        foreach (int row in new[] { 2, 3, 5 })
        {
            short[] samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
            for (int i = 0; i < samples.Length; i++)
            {
                long time = i * 8_000_000L;
                long q32 = row == 2 ? pleth.EvaluateAt(time) : row == 3 ? abp.EvaluateAt(time) : pa.EvaluateAt(time);
                if (samples[i] != (short)FixedPointMath.RoundDivideTiesToEven(q32, FixedPointMath.Q32One))
                { throw new InvalidOperationException(name + " perfusion support or phase mismatch."); }
            }
            if (samples.Skip(250).Distinct().Count() < 10) { throw new InvalidOperationException(name + " perfusion became flat."); }
        }
        var cvpPlan = perfusion.Venous.CreateChannel(plan, PhysiologyDemoSource.ChannelId(6), 0);
        var cvp = PhysiologySignalGenerator.Start(plan, "AcqPressure125@1", 1, cvpPlan.Bands,
            pressureBaselineCentiMmHg: cvpPlan.PressureBaselineCentiMmHg).GenerateBefore(6_000_000_000, 750, 200);
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 6).Zip(cvp).Any(p => p.First != p.Second.NormalizedValue))
        { throw new InvalidOperationException(name + " CVP overlap mismatch."); }
        var artifactConfig = config with { RespCardiacArtifactCounts = 200 };
        var artifactBlocks = Decode(artifactConfig);
        var artifactPlan = new RespirationPlan(1000, 200).CreateChannel(plan, PhysiologyDemoSource.ChannelId(1), 0);
        var expectedResp = PhysiologySignalGenerator.Start(plan, "AcqResp125@1", 1, artifactPlan.Bands).GenerateBefore(6_000_000_000, 750, 200);
        if (MechanicalUncouplingSmokeChecks.Samples(artifactBlocks, 1).Zip(expectedResp).Any(p => p.First != p.Second.NormalizedValue))
        { throw new InvalidOperationException(name + " respiratory cardiac artifact clock mismatch."); }
        foreach (int row in new[] { 0, 2, 3, 4, 5, 6 })
            if (!MechanicalUncouplingSmokeChecks.Samples(artifactBlocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(blocks, row)))
            { throw new InvalidOperationException(name + " respiratory artifact leaked to another channel."); }
        if (verifyMechanicalSuppression)
        {
            var noEjection = Decode(config with { VentricularMechanicalEnabled = false, RespCardiacArtifactCounts = 200 });
            if (MechanicalUncouplingSmokeChecks.Samples(noEjection, 2).Any(v => v != 0) ||
                !MechanicalUncouplingSmokeChecks.Samples(noEjection, 0).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(blocks, 0)) ||
                !MechanicalUncouplingSmokeChecks.Samples(noEjection, 1).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, 1)))
            { throw new InvalidOperationException("Disabling ejection must remove optical pulses and cardiac artifact while preserving ECG."); }
        }
        foreach (long boundary in new[] { 200_000_000L, 400_000_000, 800_000_000, 4_200_000_000, 4_400_000_000, 5_000_000_000, 5_200_000_000 })
        {
            var source = PhysiologyDemoSource.Create(config);
            for (long time = 200_000_000; time <= boundary; time += 200_000_000) { source.AdvanceTo(time, 50, 1, 100); }
            var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
            var a = source.AdvanceTo(boundary + 200_000_000, 50, 1, 100); var b = restored.AdvanceTo(boundary + 200_000_000, 50, 1, 100);
            if (a.Count != b.Count || a.Zip(b).Any(p => !p.First.SequenceEqual(p.Second))) { throw new InvalidOperationException(name + " physiology wire recovery mismatch."); }
        }
        return blocks;
    }
    // Pleth has2s acquisition delay; advance to8s to publish the first6s
    // across all channels, including irregular atrial/ventricular intervals.
    private static WaveformEnvelope[] Decode(PhysiologyDemoConfiguration config)
    {
        var source = PhysiologyDemoSource.Create(config);
        return Enumerable.Range(1, 40).SelectMany(step => source.AdvanceTo(step * 200_000_000L, 50, 1, 100))
            .Select(bytes => WaveformEnvelopeCodec.Decode(bytes)).ToArray();
    }

}
