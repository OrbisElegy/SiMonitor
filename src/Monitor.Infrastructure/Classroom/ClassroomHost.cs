// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Channels;
using Monitor.Application.Classroom;

namespace Monitor.Infrastructure.Classroom;

public sealed record ClassroomStudent(string StudentId, string Name, string Endpoint);

// Teacher-side classroom listener. Students join with the join code and then
// receive the event log since the last restart, the latest clock and any open
// exam, followed by live updates. Students may only submit exam answers.
// Callbacks run on background threads; hosts marshal them to their UI.
public sealed class ClassroomHost : IAsyncDisposable
{
    public const int MaximumStudents = 100;
    public const int MaximumNameLength = 40;
    public const int MaximumLogEvents = 20_000;
    private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);
    // Room for a complete log snapshot plus live updates.
    private const int OutboundCapacity = MaximumLogEvents + 4096;

    private sealed class Connection(TcpClient client, string studentId, string name)
    {
        internal TcpClient Client { get; } = client;
        internal ClassroomStudent Student { get; } = new(studentId, name, client.Client.RemoteEndPoint?.ToString() ?? "");
        internal Channel<ClassroomMessage> Outbound { get; } = Channel.CreateBounded<ClassroomMessage>(
            new BoundedChannelOptions(OutboundCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }

    private readonly object _gate = new();
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Dictionary<string, Connection> _connections = new(StringComparer.Ordinal);
    private readonly List<ClassroomEvent> _log = [];
    private ClockMessage _clock = new(0, 0, false);
    private ExamOpenedMessage? _exam;
    private Task? _accepting;

    public ClassroomHost(IPAddress address, int port, string joinCode)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (port is < 0 or > 65535) { throw new ArgumentOutOfRangeException(nameof(port)); }
        if (string.IsNullOrWhiteSpace(joinCode)) { throw new ArgumentException("Classroom.InvalidJoinCode", nameof(joinCode)); }
        JoinCode = joinCode;
        _listener = new TcpListener(address, port);
    }

    public string JoinCode { get; }
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public event Action? StudentsChanged;
    public event Action<ClassroomStudent, SubmitAnswersMessage>? AnswersReceived;

    public IReadOnlyList<ClassroomStudent> Students
    {
        get { lock (_gate) { return _connections.Values.Select(connection => connection.Student).ToArray(); } }
    }

    // Six random decimal digits from the system cryptographic source.
    public static string CreateJoinCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    public void Start(IEnumerable<ClassroomEvent> log, ClockMessage clock)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);
        lock (_gate)
        {
            _log.Clear();
            foreach (var item in log) { Append(item); }
            _clock = clock;
        }
        _listener.Start();
        _accepting = AcceptAsync(_stopping.Token);
    }

    // A restart replaces the log, so late joiners replay only the current generation.
    public void Publish(ClassroomEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Validate();
        lock (_gate)
        {
            if (item.Kind == ClassroomEventKind.Restart) { _log.Clear(); }
            Append(item);
            Broadcast(new EventMessage(item));
        }
    }

    public void UpdateClock(ClockMessage clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        lock (_gate)
        {
            _clock = clock;
            Broadcast(clock);
        }
    }

    public void OpenExam(ExamDefinition exam)
    {
        ArgumentNullException.ThrowIfNull(exam);
        exam.Validate();
        lock (_gate)
        {
            _exam = new ExamOpenedMessage(exam.WithoutAnswers());
            Broadcast(_exam);
        }
    }

    // Closing sends each student its own result through the given function.
    public void CloseExam(Guid examId, Func<ClassroomStudent, ExamClosedMessage> resultFor)
    {
        ArgumentNullException.ThrowIfNull(resultFor);
        lock (_gate)
        {
            if (_exam?.Exam.Id == examId) { _exam = null; }
            foreach (var connection in _connections.Values) { Enqueue(connection, resultFor(connection.Student)); }
        }
    }

    public void Send(string studentId, ClassroomMessage message)
    {
        lock (_gate)
        {
            if (_connections.TryGetValue(studentId, out var connection)) { Enqueue(connection, message); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        _listener.Stop();
        Connection[] connections;
        lock (_gate) { connections = [.. _connections.Values]; }
        foreach (var connection in connections) { connection.Client.Dispose(); }
        if (_accepting is not null)
        {
            try { await _accepting.ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { }
        }
        _stopping.Dispose();
    }

    // A student that cannot keep up is disconnected rather than silently skipping
    // messages, which would desynchronize its replay; it can join again.
    private static void Enqueue(Connection connection, ClassroomMessage message)
    {
        if (!connection.Outbound.Writer.TryWrite(message)) { connection.Client.Dispose(); }
    }

    private void Append(ClassroomEvent item)
    {
        if (_log.Count >= MaximumLogEvents) { throw new InvalidOperationException("Classroom.LogFull"); }
        _log.Add(item);
    }

    private void Broadcast(ClassroomMessage message)
    {
        foreach (var connection in _connections.Values) { Enqueue(connection, message); }
    }

    private async Task AcceptAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException or SocketException) { return; }
            _ = ServeAsync(client, cancellationToken);
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
    {
        Connection? connection = null;
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            helloTimeout.CancelAfter(HelloTimeout);
            if (await ClassroomFraming.ReadAsync(stream, helloTimeout.Token).ConfigureAwait(false) is not HelloMessage hello) { return; }
            string? rejection = hello.Protocol != ClassroomFraming.ProtocolVersion ? "Classroom.ProtocolMismatch" :
                !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(hello.JoinCode ?? ""), System.Text.Encoding.UTF8.GetBytes(JoinCode))
                    ? "Classroom.WrongJoinCode" :
                string.IsNullOrWhiteSpace(hello.StudentName) || hello.StudentName.Length > MaximumNameLength ? "Classroom.InvalidName" : null;
            lock (_gate)
            {
                if (rejection is null && _connections.Count >= MaximumStudents) { rejection = "Classroom.Full"; }
                if (rejection is null)
                {
                    connection = new Connection(client, Guid.NewGuid().ToString("N"), hello.StudentName.Trim());
                    // Queue the welcome and the current state before live updates, under the same lock.
                    Enqueue(connection, new WelcomeMessage(connection.Student.StudentId));
                    foreach (var item in _log) { Enqueue(connection, new EventMessage(item)); }
                    Enqueue(connection, _clock);
                    if (_exam is not null) { Enqueue(connection, _exam); }
                    _connections[connection.Student.StudentId] = connection;
                }
            }
            if (rejection is not null)
            {
                await ClassroomFraming.WriteAsync(stream, new RejectedMessage(rejection), cancellationToken).ConfigureAwait(false);
                return;
            }
            StudentsChanged?.Invoke();
            var writing = WriteAsync(connection!, stream, cancellationToken);
            while (await ClassroomFraming.ReadAsync(stream, cancellationToken).ConfigureAwait(false) is { } message)
            {
                if (message is not SubmitAnswersMessage answers) { break; }
                AnswersReceived?.Invoke(connection!.Student, answers);
            }
            connection!.Outbound.Writer.TryComplete();
            await writing.ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or SocketException or ArgumentException or System.Text.Json.JsonException or
            OperationCanceledException or ObjectDisposedException or NotSupportedException or InvalidOperationException)
        {
            // Malformed input or a lost connection ends only this student's session.
        }
        finally
        {
            if (connection is not null)
            {
                lock (_gate) { _connections.Remove(connection.Student.StudentId); }
                connection.Outbound.Writer.TryComplete();
                StudentsChanged?.Invoke();
            }
            client.Dispose();
        }
    }

    private static async Task WriteAsync(Connection connection, NetworkStream stream, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in connection.Outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            { await ClassroomFraming.WriteAsync(stream, message, cancellationToken).ConfigureAwait(false); }
        }
        catch (Exception error) when (error is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        { connection.Client.Dispose(); }
    }
}
