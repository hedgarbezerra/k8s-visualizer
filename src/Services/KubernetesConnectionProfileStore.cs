using System.Text.Json;
using KubernetesVisualizer.Models.UI;
using Microsoft.Data.Sqlite;

namespace KubernetesVisualizer.Services;

public sealed class KubernetesConnectionProfileStore
{
    private readonly string _databasePath;
    private readonly string _legacyJsonPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public KubernetesConnectionProfileStore(IWebHostEnvironment environment, IConfiguration configuration)
    {
        var configuredPath = configuration["ConnectionProfiles:DatabasePath"];
        _databasePath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "kubernetes-visualizer.db")
            : Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(environment.ContentRootPath, configuredPath));
        _legacyJsonPath = Path.Combine(environment.ContentRootPath, "App_Data", "connections.json");
    }

    public string DatabasePath => _databasePath;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);

            await using var connection = new SqliteConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS connection_profiles (
                        id TEXT PRIMARY KEY NOT NULL,
                        name TEXT NOT NULL,
                        kubeconfig_path TEXT NOT NULL,
                        context_name TEXT NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS application_settings (
                        key TEXT PRIMARY KEY NOT NULL,
                        value TEXT NOT NULL
                    );
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await ImportLegacyJsonAsync(connection, cancellationToken);
            _initialized = true;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<KubernetesConnectionProfile>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT id, name, kubeconfig_path, context_name FROM connection_profiles ORDER BY rowid;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var profiles = new List<KubernetesConnectionProfile>();
            while (await reader.ReadAsync(cancellationToken))
                profiles.Add(new KubernetesConnectionProfile(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            return profiles;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(IReadOnlyList<KubernetesConnectionProfile> profiles, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = (SqliteTransaction)transaction;
                delete.CommandText = "DELETE FROM connection_profiles;";
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var profile in profiles)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText = "INSERT INTO connection_profiles (id, name, kubeconfig_path, context_name) VALUES ($id, $name, $path, $context);";
                insert.Parameters.AddWithValue("$id", profile.Id);
                insert.Parameters.AddWithValue("$name", profile.Name);
                insert.Parameters.AddWithValue("$path", profile.KubeconfigPath);
                insert.Parameters.AddWithValue("$context", profile.ContextName);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
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
                await ReadSettingAsync(connection, "last_connection_id", cancellationToken),
                await ReadSettingAsync(connection, "last_namespace", cancellationToken));
        }
        finally { _gate.Release(); }
    }

    public async Task SaveLastSelectionAsync(string? connectionId, string? namespaceName, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(ConnectionString());
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await WriteSettingAsync(connection, (SqliteTransaction)transaction, "last_connection_id", connectionId ?? string.Empty, cancellationToken);
            await WriteSettingAsync(connection, (SqliteTransaction)transaction, "last_namespace", namespaceName ?? string.Empty, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private static async Task<string?> ReadSettingAsync(SqliteConnection connection, string key, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM application_settings WHERE key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", key);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text && text.Length > 0 ? text : null;
    }

    private static async Task WriteSettingAsync(SqliteConnection connection, SqliteTransaction transaction, string key, string value, CancellationToken cancellationToken)
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

    private async Task ImportLegacyJsonAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT 1 FROM application_settings WHERE key = 'legacy_json_imported' LIMIT 1;";
            if (await check.ExecuteScalarAsync(cancellationToken) is not null) return;
        }

        List<KubernetesConnectionProfile> profiles = [];
        if (File.Exists(_legacyJsonPath))
        {
            await using var stream = File.OpenRead(_legacyJsonPath);
            profiles = await JsonSerializer.DeserializeAsync<List<KubernetesConnectionProfile>>(stream, cancellationToken: cancellationToken) ?? [];
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var profile in profiles)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = "INSERT OR IGNORE INTO connection_profiles (id, name, kubeconfig_path, context_name) VALUES ($id, $name, $path, $context);";
            insert.Parameters.AddWithValue("$id", profile.Id);
            insert.Parameters.AddWithValue("$name", profile.Name);
            insert.Parameters.AddWithValue("$path", profile.KubeconfigPath);
            insert.Parameters.AddWithValue("$context", profile.ContextName);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var marker = connection.CreateCommand())
        {
            marker.Transaction = (SqliteTransaction)transaction;
            marker.CommandText = "INSERT INTO application_settings (key, value) VALUES ('legacy_json_imported', 'true');";
            await marker.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private string ConnectionString() => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = true
    }.ToString();
}
