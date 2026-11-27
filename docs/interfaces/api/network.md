# Monitor.Network 公开声明索引

[接口总览](../README.md) · [课堂网络的部署、协议和失败语义](../classroom-network.md)

以下类型位于 `Monitor.Network` 命名空间。会话与订阅使用异步 I/O，仿真时间和校时读数均以纳秒计。

## 教师身份与入场

[ClassroomBootstrap.cs](../../../src/Monitor.Network/ClassroomBootstrap.cs) · [ClassroomAdmission.cs](../../../src/Monitor.Network/ClassroomAdmission.cs)

```csharp
public sealed class ClassroomBootstrap : IDisposable
{
    public IBootstrapIdentityRepository Repository { get; }
    public LocalPrincipal Principal { get; }
    public static void Initialize(string databasePath, ReadOnlySpan<byte> masterKey, string adminName,
        ReadOnlySpan<char> bootstrapPassword, ReadOnlySpan<char> adminPassword, string blockedPasswordHashesPath);
    public static ClassroomBootstrap AuthenticateHost(string databasePath, ReadOnlySpan<byte> masterKey,
        string username, ReadOnlySpan<char> password);
    public void Dispose();
}

public sealed class ClassroomAdmission : IDisposable
{
    public ClassroomAdmission(Guid sessionId, Guid instanceId, TimeProvider? timeProvider = null);
    public Guid SessionId { get; }
    public Guid InstanceId { get; }
    public string IssueInvitation(Guid studentId);
    public bool TryRedeem(string code, string source, [NotNullWhen(true)] out string? token);
    public bool TryAuthorize(string token, out Guid studentId, out DateTimeOffset expiresAt);
    public void Dispose();
}
```

## 教师监听

成功启动后 Host 独占波形环，停止时释放入场服务和订阅。调用方在 Host 生命周期内保持证书可用。[ClassroomHost.cs](../../../src/Monitor.Network/ClassroomHost.cs)

```csharp
public sealed class ClassroomHost : IAsyncDisposable
{
    public Uri BaseAddress { get; }
    public static Task<ClassroomHost> StartAsync(IPAddress listenAddress, int port, string hostName, X509Certificate2 certificate,
        WaveformBlockRing ring, ClassroomAdmission admission, ClassroomBootstrap identity, X509Certificate2? trustedRoot = null,
        CancellationToken cancellationToken = default);
    public WaveformBlockAppendResult Publish(ReadOnlySpan<byte> rawEnvelope);
    public ValueTask DisposeAsync();
}
```

## 学生连接与订阅

每个订阅支持一个活动接收操作。`NextBlockSequence` 是首个尚未交付的块，断线后可交给同一学生连接的 `ResumeAsync`。[ClassroomStudentClient.cs](../../../src/Monitor.Network/ClassroomStudentClient.cs) · [ClassroomWaveformSubscription.cs](../../../src/Monitor.Network/ClassroomWaveformSubscription.cs)

```csharp
public sealed class ClassroomStudentClient : IDisposable
{
    public static Task<ClassroomStudentClient> JoinAsync(Uri endpoint, string joinCode,
        X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default);
    public Task<ClassroomManifest> GetSessionAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomClockSample> SynchronizeClockAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomWaveformSubscription> SubscribeAsync(ClassroomManifest manifest,
        ulong? fromBlockSequence = null, CancellationToken cancellationToken = default);
    public Task<ClassroomWaveformSubscription> ResumeAsync(ClassroomManifest previousManifest,
        ulong nextBlockSequence, CancellationToken cancellationToken = default);
    public void Dispose();
}

public sealed class ClassroomWaveformSubscription : IDisposable, IAsyncDisposable
{
    public ulong NextBlockSequence { get; }
    public WaveformBlockReplayResult ReadBufferedBlocks(ulong firstBlockSequence, int maximumBlocks);
    public ValueTask<WaveformEnvelope?> ReceiveAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
    public void Dispose();
}
```

## 控制消息

[ClassroomWire.cs](../../../src/Monitor.Network/ClassroomWire.cs)

```csharp
public static class ClassroomWire
{
    public const string WaveformSubprotocol = "simonitor.waveforms.v1";
    public const int AcknowledgementBytes = 48;
}

public sealed record ClassroomManifest(int ProtocolVersion, Guid StudentId, Guid SessionId, Guid InstanceId,
    ulong TimebaseEpoch, ulong StreamEpoch, ulong OldestBlockSequence, ulong NextBlockSequence, long NextBlockStartSimTimeNs);
public sealed record ClassroomClockReply(long T1, long T2, long T3);
public sealed record ClassroomClockSample(long ClientSendNs, long HostReceiveNs, long HostSendNs, long ClientReceiveNs)
{
    public long RoundTripNs { get; }
    public long HostOffsetNs { get; }
}
```
