// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

// Established regular SVT with optional fixed bundle-block morphology, not a reentry mechanism.
public static class SupraventricularTachycardiaReference
{
    public const string EvidenceId = "NarrowComplexSvtIllustration@1";
    // PR40 is a component-builder placeholder, not an observed PR interval.
    public static EcgCycleTiming Timing { get; } = new(300_000_000, 40_000_000,
        40_000_000, 80_000_000, 240_000_000, 100_000_000);
    public const string RightBundleEvidenceId = "SvtRightBundleBlockIllustration@1";
    public const string LeftBundleEvidenceId = "SvtLeftBundleBlockIllustration@1";
    public static EcgCycleTiming ResolveTiming(bool rightBundleBlock = false, bool leftBundleBlock = false)
    {
        if (rightBundleBlock && leftBundleBlock) { throw new EventWaveformException("Svt.ConflictingModes", "bundleBlock"); }
        return rightBundleBlock || leftBundleBlock
            ? Timing with { QrsDurationNs = 140_000_000, QtIntervalNs = 260_000_000 } : Timing;
    }
    public static RegularPhysiologyPlan CreatePlan() => new(0, Timing.RrIntervalNs,
        0, 80_000_000, 80_000_000, 3_750_000_000, 1_875_000_000,
        ConductionPattern: AvConductionPattern.NarrowComplexSvtIllustration);
    public static IReadOnlyList<ElectrodeWaveformPlan> CreateElectrodes(bool rightBundleBlock = false, bool leftBundleBlock = false)
    {
        var timing = ResolveTiming(rightBundleBlock, leftBundleBlock);
        return Array.AsReadOnly((leftBundleBlock ? LeftBundleBlockReference.CreateElectrodes(timing) : rightBundleBlock ? RightBundleBlockReference.CreateElectrodes(timing) : TextbookElectrodeReference.CreateElectrodes(timing: timing)).Select(e => e with
        {
            Bands = Array.AsReadOnly(e.Bands.Select(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical
                ? b with { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked((long)FixedPointMath.RoundDivideTiesToEven(-(Int128)v, 2))).ToArray()) }
                : b).ToArray())
        }).ToArray());
    }
    public static IReadOnlyList<EventWaveformBand> CreateLeadIIBands(bool rightBundleBlock = false, bool leftBundleBlock = false)
    {
        var e = CreateElectrodes(rightBundleBlock, leftBundleBlock);
        return Array.AsReadOnly(e[(int)EcgElectrode.LL].Bands.Concat(e[(int)EcgElectrode.RA].Bands.Select(b => b with
        { TableQ32 = Array.AsReadOnly(b.TableQ32.Select(v => checked(-v)).ToArray()) })).ToArray());
    }
}
