namespace KubernetesVisualizer.Models.UI;

public enum KubernetesSelectionChangeKind
{
    Connection,
    Cluster,
    Namespace
}

public sealed record KubernetesSelectionChangedEventArgs(
    long Revision,
    KubernetesSelectionChangeKind Kind,
    string? ConnectionId,
    string? Namespace);
