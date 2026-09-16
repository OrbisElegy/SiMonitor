// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum InfarctionIllustrationStage
{
    None, HyperacuteT, HyperacuteInjury, AcuteMonophasic, AcuteQInvertedT,
    AcuteQsInvertedT, SubacuteDeepT, SubacuteRecoveringT, OldQNormalT, OldQInvertedT, OldQLowT,
}

public enum InfarctionTerritory { CustomChest, Inferior, Lateral, Anteroseptal, Anterior, ExtensiveAnterior }

// Explicit teaching snapshots. There is no elapsed-time or treatment rule here.
public sealed record EcgChestInfarctionPlan(int ChestMask, InfarctionIllustrationStage Stage, InfarctionTerritory Territory = InfarctionTerritory.CustomChest, long RepolarizationDelayNs = 0)
{
    private static readonly int[] LimbIndices = [0, 1, 3];
    public const string EvidenceId = "ChestInfarctionIllustrationDraft@3";

    internal IReadOnlyList<ElectrodeWaveformPlan> Apply(IReadOnlyList<ElectrodeWaveformPlan> current, EcgCycleTiming timing)
    {
        if (ChestMask is < 0 or > 63 || !Enum.IsDefined(Stage) || !Enum.IsDefined(Territory) || RepolarizationDelayNs < 0) { throw Invalid(); }
        int mask = Territory switch
        {
            InfarctionTerritory.Inferior => 0,
            InfarctionTerritory.Lateral => 48,
            InfarctionTerritory.Anteroseptal => 7,
            InfarctionTerritory.Anterior => 28,
            InfarctionTerritory.ExtensiveAnterior => 31,
            _ => ChestMask,
        };
        if ((mask == 0 && Territory != InfarctionTerritory.Inferior) || Stage == InfarctionIllustrationStage.None) { return current; }
        _ = ResolveRepolarizationTiming(timing);
        var standard = TextbookElectrodeReference.CreateElectrodes(timing: timing);
        // Carry the actual limb ventricular reference through to chest
        // electrodes. The selected lead target is independent of Wilson.
        var reference = LimbIndices.SelectMany(i => current[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical))
            .Select(b => b with { TableQ32 = Scale(b.TableQ32, 1, 3) }).ToArray();
        var output = current.ToArray();
        for (int i = 4; i < 10; i++)
        {
            if ((mask & (1 << (i - 4))) == 0) { continue; }
            IReadOnlyList<long> Projected(int band) => Array.AsReadOnly(Enumerable.Range(0, standard[i].Bands[band].TableQ32.Count)
                .Select(k => checked((long)FixedPointMath.RoundDivideTiesToEven(
                    (Int128)standard[i].Bands[band].TableQ32[k] * 3 - standard[0].Bands[band].TableQ32[k] -
                    standard[1].Bands[band].TableQ32[k] - standard[3].Bands[band].TableQ32[k], 3))).ToArray());
            var targets = CreateTargets(timing, Projected);
            output[i] = current[i] with
            {
                Bands = Array.AsReadOnly(current[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical)
                    .Concat(targets).Concat(reference).ToArray()),
            };
        }
        if (Territory is InfarctionTerritory.Inferior or InfarctionTerritory.Lateral)
        {
            int positive = Territory == InfarctionTerritory.Inferior ? 3 : 1;
            IReadOnlyList<long> ProjectedLimb(int band) => Array.AsReadOnly(standard[positive].Bands[band].TableQ32
                .Zip(standard[0].Bands[band].TableQ32, (left, right) => left - right).ToArray());
            var delta = CreateTargets(timing, ProjectedLimb)
                .Concat(current[0].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical))
                .Concat(current[positive].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical)
                    .Select(b => b with { TableQ32 = Scale(b.TableQ32, -1, 1) })).ToArray();
            // Zero-sum limb change keeps Wilson and every untouched chest lead
            // exactly unchanged. Coupled augmented/opposite leads still respond.
            var thirds = delta.Select(b => b with { TableQ32 = Scale(b.TableQ32, 1, 3) }).ToArray();
            foreach (int limb in LimbIndices)
            {
                var additions = limb == positive ? thirds.Concat(thirds) :
                    thirds.Select(b => b with { TableQ32 = Scale(b.TableQ32, -1, 1) });
                output[limb] = current[limb] with { Bands = Array.AsReadOnly(current[limb].Bands.Concat(additions).ToArray()) };
            }
        }
        return Array.AsReadOnly(output);
    }

    private List<EventWaveformBand> CreateTargets(EcgCycleTiming timing, Func<int, IReadOnlyList<long>> projected)
    {
        timing = ResolveRepolarizationTiming(timing);
        long qrsDuration = Stage == InfarctionIllustrationStage.HyperacuteInjury
            ? checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)timing.QrsDurationNs * 6, 5)) : timing.QrsDurationNs;
        var localTiming = timing with { QrsDurationNs = qrsDuration };
        localTiming.Validate();
        IReadOnlyList<long> qrs = Stage switch
        {
            InfarctionIllustrationStage.HyperacuteT => projected(1),
            InfarctionIllustrationStage.HyperacuteInjury => Scale(projected(1), 5, 4),
            InfarctionIllustrationStage.AcuteMonophasic or InfarctionIllustrationStage.AcuteQsInvertedT => InfarctionIllustrationTables.QS,
            _ => InfarctionIllustrationTables.QWithReducedR,
        };
        List<EventWaveformBand> targets = [new(PhysiologyCycleEventKind.VentricularElectrical, 0, qrsDuration, qrs)];
        if (Stage is InfarctionIllustrationStage.HyperacuteInjury or InfarctionIllustrationStage.AcuteMonophasic)
        {
            var contour = Stage == InfarctionIllustrationStage.HyperacuteInjury
                ? new EcgStTFusionContour(250, 800, 600) : new EcgStTFusionContour(500, 650, 650);
            targets.Add(new EcgStTFusionPlan(Enumerable.Repeat<EcgStTFusionContour?>(contour, 10).ToArray()).CreateBands(localTiming)[4]!);
        }
        else
        {
            int amplitude = Stage switch
            {
                InfarctionIllustrationStage.HyperacuteT => 900,
                InfarctionIllustrationStage.AcuteQInvertedT or InfarctionIllustrationStage.AcuteQsInvertedT or InfarctionIllustrationStage.SubacuteDeepT => -500,
                InfarctionIllustrationStage.SubacuteRecoveringT => -200,
                InfarctionIllustrationStage.OldQInvertedT => -150,
                InfarctionIllustrationStage.OldQLowT => 50,
                _ => 0,
            };
            var t = Stage == InfarctionIllustrationStage.OldQNormalT ? projected(2) :
                Scale(TextbookEcgTables.T, (Int128)amplitude * FixedPointMath.Q32One, TextbookEcgTables.T.Max());
            targets.Add(new(PhysiologyCycleEventKind.VentricularElectrical, timing.TOffsetFromQrsNs, timing.TDurationNs, t));
            if (Stage is InfarctionIllustrationStage.AcuteQInvertedT or InfarctionIllustrationStage.AcuteQsInvertedT)
            {
                var st = new EcgStSegmentPlan(Enumerable.Repeat(200, 10).ToArray(), Enumerable.Repeat(100, 10).ToArray());
                targets.AddRange(st.CreateBands(localTiming)[4]!);
            }
        }
        return targets;
    }

    internal EcgCycleTiming ResolveRepolarizationTiming(EcgCycleTiming timing)
    {
        if (RepolarizationDelayNs < 0 || (Int128)timing.QtIntervalNs + RepolarizationDelayNs > long.MaxValue ||
            (Int128)timing.TDurationNs + RepolarizationDelayNs > long.MaxValue) { throw Invalid(); }
        var extended = timing with
        {
            QtIntervalNs = timing.QtIntervalNs + RepolarizationDelayNs,
            TDurationNs = timing.TDurationNs + RepolarizationDelayNs,
        };
        extended.Validate();
        return extended;
    }

    public bool HasActiveRegion => Stage != InfarctionIllustrationStage.None &&
        (Territory != InfarctionTerritory.CustomChest || ChestMask != 0);

    private static System.Collections.ObjectModel.ReadOnlyCollection<long> Scale(IReadOnlyList<long> table, Int128 numerator, Int128 denominator) =>
        Array.AsReadOnly(table.Select(v => checked((long)FixedPointMath.RoundDivideTiesToEven(v * numerator, denominator))).ToArray());
    private static EventWaveformException Invalid() => new("EcgInfarction.InvalidPlan", "infarction");
}
