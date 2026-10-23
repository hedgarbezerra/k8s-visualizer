using System.Net;
using System.Text;
using System.Text.Json;
using KubernetesVisualizer.Models.UI;
using KubernetesVisualizer;
using k8s;
using k8s.KubeConfigModels;
using k8s.Models;
using Microsoft.Extensions.Localization;

namespace KubernetesVisualizer.Services;

public sealed class KubernetesClusterService : IDisposable
{
    private readonly ILogger<KubernetesClusterService> _logger;
    private readonly KubernetesConnectionProfileStore _profileStore;
    private readonly IStringLocalizer<AppStrings> _localizer;
    private readonly Dictionary<string, ConnectedCluster> _connected = new(StringComparer.Ordinal);
    private K8SConfiguration? _draftKubeConfig;
    private string? _draftPath;
    private bool _profilesLoaded;
    private bool _restoreAttempted;
    private KubernetesLastSelection _lastSelection = new(null, null);
    private long _selectionRevision;

    public KubernetesClusterService(ILogger<KubernetesClusterService> logger, KubernetesConnectionProfileStore profileStore, IStringLocalizer<AppStrings> localizer)
    {
        _logger = logger;
        _profileStore = profileStore;
        _localizer = localizer;
    }

    public event Action? Changed;
    public event Action<KubernetesSelectionChangedEventArgs>? SelectionChanged;
    public IReadOnlyList<KubernetesConnectionProfile> Profiles { get; private set; } = [];
    public IReadOnlyList<KubernetesConnectionProfile> ConnectedProfiles => Profiles.Where(profile => _connected.ContainsKey(profile.Id)).ToArray();
    public IReadOnlyList<KubernetesContextOption> AvailableContexts { get; private set; } = [];
    public IReadOnlyList<string> Namespaces => Active?.Namespaces ?? [];
    public string? ActiveConnectionId { get; private set; }
    public string? CurrentContextName => Active?.Profile.ContextName;
    public string? CurrentNamespace => Active?.Namespace;
    public string? Server => Active?.Server;
    public string? CurrentConnectionName => Active?.Profile.Name;
    public bool IsConnected => Active is not null;
    public bool IsLoading { get; private set; }
    public string? Error { get; private set; }
    public string KubeconfigPath { get; private set; } = string.Empty;

    private ConnectedCluster? Active => ActiveConnectionId is not null && _connected.TryGetValue(ActiveConnectionId, out var state) ? state : null;
    public bool IsProfileConnected(string profileId) => _connected.ContainsKey(profileId);

    public async Task EnsureProfilesLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (_profilesLoaded) return;
        Profiles = await _profileStore.LoadAsync(cancellationToken);
        _lastSelection = await _profileStore.LoadLastSelectionAsync(cancellationToken);
        _profilesLoaded = true;
        NotifyChanged();
    }

    public async Task RestoreLastSelectionAsync(CancellationToken cancellationToken = default)
    {
        await EnsureProfilesLoadedAsync(cancellationToken);
        if (_restoreAttempted) return;
        _restoreAttempted = true;

        var selection = _lastSelection;
        if (selection.ConnectionId is null || Profiles.All(profile => profile.Id != selection.ConnectionId)) return;

        await ConnectAsync(selection.ConnectionId, cancellationToken);
        if (!IsProfileConnected(selection.ConnectionId)) return;

        var state = _connected[selection.ConnectionId];
        var savedNamespace = selection.Namespace;
        if (savedNamespace is not null && state.Namespaces.Count > 0 && !state.Namespaces.Contains(savedNamespace, StringComparer.Ordinal))
            savedNamespace = null;

        state.Namespace = savedNamespace;
        await _profileStore.SaveLastSelectionAsync(selection.ConnectionId, savedNamespace, cancellationToken);
        _lastSelection = new KubernetesLastSelection(selection.ConnectionId, savedNamespace);
        NotifyChanged();
        NotifySelectionChanged(KubernetesSelectionChangeKind.Namespace);
    }

    public async Task LoadContextsFromPathAsync(string? path, CancellationToken cancellationToken = default)
    {
        Error = null;
        IsLoading = true;
        NotifyChanged();
        try
        {
            _draftPath = ResolvePath(path);
            KubeconfigPath = _draftPath;
            var file = new FileInfo(_draftPath);
            if (!file.Exists) throw new FileNotFoundException("O arquivo kubeconfig não foi encontrado.", file.FullName);

            _draftKubeConfig = await KubernetesClientConfiguration.LoadKubeConfigAsync(file, useRelativePaths: true);
            var clusters = _draftKubeConfig.Clusters.ToDictionary(cluster => cluster.Name, StringComparer.Ordinal);
            AvailableContexts = _draftKubeConfig.Contexts
                .Where(context => context.ContextDetails is not null)
                .Select(context =>
                {
                    clusters.TryGetValue(context.ContextDetails.Cluster, out var cluster);
                    return new KubernetesContextOption(
                        context.Name,
                        context.ContextDetails.Cluster,
                        cluster?.ClusterEndpoint?.Server ?? "Endpoint indisponível",
                        context.ContextDetails.Namespace,
                        string.Equals(context.Name, _draftKubeConfig.CurrentContext, StringComparison.Ordinal));
                })
                .OrderByDescending(context => context.IsCurrent)
                .ThenBy(context => context.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception)
        {
            _logger.LogWarning("Não foi possível carregar kubeconfig: {Message}", exception.Message);
            AvailableContexts = [];
            _draftKubeConfig = null;
            Error = _localizer["CouldNotLoadKubeconfig", exception.Message];
        }
        finally
        {
            IsLoading = false;
            NotifyChanged();
        }
    }

    public async Task AddProfileAsync(string name, string kubeconfigPath, string contextName, CancellationToken cancellationToken = default)
    {
        await EnsureProfilesLoadedAsync(cancellationToken);
        var profile = new KubernetesConnectionProfile(Guid.NewGuid().ToString("N"), name.Trim(), ResolvePath(kubeconfigPath), contextName);
        Profiles = [.. Profiles, profile];
        await _profileStore.SaveAsync(Profiles, cancellationToken);
        NotifyChanged();
    }

    public async Task RemoveProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(profileId, cancellationToken);
        Profiles = Profiles.Where(profile => profile.Id != profileId).ToArray();
        await _profileStore.SaveAsync(Profiles, cancellationToken);
        if (_lastSelection.ConnectionId == profileId)
        {
            _lastSelection = new KubernetesLastSelection(null, null);
            await _profileStore.SaveLastSelectionAsync(null, null, cancellationToken);
        }
        NotifyChanged();
    }

    public async Task ConnectAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await EnsureProfilesLoadedAsync(cancellationToken);
        string? connectionNotice = null;
        if (!_connected.TryGetValue(profileId, out var state))
        {
            var profile = Profiles.FirstOrDefault(item => item.Id == profileId)
                ?? throw new InvalidOperationException(_localizer["MissingConfiguredConnection"]);

            IsLoading = true;
            Error = null;
            NotifyChanged();
            Kubernetes? candidateClient = null;
            try
            {
                var kubeConfig = await KubernetesClientConfiguration.LoadKubeConfigAsync(new FileInfo(profile.KubeconfigPath), useRelativePaths: true);
                var context = kubeConfig.Contexts.FirstOrDefault(item => item.Name == profile.ContextName)
                    ?? throw new InvalidOperationException(_localizer["ContextNotFound", profile.ContextName]);
                var cluster = kubeConfig.Clusters.FirstOrDefault(item => item.Name == context.ContextDetails?.Cluster);
                candidateClient = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigObject(kubeConfig, profile.ContextName));
                var newState = new ConnectedCluster(profile, candidateClient, cluster?.ClusterEndpoint?.Server ?? "Endpoint indisponível", context.ContextDetails?.Namespace);

                try
                {
                    var namespaces = await candidateClient.CoreV1.ListNamespaceAsync(cancellationToken: cancellationToken);
                    newState.Namespaces = namespaces.Items.Select(item => item.Metadata?.Name)
                        .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>()
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
                    if (newState.Namespace is not null && !newState.Namespaces.Contains(newState.Namespace)) newState.Namespace = null;
                }
                catch (KubernetesException exception) when (exception.Status?.Code == (int)HttpStatusCode.Forbidden)
                {
                    connectionNotice = _localizer["ConnectedWithoutNamespacePermission"];
                }
                catch (k8s.Autorest.HttpOperationException exception) when (exception.Response?.StatusCode == HttpStatusCode.Forbidden)
                {
                    connectionNotice = _localizer["ConnectedWithoutNamespacePermission"];
                }

                _connected[profileId] = newState;
                state = newState;
                candidateClient = null;
            }
            catch (Exception exception)
            {
                candidateClient?.Dispose();
                _logger.LogWarning("Falha ao conectar ao perfil {Profile}: {Message}", profileId, exception.Message);
                Error = _localizer["ConnectionFailed", exception.Message];
                IsLoading = false;
                NotifyChanged();
                return;
            }
            finally
            {
                IsLoading = false;
            }
        }

        ActiveConnectionId = profileId;
        KubeconfigPath = state.Profile.KubeconfigPath;
        Error = connectionNotice;
        _lastSelection = new KubernetesLastSelection(profileId, state.Namespace);
        await _profileStore.SaveLastSelectionAsync(profileId, state.Namespace, cancellationToken);
        NotifyChanged();
        NotifySelectionChanged(KubernetesSelectionChangeKind.Connection);
    }

    public async Task SelectConnectionAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        if (profileId is null || !_connected.ContainsKey(profileId)) return;
        if (ActiveConnectionId == profileId) return;
        ActiveConnectionId = profileId;
        Error = null;
        _lastSelection = new KubernetesLastSelection(profileId, Active?.Namespace);
        await _profileStore.SaveLastSelectionAsync(profileId, Active?.Namespace, cancellationToken);
        NotifyChanged();
        NotifySelectionChanged(KubernetesSelectionChangeKind.Cluster);
    }

    public async Task SelectNamespaceAsync(string? namespaceName, CancellationToken cancellationToken = default)
    {
        if (Active is null) return;
        var selectedNamespace = string.IsNullOrWhiteSpace(namespaceName) ? null : namespaceName;
        if (Active.Namespace == selectedNamespace) return;
        Active.Namespace = selectedNamespace;
        _lastSelection = new KubernetesLastSelection(ActiveConnectionId, selectedNamespace);
        await _profileStore.SaveLastSelectionAsync(ActiveConnectionId, selectedNamespace, cancellationToken);
        NotifyChanged();
        NotifySelectionChanged(KubernetesSelectionChangeKind.Namespace);
    }

    public async Task DisconnectAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var activeWasRemoved = ActiveConnectionId == profileId;
        if (_connected.Remove(profileId, out var state)) state.Client.Dispose();
        if (activeWasRemoved) ActiveConnectionId = _connected.Keys.FirstOrDefault();
        if (activeWasRemoved)
        {
            _lastSelection = Active is null ? new KubernetesLastSelection(null, null) : new KubernetesLastSelection(ActiveConnectionId, Active.Namespace);
            await _profileStore.SaveLastSelectionAsync(_lastSelection.ConnectionId, _lastSelection.Namespace, cancellationToken);
        }
        NotifyChanged();
        if (activeWasRemoved) NotifySelectionChanged(KubernetesSelectionChangeKind.Cluster);
    }

    public async Task<IReadOnlyList<KubernetesResourceType>> GetResourceTypesAsync(CancellationToken cancellationToken = default)
    {
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var paths = new List<string>();
        using (var coreResponse = await client.HttpClient.GetAsync(ApiUri(client, "/api"), cancellationToken))
        {
            coreResponse.EnsureSuccessStatusCode();
            using var coreJson = JsonDocument.Parse(await coreResponse.Content.ReadAsStreamAsync(cancellationToken));
            foreach (var version in coreJson.RootElement.GetProperty("versions").EnumerateArray())
                paths.Add("/api/" + version.GetString());
        }
        using (var groupsResponse = await client.HttpClient.GetAsync(ApiUri(client, "/apis"), cancellationToken))
        {
            groupsResponse.EnsureSuccessStatusCode();
            using var groupsJson = JsonDocument.Parse(await groupsResponse.Content.ReadAsStreamAsync(cancellationToken));
            foreach (var group in groupsJson.RootElement.GetProperty("groups").EnumerateArray())
                foreach (var version in group.GetProperty("versions").EnumerateArray())
                    paths.Add("/apis/" + version.GetProperty("groupVersion").GetString());
        }

        var results = new List<KubernetesResourceType>();
        foreach (var path in paths)
        {
            using var response = await client.HttpClient.GetAsync(ApiUri(client, path), cancellationToken);
            if (!response.IsSuccessStatusCode) continue;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var groupVersion = json.RootElement.GetProperty("groupVersion").GetString() ?? "v1";
            foreach (var resource in json.RootElement.GetProperty("resources").EnumerateArray())
            {
                var name = resource.GetProperty("name").GetString() ?? string.Empty;
                if (name.Contains('/') || !resource.GetProperty("verbs").EnumerateArray().Any(verb => verb.GetString() == "list")) continue;
                results.Add(new KubernetesResourceType(name, resource.GetProperty("kind").GetString() ?? name, groupVersion,
                    resource.GetProperty("namespaced").GetBoolean(), path));
            }
        }
        return results.OrderBy(item => item.Kind, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<KubernetesResourceItem>> ListResourcesAsync(KubernetesResourceType type, string? namespaceName, CancellationToken cancellationToken = default)
    {
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var route = type.ApiPath;
        if (type.Namespaced && !string.IsNullOrWhiteSpace(namespaceName))
            route += $"/namespaces/{Uri.EscapeDataString(namespaceName)}";
        route += "/" + type.Name + "?limit=500";

        var items = new List<KubernetesResourceItem>();
        string? continuation = null;
        do
        {
            var pageRoute = continuation is null ? route : route + "&continue=" + Uri.EscapeDataString(continuation);
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUri(client, pageRoute));
            if (type.Kind == "Secret")
                request.Headers.TryAddWithoutValidation("Accept", "application/json;as=PartialObjectMetadataList;g=meta.k8s.io;v=v1");
            using var response = await client.HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var metadata = item.GetProperty("metadata");
                var objectNamespace = metadata.TryGetProperty("namespace", out var ns) ? ns.GetString() ?? string.Empty : string.Empty;
                var status = item.TryGetProperty("status", out var statusElement) ? SummarizeStatus(type.Kind, statusElement) : string.Empty;
                var labels = ReadMetadataMap(metadata, "labels");
                var annotations = type.Kind == "Secret" ? new Dictionary<string, string>(StringComparer.Ordinal) : ReadMetadataMap(metadata, "annotations");
                items.Add(new KubernetesResourceItem(type.Kind,
                    metadata.TryGetProperty("name", out var objectName) ? objectName.GetString() ?? "(sem nome)" : "(sem nome)",
                    objectNamespace, status, type.GroupVersion, type.Name,
                    IsKubernetesSystemManaged(metadata, objectNamespace), HasOwnerReferences(metadata), labels, annotations));
            }
            continuation = json.RootElement.GetProperty("metadata").TryGetProperty("continue", out var token) ? token.GetString() : null;
        } while (!string.IsNullOrWhiteSpace(continuation));
        return items;
    }

    public async Task<IReadOnlyList<KubernetesResourceGraphItem>> ListResourceGraphItemsAsync(
        KubernetesResourceType type, string? namespaceName, CancellationToken cancellationToken = default)
    {
        if (type.Namespaced && string.IsNullOrWhiteSpace(namespaceName)) return [];

        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var scope = type.Namespaced ? $"/namespaces/{Uri.EscapeDataString(namespaceName!)}" : string.Empty;
        var route = $"{type.ApiPath}{scope}/{type.Name}?limit=500";
        var items = new List<KubernetesResourceGraphItem>();
        string? continuation = null;
        do
        {
            var pageRoute = continuation is null ? route : route + "&continue=" + Uri.EscapeDataString(continuation);
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUri(client, pageRoute));
            if (type.Kind == "Secret")
                request.Headers.TryAddWithoutValidation("Accept", "application/json;as=PartialObjectMetadataList;g=meta.k8s.io;v=v1");

            using var response = await client.HttpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var metadata = item.GetProperty("metadata");
                var owners = metadata.TryGetProperty("ownerReferences", out var ownerReferences)
                    ? ownerReferences.EnumerateArray()
                        .Select(owner => new KubernetesResourceOwnerReference(
                            owner.TryGetProperty("kind", out var kind) ? kind.GetString() ?? string.Empty : string.Empty,
                            owner.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                            owner.TryGetProperty("uid", out var uid) ? uid.GetString() ?? string.Empty : string.Empty,
                            owner.TryGetProperty("controller", out var controller) && controller.ValueKind == JsonValueKind.True))
                        .ToArray()
                    : [];
                var labels = metadata.TryGetProperty("labels", out var labelObject) && labelObject.ValueKind == JsonValueKind.Object
                    ? labelObject.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                var annotations = type.Kind != "Secret" && metadata.TryGetProperty("annotations", out var annotationObject) && annotationObject.ValueKind == JsonValueKind.Object
                    ? annotationObject.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                    : new Dictionary<string, string>(StringComparer.Ordinal);
                var selector = type.Kind == "Service"
                    && item.TryGetProperty("spec", out var serviceSpec)
                    && serviceSpec.TryGetProperty("selector", out var selectorObject)
                    && selectorObject.ValueKind == JsonValueKind.Object
                        ? selectorObject.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
                        : new Dictionary<string, string>(StringComparer.Ordinal);
                var status = item.TryGetProperty("status", out var statusObject)
                    ? SummarizeStatus(type.Kind, statusObject)
                    : string.Empty;
                var name = metadata.TryGetProperty("name", out var objectName) ? objectName.GetString() ?? "(sem nome)" : "(sem nome)";
                var objectNamespace = metadata.TryGetProperty("namespace", out var objectNamespaceElement)
                    ? objectNamespaceElement.GetString() ?? string.Empty
                    : string.Empty;
                var references = ExtractGraphReferences(type.Kind, item, objectNamespace);
                var compositions = type.Kind == "Pod" ? ExtractPodComposition(item) : [];
                var isSystemManaged = IsKubernetesSystemManaged(metadata, objectNamespace);
                var isControllerManaged = HasOwnerReferences(metadata);

                items.Add(new KubernetesResourceGraphItem(
                    type.Kind,
                    name,
                    objectNamespace,
                    metadata.TryGetProperty("uid", out var objectUid) ? objectUid.GetString() ?? string.Empty : string.Empty,
                    type.GroupVersion,
                    type.Name,
                    status,
                    owners,
                    labels,
                    annotations,
                    selector,
                    references,
                    compositions,
                    isSystemManaged,
                    isControllerManaged));
            }
            continuation = json.RootElement.GetProperty("metadata").TryGetProperty("continue", out var token) ? token.GetString() : null;
        } while (!string.IsNullOrWhiteSpace(continuation));

        return items;
    }

    public async Task<string> GetResourceYamlAsync(string groupVersion, string resourceName, string kind,
        string name, string? namespaceName, CancellationToken cancellationToken = default)
    {
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var apiRoot = groupVersion.Contains('/') ? $"/apis/{groupVersion}" : $"/api/{groupVersion}";
        var namespacePath = string.IsNullOrWhiteSpace(namespaceName) ? string.Empty : $"/namespaces/{Uri.EscapeDataString(namespaceName)}";
        var route = $"{apiRoot}{namespacePath}/{resourceName}/{Uri.EscapeDataString(name)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUri(client, route));
        if (kind == "Secret")
            request.Headers.TryAddWithoutValidation("Accept", "application/json;as=PartialObjectMetadata;g=meta.k8s.io;v=v1");
        using var response = await client.HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var builder = new StringBuilder();
        WriteYaml(document.RootElement, builder, 0, redactSecretData: kind == "Secret");
        return builder.ToString();
    }

    private static bool HasOwnerReferences(JsonElement metadata) =>
        metadata.TryGetProperty("ownerReferences", out var owners)
        && owners.ValueKind == JsonValueKind.Array
        && owners.GetArrayLength() > 0;

    private static IReadOnlyDictionary<string, string> ReadMetadataMap(JsonElement metadata, string propertyName) =>
        metadata.TryGetProperty(propertyName, out var map) && map.ValueKind == JsonValueKind.Object
            ? map.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static bool IsKubernetesSystemManaged(JsonElement metadata, string namespaceName)
    {
        if (namespaceName is "kube-system" or "kube-public" or "kube-node-lease") return true;
        if (metadata.TryGetProperty("name", out var nameElement)
            && nameElement.GetString() is "kube-system" or "kube-public" or "kube-node-lease") return true;
        if (metadata.TryGetProperty("annotations", out var annotations) && annotations.ValueKind == JsonValueKind.Object
            && annotations.TryGetProperty("kubernetes.io/created-by", out _)) return true;
        if (!metadata.TryGetProperty("managedFields", out var managedFields) || managedFields.ValueKind != JsonValueKind.Array) return false;
        return managedFields.EnumerateArray().Any(field =>
        {
            if (!field.TryGetProperty("manager", out var managerElement)) return false;
            var manager = managerElement.GetString() ?? string.Empty;
            return manager.StartsWith("kube-controller-manager", StringComparison.OrdinalIgnoreCase)
                   || manager.StartsWith("kube-scheduler", StringComparison.OrdinalIgnoreCase)
                   || manager.StartsWith("kubelet", StringComparison.OrdinalIgnoreCase)
                   || manager.StartsWith("kube-proxy", StringComparison.OrdinalIgnoreCase)
                   || manager.StartsWith("endpointslice-controller", StringComparison.OrdinalIgnoreCase)
                   || manager.StartsWith("namespace-controller", StringComparison.OrdinalIgnoreCase)
                   || manager.StartsWith("service-controller", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void WriteYaml(JsonElement value, StringBuilder output, int indent, bool redactSecretData = false)
    {
        var padding = new string(' ', indent);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = value.EnumerateObject()
                    .Where(property => !redactSecretData || property.Name is not ("data" or "stringData")).ToArray();
                if (properties.Length == 0) { output.Append(padding).Append("{}\n"); return; }
                foreach (var property in properties)
                {
                    output.Append(padding).Append(JsonSerializer.Serialize(property.Name)).Append(':');
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        if ((property.Value.ValueKind == JsonValueKind.Object && !property.Value.EnumerateObject().Any())
                            || (property.Value.ValueKind == JsonValueKind.Array && !property.Value.EnumerateArray().Any()))
                        {
                            output.Append(property.Value.ValueKind == JsonValueKind.Object ? " {}\n" : " []\n");
                        }
                        else
                        {
                            output.Append('\n');
                            WriteYaml(property.Value, output, indent + 2);
                        }
                    }
                    else
                    {
                        output.Append(' ');
                        WriteYamlScalar(property.Value, output);
                        output.Append('\n');
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var element in value.EnumerateArray())
                {
                    output.Append(padding).Append('-');
                    if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        if ((element.ValueKind == JsonValueKind.Object && !element.EnumerateObject().Any())
                            || (element.ValueKind == JsonValueKind.Array && !element.EnumerateArray().Any()))
                        {
                            output.Append(element.ValueKind == JsonValueKind.Object ? " {}\n" : " []\n");
                        }
                        else
                        {
                            output.Append('\n');
                            WriteYaml(element, output, indent + 2);
                        }
                    }
                    else
                    {
                        output.Append(' ');
                        WriteYamlScalar(element, output);
                        output.Append('\n');
                    }
                }
                break;
            default:
                output.Append(padding);
                WriteYamlScalar(value, output);
                output.Append('\n');
                break;
        }
    }

    private static void WriteYamlScalar(JsonElement value, StringBuilder output)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String: output.Append(JsonSerializer.Serialize(value.GetString())); break;
            case JsonValueKind.Number: output.Append(value.GetRawText()); break;
            case JsonValueKind.True: output.Append("true"); break;
            case JsonValueKind.False: output.Append("false"); break;
            case JsonValueKind.Null: output.Append("null"); break;
            default: output.Append(JsonSerializer.Serialize(value.GetRawText())); break;
        }
    }

    private static IReadOnlyList<KubernetesResourceGraphReference> ExtractGraphReferences(
        string kind, JsonElement item, string objectNamespace)
    {
        var references = new List<KubernetesResourceGraphReference>();
        void Add(string? targetKind, string? targetName, string? targetNamespace, string relation, bool targetIsParent)
        {
            if (!string.IsNullOrWhiteSpace(targetKind) && !string.IsNullOrWhiteSpace(targetName))
                references.Add(new KubernetesResourceGraphReference(targetKind, targetName,
                    targetNamespace ?? (targetKind is "Node" or "PersistentVolume" or "StorageClass" or "ClusterRole"
                        or "IngressClass" or "GatewayClass" or "RuntimeClass" or "PriorityClass" or "CustomResourceDefinition"
                        or "ClusterRoleBinding" or "APIService" ? string.Empty : objectNamespace),
                    relation, targetIsParent));
        }

        bool TryPath(JsonElement root, out JsonElement value, params string[] path)
        {
            value = root;
            foreach (var segment in path)
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value)) return false;
            return true;
        }

        string? StringAt(JsonElement root, params string[] path) =>
            TryPath(root, out var value, path) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        switch (kind)
        {
            case "Pod":
                Add("Node", StringAt(item, "spec", "nodeName"), null, "pod-node", false);
                Add("RuntimeClass", StringAt(item, "spec", "runtimeClassName"), null, "pod-runtime-class", false);
                Add("PriorityClass", StringAt(item, "spec", "priorityClassName"), null, "pod-priority-class", false);
                Add("ServiceAccount", StringAt(item, "spec", "serviceAccountName"), objectNamespace, "pod-service-account", false);
                AddPodReferences(item, objectNamespace, Add);
                break;
            case "EndpointSlice":
                Add("Service", StringAt(item, "metadata", "labels", "kubernetes.io/service-name"), objectNamespace, "service-endpoint-slice", true);
                if (TryPath(item, out var endpoints, "endpoints") && endpoints.ValueKind == JsonValueKind.Array)
                    foreach (var endpoint in endpoints.EnumerateArray())
                        Add(StringAt(endpoint, "targetRef", "kind"), StringAt(endpoint, "targetRef", "name"),
                            StringAt(endpoint, "targetRef", "namespace") ?? objectNamespace, "endpoint-target", false);
                break;
            case "Ingress":
                Add("IngressClass", StringAt(item, "spec", "ingressClassName"), null, "ingress-class", false);
                if (TryPath(item, out var rules, "spec", "rules") && rules.ValueKind == JsonValueKind.Array)
                    foreach (var rule in rules.EnumerateArray())
                        if (TryPath(rule, out var paths, "http", "paths") && paths.ValueKind == JsonValueKind.Array)
                            foreach (var path in paths.EnumerateArray())
                                Add("Service", StringAt(path, "backend", "service", "name"), objectNamespace, "ingress-backend", false);
                Add("Service", StringAt(item, "spec", "defaultBackend", "service", "name"), objectNamespace, "ingress-backend", false);
                break;
            case "HorizontalPodAutoscaler":
                Add(StringAt(item, "spec", "scaleTargetRef", "kind"), StringAt(item, "spec", "scaleTargetRef", "name"), objectNamespace, "hpa-target", false);
                break;
            case "VerticalPodAutoscaler":
                Add(StringAt(item, "spec", "targetRef", "kind"), StringAt(item, "spec", "targetRef", "name"), objectNamespace, "vpa-target", false);
                break;
            case "PersistentVolumeClaim":
                Add("PersistentVolume", StringAt(item, "spec", "volumeName"), null, "claim-volume", false);
                Add("StorageClass", StringAt(item, "spec", "storageClassName"), null, "claim-storage-class", false);
                break;
            case "RoleBinding":
                var roleKind = StringAt(item, "roleRef", "kind");
                Add(roleKind, StringAt(item, "roleRef", "name"), roleKind == "Role" ? objectNamespace : null, "role-binding-target", false);
                AddSubjects(item, objectNamespace, Add);
                break;
            case "ClusterRoleBinding":
                Add("ClusterRole", StringAt(item, "roleRef", "name"), null, "role-binding-target", false);
                AddSubjects(item, objectNamespace, Add);
                break;
            case "NetworkPolicy":
                AddSelectorEdges(item, "spec", "podSelector", "network-policy-selector", objectNamespace, references);
                break;
            case "PodDisruptionBudget":
                AddSelectorEdges(item, "spec", "selector", "pdb-selector", objectNamespace, references);
                break;
            case "Gateway":
                Add("GatewayClass", StringAt(item, "spec", "gatewayClassName"), null, "gateway-class", false);
                break;
            case "HTTPRoute":
            case "GRPCRoute":
            case "TCPRoute":
            case "TLSRoute":
            case "UDPRoute":
                AddRouteReferences(item, objectNamespace, Add);
                break;
            case "PersistentVolume":
                Add("StorageClass", StringAt(item, "spec", "storageClassName"), null, "volume-storage-class", false);
                break;
            case "VolumeAttachment":
                Add("Node", StringAt(item, "spec", "nodeName"), null, "volume-attachment-node", false);
                Add("PersistentVolume", StringAt(item, "spec", "source", "persistentVolumeName"), null, "volume-attachment-pv", false);
                break;
            case "StorageClass":
                Add("CSIDriver", StringAt(item, "provisioner"), null, "storage-class-driver", false);
                break;
            case "CustomResourceDefinition":
                var customKind = StringAt(item, "spec", "names", "kind");
                var customGroup = StringAt(item, "spec", "group");
                if (!string.IsNullOrWhiteSpace(customKind) && !string.IsNullOrWhiteSpace(customGroup))
                    references.Add(new KubernetesResourceGraphReference("CustomResource", customKind, null,
                        "crd-defines-resource", false, ApiGroup: customGroup));
                break;
            case "Event":
                Add(StringAt(item, "involvedObject", "kind"), StringAt(item, "involvedObject", "name"),
                    StringAt(item, "involvedObject", "namespace"), "event-involved-object", false);
                break;
        }

        return references;
    }

    private static IReadOnlyList<KubernetesResourceGraphComposition> ExtractPodComposition(JsonElement pod)
    {
        var items = new List<KubernetesResourceGraphComposition>();
        var statusByName = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (pod.TryGetProperty("status", out var status)
            && status.ValueKind == JsonValueKind.Object)
            foreach (var statusField in new[] { "containerStatuses", "initContainerStatuses", "ephemeralContainerStatuses" })
                if (status.TryGetProperty(statusField, out var statuses) && statuses.ValueKind == JsonValueKind.Array)
                    foreach (var containerStatus in statuses.EnumerateArray())
                        if (containerStatus.TryGetProperty("name", out var name) && name.GetString() is { } value)
                            statusByName[value] = containerStatus;

        AddContainers("containers", "Container");
        AddContainers("initContainers", "InitContainer");
        AddContainers("ephemeralContainers", "EphemeralContainer");

        if (TryPropertyPath(pod, out var volumes, "spec", "volumes") && volumes.ValueKind == JsonValueKind.Array)
            foreach (var volume in volumes.EnumerateArray())
            {
                var name = ReadString(volume, "name") ?? "(unnamed volume)";
                var source = volume.EnumerateObject().FirstOrDefault(property => property.Name is not "name");
                var details = source.Name switch
                {
                    "persistentVolumeClaim" => $"PVC: {ReadString(source.Value, "claimName")}",
                    "configMap" => $"ConfigMap: {ReadString(source.Value, "name")}",
                    "secret" => "Secret volume",
                    "projected" => "Projected volume",
                    "emptyDir" => "emptyDir",
                    "hostPath" => "hostPath",
                    "csi" => $"CSI: {ReadString(source.Value, "driver")}",
                    null => string.Empty,
                    _ => source.Name
                };
                items.Add(new KubernetesResourceGraphComposition("Volume", name, details, string.Empty));
            }

        return items;

        void AddContainers(string field, string kind)
        {
            if (!TryPropertyPath(pod, out var containers, "spec", field) || containers.ValueKind != JsonValueKind.Array) return;
            foreach (var container in containers.EnumerateArray())
            {
                var name = ReadString(container, "name") ?? "(unnamed container)";
                var image = ReadString(container, "image") ?? string.Empty;
                var state = string.Empty;
                if (statusByName.TryGetValue(name, out var containerStatus))
                {
                    if (containerStatus.TryGetProperty("state", out var stateObject) && stateObject.ValueKind == JsonValueKind.Object)
                        state = stateObject.EnumerateObject().FirstOrDefault().Name ?? string.Empty;
                    if (containerStatus.TryGetProperty("restartCount", out var restartCount))
                        state = string.IsNullOrEmpty(state) ? $"restarts: {restartCount}" : $"{state} · restarts: {restartCount}";
                }
                items.Add(new KubernetesResourceGraphComposition(kind, name, image, state));
            }
        }
    }

    private static void AddSelectorEdges(JsonElement item, string first, string second, string relationship,
        string objectNamespace, ICollection<KubernetesResourceGraphReference> references)
    {
        if (!TryPropertyPath(item, out var selector, first, second) || selector.ValueKind != JsonValueKind.Object) return;
        var labels = selector.TryGetProperty("matchLabels", out var matchLabels) && matchLabels.ValueKind == JsonValueKind.Object
            ? matchLabels.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetString() ?? string.Empty, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var requirements = selector.TryGetProperty("matchExpressions", out var expressions) && expressions.ValueKind == JsonValueKind.Array
            ? expressions.EnumerateArray().Select(expression => new KubernetesLabelSelectorRequirement(
                ReadString(expression, "key") ?? string.Empty,
                ReadString(expression, "operator") ?? string.Empty,
                expression.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array
                    ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToArray()
                    : [])).ToArray()
            : [];
        references.Add(new KubernetesResourceGraphReference("Pod", string.Empty, objectNamespace, relationship, false, labels, requirements));
    }

    private static bool TryPropertyPath(JsonElement root, out JsonElement value, params string[] path)
    {
        value = root;
        foreach (var segment in path)
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value)) return false;
        return true;
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void AddSubjects(JsonElement item, string defaultNamespace,
        Action<string?, string?, string?, string, bool> add)
    {
        if (!TryPropertyPath(item, out var subjects, "subjects") || subjects.ValueKind != JsonValueKind.Array) return;
        foreach (var subject in subjects.EnumerateArray())
        {
            var kind = ReadString(subject, "kind");
            var name = ReadString(subject, "name");
            var ns = ReadString(subject, "namespace") ?? (kind == "ServiceAccount" ? defaultNamespace : string.Empty);
            if (kind == "ServiceAccount") add(kind, name, ns, "role-binding-subject", false);
        }
    }

    private static void AddPodReferences(JsonElement pod, string objectNamespace,
        Action<string?, string?, string?, string, bool> add)
    {
        if (TryPropertyPath(pod, out var imagePullSecrets, "spec", "imagePullSecrets") && imagePullSecrets.ValueKind == JsonValueKind.Array)
            foreach (var secret in imagePullSecrets.EnumerateArray())
                add("Secret", ReadString(secret, "name"), objectNamespace, "pod-image-pull-secret", false);

        if (TryPropertyPath(pod, out var volumes, "spec", "volumes") && volumes.ValueKind == JsonValueKind.Array)
            foreach (var volume in volumes.EnumerateArray())
            {
                add("CSIDriver", ReadPathString(volume, "csi", "driver"), null, "pod-csi-driver", false);
                add("PersistentVolumeClaim", ReadPathString(volume, "persistentVolumeClaim", "claimName"), objectNamespace, "pod-volume-claim", false);
                add("ConfigMap", ReadPathString(volume, "configMap", "name"), objectNamespace, "pod-config-map", false);
                add("Secret", ReadPathString(volume, "secret", "secretName"), objectNamespace, "pod-secret", false);
                add("Secret", ReadPathString(volume, "csi", "nodePublishSecretRef", "name"), objectNamespace, "pod-secret", false);
                if (TryPropertyPath(volume, out var projected, "projected", "sources") && projected.ValueKind == JsonValueKind.Array)
                    foreach (var source in projected.EnumerateArray())
                    {
                        add("ConfigMap", ReadPathString(source, "configMap", "name"), objectNamespace, "pod-config-map", false);
                        add("Secret", ReadPathString(source, "secret", "name"), objectNamespace, "pod-secret", false);
                    }
            }

        foreach (var field in new[] { "containers", "initContainers", "ephemeralContainers" })
        {
            if (!TryPropertyPath(pod, out var containers, "spec", field) || containers.ValueKind != JsonValueKind.Array) continue;
            foreach (var container in containers.EnumerateArray())
            {
                if (TryPropertyPath(container, out var envFrom, "envFrom") && envFrom.ValueKind == JsonValueKind.Array)
                    foreach (var item in envFrom.EnumerateArray())
                    {
                        add("ConfigMap", ReadPathString(item, "configMapRef", "name"), objectNamespace, "pod-config-map", false);
                        add("Secret", ReadPathString(item, "secretRef", "name"), objectNamespace, "pod-secret", false);
                    }
                if (TryPropertyPath(container, out var env, "env") && env.ValueKind == JsonValueKind.Array)
                    foreach (var variable in env.EnumerateArray())
                    {
                        add("ConfigMap", ReadPathString(variable, "valueFrom", "configMapKeyRef", "name"), objectNamespace, "pod-config-map", false);
                        add("Secret", ReadPathString(variable, "valueFrom", "secretKeyRef", "name"), objectNamespace, "pod-secret", false);
                    }
            }
        }
    }

    private static string? ReadPathString(JsonElement root, params string[] path) =>
        TryPropertyPath(root, out var value, path) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void AddRouteReferences(JsonElement item, string objectNamespace,
        Action<string?, string?, string?, string, bool> add)
    {
        if (TryPropertyPath(item, out var parents, "spec", "parentRefs") && parents.ValueKind == JsonValueKind.Array)
            foreach (var parent in parents.EnumerateArray())
                add("Gateway", ReadString(parent, "name"), ReadString(parent, "namespace") ?? objectNamespace, "route-parent", false);
        if (TryPropertyPath(item, out var rules, "spec", "rules") && rules.ValueKind == JsonValueKind.Array)
            foreach (var rule in rules.EnumerateArray())
                if (rule.TryGetProperty("backendRefs", out var backends) && backends.ValueKind == JsonValueKind.Array)
                    foreach (var backend in backends.EnumerateArray())
                        add("Service", ReadString(backend, "name"), ReadString(backend, "namespace") ?? objectNamespace, "route-backend", false);
    }

    private static string SummarizeStatus(string kind, JsonElement status)
    {
        if (kind == "Pod" && status.TryGetProperty("phase", out var phase)) return phase.GetString() ?? string.Empty;
        if (kind == "Deployment" && status.TryGetProperty("readyReplicas", out var ready))
            return $"{ready.GetInt32()}/{(status.TryGetProperty("replicas", out var replicas) ? replicas.GetInt32() : 0)} prontos";
        if (status.TryGetProperty("conditions", out var conditions) && conditions.ValueKind == JsonValueKind.Array)
        {
            var latest = conditions.EnumerateArray().LastOrDefault();
            if (latest.ValueKind == JsonValueKind.Object && latest.TryGetProperty("type", out var type) && latest.TryGetProperty("status", out var value))
                return $"{type.GetString()}: {value.GetString()}";
        }
        return string.Empty;
    }

    private static Uri ApiUri(Kubernetes client, string path) => new(client.BaseUri.ToString().TrimEnd('/') + "/" + path.TrimStart('/'));

    public static string ResolvePath(string? path)
    {
        var candidate = string.IsNullOrWhiteSpace(path)
            ? Environment.GetEnvironmentVariable("KUBECONFIG")?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
              ?? KubernetesClientConfiguration.KubeConfigDefaultLocation
            : path.Trim();
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
    }

    private void NotifyChanged() => Changed?.Invoke();

    private void NotifySelectionChanged(KubernetesSelectionChangeKind kind) =>
        SelectionChanged?.Invoke(new KubernetesSelectionChangedEventArgs(
            ++_selectionRevision, kind, ActiveConnectionId, CurrentNamespace));

    public void Dispose()
    {
        foreach (var state in _connected.Values) state.Client.Dispose();
        _connected.Clear();
    }

    private sealed class ConnectedCluster(KubernetesConnectionProfile profile, Kubernetes client, string server, string? namespaceName)
    {
        public KubernetesConnectionProfile Profile { get; } = profile;
        public Kubernetes Client { get; } = client;
        public string Server { get; } = server;
        public string? Namespace { get; set; } = namespaceName;
        public IReadOnlyList<string> Namespaces { get; set; } = [];
    }
}
