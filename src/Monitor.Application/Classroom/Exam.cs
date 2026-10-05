// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;

namespace Monitor.Application.Classroom;

public enum ExamQuestionKind { SingleChoice, Numeric }

// One question. Choice questions keep their correct option; numeric questions
// accept answers within an inclusive tolerance of the correct value.
public sealed record ExamQuestion(string Prompt, ExamQuestionKind Kind, IReadOnlyList<string> Options,
    int? CorrectOption, decimal? CorrectValue, decimal Tolerance, string Unit, int Points)
{
    public const int MaximumPromptLength = 500;
    public const int MaximumOptionCount = 8;
    public const int MaximumOptionLength = 200;
    public const int MaximumPoints = 100;

    internal void Validate(bool requireAnswer)
    {
        if (string.IsNullOrWhiteSpace(Prompt) || Prompt.Length > MaximumPromptLength || !Enum.IsDefined(Kind) ||
            Options is null || Unit is null || Unit.Length > 20 || Points is < 1 or > MaximumPoints || Tolerance < 0)
        { throw new ArgumentException("Exam.InvalidQuestion"); }
        if (Kind == ExamQuestionKind.SingleChoice)
        {
            if (Options.Count is < 2 or > MaximumOptionCount || Options.Any(option => string.IsNullOrWhiteSpace(option) || option.Length > MaximumOptionLength) ||
                CorrectValue is not null || requireAnswer && (CorrectOption is not { } correct || correct < 0 || correct >= Options.Count) ||
                !requireAnswer && CorrectOption is not null)
            { throw new ArgumentException("Exam.InvalidChoiceQuestion"); }
        }
        else if (Options.Count != 0 || CorrectOption is not null || requireAnswer == CorrectValue is null)
        { throw new ArgumentException("Exam.InvalidNumericQuestion"); }
    }
}

// Practice questions show the correct answers when closed; formal exams keep
// them for the teacher. A zero duration has no time limit.
public sealed record ExamDefinition(Guid Id, string Title, bool Formal, long DurationNs, bool LockMeasurement,
    IReadOnlyList<ExamQuestion> Questions)
{
    public const int MaximumQuestionCount = 50;
    public const int MaximumTitleLength = 100;
    public const long MaximumDurationNs = 4L * 3_600_000_000_000;

    public void Validate(bool requireAnswers = true)
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Title) || Title.Length > MaximumTitleLength ||
            DurationNs is < 0 or > MaximumDurationNs || Questions is null || Questions.Count is < 1 or > MaximumQuestionCount)
        { throw new ArgumentException("Exam.InvalidDefinition"); }
        foreach (var question in Questions) { question.Validate(requireAnswers); }
    }

    // The copy sent to students, without correct answers.
    public ExamDefinition WithoutAnswers() => this with
    {
        Questions = Questions.Select(question => question with { CorrectOption = null, CorrectValue = null, Tolerance = 0 }).ToArray()
    };
}

// Answer to one question by index; unanswered questions are omitted.
public sealed record ExamAnswer(int QuestionIndex, int? Option, decimal? Value);

public sealed record ExamQuestionScore(int QuestionIndex, bool Answered, bool Correct, int Awarded, int Possible);

public sealed record ExamScore(int Awarded, int Possible, IReadOnlyList<ExamQuestionScore> Questions);

public static class ExamScorer
{
    public static ExamScore Score(ExamDefinition exam, IReadOnlyList<ExamAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(exam);
        ArgumentNullException.ThrowIfNull(answers);
        exam.Validate();
        ValidateAnswers(exam, answers);
        var scores = exam.Questions.Select((question, index) =>
        {
            var answer = answers.FirstOrDefault(candidate => candidate.QuestionIndex == index);
            bool correct = answer is not null && (question.Kind == ExamQuestionKind.SingleChoice
                ? answer.Option == question.CorrectOption
                : answer.Value is { } value && Math.Abs(value - question.CorrectValue!.Value) <= question.Tolerance);
            return new ExamQuestionScore(index, answer is not null, correct, correct ? question.Points : 0, question.Points);
        }).ToArray();
        return new(scores.Sum(score => score.Awarded), scores.Sum(score => score.Possible), scores);
    }

    // At most one answer per question, of the question's kind and in range.
    public static void ValidateAnswers(ExamDefinition exam, IReadOnlyList<ExamAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(exam);
        ArgumentNullException.ThrowIfNull(answers);
        if (answers.Count > exam.Questions.Count || answers.Select(answer => answer?.QuestionIndex).Distinct().Count() != answers.Count)
        { throw new ArgumentException("Exam.InvalidAnswers"); }
        foreach (var answer in answers)
        {
            if (answer is null || answer.QuestionIndex < 0 || answer.QuestionIndex >= exam.Questions.Count)
            { throw new ArgumentException("Exam.InvalidAnswers"); }
            var question = exam.Questions[answer.QuestionIndex];
            bool valid = question.Kind == ExamQuestionKind.SingleChoice
                ? answer.Value is null && answer.Option is { } option && option >= 0 && option < question.Options.Count
                : answer.Option is null && answer.Value is not null;
            if (!valid) { throw new ArgumentException("Exam.InvalidAnswers"); }
        }
    }
}

public sealed record ExamSubmission(string StudentId, string StudentName, long ReceivedSimTimeNs, IReadOnlyList<ExamAnswer> Answers, ExamScore Score);

// Teacher-side run of one exam: collects the latest valid submission per student
// while open and within the time limit. Time is the simulation clock of the session.
public sealed class ExamRun
{
    private readonly Dictionary<string, ExamSubmission> _submissions = new(StringComparer.Ordinal);

    public ExamRun(ExamDefinition exam, long openedSimTimeNs)
    {
        ArgumentNullException.ThrowIfNull(exam);
        exam.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(openedSimTimeNs);
        Exam = exam;
        OpenedSimTimeNs = openedSimTimeNs;
    }

    public ExamDefinition Exam { get; }
    public long OpenedSimTimeNs { get; }
    public bool IsOpen { get; private set; } = true;
    public IReadOnlyCollection<ExamSubmission> Submissions => _submissions.Values;

    public bool AcceptsAt(long simTimeNs) => IsOpen && (Exam.DurationNs == 0 || simTimeNs - OpenedSimTimeNs <= Exam.DurationNs);

    public ExamSubmission Submit(string studentId, string studentName, IReadOnlyList<ExamAnswer> answers, long simTimeNs)
    {
        if (string.IsNullOrWhiteSpace(studentId) || studentName is null) { throw new ArgumentException("Exam.InvalidStudent"); }
        if (!AcceptsAt(simTimeNs)) { throw new InvalidOperationException("Exam.Closed"); }
        var submission = new ExamSubmission(studentId, studentName, simTimeNs, answers.ToArray(), ExamScorer.Score(Exam, answers));
        _submissions[studentId] = submission;
        return submission;
    }

    public void Close() => IsOpen = false;

    // One row per submission: name, total, possible, then 1/0 per question.
    public string ToCsv()
    {
        var builder = new StringBuilder();
        builder.Append("student,score,possible");
        for (int index = 0; index < Exam.Questions.Count; index++) { builder.Append(CultureInfo.InvariantCulture, $",q{index + 1}"); }
        builder.Append('\n');
        foreach (var submission in _submissions.Values.OrderBy(item => item.StudentName, StringComparer.Ordinal))
        {
            builder.Append(Csv(submission.StudentName)).Append(',')
                .Append(submission.Score.Awarded.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(submission.Score.Possible.ToString(CultureInfo.InvariantCulture));
            foreach (var question in submission.Score.Questions) { builder.Append(question.Correct ? ",1" : ",0"); }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    // Quotes fields and neutralizes leading formula characters for spreadsheet safety.
    private static string Csv(string value)
    {
        string safe = value.Length > 0 && "=+-@".Contains(value[0], StringComparison.Ordinal) ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
