// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net.Sockets;

namespace Monitor.Infrastructure.Classroom;

public sealed class ClassroomJoinException(string reasonCode) : Exception(reasonCode)
{
    public string ReasonCode { get; } = reasonCode;
}

// Student-side connection. Messages after the welcome are delivered in order on
// a background thread; the owner marshals them to its UI.
public sealed class ClassroomClient : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _writing = new(1, 1);
    private Task? _receiving;

    private ClassroomClient(TcpClient client, string studentId)
    {
        _client = client;
        _stream = client.GetStream();
        StudentId = studentId;
    }

    public string StudentId { get; }
    public event Action<ClassroomMessage>? MessageReceived;
    // Raised once when the connection ends, with null after a local disposal.
    public event Action<Exception?>? Disconnected;

    public static async Task<ClassroomClient> ConnectAsync(string host, int port, string joinCode, string studentName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535) { throw new ClassroomJoinException("Classroom.InvalidAddress"); }
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host.Trim(), port, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await ClassroomFraming.WriteAsync(stream, new HelloMessage(ClassroomFraming.ProtocolVersion, joinCode.Trim(), studentName.Trim()),
                cancellationToken).ConfigureAwait(false);
            var reply = await ClassroomFraming.ReadAsync(stream, cancellationToken).ConfigureAwait(false);
            return reply switch
            {
                WelcomeMessage welcome => new ClassroomClient(client, welcome.StudentId),
                RejectedMessage rejected => throw new ClassroomJoinException(rejected.ReasonCode),
                _ => throw new ClassroomJoinException("Classroom.UnexpectedReply"),
            };
        }
        catch (Exception error) when (error is SocketException or IOException or ArgumentException or System.Text.Json.JsonException)
        {
            client.Dispose();
            throw new ClassroomJoinException("Classroom.ConnectionFailed");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    // Starts delivering messages; call after subscribing to the events.
    public void Start() => _receiving ??= ReceiveAsync(_stopping.Token);

    public async Task SubmitAsync(SubmitAnswersMessage answers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answers);
        await _writing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await ClassroomFraming.WriteAsync(_stream, answers, cancellationToken).ConfigureAwait(false); }
        finally { _writing.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        _client.Dispose();
        if (_receiving is not null)
        {
            try { await _receiving.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _stopping.Dispose();
        _writing.Dispose();
    }

    private async Task ReceiveAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (await ClassroomFraming.ReadAsync(_stream, cancellationToken).ConfigureAwait(false) is { } message)
            { MessageReceived?.Invoke(message); }
            failure = new EndOfStreamException();
        }
        catch (Exception error) when (error is IOException or SocketException or ArgumentException or System.Text.Json.JsonException or ObjectDisposedException)
        { failure = cancellationToken.IsCancellationRequested ? null : error; }
        catch (OperationCanceledException) { }
        Disconnected?.Invoke(failure);
    }
}
