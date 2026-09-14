// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Simulation.Physiology;

// Structural timing, not a clinical normality classifier or QT correction rule.
public sealed record EcgCycleTiming(long RrIntervalNs, long PDurationNs, long PrIntervalNs,
    long QrsDurationNs, long QtIntervalNs, long TDurationNs)
{
    public long TOffsetFromQrsNs { get { Validate(); return QtIntervalNs - TDurationNs; } }
    public long StDurationNs { get { Validate(); return QtIntervalNs - TDurationNs - QrsDurationNs; } }

    public void Validate()
    {
        if (RrIntervalNs <= 0 || PDurationNs <= 0 || PrIntervalNs < PDurationNs ||
            PrIntervalNs >= RrIntervalNs || QrsDurationNs <= 0 || TDurationNs <= 0 ||
            QtIntervalNs < TDurationNs || QtIntervalNs - TDurationNs < QrsDurationNs ||
            QtIntervalNs > RrIntervalNs - PrIntervalNs)
        { throw new EventWaveformException("EcgTiming.InconsistentIntervals", "timing"); }
    }
}

public static class TextbookEcgReference
{
    public const string EvidenceId = "TextbookEcgReferenceDraft@3";
    public const string SourceValueUnit = "microvolt";
    public static EcgCycleTiming Timing { get; } = TextbookEcgTables.Timing;

    public static IReadOnlyList<EventWaveformBand> CreateBands(EcgCycleTiming? timing = null)
    {
        timing ??= Timing;
        timing.Validate();
        return Array.AsReadOnly(new EventWaveformBand[]
        {
            new(PhysiologyCycleEventKind.AtrialElectrical, 0, timing.PDurationNs, TextbookEcgTables.P),
            new(PhysiologyCycleEventKind.VentricularElectrical, 0, timing.QrsDurationNs, TextbookEcgTables.Qrs),
            new(PhysiologyCycleEventKind.VentricularElectrical, timing.TOffsetFromQrsNs, timing.TDurationNs, TextbookEcgTables.T),
        });
    }
}
