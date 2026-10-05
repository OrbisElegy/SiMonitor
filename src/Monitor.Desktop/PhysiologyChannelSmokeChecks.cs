// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Determinism;
using Monitor.Simulation.Physiology;

namespace Monitor.Desktop;

internal static class PhysiologyChannelSmokeChecks
{
    private const long BlockDurationNs = 200_000_000;

    private static long AcquisitionLatencyNs => Math.Max(
        FrozenSignalAcquisitionProfiles.Get("AcqPleth125@1").LatencyNs,
        FrozenSignalAcquisitionProfiles.Get("AcqCO2_100@1").LatencyNs);

    internal static WaveformEnvelope[] Verify(string name, PhysiologyDemoConfiguration config,
        RegularPhysiologyPlan expectedPlan, IReadOnlyList<EventWaveformBand> ecgBands,
        FixedPerfusionPreset perfusion, bool verifyMechanicalSuppression = false)
    {
        var plan = config.ResolvePlan();
        if (plan != expectedPlan) { throw new InvalidOperationException(name + " physiology plan differs from shared source."); }
        var blocks = DecodeCompletedOutput(config, 6_000_000_000);
        var normal = DecodeCompletedOutput(PhysiologyDemoConfiguration.Default, 6_000_000_000);
        foreach (int row in new[] { 1, 4 })
            if (!MechanicalUncouplingSmokeChecks.Samples(blocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, row)))
            { throw new InvalidOperationException(name + " changed respiration/CO2."); }
        if (MechanicalUncouplingSmokeChecks.Samples(blocks, 0).Length != 1500)
        { throw new InvalidOperationException(name + " test must include six seconds of completed raw ECG after acquisition delay."); }
        var ii = PhysiologySignalGenerator.Start(plan, "AcqECGMonitor250@1", 1, ecgBands).GenerateBefore(6_000_000_000, 1500, 100);
        short[] ecgSamples = MechanicalUncouplingSmokeChecks.Samples(blocks, 0);
        if (ecgSamples.Length != ii.Count || ecgSamples.Zip(ii).Any(p => Math.Abs(p.First - p.Second.NormalizedValue) > 1))
        { throw new InvalidOperationException(name + " physiology monitorII mismatch."); }
        var pleth = PlethRunoffSource.Create(plan, perfusion.Pleth);
        var abp = VascularPressureSource.Create(plan, perfusion.Arterial);
        var pa = VascularPressureSource.Create(plan, perfusion.Pulmonary);
        foreach (int row in new[] { 2, 3, 5 })
        {
            short[] samples = MechanicalUncouplingSmokeChecks.Samples(blocks, row);
            if (samples.Length != 750)
            { throw new InvalidOperationException(name + " perfusion did not publish six seconds of completed samples."); }
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
        if (!MechanicalUncouplingSmokeChecks.Samples(blocks, 6).SequenceEqual(cvp.Select(sample => sample.NormalizedValue)))
        { throw new InvalidOperationException(name + " CVP overlap mismatch."); }
        var artifactConfig = config with { RespCardiacArtifactCounts = 200 };
        var artifactBlocks = DecodeCompletedOutput(artifactConfig, 6_000_000_000);
        var artifactPlan = new RespirationPlan(1000, 200).CreateChannel(plan, PhysiologyDemoSource.ChannelId(1), 0);
        var expectedResp = PhysiologySignalGenerator.Start(plan, "AcqResp125@1", 1, artifactPlan.Bands).GenerateBefore(6_000_000_000, 750, 200);
        if (!MechanicalUncouplingSmokeChecks.Samples(artifactBlocks, 1).SequenceEqual(expectedResp.Select(sample => sample.NormalizedValue)))
        { throw new InvalidOperationException(name + " respiratory cardiac artifact clock mismatch."); }
        foreach (int row in new[] { 0, 2, 3, 4, 5, 6 })
            if (!MechanicalUncouplingSmokeChecks.Samples(artifactBlocks, row).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(blocks, row)))
            { throw new InvalidOperationException(name + " respiratory artifact leaked to another channel."); }
        if (verifyMechanicalSuppression)
        {
            var noEjection = DecodeCompletedOutput(config with { VentricularMechanicalEnabled = false, RespCardiacArtifactCounts = 200 }, 6_000_000_000);
            if (MechanicalUncouplingSmokeChecks.Samples(noEjection, 2).Any(v => v != 0) ||
                !MechanicalUncouplingSmokeChecks.Samples(noEjection, 0).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(blocks, 0)) ||
                !MechanicalUncouplingSmokeChecks.Samples(noEjection, 1).SequenceEqual(MechanicalUncouplingSmokeChecks.Samples(normal, 1)))
            { throw new InvalidOperationException("Disabling ejection must remove optical pulses and cardiac artifact while preserving ECG."); }
        }
        foreach (long boundary in new[] { 200_000_000L, 400_000_000, 800_000_000, 4_200_000_000, 4_400_000_000, 5_000_000_000, 5_200_000_000 })
        {
            VerifyWireRecovery(config, boundary, name);
        }
        return blocks;
    }
    // Advance through acquisition latency so the requested output interval is
    // complete on every channel, including the slow Pleth and CO2 channels.
    internal static WaveformEnvelope[] DecodeCompletedOutput(PhysiologyDemoConfiguration config, long toExclusiveSimTimeNs)
    {
        var source = PhysiologyDemoSource.Create(config);
        var blocks = new List<WaveformEnvelope>();
        for (long timeNs = BlockDurationNs; timeNs <= toExclusiveSimTimeNs + AcquisitionLatencyNs; timeNs += BlockDurationNs)
        {
            blocks.AddRange(source.AdvanceTo(timeNs, 50, 1, 100).Select(bytes => WaveformEnvelopeCodec.Decode(bytes)));
        }
        if (blocks.Count != toExclusiveSimTimeNs / BlockDurationNs || blocks.Count == 0 ||
            blocks.Where((block, index) => block.StartSimTimeNs != index * BlockDurationNs || block.DurationNs != BlockDurationNs).Any() ||
            blocks[^1].StartSimTimeNs + blocks[^1].DurationNs != toExclusiveSimTimeNs)
        { throw new InvalidOperationException("Physiology fixture did not complete the requested output interval."); }
        return blocks.ToArray();
    }

    internal static void VerifyWireRecovery(PhysiologyDemoConfiguration config, long checkpointSimTimeNs, string name)
    {
        var source = PhysiologyDemoSource.Create(config);
        for (long timeNs = BlockDurationNs; timeNs <= checkpointSimTimeNs; timeNs += BlockDurationNs)
        { source.AdvanceTo(timeNs, 50, 1, 100); }
        var restored = PhysiologyWaveformGroup.Restore(source.CaptureState());
        long throughSimTimeNs = Math.Max(checkpointSimTimeNs + BlockDurationNs, AcquisitionLatencyNs + BlockDurationNs);
        int comparedBlocks = 0;
        for (long timeNs = checkpointSimTimeNs + BlockDurationNs; timeNs <= throughSimTimeNs; timeNs += BlockDurationNs)
        {
            var expected = source.AdvanceTo(timeNs, 50, 1, 100);
            var actual = restored.AdvanceTo(timeNs, 50, 1, 100);
            if (expected.Count != actual.Count || expected.Zip(actual).Any(pair => !pair.First.SequenceEqual(pair.Second)))
            { throw new InvalidOperationException(name + " physiology wire recovery mismatch."); }
            comparedBlocks += expected.Count;
        }
        if (comparedBlocks == 0)
        { throw new InvalidOperationException(name + " physiology wire recovery did not publish any completed blocks."); }
    }
}
