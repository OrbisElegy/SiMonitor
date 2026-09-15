// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum EcgElectrode { RA, LA, RL, LL, C1, C2, C3, C4, C5, C6 }
public enum EcgLead { I, II, III, AVR, AVL, AVF, V1, V2, V3, V4, V5, V6 }

// Numerator is in sixths of a Q32 microvolt. This retains halves and thirds
// exactly through projection; acquisition quantization is a separate boundary.
public readonly record struct ExactEcgPotential(Int128 Numerator)
{
    public long ToQ32()
    {
        Int128 value = FixedPointMath.RoundDivideTiesToEven(Numerator, 6);
        if (value < long.MinValue || value > long.MaxValue)
        { throw new EventWaveformException("EcgProjection.AmplitudeOverflow", "potential"); }
        return (long)value;
    }
}

public sealed record EcgElectrodePotentials(long RA, long LA, long RL, long LL,
    long C1, long C2, long C3, long C4, long C5, long C6);

public sealed class EcgLeadProjection
{
    private readonly ExactEcgPotential[] _leads;
    public ExactEcgPotential WilsonCentralTerminal { get; }
    public ExactEcgPotential this[EcgLead lead] => Enum.IsDefined(lead)
        ? _leads[(int)lead] : throw new EventWaveformException("EcgProjection.InvalidLead", "lead");

    private EcgLeadProjection(EcgElectrodePotentials electrodes)
    {
        // Widen before arithmetic. RL is the reference/drive electrode and
        // deliberately does not become an additional measured lead.
        Int128 ra = electrodes.RA, la = electrodes.LA, ll = electrodes.LL;
        Int128 sum = ra + la + ll;
        WilsonCentralTerminal = new(2 * sum);
        _leads = [new(6 * (la - ra)), new(6 * (ll - ra)), new(6 * (ll - la)),
            new(3 * (2 * ra - la - ll)), new(3 * (2 * la - ra - ll)), new(3 * (2 * ll - ra - la)),
            new(6 * (Int128)electrodes.C1 - 2 * sum), new(6 * (Int128)electrodes.C2 - 2 * sum),
            new(6 * (Int128)electrodes.C3 - 2 * sum), new(6 * (Int128)electrodes.C4 - 2 * sum),
            new(6 * (Int128)electrodes.C5 - 2 * sum), new(6 * (Int128)electrodes.C6 - 2 * sum)];
    }

    public static EcgLeadProjection Project(EcgElectrodePotentials electrodes)
    {
        ArgumentNullException.ThrowIfNull(electrodes);
        return new(electrodes);
    }
}

public sealed record ElectrodeWaveformPlan(EcgElectrode Electrode, IReadOnlyList<EventWaveformBand> Bands);
public sealed record ElectrodeWaveformState(IReadOnlyList<ElectrodeWaveformPlan> Electrodes,
    IReadOnlyList<PhysiologyCycleEvent> Events, EcgLimbPlacement Placement = EcgLimbPlacement.Standard);
public sealed record ProjectedEcgSample(long SimTimeNs, EcgLeadProjection Leads);

// Bounded immutable event window, with one shared event set and evaluation time
// for all electrodes. This does not invent a heart vector or a normal preset.
public sealed class ElectrodeWaveformComposition
{
    private readonly EventWaveformComposition[] _sources;
    private readonly EcgLimbPlacement _placement;
    private ElectrodeWaveformComposition(ElectrodeWaveformState state)
    {
        if (state is null || state.Electrodes is null || state.Electrodes.Count != 10 ||
            state.Events is null || state.Events.Count > EventWaveformComposition.MaximumEventCount)
        { throw new EventWaveformException("EcgProjection.InvalidElectrodes", "state"); }
        if (!Enum.IsDefined(state.Placement))
        { throw new EventWaveformException("EcgPlacement.InvalidWiring", "state"); }
        _placement = state.Placement;
        PhysiologyCycleEvent[] events = state.Events.ToArray();
        _sources = new EventWaveformComposition[10];
        for (int index = 0; index < _sources.Length; index++)
        {
            ElectrodeWaveformPlan plan = state.Electrodes[index];
            if (plan is null || plan.Electrode != (EcgElectrode)index)
            { throw new EventWaveformException("EcgProjection.InvalidElectrodes", "state"); }
            _sources[index] = EventWaveformComposition.Restore(new(plan.Bands, events));
        }
    }

    public static ElectrodeWaveformComposition Restore(ElectrodeWaveformState state) => new(state);
    public ElectrodeWaveformState CaptureState() => new(
        Array.AsReadOnly(_sources.Select((source, index) =>
            new ElectrodeWaveformPlan((EcgElectrode)index, source.CaptureState().Bands)).ToArray()),
        _sources[0].CaptureState().Events, _placement);

    public ProjectedEcgSample EvaluateAt(long simTimeNs, CancellationToken cancellationToken = default)
    {
        long[] values = new long[10];
        for (int index = 0; index < values.Length; index++)
        { values[index] = _sources[index].EvaluateAt(simTimeNs, cancellationToken); }
        var projection = EcgLeadProjection.Project(EcgLimbWiring.Apply(new(values[0], values[1], values[2], values[3],
            values[4], values[5], values[6], values[7], values[8], values[9]), _placement));
        cancellationToken.ThrowIfCancellationRequested();
        return new(simTimeNs, projection);
    }
}
