using KubernetesVisualizer.Models.UI;
using Microsoft.Data.Sqlite;

namespace KubernetesVisualizer.Services;

public sealed class ConnectionProfileStore
{
    private readonly string _databasePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    public ConnectionProfileStore(IWebHostEnvironment environment)
    {
        _databasePath = Path.GetFullPath(Path.Combine(environment.ContentRootPath,
            "App_Data", "kubernetes-visualizer.db"));
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
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

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

    private string ConnectionString() => new SqliteConnectionStringBuilder
    {
        DataSource = _databasePath,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = true
    }.ToString();
}
