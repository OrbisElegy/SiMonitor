// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Monitor.Application.Classroom;
using Monitor.Infrastructure.Classroom;

namespace Monitor.Desktop;

// Teacher and student classroom controls: hosting with a join code, exam
// authoring and results, joining a classroom and answering exams.
internal sealed class ClassroomPage : ContentControl
{
    private readonly DesignPreviewWindow _window;
    private readonly DesktopLocalization _localization;
    private readonly List<ExamQuestion> _questions = [];
    private List<ExamDefinition> _library = [];
    private ExamDefinition? _studentExam;
    private readonly List<Func<ExamAnswer?>> _answerReaders = [];

    // Teacher.
    internal NumericUpDown Port { get; } = Number(1024, 65535, DesignPreviewWindow.DefaultClassroomPort, 1);
    internal Button Host { get; } = new() { MinHeight = 44, Classes = { "accent" } };
    internal TextBlock HostStatus { get; } = Wrapped();
    internal ListBox Students { get; } = new() { MinHeight = 80, MaxHeight = 220 };
    internal TextBox ExamTitle { get; } = new() { MaxLength = ExamDefinition.MaximumTitleLength, MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
    internal ComboBox ExamMode { get; } = new() { MinWidth = 220 };
    internal NumericUpDown DurationMinutes { get; } = Number(0, 240, 0, 1);
    internal CheckBox LockMeasurement { get; } = new() { IsChecked = true };
    internal TextBox Prompt { get; } = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = ExamQuestion.MaximumPromptLength, MinHeight = 60, MaxWidth = 640 };
    internal ComboBox QuestionKind { get; } = new() { MinWidth = 220 };
    internal TextBox Options { get; } = new() { AcceptsReturn = true, MinHeight = 80, MaxWidth = 640 };
    internal NumericUpDown CorrectOption { get; } = Number(1, ExamQuestion.MaximumOptionCount, 1, 1);
    internal NumericUpDown CorrectValue { get; } = Number(-100000, 100000, 0, 1);
    internal NumericUpDown Tolerance { get; } = Number(0, 100000, 0, 1);
    internal TextBox Unit { get; } = new() { MaxLength = 20, Width = 120, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown Points { get; } = Number(1, ExamQuestion.MaximumPoints, 1, 1);
    internal Button AddQuestion { get; } = new() { MinHeight = 44 };
    internal ListBox Questions { get; } = new() { MinHeight = 80, MaxHeight = 260 };
    internal Button RemoveQuestion { get; } = new() { MinHeight = 44 };
    internal ComboBox Library { get; } = new() { MinWidth = 320 };
    internal Button SaveExam { get; } = new() { MinHeight = 44 };
    internal Button LoadExam { get; } = new() { MinHeight = 44 };
    internal Button OpenExam { get; } = new() { MinHeight = 44, Classes = { "accent" } };
    internal Button CloseExam { get; } = new() { MinHeight = 44 };
    internal ListBox Results { get; } = new() { MinHeight = 80, MaxHeight = 260 };
    internal TextBox ExportPath { get; } = new() { MinWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
    internal Button Export { get; } = new() { MinHeight = 44 };
    internal TextBlock ExamStatus { get; } = Wrapped();

    // Student.
    internal TextBox Address { get; } = new() { MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
    internal NumericUpDown JoinPort { get; } = Number(1, 65535, DesignPreviewWindow.DefaultClassroomPort, 1);
    internal TextBox JoinCode { get; } = new() { MaxLength = 12, Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
    internal TextBox StudentName { get; } = new() { MaxLength = ClassroomHost.MaximumNameLength, MinWidth = 260, HorizontalAlignment = HorizontalAlignment.Left };
    internal Button Join { get; } = new() { MinHeight = 44, Classes = { "accent" } };
    internal TextBlock JoinStatus { get; } = Wrapped();
    internal StackPanel Answers { get; } = new() { Spacing = 12 };
    internal Button Submit { get; } = new() { MinHeight = 44, Classes = { "accent" } };
    internal TextBlock AnswerStatus { get; } = Wrapped();

    internal ClassroomPage(DesignPreviewWindow window, DesktopLocalization localization)
    {
        _window = window;
        _localization = localization;
        localization.SetChoices(ExamMode, "classroom.modePractice", "classroom.modeFormal");
        ExamMode.SelectedIndex = 0;
        localization.SetChoices(QuestionKind, "classroom.kindChoice", "classroom.kindNumeric");
        QuestionKind.SelectedIndex = 0;
        localization.Bind(LockMeasurement, ContentControl.ContentProperty, "classroom.lockMeasurement");
        ExportPath.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SiMonitor-results.csv");

        var host = Section();
        Field(host, "classroom.port", Port);
        host.Children.Add(Host);
        host.Children.Add(HostStatus);
        Field(host, "classroom.students", Students);
        host.Children.Add(DesktopInformationPages.Help("classroom"));

        var editor = Section();
        Field(editor, "classroom.examTitle", ExamTitle);
        Field(editor, "classroom.examMode", ExamMode);
        Field(editor, "classroom.duration", DurationMinutes);
        editor.Children.Add(LockMeasurement);
        Field(editor, "classroom.prompt", Prompt);
        Field(editor, "classroom.kind", QuestionKind);
        Field(editor, "classroom.options", Options);
        Field(editor, "classroom.correctOption", CorrectOption);
        Field(editor, "classroom.correctValue", CorrectValue);
        Field(editor, "classroom.tolerance", Tolerance);
        Field(editor, "classroom.unit", Unit);
        Field(editor, "classroom.points", Points);
        editor.Children.Add(Row(AddQuestion, RemoveQuestion));
        Field(editor, "classroom.questions", Questions);
        Field(editor, "classroom.library", Library);
        editor.Children.Add(Row(SaveExam, LoadExam));
        editor.Children.Add(DesktopInformationPages.Help("exams"));

        var run = Section();
        run.Children.Add(Row(OpenExam, CloseExam));
        run.Children.Add(ExamStatus);
        Field(run, "classroom.results", Results);
        Field(run, "classroom.exportPath", ExportPath);
        run.Children.Add(Export);

        var join = Section();
        Field(join, "classroom.address", Address);
        Field(join, "classroom.port", JoinPort);
        Field(join, "classroom.joinCode", JoinCode);
        Field(join, "classroom.name", StudentName);
        join.Children.Add(Join);
        join.Children.Add(JoinStatus);

        var answer = Section();
        answer.Children.Add(Answers);
        answer.Children.Add(Submit);
        answer.Children.Add(AnswerStatus);

        foreach (var (button, key) in new[]
        {
            (AddQuestion, "classroom.addQuestion"), (RemoveQuestion, "classroom.removeQuestion"), (SaveExam, "classroom.saveExam"),
            (LoadExam, "classroom.loadExam"), (OpenExam, "classroom.openExam"), (CloseExam, "classroom.closeExam"),
            (Export, "classroom.export"), (Submit, "classroom.submit"),
        })
        { localization.Bind(button, ContentControl.ContentProperty, key); }

        Content = new SettingsSections(localization, "navigation.classroom", new Dictionary<int, string> { [0] = "classroom.headerTeacher", [3] = "classroom.headerStudent" },
        [
            ("classroom.sectionHost", host), ("classroom.sectionEditor", editor), ("classroom.sectionRun", run),
            ("classroom.sectionJoin", join), ("classroom.sectionAnswer", answer),
        ]);

        Host.Click += (_, _) => ToggleHost();
        QuestionKind.SelectionChanged += (_, _) => RefreshEditor();
        Questions.SelectionChanged += (_, _) => RefreshEditor();
        AddQuestion.Click += (_, _) => AddCurrentQuestion();
        RemoveQuestion.Click += (_, _) =>
        {
            if (Questions.SelectedIndex >= 0) { _questions.RemoveAt(Questions.SelectedIndex); }
            RefreshQuestions();
        };
        SaveExam.Click += (_, _) => SaveToLibrary();
        LoadExam.Click += (_, _) => LoadFromLibrary();
        OpenExam.Click += (_, _) => PublishExam();
        CloseExam.Click += (_, _) => _window.CloseExam();
        Export.Click += (_, _) => ExportResults();
        Join.Click += async (_, _) => await ToggleJoin();
        Submit.Click += async (_, _) => await SubmitAnswers();
        AttachedToVisualTree += (_, _) => localization.LocaleChanged += RefreshAll;
        DetachedFromVisualTree += (_, _) => localization.LocaleChanged -= RefreshAll;
        _library = [.. _window.ExamLibrary?.Load(out _) ?? []];
        RefreshAll();
    }

    internal IReadOnlyList<ExamQuestion> DraftQuestions => _questions;

    private void RefreshAll()
    {
        RefreshEditor();
        RefreshQuestions();
        RefreshTeacher();
        RefreshStudent(null);
        ShowExam(_studentExam);
    }

    internal void RefreshTeacher()
    {
        var host = _window.ClassroomHost;
        bool student = _window.IsClassroomStudent;
        _localization.Bind(Host, ContentControl.ContentProperty, host is null ? "classroom.startHost" : "classroom.stopHost");
        Host.IsEnabled = !student;
        Port.IsEnabled = host is null && !student;
        if (host is null) { _localization.Bind(HostStatus, TextBlock.TextProperty, "classroom.hostIdle"); }
        else
        {
            Port.Value = host.Port;
            string addresses = string.Join(_localization.Get("alarm.listSeparator"), LocalAddresses().Select(address => $"{address}:{host.Port}"));
            _localization.Bind(HostStatus, TextBlock.TextProperty, "classroom.hosting", host.JoinCode, addresses);
        }
        Students.ItemsSource = host?.Students.Select(item => $"{item.Name} · {item.Endpoint}").ToArray() ?? [];
        var run = _window.ExamRun;
        OpenExam.IsEnabled = host is not null && _questions.Count > 0;
        CloseExam.IsEnabled = run is { IsOpen: true };
        Results.ItemsSource = run?.Submissions.OrderBy(item => item.StudentName, StringComparer.Ordinal)
            .Select(item => _localization.Format("classroom.result", item.StudentName, item.Score.Awarded, item.Score.Possible)).ToArray() ?? [];
        Export.IsEnabled = run is not null;
        if (run is null) { _localization.Bind(ExamStatus, TextBlock.TextProperty, "classroom.noExam"); }
        else
        {
            _localization.Bind(ExamStatus, TextBlock.TextProperty, run.IsOpen ? "classroom.examOpen" : "classroom.examClosed",
                run.Exam.Title, run.Submissions.Count);
        }
    }

    internal void RefreshStudent(string? statusKey)
    {
        bool student = _window.IsClassroomStudent;
        _localization.Bind(Join, ContentControl.ContentProperty, student ? "classroom.leave" : "classroom.join");
        Join.IsEnabled = _window.ClassroomHost is null;
        foreach (Control control in new Control[] { Address, JoinPort, JoinCode, StudentName }) { control.IsEnabled = !student; }
        if (statusKey is not null) { _localization.Bind(JoinStatus, TextBlock.TextProperty, statusKey); }
    }

    // Builds answer controls for an open exam; null clears them.
    internal void ShowExam(ExamDefinition? exam)
    {
        _studentExam = exam;
        Answers.Children.Clear();
        _answerReaders.Clear();
        Submit.IsEnabled = exam is not null && _window.IsClassroomStudent;
        if (exam is null)
        {
            Answers.Children.Add(Label("classroom.noOpenExam"));
            return;
        }
        string limit = exam.DurationNs == 0 ? _localization.Get("classroom.noLimit")
            : _localization.Format("classroom.limitMinutes", exam.DurationNs / 60_000_000_000);
        Answers.Children.Add(new TextBlock
        {
            Text = _localization.Format("classroom.examHeading", exam.Title, limit),
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        for (int index = 0; index < exam.Questions.Count; index++)
        {
            var question = exam.Questions[index];
            int questionIndex = index;
            Answers.Children.Add(new SelectableTextBlock
            {
                Text = _localization.Format("classroom.questionHeading", index + 1, question.Points, question.Prompt),
                TextWrapping = TextWrapping.Wrap
            });
            if (question.Kind == ExamQuestionKind.SingleChoice)
            {
                var choice = new ComboBox { MinWidth = 320, ItemsSource = question.Options.ToArray() };
                AutomationProperties.SetName(choice, _localization.Format("classroom.answerName", index + 1));
                Answers.Children.Add(choice);
                _answerReaders.Add(() => choice.SelectedIndex >= 0 ? new ExamAnswer(questionIndex, choice.SelectedIndex, null) : null);
            }
            else
            {
                var value = new NumericUpDown { Width = 200, HorizontalAlignment = HorizontalAlignment.Left, Value = null, FormatString = "0.###" };
                AutomationProperties.SetName(value, _localization.Format("classroom.answerName", index + 1));
                var line = Row(value, new TextBlock { Text = question.Unit, VerticalAlignment = VerticalAlignment.Center });
                Answers.Children.Add(line);
                _answerReaders.Add(() => value.Value is { } number ? new ExamAnswer(questionIndex, null, number) : null);
            }
        }
        _localization.Bind(AnswerStatus, TextBlock.TextProperty, "classroom.answerHint");
    }

    internal void ShowReceipt(SubmissionReceiptMessage receipt) =>
        _localization.Bind(AnswerStatus, TextBlock.TextProperty, receipt.Accepted ? "classroom.submitted" : "classroom.submitRejected");

    internal void ShowExamClosed(ExamClosedMessage closed)
    {
        Submit.IsEnabled = false;
        _studentExam = null;
        if (closed is { Score: { } score, Revealed: { } revealed })
        {
            Answers.Children.Clear();
            Answers.Children.Add(new TextBlock
            {
                Text = _localization.Format("classroom.practiceScore", revealed.Title, score.Awarded, score.Possible),
                FontSize = 16,
                FontWeight = FontWeight.SemiBold
            });
            for (int index = 0; index < revealed.Questions.Count; index++)
            {
                var question = revealed.Questions[index];
                string correct = question.Kind == ExamQuestionKind.SingleChoice
                    ? question.Options[question.CorrectOption!.Value]
                    : question.CorrectValue!.Value.ToString("0.###", CultureInfo.InvariantCulture) + " ± " +
                        question.Tolerance.ToString("0.###", CultureInfo.InvariantCulture) + " " + question.Unit;
                Answers.Children.Add(new SelectableTextBlock
                {
                    Text = _localization.Format(score.Questions[index].Correct ? "classroom.reviewCorrect" : "classroom.reviewWrong", index + 1, correct),
                    TextWrapping = TextWrapping.Wrap
                });
            }
            _localization.Bind(AnswerStatus, TextBlock.TextProperty, "classroom.practiceClosed");
        }
        else { _localization.Bind(AnswerStatus, TextBlock.TextProperty, "classroom.formalClosed"); }
    }

    private void ToggleHost()
    {
        if (_window.ClassroomHost is not null)
        {
            _window.StopClassroom();
            return;
        }
        int port = (int)(Port.Value ?? DesignPreviewWindow.DefaultClassroomPort);
        if (!_window.StartClassroom(port)) { _localization.Bind(HostStatus, TextBlock.TextProperty, "classroom.hostFailed"); }
        RefreshStudent(null);
    }

    private void RefreshEditor()
    {
        bool choice = QuestionKind.SelectedIndex == (int)ExamQuestionKind.SingleChoice;
        Options.IsEnabled = CorrectOption.IsEnabled = choice;
        CorrectValue.IsEnabled = Tolerance.IsEnabled = Unit.IsEnabled = !choice;
        RemoveQuestion.IsEnabled = Questions.SelectedIndex >= 0;
    }

    private void RefreshQuestions()
    {
        int selected = Questions.SelectedIndex;
        Questions.ItemsSource = _questions.Select((question, index) =>
            _localization.Format("classroom.questionItem", index + 1, question.Points, question.Prompt)).ToArray();
        Questions.SelectedIndex = Math.Min(selected, _questions.Count - 1);
        Library.ItemsSource = _library.Select(exam => exam.Title).ToArray();
        LoadExam.IsEnabled = _library.Count > 0;
        SaveExam.IsEnabled = _questions.Count > 0 && _window.ExamLibrary is not null;
        OpenExam.IsEnabled = _window.ClassroomHost is not null && _questions.Count > 0;
    }

    private void AddCurrentQuestion()
    {
        try
        {
            var kind = (ExamQuestionKind)QuestionKind.SelectedIndex;
            string[] options = kind == ExamQuestionKind.SingleChoice
                ? (Options.Text ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) : [];
            var question = new ExamQuestion((Prompt.Text ?? "").Trim(), kind, options,
                kind == ExamQuestionKind.SingleChoice ? (int)(CorrectOption.Value ?? 1) - 1 : null,
                kind == ExamQuestionKind.Numeric ? CorrectValue.Value ?? throw new ArgumentException("Exam.InvalidNumericQuestion") : null,
                kind == ExamQuestionKind.Numeric ? Tolerance.Value ?? 0 : 0,
                kind == ExamQuestionKind.Numeric ? (Unit.Text ?? "").Trim() : "",
                (int)(Points.Value ?? 1));
            if (_questions.Count >= ExamDefinition.MaximumQuestionCount) { throw new ArgumentException("Exam.TooManyQuestions"); }
            new ExamDefinition(Guid.NewGuid(), "draft", false, 0, false, [question]).Validate();
            _questions.Add(question);
            Prompt.Text = "";
            Options.Text = "";
            RefreshQuestions();
            _localization.Bind(ExamStatus, TextBlock.TextProperty, "classroom.questionAdded", _questions.Count);
        }
        catch (ArgumentException) { _localization.Bind(ExamStatus, TextBlock.TextProperty, "classroom.questionInvalid"); }
    }

    private ExamDefinition? CurrentExam()
    {
        try
        {
            var exam = new ExamDefinition(Guid.NewGuid(), (ExamTitle.Text ?? "").Trim(), ExamMode.SelectedIndex == 1,
                (long)(DurationMinutes.Value ?? 0) * 60_000_000_000, LockMeasurement.IsChecked == true, _questions.ToArray());
            exam.Validate();
            return exam;
        }
        catch (ArgumentException)
        {
            _localization.Bind(ExamStatus, TextBlock.TextProperty, "classroom.examInvalid");
            return null;
        }
    }

    private void PublishExam()
    {
        if (CurrentExam() is not { } exam) { return; }
        _window.OpenExam(exam);
    }

    private void SaveToLibrary()
    {
        if (CurrentExam() is not { } exam || _window.ExamLibrary is not { } store) { return; }
        _library.RemoveAll(item => item.Title == exam.Title);
        _library.Add(exam);
        if (_library.Count > ExamLibraryStore.MaximumExams) { _library.RemoveAt(0); }
        bool saved = store.Save(_library);
        _localization.Bind(ExamStatus, TextBlock.TextProperty, saved ? "classroom.examSaved" : "classroom.examSaveFailed");
        RefreshQuestions();
    }

    private void LoadFromLibrary()
    {
        if (Library.SelectedIndex < 0 || Library.SelectedIndex >= _library.Count) { return; }
        var exam = _library[Library.SelectedIndex];
        ExamTitle.Text = exam.Title;
        ExamMode.SelectedIndex = exam.Formal ? 1 : 0;
        DurationMinutes.Value = exam.DurationNs / 60_000_000_000;
        LockMeasurement.IsChecked = exam.LockMeasurement;
        _questions.Clear();
        _questions.AddRange(exam.Questions);
        RefreshQuestions();
        _localization.Bind(ExamStatus, TextBlock.TextProperty, "classroom.examLoaded", exam.Title);
    }

    private void ExportResults()
    {
        if (_window.ExamRun is not { } run) { return; }
        try
        {
            string path = Path.GetFullPath((ExportPath.Text ?? "").Trim());
            File.WriteAllText(path, run.ToCsv(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            _localization.Bind(ExamStatus, TextBlock.TextProperty, "classroom.exported", path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { _localization.Bind(ExamStatus, TextBlock.TextProperty, "classroom.exportFailed"); }
    }

    private async Task ToggleJoin()
    {
        if (_window.IsClassroomStudent)
        {
            _window.LeaveClassroom();
            return;
        }
        Join.IsEnabled = false;
        _localization.Bind(JoinStatus, TextBlock.TextProperty, "classroom.joining");
        string? failure = await _window.JoinClassroomAsync(Address.Text ?? "", (int)(JoinPort.Value ?? DesignPreviewWindow.DefaultClassroomPort),
            JoinCode.Text ?? "", StudentName.Text ?? "");
        Dispatcher.UIThread.Post(() =>
        {
            RefreshStudent(failure is null ? null : FailureKey(failure));
            RefreshTeacher();
        });
    }

    private static string FailureKey(string reasonCode) => reasonCode switch
    {
        "Classroom.WrongJoinCode" => "classroom.wrongCode",
        "Classroom.InvalidName" => "classroom.invalidName",
        "Classroom.Full" => "classroom.full",
        "Classroom.ProtocolMismatch" => "classroom.protocolMismatch",
        _ => "classroom.connectFailed",
    };

    private async Task SubmitAnswers()
    {
        if (_studentExam is not { } exam) { return; }
        var answers = _answerReaders.Select(read => read()).OfType<ExamAnswer>().ToArray();
        try
        {
            await _window.SubmitAnswersAsync(exam.Id, answers);
            _localization.Bind(AnswerStatus, TextBlock.TextProperty, "classroom.submitting");
        }
        catch (Exception error) when (error is IOException or ObjectDisposedException or System.Net.Sockets.SocketException)
        { _localization.Bind(AnswerStatus, TextBlock.TextProperty, "classroom.submitFailed"); }
    }

    private static string[] LocalAddresses()
    {
        string[] addresses;
        try
        {
            addresses = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(item => item.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .SelectMany(item => item.GetIPProperties().UnicastAddresses)
                .Select(item => item.Address)
                .Where(address => address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(address))
                .Select(address => address.ToString()).Distinct().ToArray();
        }
        catch (System.Net.NetworkInformation.NetworkInformationException) { addresses = []; }
        return addresses.Length > 0 ? addresses : ["127.0.0.1"];
    }

    private static StackPanel Section() => new() { Spacing = 12 };

    private void Field(StackPanel panel, string key, Control control)
    {
        control.HorizontalAlignment = control is ListBox ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        if (control is TextBox { AcceptsReturn: true } multiline) { multiline.Width = 640; }
        panel.Children.Add(Label(key));
        panel.Children.Add(control);
        _localization.Bind(control, AutomationProperties.NameProperty, key);
    }

    private TextBlock Label(string key)
    {
        var label = Wrapped();
        _localization.Bind(label, TextBlock.TextProperty, key);
        return label;
    }

    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var control in controls) { row.Children.Add(control); }
        return row;
    }

    private static TextBlock Wrapped() => new() { TextWrapping = TextWrapping.Wrap };

    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, decimal increment) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Value = value,
        Increment = increment,
        FormatString = "0.###",
        Width = 180,
        HorizontalAlignment = HorizontalAlignment.Left
    };
}
