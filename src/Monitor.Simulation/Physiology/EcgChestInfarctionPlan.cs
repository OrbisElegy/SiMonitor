// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum InfarctionIllustrationStage
{
    None, HyperacuteT, HyperacuteInjury, AcuteMonophasic, AcuteQInvertedT,
    AcuteQsInvertedT, SubacuteDeepT, SubacuteRecoveringT, OldQNormalT, OldQInvertedT, OldQLowT,
}

// Explicit teaching snapshots. There is no elapsed-time or treatment rule here.
public sealed record EcgChestInfarctionPlan(int ChestMask, InfarctionIllustrationStage Stage)
{
    private static readonly int[] LimbIndices = [0, 1, 3];
    public const string EvidenceId = "ChestInfarctionIllustrationDraft@1";

    internal IReadOnlyList<ElectrodeWaveformPlan> Apply(IReadOnlyList<ElectrodeWaveformPlan> current, EcgCycleTiming timing)
    {
        if (ChestMask is < 0 or > 63 || !Enum.IsDefined(Stage)) { throw Invalid(); }
        if (ChestMask == 0 || Stage == InfarctionIllustrationStage.None) { return current; }
        var standard = TextbookElectrodeReference.CreateElectrodes(timing: timing);
        // Carry the actual limb ventricular reference through to chest
        // electrodes. The selected lead target is independent of Wilson.
        var reference = LimbIndices.SelectMany(i => current[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.VentricularElectrical))
            .Select(b => b with { TableQ32 = Scale(b.TableQ32, 1, 3) }).ToArray();
        var output = current.ToArray();
        for (int i = 4; i < 10; i++)
        {
            if ((ChestMask & (1 << (i - 4))) == 0) { continue; }
            IReadOnlyList<long> Projected(int band) => Array.AsReadOnly(Enumerable.Range(0, standard[i].Bands[band].TableQ32.Count)
                .Select(k => checked((long)FixedPointMath.RoundDivideTiesToEven(
                    (Int128)standard[i].Bands[band].TableQ32[k] * 3 - standard[0].Bands[band].TableQ32[k] -
                    standard[1].Bands[band].TableQ32[k] - standard[3].Bands[band].TableQ32[k], 3))).ToArray());
            long qrsDuration = Stage == InfarctionIllustrationStage.HyperacuteInjury
                ? checked((long)FixedPointMath.RoundDivideTiesToEven((Int128)timing.QrsDurationNs * 6, 5)) : timing.QrsDurationNs;
            var localTiming = timing with { QrsDurationNs = qrsDuration };
            localTiming.Validate();
            IReadOnlyList<long> qrs = Stage switch
            {
                InfarctionIllustrationStage.HyperacuteT => Projected(1),
                InfarctionIllustrationStage.HyperacuteInjury => Scale(Projected(1), 5, 4),
                InfarctionIllustrationStage.AcuteMonophasic or InfarctionIllustrationStage.AcuteQsInvertedT => InfarctionIllustrationTables.QS,
                _ => InfarctionIllustrationTables.QWithReducedR,
            };
            List<EventWaveformBand> targets = [new(PhysiologyCycleEventKind.VentricularElectrical, 0, qrsDuration, qrs)];
            if (Stage is InfarctionIllustrationStage.HyperacuteInjury or InfarctionIllustrationStage.AcuteMonophasic)
            {
                var contour = Stage == InfarctionIllustrationStage.HyperacuteInjury
                    ? new EcgStTFusionContour(250, 800, 600) : new EcgStTFusionContour(500, 650, 650);
                targets.Add(new EcgStTFusionPlan(Enumerable.Repeat<EcgStTFusionContour?>(contour, 10).ToArray()).CreateBands(localTiming)[i]!);
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
                var t = Stage == InfarctionIllustrationStage.OldQNormalT ? Projected(2) :
                    Scale(TextbookEcgTables.T, (Int128)amplitude * FixedPointMath.Q32One, TextbookEcgTables.T.Max());
                targets.Add(new(PhysiologyCycleEventKind.VentricularElectrical, timing.TOffsetFromQrsNs, timing.TDurationNs, t));
                if (Stage is InfarctionIllustrationStage.AcuteQInvertedT or InfarctionIllustrationStage.AcuteQsInvertedT)
                {
                    var st = new EcgStSegmentPlan(Enumerable.Repeat(200, 10).ToArray(), Enumerable.Repeat(100, 10).ToArray());
                    targets.AddRange(st.CreateBands(localTiming)[i]!);
                }
            }
            output[i] = current[i] with
            {
                Bands = Array.AsReadOnly(current[i].Bands.Where(b => b.Trigger == PhysiologyCycleEventKind.AtrialElectrical)
                    .Concat(targets).Concat(reference).ToArray()),
            };
        }
        return Array.AsReadOnly(output);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<long> Scale(IReadOnlyList<long> table, Int128 numerator, Int128 denominator) =>
        Array.AsReadOnly(table.Select(v => checked((long)FixedPointMath.RoundDivideTiesToEven(v * numerator, denominator))).ToArray());
    private static EventWaveformException Invalid() => new("EcgInfarction.InvalidPlan", "infarction");
}
