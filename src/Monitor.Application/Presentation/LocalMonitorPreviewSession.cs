// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Authoring;
using Monitor.Simulation.Physiology;

namespace Monitor.Application.Presentation;

// Local preview runtime, not a replacement for the product authority gateway.
// Uses the same bounded pacing and 2.2s presentation buffer as the physiology demo.
public sealed class LocalMonitorPreviewSession
{
    public const long PresentationLatencyNs = 2_200_000_000;
    public const int RetainedBlockCount = 202;
    private readonly PhysiologyWaveformGroup _source;
    private WaveformEnvelope[] _blocks = [];
    private readonly LiveWaveformMeasurements? _measurements;
    private readonly PulseOximeterIllustrationSource? _opticalSource;
    private long _measurementFrontier;
    private static readonly long AcquisitionLatencyNs = FrozenSignalAcquisitionProfiles.Get("AcqPleth125@1").LatencyNs;
    public LiveMeasurementSnapshot? Measurements => _measurements?.Read(Math.Max(_measurementFrontier,
        Math.Max(0, SimulationTimeNs - AcquisitionLatencyNs)));
    public long SimulationTimeNs { get; private set; }
    public long FrontierNs { get; private set; }
    public ulong DataRevision { get; private set; }
    public IReadOnlyList<WaveformEnvelope> Blocks => Array.AsReadOnly(_blocks);
    public MonitorDisplayConfiguration Display { get; }
    public MonitorSweepRanges Ranges { get; }
    public LocalMonitorPreviewSession(PhysiologyIllustrationConfiguration configuration, MonitorDisplayConfiguration display,
        bool enableMeasurements = false, int? opticalSaturationMilliPercent = null, int opticalModulationPermille = 1000)
    {
        ArgumentNullException.ThrowIfNull(display);
        _source = PhysiologyIllustrationSource.Create(configuration);
        if (opticalSaturationMilliPercent.HasValue && !enableMeasurements)
        { throw new ArgumentException("Preview.OpticsRequireMeasurements", nameof(opticalSaturationMilliPercent)); }
        if (enableMeasurements) { _measurements = LiveWaveformMeasurements.CreateIllustration(); }
        if (opticalSaturationMilliPercent is { } target)
        {
            _opticalSource = new(PhysiologyIllustrationSource.ChannelId(2), PhysiologyIllustrationSource.ChannelId(2),
                Guid.NewGuid(), target, opticalModulationPermille);
        }
        Display = display; Ranges = new(display);
    }
    public void Advance(long deltaNs)
    {
        if (deltaNs is <= 0 or > 250_000_000) { throw new ArgumentOutOfRangeException(nameof(deltaNs)); }
        while (deltaNs > 0)
        {
            long chunk = Math.Min(deltaNs, 50_000_000);
            long next = checked(SimulationTimeNs + chunk);
            var wires = _source.AdvanceTo(next, 50, 1, 100);
            if (wires.Count > 0)
            {
                foreach (var wire in wires)
                {
                    if (_measurements is null) { break; }
                    byte[] measurementWire = wire;
                    if (_opticalSource is not null)
                    {
                        var optical = WaveformEnvelopeCodec.Decode(_opticalSource.ConvertAcquiredPulse(wire));
                        var original = WaveformEnvelopeCodec.Decode(wire);
                        measurementWire = WaveformEnvelopeCodec.EncodeRaw(original with
                        {
                            InstanceId = optical.InstanceId,
                            Planes = original.Planes.Concat(optical.Planes.Where(p => p.ChannelId != PhysiologyIllustrationSource.ChannelId(2))).ToArray()
                        });
                    }
                    _measurementFrontier = _measurements.Consume(measurementWire).SampleTimeNs;
                }
                _blocks = _blocks.Concat(wires.Select(b => WaveformEnvelopeCodec.Decode(b))).TakeLast(RetainedBlockCount).ToArray();
                DataRevision++;
            }
            SimulationTimeNs = next;
            FrontierNs = _blocks.Length == 0 ? 0 : Math.Max(FrontierNs,
                Math.Min(_blocks[^1].StartSimTimeNs + 200_000_000, Math.Max(0, next - PresentationLatencyNs)));
            Ranges.Advance(FrontierNs, (channel, from, to) => Samples(channel, from, to).Select(s => s.Value));
            deltaNs -= chunk;
        }
    }
    public IEnumerable<(long TimeNs, double Value)> Samples(int channel, long from, long to)
    {
        Guid id = PhysiologyIllustrationSource.ChannelId(channel);
        foreach (var block in _blocks)
        {
            if (block.StartSimTimeNs + 200_000_000 <= from || block.StartSimTimeNs >= to) { continue; }
            var plane = block.Planes.Single(p => p.ChannelId == id);
            long step = checked(1_000_000_000L * plane.SampleRateDenominator / plane.SampleRateNumerator);
            for (int i = 0; i < plane.Samples.Count; i++)
            {
                long time = block.StartSimTimeNs + i * step;
                if (time >= from && time < to)
                {
                    yield return (time, (double)plane.Samples[i] * plane.ScaleNumerator / plane.ScaleDenominator +
                        (double)plane.OffsetNumerator / plane.OffsetDenominator);
                }
            }
        }
    }
}
