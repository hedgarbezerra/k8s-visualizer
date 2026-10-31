namespace KubernetesVisualizer.Models.UI;

public sealed record KubernetesResourceType(
    string Name,
    string Kind,
    string GroupVersion,
    bool Namespaced,
    string ApiPath);
