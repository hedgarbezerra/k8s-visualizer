namespace KubernetesVisualizer.Models.UI;

public sealed record WorkloadReplicaState(int CurrentReplicas, int? PreviousReplicas);
