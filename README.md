# Kubernetes Visualizer

Dashboard web para explorar recursos vivos de clusters Kubernetes, visualizar suas relações e executar operações comuns. A interface é construída com Blazor Server e usa o kubeconfig do usuário para acessar a API Kubernetes.

> **Idiomas / Languages:** [Português (Brasil)](#português-brasil) · [English](#english)

## Sumário

- [Português (Brasil)](#português-brasil)
  - [Visão geral](#visão-geral)
  - [Requisitos e configuração](#requisitos-e-configuração)
  - [Como executar](#como-executar)
  - [Imagem Docker](#imagem-docker)
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
  - [Docker image](#docker-image)
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

Acesse [http://localhost:5080](http://localhost:5080). O Compose monta o kubeconfig como somente leitura e mantém o banco do app em um volume nomeado.

### Imagem Docker

**Construir localmente**, a partir da raiz do repositório (contexto e Dockerfile em `src/`):

```bash
docker build -f src/Dockerfile -t kubernetes-visualizer:local ./src
```

Executar a imagem local com acesso ao kubeconfig:

```bash
docker run --rm -p 5080:8080 \
  -e KUBECONFIG=/run/kubeconfig/config \
  -v "$HOME/.kube/config:/run/kubeconfig/config:ro" \
  -v visualizer-data:/app/App_Data \
  kubernetes-visualizer:local
```

No PowerShell:

```powershell
docker run --rm -p 5080:8080 `
  -e KUBECONFIG=/run/kubeconfig/config `
  -v "${env:USERPROFILE}\.kube\config:/run/kubeconfig/config:ro" `
  -v visualizer-data:/app/App_Data `
  kubernetes-visualizer:local
```

Acesse [http://localhost:5080](http://localhost:5080). O volume nomeado preserva o banco e as chaves de proteção de dados. Em um manifest Kubernetes, use `image: kubernetes-visualizer:local`, monte o kubeconfig em `/run/kubeconfig/config`, configure `KUBECONFIG` com esse caminho e exponha a porta `8080`.

**Imagem publicada pela GitHub Action:** pushes para qualquer branch acionam o build. `main` publica `ghcr.io/hedgarbezerra/k8s-visualizer:latest`; cada outra branch publica uma tag `sha-<hash-curto>` para permitir testar aquele commit. Tags de release `v*` também publicam a versão. O resumo da action mostra a versão OCI (`org.opencontainers.image.version`), tags, labels, commit, ID, digest e comandos de pull/run. Para usar pelo Docker CLI:

```bash
docker pull ghcr.io/hedgarbezerra/k8s-visualizer:latest
docker run --rm -p 5080:8080 \
  -e KUBECONFIG=/run/kubeconfig/config \
  -v "$HOME/.kube/config:/run/kubeconfig/config:ro" \
  -v visualizer-data:/app/App_Data \
  ghcr.io/hedgarbezerra/k8s-visualizer:latest
```

No YAML, use `image: ghcr.io/hedgarbezerra/k8s-visualizer:latest` ou a tag `sha-<hash-curto>` mostrada na execução da action. Configure `KUBECONFIG` e disponibilize o kubeconfig no contêiner. Dentro de um cluster, prefira conceder acesso via ServiceAccount/RBAC em vez de montar kubeconfig de uma estação de trabalho.

**Autenticação no GHCR:** imagens públicas podem ser baixadas anonimamente. Se o pacote estiver privado, faça login antes do `docker pull` usando um Personal Access Token (classic) com escopo `read:packages`:

```bash
IMAGE=ghcr.io/hedgarbezerra/k8s-visualizer:latest
read -r -p 'Usuário do GitHub: ' GHCR_USER
read -r -s -p 'Token GHCR (classic, read:packages): ' GHCR_TOKEN; printf '\n'
printf '%s' "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin \
  && docker pull "$IMAGE" \
  && docker run --rm -p 5080:8080 -e KUBECONFIG=/run/kubeconfig/config \
       -v "$HOME/.kube/config:/run/kubeconfig/config:ro" \
       -v visualizer-data:/app/App_Data "$IMAGE"
unset GHCR_TOKEN
```

O token é solicitado sem exibir os caracteres e não é passado ao container; o Docker o usa apenas para autenticar e baixar a imagem. Troque `latest` por `sha-<hash-curto>` para testar outra branch. No PowerShell:

```powershell
$image = 'ghcr.io/hedgarbezerra/k8s-visualizer:latest'
$user = Read-Host 'Usuário do GitHub'
$secureToken = Read-Host 'Token GHCR (classic, read:packages)' -AsSecureString
$token = [System.Net.NetworkCredential]::new('', $secureToken).Password
$token | docker login ghcr.io -u $user --password-stdin
if ($LASTEXITCODE -eq 0) {
  docker pull $image
  if ($LASTEXITCODE -eq 0) {
    docker run --rm -p 5080:8080 -e KUBECONFIG=/run/kubeconfig/config `
      -v "${env:USERPROFILE}\.kube\config:/run/kubeconfig/config:ro" `
      -v visualizer-data:/app/App_Data $image
  }
}
$token = $null
$secureToken = $null
```

Para um pacote privado consumido por Kubernetes, crie um `imagePullSecret` com essas credenciais e referencie-o em `spec.imagePullSecrets`.

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

O fluxo principal passa pela interface Blazor Server, pelos serviços de aplicação e pela API Kubernetes. Os componentes interativos rodam no circuito do usuário; os serviços de conexão e grafo são registrados por escopo, enquanto stores SQLite e o pipeline de filtros são compartilhados.

1. `Program.cs` registra serviços, configura cultura (`pt-BR` e `en`), Data Protection e componentes Razor interativos. Na inicialização, os stores SQLite criam suas tabelas.
2. `MainLayout.razor`, `Settings.razor` e `ClusterSidebarSelector.razor` usam `ApplicationEventManager` para carregar perfis, restaurar a última conexão/namespace, conectar ou desconectar e propagar mudanças de seleção e cultura para a interface.
3. `ClusterService` lê o kubeconfig e contexto escolhidos, mantém os clientes conectados e consulta a API Kubernetes. Ele concentra descoberta/listagem de recursos e também operações como aplicar YAML, excluir recursos, parar/retomar workloads e ler logs.
4. `Home.razor` pede um snapshot a `ResourceGraphService`. O serviço obtém os tipos descobertos, carrega recursos em paralelo (até seis tipos por vez), mantém o snapshot em cache e constrói relações a partir de referências, seletores, ownership e labels.
5. `DashboardResourceGraphFilterManager` aplica etapas ordenadas: identidade e exclusões, tipos disponíveis, construção de relações, sementes por grupo funcional e tipos visíveis, expansão para recursos relacionados e resultado visível. `Home.razor` aplica o layout ao resultado e `src/wwwroot/app.js` trata a navegação no canvas.
6. `Resources.razor` lista e opera sobre recursos via `ApplicationEventManager`/`ClusterService`; quando necessário, reutiliza o snapshot do `ResourceGraphService` para mostrar relações. Após mutações, a página invalida o cache do grafo.
7. `ConnectionProfileStore` armazena perfis e `ApplicationSettingsStore` guarda configurações e última seleção em `App_Data/kubernetes-visualizer.db`. `GraphGroupingSettingsService` combina labels padrão de `GraphGroupingOptions` com labels adicionais persistidas.

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

Open [http://localhost:5080](http://localhost:5080). Compose mounts the kubeconfig read-only and stores app data in a named volume.

### Docker image

**Build locally** from the repository root (build context and Dockerfile are in `src/`):

```bash
docker build -f src/Dockerfile -t kubernetes-visualizer:local ./src
```

Run the local image with access to your kubeconfig:

```bash
docker run --rm -p 5080:8080 \
  -e KUBECONFIG=/run/kubeconfig/config \
  -v "$HOME/.kube/config:/run/kubeconfig/config:ro" \
  -v visualizer-data:/app/App_Data \
  kubernetes-visualizer:local
```

Open [http://localhost:5080](http://localhost:5080). The named volume preserves the database and data-protection keys. A Kubernetes manifest can use `image: kubernetes-visualizer:local`; mount the kubeconfig at `/run/kubeconfig/config`, set `KUBECONFIG` to that path, and expose container port `8080`.

**Image published by the GitHub Action:** pushes to every branch trigger a build. `main` publishes `ghcr.io/hedgarbezerra/k8s-visualizer:latest`; every other branch publishes a `sha-<short-hash>` tag so that commit can be tested. `v*` release tags also publish the version. The action summary shows the OCI version (`org.opencontainers.image.version`), tags, labels, commit, image ID, digest, and pull/run commands. To use Docker CLI:

```bash
docker pull ghcr.io/hedgarbezerra/k8s-visualizer:latest
docker run --rm -p 5080:8080 \
  -e KUBECONFIG=/run/kubeconfig/config \
  -v "$HOME/.kube/config:/run/kubeconfig/config:ro" \
  -v visualizer-data:/app/App_Data \
  ghcr.io/hedgarbezerra/k8s-visualizer:latest
```

In a Kubernetes manifest, use `image: ghcr.io/hedgarbezerra/k8s-visualizer:latest` or the `sha-<short-hash>` tag shown in the action run. Set `KUBECONFIG` and make a kubeconfig available inside the container. In a cluster, prefer granting access through a ServiceAccount/RBAC instead of mounting a workstation kubeconfig.

**GHCR authentication:** public images can be pulled anonymously. If the package is private, log in before `docker pull` with a classic Personal Access Token that has the `read:packages` scope:

```bash
IMAGE=ghcr.io/hedgarbezerra/k8s-visualizer:latest
read -r -p 'GitHub username: ' GHCR_USER
read -r -s -p 'GHCR token (classic, read:packages): ' GHCR_TOKEN; printf '\n'
printf '%s' "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin \
  && docker pull "$IMAGE" \
  && docker run --rm -p 5080:8080 -e KUBECONFIG=/run/kubeconfig/config \
       -v "$HOME/.kube/config:/run/kubeconfig/config:ro" \
       -v visualizer-data:/app/App_Data "$IMAGE"
unset GHCR_TOKEN
```

The token is entered without displaying its characters and is not passed into the container; Docker uses it only to authenticate and pull the image. Change `latest` to `sha-<short-hash>` to test another branch. In PowerShell:

```powershell
$image = 'ghcr.io/hedgarbezerra/k8s-visualizer:latest'
$user = Read-Host 'GitHub username'
$secureToken = Read-Host 'GHCR token (classic, read:packages)' -AsSecureString
$token = [System.Net.NetworkCredential]::new('', $secureToken).Password
$token | docker login ghcr.io -u $user --password-stdin
if ($LASTEXITCODE -eq 0) {
  docker pull $image
  if ($LASTEXITCODE -eq 0) {
    docker run --rm -p 5080:8080 -e KUBECONFIG=/run/kubeconfig/config `
      -v "${env:USERPROFILE}\.kube\config:/run/kubeconfig/config:ro" `
      -v visualizer-data:/app/App_Data $image
  }
}
$token = $null
$secureToken = $null
```

For a private package consumed by Kubernetes, create an `imagePullSecret` with those credentials and reference it from `spec.imagePullSecrets`.

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

The main flow passes through the Blazor Server UI, application services, and the Kubernetes API. Interactive components run in the user's circuit; connection and graph services are scoped, while SQLite stores and the filter pipeline are shared.

1. `Program.cs` registers services, configures culture (`pt-BR` and `en`), Data Protection, and interactive Razor components. On startup, the SQLite stores create their tables.
2. `MainLayout.razor`, `Settings.razor`, and `ClusterSidebarSelector.razor` use `ApplicationEventManager` to load profiles, restore the last connection/namespace, connect or disconnect, and propagate selection and culture changes to the UI.
3. `ClusterService` reads the selected kubeconfig and context, maintains connected clients, and queries the Kubernetes API. It handles discovery/resource listing as well as operations such as applying YAML, deleting resources, stopping/resuming workloads, and reading logs.
4. `Home.razor` requests a snapshot from `ResourceGraphService`. The service loads discovered resource types in parallel (up to six types at a time), caches the snapshot, and builds relationships from references, selectors, ownership, and labels.
5. `DashboardResourceGraphFilterManager` runs ordered stages for identity and exclusion filters, available kinds, relationship building, functional-group and visible-kind seeds, related-resource expansion, and visible output. `Home.razor` lays out the result; `src/wwwroot/app.js` handles canvas navigation.
6. `Resources.razor` lists and operates on resources through `ApplicationEventManager`/`ClusterService`; when needed, it reuses the `ResourceGraphService` snapshot to show relationships. After mutations, the page invalidates the graph cache.
7. `ConnectionProfileStore` stores profiles and `ApplicationSettingsStore` stores settings and the last selection in `App_Data/kubernetes-visualizer.db`. `GraphGroupingSettingsService` combines default labels from `GraphGroupingOptions` with persisted additional labels.

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
