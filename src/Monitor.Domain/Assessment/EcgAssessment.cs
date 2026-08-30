// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text;

namespace Monitor.Domain.Assessment;

public enum AssessmentOperationMode
{
    PracticeOnly,
    FormalExamination,
}

public enum AssessmentQuestionKind
{
    EcgInterpretationStatic,
    RealtimeMonitor,
    DefibrillatorScenario,
    PacerScenario,
}

public enum AssessmentCaseKind
{
    StandardBuiltIn,
    TeacherCustom,
}

public sealed record EcgAnswer(
    string? RateClass,
    IReadOnlyCollection<string> Rhythm,
    IReadOnlyCollection<string> Axis,
    IReadOnlyCollection<string> Conduction,
    IReadOnlyCollection<string> Chamber,
    IReadOnlyCollection<string> StT,
    IReadOnlyCollection<string> Other);

public sealed record FieldScore(string Field, int Possible, int Awarded, bool ExactMatch);

public sealed record AssessmentResult(
    string Status,
    int? Score,
    string Method,
    IReadOnlyList<FieldScore> Fields);

public sealed class ConceptNormalizer
{
    private readonly Dictionary<string, string> _aliases;

    public ConceptNormalizer(IReadOnlyDictionary<string, string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        foreach ((string alias, string conceptId) in aliases)
        {
            if (!normalized.TryAdd(NormalizeAlias(alias), conceptId))
            {
                throw new ArgumentException($"Duplicate normalized alias: {alias}", nameof(aliases));
            }
        }

        _aliases = normalized;
    }

    public string? NormalizeOne(string? value)
    {
        if (value is null)
        {
            return null;
        }

        return Resolve(value);
    }

    public IReadOnlyList<string> Normalize(IReadOnlyCollection<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        SortedSet<string> normalized = new(StringComparer.Ordinal);
        foreach (string value in values)
        {
            normalized.Add(Resolve(value));
        }

        return normalized.ToArray();
    }

    private string Resolve(string value)
    {
        if (!_aliases.TryGetValue(NormalizeAlias(value), out string? conceptId))
        {
            throw new UnknownConceptException(value);
        }

        return conceptId;
    }

    private static string NormalizeAlias(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string input = value.Normalize(NormalizationForm.FormKC).Trim();
        System.Text.StringBuilder output = new(input.Length);
        bool previousWasSpace = false;
        foreach (char character in input)
        {
            bool isSpace = char.IsWhiteSpace(character);
            if (!isSpace)
            {
                output.Append(character);
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                output.Append(' ');
                previousWasSpace = true;
            }
        }

        return output.ToString();
    }
}

internal sealed class UnknownConceptException : Exception
{
    public UnknownConceptException(string concept)
        : base($"Unknown concept alias: {concept}")
    {
    }
}

public sealed class EcgAssessmentScorer
{
    private static readonly (string Name, int Weight)[] Weights =
    [
        ("rate", 10),
        ("rhythm", 25),
        ("axis", 10),
        ("conduction", 15),
        ("chamber", 10),
        ("st_t", 20),
        ("other", 10),
    ];

    private readonly ConceptNormalizer _normalizer;

    public EcgAssessmentScorer(ConceptNormalizer normalizer)
    {
        _normalizer = normalizer;
    }

    public AssessmentResult Score(
        AssessmentOperationMode operationMode,
        AssessmentQuestionKind questionKind,
        AssessmentCaseKind caseKind,
        EcgAnswer answer,
        IReadOnlyList<EcgAnswer> referenceAnswerSets)
    {
        if (operationMode == AssessmentOperationMode.FormalExamination &&
            questionKind != AssessmentQuestionKind.EcgInterpretationStatic)
        {
            throw new InvalidOperationException("assessment.question_kind.not_armed");
        }

        if (questionKind != AssessmentQuestionKind.EcgInterpretationStatic)
        {
            throw new InvalidOperationException("assessment.scoring.practice_only");
        }

        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(referenceAnswerSets);
        if (referenceAnswerSets.Count == 0)
        {
            throw new ArgumentException("At least one reference answer set is required.", nameof(referenceAnswerSets));
        }

        try
        {
            EcgAnswer normalizedAnswer = Normalize(answer);
            EcgAnswer[] normalizedReferences = referenceAnswerSets.Select(Normalize).ToArray();
            return caseKind == AssessmentCaseKind.StandardBuiltIn
                ? ScoreStandard(normalizedAnswer, normalizedReferences)
                : ScoreCustom(normalizedAnswer, normalizedReferences);
        }
        catch (UnknownConceptException)
        {
            return new AssessmentResult("Rejected", null, "UnknownConcept", []);
        }
    }

    private static AssessmentResult ScoreStandard(EcgAnswer answer, IReadOnlyList<EcgAnswer> references)
    {
        List<FieldScore>? bestFields = null;
        int bestScore = -1;

        foreach (EcgAnswer reference in references)
        {
            IReadOnlyList<IReadOnlyCollection<string>> actual = Fields(answer);
            IReadOnlyList<IReadOnlyCollection<string>> expected = Fields(reference);
            List<FieldScore> fields = new(Weights.Length);
            int total = 0;
            for (int index = 0; index < Weights.Length; index++)
            {
                bool exact = actual[index].SequenceEqual(expected[index]);
                int awarded = exact ? Weights[index].Weight : 0;
                total += awarded;
                fields.Add(new FieldScore(Weights[index].Name, Weights[index].Weight, awarded, exact));
            }

            if (total > bestScore)
            {
                bestScore = total;
                bestFields = fields;
            }
        }

        return new AssessmentResult("AutoScored", bestScore, "BuiltInRubric", bestFields!);
    }

    private static AssessmentResult ScoreCustom(EcgAnswer answer, IReadOnlyList<EcgAnswer> references)
    {
        bool exact = references.Any(reference => Fields(answer)
            .Zip(Fields(reference), static (actual, expected) => actual.SequenceEqual(expected))
            .All(static value => value));

        return exact
            ? new AssessmentResult("AutoScored", 100, "CustomExactMatch", [])
            : new AssessmentResult("PendingManualReview", null, "CustomNotExact", []);
    }

    private EcgAnswer Normalize(EcgAnswer answer) => new(
        _normalizer.NormalizeOne(answer.RateClass),
        _normalizer.Normalize(answer.Rhythm),
        _normalizer.Normalize(answer.Axis),
        _normalizer.Normalize(answer.Conduction),
        _normalizer.Normalize(answer.Chamber),
        _normalizer.Normalize(answer.StT),
        _normalizer.Normalize(answer.Other));

    private static IReadOnlyList<IReadOnlyCollection<string>> Fields(EcgAnswer answer) =>
    [
        answer.RateClass is null ? [] : [answer.RateClass],
        answer.Rhythm,
        answer.Axis,
        answer.Conduction,
        answer.Chamber,
        answer.StT,
        answer.Other,
    ];
}
