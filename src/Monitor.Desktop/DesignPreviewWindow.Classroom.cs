// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using Avalonia.Threading;
using Monitor.Application.Classroom;
using Monitor.Domain.Presentation;
using Monitor.Infrastructure.Classroom;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Desktop;

// Classroom roles. A teacher hosts its running simulation: every restart, apply,
// vital change step and ventilation update is published with its simulation time,
// and the clock follows. A student turns off its own timer and replays those
// events at the same simulation times, so the deterministic generator reproduces
// the teacher's waveforms; it stays a little behind the latest clock so that it
// never passes an event still on its way.
internal sealed partial class DesignPreviewWindow
{
    internal const int DefaultClassroomPort = 47630;
    private const long ClockIntervalNs = 50_000_000;
    private const long StudentLagNs = 200_000_000;
    private const long CatchUpPerTickNs = 20_000_000_000;

    private ClassroomPage? _classroomPage;
    private ClassroomHost? _classroomHost;
    private long _classroomGeneration;
    private long _lastClockTimestamp;
    private ExamRun? _examRun;

    private ClassroomClient? _classroomClient;
    private readonly ConcurrentQueue<ClassroomMessage> _incoming = new();
    private readonly List<ClassroomEvent> _pendingEvents = [];
    private ClockMessage? _teacherClock;
    private long _teacherClockTimestamp;
    private long _studentGeneration = -1;
    private DispatcherTimer? _studentTimer;

    internal ExamLibraryStore? ExamLibrary { get; }
    internal bool LastApplySucceeded { get; private set; }
    internal bool IsClassroomStudent => _classroomClient is not null;
    internal ClassroomHost? ClassroomHost => _classroomHost;
    internal ExamRun? ExamRun => _examRun;
    internal ClassroomPage Classroom => _classroomPage ??= new ClassroomPage(this, Localization);
    internal long StudentGeneration => _studentGeneration;

    // Opening a classroom restarts the teacher's simulation with the current
    // settings, so every student replays the same generation from its start.
    internal bool StartClassroom(int port)
    {
        if (_classroomHost is not null || IsClassroomStudent) { return false; }
        var host = new ClassroomHost(IPAddress.Any, port, ClassroomHost.CreateJoinCode());
        try { host.Start([], new ClockMessage(_classroomGeneration, 0, false)); }
        catch (System.Net.Sockets.SocketException)
        {
            _ = host.DisposeAsync().AsTask();
            return false;
        }
        host.StudentsChanged += () => Dispatcher.UIThread.Post(() => Classroom.RefreshTeacher());
        host.AnswersReceived += (student, answers) => Dispatcher.UIThread.Post(() => ReceiveAnswers(student, answers));
        _classroomHost = host;
        RestartSettings();
        if (!LastApplySucceeded)
        {
            StopClassroom();
            return false;
        }
        Classroom.RefreshTeacher();
        return true;
    }

    internal void StopClassroom()
    {
        var host = _classroomHost;
        _classroomHost = null;
        _examRun = null;
        if (host is not null) { _ = host.DisposeAsync().AsTask(); }
        Classroom.RefreshTeacher();
    }

    private void RecordClassroomSettings(bool restart, long appliedAtNs)
    {
        if (_classroomHost is null || IsClassroomStudent) { return; }
        string preferences = Encoding.UTF8.GetString(DisplayPreferenceStore.Serialize(new(Settings.ReadDisplay(),
            Settings.PaperLayout.SelectedIndex, Capture(Settings.Alerts.CapturePreferences), null, Settings.CaptureGenerator())));
        RecordClassroomEvent(restart
            ? new ClassroomEvent(ClassroomEventKind.Restart, ++_classroomGeneration, 0, preferences)
            : new ClassroomEvent(ClassroomEventKind.Apply, _classroomGeneration, appliedAtNs, preferences));
        PublishClassroomClock(force: true);
        static T? Capture<T>(Func<T> read) where T : class
        {
            try { return read(); }
            catch (ArgumentException) { return null; }
        }
    }

    private void RecordClassroomEvent(ClassroomEvent item)
    {
        if (_classroomHost is null || IsClassroomStudent) { return; }
        try { _classroomHost.Publish(item); }
        catch (InvalidOperationException) { StopClassroom(); }
    }

    private void PublishClassroomClock(bool force)
    {
        if (_classroomHost is null || IsClassroomStudent) { return; }
        long now = Stopwatch.GetTimestamp();
        if (!force && Stopwatch.GetElapsedTime(_lastClockTimestamp, now).Ticks * 100 < ClockIntervalNs) { return; }
        _lastClockTimestamp = now;
        _classroomHost.UpdateClock(new ClockMessage(_classroomGeneration, _session.SimulationTimeNs, _timer is not null));
    }

    internal void OpenExam(ExamDefinition exam)
    {
        if (_classroomHost is null) { throw new InvalidOperationException("Classroom.NotHosting"); }
        _examRun?.Close();
        _examRun = new ExamRun(exam, _session.SimulationTimeNs);
        _classroomHost.OpenExam(exam);
        Classroom.RefreshTeacher();
    }

    internal void CloseExam()
    {
        if (_examRun is not { } run || _classroomHost is null) { return; }
        run.Close();
        var exam = run.Exam;
        _classroomHost.CloseExam(exam.Id, student =>
        {
            if (exam.Formal) { return new ExamClosedMessage(exam.Id, null, null); }
            var submission = run.Submissions.FirstOrDefault(item => item.StudentId == student.StudentId);
            return new ExamClosedMessage(exam.Id, exam, submission?.Score ?? ExamScorer.Score(exam, []));
        });
        Classroom.RefreshTeacher();
    }

    // Timed exams close themselves once the limit has passed in simulation time.
    private void AdvanceClassroomExam()
    {
        if (_examRun is { IsOpen: true } run && !run.AcceptsAt(_session.SimulationTimeNs)) { CloseExam(); }
    }

    private void ReceiveAnswers(ClassroomStudent student, SubmitAnswersMessage answers)
    {
        if (_classroomHost is not { } host) { return; }
        string? reason = null;
        if (_examRun is not { } run || run.Exam.Id != answers.ExamId) { reason = "Exam.NotOpen"; }
        else
        {
            try { _ = run.Submit(student.StudentId, student.Name, answers.Answers ?? [], _session.SimulationTimeNs); }
            catch (ArgumentException exception) { reason = exception.Message; }
            catch (InvalidOperationException exception) { reason = exception.Message; }
        }
        host.Send(student.StudentId, new SubmissionReceiptMessage(answers.ExamId, reason is null, reason));
        Classroom.RefreshTeacher();
    }

    internal async Task<string?> JoinClassroomAsync(string address, int port, string joinCode, string name)
    {
        if (_classroomHost is not null || IsClassroomStudent) { return "Classroom.AlreadyConnected"; }
        ClassroomClient client;
        try { client = await ClassroomClient.ConnectAsync(address, port, joinCode, name, CancellationToken.None); }
        catch (ClassroomJoinException exception) { return exception.ReasonCode; }
        Pause();
        _classroomClient = client;
        _pendingEvents.Clear();
        _teacherClock = null;
        _studentGeneration = -1;
        client.MessageReceived += message =>
        {
            _incoming.Enqueue(message);
            Dispatcher.UIThread.Post(ProcessIncoming);
        };
        client.Disconnected += _ => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_classroomClient, client)) { LeaveClassroom("classroom.disconnected"); }
        });
        Settings.SetClassroomStudent(true);
        _studentTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _studentTimer.Tick += (_, _) => StudentTick();
        _studentTimer.Start();
        client.Start();
        Classroom.RefreshStudent("classroom.joined");
        return null;
    }

    internal void LeaveClassroom(string statusKey = "classroom.left")
    {
        var client = _classroomClient;
        _classroomClient = null;
        _studentTimer?.Stop();
        _studentTimer = null;
        _pendingEvents.Clear();
        _incoming.Clear();
        if (client is not null) { _ = client.DisposeAsync().AsTask(); }
        Settings.SetClassroomStudent(false);
        SetMeasurementPolicy(SystemViewCommandAssessmentPolicy.Enabled);
        Classroom.ShowExam(null);
        Classroom.RefreshStudent(statusKey);
    }

    internal Task SubmitAnswersAsync(Guid examId, IReadOnlyList<ExamAnswer> answers) =>
        _classroomClient?.SubmitAsync(new SubmitAnswersMessage(examId, answers), CancellationToken.None) ?? Task.CompletedTask;

    private void EndClassroom()
    {
        if (_classroomHost is not null) { StopClassroom(); }
        if (_classroomClient is not null) { LeaveClassroom(); }
    }

    private void ProcessIncoming()
    {
        while (_incoming.TryDequeue(out var message))
        {
            switch (message)
            {
                case EventMessage { Event: { } item }:
                    try
                    {
                        item.Validate();
                        _pendingEvents.Add(item);
                    }
                    catch (ArgumentException) { LeaveClassroom("classroom.replayFailed"); return; }
                    break;
                case ClockMessage clock:
                    _teacherClock = clock;
                    _teacherClockTimestamp = Stopwatch.GetTimestamp();
                    break;
                case ExamOpenedMessage opened:
                    try { opened.Exam.Validate(requireAnswers: false); }
                    catch (ArgumentException) { break; }
                    SetMeasurementPolicy(opened.Exam.LockMeasurement ? SystemViewCommandAssessmentPolicy.CourseLocked : SystemViewCommandAssessmentPolicy.Enabled);
                    Classroom.ShowExam(opened.Exam);
                    break;
                case ExamClosedMessage closed:
                    SetMeasurementPolicy(SystemViewCommandAssessmentPolicy.Enabled);
                    Classroom.ShowExamClosed(closed);
                    break;
                case SubmissionReceiptMessage receipt:
                    Classroom.ShowReceipt(receipt);
                    break;
            }
        }
    }

    // Replays due events and follows the teacher's clock; a late joiner catches up
    // in bounded slices so the interface stays responsive.
    internal void StudentTick()
    {
        if (!IsClassroomStudent) { return; }
        long budgetNs = CatchUpPerTickNs;
        try
        {
            while (_pendingEvents.Count > 0)
            {
                var next = _pendingEvents[0];
                if (next.Generation < _studentGeneration) { _pendingEvents.RemoveAt(0); continue; }
                if (next.Kind == ClassroomEventKind.Restart && next.Generation > _studentGeneration)
                {
                    _pendingEvents.RemoveAt(0);
                    ReplaySettings(next, restart: true);
                    _studentGeneration = next.Generation;
                    continue;
                }
                if (next.Generation != _studentGeneration || next.AtSimulationTimeNs > StudentTargetNs()) { break; }
                if (!AdvanceStudentTo(next.AtSimulationTimeNs, ref budgetNs)) { break; }
                _pendingEvents.RemoveAt(0);
                Replay(next);
            }
            _ = AdvanceStudentTo(StudentTargetNs(), ref budgetNs);
            RefreshAfterAdvance();
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or InvalidOperationException or System.Text.Json.JsonException)
        { LeaveClassroom("classroom.replayFailed"); }
    }

    // The teacher's time, held back by a fixed lag and never beyond the latest clock.
    private long StudentTargetNs()
    {
        if (_teacherClock is not { } clock || clock.Generation != _studentGeneration) { return _session.SimulationTimeNs; }
        long target = clock.SimulationTimeNs;
        if (clock.Running)
        {
            long elapsedNs = Stopwatch.GetElapsedTime(_teacherClockTimestamp).Ticks * 100;
            target = Math.Min(clock.SimulationTimeNs, clock.SimulationTimeNs - StudentLagNs + elapsedNs);
        }
        return Math.Max(target, _session.SimulationTimeNs);
    }

    private bool AdvanceStudentTo(long targetNs, ref long budgetNs)
    {
        while (_session.SimulationTimeNs < targetNs)
        {
            if (budgetNs <= 0) { return false; }
            long step = Math.Min(LocalMonitorStepNs, targetNs - _session.SimulationTimeNs);
            AdvanceCore(step);
            budgetNs -= step;
        }
        return true;
    }

    private const long LocalMonitorStepNs = 250_000_000;

    private void Replay(ClassroomEvent item)
    {
        switch (item.Kind)
        {
            case ClassroomEventKind.Apply:
                ReplaySettings(item, restart: false);
                break;
            case ClassroomEventKind.VitalStep:
                var inputs = VitalChangeSources.Apply(_appliedInputs ?? throw new InvalidOperationException("Classroom.NoAppliedInputs"), item.VitalValues!);
                _ = _session.ScheduleSource(inputs.CreateSession(), 0);
                break;
            case ClassroomEventKind.Ventilation:
                _ = _session.UpdateOxygenationVentilation(item.Ventilation!, item.OxygenDemandMultiplier);
                break;
        }
    }

    private void ReplaySettings(ClassroomEvent item, bool restart)
    {
        var preferences = DisplayPreferenceStore.Deserialize(Encoding.UTF8.GetBytes(item.PreferencesJson!));
        Settings.RestoreDisplay(preferences.Display, preferences.PaperLayout);
        if (preferences.Generator is { } generator) { Settings.RestoreGenerator(generator); }
        if (preferences.Alarms is { } alarms) { Settings.Alerts.RestorePreferences(alarms); }
        ApplySettings(restart);
        if (!LastApplySucceeded) { throw new InvalidOperationException("Classroom.ReplayRejected"); }
    }
}
