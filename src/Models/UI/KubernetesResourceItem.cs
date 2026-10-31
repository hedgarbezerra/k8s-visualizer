namespace KubernetesVisualizer.Models.UI;

public sealed record KubernetesResourceItem(
    string Kind,
    string Name,
    string Namespace,
    string Status,
    string GroupVersion,
    string ResourceName,
    bool IsKubernetesSystemManaged = false,
    bool IsControllerManaged = false,
    IReadOnlyDictionary<string, string>? Labels = null,
    IReadOnlyDictionary<string, string>? Annotations = null);
