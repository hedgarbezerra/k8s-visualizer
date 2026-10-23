# Plano de produto e implementação: Kubernetes Visualizer

**Status:** plano ativo; decisões de produto consolidadas em 22/10/2026
**Base:** `plano-dashboard-kubernetes-blazor.md` e padrões observados em `AwsLocalStackVisualizer`  
**Objetivo:** definir uma interface pessoal e simples para observar e administrar recursos Kubernetes por uma aplicação Blazor/.NET.

## 1. Decisões de produto já estabelecidas

- A ferramenta é para uso pessoal; não haverá autenticação de usuário, sistema próprio de permissões nem auditoria de operações.
- O app poderá conectar-se a um cluster Kubernetes local ou a outro cluster acessível pela rede, usando contexto e credenciais configurados no kubeconfig do usuário.
- A intenção é visualizar, criar, editar e excluir qualquer recurso que a API do cluster exponha, incluindo recursos customizados (CRDs), respeitadas as capacidades do cliente .NET e as permissões da identidade usada no kubeconfig.
- A API Kubernetes será acessada pelo Kubernetes Client para .NET. O app não deve depender de comandos `kubectl` para ler ou alterar objetos Kubernetes.
- A confirmação do usuário será exigida antes de mutações. Para exclusões e alterações com potencial de impacto amplo, o diálogo deve identificar claramente cluster, escopo, tipo e nome do recurso e resumir o efeito esperado.
- A navegação principal dos recursos será uma árvore horizontal, com expansão sob demanda e uma alternativa de lista/busca para clusters e namespaces grandes.
- O app terá uma página Wiki/Suporte com documentação Kubernetes em arquivos Markdown versionados no repositório. Tooltips dos recursos explicarão o conceito e apontarão para a seção correspondente da Wiki.
- Configurar múltiplas conexões (nome, caminho do kubeconfig e contexto) e conectá-las/desconectá-las será responsabilidade da página Configurações. A barra lateral, no rodapé, apenas alternará entre conexões já abertas e seus namespaces; não terá fluxo de cadastro ou conexão.
- A página Recursos concentrará Pods e os demais tipos anunciados pelo cluster; não haverá uma aba/rota principal exclusiva para Pods.
- O app não presumirá que a API Kubernetes consiga enumerar clusters externos; a descoberta de conexões parte dos kubeconfigs informados pelo usuário.
- O projeto `AwsLocalStackVisualizer` será referência de organização e experiência: Blazor Web App com `InteractiveServer`, composição em `Program.cs`, injeção de dependências, abstrações de serviços, modelos próprios de UI, componentes Razor por área, estados de carregamento/erro e tratamento consistente de exceções.
- Usar MudBlazor como biblioteca de componentes. Aplicar o tema de componentes com cor primária `#8336E0` e controle de tema por ícone sol/lua, iniciando no tema do sistema e alternando claro/escuro ao clicar.
- A visualização principal de relações será um grafo horizontal inspirado na Resource Tree do Argo CD, não o MudTreeView vertical padrão. Usar MudBlazor para controles, painéis, tooltips e diálogos; desenhar nós e ligações do grafo em componente Razor próprio.
- Não copiar dependências, configurações ou conceitos específicos de AWS/LocalStack. O projeto usa .NET 10 e KubernetesClient 19.0.2; isso não substitui a validação funcional contra cluster real.

### Limite importante do modelo de acesso

O app não terá autorização própria, mas isso não remove a autorização do Kubernetes. As chamadas continuam sujeitas à identidade, credenciais, RBAC e demais políticas aplicadas pelo cluster. O app não consegue conceder ações que essa identidade não pode realizar.

Como não haverá login no app, qualquer pessoa que consiga alcançar sua interface poderá tentar operar com a identidade Kubernetes carregada no servidor. A execução deve usar loopback por padrão. A conexão a um cluster remoto refere-se ao caminho do servidor até o endpoint Kubernetes; expor a interface do app a outros dispositivos da rede é uma decisão separada e deve ser uma configuração explícita.

As conexões e a última seleção ficam em SQLite. Para sobreviver à recriação de um Pod Kubernetes, `/app/App_Data` precisa de volume persistente/PVC; o volume nomeado do Compose cobre o ciclo de vida local do Compose.

## 2. Objetivo e escopo

Criar uma interface de uso pessoal para:

- Descobrir os grupos, versões e tipos de recurso anunciados pela API do cluster, incluindo CRDs.
- Navegar recursos namespaced e cluster-wide, filtrar, pesquisar, ordenar e abrir detalhes.
- Inspecionar o estado observado do recurso, manifesto YAML/JSON, metadados, status, condições, relacionamentos e eventos relacionados, quando disponíveis.
- Inspecionar logs de Pods e comparar Pods lado a lado para diagnóstico.
- Criar, editar e excluir recursos genéricos por manifesto, além de oferecer formulários específicos para tipos comuns quando eles melhorarem a experiência.
- Conectar-se a contextos Kubernetes do kubeconfig local, mostrar sempre o contexto ativo e permitir trocar de contexto.

### Fora do objetivo inicial

- Login, usuários, perfis, permissões geridas pelo app ou trilha de auditoria.
- Reconciliação GitOps, sincronização/rollback do Argo CD ou edição de repositórios Git.
- Provisionamento do cluster ou gestão do ciclo de vida da infraestrutura.
- Terminal de comandos ou proxy genérico que aceite URL/verbo Kubernetes arbitrário.
- Promessa de que todo recurso terá formulário visual próprio. O mecanismo genérico deve cobrir tipos sem editor especializado.

## 3. Modelo de descoberta e operação de recursos

### Catálogo dinâmico

- Consultar a discovery API do Kubernetes para obter grupos, versões, recursos, escopo namespaced/cluster-wide e verbos anunciados.
- Refletir a API disponível no contexto atual; não manter uma lista estática como limite do produto.
- Tratar APIs indisponíveis, tipos removidos, CRDs recém-instalados e diferenças de versão com estados de erro claros e opção de atualizar a descoberta.
- Exibir os recursos que o servidor anuncia mesmo quando a identidade atual não consegue executar uma operação; deixar a resposta `403` do cluster visível sem inferir acesso.

### Seleção de cluster e namespace

- Cadastrar várias conexões em Configurações, cada uma com nome de exibição, caminho do kubeconfig e contexto.
- Conectar/desconectar perfis em Configurações; mostrar na barra lateral inferior somente perfis já conectados e permitir alternar entre eles.
- Depois de escolher um perfil ativo na barra lateral, permitir selecionar namespace ou todos os namespaces conforme RBAC.
- Exibir o nome do contexto e endpoint configurado; o nome é um alias do kubeconfig, não uma identidade de cluster certificada pelo Kubernetes.
- Não oferecer “descobrir clusters” consultando um cluster já conectado: a API Kubernetes descreve o próprio cluster e seus recursos, não uma lista universal de outros clusters.
- Após selecionar o contexto e validar a conexão, carregar Namespaces e permitir selecionar namespace. Incluir a opção “todos os namespaces” apenas quando a identidade puder listar objetos nessa amplitude.
- Manter recursos cluster-wide em uma raiz própria, sem forçá-los para dentro de um namespace.
- Ao trocar contexto, limpar estado, catálogo, seleção de namespace e cache antes de carregar os dados do novo cluster.

### Listagem, detalhes e “describe” visual

- Listar objetos por tipo e escopo com pesquisa, filtros por namespace, nome e labels, ordenação e paginação quando suportadas.
- A página do recurso deve combinar: resumo de `metadata`, manifesto atual, status/condições, owner references, relacionamentos conhecidos, eventos relacionados e timestamps relevantes.
- A apresentação “descrever recurso” deve ser construída a partir da API Kubernetes e dos objetos/eventos relacionados. Não depender da saída textual de `kubectl describe`.
- Para Pods, incluir containers, imagens, estado, reinícios, Node, eventos e logs com seleção de container e limites de saída.
- Comparação de Pods: selecionar dois ou mais Pods e comparar campos úteis (labels, Node, imagens, condições, containers, reinícios e configuração declarada), com diferenças destacadas. Definir limite inicial de quantidade para manter a leitura prática.
- YAML/JSON representa o objeto retornado pela API; deixar claro que campos calculados/geridos pelo servidor podem diferir do manifesto originalmente aplicado.

### Árvore horizontal e ajuda contextual

- Apresentar uma Resource Tree em formato de grafo horizontal inspirado no Argo CD: nós de recursos, ligações entre relações conhecidas, indicadores de estado e painel de detalhes ao selecionar um nó.
- Montar relações a partir de `ownerReferences` e relações tipadas conhecidas; não inventar arestas quando a API não demonstra vínculo.
- No navegador global, organizar `Contexto/Cluster → Namespace → Recursos namespaced` e `Contexto/Cluster → Recursos cluster-wide`. A visualização é um grafo: ownerReferences formam relações de propriedade; selectors e referências tipadas aparecem como arestas próprias, sem converter associações em hierarquia. Exemplo: Deployment → ReplicaSet → Pod por ownerReferences; Service → Pod por selector/labels.
- Carregar os recursos do namespace selecionado e recursos cluster-wide; não consultar recursos namespaced dos demais namespaces na primeira renderização. Oferecer busca/filtro por tipo e nome, zoom/ajuste à tela e opção de recolher ramos.
- Permitir alternar para lista tabular com busca/filtros, pois grafos muito largos podem ser difíceis de usar em telas menores.
- Cada tipo de recurso conhecido deve ter tooltip curto explicando sua finalidade e link para uma âncora da Wiki. O tooltip deve funcionar com mouse e teclado e não pode ser a única forma de acessar a explicação.
- Recursos desconhecidos/CRDs devem mostrar tooltip genérico com `apiGroup`, versão e escopo descobertos e link para a Wiki sobre CRDs; a UI não deve inventar uma descrição específica.
- Documentar os principais conceitos usados pela interface (Namespace, Pod, Deployment, Service, Node, ConfigMap, Secret, Ingress/Gateway, PersistentVolume/Claim, Job/CronJob, RBAC, CRD, eventos e status) em arquivos Markdown locais.
- A página Wiki/Suporte deve navegar/renderizar esses arquivos e expor links estáveis por seção. Manter textos de tooltip e documentação alinhados por uma chave estável de tipo (`apiGroup` + `kind`), com fallback para kind conhecido em APIs versionadas.

### Criar, editar e excluir

- Operação genérica por editor YAML/JSON: validar sintaxe e estrutura, identificar tipo e namespace, mostrar o contexto, fazer preview/diff quando possível e pedir confirmação antes de aplicar.
- Para editar, carregar a versão atual, destacar as alterações e lidar com conflitos de `resourceVersion` sem sobrescrever silenciosamente mudanças concorrentes.
- Para criar, validar se o tipo existe no catálogo e se o escopo/namespace informado é coerente.
- Para excluir, mostrar o alvo completo e o efeito esperado; para recursos cluster-wide ou de grande impacto, exigir confirmação reforçada digitando o nome do recurso.
- Mensagens de sucesso só após confirmação da resposta da API. Diferenciar `403`, `404`, conflito, validação, timeout e falha de conectividade.
- Ações específicas comuns (por exemplo, escalar Deployment, rollout restart, excluir Pod) podem ganhar controles próprios, mas devem chamar operações tipadas do cliente .NET e explicar o que muda.
- Manter os recursos Secret visíveis como recursos Kubernetes, mas nunca mostrar seus valores por padrão. Se edição de Secret for suportada, sinalizar explicitamente o conteúdo sensível antes de revelar ou aplicar valores.

### Capacidades da API e limites do cliente

- A cobertura genérica depende de operações suportadas pelo Kubernetes Client .NET para recursos dinâmicos e APIs customizadas. Antes de fechar a arquitetura, fazer uma prova técnica pequena com um recurso nativo e um CRD: descoberta, listagem, leitura, criação/patch e exclusão.
- Quando a SDK não expuser uma operação dinâmica necessária, registrar a limitação e avaliar o mecanismo tipado/CustomObjects apropriado. Não substituir silenciosamente por execução de shell ou por uma API HTTP arbitrária montada com dados livres.
- Fazer chamadas respeitando o escopo e a versão de API descobertos. Não assumir que todos os clusters oferecem os mesmos tipos ou versões.

## 4. Conexão e contexto Kubernetes

- Ler contextos do kubeconfig local e permitir selecionar o contexto ativo na interface.
- Mostrar de forma persistente nome do contexto, endpoint/cluster, usuário configurado (sem expor credenciais) e estado da conexão.
- Permitir atualizar o catálogo e o estado do cluster após troca de contexto.
- Isolar clientes/cache por contexto e descartar ou invalidar dados ao trocar de cluster, para não misturar resultados.
- Exibir indisponibilidade, timeout, erro de certificado e erro de credencial sem encerrar a aplicação.
- Trocas de conexão e namespace emitem evento global versionado; as páginas devem descartar resultados antigos e atualizar consultas. A troca de idioma também emite evento global e recarrega o circuito para aplicar a cultura.

## 5. Arquitetura e padrão de referência

Começar como monólito modular Blazor Web App com renderização interativa no servidor. O servidor usa o kubeconfig local e chama o Kubernetes Client .NET; componentes Razor chamam serviços de aplicação por DI. Evitar uma API pública separada no MVP, a menos que a implementação revele uma necessidade concreta.

```text
Navegador local
   │ circuito Blazor Interactive Server
   ▼
Componentes Razor por área
   │ serviços de aplicação injetados
   ▼
Abstrações Kubernetes ──► adaptador Kubernetes Client .NET
   │                           │
   ├── catálogo/discovery       └── API do contexto escolhido no kubeconfig
   ├── recursos genéricos
   ├── Pods/logs/eventos
   └── operações tipadas
```

### Organização sugerida

Seguir a estrutura compreensível do projeto de referência sem fragmentar cedo em muitos projetos. Base inicial de pastas:

```text
repo/
  .github/
    workflows/
      publish-image.yml          # build e push da imagem ao GHCR
  src/
    docs/
      kubernetes-wiki/             # artigos Markdown da Wiki/Suporte
      persistencia-conexoes.md
    Components/
      Layout/                     # shell e navegação
      Pages/                      # Dashboard, Resources, Wiki, Settings
      Shared/                     # seletores, árvore, estados e diálogos
    Models/UI/                      # modelos de apresentação e grafo
    Services/                       # integração Kubernetes, SQLite e cultura
    Models/UI/                    # perfis, namespace, recursos e grafo
    Resources/                      # recursos .resx PT-BR e Inglês
    wwwroot/                        # CSS e assets
    App_Data/                       # SQLite e chaves Data Protection (persistir em volume)
    Program.cs
    KubernetesVisualizer.csproj
    Dockerfile                      # build multi-stage
    .dockerignore
    compose.yaml
  plano-dashboard-kubernetes-blazor.md     # plano inicial de referência
  plano-dashboard-kubernetes-blazor-v2.md  # plano vigente
```

- Preservar interfaces/serviços/modelos para manter componentes desacoplados da SDK, como no projeto AWS de referência.
- Registrar clientes Kubernetes e serviços no `Program.cs` por DI, respeitando ciclo de vida, contexto selecionado e credenciais.
- Usar modelos próprios para UI onde isso evite acoplar componentes a tipos gerados pela SDK; manter o objeto bruto acessível ao editor genérico.
- Reaproveitar os padrões de organização, estados de carregamento/erro e tratamento global do projeto de referência; usar MudBlazor no sistema visual.
- Usar SQLite para perfis de conexão e última conexão/namespace selecionados. Não persistir objetos do cluster como banco de dados da aplicação.
- Não criar projetos `Domain`, `Application` e `Infrastructure` separados no primeiro corte sem necessidade demonstrada; pastas/serviços são suficientes para validar o produto.

## 6. Segurança e salvaguardas proporcionais ao uso pessoal

Este plano aceita explicitamente ausência de autenticação própria, autorização própria e auditoria. Permanecem as proteções contra acidentes e exposição involuntária que não criam esses sistemas:

- Loopback por padrão; escuta em interface de rede só por configuração intencional.
- Exibir cluster/contexto ativo próximo às ações, e repetir o alvo no diálogo de confirmação.
- Confirmação para toda mutação; confirmação digitada para exclusões de alto impacto.
- Sem comandos shell construídos a partir de entrada do usuário.
- Não registrar kubeconfig, tokens, valores de Secret, conteúdo de logs ou manifestos completos em logs técnicos.
- Limitar saída de logs de Pods e mascarar dados sensíveis em visualizações quando reconhecíveis.
- Usar permissões e identidade já configuradas no kubeconfig. O app não cria credenciais nem eleva privilégios.

## 7. Permissões Kubernetes e responsabilidades de acesso

O app não implementa um permissionamento próprio. A autorização é feita pelo Kubernetes para cada chamada usando a identidade do contexto selecionado. Na prática, é possível preparar perfis RBAC diferentes para leitura, operação em namespaces escolhidos ou administração ampla.

| Perfil de uso | RBAC típico | O que permite |
|---|---|---|
| Leitura de namespaces selecionados | `Role` + `RoleBinding` por namespace | Listar e ler os recursos permitidos somente nos namespaces ligados. |
| Leitura em todo o cluster | `ClusterRole` + `ClusterRoleBinding` | Ler recursos namespaced em todos os namespaces e recursos cluster-wide incluídos nas regras. |
| Gerenciamento de namespaces selecionados | `Role` + `RoleBinding` com verbos de escrita nos recursos necessários | Criar, alterar e excluir os tipos permitidos nos namespaces ligados. |
| Gerenciamento amplo | `ClusterRole` + `ClusterRoleBinding` com regras amplas | Gerenciar recursos namespaced em todo o cluster e os recursos cluster-wide concedidos. |
| Controle total | `cluster-admin` | Acesso irrestrito ao cluster; usar apenas se o usuário realmente quiser esse nível de poder. |

### Regras RBAC que afetam as funções da UI

- Verbos comuns são `get`, `list`, `watch`, `create`, `update`, `patch` e `delete`. Não presumir que leitura concede escrita ou que `update` concede `patch`.
- Para visualizar logs de Pods, a identidade precisa acesso ao subrecurso `pods/log`; listar Pods por si só pode não permitir ler logs.
- A leitura da discovery API informa os tipos servidos pelo cluster, mas não prova que a identidade tenha permissão para listar ou alterar cada tipo.
- Namespaces, Nodes, PersistentVolumes e outros recursos cluster-wide exigem regras que cubram esses recursos; um Role limitado a namespace não basta.
- Para recursos customizados, as regras precisam incluir o API group e o nome do recurso do CRD. Um perfil genérico que usa curingas pode passar a conceder acesso a novos recursos no futuro.
- Segredos merecem escolha explícita: leitura de `secrets` expõe credenciais armazenadas no cluster. A UI pode ocultar valores, mas ocultação visual não substitui RBAC.
- A UI deve mostrar erros `403` por tipo/operação e continuar permitindo as demais áreas autorizadas.
- O app pode oferecer uma tela informativa de “o que consigo acessar” por meio de tentativas de leitura/`SelfSubjectAccessReview`, caso suportado pela identidade; ela não pode aumentar as permissões existentes.

### De onde vêm as credenciais

- **Execução local:** usar o contexto/credencial selecionado no kubeconfig. A identidade Kubernetes autenticada recebe os direitos definidos pelo cluster.
- **Execução em Pod:** usar a ServiceAccount do workload e conceder as regras com `RoleBinding`/`ClusterRoleBinding` conforme o escopo desejado.
- **Container local falando com cluster remoto:** disponibilizar kubeconfig/credenciais ao container por montagem ou mecanismo externo; não copiar segredos para a imagem nem para o repositório. Plugins de autenticação externos ao kubeconfig também precisam existir e funcionar no ambiente do container.
- O usuário pode escolher o contexto menos privilegiado que ainda cubra suas tarefas; uma ferramenta pessoal não exige `cluster-admin` por definição.

## 8. Empacotamento Docker e publicação da imagem

O repositório deve conter um `Dockerfile` multi-stage para restaurar/publicar a aplicação .NET e executar a imagem final com runtime ASP.NET Core, usuário não-root quando viável, porta documentada e sem kubeconfig embutido.

O workflow GitHub Actions em `.github/workflows/publish-image.yml` deve:

1. Disparar em push na branch principal e em tags de release, além de permitir execução manual.
2. Fazer checkout do repositório, configurar Buildx e autenticar no `ghcr.io` com `GITHUB_TOKEN` e permissão mínima `packages: write` (e `contents: read`).
3. Construir usando `src/Dockerfile`, publicar tags de branch, tag de release e SHA. Não publicar `latest` até existir uma política de release explícita.
4. Opcionalmente gerar metadados/cache e SBOM/attestations em uma etapa posterior.
5. Falhar se build ou push falhar e não executar deploy do cluster como parte dessa automação.

O pacote GHCR deve ser associado ao repositório; na primeira publicação, confirmar a visibilidade desejada. Publicar imagem é responsabilidade do workflow; selecionar contexto Kubernetes e operar recursos continua responsabilidade do app em execução.

### Execução containerizada e acesso ao cluster

- A imagem não inclui credenciais Kubernetes. Em execução local, montar kubeconfig em modo somente leitura e configurar o caminho; proteger o arquivo no host.
- Para autenticação de cluster via plugin `exec`, instalar no container o plugin/binário requerido ou escolher outro mecanismo compatível; kubeconfig sozinho pode apenas referenciar um executável externo.
- Acesso de rede do container ao API server deve ser validado para cada tipo de cluster (Docker Desktop, kind, minikube ou endpoint remoto); endereços como `localhost` dentro do container podem não apontar para o host.
- Para implantação dentro do próprio cluster, usar autenticação in-cluster com ServiceAccount em vez de montar kubeconfig de uma pessoa.

## 9. Separação de responsabilidades por tecnologia

| Parte | Responsabilidade | Não é responsabilidade dela |
|---|---|---|
| Blazor Web App (`InteractiveServer`) | Páginas/componentes, seleção de namespace/contexto, filtros, editor YAML, diálogos de confirmação, visualização/comparação e estados de carregamento/erro. | Conceder acesso Kubernetes, armazenar permissões próprias ou publicar imagem Docker. |
| Serviços .NET da aplicação | Orquestrar casos de uso, validar manifestos/escopos, construir preview/diff, escolher o adaptador correto, mapear erros e chamar operações do cliente Kubernetes. | Substituir a autorização do API server ou prometer CRUD para APIs que a SDK não consiga chamar. |
| Kubernetes Client .NET | Carregar configuração/credenciais, enviar chamadas à API Kubernetes, serializar objetos e oferecer operações tipadas/dinâmicas conforme cobertura da biblioteca. | Decidir se a identidade tem RBAC, criar telas, fazer build de container ou publicar no GHCR. |
| API Kubernetes e RBAC | Discovery, validação/admissão, armazenamento/estado observado e autorização por identidade, recurso, escopo e verbo. | Fornecer a interface do usuário ou construir o app. |
| Wiki local em Markdown | Guardar fundamentos Kubernetes versionados e acessíveis offline na aplicação; fornecer páginas e âncoras referenciadas pela UI. | Ser fonte de verdade do estado real do cluster ou descrever automaticamente CRDs desconhecidos. |
| MudBlazor | Fornecer componentes Blazor para formulários, diálogos, tooltips, tabelas e controles gerais, tematizados com claro/escuro e cor primária `#8336E0`. | Conceder acesso Kubernetes ou fornecer automaticamente a Resource Tree horizontal do Argo CD. |
| Componente ResourceGraph próprio | Desenhar o grafo horizontal com nós/arestas, relações, estado, seleção, zoom e carregamento incremental. | Determinar autorização Kubernetes ou inferir relações sem dados suficientes. |
| Dockerfile | Definir como compilar/publicar a aplicação e montar a imagem executável reproduzível. | Conceder acesso ao cluster ou publicar a imagem no registry por conta própria. |
| GitHub Actions | Executar checkout/build e autenticar para publicar tags da imagem no GHCR. | Fazer CRUD de recursos Kubernetes ou conceder RBAC ao app. |

**Nota de arquitetura:** o plano usa Blazor Web App com componentes Razor e serviços .NET; não propõe ASP.NET MVC Controllers no MVP. O protocolo HTTP com o cluster fica encapsulado no cliente Kubernetes, não em controladores MVC criados apenas para repetir essa API.

**Decisão visual:** usar MudBlazor para controles comuns e criar a Resource Tree horizontal como componente próprio. Definir `#8336E0` como cor primária nos temas claro e escuro. Oferecer `Claro`, `Escuro` e `Sistema` (padrão inicial), persistindo a preferência localmente. O grafo deve ter contraste e cores de status distintos da cor primária.

## 10. Fases propostas

### Fase 0 — validar a integração dinâmica

1. Inspecionar solução/repositório e convenções atuais; confirmar versão do .NET e ambiente de execução.
2. Conexão via kubeconfig, discovery e listagem dinâmica estão implementadas; ainda falta validar em cluster real com recurso nativo e CRD.
3. Troca de contexto e evento global estão implementados; validar a atualização em execução e os erros comuns.
4. Lista e primeira árvore horizontal estão implementadas; detalhes/“describe” e comparação de Pods continuam pendentes.

**Pronto quando:** build estável e operações dinâmicas de leitura/escrita de um recurso nativo e um CRD forem verificadas em cluster real.

### Fase 1 — host e conexão

1. Criar host Blazor `InteractiveServer`, instalar/configurar MudBlazor e layout simples, sem login.
2. Registrar cliente/serviços Kubernetes por DI.
3. Implementar cadastro/conexão de múltiplos perfis em Configurações e seletores de conexões ativas/namespaces na parte inferior da navegação lateral.
4. Configurar loopback por padrão e documentar a opção de rede.

**Pronto quando:** o app inicia sem cluster, comunica falha de conexão e conecta ao contexto selecionado sem expor credenciais ao navegador.

### Fase 2 — descoberta, navegação e leitura

1. Implementar catálogo dinâmico de grupos e recursos.
2. Separar navegação namespaced e cluster-wide.
3. Implementar Resource Tree em grafo horizontal inspirado no Argo CD: contexto do cluster e namespace como raízes, recursos cluster-wide no escopo do cluster, colunas por relações de propriedade e arestas laterais para selectors/referências; manter alternativa em lista.
4. Implementar listagem genérica com filtros e detalhes do recurso.
5. Implementar manifesto YAML/JSON e visão “describe” com status, condições, relações e eventos disponíveis.
6. Criar Wiki local em Markdown e ligar tooltips de recursos às âncoras correspondentes.
7. Adicionar navegação e atualização do catálogo ao trocar contexto.

**Pronto quando:** tipos nativos e CRDs descobertos podem ser navegados e inspecionados sem depender de páginas específicas para cada tipo.

### Fase 3 — Pods para diagnóstico

1. Implementar detalhe de Pod, containers e eventos.
2. Implementar leitura limitada de logs com seleção de container.
3. Implementar comparação de dois ou mais Pods com diferenças destacadas.

**Pronto quando:** erros de eventos ou logs não escondem os demais dados do Pod, e a comparação mostra os campos efetivamente distintos.

### Fase 4 — mutações genéricas e específicas

1. Implementar criar recurso por YAML/JSON com validação e preview.
2. Implementar edição com diff e controle de conflito.
3. Implementar exclusão com resumo de alvo e confirmação reforçada para alto impacto.
4. Adicionar formulários/ações dedicados para operações frequentes (ex.: escala, rollout restart) quando forem mais simples que YAML.

**Pronto quando:** as operações suportadas funcionam para recurso nativo e CRD de prova, respeitam as respostas/limites do cluster e nunca aplicam mutação sem confirmação explícita.

### Fase 5 — refinamentos

- Visão geral do cluster e indicadores suportados pela API.
- Cache curto, paginação, cancelamento e controle de concorrência conforme necessidade observada.
- Melhorias de navegação por labels, relacionamentos e árvore de recursos.
- Avaliar integração Helm/Argo CD separadamente, sem misturá-la à promessa de CRUD genérico da API Kubernetes.

## 11. Critérios transversais de aceite

- Contexto e endpoint do cluster ficam visíveis durante navegação e confirmação de mutações.
- O usuário cadastra e conecta perfis em Configurações; a barra lateral inferior alterna entre os perfis conectados e seleciona namespace; a opção “todos” respeita RBAC.
- A árvore de recursos é horizontal, carrega ramos sob demanda e oferece lista/busca como alternativa.
- A Resource Tree desenha somente relações identificadas e permite selecionar um nó para abrir detalhes.
- MudBlazor usa a cor primária `#8336E0`; tema claro, escuro e sistema funcionam e a escolha persiste entre sessões.
- Tipos conhecidos mostram tooltip acessível com explicação e link funcional para a Wiki; CRDs desconhecidos mostram explicação genérica sem descrição inventada.
- A Wiki/Suporte usa documentação Markdown incluída no repositório e pode ser exibida sem depender de rede externa.
- Layout e componentes usam MudBlazor no MVP e permanecem utilizáveis em larguras de tela suportadas.
- Catálogo respeita discovery e escopo namespaced/cluster-wide do cluster selecionado.
- A camada de UI não depende diretamente do cliente Kubernetes.
- Leitura e mutações comunicam estados de carregamento, vazio, indisponibilidade, erro de autorização do cluster e conflito.
- Dados de um contexto não aparecem após a troca para outro contexto.
- Objetos desconhecidos/CRDs continuam acessíveis pelo fluxo genérico sempre que a SDK oferecer as operações necessárias.
- O usuário consegue inspecionar estado observado, manifesto, eventos relacionados e logs de Pod com limites explícitos.
- Toda mutação apresenta alvo e resumo antes de ser enviada; exclusões de alto impacto exigem confirmação reforçada.
- Nenhum token ou conteúdo de Secret é exposto em logs técnicos.
- O app permanece utilizável sem cluster conectado.
- Dockerfile constrói a imagem em ambiente limpo, sem exigir kubeconfig durante build.
- GitHub Actions publica a imagem no GHCR usando `GITHUB_TOKEN` e tags documentadas, sem credenciais Kubernetes no workflow.

## 12. Decisões validadas e limites ainda por verificar

| Decisão | Sugestão inicial |
|---|---|
| Identidade e autorização | Validado: sem autenticação/autorização/auditoria próprias; cada ação depende do kubeconfig e RBAC Kubernetes. |
| Conexões e seleção | Validado: múltiplos perfis SQLite em Configurações; seletor inferior alterna conexão conectada e namespace; restaurar última seleção. |
| Persistência em container | Validado: Compose usa volume nomeado; em Kubernetes, montar PVC em `/app/App_Data` para persistir ao substituir Pod. |
| Recursos | Validado: catálogo/lista dinâmica cobre recursos anunciados e CRDs, condicionada por discovery, SDK e RBAC; manter a lista e acrescentar árvore na Dashboard. |
| Relações da árvore | Validado: usar `ownerReferences` e relações tipadas implementadas, sem inferir vínculos. Carregar o escopo/ramo quando solicitado. |
| Idioma | Validado: PT-BR padrão e Inglês, arquivos `.resx`, seletor em Configurações e evento global. |
| Visual | Validado: MudBlazor, primária `#8336E0`, tema Claro/Escuro/Sistema com ícone sol/lua. |
| Confirmação | Validado: confirmar mutações; exclusões de alto impacto exigem confirmação reforçada e alvo explícito. |
| Secrets | Valores ocultos por padrão; revelar/editar deve ser explícito. A ocultação visual não substitui RBAC. |
| Docker/GHCR | Dockerfile e workflow GHCR fazem parte do escopo; workflow usa `GITHUB_TOKEN`. Publicação real após configurar e disparar o workflow. |
| Dependente de ambiente | Validar kubeconfig, plugins exec, conectividade local/remota, RBAC parcial e operações nativo/CRD contra cluster real. |

## 13. Próximo passo

As decisões de produto estão consolidadas. A primeira versão da árvore horizontal da Dashboard e o workflow de build/publicação GHCR estão implementados. Próximos itens: validar árvore no navegador e em cluster real, completar relações/detalhes, Wiki e operações de leitura/escrita; não publicar imagem nem fazer deploy sem a configuração do pacote e o acionamento do workflow.

## 14. Checklist de execução por feature

Esta checklist acompanha o plano; não substitui as explicações, decisões ou critérios das seções anteriores. Marcar cada caixa somente depois de concluir e verificar a ação correspondente. Se uma ação exigir mudança de escopo ou uma dependência indisponível, parar nessa ação e registrar o bloqueio antes de seguir.

**Estado em 23/10/2026:** a solução está em `src/KubernetesVisualizer.slnx` e o projeto Blazor em `src/KubernetesVisualizer.csproj`. Shell MudBlazor, tema claro/escuro por ícone sol/lua, preferência inicial pelo tema do sistema e persistência em `localStorage` estão implementados. A interface tem recursos `.resx` PT-BR/EN, idioma padrão PT-BR e seletor persistido em cookie próprio em Configurações; a troca emite evento global e recarrega o circuito para atualizar a cultura. As conexões são cadastradas em Configurações (nome, caminho de kubeconfig e contexto) e persistidas em `src/App_Data/kubernetes-visualizer.db`; a última conexão e o namespace são restaurados após reinício. O Compose monta volume nomeado em `App_Data` e kubeconfig do host somente para leitura, com UI limitada a loopback. Recursos consulta discovery, lista tipos específicos ou “Todos” com concorrência limitada e trata falhas parciais; Pods aparecem no catálogo. Eventos globais versionados atualizam as listagens na troca de cluster/namespace. A Dashboard usa grafo horizontal com raízes Contexto/Cluster → Namespace e recursos cluster-wide. A hierarquia principal usa ownerReferences (controlador preferencial); Services, Ingress/Gateway, HPA/VPA, políticas, agendamento e referências são arestas laterais tipadas. Componentes relacionados são agrupados e ordenados por vizinhança entre colunas, sem usar associações compartilhadas como estrutura principal. Pods expandem containers, init/ephemeral containers e volumes como composição. Um filtro localiza recursos em 12 grupos funcionais sem transformá-los em pais no grafo; os ramos de ownership/composição recolhem e expandem com botões funcionais. Ao selecionar um nó, um painel lateral mostra metadados e recursos relacionados, e o grafo realça os nós e arestas relacionados. Há zoom/reset e acesso à lista de Recursos. Workflow GHCR configurado para `master`, tags `v*` e execução manual; publica branch/tag/SHA por `GITHUB_TOKEN`, sem tag `latest`. Permanecem: validação visual no navegador e contra cluster, YAML/JSON observado, condições/eventos, Wiki Markdown dinâmica, CRUD e logs/comparação de Pods. A compilação final em diretório de saída separado concluiu com 0 avisos e 0 erros; o diretório de saída padrão estava bloqueado por um processo que mantém os binários em uso, então não encerrei esse processo. Nenhuma conexão real a cluster foi validada.

**Atualização de UI e detalhes:** filtros do dashboard e da lista, tipo selecionado, Grupo funcional, busca, origem estimada, zoom, ramos recolhidos e abertura do menu lateral são persistidos em `localStorage`; idioma e tema mantêm seus mecanismos próprios. A lista abre o detalhe em um clique; no dashboard o primeiro clique seleciona/destaca e o segundo abre o painel. YAML observado é consultado sob demanda, com Secret em resposta parcial. “Sistema”, “controlado por ownerReference” e “sem owner controller” são categorias heurísticas, pois a API não tem um marcador universal de autoria.

### A. Base do repositório e aplicação

- [x] Criar solução e projeto Blazor Web App em `src`.
- [x] Confirmar target framework suportado e modo `InteractiveServer`, sem autenticação.
- [x] Adicionar MudBlazor e registrar os serviços/providers necessários no host.
- [x] Criar layout inicial com navegação para Dashboard, Recursos, Wiki e Configurações; Pods são acessados dentro de Recursos.
- [ ] Verificar por HTTP que a aplicação renderiza a rota inicial e as páginas da navegação.

### B. Tema e identidade visual

- [x] Definir tema claro MudBlazor com primária `#8336E0`.
- [x] Definir tema escuro MudBlazor com primária `#8336E0`.
- [x] Criar controle por ícone sol/lua; iniciar seguindo o sistema e alternar entre Claro e Escuro ao clicar.
- [x] Persistir a preferência de tema em `localStorage` e restaurá-la após reinício.
- [ ] Verificar o seletor no navegador, reação à mudança do tema do sistema, foco por teclado e contraste nas páginas.
- [ ] Verificar foco por teclado, contraste básico e uso dos temas nas páginas principais.

### C. Kubeconfig, conexões, cluster e namespace

- [x] Implementar cadastro persistente de múltiplas conexões por nome, caminho de kubeconfig e contexto, sem copiar credenciais para o armazenamento do app.
- [x] Permitir carregar contextos a partir do caminho de kubeconfig configurado e salvar cada conexão.
- [x] Implementar conexão/desconexão por perfil em Configurações via Kubernetes Client .NET.
- [x] Mostrar no rodapé da barra lateral os clusters conectados e o seletor de namespace; trocar a seleção ativa sem configurar conexões ali.
- [x] Carregar namespaces após conexão e permitir selecionar namespace ou “todos”; se RBAC negar a listagem, manter conexão e informar o usuário.
- [x] Limpar seleção e estado visível ao trocar a conexão ativa; manter clientes isolados por perfil dentro da sessão.
- [x] Emitir evento global versionado em mudança de conexão ativa/namespace para que as páginas de listagem atualizem seus dados.
- [x] Persistir perfis em SQLite e importar uma única vez o antigo `App_Data/connections.json`.
- [x] Persistir a última conexão ativa e o namespace e restaurá-los automaticamente após reinício da aplicação/container com armazenamento persistente; não reconectar quando a última conexão foi explicitamente desconectada sem fallback.
- [ ] Verificar conexão com um cluster de desenvolvimento e comportamento sem cluster.

### D. Discovery, navegação e Resource Tree

- [x] Consultar discovery API e montar catálogo dinâmico de grupos, versões, recursos listáveis e escopos.
- [x] Criar listagem por tipo para recursos namespaced e cluster-wide, com busca por nome/namespace.
- [x] Adicionar opção “Todos os recursos”, agregando os tipos descobertos com concorrência limitada e aviso para tipos sem acesso/listagem.
- [ ] Validar a agregação “Todos os recursos” em cluster real e namespaces distintos, incluindo RBAC parcial e volumes grandes de objetos.
- [x] Criar grafo horizontal com contexto de cluster e namespace, recursos cluster-wide descobertos, colunas por profundidade, cartões e conectores SVG direcionais.
- [x] Classificar tipos descobertos em 12 grupos funcionais para localização/filtro; manter grupos fora da hierarquia de ownership.
- [x] Filtrar tipos visíveis no dashboard por seleção múltipla.
- [x] Oferecer filtros para ocultar recursos de sistema, recursos com ownerReferences e recursos sem owner controlador.
- [x] Persistir Grupo funcional, tipos visíveis, filtros de origem, zoom e ramos recolhidos no navegador.
- [x] Derivar Deployment → ReplicaSet → Pod por `ownerReferences`; desenhar Service → Pod como aresta lateral derivada de selector/labels, sem reparentear Deployment.
- [x] Desenhar arestas tipadas para Service/EndpointSlice/Ingress/Gateway API, HPA/VPA, seletores de NetworkPolicy/PDB, Pod/Node/Container/Volume, PVC/PV/StorageClass/CSI, configuração, ServiceAccount/RBAC, Events e CRD → Custom Resource quando os destinos estão carregados.
- [x] Carregar árvore sob demanda quando um namespace é escolhido; limitar cada consulta ao namespace ativo.
- [x] Implementar recolhimento e expansão em ramos de ownership e composição; expor containers, init/ephemeral containers e volumes dentro do ramo de Pod.
- [x] Corrigir a identificação do nó pai no recolhimento para que o botão “−” oculte descendentes e “+” os restaure.
- [x] Agrupar componentes relacionados em blocos verticais e ordenar cada coluna por proximidade das relações, mantendo relações compartilhadas fora do layout estrutural.
- [x] Implementar zoom e redefinição de zoom.
- [x] No dashboard, primeiro clique seleciona e destaca; segundo clique abre o painel lateral com tipo, versão, namespace/escopo, status, labels e annotations (sem annotations de Secret).
- [x] Na lista de Recursos, selecionar o nome abre painel lateral no primeiro clique, com metadados disponíveis.
- [x] Disponibilizar visualização do YAML observado nos dois painéis de detalhes, sem expor os valores de Secret.
- [ ] Restringir o destaque ao selecionado e às relações diretas de ownership/agrupamento, dependência ou controle; impedir expansão transitiva por selectors e vizinhos de vizinhos.
- [x] Permitir navegar do painel lateral para recursos relacionados visíveis.
- [x] Manter a lista de Recursos como alternativa à árvore e ligar as duas páginas.
- [ ] Verificar navegação com recursos nativos e pelo menos um CRD em cluster real.

### E. Detalhes, Pods e diagnóstico

- [x] Mostrar detalhes laterais na lista de Recursos e, após segundo clique, no dashboard.
- [x] Mostrar YAML observado sob demanda; Secrets usam resposta parcial de metadados.
- [x] Persistir filtros e busca da lista de Recursos no navegador.
- [ ] Completar detalhes de recurso com YAML/JSON, status, condições e eventos relacionados.
- [ ] Exibir eventos relacionados quando disponíveis.
- [ ] Criar detalhes de Pod com containers, imagens, Node, reinícios e condições.
- [ ] Implementar leitura de logs com seleção de container e limite configurável.
- [ ] Implementar comparação lado a lado de Pods e realce de diferenças.
- [ ] Verificar estados de carregamento, ausência de dados, erros `403` e recurso removido.

### F. Wiki e ajuda contextual

- [ ] Integrar `src/docs/kubernetes-wiki/` como fonte Markdown versionada da página Wiki/Suporte.
- [ ] Criar navegação e âncoras estáveis para os fundamentos Kubernetes.
- [ ] Mapear tipos conhecidos (`apiGroup` + `kind`) para explicações curtas e artigos.
- [ ] Adicionar tooltip acessível com link para Wiki em cada tipo conhecido na navegação.
- [ ] Mostrar explicação genérica e link sobre CRDs para tipos desconhecidos.
- [ ] Verificar que Wiki e tooltips funcionam sem rede externa.

### G. Criar, editar e excluir por manifesto

- [ ] Implementar carregamento/edição de YAML/JSON e suporte a documentos separados por `---`.
- [ ] Validar sintaxe, GVK, escopo e namespace antes de enviar.
- [ ] Mostrar preview/diff, contexto e alvos afetados antes de mutações.
- [ ] Implementar criação genérica com Kubernetes Client .NET.
- [ ] Implementar patch/edição genérica com tratamento de conflito de versão.
- [ ] Implementar exclusão com confirmação reforçada para recursos de alto impacto.
- [ ] Verificar operações com recurso nativo e CRD; parar e registrar limites se a SDK não cobrir uma operação.

### H. Docker e publicação GHCR

- [x] Criar `src/Dockerfile` multi-stage sem incluir kubeconfig ou credenciais.
- [x] Criar `src/.dockerignore` para excluir artefatos locais e segredos.
- [x] Adicionar Compose com volume nomeado para preservar SQLite/Data Protection e bind de kubeconfig somente leitura, expondo a UI em loopback.
- [ ] Documentar configuração de kubeconfig/contexto para execução local do container.
- [x] Criar workflow `.github/workflows/publish-image.yml` com permissões mínimas, execução manual e `GITHUB_TOKEN`.
- [x] Definir tags branch/release/SHA e não emitir `latest` até existir política de release.
- [ ] Construir e publicar a primeira imagem no GHCR após confirmar visibilidade do pacote e acionar o workflow.
- [ ] Verificar build da imagem; publicar somente quando o usuário configurar/autorizar o primeiro push.

### I. Verificação integrada

- [x] Executar build da solução e corrigir erros antes de avançar para a próxima feature.
- [ ] Verificar manualmente os fluxos concluídos contra seus critérios de pronto.
- [ ] Atualizar esta checklist e registrar arquivos/limitações após cada feature concluída.

### J. Internacionalização

- [x] Criar recursos `.resx` base em PT-BR e tradução em Inglês para textos da interface.
- [x] Configurar cultura padrão PT-BR e persistir a seleção de idioma em cookie próprio; descartar cookie antigo do app para retornar ao padrão PT-BR após esta atualização.
- [x] Emitir evento global ao trocar idioma, atualizar a navegação e recarregar a aplicação para aplicar a cultura ao novo circuito Blazor.
- [x] Adicionar seletor PT-BR/Inglês em Configurações e atualizar `lang` do documento.
- [x] Localizar navegação, Dashboard, Recursos, Configurações, Wiki, tema, estados de erro e reconexão.
- [ ] Verificar troca de idioma no navegador e cobertura dos textos de todas as rotas.

## 15. Refinamentos solicitados para Dashboard e Recursos

### Factibilidade e limites confirmados

- **Linha inteira clicável na tabela:** factível com o evento de clique da linha do MudTable; o painel continua abrindo no primeiro clique.
- **Limpar tipos selecionados:** factível com ação explícita junto ao seletor; deve remover o filtro e restaurar todos os tipos.
- **Destaque apenas das relações relevantes:** factível e necessário. O realce deve limitar-se aos vizinhos diretos do selecionado, classificados como ownership/agrupamento, dependência explícita ou controle; não deve percorrer vizinhos de vizinhos. Relações por selector podem ser mostradas como ligações diretas, sem iniciar uma nova travessia.
- **Copiar YAML:** factível em navegadores com Clipboard API em contexto seguro (HTTPS/localhost), com botão junto ao topo do leitor e feedback de cópia/falha.
- **YAML originalmente enviado:** não é recuperável da API Kubernetes. A API guarda o objeto estruturado, não comentários, formatação ou a ordem original do manifesto. Para recursos existentes, é possível oferecer YAML normalizado a partir do objeto observado. Para preservar exatamente arquivos enviados pela própria aplicação no futuro, seria necessário arquivar o conteúdo original localmente antes de aplicar.
- **Fullscreen e navegação por mouse:** factíveis com Fullscreen API iniciada pelo botão e pan por arraste do canvas. A disponibilidade de fullscreen depende do navegador e do contexto seguro.
- **Ordem horizontal por níveis conceituais:** factível e alinhado à expectativa. As colunas representam níveis estruturais do contexto; os recursos ocupam posições horizontais conforme sua relação de propriedade/agrupamento, com cabeçalho que identifica os tipos encontrados naquele nível. Não se fixa uma coluna global por `Kind`, porque um Pod pode pertencer a Job, StatefulSet, DaemonSet ou ReplicaSet. Relações diretas de controle, selector e dependência permanecem como ligações cruzadas, sem alterar a hierarquia principal.

### Checklist de implementação desta rodada

- [x] Tornar toda a linha de recurso clicável e abrir o painel lateral no primeiro clique.
- [x] Adicionar botão “limpar seleção” ao filtro de tipos visíveis no dashboard.
- [x] Limitar o destaque ao item selecionado e seus relacionamentos diretos relevantes; não propagar realce por vizinhos de vizinhos.
- [x] Revisar as categorias de relação usadas no destaque para manter apenas arestas diretas não contextuais.
- [x] Adicionar botão para copiar o YAML observado no topo do leitor, com confirmação de sucesso e tratamento de Clipboard API indisponível.
- [x] Exibir YAML observado normalizado e indicar que a formatação original não pode ser recuperada do cluster.
- [x] Adicionar botão de fullscreen junto aos controles de zoom.
- [x] Permitir pan do grafo por arraste do mouse e manter zoom/controles dentro do fullscreen.
- [x] Organizar a apresentação em níveis conceituais horizontais, determinados pela profundidade estrutural, com cabeçalho dos tipos presentes e relações diretas transversais.
- [x] Conferir compilação da solução sem avisos nem erros.
- [ ] Validar visualmente no navegador as interações de tabela, seleção, filtros, cópia, fullscreen e pan; conferir hierarquias reais variadas em cluster.
