// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

public sealed record RealtimeOxygenationConfiguration(VentilationTransportPlan Ventilation,
    OxygenReservoirParameters Parameters, int ReferenceStrokeVolumeMicroliters = 66667)
{
    public static RealtimeOxygenationConfiguration ReferenceAdult => new(new(450000, 150000, 210000), new());
    public decimal OxygenDemandMultiplier { get; init; } = 1;
}

public readonly record struct RealtimeOxygenationSnapshot(long SourceSimTimeNs, int SaturationMilliPercent,
    OxygenReservoirState Reservoirs, decimal OxygenBalanceResidualMl, int RetainedSamples,
    decimal OxygenDemandMultiplier = 1);

// A bounded source-time history, advanced explicitly by its owner. Reading the
// sensor never advances the physiology. The fixed grid is independent of UI pacing.
public sealed class RealtimeOxygenationSource : IArterialOxygenationSource
{
    private readonly record struct TransportChange(long AtSimTimeNs, PhysiologyTransportSource Transport, decimal OxygenDemandMultiplier);
    public const int HistoryCapacity = 1024; // 8.184s, including the 2s acquisition delay.
    private readonly OxygenReservoirParameters _parameters;
    private readonly decimal _initialOxygenMl;
    private PhysiologyTransportSource _transport;
    // At most the current unstarted interval and the next grid boundary.
    // A later live edit must not replace an earlier, not-yet-integrated change.
    private TransportChange[] _pendingChanges = [];
    private decimal _oxygenDemandMultiplier;
    private OxygenReservoirState _reservoirs;
    private List<ArterialOxygenationSample> _history;
    public long SourceSimTimeNs => _history[^1].SourceSimTimeNs;
    public RealtimeOxygenationSnapshot Snapshot => new(SourceSimTimeNs, _history[^1].SaturationMilliPercent,
        _reservoirs, _reservoirs.ConservedOxygenMl - _initialOxygenMl, _history.Count, _oxygenDemandMultiplier);

    public RealtimeOxygenationSource(PhysiologyTransportSource transport, OxygenReservoirParameters parameters,
        decimal oxygenDemandMultiplier = 1, long? initialSimTimeNs = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(parameters);
        OxygenReservoirModel.ValidateOxygenDemandMultiplier(oxygenDemandMultiplier);
        _oxygenDemandMultiplier = oxygenDemandMultiplier;
        _parameters = parameters;
        _transport = transport;
        ValidateVentilationFlow(transport.CaptureState().Ventilation);
        _reservoirs = OxygenReservoirModel.ReferenceState(parameters);
        _initialOxygenMl = _reservoirs.ConservedOxygenMl;
        long initialTime = initialSimTimeNs ?? transport.CaptureState().Physiology.EpochAnchorSimTimeNs;
        if (initialTime < transport.CaptureState().Physiology.EpochAnchorSimTimeNs || initialTime % OxygenReservoirModel.StepNs != 0)
        { throw new ArgumentOutOfRangeException(nameof(initialSimTimeNs)); }
        _history = [new(initialTime,
            OxygenReservoirModel.ArterialSaturationMilliPercent(_reservoirs, parameters))];
    }

    private RealtimeOxygenationSource(RealtimeOxygenationSource source)
    {
        _parameters = source._parameters;
        _initialOxygenMl = source._initialOxygenMl;
        _transport = source._transport;
        _pendingChanges = source._pendingChanges;
        _oxygenDemandMultiplier = source._oxygenDemandMultiplier;
        _reservoirs = source._reservoirs;
        _history = [.. source._history];
    }

    public RealtimeOxygenationSource Fork() => new(this);

    public void AdvanceTo(long toSimTimeNs)
    {
        if (toSimTimeNs < SourceSimTimeNs || toSimTimeNs - SourceSimTimeNs > 1_000_000_000)
        { throw new ArgumentOutOfRangeException(nameof(toSimTimeNs)); }
        var history = new List<ArterialOxygenationSample>(_history);
        var reservoirs = _reservoirs;
        var transport = _transport;
        int appliedChanges = 0;
        decimal multiplier = _oxygenDemandMultiplier;
        long time = SourceSimTimeNs;
        while (toSimTimeNs - time >= OxygenReservoirModel.StepNs)
        {
            while (appliedChanges < _pendingChanges.Length && time >= _pendingChanges[appliedChanges].AtSimTimeNs)
            {
                var change = _pendingChanges[appliedChanges++];
                transport = change.Transport;
                multiplier = change.OxygenDemandMultiplier;
            }
            long next = checked(time + OxygenReservoirModel.StepNs);
            reservoirs = OxygenReservoirModel.Step(reservoirs, _parameters, transport.Integrate(time, next), multiplier);
            history.Add(new(next, OxygenReservoirModel.ArterialSaturationMilliPercent(reservoirs, _parameters)));
            time = next;
        }
        if (history.Count > HistoryCapacity) { history.RemoveRange(0, history.Count - HistoryCapacity); }
        _reservoirs = reservoirs;
        _history = history;
        _transport = transport;
        _pendingChanges = _pendingChanges.Skip(appliedChanges).ToArray();
        _oxygenDemandMultiplier = multiplier;
    }

    // Apply at the first unstarted grid interval after the user's simulation
    // time. Never rewrite an already integrated partial interval or old samples.
    public long ChangeVentilation(VentilationTransportPlan ventilation, long atSimTimeNs, decimal? oxygenDemandMultiplier = null)
    {
        var transport = _pendingChanges.Length > 0 ? _pendingChanges[^1].Transport : _transport;
        ValidateVentilationFlow(ventilation, transport);
        decimal multiplier = oxygenDemandMultiplier ?? (_pendingChanges.Length > 0 ? _pendingChanges[^1].OxygenDemandMultiplier : _oxygenDemandMultiplier);
        OxygenReservoirModel.ValidateOxygenDemandMultiplier(multiplier);
        if (atSimTimeNs < SourceSimTimeNs || atSimTimeNs - SourceSimTimeNs >= OxygenReservoirModel.StepNs)
        { throw new ArgumentOutOfRangeException(nameof(atSimTimeNs)); }
        var next = PhysiologyTransportSource.Restore(transport.CaptureState() with { Ventilation = ventilation });
        long effectiveNs = checked(SourceSimTimeNs + (atSimTimeNs == SourceSimTimeNs ? 0 : OxygenReservoirModel.StepNs));
        _pendingChanges = _pendingChanges.Where(change => change.AtSimTimeNs < effectiveNs)
            .Append(new TransportChange(effectiveNs, next, multiplier)).ToArray();
        return effectiveNs;
    }

    public void ChangeTransport(PhysiologyTransportSource transport, long atSimTimeNs, decimal oxygenDemandMultiplier)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ValidateVentilation(transport.CaptureState().Ventilation);
        OxygenReservoirModel.ValidateOxygenDemandMultiplier(oxygenDemandMultiplier);
        ArgumentOutOfRangeException.ThrowIfNotEqual(atSimTimeNs, SourceSimTimeNs);
        var trial = new RealtimeOxygenationSource(transport, _parameters, oxygenDemandMultiplier, atSimTimeNs);
        _pendingChanges = [new(atSimTimeNs, trial._transport, oxygenDemandMultiplier)];
    }

    public ArterialOxygenationSample ReadAt(long sourceSimTimeNs)
    {
        if (sourceSimTimeNs < _history[0].SourceSimTimeNs || sourceSimTimeNs > SourceSimTimeNs)
        { throw new ArgumentOutOfRangeException(nameof(sourceSimTimeNs), "Oxygenation.HistoryUnavailable"); }
        long offset = sourceSimTimeNs - _history[0].SourceSimTimeNs;
        int index = (int)(offset / OxygenReservoirModel.StepNs);
        long remainder = offset % OxygenReservoirModel.StepNs;
        int saturation = _history[index].SaturationMilliPercent;
        if (remainder != 0)
        {
            saturation = decimal.ToInt32(decimal.Round(saturation +
                (_history[index + 1].SaturationMilliPercent - saturation) * (decimal)remainder / OxygenReservoirModel.StepNs,
                0, MidpointRounding.ToEven));
        }
        return new(sourceSimTimeNs, saturation);
    }

    public static void ValidateVentilation(VentilationTransportPlan ventilation)
    {
        ArgumentNullException.ThrowIfNull(ventilation);
        if (ventilation.TidalVolumeMicrolitersBtps is < 0 or > 1500000 ||
            ventilation.DeadSpaceMicrolitersBtps is < 0 or > 500000 ||
            ventilation.InspiredOxygenMillionths is < 100000 or > 1000000)
        { throw new ArgumentException("Oxygenation.InvalidVentilation", nameof(ventilation)); }
    }

    private void ValidateVentilationFlow(VentilationTransportPlan ventilation, PhysiologyTransportSource? transport = null)
    {
        ValidateVentilation(ventilation);
        var physiology = (transport ?? _transport).CaptureState().Physiology;
        long flowDurationNs = physiology.InspirationDurationNs - physiology.InspiratoryPauseNs;
        // All supported depth patterns peak at 1000 permille. Bound the integer
        // interval volume before accepting controls, including sub-step breaths.
        Int128 volumeNumerator = (Int128)ventilation.TidalVolumeMicrolitersBtps * 1000 * Math.Min(flowDurationNs, OxygenReservoirModel.StepNs);
        if (ventilation.AirwayOpen && (volumeNumerator + flowDurationNs - 1) / flowDurationNs > 100_000_000)
        { throw new ArgumentException("Oxygenation.VentilationFlowTooHigh"); }
    }
}
