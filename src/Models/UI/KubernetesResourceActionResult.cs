namespace KubernetesVisualizer.Models.UI;

public sealed record KubernetesResourceActionResult(KubernetesResourceItem Resource, bool Succeeded, string Message);
