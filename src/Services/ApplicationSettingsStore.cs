using KubernetesVisualizer.Models.UI;
using Microsoft.Data.Sqlite;

namespace KubernetesVisualizer.Services;

public sealed class ApplicationSettingsStore(ConnectionProfileStore profileStore)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await profileStore.InitializeAsync(cancellationToken);
            await using var connection = new SqliteConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS application_settings (
                    key TEXT PRIMARY KEY NOT NULL,
                    value TEXT NOT NULL
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    public async Task<KubernetesLastSelection> LoadLastSelectionAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            return new KubernetesLastSelection(
                NullIfEmpty(await ReadSettingAsync(connection, "last_connection_id", cancellationToken)),
                NullIfEmpty(await ReadSettingAsync(connection, "last_namespace", cancellationToken)));
        }
        finally { _gate.Release(); }
    }

    public async Task SaveLastSelectionAsync(string? connectionId, string? namespaceName,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await WriteSettingAsync(connection, (SqliteTransaction)transaction,
                "last_connection_id", connectionId ?? string.Empty, cancellationToken);
            await WriteSettingAsync(connection, (SqliteTransaction)transaction,
                "last_namespace", namespaceName ?? string.Empty, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<string?> GetApplicationSettingAsync(string key, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            return await ReadSettingAsync(connection, key, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task SetApplicationSettingAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            if (value is null)
            {
                await using var delete = connection.CreateCommand();
                delete.Transaction = (SqliteTransaction)transaction;
                delete.CommandText = "DELETE FROM application_settings WHERE key = $key;";
                delete.Parameters.AddWithValue("$key", key);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await WriteSettingAsync(connection, (SqliteTransaction)transaction, key, value, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private static async Task<string?> ReadSettingAsync(SqliteConnection connection, string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM application_settings WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static async Task WriteSettingAsync(SqliteConnection connection, SqliteTransaction transaction,
        string key, string value, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO application_settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string ConnectionString() => new SqliteConnectionStringBuilder
    {
        DataSource = profileStore.DatabasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = true
    }.ToString();
}
