// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monitor.Application.Measurements;
using Monitor.Simulation.Acquisition;
using Monitor.Simulation.Physiology;

namespace Monitor.Network;

public sealed record ClassroomBranchArchive(Guid StudentId, Guid SharedInstanceId, long ForkSimTimeNs,
    ClassroomControlSnapshot Device, ClassroomTelemetrySnapshot Telemetry, WaveformBlockRingState RetainedWaveforms,
    PhysiologyWaveformGroupState Patient, LiveMeasurementSnapshot Measurements, IReadOnlyList<JsonElement> RetainedEvents);

public sealed partial class ClassroomHost
{
    public ClassroomBranchArchive? ReadArchivedBranch(Guid instanceId) => _identity.ReadArchivedBranch(_ring.SessionId, instanceId);

    private ClassroomBranchArchive CaptureBranchArchive(ClassroomHost child, Guid studentId, long forkTimeNs) =>
        new ClassroomBranchArchive(studentId, _ring.InstanceId, forkTimeNs, child.ReadControlSnapshot(),
            child.CurrentTelemetrySnapshot(), child._ring.CaptureState(), child._source!.CaptureState(),
            child._measurements!.Read(child._ring.NextBlockStartSimTimeNs), child._events.Select(entry => entry.Envelope).ToArray());
}

public sealed partial class ClassroomBootstrap
{
    private static string ArchivePurpose(Guid session, Guid instance) =>
        "classroom.branch." + session.ToString("N") + "." + instance.ToString("N");

    private const string CreateArchive = "CREATE TABLE IF NOT EXISTS classroom_branch_archives(session TEXT NOT NULL, instance TEXT NOT NULL, owner TEXT NOT NULL, sealed BLOB NOT NULL, PRIMARY KEY(session,instance)) STRICT";

    private SqliteConnection OpenArchiveDatabase()
    {
        var database = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = DatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString());
        try
        {
            database.Open();
            using var setup = database.CreateCommand();
            setup.CommandText = CreateArchive;
            setup.ExecuteNonQuery();
            return database;
        }
        catch { database.Dispose(); throw; }
    }

    internal void WriteBranchArchive(SqliteConnection database, SqliteTransaction transaction, ClassroomBranchArchive archive)
    {
        Guid instance = archive.Telemetry.InstanceId;
        Guid session = archive.Telemetry.SessionId;
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(archive, WaveformCheckpointJson.Options);
        try
        {
            _ = DecodeBranchArchive(plain);
            using var command = database.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = CreateArchive;
            command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO classroom_branch_archives(session,instance,owner,sealed) VALUES ($session,$instance,$owner,$sealed)";
            command.Parameters.AddWithValue("$session", session.ToString("N"));
            command.Parameters.AddWithValue("$instance", instance.ToString("N"));
            command.Parameters.AddWithValue("$owner", Principal.PrincipalId.ToString("N"));
            command.Parameters.AddWithValue("$sealed", PayloadProtector.Protect(plain, ArchivePurpose(session, instance)));
            command.ExecuteNonQuery();
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static ClassroomBranchArchive DecodeBranchArchive(byte[] plain)
    {
        var archive = JsonSerializer.Deserialize<ClassroomBranchArchive>(plain, WaveformCheckpointJson.Options) ??
            throw new InvalidDataException("Invalid classroom branch archive.");
        var patient = WaveformCheckpointJson.Normalize(archive.Patient);
        _ = PhysiologyWaveformGroup.Restore(patient);
        return archive with { Patient = patient };
    }

    // This operates on the authenticated teacher's local encrypted archive, outside all classroom network routes.
    public ClassroomBranchArchive? ReadArchivedBranch(Guid session, Guid instanceId)
    {
        using var database = OpenArchiveDatabase();
        using var command = database.CreateCommand();
        command.CommandText = "SELECT sealed FROM classroom_branch_archives WHERE session=$session AND instance=$instance AND owner=$owner";
        command.Parameters.AddWithValue("$session", session.ToString("N"));
        command.Parameters.AddWithValue("$instance", instanceId.ToString("N"));
        command.Parameters.AddWithValue("$owner", Principal.PrincipalId.ToString("N"));
        if (command.ExecuteScalar() is not byte[] sealedBytes) { return null; }
        byte[] plain = PayloadProtector.Unprotect(sealedBytes, ArchivePurpose(session, instanceId));
        try { return DecodeBranchArchive(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
}
