using System.Collections.Concurrent;
using KubernetesVisualizer.Models.UI;

namespace KubernetesVisualizer.Services;

/// <summary>Loads the graph's Kubernetes snapshot and builds reusable, evidence-labelled relations.</summary>
public sealed class ResourceGraphService(ClusterService clusterService)
{
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private string? _cacheKey;
    private KubernetesResourceGraphData? _cached;

    public void Invalidate()
    {
        _cacheGate.Wait();
        try
        {
            _cacheKey = null;
            _cached = null;
        }
        finally { _cacheGate.Release(); }
    }

    public async Task<KubernetesResourceGraphData> LoadAsync(string namespaceName, bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var key = $"{clusterService.ActiveConnectionId}|{clusterService.CurrentContextName}|{namespaceName}";
        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _cacheKey == key && _cached is not null) return _cached;
        }
        finally { _cacheGate.Release(); }

        var types = await clusterService.GetResourceTypesAsync(cancellationToken);
        var items = new ConcurrentBag<KubernetesResourceGraphItem>();
        var failures = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(types.Where(type => type.Kind != "Namespace"),
            new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = cancellationToken },
            async (type, token) =>
            {
                try
                {
                    foreach (var item in await clusterService.ListResourceGraphItemsAsync(
                                 type, type.Namespaced ? namespaceName : null, token))
                        items.Add(item);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failures.Add(type.Kind);
                }
            });

        var snapshot = new KubernetesResourceGraphData(
            items.OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
            failures.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray());
        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            _cacheKey = key;
            _cached = snapshot;
        }
        finally { _cacheGate.Release(); }
        return snapshot;
    }

    public static KubernetesResourceGraphRelations BuildRelations(IReadOnlyList<KubernetesResourceGraphItem> items,
        IReadOnlyCollection<string> relatedLabelKeys)
    {
        var byUid = items.Where(item => !string.IsNullOrWhiteSpace(item.Uid))
            .GroupBy(item => item.Uid, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var byName = items.GroupBy(item => (item.Kind, item.Name, item.Namespace))
            .ToDictionary(group => group.Key, group => group.First());
        var edges = new List<KubernetesGraphEdge>();
        var ownerByUid = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var item in byUid.Values)
        {
            var owners = item.OwnerReferences.Where(reference => byUid.ContainsKey(reference.Uid)).ToArray();
            var controller = owners.FirstOrDefault(reference => reference.IsController);
            var primary = controller ?? owners.FirstOrDefault();
            if (primary is null) continue;
            ownerByUid[item.Uid] = primary.Uid;
            var knownControllerPair = primary.IsController && byUid.TryGetValue(primary.Uid, out var ownerItem)
                && IsControllerPair(ownerItem.Kind, item.Kind);
            var axis = knownControllerPair ? "property+control" : "property";
            if (primary.BlockOwnerDeletion) axis += "+protection";
            edges.Add(new KubernetesGraphEdge(primary.Uid, item.Uid,
                knownControllerPair ? "controller-owner" : primary.IsController ? "owner" : "owner-reference",
                axis, primary.BlockOwnerDeletion ? "metadata.ownerReferences[].blockOwnerDeletion" : "metadata.ownerReferences",
                knownControllerPair));
            foreach (var owner in owners.Where(reference => reference.Uid != primary.Uid))
                edges.Add(new KubernetesGraphEdge(owner.Uid, item.Uid, "owner-reference", "property",
                    "metadata.ownerReferences", false));
        }

        foreach (var service in byUid.Values.Where(item => item.Kind == "Service" && item.Selector.Count > 0))
        foreach (var pod in byUid.Values.Where(item => item.Kind == "Pod" && item.Namespace == service.Namespace
                     && service.Selector.All(label => item.Labels.TryGetValue(label.Key, out var value) && value == label.Value)))
            edges.Add(new KubernetesGraphEdge(service.Uid, pod.Uid, "service-selector-pod", "selection",
                "Service.spec.selector ↔ Pod.metadata.labels", false));

        foreach (var source in byUid.Values)
        foreach (var reference in source.References)
        {
            if (reference.Relationship == "crd-defines-resource")
            {
                foreach (var target in byUid.Values.Where(item => item.Kind == reference.Name
                             && item.GroupVersion.StartsWith($"{reference.ApiGroup}/", StringComparison.Ordinal)))
                    edges.Add(new KubernetesGraphEdge(source.Uid, target.Uid, reference.Relationship, "reference",
                        "CustomResourceDefinition.spec.names.kind", false));
                continue;
            }
            if (reference.Relationship is "network-policy-selector" or "pdb-selector" or "workload-selector")
            {
                foreach (var pod in byUid.Values.Where(item => item.Kind == "Pod"
                             && item.Namespace == (reference.Namespace ?? string.Empty)
                             && SelectorMatches(item.Labels, reference.Selector, reference.SelectorRequirements)))
                    edges.Add(new KubernetesGraphEdge(source.Uid, pod.Uid, reference.Relationship, "selection",
                        FieldPathFor(reference.Relationship), false));
                continue;
            }
            var targets = reference.TargetNameIsPrefix
                ? byUid.Values.Where(item => item.Kind == reference.Kind && item.Namespace == (reference.Namespace ?? string.Empty)
                    && item.Name.StartsWith(reference.Name, StringComparison.Ordinal)).ToArray()
                : byName.TryGetValue((reference.Kind, reference.Name, reference.Namespace ?? string.Empty), out var exactTarget)
                    ? [exactTarget] : [];
            foreach (var targetItem in targets)
                edges.Add(reference.TargetIsParent
                    ? new KubernetesGraphEdge(targetItem.Uid, source.Uid, reference.Relationship,
                        AxisFor(reference.Relationship), FieldPathFor(reference.Relationship), reference.TargetNameIsPrefix)
                    : new KubernetesGraphEdge(source.Uid, targetItem.Uid, reference.Relationship,
                        AxisFor(reference.Relationship), FieldPathFor(reference.Relationship), reference.TargetNameIsPrefix));
        }

        foreach (var labelGroup in byUid.Values
                     .SelectMany(resource => resource.Labels
                         .Where(pair => relatedLabelKeys.Contains(pair.Key, StringComparer.Ordinal)
                             && !string.IsNullOrWhiteSpace(pair.Value))
                         .Select(pair => (Resource: resource, pair.Key, pair.Value)))
                     .GroupBy(entry => (entry.Key, entry.Value)))
        {
            var members = labelGroup.Select(entry => entry.Resource)
                .DistinctBy(resource => resource.Uid)
                .OrderBy(resource => resource.Kind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (members.Length < 2) continue;
            var anchor = members[0];
            foreach (var member in members.Skip(1))
                edges.Add(new KubernetesGraphEdge(anchor.Uid, member.Uid, "shared-label", "label-association",
                    $"metadata.labels[{labelGroup.Key.Key}]", true, labelGroup.Key.Key, labelGroup.Key.Value));
        }

        return new KubernetesResourceGraphRelations(edges.Distinct().ToArray(), ownerByUid);
    }

    private static readonly HashSet<(string Parent, string Child)> ControllerPairs =
    [
        ("Deployment", "ReplicaSet"), ("ReplicaSet", "Pod"), ("StatefulSet", "Pod"),
        ("DaemonSet", "Pod"), ("Job", "Pod"), ("CronJob", "Job")
    ];

    private static bool IsControllerPair(string parent, string child) => ControllerPairs.Contains((parent, child));

    private static readonly IReadOnlyDictionary<string, string> RelationAxes = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["hpa-target"] = "control+reference", ["vpa-target"] = "control+reference",
        ["statefulset-claim-template"] = "control", ["service-endpoint-slice"] = "property"
    };

    private static string AxisFor(string relation) => RelationAxes.TryGetValue(relation, out var axis) ? axis : "reference";

    private static readonly IReadOnlyDictionary<string, string> RelationFieldPaths = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["hpa-target"] = "spec.scaleTargetRef",
        ["vpa-target"] = "spec.targetRef",
        ["service-endpoint-slice"] = "metadata.labels[kubernetes.io/service-name]",
        ["endpoint-target"] = "endpoints[].targetRef",
        ["ingress-backend"] = "spec.rules[].http.paths[].backend.service.name | spec.defaultBackend.service.name",
        ["ingress-class"] = "spec.ingressClassName",
        ["ingressroute-middleware"] = "spec.routes[].middlewares[].name",
        ["ingressroute-service"] = "spec.routes[].services[].name",
        ["middleware-secret"] = "spec.basicAuth.secret",
        ["middleware-chain"] = "spec.chain.middlewares[].name",
        ["pod-volume-claim"] = "spec.volumes[].persistentVolumeClaim.claimName",
        ["pod-config-map"] = "spec.volumes[].configMap.name | spec.containers[].envFrom[].configMapRef.name | spec.containers[].env[].valueFrom.configMapKeyRef.name",
        ["pod-secret"] = "spec.volumes[].secret.secretName | spec.containers[].envFrom[].secretRef.name | spec.containers[].env[].valueFrom.secretKeyRef.name | spec.imagePullSecrets[].name",
        ["pod-image-pull-secret"] = "spec.volumes[].secret.secretName | spec.containers[].envFrom[].secretRef.name | spec.containers[].env[].valueFrom.secretKeyRef.name | spec.imagePullSecrets[].name",
        ["pod-service-account"] = "spec.serviceAccountName",
        ["claim-volume"] = "spec.volumeName",
        ["statefulset-service"] = "spec.serviceName",
        ["statefulset-claim-template"] = "spec.volumeClaimTemplates[].metadata.name (claim naming convention)",
        ["claim-storage-class"] = "spec.storageClassName",
        ["volume-storage-class"] = "spec.storageClassName",
        ["role-binding-target"] = "roleRef",
        ["role-binding-subject"] = "subjects[]",
        ["network-policy-selector"] = "spec.podSelector",
        ["pdb-selector"] = "spec.selector",
        ["workload-selector"] = "spec.selector",
        ["route-parent"] = "spec.parentRefs[]",
        ["route-backend"] = "spec.rules[].backendRefs[]",
        ["pod-node"] = "spec.nodeName",
        ["volume-attachment-node"] = "spec.nodeName",
        ["pod-runtime-class"] = "spec.runtimeClassName",
        ["pod-priority-class"] = "spec.priorityClassName",
        ["volume-attachment-pv"] = "spec.source.persistentVolumeName",
        ["gateway-class"] = "spec.gatewayClassName",
        ["storage-class-driver"] = "provisioner",
        ["event-involved-object"] = "involvedObject",
        ["crd-defines-resource"] = "spec.names.kind"
    };

    private static string FieldPathFor(string relation) => RelationFieldPaths.TryGetValue(relation, out var fieldPath) ? fieldPath : string.Empty;

    private static bool SelectorMatches(IReadOnlyDictionary<string, string> labels,
        IReadOnlyDictionary<string, string>? selector, IReadOnlyList<KubernetesLabelSelectorRequirement>? requirements)
    {
        if (selector is not null && selector.Any(pair => !labels.TryGetValue(pair.Key, out var value) || value != pair.Value)) return false;
        if (requirements is null) return true;
        return requirements.All(requirement =>
        {
            var exists = labels.TryGetValue(requirement.Key, out var value);
            return requirement.Operator switch
            {
                "In" => exists && requirement.Values.Contains(value!, StringComparer.Ordinal),
                "NotIn" => !exists || !requirement.Values.Contains(value!, StringComparer.Ordinal),
                "Exists" => exists,
                "DoesNotExist" => !exists,
                _ => false
            };
        });
    }
}
