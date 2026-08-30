// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Data.Sqlite;

namespace Monitor.Infrastructure.Identity;

internal sealed class SqliteIdentityDatabase
{
    private readonly string _connectionString;

    public SqliteIdentityDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        if (!Path.IsPathRooted(databasePath))
        {
            throw new ArgumentException("The identity database path must be absolute.", nameof(databasePath));
        }

        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (directory is null || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("The identity database directory does not exist.");
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();

        using SqliteConnection connection = Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
    }

    public SqliteConnection Open()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = FULL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS schema_info (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            schema_version INTEGER NOT NULL CHECK (schema_version = 1)
        ) STRICT;
        INSERT OR IGNORE INTO schema_info(singleton, schema_version) VALUES (1, 1);

        CREATE TABLE IF NOT EXISTS identity_bootstrap (
            singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
            lifecycle_state INTEGER NOT NULL,
            protected_verifier BLOB,
            root_retired INTEGER NOT NULL CHECK (root_retired IN (0, 1)),
            revision INTEGER NOT NULL CHECK (revision >= 1)
        ) STRICT;

        CREATE TABLE IF NOT EXISTS identity_accounts (
            account_row_id INTEGER PRIMARY KEY,
            username_lookup TEXT NOT NULL UNIQUE,
            protected_payload BLOB NOT NULL,
            revision INTEGER NOT NULL CHECK (revision >= 1)
        ) STRICT;

        CREATE TABLE IF NOT EXISTS identity_audit (
            audit_id TEXT PRIMARY KEY,
            protected_payload BLOB NOT NULL,
            recorded_at_ticks INTEGER NOT NULL
        ) STRICT;
        CREATE TRIGGER IF NOT EXISTS identity_audit_no_update
        BEFORE UPDATE ON identity_audit BEGIN
            SELECT RAISE(ABORT, 'identity audit is append-only');
        END;
        CREATE TRIGGER IF NOT EXISTS identity_audit_no_delete
        BEFORE DELETE ON identity_audit BEGIN
            SELECT RAISE(ABORT, 'identity audit is append-only');
        END;

        CREATE TABLE IF NOT EXISTS authentication_failures (
            failure_id INTEGER PRIMARY KEY,
            dimension INTEGER NOT NULL CHECK (dimension IN (1, 2)),
            subject_lookup TEXT NOT NULL,
            occurred_at_ticks INTEGER NOT NULL
        ) STRICT;
        CREATE INDEX IF NOT EXISTS ix_authentication_failures_subject
        ON authentication_failures(dimension, subject_lookup, occurred_at_ticks);

        CREATE TABLE IF NOT EXISTS authentication_locks (
            dimension INTEGER NOT NULL CHECK (dimension IN (1, 2)),
            subject_lookup TEXT NOT NULL,
            locked_until_ticks INTEGER NOT NULL,
            PRIMARY KEY (dimension, subject_lookup)
        ) STRICT;
        """;
}
