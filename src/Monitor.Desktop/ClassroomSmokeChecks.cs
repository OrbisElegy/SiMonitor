// SPDX-License-Identifier: AGPL-3.0-or-later
using Avalonia.Threading;
using Monitor.Application.Classroom;
using Monitor.Application.Measurements;
using Monitor.Application.Scenarios;
using Monitor.Domain.Presentation;

namespace Monitor.Desktop;

internal static class ClassroomSmokeChecks
{
    private const long Step = 50_000_000;

    internal static void Verify()
    {
        var teacher = new DesignPreviewWindow();
        teacher.Show();
        var student = new DesignPreviewWindow();
        student.Show();
        DesignPreviewWindow? late = null;
        try
        {
            Require(teacher.StartClassroom(0) && teacher.ClassroomHost is { } host && host.JoinCode.Length == 6, "a teacher hosts with a six-digit join code");
            int port = teacher.ClassroomHost!.Port;
            string code = teacher.ClassroomHost.JoinCode;
            Teach(teacher, [], 3);
            Require(Join(student, port, "000000") is "Classroom.WrongJoinCode" || code == "000000", "a wrong join code is refused");
            Require(Join(student, port, code) is null && student.IsClassroomStudent && !student.Settings.Apply.IsEnabled,
                "a student joins and its own simulation settings are locked");
            Follow(teacher, [student], 3);
            RequireSameSamples(teacher, student, "a joining student replays the teacher's generation exactly");

            var settings = teacher.Settings;
            settings.CardiacRateEnabled.IsChecked = true;
            settings.HeartRate.Value = 100;
            settings.ApplyDelaySeconds.Value = 0;
            teacher.ApplySettings();
            Follow(teacher, [student], 10);
            RequireSameSamples(teacher, student, "an applied change continues identically on the student");
            var rate = student.Session.Measurements!.HeartRate;
            Require(rate.Status == WaveformMeasurementStatus.Valid && Math.Abs(rate.MilliBeatsPerMinute!.Value - 100_000) <= 2000,
                "the student's measurements follow the teacher's change");

            int change = teacher.VitalChanges!.Add(new(new Dictionary<VitalSign, int> { [VitalSign.HeartRateBpm] = 70 }, 4_000_000_000, VitalChangeTrigger.Manual));
            teacher.VitalChanges.Trigger(change);
            Follow(teacher, [student], 6);
            RequireSameSamples(teacher, student, "vital change steps are replayed at the same times");

            late = new DesignPreviewWindow();
            late.Show();
            Require(Join(late, port, code) is null, "a second student joins later");
            Follow(teacher, [student, late], 5);
            RequireSameSamples(teacher, late, "a late joiner catches up to the same waveforms");

            var exam = new ExamDefinition(Guid.NewGuid(), "Rate quiz", false, 0, true,
                [new("Heart rate?", ExamQuestionKind.Numeric, [], null, 70, 3, "bpm", 2)]);
            teacher.OpenExam(exam);
            Follow(teacher, [student, late], 1);
            Require(student.MeasurementPolicy == SystemViewCommandAssessmentPolicy.CourseLocked && student.Classroom.Submit.IsEnabled,
                "an exam with locked measurement reaches the student");
            Wait(student.SubmitAnswersAsync(exam.Id, [new ExamAnswer(0, null, 71)]));
            Follow(teacher, [student, late], 1);
            Require(teacher.ExamRun!.Submissions.Single().Score.Awarded == 2, "the teacher scores the submitted answer");
            teacher.CloseExam();
            Follow(teacher, [student, late], 1);
            Require(student.MeasurementPolicy == SystemViewCommandAssessmentPolicy.Enabled && !student.Classroom.Submit.IsEnabled &&
                teacher.ExamRun.ToCsv().Contains(",2,2,1", StringComparison.Ordinal), "closing restores measurement and keeps the results");

            teacher.RestartSettings();
            Follow(teacher, [student, late], 5);
            Require(student.StudentGeneration == late.StudentGeneration && student.Session.SimulationTimeNs <= teacher.Session.SimulationTimeNs &&
                student.Session.SimulationTimeNs > 0, "a teacher restart starts a new generation for every student");
            RequireSameSamples(teacher, student, "the restarted generation matches");

            student.LeaveClassroom();
            Require(!student.IsClassroomStudent && student.Settings.Apply.IsEnabled, "leaving restores the student's own settings");
            teacher.StopClassroom();
            Pump(1);
        }
        finally
        {
            late?.Close();
            student.Close();
            teacher.Close();
        }
    }

    // Authoring through the page, the saved library and validation messages.
    internal static void VerifyEditor()
    {
        string directory = Path.Combine(Path.GetTempPath(), "monitor-exams-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string preferences = Path.Combine(directory, "display.json");
        try
        {
            var window = new DesignPreviewWindow(preferences);
            window.Show();
            try
            {
                var page = window.Classroom;
                void Click(Avalonia.Controls.Button button) => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                page.Prompt.Text = "Which rhythm?";
                page.Options.Text = "Sinus\nAF\n\nFlutter";
                page.CorrectOption.Value = 2;
                Click(page.AddQuestion);
                page.QuestionKind.SelectedIndex = (int)ExamQuestionKind.Numeric;
                page.Prompt.Text = "PR interval?";
                page.CorrectValue.Value = 160;
                page.Tolerance.Value = 20;
                page.Unit.Text = "ms";
                Click(page.AddQuestion);
                Require(page.DraftQuestions.Count == 2 && page.DraftQuestions[0] is { CorrectOption: 1, Options.Count: 3 } &&
                    page.DraftQuestions[1] is { Kind: ExamQuestionKind.Numeric, CorrectValue: 160 }, "questions are added from the editor, skipping blank options");
                page.QuestionKind.SelectedIndex = (int)ExamQuestionKind.SingleChoice;
                page.Prompt.Text = "Only one option";
                page.Options.Text = "Sinus";
                Click(page.AddQuestion);
                Require(page.DraftQuestions.Count == 2 && page.ExamStatus.Text == window.Localization.Get("classroom.questionInvalid"),
                    "an incomplete question is explained and not added");
                Require(!page.OpenExam.IsEnabled, "an exam can only be published while hosting");
                Click(page.SaveExam);
                Require(page.ExamStatus.Text == window.Localization.Get("classroom.examInvalid"), "an exam needs a title");
                page.ExamTitle.Text = "Rhythm basics";
                Click(page.SaveExam);
                Require(page.ExamStatus.Text == window.Localization.Get("classroom.examSaved") && File.Exists(Path.Combine(directory, "exams.json")),
                    "the exam is saved to the library next to the preferences");
            }
            finally { window.Close(); }
            var reopened = new DesignPreviewWindow(preferences);
            reopened.Show();
            try
            {
                var page = reopened.Classroom;
                page.Library.SelectedIndex = 0;
                page.LoadExam.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Require(page.ExamTitle.Text == "Rhythm basics" && page.DraftQuestions.Count == 2, "a saved exam loads into the editor");
            }
            finally { reopened.Close(); }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static string? Join(DesignPreviewWindow student, int port, string code)
    {
        var joining = student.JoinClassroomAsync("127.0.0.1", port, code, "Student");
        Wait(joining);
        return joining.Result;
    }

    // Advances the teacher in real time and lets students follow its clock.
    private static void Follow(DesignPreviewWindow teacher, DesignPreviewWindow[] students, int seconds)
    {
        Teach(teacher, students, seconds);
        // Let students reach the last clock once the teacher stops advancing.
        teacher.Pause();
        for (int i = 0; i < 200 && students.Any(student => student.Session.SimulationTimeNs < teacher.Session.SimulationTimeNs); i++)
        {
            Pump(1);
            foreach (var student in students) { student.StudentTick(); }
        }
        teacher.Start();
    }

    private static void Teach(DesignPreviewWindow teacher, DesignPreviewWindow[] students, int seconds)
    {
        for (int i = 0; i < seconds * 20; i++)
        {
            teacher.Pulse(teacher.ActiveTimer, Step);
            Pump(0);
            foreach (var student in students) { student.StudentTick(); }
        }
    }

    private static void Pump(int sleepMilliseconds)
    {
        if (sleepMilliseconds > 0) { Thread.Sleep(sleepMilliseconds); }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Wait(Task task)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!task.IsCompleted)
        {
            if (DateTime.UtcNow > deadline) { throw new TimeoutException("Classroom operation did not finish."); }
            Pump(5);
        }
        task.GetAwaiter().GetResult();
    }

    // The last four seconds both have generated; every channel must match sample for sample.
    private static void RequireSameSamples(DesignPreviewWindow teacher, DesignPreviewWindow student, string message)
    {
        long end = Math.Min(teacher.Session.FrontierNs, student.Session.FrontierNs);
        long start = Math.Max(0, end - 4_000_000_000);
        for (int channel = 0; channel < 7; channel++)
        {
            var expected = teacher.Session.Samples(channel, start, end).ToArray();
            var actual = student.Session.Samples(channel, start, end).ToArray();
            Require(expected.Length > 0 && expected.SequenceEqual(actual), $"{message} (channel {channel}, {expected.Length}/{actual.Length} samples)");
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) { throw new InvalidOperationException("Classroom: " + message); } }
}
