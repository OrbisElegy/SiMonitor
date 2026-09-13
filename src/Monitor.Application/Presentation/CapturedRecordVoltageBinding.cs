// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Presentation;
using Monitor.Simulation.Acquisition;

namespace Monitor.Application.Presentation;

// Trusted local resolution, not a new profile wire format or authenticated import.
public sealed record ResolvedEcgVoltageChannel(uint SampleRateNumerator, uint SampleRateDenominator,
    EcgRawVoltageCalibration Calibration);

public sealed class CapturedRecordVoltageBinding
{
    private readonly CapturedRecordBinding _record;
    public RecordSlotBinding Slot { get; }
    public ResolvedEcgVoltageChannel Channel { get; }

    public CapturedRecordVoltageBinding(CapturedRecordBinding record, string slotId, ResolvedEcgVoltageChannel channel)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (channel is null || channel.Calibration is null ||
            channel.SampleRateNumerator is 0 or > 1_000_000 || channel.SampleRateDenominator is 0 or > 1_000_000)
        { throw new EcgRawVoltageException("EcgRawVoltage.InvalidChannel", nameof(channel)); }
        channel.Calibration.Validate();
        Slot = record.Slots.SingleOrDefault(slot => slot.SlotId == slotId) ??
            throw new EcgRawVoltageException("EcgRawVoltage.UnknownSlot", nameof(slotId));
        _record = record;
        Channel = channel;
        ArchivedWaveformChannelShape shape = record.ReadChannelShape(Slot.ChannelId);
        EcgRawVoltageCalibration calibration = channel.Calibration;
        if (shape.SampleRateNumerator != channel.SampleRateNumerator || shape.SampleRateDenominator != channel.SampleRateDenominator ||
            shape.ScaleNumerator != calibration.ScaleNumerator || shape.ScaleDenominator != calibration.ScaleDenominator ||
            shape.OffsetNumerator != calibration.OffsetNumerator || shape.OffsetDenominator != calibration.OffsetDenominator)
        { throw new EcgRawVoltageException("EcgRawVoltage.ProfileMismatch", nameof(channel)); }
    }

    internal void RequireBinding(CapturedRecordBinding record, RecordSlotBinding slot)
    {
        if (!ReferenceEquals(record, _record) || slot != Slot)
        { throw new EcgRawVoltageException("EcgRawVoltage.ForeignBinding", nameof(record)); }
    }

    internal void RequirePlane(WaveformPlane plane)
    {
        EcgRawVoltageCalibration calibration = Channel.Calibration;
        if (plane.ChannelId != Slot.ChannelId || plane.SampleRateNumerator != Channel.SampleRateNumerator ||
            plane.SampleRateDenominator != Channel.SampleRateDenominator ||
            plane.ScaleNumerator != calibration.ScaleNumerator || plane.ScaleDenominator != calibration.ScaleDenominator ||
            plane.OffsetNumerator != calibration.OffsetNumerator || plane.OffsetDenominator != calibration.OffsetDenominator)
        { throw new EcgRawVoltageException("EcgRawVoltage.ProfileMismatch", nameof(plane)); }
    }
}

public sealed record CapturedRecordVoltageBlock(CapturedRecordWaveformHorizontalBlock Source,
    IReadOnlyList<EcgSampleVoltage> Voltages, IReadOnlyList<EcgVerticalPosition> SampleY);

public sealed record CapturedRecordVoltagePageDisplay(CapturedRecordWaveformHorizontalPageDisplay Content,
    EcgVerticalScale? VerticalScale, ResolvedEcgVoltageChannel? VoltageChannel,
    IReadOnlyList<CapturedRecordVoltageBlock> Blocks);

internal static class CapturedRecordVoltageProjection
{
    internal static CapturedRecordVoltagePageDisplay Build(CapturedRecordWaveformHorizontalPageDisplay content,
        CapturedRecordVoltageBinding binding, EcgVerticalScale scale, CancellationToken cancellationToken)
    {
        List<CapturedRecordVoltageBlock> blocks = [];
        foreach (CapturedRecordWaveformHorizontalBlock block in content.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            binding.RequirePlane(block.Source.Plane);
            var voltages = new EcgSampleVoltage[block.SampleX.Count];
            var positions = new EcgVerticalPosition[voltages.Length];
            for (int index = 0; index < voltages.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EcgSampleVoltage voltage = binding.Channel.Calibration.Convert(block.Source.Plane.Samples[index]);
                voltages[index] = voltage;
                positions[index] = EcgVerticalGeometry.MapMicrovolts(scale, voltage.NumeratorMicrovolts, voltage.Denominator);
            }
            blocks.Add(new(block, Array.AsReadOnly(voltages), Array.AsReadOnly(positions)));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(content, scale, binding.Channel, blocks.AsReadOnly());
    }
}
