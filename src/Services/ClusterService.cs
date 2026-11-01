using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;
using KubernetesVisualizer.Models.UI;
using KubernetesVisualizer;
using k8s;
using k8s.KubeConfigModels;
using k8s.Models;
using Microsoft.Extensions.Localization;

namespace KubernetesVisualizer.Services;

public sealed class ClusterService : IDisposable
{
    private readonly ILogger<ClusterService> _logger;
    private readonly ConnectionProfileStore _profileStore;
    private readonly ApplicationSettingsStore _settingsStore;
    private readonly IStringLocalizer<AppStrings> _localizer;
    private readonly Dictionary<string, ConnectedCluster> _connected = new(StringComparer.Ordinal);
    private K8SConfiguration? _draftKubeConfig;
    private string? _draftPath;
    private bool _profilesLoaded;
    private KubernetesLastSelection _lastSelection = new(null, null);

    public ClusterService(ILogger<ClusterService> logger, ConnectionProfileStore profileStore,
        ApplicationSettingsStore settingsStore,
        IStringLocalizer<AppStrings> localizer)
    {
        _logger = logger;
        _profileStore = profileStore;
        _settingsStore = settingsStore;
        _localizer = localizer;
    }

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
        _lastSelection = await _settingsStore.LoadLastSelectionAsync(cancellationToken);
        _profilesLoaded = true;
    }

    public async Task LoadContextsFromPathAsync(string? path, CancellationToken cancellationToken = default)
    {
        Error = null;
        IsLoading = true;
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
        }
    }

    public async Task AddProfileAsync(string name, string kubeconfigPath, string contextName, CancellationToken cancellationToken = default)
    {
        await EnsureProfilesLoadedAsync(cancellationToken);
        var profile = new KubernetesConnectionProfile(Guid.NewGuid().ToString("N"), name.Trim(), ResolvePath(kubeconfigPath), contextName);
        Profiles = [.. Profiles, profile];
        await _profileStore.SaveAsync(Profiles, cancellationToken);
    }

    public async Task RemoveProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(profileId, cancellationToken);
        Profiles = Profiles.Where(profile => profile.Id != profileId).ToArray();
        await _profileStore.SaveAsync(Profiles, cancellationToken);
        if (_lastSelection.ConnectionId == profileId)
        {
            _lastSelection = new KubernetesLastSelection(null, null);
            await _settingsStore.SaveLastSelectionAsync(null, null, cancellationToken);
        }
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
            Kubernetes? candidateClient = null;
            var connectionStage = "carregar kubeconfig";
            var apiServer = "indisponível";
            _logger.LogInformation(
                "Iniciando conexão com perfil {ProfileName} ({ProfileId}); kubeconfig {KubeconfigPath}; contexto {ContextName}",
                profile.Name, profile.Id, profile.KubeconfigPath, profile.ContextName);
            try
            {
                var kubeConfig = await KubernetesClientConfiguration.LoadKubeConfigAsync(new FileInfo(profile.KubeconfigPath), useRelativePaths: true);
                connectionStage = "resolver contexto e endpoint";
                var context = kubeConfig.Contexts.FirstOrDefault(item => item.Name == profile.ContextName)
                    ?? throw new InvalidOperationException(_localizer["ContextNotFound", profile.ContextName]);
                var cluster = kubeConfig.Clusters.FirstOrDefault(item => item.Name == context.ContextDetails?.Cluster);
                apiServer = SafeApiServer(cluster?.ClusterEndpoint?.Server);
                _logger.LogInformation(
                    "Kubeconfig carregado para perfil {ProfileName}; contexto {ContextName}; cluster {ClusterName}; servidor da API {ApiServer}",
                    profile.Name, profile.ContextName, context.ContextDetails?.Cluster, apiServer);

                connectionStage = "criar cliente Kubernetes";
                candidateClient = new Kubernetes(KubernetesClientConfiguration.BuildConfigFromConfigObject(kubeConfig, profile.ContextName));
                var newState = new ConnectedCluster(profile, candidateClient, apiServer, context.ContextDetails?.Namespace);

                try
                {
                    connectionStage = "consultar namespaces na API Kubernetes";
                    _logger.LogInformation(
                        "Consultando namespaces no servidor {ApiServer} para perfil {ProfileName} ({ProfileId})",
                        apiServer, profile.Name, profile.Id);
                    var namespaces = await candidateClient.CoreV1.ListNamespaceAsync(cancellationToken: cancellationToken);
                    newState.Namespaces = namespaces.Items.Select(item => item.Metadata?.Name)
                        .Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>()
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
                    if (newState.Namespace is not null && !newState.Namespaces.Contains(newState.Namespace)) newState.Namespace = null;
                    _logger.LogInformation(
                        "Conexão com perfil {ProfileName} concluída; API {ApiServer} respondeu e retornou {NamespaceCount} namespaces",
                        profile.Name, apiServer, newState.Namespaces.Count);
                }
                catch (KubernetesException exception) when (exception.Status?.Code == (int)HttpStatusCode.Forbidden)
                {
                    _logger.LogInformation(exception,
                        "A API {ApiServer} respondeu Forbidden ao listar namespaces para o perfil {ProfileName}; a conexão foi mantida sem a lista de namespaces",
                        apiServer, profile.Name);
                    connectionNotice = _localizer["ConnectedWithoutNamespacePermission"];
                }
                catch (k8s.Autorest.HttpOperationException exception) when (exception.Response?.StatusCode == HttpStatusCode.Forbidden)
                {
                    _logger.LogInformation(exception,
                        "A API {ApiServer} respondeu Forbidden ao listar namespaces para o perfil {ProfileName}; a conexão foi mantida sem a lista de namespaces",
                        apiServer, profile.Name);
                    connectionNotice = _localizer["ConnectedWithoutNamespacePermission"];
                }

                _connected[profileId] = newState;
                state = newState;
                candidateClient = null;
            }
            catch (Exception exception)
            {
                candidateClient?.Dispose();
                _logger.LogWarning(exception,
                    "Falha ao conectar ao perfil {ProfileName} ({ProfileId}) na etapa {ConnectionStage}; kubeconfig {KubeconfigPath}; contexto {ContextName}; servidor da API {ApiServer}; tipo {ExceptionType}; mensagem {ExceptionMessage}",
                    profile.Name, profile.Id, connectionStage, profile.KubeconfigPath, profile.ContextName, apiServer,
                    exception.GetType().FullName, exception.Message);
                Error = _localizer["ConnectionFailed", exception.Message];
                IsLoading = false;
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
        await _settingsStore.SaveLastSelectionAsync(profileId, state.Namespace, cancellationToken);
    }

    private static string SafeApiServer(string? server)
    {
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri)) return "Endpoint indisponível";
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public async Task SelectConnectionAsync(string? profileId, CancellationToken cancellationToken = default)
    {
        if (profileId is null || !_connected.ContainsKey(profileId)) return;
        if (ActiveConnectionId == profileId) return;
        ActiveConnectionId = profileId;
        Error = null;
        _lastSelection = new KubernetesLastSelection(profileId, Active?.Namespace);
        await _settingsStore.SaveLastSelectionAsync(profileId, Active?.Namespace, cancellationToken);
    }

    public async Task SelectNamespaceAsync(string? namespaceName, CancellationToken cancellationToken = default)
    {
        if (Active is null) return;
        var selectedNamespace = string.IsNullOrWhiteSpace(namespaceName) ? null : namespaceName;
        if (Active.Namespace == selectedNamespace) return;
        Active.Namespace = selectedNamespace;
        _lastSelection = new KubernetesLastSelection(ActiveConnectionId, selectedNamespace);
        await _settingsStore.SaveLastSelectionAsync(ActiveConnectionId, selectedNamespace, cancellationToken);
    }

    public async Task DisconnectAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var activeWasRemoved = ActiveConnectionId == profileId;
        if (_connected.Remove(profileId, out var state)) state.Client.Dispose();
        if (activeWasRemoved) ActiveConnectionId = _connected.Keys.FirstOrDefault();
        if (activeWasRemoved)
        {
            _lastSelection = Active is null ? new KubernetesLastSelection(null, null) : new KubernetesLastSelection(ActiveConnectionId, Active.Namespace);
            await _settingsStore.SaveLastSelectionAsync(_lastSelection.ConnectionId, _lastSelection.Namespace, cancellationToken);
        }
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

    private static async Task<HttpResponseMessage> SendMetadataListRequestAsync(Kubernetes client, string route,
        bool metadataOnly, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, ApiUri(client, route));
        if (metadataOnly)
            request.Headers.TryAddWithoutValidation("Accept", "application/json;as=PartialObjectMetadataList;g=meta.k8s.io;v=v1");
        var response = await client.HttpClient.SendAsync(request, cancellationToken);
        request.Dispose();
        if (!metadataOnly || response.StatusCode is not (System.Net.HttpStatusCode.NotAcceptable or System.Net.HttpStatusCode.BadRequest))
            return response;
        response.Dispose();
        using var fallback = new HttpRequestMessage(HttpMethod.Get, ApiUri(client, route));
        return await client.HttpClient.SendAsync(fallback, cancellationToken);
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
            using var response = await SendMetadataListRequestAsync(client, pageRoute, type.Kind == "Secret", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                var metadata = item.GetProperty("metadata");
                var objectNamespace = metadata.TryGetProperty("namespace", out var ns) ? ns.GetString() ?? string.Empty : string.Empty;
                var status = IsTerminating(metadata) ? _localizer["Terminating"].Value
                    : item.TryGetProperty("status", out var statusElement) ? SummarizeStatus(type.Kind, item, statusElement) : string.Empty;
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
            using var response = await SendMetadataListRequestAsync(client, pageRoute, type.Kind == "Secret", cancellationToken);
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
                            owner.TryGetProperty("controller", out var controller) && controller.ValueKind == JsonValueKind.True,
                            owner.TryGetProperty("blockOwnerDeletion", out var block) && block.ValueKind == JsonValueKind.True))
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
                var status = IsTerminating(metadata) ? _localizer["Terminating"].Value
                    : item.TryGetProperty("status", out var statusObject)
                    ? SummarizeStatus(type.Kind, item, statusObject)
                    : string.Empty;
                var name = metadata.TryGetProperty("name", out var objectName) ? objectName.GetString() ?? "(sem nome)" : "(sem nome)";
                var objectNamespace = metadata.TryGetProperty("namespace", out var objectNamespaceElement)
                    ? objectNamespaceElement.GetString() ?? string.Empty
                    : string.Empty;
                var references = ExtractGraphReferences(type.Kind, item, objectNamespace);
                var compositions = type.Kind == "Pod" ? ExtractPodComposition(item) : [];
                var isSystemManaged = IsKubernetesSystemManaged(metadata, objectNamespace);
                var isControllerManaged = HasOwnerReferences(metadata);
                var finalizers = metadata.TryGetProperty("finalizers", out var finalizerArray) && finalizerArray.ValueKind == JsonValueKind.Array
                    ? finalizerArray.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                        .Select(value => value.GetString()!).ToArray()
                    : [];

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
                    isControllerManaged,
                    finalizers));
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
        using var response = await client.HttpClient.GetAsync(ApiUri(client, route), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var yaml = new YamlStream(new YamlDocument(JsonToYaml(document.RootElement, redactSecretData: kind == "Secret")));
        using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        yaml.Save(writer, assignAnchors: false);
        return writer.ToString();
    }

    public async Task<IReadOnlyDictionary<string, string>> GetSecretDataAsync(KubernetesResourceItem item,
        CancellationToken cancellationToken = default)
    {
        if (item.Kind != "Secret") throw new InvalidOperationException("O recurso selecionado não é um Secret.");
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var apiRoot = item.GroupVersion.Contains('/') ? $"/apis/{item.GroupVersion}" : $"/api/{item.GroupVersion}";
        var ns = string.IsNullOrWhiteSpace(item.Namespace) ? string.Empty : $"/namespaces/{Uri.EscapeDataString(item.Namespace)}";
        var route = $"{apiRoot}{ns}/{item.ResourceName}/{Uri.EscapeDataString(item.Name)}";
        using var response = await client.HttpClient.GetAsync(ApiUri(client, route), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Falha ao revelar Secret ({(int)response.StatusCode}): {error}", null, response.StatusCode);
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in data.EnumerateObject())
        {
            var encoded = entry.Value.GetString() ?? string.Empty;
            try { values[entry.Name] = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded)); }
            catch (Exception exception) when (exception is FormatException or DecoderFallbackException) { values[entry.Name] = encoded; }
        }
        return values;
    }

    public async Task<string> ApplyManifestYamlAsync(string yaml, CancellationToken cancellationToken = default)
    {
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var yamlStream = new YamlStream();
        yamlStream.Load(new StringReader(yaml));
        if (yamlStream.Documents.Count != 1 || yamlStream.Documents[0].RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("Informe exatamente um manifesto YAML com um objeto na raiz.");

        var json = ConvertYamlNode(root) as JsonObject ?? throw new InvalidOperationException("O manifesto deve ser um objeto YAML.");
        var apiVersion = json["apiVersion"]?.GetValue<string>();
        var kind = json["kind"]?.GetValue<string>();
        var metadata = json["metadata"] as JsonObject;
        var name = metadata?["name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(apiVersion) || string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("O manifesto precisa conter apiVersion, kind e metadata.name.");

        var type = (await GetResourceTypesAsync(cancellationToken)).FirstOrDefault(candidate =>
            candidate.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)
            && candidate.GroupVersion.Equals(apiVersion, StringComparison.OrdinalIgnoreCase));
        if (type is null) throw new InvalidOperationException($"O cluster não anuncia {kind} em {apiVersion}.");
        var namespaceName = metadata?["namespace"]?.GetValue<string>();
        if (type.Namespaced && string.IsNullOrWhiteSpace(namespaceName))
            namespaceName = CurrentNamespace ?? throw new InvalidOperationException("Informe metadata.namespace ou selecione um namespace.");
        if (!type.Namespaced && !string.IsNullOrWhiteSpace(namespaceName))
            throw new InvalidOperationException($"{kind} é cluster-wide e não aceita metadata.namespace.");
        if (metadata is not null && type.Namespaced) metadata["namespace"] = namespaceName;

        var apiRoute = type.ApiPath;
        if (type.Namespaced && !string.IsNullOrWhiteSpace(namespaceName)) apiRoute += $"/namespaces/{Uri.EscapeDataString(namespaceName)}";
        var collectionRoute = $"{apiRoute}/{type.Name}";
        var itemRoute = $"{collectionRoute}/{Uri.EscapeDataString(name)}";
        var existing = await client.HttpClient.GetAsync(ApiUri(client, itemRoute), cancellationToken);
        HttpMethod method;
        string route;
        if (existing.IsSuccessStatusCode)
        {
            using var currentDocument = JsonDocument.Parse(await existing.Content.ReadAsStreamAsync(cancellationToken));
            var currentMetadata = currentDocument.RootElement.GetProperty("metadata");
            if (currentMetadata.TryGetProperty("resourceVersion", out var resourceVersion)) metadata!["resourceVersion"] = resourceVersion.GetString();
            existing.Dispose();
            method = HttpMethod.Put;
            route = itemRoute;
            foreach (var field in new[] { "uid", "creationTimestamp", "generation", "managedFields" }) metadata?.Remove(field);
        }
        else if (existing.StatusCode == HttpStatusCode.NotFound)
        {
            existing.Dispose();
            method = HttpMethod.Post;
            route = collectionRoute;
        }
        else
        {
            var error = await existing.Content.ReadAsStringAsync(cancellationToken);
            var code = existing.StatusCode;
            existing.Dispose();
            throw new HttpRequestException($"Falha ao verificar o recurso ({(int)code}): {error}", null, code);
        }

        json.Remove("status");
        using var request = new HttpRequestMessage(method, ApiUri(client, route))
        {
            Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using var response = await client.HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Falha ao aplicar {kind}/{name} ({(int)response.StatusCode}): {error}", null, response.StatusCode);
        }
        return $"{kind}/{name} {(method == HttpMethod.Post ? "criado" : "atualizado")}.";
    }

    public async Task DeleteResourceAsync(KubernetesResourceItem item, CancellationToken cancellationToken = default)
    {
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var type = (await GetResourceTypesAsync(cancellationToken)).FirstOrDefault(candidate =>
            candidate.Kind.Equals(item.Kind, StringComparison.OrdinalIgnoreCase)
            && candidate.GroupVersion.Equals(item.GroupVersion, StringComparison.OrdinalIgnoreCase)
            && candidate.Name.Equals(item.ResourceName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"O tipo {item.Kind} não está mais disponível no cluster.");
        var route = type.ApiPath;
        if (type.Namespaced && !string.IsNullOrWhiteSpace(item.Namespace)) route += $"/namespaces/{Uri.EscapeDataString(item.Namespace)}";
        route += $"/{type.Name}/{Uri.EscapeDataString(item.Name)}";
        using var response = await client.HttpClient.DeleteAsync(ApiUri(client, route), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Falha ao excluir {item.Kind}/{item.Name} ({(int)response.StatusCode}): {error}", null, response.StatusCode);
        }
    }

    public async Task<string> GetPodLogsAsync(string namespaceName, string podName, string? containerName,
        int tailLines = 200, CancellationToken cancellationToken = default)
    {
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var route = $"/api/v1/namespaces/{Uri.EscapeDataString(namespaceName)}/pods/{Uri.EscapeDataString(podName)}/log?tailLines={Math.Clamp(tailLines, 1, 5000)}";
        if (!string.IsNullOrWhiteSpace(containerName)) route += $"&container={Uri.EscapeDataString(containerName)}";
        using var response = await client.HttpClient.GetAsync(ApiUri(client, route), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Falha ao ler logs do Pod ({(int)response.StatusCode}): {error}", null, response.StatusCode);
        }
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPodContainerNamesAsync(string namespaceName, string podName,
        CancellationToken cancellationToken = default)
    {
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var route = $"/api/v1/namespaces/{Uri.EscapeDataString(namespaceName)}/pods/{Uri.EscapeDataString(podName)}";
        using var response = await client.HttpClient.GetAsync(ApiUri(client, route), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Falha ao ler containers do Pod ({(int)response.StatusCode}): {error}", null, response.StatusCode);
        }

        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty("spec", out var spec)) return [];

        var names = new List<string>();
        foreach (var field in new[] { "containers", "initContainers", "ephemeralContainers" })
        {
            if (!spec.TryGetProperty(field, out var containers) || containers.ValueKind != JsonValueKind.Array) continue;
            foreach (var container in containers.EnumerateArray())
                if (container.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString()))
                    names.Add(name.GetString()!);
        }
        return names.Distinct(StringComparer.Ordinal).ToArray();
    }

    public async Task StopWorkloadAsync(KubernetesResourceItem item, CancellationToken cancellationToken = default)
    {
        if (item.Kind == "Pod")
        {
            await DeleteResourceAsync(item, cancellationToken);
            return;
        }
        if (item.Kind is not ("Deployment" or "StatefulSet" or "ReplicaSet"))
            throw new InvalidOperationException("Parada é suportada para Pod, Deployment, StatefulSet e ReplicaSet.");
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var type = (await GetResourceTypesAsync(cancellationToken)).First(candidate =>
            candidate.Kind.Equals(item.Kind, StringComparison.OrdinalIgnoreCase)
            && candidate.GroupVersion.Equals(item.GroupVersion, StringComparison.OrdinalIgnoreCase));
        var route = $"{type.ApiPath}/namespaces/{Uri.EscapeDataString(item.Namespace)}/{type.Name}/{Uri.EscapeDataString(item.Name)}";
        using var current = await client.HttpClient.GetAsync(ApiUri(client, route), cancellationToken);
        current.EnsureSuccessStatusCode();
        using var currentJson = JsonDocument.Parse(await current.Content.ReadAsStreamAsync(cancellationToken));
        var replicas = currentJson.RootElement.TryGetProperty("spec", out var spec)
                       && spec.TryGetProperty("replicas", out var currentReplicas) && currentReplicas.TryGetInt32(out var count)
            ? count : 1;
        if (replicas <= 0) throw new InvalidOperationException("Este workload já está sem réplicas ativas para salvar como estado anterior.");
        var stateKey = GetStoppedWorkloadSettingKey(item);
        var previous = await _settingsStore.GetApplicationSettingAsync(stateKey, cancellationToken);
        await _settingsStore.SetApplicationSettingAsync(stateKey, replicas.ToString(System.Globalization.CultureInfo.InvariantCulture), cancellationToken);
        try { await PatchReplicasAsync(client, route, 0, cancellationToken); }
        catch
        {
            await _settingsStore.SetApplicationSettingAsync(stateKey, previous, cancellationToken);
            throw;
        }
    }

    public async Task<int?> GetStoppedWorkloadReplicasAsync(KubernetesResourceItem item, CancellationToken cancellationToken = default)
    {
        var value = await _settingsStore.GetApplicationSettingAsync(GetStoppedWorkloadSettingKey(item), cancellationToken);
        return int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var replicas)
            ? replicas : null;
    }

    public async Task<WorkloadReplicaState?> GetWorkloadReplicaStateAsync(KubernetesResourceItem item, CancellationToken cancellationToken = default)
    {
        if (item.Kind is not ("Deployment" or "StatefulSet" or "ReplicaSet")) return null;
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var type = (await GetResourceTypesAsync(cancellationToken)).First(candidate =>
            candidate.Kind.Equals(item.Kind, StringComparison.OrdinalIgnoreCase)
            && candidate.GroupVersion.Equals(item.GroupVersion, StringComparison.OrdinalIgnoreCase));
        var route = $"{type.ApiPath}/namespaces/{Uri.EscapeDataString(item.Namespace)}/{type.Name}/{Uri.EscapeDataString(item.Name)}";
        using var response = await client.HttpClient.GetAsync(ApiUri(client, route), cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        var currentReplicas = ReadCount(json.RootElement, "spec", "replicas") ?? 1;
        var previousReplicas = currentReplicas == 0 ? await GetStoppedWorkloadReplicasAsync(item, cancellationToken) : null;
        return new WorkloadReplicaState(currentReplicas, previousReplicas);
    }

    public async Task ResumeWorkloadAsync(KubernetesResourceItem item, CancellationToken cancellationToken = default)
    {
        if (item.Kind is not ("Deployment" or "StatefulSet" or "ReplicaSet"))
            throw new InvalidOperationException("Retomada de réplica é suportada para Deployment, StatefulSet e ReplicaSet.");
        var replicas = await GetStoppedWorkloadReplicasAsync(item, cancellationToken)
            ?? throw new InvalidOperationException("Não há uma quantidade anterior de réplicas salva para restaurar.");
        var client = Active?.Client ?? throw new InvalidOperationException(_localizer["ConnectInSettings"]);
        var type = (await GetResourceTypesAsync(cancellationToken)).First(candidate =>
            candidate.Kind.Equals(item.Kind, StringComparison.OrdinalIgnoreCase)
            && candidate.GroupVersion.Equals(item.GroupVersion, StringComparison.OrdinalIgnoreCase));
        var route = $"{type.ApiPath}/namespaces/{Uri.EscapeDataString(item.Namespace)}/{type.Name}/{Uri.EscapeDataString(item.Name)}";
        await PatchReplicasAsync(client, route, replicas, cancellationToken);
        await _settingsStore.SetApplicationSettingAsync(GetStoppedWorkloadSettingKey(item), null, cancellationToken);
    }

    private string GetStoppedWorkloadSettingKey(KubernetesResourceItem item) =>
        $"stopped_replicas:{ActiveConnectionId}:{item.GroupVersion}:{item.Namespace}:{item.Kind}:{item.Name}";

    private static async Task PatchReplicasAsync(Kubernetes client, string route, int replicas, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, ApiUri(client, route))
        {
            Content = new StringContent(JsonSerializer.Serialize(new { spec = new { replicas } }), Encoding.UTF8, "application/merge-patch+json")
        };
        using var response = await client.HttpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Falha ao alterar réplicas ({(int)response.StatusCode}): {error}", null, response.StatusCode);
        }
    }

    public async Task<IReadOnlyList<KubernetesResourceActionResult>> DeleteResourcesAsync(
        IReadOnlyList<KubernetesResourceItem> items, CancellationToken cancellationToken = default) =>
        await RunResourceActionsAsync(items, item => DeleteResourceAsync(item, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<KubernetesResourceActionResult>> StopResourcesAsync(
        IReadOnlyList<KubernetesResourceItem> items, CancellationToken cancellationToken = default) =>
        await RunResourceActionsAsync(items, item => StopWorkloadAsync(item, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<KubernetesResourceActionResult>> ResumeResourcesAsync(
        IReadOnlyList<KubernetesResourceItem> items, CancellationToken cancellationToken = default) =>
        await RunResourceActionsAsync(items, item => ResumeWorkloadAsync(item, cancellationToken), cancellationToken);

    private static async Task<IReadOnlyList<KubernetesResourceActionResult>> RunResourceActionsAsync(
        IReadOnlyList<KubernetesResourceItem> items, Func<KubernetesResourceItem, Task> action, CancellationToken cancellationToken)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<KubernetesResourceActionResult>();
        await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (item, token) =>
        {
            try { await action(item); results.Add(new KubernetesResourceActionResult(item, true, "OK")); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { results.Add(new KubernetesResourceActionResult(item, false, exception.Message)); }
        });
        return results.OrderBy(result => result.Resource.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Resource.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Resource.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static JsonNode? ConvertYamlNode(YamlNode node) => node switch
    {
        YamlMappingNode mapping => new JsonObject(mapping.Children.Select(pair =>
            new KeyValuePair<string, JsonNode?>(pair.Key is YamlScalarNode key ? key.Value ?? string.Empty : pair.Key.ToString(), ConvertYamlNode(pair.Value)))),
        YamlSequenceNode sequence => new JsonArray(sequence.Children.Select(ConvertYamlNode).ToArray()),
        YamlScalarNode scalar => ConvertYamlScalar(scalar),
        _ => null
    };

    private static YamlNode JsonToYaml(JsonElement value, bool redactSecretData = false)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var mapping = new YamlMappingNode();
                foreach (var property in value.EnumerateObject())
                {
                    if (redactSecretData && property.Name is "data" or "stringData") continue;
                    mapping.Add(new YamlScalarNode(property.Name), JsonToYaml(property.Value));
                }
                return mapping;
            }
            case JsonValueKind.Array:
                return new YamlSequenceNode(value.EnumerateArray().Select(child => JsonToYaml(child)));
            case JsonValueKind.String:
                return new YamlScalarNode(value.GetString()) { Style = YamlDotNet.Core.ScalarStyle.DoubleQuoted };
            case JsonValueKind.Number:
                return new YamlScalarNode(value.GetRawText()) { Style = YamlDotNet.Core.ScalarStyle.Plain };
            case JsonValueKind.True: return new YamlScalarNode("true") { Style = YamlDotNet.Core.ScalarStyle.Plain };
            case JsonValueKind.False: return new YamlScalarNode("false") { Style = YamlDotNet.Core.ScalarStyle.Plain };
            default: return new YamlScalarNode("null") { Style = YamlDotNet.Core.ScalarStyle.Plain };
        }
    }

    private static JsonNode? ConvertYamlScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (scalar.Style != YamlDotNet.Core.ScalarStyle.Plain || value is null) return value is null ? null : JsonValue.Create(value);
        if (value is "null" or "Null" or "NULL" or "~") return null;
        if (bool.TryParse(value, out var boolean)) return JsonValue.Create(boolean);
        if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integer)) return JsonValue.Create(integer);
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)) return JsonValue.Create(number);
        return JsonValue.Create(value);
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
        if (metadata.TryGetProperty("annotations", out var annotations) && annotations.ValueKind == JsonValueKind.Object
            && annotations.TryGetProperty("kubernetes.io/created-by", out _)) return true;
        // managedFields identifies who last managed fields, not whether this is a system resource.
        return false;
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
            case "Deployment":
            case "ReplicaSet":
            case "DaemonSet":
            case "ReplicationController":
                AddSelectorEdges(item, "spec", "selector", "workload-selector", objectNamespace, references);
                break;
            case "StatefulSet":
                AddSelectorEdges(item, "spec", "selector", "workload-selector", objectNamespace, references);
                Add("Service", StringAt(item, "spec", "serviceName"), objectNamespace, "statefulset-service", false);
                if (TryPath(item, out var claimTemplates, "spec", "volumeClaimTemplates") && claimTemplates.ValueKind == JsonValueKind.Array)
                    foreach (var template in claimTemplates.EnumerateArray())
                    {
                        var claimName = StringAt(template, "metadata", "name");
                        if (!string.IsNullOrWhiteSpace(claimName))
                            references.Add(new KubernetesResourceGraphReference("PersistentVolumeClaim",
                                $"{claimName}-{StringAt(item, "metadata", "name")}-", objectNamespace,
                                "statefulset-claim-template", false, TargetNameIsPrefix: true));
                    }
                break;
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
            case "IngressRoute":
                if (TryPath(item, out var ingressRoutes, "spec", "routes") && ingressRoutes.ValueKind == JsonValueKind.Array)
                    foreach (var route in ingressRoutes.EnumerateArray())
                    {
                        if (TryPath(route, out var middlewares, "middlewares") && middlewares.ValueKind == JsonValueKind.Array)
                            foreach (var middleware in middlewares.EnumerateArray())
                                Add("Middleware", StringAt(middleware, "name"), StringAt(middleware, "namespace") ?? objectNamespace,
                                    "ingressroute-middleware", false);
                        if (TryPath(route, out var services, "services") && services.ValueKind == JsonValueKind.Array)
                            foreach (var service in services.EnumerateArray())
                            {
                                var name = StringAt(service, "name");
                                // Traefik's api@internal is an internal provider target, not a Kubernetes Service object.
                                if (string.Equals(name, "api@internal", StringComparison.OrdinalIgnoreCase)) continue;
                                Add(StringAt(service, "kind") ?? "Service", name,
                                    StringAt(service, "namespace") ?? objectNamespace, "ingressroute-service", false);
                            }
                    }
                break;
            case "Middleware":
                Add("Secret", StringAt(item, "spec", "basicAuth", "secret"), objectNamespace, "middleware-secret", false);
                if (TryPath(item, out var chainMiddlewares, "spec", "chain", "middlewares") && chainMiddlewares.ValueKind == JsonValueKind.Array)
                    foreach (var middleware in chainMiddlewares.EnumerateArray())
                        Add("Middleware", StringAt(middleware, "name"), StringAt(middleware, "namespace") ?? objectNamespace,
                            "middleware-chain", false);
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
                var claimName = source.Name == "persistentVolumeClaim"
                    ? ReadString(source.Value, "claimName")
                    : null;
                var relatedResource = string.IsNullOrWhiteSpace(claimName)
                    ? null
                    : new KubernetesResourceGraphReference("PersistentVolumeClaim", claimName,
                        ReadPathString(pod, "metadata", "namespace") ?? string.Empty,
                        "pod-volume-claim", false);
                items.Add(new KubernetesResourceGraphComposition("Volume", name, details, string.Empty, relatedResource));
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

    private string SummarizeStatus(string kind, JsonElement resource, JsonElement status)
    {
        if (kind == "Pod") return string.IsNullOrWhiteSpace(SummarizePodStatus(status))
            ? _localizer["NotApplicable"].Value : SummarizePodStatus(status);
        if (kind is "Deployment" or "ReplicaSet" or "StatefulSet")
        {
            var desired = ReadCount(resource, "spec", "replicas") ?? ReadCount(status, "replicas") ?? 0;
            var ready = ReadCount(status, "readyReplicas") ?? 0;
            return $"{ready}/{desired} {_localizer["Ready"]}";
        }
        if (kind == "DaemonSet")
        {
            var desired = ReadCount(status, "desiredNumberScheduled") ?? 0;
            var ready = ReadCount(status, "numberReady") ?? 0;
            return $"{ready}/{desired} {_localizer["Ready"]}";
        }
        if (kind == "Job")
        {
            var succeeded = ReadCount(status, "succeeded") ?? 0;
            var completions = ReadCount(resource, "spec", "completions");
            var active = ReadCount(status, "active") ?? 0;
            var failed = ReadCount(status, "failed") ?? 0;
            return completions is null ? $"{succeeded} {_localizer["Completed"]} · {active} {_localizer["Active"]} · {failed} {_localizer["Failed"]}"
                : $"{succeeded}/{completions} {_localizer["Completed"]} · {active} {_localizer["Active"]} · {failed} {_localizer["Failed"]}";
        }
        if (kind == "CronJob" && status.TryGetProperty("active", out var activeJobs) && activeJobs.ValueKind == JsonValueKind.Array)
            return $"{activeJobs.GetArrayLength()} {_localizer["ActiveJobs"]}";
        if (kind is "PersistentVolume" or "PersistentVolumeClaim" or "Namespace"
            && status.TryGetProperty("phase", out var phase) && phase.ValueKind == JsonValueKind.String)
            return phase.GetString() ?? _localizer["NotApplicable"].Value;
        if (kind == "HorizontalPodAutoscaler")
        {
            var current = ReadCount(status, "currentReplicas");
            var desired = ReadCount(status, "desiredReplicas");
            if (current is not null && desired is not null) return $"{current}/{desired} {_localizer["Replicas"]}";
        }
        if (kind == "PodDisruptionBudget" && ReadCount(status, "disruptionsAllowed") is { } allowed)
            return $"{allowed} {_localizer["DisruptionsAllowed"]}";
        if (kind == "Service")
        {
            if (status.TryGetProperty("loadBalancer", out var loadBalancer)
                && TryPropertyPath(loadBalancer, out var ingresses, "ingress") && ingresses.ValueKind == JsonValueKind.Array
                && ingresses.GetArrayLength() > 0)
                return $"{_localizer["LoadBalancer"]}: {ingresses.GetArrayLength()} {_localizer["Addresses"]}";
            var type = ReadPathString(resource, "spec", "type") ?? "ClusterIP";
            var clusterIp = ReadPathString(resource, "spec", "clusterIP");
            return string.IsNullOrWhiteSpace(clusterIp) || clusterIp == "None" ? type : $"{type} · {clusterIp}";
        }
        if (status.TryGetProperty("conditions", out var conditions) && conditions.ValueKind == JsonValueKind.Array)
        {
            var priorities = new[] { "Ready", "Available", "Progressing", "Complete", "Failed", "Established", "Scheduled" };
            var condition = conditions.EnumerateArray()
                .Where(candidate => candidate.ValueKind == JsonValueKind.Object)
                .OrderBy(candidate => Array.FindIndex(priorities, preferred =>
                    candidate.TryGetProperty("type", out var type) && type.GetString()?.Equals(preferred, StringComparison.OrdinalIgnoreCase) == true) is var index && index >= 0 ? index : int.MaxValue)
                .FirstOrDefault();
            if (condition.ValueKind == JsonValueKind.Object && condition.TryGetProperty("type", out var conditionType)
                && condition.TryGetProperty("status", out var conditionStatus))
            {
                var reason = ReadString(condition, "reason");
                return string.IsNullOrWhiteSpace(reason)
                    ? $"{conditionType.GetString()}: {conditionStatus.GetString()}"
                    : $"{conditionType.GetString()}: {conditionStatus.GetString()} · {reason}";
            }
        }
        if (status.TryGetProperty("phase", out var genericPhase) && genericPhase.ValueKind == JsonValueKind.String)
            return genericPhase.GetString() ?? _localizer["NotApplicable"].Value;
        return _localizer["NotApplicable"].Value;
    }

    private static int? ReadCount(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current)) return null;
        }
        return current.TryGetInt32(out var value) ? value : null;
    }

    private string SummarizePodStatus(JsonElement status)
    {
        var parts = new List<string>();
        if (status.TryGetProperty("phase", out var phase) && phase.ValueKind == JsonValueKind.String)
            parts.Add(phase.GetString() ?? "Unknown");
        if (status.TryGetProperty("containerStatuses", out var containers) && containers.ValueKind == JsonValueKind.Array)
        {
            var containerStates = containers.EnumerateArray().ToArray();
            var ready = containerStates.Count(container => container.TryGetProperty("ready", out var value) && value.ValueKind == JsonValueKind.True);
            parts.Add($"{ready}/{containerStates.Length} {_localizer["Ready"]}");
            var restarts = containerStates.Sum(container => container.TryGetProperty("restartCount", out var value) && value.TryGetInt32(out var count) ? count : 0);
            if (restarts > 0) parts.Add(_localizer["RestartsCount", restarts].Value);
        }
        var reason = ReadPodContainerReason(status);
        if (!string.IsNullOrWhiteSpace(reason)) parts.Add(reason);
        return string.Join(" · ", parts);
    }

    private static string? ReadPodContainerReason(JsonElement status)
    {
        foreach (var field in new[] { "initContainerStatuses", "containerStatuses", "ephemeralContainerStatuses" })
            if (status.TryGetProperty(field, out var containers) && containers.ValueKind == JsonValueKind.Array)
                foreach (var container in containers.EnumerateArray())
                    if (container.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.Object)
                        foreach (var stateName in new[] { "waiting", "terminated" })
                            if (state.TryGetProperty(stateName, out var detail) && detail.ValueKind == JsonValueKind.Object
                                && detail.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
                                return reason.GetString();
        return null;
    }

    private static bool IsTerminating(JsonElement metadata) => metadata.TryGetProperty("deletionTimestamp", out var timestamp)
        && timestamp.ValueKind == JsonValueKind.String;

    private static Uri ApiUri(Kubernetes client, string path) => new(client.BaseUri.ToString().TrimEnd('/') + "/" + path.TrimStart('/'));

    public static string ResolvePath(string? path)
    {
        var candidate = string.IsNullOrWhiteSpace(path)
            ? Environment.GetEnvironmentVariable("KUBECONFIG")?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
              ?? KubernetesClientConfiguration.KubeConfigDefaultLocation
            : path.Trim();
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate));
    }

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
