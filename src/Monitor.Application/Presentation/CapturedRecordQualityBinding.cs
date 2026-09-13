// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Acquisition;

namespace Monitor.Application.Presentation;

public sealed class CapturedRecordQualityException(string reasonCode, string parameterName)
    : ArgumentException(reasonCode, parameterName)
{
    public string ReasonCode { get; } = reasonCode;
}

// Exact whole-word matching. No built-in meanings, subset masks or zero default.
public sealed record RecordQualityRule(uint QualityFlags, bool Drawable);
public sealed record RecordSampleQuality(uint QualityFlags, bool Drawable);

public sealed class CapturedRecordQualityBinding
{
    private readonly CapturedRecordBinding _record;
    private readonly Dictionary<uint, bool> _lookup;
    public RecordSlotBinding Slot { get; }
    public IReadOnlyList<RecordQualityRule> Rules { get; }

    // Trusted local policy resolution; this is not a frozen profile import format.
    public CapturedRecordQualityBinding(CapturedRecordBinding record, string slotId,
        IReadOnlyList<RecordQualityRule> rules, int maximumRules)
    {
        ArgumentNullException.ThrowIfNull(record);
        int count = rules?.Count ?? 0;
        if (maximumRules <= 0 || rules is null || count <= 0 || count > maximumRules)
        { throw new CapturedRecordQualityException("RecordQuality.InvalidRules", nameof(rules)); }
        var copied = new RecordQualityRule[count];
        Dictionary<uint, bool> lookup = [];
        uint? previous = null;
        for (int index = 0; index < count; index++)
        {
            RecordQualityRule rule = rules[index];
            if (rule is null || previous is { } value && rule.QualityFlags <= value)
            { throw new CapturedRecordQualityException("RecordQuality.InvalidRules", nameof(rules)); }
            lookup.Add(rule.QualityFlags, rule.Drawable);
            copied[index] = rule;
            previous = rule.QualityFlags;
        }
        Slot = record.Slots.SingleOrDefault(slot => slot.SlotId == slotId) ??
            throw new CapturedRecordQualityException("RecordQuality.UnknownSlot", nameof(slotId));
        _record = record;
        Rules = Array.AsReadOnly(copied);
        _lookup = lookup;
    }

    internal void RequireBinding(CapturedRecordBinding record, RecordSlotBinding slot)
    {
        if (!ReferenceEquals(_record, record) || Slot != slot)
        { throw new CapturedRecordQualityException("RecordQuality.ForeignBinding", nameof(record)); }
    }

    internal RecordSampleQuality Resolve(uint flags)
    {
        if (!_lookup.TryGetValue(flags, out bool drawable))
        { throw new CapturedRecordQualityException("RecordQuality.UnknownFlags", nameof(flags)); }
        return new(flags, drawable);
    }
}

public sealed record CapturedRecordQualityBlock(CapturedRecordVoltageBlock Source,
    IReadOnlyList<RecordSampleQuality> Samples);

public sealed record CapturedRecordQualityPageDisplay(CapturedRecordVoltagePageDisplay Content,
    IReadOnlyList<RecordQualityRule> Rules, IReadOnlyList<CapturedRecordQualityBlock> Blocks);

internal static class CapturedRecordQualityProjection
{
    internal static CapturedRecordQualityPageDisplay Build(CapturedRecordVoltagePageDisplay content,
        CapturedRecordQualityBinding binding, CancellationToken cancellationToken)
    {
        List<CapturedRecordQualityBlock> blocks = [];
        foreach (CapturedRecordVoltageBlock block in content.Blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaveformPlane plane = block.Source.Source.Plane;
            var samples = new RecordSampleQuality[plane.Samples.Count];
            int rangeIndex = 0;
            for (int index = 0; index < samples.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                while (rangeIndex < plane.QualityRanges.Count &&
                    (long)plane.QualityRanges[rangeIndex].FirstSampleOffset + plane.QualityRanges[rangeIndex].Count <= index)
                { rangeIndex++; }
                uint flags = rangeIndex < plane.QualityRanges.Count && plane.QualityRanges[rangeIndex].FirstSampleOffset <= index
                    ? plane.QualityRanges[rangeIndex].QualityFlags : 0;
                samples[index] = binding.Resolve(flags);
            }
            blocks.Add(new(block, Array.AsReadOnly(samples)));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(content, binding.Rules, blocks.AsReadOnly());
    }
}
