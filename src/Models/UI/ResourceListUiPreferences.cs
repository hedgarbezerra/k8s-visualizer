namespace KubernetesVisualizer.Models.UI;

public sealed record ResourceListUiPreferences(
    string? SelectedResourceKey,
    string Search,
    string[]? SelectedResourceKeys = null,
    bool ShowAllResources = false);
