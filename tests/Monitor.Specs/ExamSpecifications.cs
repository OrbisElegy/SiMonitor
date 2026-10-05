// SPDX-License-Identifier: AGPL-3.0-or-later
using Monitor.Application.Classroom;

namespace Monitor.Specs;

internal static class ExamSpecifications
{
    private const long Second = 1_000_000_000;

    public static Specification[] All =>
    [
        new(nameof(ExamsScoreChoiceAndToleranceAnswers), ExamsScoreChoiceAndToleranceAnswers),
        new(nameof(StudentCopiesOmitAnswersAndStillValidate), StudentCopiesOmitAnswersAndStillValidate),
        new(nameof(InvalidExamsAndAnswersAreRejected), InvalidExamsAndAnswersAreRejected),
        new(nameof(ExamRunsKeepTheLatestSubmissionWithinTheTimeLimit), ExamRunsKeepTheLatestSubmissionWithinTheTimeLimit),
    ];

    private static ExamDefinition Exam(long durationNs = 0) => new(Guid.NewGuid(), "Rhythm quiz", true, durationNs, true,
    [
        new("Which rhythm is shown?", ExamQuestionKind.SingleChoice, ["Sinus", "AF", "Flutter"], 1, null, 0, "", 2),
        new("Measure the PR interval", ExamQuestionKind.Numeric, [], null, 160, 20, "ms", 3),
    ]);

    private static void ExamsScoreChoiceAndToleranceAnswers()
    {
        var exam = Exam();
        var full = ExamScorer.Score(exam, [new(0, 1, null), new(1, null, 180)]);
        Check.That(full is { Awarded: 5, Possible: 5 } && full.Questions.All(question => question.Correct), "correct choice and a value at the tolerance edge score fully");
        var partial = ExamScorer.Score(exam, [new(1, null, 181)]);
        Check.That(partial is { Awarded: 0, Possible: 5 } && !partial.Questions[0].Answered && !partial.Questions[1].Correct,
            "unanswered and out-of-tolerance answers score nothing");
    }

    private static void StudentCopiesOmitAnswersAndStillValidate()
    {
        var copy = Exam().WithoutAnswers();
        copy.Validate(requireAnswers: false);
        Check.That(copy.Questions.All(question => question.CorrectOption is null && question.CorrectValue is null && question.Tolerance == 0),
            "the student copy carries no correct answers or tolerances");
        bool rejected = false;
        try { copy.Validate(); }
        catch (ArgumentException) { rejected = true; }
        Check.That(rejected, "a copy without answers cannot be scored");
    }

    private static void InvalidExamsAndAnswersAreRejected()
    {
        var exam = Exam();
        foreach (var invalid in new[]
        {
            exam with { Title = " " },
            exam with { Questions = [] },
            exam with { Questions = [exam.Questions[0] with { CorrectOption = 3 }] },
            exam with { Questions = [exam.Questions[0] with { Options = ["only"] }] },
            exam with { Questions = [exam.Questions[1] with { CorrectValue = null }] },
            exam with { Questions = [exam.Questions[1] with { Tolerance = -1 }] },
            exam with { DurationNs = ExamDefinition.MaximumDurationNs + 1 },
        })
        {
            bool rejected = false;
            try { invalid.Validate(); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "an invalid exam definition is rejected");
        }
        foreach (ExamAnswer[] invalid in new[]
        {
            new ExamAnswer[] { new(0, 3, null) },
            [new(0, null, 1)],
            [new(1, 0, null)],
            [new(2, 0, null)],
            [new(0, 1, null), new(0, 2, null)],
        })
        {
            bool rejected = false;
            try { _ = ExamScorer.Score(exam, invalid); }
            catch (ArgumentException) { rejected = true; }
            Check.That(rejected, "answers of the wrong kind, out of range or repeated are rejected");
        }
    }

    private static void ExamRunsKeepTheLatestSubmissionWithinTheTimeLimit()
    {
        var run = new ExamRun(Exam(60 * Second), 10 * Second);
        _ = run.Submit("s1", "=Alice", [new(0, 0, null)], 20 * Second);
        _ = run.Submit("s1", "=Alice", [new(0, 1, null)], 30 * Second);
        _ = run.Submit("s2", "Bob \"B\"", [new(1, null, 150)], 70 * Second);
        Check.That(run.Submissions.Count == 2 && run.Submissions.Single(item => item.StudentId == "s1").Score.Awarded == 2,
            "a student's latest submission replaces the earlier one");
        bool late = false;
        try { _ = run.Submit("s3", "Carol", [], 70 * Second + 1); }
        catch (InvalidOperationException) { late = true; }
        Check.That(late && run.AcceptsAt(70 * Second) && !run.AcceptsAt(70 * Second + 1), "submissions after the time limit are refused");
        Check.That(run.ToCsv() == "student,score,possible,q1,q2\n\"'=Alice\",2,5,1,0\n\"Bob \"\"B\"\"\",3,5,0,1\n",
            "results export as CSV with quoted names and formula characters neutralized");
        run.Close();
        Check.That(!run.AcceptsAt(20 * Second), "a closed exam accepts nothing");
    }
}
