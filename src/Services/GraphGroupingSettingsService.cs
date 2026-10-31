using KubernetesVisualizer.Models.Configuration;
using Microsoft.Extensions.Options;

namespace KubernetesVisualizer.Services;

/// <summary>Persists additional graph grouping labels in SQLite.</summary>
public sealed class GraphGroupingSettingsService(
    ApplicationSettingsStore store,
    IOptions<GraphGroupingOptions> options)
{
    private const string SettingKey = "graph.related_label_keys";
    public IReadOnlyList<string> DefaultRelatedLabelKeys { get; } = Array.AsReadOnly(
        options.Value.DefaultRelatedLabelKeys
        .Select(key => key.Trim())
        .Where(key => key.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToArray());

    public async Task<IReadOnlyList<string>> GetRelatedLabelKeysAsync(CancellationToken cancellationToken = default)
    {
        var storedValue = await store.GetApplicationSettingAsync(SettingKey, cancellationToken);
        var configuredKeys = storedValue?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        return DefaultRelatedLabelKeys.Concat(configuredKeys)
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    public async Task SaveRelatedLabelKeysAsync(IEnumerable<string> labelKeys, CancellationToken cancellationToken = default)
    {
        var normalized = labelKeys.Select(key => key.Trim())
            .Where(key => key.Length > 0 && !DefaultRelatedLabelKeys.Contains(key, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (normalized.Any(key => key.Length > 253 || key.Any(char.IsWhiteSpace)
                                  || key.StartsWith('/') || key.EndsWith('/') || key.Count(character => character == '/') > 1))
            throw new ArgumentException("Each related label must be a valid Kubernetes label key.", nameof(labelKeys));

        await store.SetApplicationSettingAsync(SettingKey, string.Join('\n', normalized), cancellationToken);
    }
}
