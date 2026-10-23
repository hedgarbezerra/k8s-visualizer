namespace KubernetesVisualizer.Models.UI;

public sealed record KubernetesContextOption(
    string Name,
    string ClusterName,
    string Server,
    string? DefaultNamespace,
    bool IsCurrent);
