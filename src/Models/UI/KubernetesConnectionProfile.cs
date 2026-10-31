namespace KubernetesVisualizer.Models.UI;

public sealed record KubernetesConnectionProfile(
    string Id,
    string Name,
    string KubeconfigPath,
    string ContextName);
