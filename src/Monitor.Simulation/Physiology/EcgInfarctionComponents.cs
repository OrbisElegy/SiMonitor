// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Simulation.Determinism;

namespace Monitor.Simulation.Physiology;

public enum NecrosisIllustrationShape { Reference, QWithReducedR, QS }

// Independent source-shape choices, not severity or tissue-size measurements.
public sealed record EcgInfarctionComponents(NecrosisIllustrationShape Necrosis = NecrosisIllustrationShape.Reference,
    int? TPeakMicrovolts = null, int JMicrovolts = 0, int StEndMicrovolts = 0, int StArchMicrovolts = 0,
    int QrsTemplatePermille = 1000, EcgQrsContributionLoss? ContributionLoss = null)
{
    internal void Validate()
    {
        ContributionLoss?.Validate();
        if (ContributionLoss is not null && (Necrosis != NecrosisIllustrationShape.Reference || QrsTemplatePermille != 1000))
        { throw new EventWaveformException("EcgQrs.ConflictingAuthoringModes", "components"); }
        if (!Enum.IsDefined(Necrosis) || QrsTemplatePermille is < 0 or > 1000 || TPeakMicrovolts is < -4000 or > 4000 ||
            JMicrovolts is < -4000 or > 4000 || StEndMicrovolts is < -4000 or > 4000 ||
            StArchMicrovolts is < -4000 or > 4000)
        { throw new EventWaveformException("EcgInfarction.InvalidComponents", "components"); }
    }

    internal List<EventWaveformBand> CreateTargets(EcgCycleTiming timing, Func<int, IReadOnlyList<long>> projected)
    {
        Validate();
        IReadOnlyList<long> qrs = Necrosis switch
        {
            NecrosisIllustrationShape.QWithReducedR => InfarctionIllustrationTables.QWithReducedR,
            NecrosisIllustrationShape.QS => InfarctionIllustrationTables.QS,
            _ => projected(1),
        };
        if (Necrosis != NecrosisIllustrationShape.Reference && QrsTemplatePermille != 1000)
        {
            var reference = projected(1);
            // Blend the projected QRS target, not the entire electrode signal.
            // The weight is an authoring control, not a myocardial tissue ratio.
            qrs = QrsTemplatePermille == 0 ? reference : Array.AsReadOnly(qrs.Zip(reference,
                (target, original) => checked((long)FixedPointMath.RoundDivideTiesToEven(
                    (Int128)target * QrsTemplatePermille + (Int128)original * (1000 - QrsTemplatePermille), 1000))).ToArray());
        }
        IReadOnlyList<long> t = TPeakMicrovolts is { } amplitude
            ? Array.AsReadOnly(TextbookEcgTables.T.Select(value => checked((long)FixedPointMath.RoundDivideTiesToEven(
                (Int128)value * amplitude * FixedPointMath.Q32One, TextbookEcgTables.T.Max()))).ToArray()) : projected(2);
        List<EventWaveformBand> result =
        [
            new(PhysiologyCycleEventKind.VentricularElectrical, 0, timing.QrsDurationNs, qrs),
            new(PhysiologyCycleEventKind.VentricularElectrical, timing.TOffsetFromQrsNs, timing.TDurationNs, t),
        ];
        if (ContributionLoss?.CreateBand(timing) is { } loss) { result.Add(loss); }
        var st = new EcgStSegmentPlan(Enumerable.Repeat(JMicrovolts, 10).ToArray(),
            Enumerable.Repeat(StEndMicrovolts, 10).ToArray(), Enumerable.Repeat(StArchMicrovolts, 10).ToArray());
        if (st.CreateBands(timing)[4] is { } bands) { result.AddRange(bands); }
        return result;
    }
}
