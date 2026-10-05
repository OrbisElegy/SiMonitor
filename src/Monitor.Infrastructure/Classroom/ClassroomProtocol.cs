// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Monitor.Application.Classroom;
using Monitor.Application.Scenarios;
using Monitor.Simulation.Physiology;

namespace Monitor.Infrastructure.Classroom;

public enum ClassroomEventKind { Restart, Apply, VitalStep, Ventilation }

// One change of the teacher's simulation, replayed by students at the same
// simulation time. Restart and Apply carry the complete applied settings as a
// preferences document; a restart starts a new generation at time zero.
public sealed record ClassroomEvent(ClassroomEventKind Kind, long Generation, long AtSimulationTimeNs,
    string? PreferencesJson = null, IReadOnlyDictionary<VitalSign, int>? VitalValues = null,
    VentilationTransportPlan? Ventilation = null, decimal? OxygenDemandMultiplier = null)
{
    public void Validate()
    {
        bool valid = Enum.IsDefined(Kind) && Generation >= 0 && AtSimulationTimeNs >= 0 && Kind switch
        {
            ClassroomEventKind.Restart or ClassroomEventKind.Apply => PreferencesJson is { Length: > 0 } && VitalValues is null && Ventilation is null,
            // An empty step returns every sign to its applied value.
            ClassroomEventKind.VitalStep => PreferencesJson is null && VitalValues is not null && Ventilation is null &&
                VitalValues.All(pair => Enum.IsDefined(pair.Key)),
            _ => PreferencesJson is null && VitalValues is null && Ventilation is not null,
        };
        if (!valid) { throw new ArgumentException("Classroom.InvalidEvent"); }
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization)]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(WelcomeMessage), "welcome")]
[JsonDerivedType(typeof(RejectedMessage), "rejected")]
[JsonDerivedType(typeof(ClockMessage), "clock")]
[JsonDerivedType(typeof(EventMessage), "event")]
[JsonDerivedType(typeof(ExamOpenedMessage), "examOpened")]
[JsonDerivedType(typeof(ExamClosedMessage), "examClosed")]
[JsonDerivedType(typeof(SubmitAnswersMessage), "submitAnswers")]
[JsonDerivedType(typeof(SubmissionReceiptMessage), "submissionReceipt")]
public abstract record ClassroomMessage;

public sealed record HelloMessage(int Protocol, string JoinCode, string StudentName) : ClassroomMessage;
public sealed record WelcomeMessage(string StudentId) : ClassroomMessage;
public sealed record RejectedMessage(string ReasonCode) : ClassroomMessage;
// The teacher's simulation time in the current generation; students never run ahead of it.
public sealed record ClockMessage(long Generation, long SimulationTimeNs, bool Running) : ClassroomMessage;
public sealed record EventMessage(ClassroomEvent Event) : ClassroomMessage;
// The exam without answers, as students see it.
public sealed record ExamOpenedMessage(ExamDefinition Exam) : ClassroomMessage;
// Practice exams reveal the answers and the student's score when closed.
public sealed record ExamClosedMessage(Guid ExamId, ExamDefinition? Revealed, ExamScore? Score) : ClassroomMessage;
public sealed record SubmitAnswersMessage(Guid ExamId, IReadOnlyList<ExamAnswer> Answers) : ClassroomMessage;
public sealed record SubmissionReceiptMessage(Guid ExamId, bool Accepted, string? ReasonCode) : ClassroomMessage;

// Length-prefixed UTF-8 JSON frames: a four-byte big-endian length, then the message.
public static class ClassroomFraming
{
    public const int ProtocolVersion = 1;
    public const int MaximumFrameBytes = 512 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    public static byte[] Encode(ClassroomMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        if (json.Length > MaximumFrameBytes) { throw new ArgumentException("Classroom.FrameTooLarge", nameof(message)); }
        byte[] frame = new byte[4 + json.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, json.Length);
        json.CopyTo(frame, 4);
        return frame;
    }

    public static ClassroomMessage Decode(ReadOnlySpan<byte> json)
    {
        if (json.Length is 0 or > MaximumFrameBytes) { throw new ArgumentException("Classroom.FrameTooLarge"); }
        return JsonSerializer.Deserialize<ClassroomMessage>(json, Options) ?? throw new ArgumentException("Classroom.EmptyMessage");
    }

    public static async Task WriteAsync(Stream stream, ClassroomMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        await stream.WriteAsync(Encode(message), cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    // Returns null at a clean end of stream; malformed or oversized frames throw.
    public static async Task<ClassroomMessage?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        byte[] header = new byte[4];
        if (!await FillAsync(stream, header, cancellationToken).ConfigureAwait(false)) { return null; }
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is <= 0 or > MaximumFrameBytes) { throw new ArgumentException("Classroom.FrameTooLarge"); }
        byte[] body = new byte[length];
        if (!await FillAsync(stream, body, cancellationToken).ConfigureAwait(false)) { throw new EndOfStreamException(); }
        return Decode(body);
    }

    private static async Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (offset == 0) { return false; }
                throw new EndOfStreamException();
            }
            offset += read;
        }
        return true;
    }
}
