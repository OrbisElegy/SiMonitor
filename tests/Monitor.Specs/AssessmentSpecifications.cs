// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Domain.Assessment;

namespace Monitor.Specs;

internal static class AssessmentSpecifications
{
    public static Specification[] All =>
    [
        new(nameof(StandardRubricScoresExactFields), StandardRubricScoresExactFields),
        new(nameof(CustomRubricRoutesPartialAnswerToReview), CustomRubricRoutesPartialAnswerToReview),
        new(nameof(UnknownConceptIsRejected), UnknownConceptIsRejected),
        new(nameof(FormalAssessmentScopeFailsClosed), FormalAssessmentScopeFailsClosed),
    ];

    private static void StandardRubricScoresExactFields()
    {
        EcgAssessmentScorer scorer = Scorer();
        EcgAnswer reference = Answer("Rate.Normal", "Rhythm.Sinus", "Axis.Normal", "Conduction.RBBB",
            "Chamber.None", "STT.InferiorInjury", "Other.PVC");
        EcgAnswer actual = reference with
        {
            Axis = ["Axis.Left"],
            StT = ["STT.None"],
            Other = ["Other.None"],
        };
        AssessmentResult result = scorer.Score(
            AssessmentOperationMode.FormalExamination,
            AssessmentQuestionKind.EcgInterpretationStatic,
            AssessmentCaseKind.StandardBuiltIn,
            actual,
            [reference]);
        Check.That(result.Score == 60, "the frozen standard vector must score 60");
    }

    private static void CustomRubricRoutesPartialAnswerToReview()
    {
        EcgAssessmentScorer scorer = Scorer();
        EcgAnswer reference = Answer("Rate.Normal", "Rhythm.Sinus", "Axis.Normal", "Conduction.RBBB",
            "Chamber.None", "STT.InferiorInjury", "Other.PVC");
        AssessmentResult result = scorer.Score(
            AssessmentOperationMode.FormalExamination,
            AssessmentQuestionKind.EcgInterpretationStatic,
            AssessmentCaseKind.TeacherCustom,
            reference with { Other = ["Other.None", "Other.PVC"] },
            [reference]);
        Check.That(result.Status == "PendingManualReview", "partial custom mismatch requires review");
        Check.That(result.Score is null, "custom mismatch must not become zero");
    }

    private static void UnknownConceptIsRejected()
    {
        EcgAssessmentScorer scorer = Scorer();
        EcgAnswer reference = Answer("Rate.Normal", "Rhythm.Sinus", "Axis.Normal", "Conduction.RBBB",
            "Chamber.None", "STT.InferiorInjury", "Other.PVC");
        AssessmentResult result = scorer.Score(
            AssessmentOperationMode.FormalExamination,
            AssessmentQuestionKind.EcgInterpretationStatic,
            AssessmentCaseKind.StandardBuiltIn,
            reference with { Rhythm = ["Rhythm.Unknown"] },
            [reference]);
        Check.That(result.Status == "Rejected" && result.Method == "UnknownConcept",
            "unknown concepts must fail closed");
    }

    private static void FormalAssessmentScopeFailsClosed()
    {
        EcgAssessmentScorer scorer = Scorer();
        bool rejected = false;
        try
        {
            scorer.Score(
                AssessmentOperationMode.FormalExamination,
                AssessmentQuestionKind.DefibrillatorScenario,
                AssessmentCaseKind.StandardBuiltIn,
                Answer("Rate.Normal", "Rhythm.Sinus", "Axis.Normal", "Conduction.RBBB",
                    "Chamber.None", "STT.None", "Other.None"),
                [Answer("Rate.Normal", "Rhythm.Sinus", "Axis.Normal", "Conduction.RBBB",
                    "Chamber.None", "STT.None", "Other.None")]);
        }
        catch (InvalidOperationException exception) when (exception.Message == "assessment.question_kind.not_armed")
        {
            rejected = true;
        }

        Check.That(rejected, "formal defibrillator scoring must fail closed");
    }

    private static EcgAssessmentScorer Scorer()
    {
        string[] concepts =
        [
            "Rate.Normal", "Rhythm.Sinus", "Axis.Normal", "Axis.Left", "Conduction.RBBB",
            "Chamber.None", "STT.None", "STT.InferiorInjury", "Other.None", "Other.PVC",
        ];
        Dictionary<string, string> aliases = concepts.ToDictionary(static value => value, StringComparer.Ordinal);
        return new EcgAssessmentScorer(new ConceptNormalizer(aliases));
    }

    private static EcgAnswer Answer(
        string rate,
        string rhythm,
        string axis,
        string conduction,
        string chamber,
        string stT,
        string other) => new(rate, [rhythm], [axis], [conduction], [chamber], [stT], [other]);
}
