# 课堂资源、配对与分支公开声明

[行为与 wire 格式](../classroom-resources.md) · [Monitor.Network](network.md)。以下类型位于 `Monitor.Network`。

```csharp
public sealed record ClassroomArtifactDescriptor(string Sha256, int LengthBytes, string MediaType)
{
    public const string StudyRecordMediaType = "application/vnd.simonitor.waveform-record.v1";
    public const string ImageMediaType = "image/png";
    public const int MaximumLengthBytes = 16 * 1024 * 1024;
}
public sealed record ClassroomDiscoveredHost(string DisplayName, Uri Origin, string CertificateSha256);
public static class ClassroomDiscovery
{
    public static Task AdvertiseAsync(IPAddress interfaceAddress, ClassroomDiscoveredHost advertisement,
        CancellationToken cancellationToken);
    public static Task<IReadOnlyList<ClassroomDiscoveredHost>> BrowseAsync(IPAddress interfaceAddress,
        TimeSpan duration, CancellationToken cancellationToken = default);
}
public sealed class ClassroomPairingTicket
{
    public Uri Origin { get; }
    public Guid SessionId { get; }
    public string CertificateSha256 { get; }
    public static ClassroomPairingTicket Create(Uri origin, Guid sessionId, string certificateSha256, string joinCode);
    public string Encode();
    public static ClassroomPairingTicket Parse(string ticket);
    public override string ToString();
}

public enum ClassroomBranchDisposition { Archive, Discard }
public sealed record ClassroomBranchAssignment(Guid StudentId, Guid SessionId, Guid SharedInstanceId,
    Guid InstanceId, string State, long EffectiveSimTimeNs, string? Reason);
public sealed record ClassroomBranchArchive(Guid StudentId, Guid SharedInstanceId, long ForkSimTimeNs,
    ClassroomControlSnapshot Device, ClassroomTelemetrySnapshot Telemetry, WaveformBlockRingState RetainedWaveforms,
    PhysiologyWaveformGroupState Patient, LiveMeasurementSnapshot Measurements, IReadOnlyList<JsonElement> RetainedEvents);

public sealed partial class ClassroomHost
{
    public ClassroomArtifactDescriptor PublishStudyRecord(WaveformRecordArchive record, Guid? studentId = null);
    public ClassroomArtifactDescriptor PublishLearnerImage(ReadOnlySpan<byte> png, Guid? studentId = null);
    public bool RemoveArtifact(string sha256);
    public Task RunIllustrationAsync(PhysiologyIllustrationConfiguration configuration, CancellationToken cancellationToken);
    public ClassroomBranchAssignment GetBranchAssignment(Guid studentId);
    public string? GetBranchFailure(Guid studentId);
    public ClassroomBranchAssignment SchedulePrivateBranch(Guid studentId, long? effectiveSimTimeNs = null);
    public ClassroomBranchAssignment ScheduleSharedReturn(Guid studentId, ClassroomBranchDisposition disposition,
        long? effectiveSimTimeNs = null);
    public ClassroomTelemetrySnapshot GetPrivateTelemetry(Guid studentId);
    public ClassroomControlSnapshot GetPrivateControlSnapshot(Guid studentId);
    public ClassroomScheduledControl SchedulePrivateConfiguration(Guid studentId, Guid changeId, ulong basedOnConfigurationRevision,
        long effectiveSimTimeNs, ClassroomConfigurationContent content, IReadOnlyList<Guid> requiredClients, int preparedQuorum,
        ClassroomChangeConflict conflict = ClassroomChangeConflict.RejectOnConflict, bool requireTeacherConfirmation = false);
    public ClassroomBranchArchive? ReadArchivedBranch(Guid instanceId);
}
public sealed partial class ClassroomBootstrap
{
    public ClassroomBranchArchive? ReadArchivedBranch(Guid session, Guid instanceId);
}
public sealed partial class ClassroomStudentClient
{
    public static Task<ClassroomStudentClient> JoinAsync(ClassroomPairingTicket ticket,
        X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default);
    public static Task<ClassroomStudentClient> JoinWithPresentationAsync(ClassroomPairingTicket ticket, JsonElement hello,
        X509Certificate2? trustedRoot = null, CancellationToken cancellationToken = default);
    public Task<IReadOnlyList<ClassroomArtifactDescriptor>> ListArtifactsAsync(CancellationToken cancellationToken = default);
    public Task<byte[]> DownloadArtifactAsync(ClassroomArtifactDescriptor descriptor, CancellationToken cancellationToken = default);
    public Task<WaveformRecordArchive> DownloadStudyRecordAsync(ClassroomArtifactDescriptor descriptor,
        CancellationToken cancellationToken = default);
    public Task<ClassroomBranchAssignment> GetBranchAssignmentAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomStudentClient> ConnectAssignedBranchAsync(CancellationToken cancellationToken = default);
    public Task<ClassroomStudentClient> ConnectAssignedBranchAsync(JsonElement? presentation, CancellationToken cancellationToken = default);
}
```

[资源发布和下载](../../../src/Monitor.Network/ClassroomArtifact.cs) · [客户端资源校验](../../../src/Monitor.Network/ClassroomArtifactClient.cs) · [配对](../../../src/Monitor.Network/ClassroomPairingTicket.cs) · [发现](../../../src/Monitor.Network/ClassroomDiscovery.cs) · [教师分支](../../../src/Monitor.Network/ClassroomBranches.cs) · [学生分支](../../../src/Monitor.Network/ClassroomBranchClient.cs) · [本地归档](../../../src/Monitor.Network/ClassroomBranchArchive.cs)
