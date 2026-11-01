# Plano de implementação: dashboard Kubernetes em Blazor

**Status:** documento histórico; substituído por `plano-dashboard-kubernetes-blazor-v2.md`, que consolida as decisões atualizadas.
**Data:** 20 de outubro de 2026  
**Escopo:** aplicação web para visualizar e administrar recursos Kubernetes, com evolução opcional para integração com Argo CD e GitOps.

### Projeto de referência

[Certeza] Usar o projeto de visualização AWS/LocalStack e seu arquivo `AwsLocalStackVisualizer.csproj` como referência de organização e experiência de uso. [Certeza] O projeto usa ASP.NET Core Web SDK, `net9.0`, nullable e implicit usings; hospeda componentes Razor com `InteractiveServer`, registra serviços via DI, separa interfaces/serviços/modelos, configura health checks, tratamento global de erros e Serilog, e usa Bootstrap. [Provável] Reaproveitar esses padrões onde couber; não copiar dependências AWS/LocalStack nem adotar automaticamente o target framework ou os detalhes específicos de AWS.

> [Provável] Este plano agora assume uma ferramenta de laboratório local, sem login, para operar um cluster de desenvolvimento descartável. Essa escolha tira autenticação de usuário do escopo, mas não torna segura uma conexão acidental com um cluster compartilhado ou de produção; por isso, o perfil local precisa restringir endpoint/contexto e tornar operações destrutivas visíveis.

## 1. Objetivo e limites

[Provável] Construir um dashboard operacional que ofereça uma visão rápida do estado do cluster, navegação por namespaces e recursos, detalhes e eventos, além de um conjunto pequeno e explícito de ações administrativas.

[Provável] A primeira versão deve ser um painel de observabilidade operacional e administração assistida; ela não deve tentar reproduzir todo o ciclo de reconciliação de GitOps do Argo CD.

### Dentro do escopo inicial

- [Provável] Exibir saúde e capacidade geral do cluster, namespaces e recursos de workload mais usados.
- [Provável] Pesquisar e filtrar recursos por cluster, namespace, tipo, nome e rótulos.
- [Provável] Mostrar detalhes, status, condições, eventos recentes e, quando permitido, logs de Pods.
- [Provável] Criar, editar e remover recursos pelo app, incluindo criação/edição/exclusão de workloads e operações como reiniciar Pods e refazer rollout de Deployments.
- [Provável] Gerenciar Helm releases/charts: listar, instalar, atualizar, reverter e remover releases; edição de um chart significa editar seus values/versão ou arquivos locais do chart, conforme decisão de produto.
- [Provável] Oferecer navegação em árvore com perspectivas por namespace e por Node.
- [Provável] Operar por meio da API Kubernetes usando o pacote KubernetesClient no backend ASP.NET Core.

### Fora do escopo inicial

- [Provável] Reconciliação de manifests, cálculo de drift, sincronização e rollback GitOps.
- [Provável] Uso em produção, acesso multiusuário ou multi-tenant e publicação externa da aplicação.
- [Provável] Terminal `kubectl` remoto, acesso a Secrets ou proxy genérico para a API Kubernetes.
- [Provável] Gestão de ciclo de vida do cluster, instalação de operadores ou provisionamento de infraestrutura.

## 2. Princípios de produto e arquitetura

1. **[Provável] Perfil de laboratório:** assumir ambiente local e de desenvolvimento, sem login de usuário.
2. **[Provável] Limite de contexto:** mostrar o contexto Kubernetes e endpoint selecionados de forma persistente; aceitar apenas contextos explicitamente marcados como locais/de desenvolvimento no perfil de laboratório.
3. **[Provável] Operações explícitas:** separar criar, editar, excluir, reiniciar e rollout em ações tipadas; mostrar alvo/namespace e pedir confirmação em operações destrutivas.
4. **[Provável] Sem proxy arbitrário:** a UI chama serviços de aplicação e adaptadores; não expõe endpoint genérico que aceite qualquer verbo e URL Kubernetes.
5. **[Provável] Transparência de estado:** indicar atualização, contexto e indisponibilidade do cluster.
6. **[Provável] GitOps é modo próprio:** uma alteração direta pode divergir de Git e ser revertida pelo reconciliador; exibir essa condição se Argo CD for conectado.

## 3. Arquitetura proposta

[Provável] Começar como um monólito modular ASP.NET Core hospedando um Blazor Web App em modo interativo no servidor. O backend usa o kubeconfig/contexto escolhido localmente e a aplicação fica vinculada a loopback (`localhost`) no perfil de laboratório. O modo interativo no servidor mantém acesso ao cluster no backend ([modos de renderização do Blazor](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes)).

```text
Navegador local
   │ HTTP/HTTPS em loopback
   ▼
Blazor Web App (Interactive Server, sem login no perfil local)
   │ validação do contexto local, confirmações, auditoria operacional
   ▼
ASP.NET Core — serviços de aplicação / APIs internas
   │ adaptadores locais
   ├── KubernetesClient ──► API Kubernetes do contexto selecionado
   ├── Helm SDK/CLI isolado ──► Helm releases/charts
   └── (fase opcional) API do Argo CD
```

### Componentes

- **[Provável] Apresentação Blazor:** páginas, componentes, filtros, navegação e diálogos de confirmação; sem credenciais de cluster no cliente.
- **[Provável] API/host ASP.NET Core:** validação de contexto, proteção contra CSRF para operações mutáveis, validação, rate limiting e tratamento uniforme de erros; sem login no perfil local.
- **[Provável] Aplicação:** casos de uso como `ListDeployments`, `GetPodDetails`, `GetEvents` e `ScaleDeployment`; aplica limites e regras antes de chamar adaptadores.
- **[Provável] Domínio:** modelos de leitura independentes dos modelos gerados pelo cliente Kubernetes, definições de estados de saúde e contratos de auditoria.
- **[Provável] Infraestrutura Kubernetes:** adaptador baseado em KubernetesClient, carregando credenciais do contexto local selecionado ([documentação Kubernetes C# Client](https://kubernetes-client.github.io/csharp/)).
- **[Provável] Infraestrutura Helm:** adaptador para operações de release/chart. Avaliar SDK .NET compatível ou invocação controlada do Helm CLI como processo local; Helm cobre install, upgrade, rollback e uninstall ([documentação Helm](https://helm.sh/docs/intro/using_helm/)).
- **[Provável] Infraestrutura opcional Argo CD:** adaptador separado que chama a API oficial do Argo CD; não deve depender de detalhes internos do adaptador Kubernetes ([API do Argo CD](https://argo-cd.readthedocs.io/en/stable/developer-guide/api-docs/)).
- **[Provável] Observabilidade:** logs estruturados, métricas e traces com identificador de correlação; omitir dados sensíveis.

### Fluxo de dados e atualização

- [Provável] Para listas e resumos, usar chamadas paginadas e filtros no servidor sempre que a API permitir; evitar varrer todo o cluster a cada interação.
- [Provável] Atualizar painéis em intervalos configuráveis, com possibilidade de atualização manual e indicação visível de horário.
- [Provável] Considerar watches/streams Kubernetes numa fase posterior; começar com polling controlado reduz complexidade de reconexão e consumo.
- [Provável] Aplicar cache curto apenas a dados de leitura, com chave que inclua cluster, namespace, tipo e identidade/escopo relevante. Nunca compartilhar dados entre escopos sem validação.
- [Provável] Definir limites de concorrência, timeout, cancelamento, tamanho máximo de resposta e tratamento de `429`, `403` e falhas de conexão.

### Navegação em árvore

- [Certeza] Namespaces e Nodes são escopos distintos no modelo Kubernetes: Nodes são recursos de cluster, enquanto Pods e Deployments são normalmente namespaced; um Pod é associado a um Node pelo agendador ([Namespaces](https://kubernetes.io/docs/concepts/overview/working-with-objects/namespaces/), [atribuição de Pods a Nodes](https://kubernetes.io/docs/concepts/scheduling-eviction/assign-pod-node/)).
- [Provável] Portanto, apresentar duas árvores ou modos de navegação: **por namespace** (`Cluster > Namespaces > Namespace > Workloads > Pods`) e **por Node** (`Cluster > Nodes > Node > Pods`). Em cada Pod da visão de Node, mostrar seu namespace como coluna/etiqueta e permitir abrir o namespace.
- [Provável] Tratar recursos cluster-wide (Nodes, PersistentVolumes, StorageClasses) em um ramo próprio `Cluster > Recursos globais`.
- [Provável] A árvore serve para navegação e seleção; buscas e listas tabulares continuam disponíveis para operações em lote e filtragem.

## 4. Funcionalidades por área

### Visão geral

- [Provável] Cartões de namespaces, Nodes, Pods, Deployments, StatefulSets, DaemonSets e Jobs, respeitando a cobertura de métricas disponível.
- [Provável] Resumo de workloads prontos, indisponíveis ou em progresso, com link para a lista filtrada correspondente.
- [Provável] Alertas operacionais baseados em condições e eventos, sem afirmar uma saúde que a API não consegue determinar.
- [Provável] Se não houver Metrics Server ou outra fonte de métricas, mostrar “métrica indisponível” em vez de inventar percentuais de CPU/memória.

### Exploração de recursos

- [Provável] Navegação por cluster, namespace, grupo/tipo de recurso e estado.
- [Provável] Busca por nome e rótulo, paginação, ordenação e filtros persistidos na URL quando apropriado.
- [Provável] Detalhe de workload com réplicas desejadas/atuais/prontas, imagem, seletores, condições, Pods associados e eventos recentes.
- [Provável] Detalhe de Pod com fase, Node, reinícios, condições, containers, eventos e logs conforme o acesso concedido ao contexto Kubernetes selecionado.
- [Provável] Visualização YAML somente leitura inicialmente, com ocultação ou mascaramento de campos sensíveis.
- [Provável] Tratamento explícito para CRDs desconhecidos: identificação e visualização básica, sem assumir esquema ou oferecer edição automática.

### Ações administrativas controladas

- [Provável] Ações planejadas para o perfil local: criar/editar/excluir recursos Kubernetes, alterar réplicas, excluir Pods para que controladores os recriem e iniciar rollout restart de Deployment.
- [Provável] “Refazer deployment” deve ser implementado como operação de rollout explícita (reiniciar rollout ou atualizar imagem/manifest), diferenciada de excluir Deployment, que remove o controlador e seus Pods.
- [Provável] Suportar YAML avançado como editor opcional após validação sintática, preview/diff e resumo dos objetos afetados; oferecer formulários tipados para os casos comuns.
- [Provável] Operações destrutivas mostram contexto, namespace, tipo e nome, e exigem confirmação por ação. Para exclusão de namespace ou recursos cluster-wide, exigir uma segunda confirmação textual.
- [Provável] Chart Helm é um pacote versionado; instalação/upgrade criam ou alteram um release. Edição de chart pode significar alterar values da release ou modificar os arquivos-fonte do chart, que são fluxos diferentes e devem ser separados na UI.
- [Provável] Se Argo CD estiver reconciliando a aplicação, avisar antes de alterar recurso gerenciado diretamente, pois o estado desejado pode ser restaurado.

### Integração opcional com Argo CD

- [Provável] Adicionar uma tela de Applications com projeto, destino, repositório/revisão, estado de sincronização e saúde.
- [Provável] Exibir recursos e condições reportados pelo Argo CD, histórico de sincronizações e links contextuais.
- [Provável] Adicionar operações de refresh/sync apenas depois de definir como confirmar ações e apresentar o destino/revisão no perfil local.
- [Provável] Modelar o Argo CD como fonte de estado GitOps e o Kubernetes como estado observado; não presumir que toda aplicação do cluster seja gerenciada pelo Argo CD.

## 5. Perfil local e limites de segurança

### Identidade da aplicação

- [Provável] Não implementar login de usuário no modo de laboratório local.
- [Provável] Vincular o servidor apenas a loopback por padrão e exibir um aviso persistente com contexto e endpoint Kubernetes ativos.
- [Provável] Falhar fechado se o contexto/endpoint não for reconhecido como o cluster de desenvolvimento escolhido; permitir uma opção de override somente via configuração local explícita.

### Acesso Kubernetes no laboratório

- [Provável] Usar kubeconfig local fora do repositório; nunca copiar credenciais para arquivos de configuração versionados.
- [Provável] Exibir o nome do contexto e servidor antes de qualquer mutação e bloquear contexts marcados como produção, se esse metadado estiver disponível.
- [Provável] Uma ferramenta sem login pode executar com todas as permissões do kubeconfig selecionado; por isso, recomenda-se usar um cluster kind/minikube dedicado e descartável.
- [Provável] A aplicação precisa dos verbos Kubernetes necessários para CRUD, rollout e logs; permissões amplas continuam sendo risco, mesmo em laboratório, se o contexto apontar para o cluster errado.

### Salvaguardas mínimas do modo local

- [Provável] Ocultar Secrets por padrão, pois valores podem aparecer em YAML, logs ou telas compartilhadas.
- [Provável] Limitar logs de Pod, eventos e YAML para evitar exposição acidental de tokens, variáveis sensíveis ou dados pessoais.
- [Provável] Proteger operações mutáveis contra CSRF, validar entradas e nomes, impor limites de tamanho e usar `CancellationToken`.
- [Provável] Não construir comandos de shell com dados do usuário; interagir com a API usando cliente tipado.
- [Provável] Manter CSRF, validação de entrada, confirmação de exclusões e logs operacionais, embora não haja autenticação de usuário.
- [Provável] Não expor o serviço na rede local/externa sem reintroduzir autenticação e revisão de permissões.

## 6. Stack sugerida

- [Provável] **Frontend:** Blazor Web App, renderização interativa no servidor para a primeira versão.
- [Provável] **Backend:** ASP.NET Core com DI, validação de contexto local, health checks e logging estruturado, seguindo os padrões simples de host e composição do projeto de referência.
- [Provável] **Acesso Kubernetes:** `KubernetesClient` (cliente C# da comunidade Kubernetes), encapsulado atrás de interfaces de infraestrutura.
- [Provável] **Helm:** avaliar integração com Helm SDK ou processo local do Helm CLI para gerenciamento de releases; definir como será edição de values versus edição de chart-fonte.
- [Certeza] **Padrões visuais e de código a avaliar para reaproveitamento:** Razor Components interativos no servidor, abstrações de serviço, modelos próprios de UI, estados de carregamento/erro, Bootstrap, health checks e exception handler vistos em `AwsLocalStackVisualizer`.
- [Provável] **Persistência:** nenhuma obrigatória para o MVP; adicionar banco somente para preferências, auditoria durável, mapeamentos de cluster ou dados próprios.
- [Provável] **Cache:** memória distribuída somente quando escala horizontal ou carga exigir; manter TTL curto e isolamento de escopo.
- [Provável] **Observabilidade:** OpenTelemetry e backend de logs/métricas já usado pela organização.
- [Provável] **Entrega:** execução local, configuração externa, probes de saúde e limites de recursos; deployment em cluster fica fora do perfil inicial.
- [Chute] Uma biblioteca de componentes visuais específica deve ser escolhida após um protótipo curto de tabelas, árvores e estados; não é necessário fixar fornecedor neste plano.
- [Provável] Fixar versões de .NET, KubernetesClient e Kubernetes suportadas no início da implementação, após conferir as versões estáveis e a matriz de compatibilidade vigentes.

## 7. Estrutura sugerida de projetos

```text
src/
  KubernetesVisualizer/             # Blazor Web App, layout e páginas locais
  KubernetesVisualizer.Application/     # casos de uso, políticas e contratos
  KubernetesVisualizer.Domain/          # modelos e regras de estado
  KubernetesVisualizer.Infrastructure.Kubernetes/  # KubernetesClient e mapeamentos
  KubernetesVisualizer.Infrastructure.ArgoCd/       # integração opcional futura
  KubernetesVisualizer.Contracts/       # DTOs se houver API pública/interna separada

ops/
  kubernetes/                        # configuração local de desenvolvimento, opcional
  dashboards/                        # configuração de observabilidade opcional

docs/
  architecture/                      # decisões arquiteturais e diagramas
```

[Provável] Para um MVP pequeno, os projetos `Application`, `Domain` e `Infrastructure` podem começar como pastas ou assemblies dentro de uma solução reduzida; extraia limites quando ajudarem testes, segurança ou evolução independente.

## 8. Tasks de implementação para um agent

[Provável] Executar as tasks em ordem. Cada task deve produzir uma alteração pequena e revisável; não iniciar a próxima antes de a definição de pronto da atual estar satisfeita. As tasks abaixo descrevem entregáveis, não prazos de equipe.

### Etapa A — confirmar o alvo local

#### Task 1 — registrar as decisões do MVP

- **Objetivo:** fechar as escolhas que mudam a implementação.
- **Fazer:** registrar o cluster local suportado primeiro, como será reconhecido o contexto permitido, quais recursos entram no CRUD inicial e o que “editar charts” significa (values, versão ou arquivos do chart).
- **Pronto quando:** as quatro decisões estão escritas em `docs/decisions.md`; itens sem resposta permanecem explicitamente marcados como pendentes, sem serem tratados como requisitos confirmados.

#### Task 2 — inspecionar o repositório e propor a estrutura mínima

- **Objetivo:** alinhar o plano à solução real antes de criar arquivos.
- **Fazer:** identificar projetos, versão de .NET, convenções, instruções locais e comandos existentes; inspecionar como o projeto de referência usa `Program.cs`, componentes `InteractiveServer`, DI, interfaces/serviços/modelos, `OperationResult`, tratamento global de erros, health checks e Bootstrap; propor o menor conjunto de projetos necessário para Blazor e Kubernetes.
- **Pronto quando:** a estrutura existente e as mudanças propostas estão resumidas; cada padrão reaproveitado ou não reaproveitado tem justificativa curta; dependências AWS/LocalStack não são copiadas; nenhum arquivo de aplicação foi criado ou reorganizado sem necessidade.

### Etapa B — iniciar o app local

#### Task 3 — criar o host Blazor Web App

- **Objetivo:** iniciar a aplicação local sem login.
- **Fazer:** criar/configurar o host Blazor Web App + ASP.NET Core conforme a estrutura aprovada e os padrões compatíveis do projeto de referência (`InteractiveServer`, DI e configuração central em `Program.cs`); não adicionar Identity ou fluxo de autenticação.
- **Pronto quando:** a aplicação inicia localmente, mostra uma página inicial simples e não contém telas de login.

#### Task 4 — limitar o host a loopback

- **Objetivo:** impedir exposição acidental do perfil de laboratório na rede.
- **Fazer:** configurar o endereço de escuta para `localhost`/loopback por padrão e mostrar no README como alterar essa configuração somente se o perfil mudar.
- **Pronto quando:** a configuração padrão não escuta em interfaces externas e o endereço efetivo é documentado.

#### Task 5 — definir e exibir o contexto Kubernetes

- **Objetivo:** deixar claro qual cluster será alterado.
- **Fazer:** ler o contexto selecionado do kubeconfig local, permitir selecionar entre os contextos autorizados pela configuração local e exibir nome do contexto e endpoint em cabeçalho persistente.
- **Pronto quando:** seleção ausente, endpoint não permitido ou contexto inacessível geram estado de erro visível; nenhuma ação de escrita fica habilitada sem contexto válido.

#### Task 6 — adicionar verificação de conexão ao cluster

- **Objetivo:** distinguir app disponível de cluster disponível.
- **Fazer:** implementar uma chamada de leitura simples e apresentar conectado/desconectado, contexto e horário da última verificação.
- **Pronto quando:** falha de rede ou credencial não encerra o app e a tela comunica que os dados do cluster não estão disponíveis.

### Etapa C — leitura e navegação

#### Task 7 — criar o adaptador Kubernetes de namespaces

- **Objetivo:** isolar a chamada ao KubernetesClient.
- **Fazer:** implementar somente consulta e mapeamento de namespaces para um modelo de leitura próprio da aplicação; seguir a separação interface/serviço/modelo do projeto de referência e reutilizar um tipo de resultado comum apenas se ele se encaixar sem acoplamento desnecessário.
- **Pronto quando:** a UI não depende diretamente de tipos KubernetesClient e erros da API são convertidos em estados de aplicação claros; a dependência Kubernetes fica registrada via DI.

#### Task 8 — listar e selecionar namespaces

- **Objetivo:** disponibilizar o primeiro fluxo útil de exploração.
- **Fazer:** apresentar namespaces com busca simples e seleção; manter visíveis carregamento, lista vazia e erro.
- **Pronto quando:** selecionar um namespace atualiza o escopo das próximas telas e não mistura dados de namespaces diferentes.

#### Task 9 — listar Deployments do namespace

- **Objetivo:** exibir workloads gerenciados pelo usuário.
- **Fazer:** consultar Deployments do namespace selecionado e mostrar nome, réplicas desejadas/prontas, imagem e estado resumido.
- **Pronto quando:** trocar namespace recarrega a lista e a tela distingue lista vazia de erro de acesso.

#### Task 10 — listar Pods e seus Nodes

- **Objetivo:** conectar workloads aos Pods em execução.
- **Fazer:** listar Pods do namespace, indicando fase, reinícios, Node e controlador proprietário quando disponível.
- **Pronto quando:** um Pod pode ser relacionado ao namespace e ao Node sem inferir uma relação namespace-filho-de-Node.

#### Task 11 — implementar a árvore por namespace

- **Objetivo:** navegar da visão geral até os Pods pela hierarquia de namespace.
- **Fazer:** implementar `Cluster > Namespaces > Namespace > Workloads > Pods`, com carregamento sob demanda dos filhos.
- **Pronto quando:** cada ramo abre a lista correspondente e a seleção da árvore mantém contexto e namespace consistentes.

#### Task 12 — implementar a árvore por Node

- **Objetivo:** localizar Pods por máquina de execução.
- **Fazer:** listar Nodes e, ao selecionar um, mostrar Pods agendados nele; exibir namespace, workload proprietário e estado de cada Pod.
- **Pronto quando:** a navegação `Cluster > Nodes > Node > Pods` funciona e o namespace aparece como atributo do Pod, não como filho do Node.

#### Task 13 — implementar o detalhe de Deployment

- **Objetivo:** mostrar os dados que o usuário precisa antes de alterar um workload.
- **Fazer:** mostrar réplicas, imagem, seletores, condições e Pods associados.
- **Pronto quando:** os dados pertencem ao Deployment selecionado e a tela apresenta carregamento, indisponibilidade e recurso removido.

#### Task 14 — implementar o detalhe de Pod, eventos e logs

- **Objetivo:** diagnosticar um Pod sem recorrer a comandos externos.
- **Fazer:** exibir condições, containers, eventos recentes e logs com limite de linhas/tempo; ocultar Secrets e credenciais.
- **Pronto quando:** falha ao obter logs não impede a visualização dos demais detalhes e o usuário vê quando os logs foram consultados.

#### Task 15 — adicionar YAML somente leitura

- **Objetivo:** permitir inspeção de campos não exibidos nos formulários.
- **Fazer:** apresentar YAML/JSON do recurso selecionado em modo somente leitura, mascarando dados sensíveis conhecidos.
- **Pronto quando:** não há botão de aplicar nessa tela e o mascaramento não altera o objeto no cluster.

### Etapa D — mutações diretas

#### Task 16 — implementar edição de réplicas de Deployment

- **Objetivo:** oferecer a primeira alteração de workload com escopo claro.
- **Fazer:** aceitar número de réplicas, validar entrada, mostrar valor atual e novo e chamar a API Kubernetes.
- **Pronto quando:** sucesso atualiza a tela; falha preserva valor atual e mostra mensagem clara; a operação não altera outros campos.

#### Task 17 — implementar rollout restart de Deployment

- **Objetivo:** reiniciar os Pods gerenciados pelo Deployment sem remover o controlador.
- **Fazer:** implementar a operação de restart do rollout e apresentar confirmação com contexto, namespace e Deployment.
- **Pronto quando:** o Deployment continua existindo e o app mostra o novo estado do rollout; a ação não é confundida com exclusão/recriação do Deployment.

#### Task 18 — implementar exclusão de Pod

- **Objetivo:** permitir remover um Pod para que seu controlador o recrie quando aplicável.
- **Fazer:** mostrar nome, namespace, Node e controlador; pedir confirmação; excluir e atualizar a lista.
- **Pronto quando:** o resultado distingue exclusão bem-sucedida, erro e objeto já removido; o app explica que um controlador pode criar outro Pod.

#### Task 19 — implementar criação de Deployment por formulário

- **Objetivo:** criar um workload comum sem exigir YAML manual.
- **Fazer:** coletar namespace, nome, imagem e réplicas; validar campos antes de enviar.
- **Pronto quando:** o Deployment aparece na lista após criação e erros de validação/API são apresentados junto ao formulário.

#### Task 20 — implementar edição tipada de Deployment

- **Objetivo:** editar os campos suportados sem sobrescrever status ou campos geridos pelo Kubernetes.
- **Fazer:** começar por imagem e réplicas; atualizar somente esses campos e mostrar valores anterior/novo.
- **Pronto quando:** campos não editáveis permanecem intactos e conflito/erro de atualização pode ser recuperado sem perder a seleção.

#### Task 21 — implementar exclusão de Deployment

- **Objetivo:** remover um Deployment de forma explícita.
- **Fazer:** mostrar contexto, namespace, nome e que Pods gerenciados podem ser removidos; exigir confirmação antes de enviar.
- **Pronto quando:** o usuário distingue excluir Deployment de rollout restart e recebe o resultado da operação.

#### Task 22 — implementar editor YAML com preview

- **Objetivo:** permitir casos avançados depois dos formulários comuns.
- **Fazer:** validar YAML, mostrar diff/objetos afetados e oferecer aplicar somente após confirmação; limitar inicialmente aos tipos e namespaces acordados.
- **Pronto quando:** YAML inválido nunca é enviado e o preview identifica contexto e recursos que serão criados, alterados ou removidos.

### Etapa E — Helm e integração opcional

#### Task 23 — validar a estratégia de integração Helm

- **Objetivo:** escolher como o app chamará Helm.
- **Fazer:** comparar SDK .NET disponível com processo Helm CLI local considerando compatibilidade, renderização, erros e captura de saída; registrar decisão antes de implementar.
- **Pronto quando:** decisão documentada e nenhum comando recebe texto de shell concatenado a partir de entrada livre do usuário.

#### Task 24 — listar Helm releases

- **Objetivo:** inspecionar aplicações instaladas por Helm.
- **Fazer:** listar release, chart/versão, namespace, revisão e estado.
- **Pronto quando:** a lista respeita o contexto selecionado e diferencia ausência de release de falha no Helm.

#### Task 25 — instalar e atualizar Helm release

- **Objetivo:** instalar chart e atualizar uma release existente.
- **Fazer:** selecionar chart e versão, editar values, mostrar o destino e apresentar preview/diff quando disponível antes da confirmação.
- **Pronto quando:** resultado mostra revisão e estado; falha não é apresentada como sucesso; o usuário consegue identificar a release e namespace afetados.

#### Task 26 — remover e reverter Helm release

- **Objetivo:** suportar uninstall e rollback de release.
- **Fazer:** listar revisões para rollback e exigir confirmação para uninstall, mostrando nome e namespace.
- **Pronto quando:** ação e alvo são inequívocos e a tela atualiza o estado da release após a resposta do Helm.

#### Task 27 — consultar Applications do Argo CD (opcional)

- **Objetivo:** adicionar leitura GitOps sem acoplar o fluxo Kubernetes existente.
- **Fazer:** criar adaptador separado para listar Applications e mostrar destino, revisão, sync e saúde.
- **Pronto quando:** indisponibilidade do Argo CD não afeta as telas Kubernetes e a UI distingue sync de saúde.

#### Task 28 — relacionar recursos e ações Argo CD (opcional)

- **Objetivo:** contextualizar quais recursos são geridos por uma Application.
- **Fazer:** mostrar origem GitOps nos detalhes de recursos; só depois adicionar refresh/sync com destino/revisão visíveis e confirmação.
- **Pronto quando:** a UI avisa que mudanças diretas podem ser revertidas e nunca apresenta uma mutação Kubernetes como commit Git.

### Regras de execução das tasks

- [Provável] Cada task altera somente o necessário para o seu objetivo e mantém nomes/contratos coerentes com os projetos existentes.
- [Provável] Ao concluir uma task, o agent registra arquivos alterados, comportamento entregue, critérios verificados e limitações conhecidas.
- [Provável] Se uma decisão em aberto bloquear uma task, o agent atualiza a decisão pendente e para apenas aquela dependência; pode continuar tasks independentes.
- [Provável] Não adicionar funcionalidades de task futura “por conveniência”; isso mantém cada alteração pequena e fácil de revisar.

## 9. Critérios transversais de aceite

- [Provável] O servidor é o único componente com credenciais e conectividade administrativa ao cluster.
- [Provável] Cada mutação valida tipo, contexto, namespace e alvo; ações destrutivas exigem confirmação; a aplicação inicia apenas em loopback no perfil local.
- [Provável] Erros de rede, `403`, `404`, timeout e throttling são apresentados com estados distintos e sem conteúdo sensível.
- [Provável] A interface funciona em larguras de tela suportadas e oferece navegação por teclado nos fluxos críticos.
- [Provável] Há logs estruturados correlacionáveis, sem tokens, kubeconfig, Secret data ou payloads sensíveis.
- [Provável] A aplicação pode iniciar e verificar prontidão sem permissões administrativas de cluster.
- [Provável] Atualizações de dependências e versões de Kubernetes suportadas são revisadas antes de cada release.

## 10. Decisões em aberto

| Decisão | Opções comuns | Recomendação inicial |
|---|---|---|
| [Provável] Qual cluster local será suportado primeiro? | kind; minikube; Docker Desktop Kubernetes; outro | [Provável] Escolher um e testar a validação de endpoint/contexto nele. |
| [Provável] Como distinguir o ambiente de laboratório? | Endpoint allowlist; nome de contexto; configuração local explícita | [Provável] Usar endpoint permitido e confirmação explícita; nome de contexto sozinho não prova que o cluster é local. |
| [Provável] O que significa editar charts? | Alterar values; trocar versão; editar arquivos do chart | [Provável] Separar gerenciamento de Helm release da edição do chart-fonte. |
| [Provável] Qual fluxo de criação/edição? | Formulários tipados; YAML com preview; ambos | [Provável] Formulários para recursos comuns e YAML avançado com diff/preview. |
| [Provável] O que significa refazer deployment? | Rollout restart; atualizar imagem; excluir/recriar Deployment | [Provável] Tratar restart e atualização de imagem como ações distintas; não chamar exclusão do controlador de “restart”. |
| [Provável] O Argo CD já está disponível? | Sim; planejado; não | [Provável] Manter integração opcional isolada e exibir recursos gerenciados por GitOps. |
| [Provável] Quais recursos entram na primeira tela? | Workloads; Nodes; eventos; métricas; CRDs | [Provável] Namespaces, Nodes, Deployments, Pods e eventos; expandir depois. |

## 11. Riscos e mitigação

- **[Provável] Contexto aponta para cluster errado:** validar endpoint/contexto, avisar persistentemente e usar cluster descartável dedicado.
- **[Provável] Aplicação torna-se caminho indireto para ações administrativas:** endpoints por operação, validação de alvo, limites e logs; sem proxy genérico.
- **[Provável] Dados desatualizados parecem estado atual:** mostrar timestamp, TTL, falha de atualização e ação de refresh.
- **[Provável] Alterações manuais conflitam com GitOps:** sinalizar recursos controlados pelo Argo CD e indicar que a fonte Git é a origem desejada.
- **[Provável] CRDs ampliam o escopo sem limite:** leitura genérica controlada ou suporte explícito por extensão; não prometer edição universal.
- **[Provável] Escala de polling afeta o API server:** limitar frequência, cachear leituras, usar paginação e adotar watches apenas após medir a necessidade.
- **[Provável] Charts podem criar ou excluir muitos objetos:** mostrar versão, namespace, diff e resumo antes de install/upgrade/uninstall; avisar sobre CRDs e recursos compartilhados.

## 12. Próximo passo recomendado

[Provável] Fazer a Fase 0 e fechar quatro decisões antes de implementar telas: cluster local alvo, regra para reconhecer o contexto permitido, escopo inicial de CRUD e significado de “editar charts”. Em paralelo, validar KubernetesClient contra um cluster descartável e produzir wireframes das duas árvores.

## Referências oficiais

- [Certeza] [ASP.NET Core Blazor: segurança](https://learn.microsoft.com/aspnet/core/blazor/security/)
- [Certeza] [ASP.NET Core Blazor: modos de renderização](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes)
- [Certeza] [Kubernetes C# Client](https://kubernetes-client.github.io/csharp/)
- [Certeza] [Kubernetes: RBAC](https://kubernetes.io/docs/reference/access-authn-authz/rbac/)
- [Certeza] [Kubernetes: boas práticas de RBAC](https://kubernetes.io/docs/concepts/security/rbac-good-practices/)
- [Certeza] [Argo CD: API Docs](https://argo-cd.readthedocs.io/en/stable/developer-guide/api-docs/)
