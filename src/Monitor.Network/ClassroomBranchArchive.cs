// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private static readonly JsonSerializerOptions ArchiveJson = new() { Converters = { new RateAdjustmentConverter() } };
    private sealed record ArchivedRate(int HeartRateBpm, int? AtrialRateBpm, string SeedHex, int VariationPermille);
    private sealed class RateAdjustmentConverter : JsonConverter<CardiacRateAdjustment>
    {
        public override CardiacRateAdjustment Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
        {
            var rate = JsonSerializer.Deserialize<ArchivedRate>(ref reader, options) ?? throw new JsonException("Missing cardiac rate.");
            return new CardiacRateAdjustment(rate.HeartRateBpm, rate.AtrialRateBpm, rate.SeedHex, rate.VariationPermille);
        }
        public override void Write(Utf8JsonWriter writer, CardiacRateAdjustment value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new ArchivedRate(value.Rate.HeartRateBpm, value.AtrialRate?.HeartRateBpm,
                value.Rate.SeedHex, value.Rate.VariationPermille), options);
    }

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
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(archive, ArchiveJson);
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
        var archive = JsonSerializer.Deserialize<ClassroomBranchArchive>(plain, ArchiveJson) ??
            throw new InvalidDataException("Invalid classroom branch archive.");
        var channels = archive.Patient.Channels.ToArray();
        if (channels.Length == 0) { throw new InvalidDataException("Empty classroom branch checkpoint."); }
        // The group has one clinical plan. Record equality in the live engine also
        // uses identity of prepared rate schedules; JSON does not retain those aliases.
        var common = channels[0].Generator.Timeline.Plan;
        JsonElement canonical = JsonSerializer.SerializeToElement(common, ArchiveJson);
        for (int i = 1; i < channels.Length; i++)
        {
            var g = channels[i].Generator;
            if (!JsonElement.DeepEquals(canonical, JsonSerializer.SerializeToElement(g.Timeline.Plan, ArchiveJson)))
            { throw new InvalidDataException("Archived channels have different clinical plans."); }
            channels[i] = channels[i] with { Generator = g with { Timeline = g.Timeline with { Plan = common } } };
        }
        var patient = archive.Patient with { Channels = channels };
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
