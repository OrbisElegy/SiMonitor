// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public readonly record struct ArterialOxygenationSample(long SourceSimTimeNs, int SaturationMilliPercent);

// A pure read at the requested physiological source time. Implementations must
// retain/reconstruct that time or reject it; never substitute their latest state.
// Acquisition and pulse-transit delays must not be added by this interface.
public interface IArterialOxygenationSource
{
    public ArterialOxygenationSample ReadAt(long sourceSimTimeNs);
}

public sealed record SampledArterialOxygenationState(long StartSimTimeNs, long SampleStepNs,
    IReadOnlyList<int> SaturationMilliPercent, string ModelId = "SampledArterialOxygenation@1");

// An owned, bounded prototype-trace adapter. Linear interpolation is explicit;
// no extrapolation, implicit target reset or history-dependent random draw.
public sealed class SampledArterialOxygenation : IArterialOxygenationSource
{
    public const int MaximumSamples = 450001;
    private readonly int[] _samples;
    public long StartSimTimeNs { get; }
    public long SampleStepNs { get; }
    public long EndSimTimeNs { get; }

    public SampledArterialOxygenation(SampledArterialOxygenationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.ModelId != "SampledArterialOxygenation@1" || state.StartSimTimeNs < 0 ||
            state.SampleStepNs is < 1_000_000 or > 1_000_000_000 ||
            state.SaturationMilliPercent is not { Count: >= 2 and <= MaximumSamples } values)
        { throw new ArgumentException("Oxygenation.InvalidTrace", nameof(state)); }
        long end = checked(state.StartSimTimeNs + (values.Count - 1L) * state.SampleStepNs);
        _samples = values.ToArray();
        if (_samples.Any(value => value is < 0 or > 100000))
        { throw new ArgumentException("Oxygenation.InvalidSaturation", nameof(state)); }
        StartSimTimeNs = state.StartSimTimeNs;
        SampleStepNs = state.SampleStepNs;
        EndSimTimeNs = end;
    }

    public SampledArterialOxygenationState CaptureState() => new(StartSimTimeNs, SampleStepNs, Array.AsReadOnly(_samples));

    public ArterialOxygenationSample ReadAt(long sourceSimTimeNs)
    {
        if (sourceSimTimeNs < StartSimTimeNs || sourceSimTimeNs > EndSimTimeNs)
        { throw new ArgumentOutOfRangeException(nameof(sourceSimTimeNs), "Oxygenation.HistoryUnavailable"); }
        long elapsed = sourceSimTimeNs - StartSimTimeNs;
        int index = (int)(elapsed / SampleStepNs);
        long remainder = elapsed % SampleStepNs;
        int value = _samples[index];
        if (remainder != 0)
        {
            value = (int)FixedPointMath.RoundDivideTiesToEven(
                (Int128)value * (SampleStepNs - remainder) + (Int128)_samples[index + 1] * remainder, SampleStepNs);
        }
        return new(sourceSimTimeNs, value);
    }
}
