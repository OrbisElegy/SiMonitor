// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Monitor.Application.Classroom;
using Monitor.Application.Presentation;
using Monitor.Application.Scenarios;
using Monitor.Infrastructure.Classroom;
using Monitor.Infrastructure.Preferences;

namespace Monitor.Specs;

internal static class ClassroomSpecifications
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static Specification[] All =>
    [
        new(nameof(FramesRoundTripAndRejectMalformedInput), FramesRoundTripAndRejectMalformedInput),
        new(nameof(PreferencesSerializeToTheValidatedDocument), PreferencesSerializeToTheValidatedDocument),
        new(nameof(StudentsJoinWithTheCodeAndReceiveTheCurrentGeneration), StudentsJoinWithTheCodeAndReceiveTheCurrentGeneration),
        new(nameof(ExamsReachStudentsWithoutAnswersAndCollectSubmissions), ExamsReachStudentsWithoutAnswersAndCollectSubmissions),
    ];

    private static string Preferences => Encoding.UTF8.GetString(DisplayPreferenceStore.Serialize(new(MonitorDisplayConfiguration.Default(), 0)));

    private static ClassroomEvent Restart(long generation) => new(ClassroomEventKind.Restart, generation, 0, Preferences);

    private static void FramesRoundTripAndRejectMalformedInput()
    {
        ClassroomMessage[] messages =
        [
            new HelloMessage(1, "123456", "Alice"),
            new ClockMessage(2, 5_000_000_000, true),
            new EventMessage(new ClassroomEvent(ClassroomEventKind.VitalStep, 2, 7, VitalValues: new Dictionary<VitalSign, int> { [VitalSign.HeartRateBpm] = 90 })),
            new EventMessage(new ClassroomEvent(ClassroomEventKind.Ventilation, 2, 9, Ventilation: new(450000, 150000, 210000), OxygenDemandMultiplier: 1.5m)),
            new SubmitAnswersMessage(Guid.NewGuid(), [new ExamAnswer(0, 1, null)]),
        ];
        foreach (var message in messages)
        {
            byte[] frame = ClassroomFraming.Encode(message);
            var decoded = ClassroomFraming.Decode(frame.AsSpan(4));
            Check.That(ClassroomFraming.Encode(decoded).AsSpan().SequenceEqual(frame), $"{message.GetType().Name} survives a round trip");
        }
        foreach (string invalid in new[] { "{\"type\":\"unknown\"}", "{\"type\":\"clock\",\"Generation\":1,\"SimulationTimeNs\":2,\"Running\":true,\"Extra\":1}", "null", "[" })
        {
            bool rejected = false;
            try { _ = ClassroomFraming.Decode(Encoding.UTF8.GetBytes(invalid)); }
            catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException or NotSupportedException) { rejected = true; }
            Check.That(rejected, "unknown types, extra members and malformed JSON are rejected: " + invalid);
        }
        bool oversized = false;
        try { _ = ClassroomFraming.ReadAsync(new MemoryStream([0x7f, 0, 0, 0]), CancellationToken.None).GetAwaiter().GetResult(); }
        catch (ArgumentException) { oversized = true; }
        Check.That(oversized, "a frame length above the limit is refused before reading the body");
        bool invalidEvent = false;
        try { new ClassroomEvent(ClassroomEventKind.Apply, 0, 0).Validate(); }
        catch (ArgumentException) { invalidEvent = true; }
        Check.That(invalidEvent, "an apply event needs its settings");
    }

    private static void PreferencesSerializeToTheValidatedDocument()
    {
        var preferences = new DisplayPreferences(MonitorDisplayConfiguration.Default(), 1, MonitorAlarmPreferences.Default, MonitorSoundPreferences.Default);
        var restored = DisplayPreferenceStore.Deserialize(DisplayPreferenceStore.Serialize(preferences));
        Check.That(restored.PaperLayout == 1 && restored.Display.Slots.SequenceEqual(preferences.Display.Slots) && restored.Alarms is not null,
            "preferences survive the shared document format");
        bool rejected = false;
        try { _ = DisplayPreferenceStore.Deserialize("{\"Version\":99}"u8); }
        catch (Exception error) when (error is ArgumentException or System.Text.Json.JsonException) { rejected = true; }
        Check.That(rejected, "documents the store would reject are rejected");
    }

    private static void StudentsJoinWithTheCodeAndReceiveTheCurrentGeneration()
    {
        Run(async () =>
        {
            await using var host = new ClassroomHost(IPAddress.Loopback, 0, "246810");
            host.Start([Restart(0)], new ClockMessage(0, 1_000_000_000, true));
            var wrong = await Join(host, "111111", "Mallory");
            Check.That(wrong.Rejection == "Classroom.WrongJoinCode", "a wrong join code is refused");
            var (client, received, _) = await Join(host, "246810", "Alice");
            await using (client!)
            {
                await Expect(received, 2);
                Check.That(received.ToArray() is [EventMessage { Event.Kind: ClassroomEventKind.Restart }, ClockMessage { SimulationTimeNs: 1_000_000_000 }],
                    "a joining student receives the log and the latest clock");
                Check.That(host.Students.Single().Name == "Alice", "the teacher sees the joined student");
                host.Publish(new ClassroomEvent(ClassroomEventKind.VitalStep, 0, 2_000_000_000, VitalValues: new Dictionary<VitalSign, int> { [VitalSign.HeartRateBpm] = 100 }));
                host.Publish(Restart(1));
                host.UpdateClock(new ClockMessage(1, 0, true));
                await Expect(received, 5);
                Check.That(received.Skip(2).Select(message => message.GetType()).SequenceEqual([typeof(EventMessage), typeof(EventMessage), typeof(ClockMessage)]),
                    "live events and clocks follow in order");
            }
            var (late, lateReceived, _) = await Join(host, "246810", "Bob");
            await using (late!)
            {
                await Expect(lateReceived, 2);
                Check.That(lateReceived.ToArray() is [EventMessage { Event.Generation: 1 }, ClockMessage { Generation: 1 }],
                    "a late joiner replays only the generation after the last restart");
            }
        });
    }

    private static void ExamsReachStudentsWithoutAnswersAndCollectSubmissions()
    {
        Run(async () =>
        {
            await using var host = new ClassroomHost(IPAddress.Loopback, 0, "135790");
            host.Start([Restart(0)], new ClockMessage(0, 0, false));
            var submitted = new ConcurrentQueue<(ClassroomStudent Student, SubmitAnswersMessage Answers)>();
            host.AnswersReceived += (student, answers) => submitted.Enqueue((student, answers));
            var exam = new ExamDefinition(Guid.NewGuid(), "Quiz", false, 0, true,
                [new("Rate?", ExamQuestionKind.Numeric, [], null, 75, 5, "bpm", 1)]);
            var (client, received, _) = await Join(host, "135790", "Alice");
            await using (client!)
            {
                await Expect(received, 2);
                host.OpenExam(exam);
                await Expect(received, 3);
                Check.That(received.Last() is ExamOpenedMessage { Exam.Questions: [{ CorrectValue: null, Tolerance: 0 }] },
                    "students receive the exam without answers");
                await client!.SubmitAsync(new SubmitAnswersMessage(exam.Id, [new ExamAnswer(0, null, 78)]), CancellationToken.None);
                await Until(() => !submitted.IsEmpty);
                Check.That(submitted.Single().Student.Name == "Alice" && submitted.Single().Answers.Answers[0].Value == 78,
                    "submitted answers reach the teacher with the student identity");
                host.CloseExam(exam.Id, student => new ExamClosedMessage(exam.Id, exam, ExamScorer.Score(exam, submitted.Single().Answers.Answers)));
                await Expect(received, 4);
                Check.That(received.Last() is ExamClosedMessage { Score.Awarded: 1, Revealed: not null }, "closing a practice exam returns the student's result");
            }
        });
    }

    private static async Task<(ClassroomClient? Client, ConcurrentQueue<ClassroomMessage> Received, string? Rejection)> Join(ClassroomHost host, string code, string name)
    {
        var received = new ConcurrentQueue<ClassroomMessage>();
        try
        {
            var client = await ClassroomClient.ConnectAsync("127.0.0.1", host.Port, code, name, CancellationToken.None);
            client.MessageReceived += received.Enqueue;
            client.Start();
            return (client, received, null);
        }
        catch (ClassroomJoinException error) { return (null, received, error.ReasonCode); }
    }

    private static Task Expect(ConcurrentQueue<ClassroomMessage> received, int count) => Until(() => received.Count >= count);

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) { throw new TimeoutException("Classroom message did not arrive."); }
            await Task.Delay(10);
        }
    }

    private static void Run(Func<Task> body) => Task.Run(body).GetAwaiter().GetResult();
}
