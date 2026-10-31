# Kubernetes Visualizer

Dashboard web para explorar recursos vivos de clusters Kubernetes, visualizar suas relações e executar operações comuns. A interface é construída com Blazor Server e usa o kubeconfig do usuário para acessar a API Kubernetes.

> **Idiomas / Languages:** [Português (Brasil)](#português-brasil) · [English](#english)

## Sumário

- [Português (Brasil)](#português-brasil)
  - [Visão geral](#visão-geral)
  - [Requisitos e configuração](#requisitos-e-configuração)
  - [Como executar](#como-executar)
  - [Como usar](#como-usar)
  - [Recursos](#recursos)
  - [Arquitetura e serviços](#arquitetura-e-serviços)
  - [Como o grafo é gerado](#como-o-grafo-é-gerado)
  - [Privacidade e segurança](#privacidade-e-segurança)
  - [Limitações conhecidas](#limitações-conhecidas)
  - [Licença](#licença)
- [English](#english)
  - [Overview](#overview)
  - [Requirements and configuration](#requirements-and-configuration)
  - [Run the application](#run-the-application)
  - [How to use](#how-to-use)
  - [Features](#features)
  - [Architecture and services](#architecture-and-services)
  - [How the graph is generated](#how-the-graph-is-generated)
  - [Privacy and security](#privacy-and-security)
  - [Known limitations](#known-limitations)
  - [License](#license)

---

## Português (Brasil)

### Visão geral

O Kubernetes Visualizer ajuda a entender o estado atual de um cluster por meio de um dashboard visual e de uma lista de recursos. Ele consulta objetos existentes na API do Kubernetes: não renderiza charts Helm nem lê manifests locais para montar o grafo.

### Requisitos e configuração

- .NET SDK 10 para execução local.
- Um cluster Kubernetes acessível e um kubeconfig válido com o contexto desejado.
- Docker e Docker Compose para execução em contêiner.

Por padrão, o app procura o kubeconfig no caminho indicado pela variável `KUBECONFIG` ou no caminho padrão do cliente Kubernetes. Na tela **Configurações**, adicione um perfil com o caminho do kubeconfig e o contexto a usar. O app salva perfis, última conexão e preferências operacionais em SQLite, por padrão em `src/App_Data/kubernetes-visualizer.db` na execução local.

As opções de configuração podem ser fornecidas pelo sistema de configuração do ASP.NET Core:

| Opção | Variável de ambiente | Padrão / uso |
| --- | --- | --- |
| Caminho do kubeconfig | `KUBECONFIG` | Caminho padrão do kubeconfig quando não informado. |
| Chaves de proteção de dados | `DataProtection__KeysDirectory` | `App_Data/DataProtection-Keys` sob o diretório de conteúdo do app. |

### Como executar

**Localmente**, na raiz do repositório:

```bash
dotnet run --project src/KubernetesVisualizer.csproj
```

O perfil HTTP de desenvolvimento usa `http://localhost:5080`.

**Com Docker Compose**, informe o caminho absoluto do kubeconfig do host antes de iniciar:

```bash
export KUBECONFIG_HOST_PATH="$HOME/.kube/config"
docker compose -f compose.yaml up --build
```

No PowerShell:

```powershell
$env:KUBECONFIG_HOST_PATH = "$env:USERPROFILE\.kube\config"
docker compose -f compose.yaml up --build
```

Acesse [http://localhost:5080](http://localhost:5080). O Compose monta o kubeconfig como somente leitura e mantém o banco do app em um volume nomeado. Instruções adicionais estão em [docs/docker.md](docs/docker.md).

### Como usar

1. Abra **Configurações**, informe um nome para a conexão, o caminho do kubeconfig e um contexto.
2. Conecte-se ao perfil e escolha um namespace quando aplicável.
3. Use o dashboard para explorar relações; arraste o canvas para navegar e clique em um recurso para selecioná-lo e abrir detalhes.
4. Abra **Recursos** para pesquisar, filtrar e operar sobre os objetos que a API disponibiliza ao usuário atual.
5. Use **Wiki** para consultar o material de referência em português ou inglês.

### Recursos

- Conexões salvas para múltiplos perfis e contextos Kubernetes, com restauração da última seleção.
- Descoberta dinâmica dos tipos de recurso que a API anuncia com suporte a listagem.
- Grafo com relações de propriedade, controle, seleção, referência, composição e associação por label, além de destaque de conexões e detalhes do recurso.
- Filtros por tipo e origem do recurso, navegação por arraste, zoom e modo de tela cheia.
- Lista de recursos com pesquisa, filtros e detalhes; leitura e aplicação de manifest YAML; exclusão; parada e retomada de workloads suportados; e logs de Pods.
- Secrets aparecem inicialmente apenas como metadados. A leitura de valores exige uma ação explícita e permissão adequada; o YAML do Secret é redigido.
- Preferências de interface e operação persistidas localmente.
- Wiki Kubernetes com conteúdo em português e inglês.

### Arquitetura e serviços

- `ClusterService` gerencia conexões com o cluster e chamadas à API Kubernetes.
- `ApplicationEventManager` coordena operações entre a interface e os serviços e centraliza os eventos de atualização.
- `ConnectionProfileStore` persiste somente perfis de conexão; `ApplicationSettingsStore` persiste configurações e última seleção em SQLite.
- `ResourceGraphService` carrega snapshots de recursos e constrói relações reutilizáveis para o grafo.
- `DashboardResourceGraphFilterManager` aplica os filtros do dashboard em estágios ordenados antes de entregar os recursos ao layout.
- `GraphGroupingSettingsService` persiste labels adicionais; os defaults tipados são carregados por `IOptions<GraphGroupingOptions>`.
- `Home.razor` organiza os nós e arestas filtrados para o dashboard; a interação de pan do canvas é tratada por `src/wwwroot/app.js`.
- `Resources.razor` apresenta a lista de recursos e ações suportadas pela API.

### Como o grafo é gerado

O serviço consulta a descoberta da API em `/api` e `/apis`, seleciona os tipos com verbo `list` e lista seus objetos em paralelo, com limite de seis tipos por vez. O snapshot contém metadados, referências reconhecidas e dados necessários para construir as relações. Erros por tipo são mantidos como falhas parciais para não descartar os outros resultados.

As relações usam evidências diferentes, identificadas no grafo e nos detalhes:

- **Propriedade e controle:** `metadata.ownerReferences`; pares conhecidos, como Deployment → ReplicaSet → Pod, também são marcados como relações de controle inferidas.
- **Seleção:** seletores de Service e de workloads são comparados com labels dos Pods.
- **Referência:** campos mapeados em recursos como HPA/VPA, Ingress, PVC, ConfigMap, Secret, ServiceAccount, RBAC, Gateway API e recursos Traefik.
- **Composição:** containers e volumes são representados como partes estruturais de Pods, sem alegar que sejam objetos Kubernetes independentes.
- **Associação por label:** os labels padrão definidos em `GraphGrouping:DefaultRelatedLabelKeys` no `appsettings.json` e os adicionais configurados em **Configurações** agrupam recursos que compartilham a mesma chave e valor. Os adicionais ficam no SQLite; os defaults sempre são incluídos. Isso não significa propriedade ou dependência Kubernetes.
- **Proteção:** finalizers e `ownerReferences[].blockOwnerDeletion` são apresentados como evidências de proteção.

O layout organiza os recursos em colunas horizontais e aproxima controladores, dependentes e recursos conectados. Mais detalhes estão em [docs/como-funciona.md](docs/como-funciona.md).

### Privacidade e segurança

- O app usa o kubeconfig fornecido para acessar o cluster; as permissões efetivas são determinadas pelo RBAC desse contexto.
- Para apenas consultar o dashboard, conceda as permissões de descoberta e listagem necessárias aos recursos desejados. Ações como ler Secrets, aplicar YAML, excluir recursos, escalar workloads e ler logs precisam das permissões correspondentes.
- O kubeconfig é montado como somente leitura pelo Compose. Perfis, seleção e configurações do app ficam no banco SQLite persistido.
- Não exponha a aplicação diretamente a redes públicas sem colocar controles de rede e autenticação adequados. O app não implementa um fluxo próprio de autenticação de usuários.
- Os valores de Secret só são solicitados por ação explícita; o conteúdo não é incluído no YAML mostrado pelo app.

### Limitações conhecidas

- O grafo representa objetos vivos retornados pela API e visíveis ao contexto conectado; não é um renderizador Helm nem um analisador de manifests locais.
- Relações de referências só são inferidas quando o campo está mapeado. CRDs desconhecidos e alvos não carregados não recebem relações ou nós substitutos automaticamente.
- Restrições de RBAC podem fazer com que alguns tipos não sejam listados. O dashboard mantém os demais resultados e informa falhas parciais.
- A leitura dos valores de Secret requer permissão de leitura específica. O YAML de Secret permanece redigido e Secret existente não pode ser editado pelo editor de manifest.
- “Parar” se aplica aos workloads escaláveis suportados (Deployment, StatefulSet e ReplicaSet), reduzindo réplicas a zero; o app salva a contagem anterior para a retomada. Alterações externas durante a parada podem deixar essa contagem desatualizada.
- Aplicar YAML aceita um objeto por vez. A API e o RBAC do cluster determinam se a operação será aceita.

### Licença

Este projeto está sob a licença [MIT](LICENSE).

---

## English

### Overview

Kubernetes Visualizer helps you understand a cluster's current state through a visual dashboard and a resource list. It queries live objects from the Kubernetes API; it does not render Helm charts or read local manifests to build the graph.

### Requirements and configuration

- .NET 10 SDK for local development.
- An accessible Kubernetes cluster and a valid kubeconfig with the context you want to use.
- Docker and Docker Compose for containerized runs.

By default, the app looks for the kubeconfig path specified by `KUBECONFIG` or the Kubernetes client's default location. In **Settings**, add a profile with the kubeconfig path and context. Profiles, the last connection, and operational preferences are stored in SQLite, by default at `src/App_Data/kubernetes-visualizer.db` during a local run.

Configuration can be supplied through ASP.NET Core configuration:

| Setting | Environment variable | Default / purpose |
| --- | --- | --- |
| Kubeconfig path | `KUBECONFIG` | Kubernetes client default when unset. |
| Data protection keys | `DataProtection__KeysDirectory` | `App_Data/DataProtection-Keys` under the app content root. |

### Run the application

**Locally**, from the repository root:

```bash
dotnet run --project src/KubernetesVisualizer.csproj
```

The development HTTP profile uses `http://localhost:5080`.

**With Docker Compose**, set the absolute path to the host kubeconfig before starting:

```bash
export KUBECONFIG_HOST_PATH="$HOME/.kube/config"
docker compose -f compose.yaml up --build
```

In PowerShell:

```powershell
$env:KUBECONFIG_HOST_PATH = "$env:USERPROFILE\.kube\config"
docker compose -f compose.yaml up --build
```

Open [http://localhost:5080](http://localhost:5080). Compose mounts the kubeconfig read-only and stores app data in a named volume. See [docs/docker.md](docs/docker.md) for additional instructions.

### How to use

1. Open **Settings**, enter a connection name, kubeconfig path, and context.
2. Connect to the profile and choose a namespace when applicable.
3. Use the dashboard to explore relationships; drag the canvas to navigate and click a resource to select it and open its details.
4. Open **Resources** to search, filter, and operate on objects visible to the current Kubernetes user.
5. Use the **Wiki** to browse reference material in Portuguese or English.

### Features

- Saved connections for multiple Kubernetes profiles and contexts, with restoration of the last selection.
- Dynamic discovery of resource types that the API advertises with the `list` verb.
- A graph of ownership, control, selector, reference, composition, and label-association relationships, with connection highlighting and resource details.
- Filters by resource kind and origin, drag-to-pan navigation, zoom, and fullscreen mode.
- A resource list with search, filters, and details; manifest YAML read/apply; deletion; stop/resume for supported workloads; and Pod logs.
- Secrets are shown as metadata by default. Reading values requires an explicit action and sufficient permission; Secret YAML is redacted.
- Locally persisted UI and operational preferences.
- Kubernetes wiki content in Portuguese and English.

### Architecture and services

- `ClusterService` manages cluster connections and Kubernetes API calls.
- `ApplicationEventManager` coordinates UI-to-service operations and centralizes update events.
- `ConnectionProfileStore` persists connection profiles only; `ApplicationSettingsStore` persists settings and the last selection in SQLite.
- `ResourceGraphService` loads resource snapshots and builds reusable graph relationships.
- `DashboardResourceGraphFilterManager` applies dashboard filters in ordered stages before handing resources to the layout.
- `GraphGroupingSettingsService` persists additional labels; typed defaults are loaded through `IOptions<GraphGroupingOptions>`.
- `Home.razor` arranges the filtered graph nodes and edges; canvas panning is handled by `src/wwwroot/app.js`.
- `Resources.razor` displays the resource list and API-backed actions.

### How the graph is generated

The service queries API discovery at `/api` and `/apis`, selects resource types with the `list` verb, and lists their objects in parallel, with a limit of six types at a time. The snapshot contains metadata, recognized references, and the data needed to build relationships. Per-type errors are recorded as partial failures so other results remain available.

Relationships use different kinds of evidence, identified in the graph and details:

- **Ownership and control:** `metadata.ownerReferences`; known pairs such as Deployment → ReplicaSet → Pod are also marked as inferred control relationships.
- **Selectors:** Service and workload selectors are matched against Pod labels.
- **References:** mapped fields in resources such as HPA/VPA, Ingress, PVC, ConfigMap, Secret, ServiceAccount, RBAC, Gateway API, and Traefik resources.
- **Composition:** containers and volumes appear as structural parts of Pods, without claiming they are standalone Kubernetes objects.
- **Label association:** default labels from `GraphGrouping:DefaultRelatedLabelKeys` in `appsettings.json` and additional labels configured in **Settings** visually group resources that share the same key and value. Additional labels are stored in SQLite; defaults are always included. This does not imply Kubernetes ownership or dependency.
- **Protection:** finalizers and `ownerReferences[].blockOwnerDeletion` are surfaced as protection evidence.

The layout arranges resources in horizontal columns and keeps controllers, dependents, and connected resources close together. More details are available in [docs/como-funciona.md](docs/como-funciona.md) (Portuguese).

### Privacy and security

- The app uses the supplied kubeconfig to access the cluster; effective permissions are determined by RBAC for that context.
- For dashboard reads, grant the discovery and list permissions needed for the resources you want to view. Actions such as reading Secrets, applying YAML, deleting resources, scaling workloads, and reading logs require their corresponding permissions.
- Compose mounts the kubeconfig read-only. Profiles, selection, and app settings are stored in the persistent SQLite database.
- Do not expose the app directly to public networks without appropriate network and authentication controls. The app does not implement its own user authentication flow.
- Secret values are fetched only after an explicit action; Secret content is not included in the YAML shown by the app.

### Known limitations

- The graph represents live objects returned by the API and visible to the connected context; it is not a Helm renderer or local manifest analyzer.
- Reference relationships are inferred only for mapped fields. Unknown CRDs and unloaded targets do not automatically receive relationships or placeholder nodes.
- RBAC restrictions may prevent some resource types from being listed. The dashboard retains other results and reports partial failures.
- Reading Secret values requires explicit read permission. Secret YAML remains redacted, and existing Secrets cannot be edited with the manifest editor.
- **Stop** applies to supported scalable workloads (Deployment, StatefulSet, and ReplicaSet), scaling them to zero; the app saves the previous replica count for resume. External changes while stopped may make the saved count stale.
- YAML apply accepts one object at a time. The API and cluster RBAC determine whether the operation is allowed.

### License

This project is licensed under the [MIT License](LICENSE).
