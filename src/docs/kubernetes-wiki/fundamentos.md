
# Kubernetes â€” Fundamentos e Conceitos AvanÃ§ados
## 1. VisÃ£o geral
Kubernetes Ã© uma plataforma para executar e gerenciar aplicaÃ§Ãµes containerizadas de forma declarativa.
A ideia central Ã© descrever **o estado desejado** da aplicaÃ§Ã£o e deixar o Kubernetes responsÃ¡vel por manter o ambiente nesse estado.
```mermaid
flowchart TD
    A[Estado desejado] --> B[Kubernetes]
    B --> C[Cluster]
    C --> D[Nodes]
    D --> E[Pods]
    E --> F[Containers]
```
O ponto mais importante para estudar Kubernetes Ã© separar:
- **hierarquia**: quem contÃ©m ou agrupa quem;
- **relaÃ§Ãµes**: recursos que apontam, selecionam ou controlam outros recursos.
***
# 2. Cluster
O **Cluster** Ã© o nÃ­vel mais alto da arquitetura Kubernetes.
Ele representa todo o ambiente gerenciado pelo Kubernetes.
```mermaid
flowchart TD
    C[Cluster]
    C --> CP[Control Plane]
    C --> N[Worker Nodes]
    C --> NS[Namespaces]
```
Um cluster normalmente possui:
- Control Plane
- Worker Nodes
- Namespaces
- workloads
- networking
- storage
- polÃ­ticas
- configuraÃ§Ãµes
O cluster Ã© o contexto dentro do qual os demais recursos Kubernetes existem.
***
# 3. Control Plane
O **Control Plane** Ã© responsÃ¡vel por controlar o cluster.
Principais componentes:
- API Server
- Scheduler
- Controller Manager
- etcd
```mermaid
flowchart LR
    U[kubectl / API Client] --> API[API Server]
    API --> ETCD[(etcd)]
    API --> SCH[Scheduler]
    API --> CM[Controller Manager]
    SCH --> N[Nodes]
    CM --> N
```
### API Server
Ã‰ a porta de entrada da API do Kubernetes.
Comandos como:
```bash
kubectl get pods
kubectl apply -f deployment.yaml
```
interagem com o API Server.
### etcd
Banco distribuÃ­do usado para armazenar o estado do cluster.
### Scheduler
Decide em qual Node um Pod serÃ¡ executado.
### Controller Manager
Executa controllers que observam o estado atual e tentam aproximÃ¡-lo do estado desejado.
***
# 4. Worker Node
Um **Node** Ã© uma mÃ¡quina que executa workloads.
Pode ser uma mÃ¡quina fÃ­sica ou virtual.
```mermaid
flowchart TD
    N[Worker Node]
    N --> K[Kubelet]
    N --> KPROXY[kube-proxy]
    N --> R[Container Runtime]
    N --> P1[Pod]
    N --> P2[Pod]
```
Principais componentes:
- kubelet
- container runtime
- kube-proxy
Um Node executa Pods, mas normalmente nÃ£o se deve pensar em Pods como pertencentes permanentemente a um Node. O Scheduler pode colocÃ¡-los em Nodes diferentes conforme as regras do cluster.
***
# 5. Namespace
**Namespace** Ã© uma forma de dividir logicamente os recursos dentro de um Cluster.
```mermaid
flowchart TD
    C[Cluster]
    C --> NS1[Namespace: dev]
    C --> NS2[Namespace: staging]
    C --> NS3[Namespace: prod]
    NS1 --> D1[Deployments]
    NS1 --> S1[Services]
    NS2 --> D2[Deployments]
    NS2 --> S2[Services]
    NS3 --> D3[Deployments]
    NS3 --> S3[Services]
```
Exemplo:
```bash
kubectl create namespace dev
kubectl get pods -n dev
```
Namespaces sÃ£o Ãºteis para:
- separar ambientes;
- separar times;
- aplicar polÃ­ticas;
- controlar quotas;
- organizar recursos.
Importante:
> Namespace nÃ£o Ã© um Node e nÃ£o Ã© uma mÃ¡quina virtual.
Um mesmo Node pode executar Pods pertencentes a vÃ¡rios Namespaces.
***
# 6. Workloads
**Workload** Ã© uma aplicaÃ§Ã£o ou processo que o Kubernetes precisa executar.
Os principais recursos de workload sÃ£o:
- Deployment
- StatefulSet
- DaemonSet
- Job
- CronJob
A relaÃ§Ã£o geral Ã©:
```mermaid
flowchart TD
    C[Cluster] --> NS[Namespace]
    NS --> W[Workloads]
    W --> D[Deployment]
    W --> SS[StatefulSet]
    W --> DS[DaemonSet]
    W --> J[Job]
    W --> CJ[CronJob]
```
***
# 7. Deployment
Deployment Ã© o principal recurso para aplicaÃ§Ãµes **stateless**.
Ele define quantas rÃ©plicas devem existir e qual versÃ£o da aplicaÃ§Ã£o deve ser executada.
```mermaid
flowchart TD
    D[Deployment]
    D --> RS[ReplicaSet]
    RS --> P1[Pod]
    RS --> P2[Pod]
    RS --> P3[Pod]
```
Exemplo:
```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: api
  template:
    metadata:
      labels:
        app: api
    spec:
      containers:
        - name: api
          image: my-api:1.0
```
O Deployment normalmente cria e gerencia um ReplicaSet, que por sua vez mantÃ©m os Pods.
***
# 8. ReplicaSet
ReplicaSet garante que determinada quantidade de Pods esteja disponÃ­vel.
```text
Deployment
    |
    v
ReplicaSet
    |
    +-- Pod
    +-- Pod
    +-- Pod
```
Normalmente nÃ£o criamos ReplicaSets diretamente.
Usamos Deployment e deixamos o Kubernetes gerenciar os ReplicaSets.
***
# 9. Pod
O **Pod** Ã© a menor unidade de execuÃ§Ã£o do Kubernetes.
Um Pod contÃ©m um ou mais containers que compartilham:
- rede;
- IP;
- volumes;
- contexto de execuÃ§Ã£o.
```mermaid
flowchart TD
    P[Pod]
    P --> C1[Container]
    P --> C2[Container]
    P --> V[Shared Volume]
    P --> IP[Pod IP]
```
Na maioria das aplicaÃ§Ãµes:
```text
1 Pod = 1 container principal
```
Mas sidecars tambÃ©m sÃ£o comuns.
Exemplo:
```text
Pod
â”œâ”€â”€ API container
â””â”€â”€ Logging sidecar
```
***
# 10. Containers
O container Ã© o processo efetivamente executado dentro do Pod.
```text
Cluster
â””â”€â”€ Node
    â””â”€â”€ Pod
        â””â”€â”€ Container
```
Kubernetes nÃ£o executa containers diretamente. Ele executa Pods, e os Pods contÃªm containers.
***
# 11. Labels e Selectors
Labels identificam recursos.
```yaml
metadata:
  labels:
    app: api
    environment: production
```
Selectors permitem encontrar recursos com determinadas labels.
```yaml
selector:
  matchLabels:
    app: api
```
Essa relaÃ§Ã£o Ã© fundamental para entender Services, Deployments, NetworkPolicies e outros recursos.
```mermaid
flowchart LR
    S[Service selector: app=api] --> P1[Pod app=api]
    S --> P2[Pod app=api]
    S -. nÃ£o seleciona .-> P3[Pod app=worker]
```
***
# 12. Service
Pod IPs sÃ£o efÃªmeros. Um Service fornece um endereÃ§o estÃ¡vel para acessar um conjunto de Pods.
```mermaid
flowchart LR
    C[Client] --> S[Service]
    S --> P1[Pod]
    S --> P2[Pod]
    S --> P3[Pod]
```
Tipos comuns:
- ClusterIP
- NodePort
- LoadBalancer
- ExternalName
O Service usa selectors para descobrir quais Pods devem receber trÃ¡fego.
***
# 13. Ingress e Gateway API
Ingress e Gateway API tratam do trÃ¡fego HTTP/HTTPS que entra no cluster.
Uma visÃ£o simplificada:
```mermaid
flowchart LR
    U[Internet] --> G[Ingress / Gateway]
    G --> S1[Service]
    S1 --> P1[Pods]
    G --> S2[Service]
    S2 --> P2[Pods]
```
A diferenÃ§a conceitual importante:
```text
Service
    -> expÃµe uma aplicaÃ§Ã£o dentro/atravÃ©s do cluster
Ingress / Gateway
    -> define como requisiÃ§Ãµes externas chegam aos Services
```
Gateway API Ã© a evoluÃ§Ã£o mais flexÃ­vel do modelo de Ingress.
***
# 14. ConfigMap
ConfigMap armazena configuraÃ§Ãµes nÃ£o sensÃ­veis.
Exemplo:
```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: api-config
data:
  ASPNETCORE_ENVIRONMENT: Production
```
Pode ser consumido por um Pod como:
- variÃ¡vel de ambiente;
- arquivo montado.
```mermaid
flowchart LR
    CM[ConfigMap] --> P[Pod]
```
***
# 15. Secret
Secret Ã© utilizado para informaÃ§Ãµes sensÃ­veis, como:
- passwords;
- tokens;
- connection strings;
- certificados.
```mermaid
flowchart LR
    S[Secret] --> P[Pod]
```
Importante:
> Secret nÃ£o deve ser interpretado automaticamente como um cofre criptogrÃ¡fico completo. A proteÃ§Ã£o real depende tambÃ©m da configuraÃ§Ã£o do cluster, especialmente do armazenamento e controle de acesso.
***
### Cofres externos

Um Secret Kubernetes fica armazenado no cluster; Base64 Ã© codificaÃ§Ã£o, nÃ£o criptografia. Restrinja o acesso com RBAC e configure criptografia em repouso. Para manter a origem do valor fora do cluster, instale um operador ou driver e configure sua identidade e permissÃµes no provedor.

#### External Secrets Operator

O [External Secrets Operator](https://external-secrets.io/latest/) lÃª um cofre externo e sincroniza os valores para um Secret Kubernetes. Ã‰ adequado quando a aplicaÃ§Ã£o jÃ¡ consome Secrets por variÃ¡veis de ambiente ou volumes. Requer o operador, um SecretStore/ClusterSecretStore configurado e permissÃµes para ler o provedor e criar/atualizar o Secret.

```yaml
apiVersion: external-secrets.io/v1
kind: ExternalSecret
metadata:
  name: payments-db
  namespace: platform
spec:
  refreshInterval: 1h
  secretStoreRef:
    name: production-secrets
    kind: ClusterSecretStore
  target:
    name: payments-db
    creationPolicy: Owner
  data:
    - secretKey: password
      remoteRef:
        key: apps/payments
        property: database-password
```

O exemplo pressupÃµe store, identidade do operador e chave remota configurados. O ExternalSecret cria ou atualiza o Secret `payments-db`, que um [Deployment](#deployment) pode consumir.

#### Secrets Store CSI Driver

O [Secrets Store CSI Driver](https://secrets-store-csi-driver.sigs.k8s.io/) monta valores do provedor como arquivos no Pod usando um SecretProviderClass. Ã‰ adequado quando a aplicaÃ§Ã£o lÃª arquivos e a polÃ­tica busca evitar uma cÃ³pia Secret no API server. Requer driver e plugin do provedor, identidade do Pod autorizada e configuraÃ§Ã£o de montagem.

```yaml
apiVersion: secrets-store.csi.x-k8s.io/v1
kind: SecretProviderClass
metadata:
  name: payments-db
  namespace: platform
spec:
  provider: vault
  parameters:
    roleName: payments
    objects: |
      - objectName: database-password
        secretPath: secret/data/apps/payments
        secretKey: password
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: payments
  namespace: platform
spec:
  selector:
    matchLabels: { app: payments }
  template:
    metadata:
      labels: { app: payments }
    spec:
      containers:
        - name: app
          image: example/payments:1.0
          volumeMounts:
            - name: external-secrets
              mountPath: /mnt/secrets-store
              readOnly: true
      volumes:
        - name: external-secrets
          csi:
            driver: secrets-store.csi.k8s.io
            readOnly: true
            volumeAttributes:
              secretProviderClass: payments-db
```

O trecho Vault requer o plugin Vault e autenticaÃ§Ã£o configurada. Sincronizar arquivos CSI para Secrets Kubernetes Ã© opcional e cria cÃ³pia no cluster; habilite somente se necessÃ¡rio. Consulte a documentaÃ§Ã£o do driver sobre suporte e rotaÃ§Ã£o para a versÃ£o instalada.

**Escolha:** Secret nativo Ã© a integraÃ§Ã£o mais simples; ESO sincroniza cofre externo para o modelo Secret existente; CSI monta arquivos sem criar essa cÃ³pia por padrÃ£o. Nenhuma opÃ§Ã£o substitui RBAC mÃ­nimo, rotaÃ§Ã£o e criptografia em repouso.

ReferÃªncias: [Secrets no Kubernetes](https://kubernetes.io/docs/concepts/configuration/secret/), [criptografia em repouso](https://kubernetes.io/docs/tasks/administer-cluster/encrypt-data/), [API ExternalSecret](https://external-secrets.io/latest/api/externalsecret/) e [uso do CSI Driver](https://secrets-store-csi-driver.sigs.k8s.io/getting-started/usage).

# 16. Storage
Storage permite persistir dados alÃ©m do ciclo de vida de um Pod.
Os principais conceitos sÃ£o:
```text
Pod
 |
 v
PVC
 |
 v
PV
 |
 v
Storage
```
***
# 17. PersistentVolumeClaim
PVC Ã© uma solicitaÃ§Ã£o de armazenamento feita pela aplicaÃ§Ã£o.
```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: postgres-data
spec:
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 20Gi
```
O Pod utiliza o PVC.
***
# 18. PersistentVolume
PV representa o armazenamento disponibilizado para o cluster.
```mermaid
flowchart LR
    P[Pod] --> PVC[PVC]
    PVC --> PV[PersistentVolume]
    PV --> S[Storage Backend]
```
***
# 19. StorageClass
StorageClass define como volumes podem ser provisionados.
```text
PVC
 |
 v
StorageClass
 |
 v
Dynamic Provisioning
 |
 v
PV
```
Isso permite que o Kubernetes crie volumes dinamicamente conforme as necessidades da aplicaÃ§Ã£o.
***
# 20. StatefulSet
StatefulSet Ã© utilizado para workloads que precisam de identidade e/ou armazenamento persistente associado Ã s rÃ©plicas.
Exemplo:
```mermaid
flowchart TD
    SS[StatefulSet]
    SS --> P0[postgres-0]
    SS --> P1[postgres-1]
    P0 --> PVC0[PVC]
    P1 --> PVC1[PVC]
```
Diferentemente de um Deployment, os Pods possuem identidade previsÃ­vel.
Exemplo:
```text
postgres-0
postgres-1
postgres-2
```
Ã‰ comum em:
- bancos;
- sistemas distribuÃ­dos;
- Kafka;
- etcd;
- outras aplicaÃ§Ãµes stateful.
***
# 21. DaemonSet
DaemonSet garante que um Pod seja executado em cada Node elegÃ­vel.
```mermaid
flowchart TD
    DS[DaemonSet]
    DS --> N1[Node 1]
    DS --> N2[Node 2]
    DS --> N3[Node 3]
    N1 --> P1[Pod]
    N2 --> P2[Pod]
    N3 --> P3[Pod]
```
Casos comuns:
- agentes de logging;
- monitoramento;
- networking;
- seguranÃ§a.
***
# 22. Job e CronJob
Job representa uma tarefa que precisa terminar.
```mermaid
flowchart LR
    J[Job] --> P[Pod]
    P --> F[Completed]
```
Exemplos:
- migrations;
- processamento batch;
- importaÃ§Ã£o de dados.
CronJob executa Jobs em um horÃ¡rio definido.
```text
CronJob
   |
   +-- Job
   |    â””â”€â”€ Pod
   |
   +-- Job
        â””â”€â”€ Pod
```
***
# 23. Requests e Limits
Definem recursos de CPU e memÃ³ria.
```yaml
resources:
  requests:
    cpu: "250m"
    memory: "256Mi"
  limits:
    cpu: "500m"
    memory: "512Mi"
```
### Requests
Quantidade usada pelo Scheduler para decidir onde colocar o Pod.
### Limits
Limite mÃ¡ximo permitido para o container.
```text
Pod
 |
 +-- requests -> planejamento/scheduling
 |
 +-- limits   -> limite de execuÃ§Ã£o
```
***
# 24. Scheduling
O Scheduler decide em qual Node um Pod serÃ¡ executado.
```mermaid
flowchart LR
    P[Pending Pod] --> S[Scheduler]
    S --> N1[Node 1]
    S --> N2[Node 2]
    S --> N3[Node 3]
```
A decisÃ£o pode considerar:
- CPU;
- memÃ³ria;
- requests;
- affinity;
- anti-affinity;
- taints;
- tolerations;
- topology;
- disponibilidade.
***
# 25. Node Affinity
Affinity permite expressar preferÃªncia ou requisito sobre onde um Pod deve ser executado.
Exemplo conceitual:
```text
Pod
 |
 +-- prefere Node com:
       disk=ssd
```
***
# 26. Pod Anti-Affinity
Anti-affinity permite evitar que Pods relacionados fiquem no mesmo local.
Exemplo:
```text
API replica 1 -> Node A
API replica 2 -> Node B
API replica 3 -> Node C
```
Isso aumenta a resiliÃªncia contra falha de um Node.
***
# 27. Taints e Tolerations
Taint pode impedir que Pods sejam agendados em determinado Node.
```text
Node
 |
 +-- taint: workload=gpu
```
Um Pod precisa de uma Toleration compatÃ­vel para ser executado ali.
```mermaid
flowchart LR
    N[Node with Taint] --> T[Toleration]
    T --> P[Pod]
```
Use cases:
- Nodes de GPU;
- Nodes dedicados;
- workloads especiais.
***
# 28. Topology Spread Constraints
Permitem distribuir Pods entre domÃ­nios de falha.
Por exemplo:
```text
Zone A
  Pod
  Pod
Zone B
  Pod
  Pod
Zone C
  Pod
  Pod
```
Isso reduz o impacto de falhas de uma zona inteira.
***
# 29. Health Probes
Kubernetes possui trÃªs probes principais.
### Startup Probe
Verifica se a aplicaÃ§Ã£o conseguiu iniciar.
### Readiness Probe
Indica se a aplicaÃ§Ã£o estÃ¡ pronta para receber trÃ¡fego.
### Liveness Probe
Indica se o processo continua saudÃ¡vel.
```mermaid
flowchart LR
    C[Container]
    C --> S[Startup]
    S --> R[Readiness]
    R --> L[Liveness]
```
Conceitualmente:
```text
Startup
   â†“
AplicaÃ§Ã£o iniciou?
   â†“
Readiness
   â†“
Pode receber trÃ¡fego?
   â†“
Liveness
   â†“
Continua saudÃ¡vel?
```
***
# 30. Horizontal Pod Autoscaler â€” HPA
HPA altera a quantidade de rÃ©plicas de um workload.
```mermaid
flowchart LR
    M[Metrics] --> H[HPA]
    H --> D[Deployment]
    D --> P[Pods]
```
Exemplo:
```text
CPU baixa
    â†“
3 Pods
CPU alta
    â†“
6 Pods
```
HPA normalmente trabalha com mÃ©tricas como:
- CPU;
- memÃ³ria;
- mÃ©tricas customizadas;
- mÃ©tricas externas.
***
# 31. Vertical Pod Autoscaler â€” VPA
VPA ajusta requests/limits de recursos dos Pods.
```text
VPA
 |
 +-- CPU request
 +-- Memory request
```
DiferenÃ§a principal:
```text
HPA -> mais/menos Pods
VPA -> mais/menos recursos por Pod
```
***
# 32. PodDisruptionBudget â€” PDB
PDB protege contra interrupÃ§Ãµes voluntÃ¡rias.
Exemplo:
```yaml
minAvailable: 2
```
Se existem 3 rÃ©plicas, o Kubernetes deve evitar uma operaÃ§Ã£o voluntÃ¡ria que deixe menos de 2 disponÃ­veis.
PDB Ã© Ãºtil durante:
- manutenÃ§Ã£o;
- drain de Nodes;
- upgrades;
- operaÃ§Ãµes administrativas.
***
# 33. PriorityClass
Define prioridade relativa entre Pods.
Quando recursos sÃ£o insuficientes, Pods de maior prioridade podem receber preferÃªncia.
```text
Critical workload
      â†“
High priority
Normal workload
      â†“
Normal priority
Batch workload
      â†“
Low priority
```
***
# 34. ServiceAccount
ServiceAccount representa uma identidade usada por Pods para interagir com a API do Kubernetes.
```mermaid
flowchart LR
    P[Pod] --> SA[ServiceAccount]
    SA --> RBAC[RBAC]
    RBAC --> API[Kubernetes API]
```
***
# 35. RBAC
RBAC controla quem pode executar quais aÃ§Ãµes sobre quais recursos.
Os principais objetos sÃ£o:
- Role
- ClusterRole
- RoleBinding
- ClusterRoleBinding
Exemplo:
```text
ServiceAccount
      |
      v
RoleBinding
      |
      v
Role
      |
      v
Permissions
```
Exemplo conceitual:
```text
get pods
list pods
watch pods
```
***
# 36. SecurityContext
SecurityContext define aspectos de seguranÃ§a de Pods e containers.
Pode controlar:
- usuÃ¡rio;
- grupo;
- capabilities;
- filesystem read-only;
- privilege escalation.
Exemplo:
```yaml
securityContext:
  runAsNonRoot: true
  allowPrivilegeEscalation: false
```
***
# 37. NetworkPolicy
NetworkPolicy controla quais Pods podem se comunicar.
```mermaid
flowchart LR
    API[API Pod] -->|allowed| DB[Database Pod]
    WEB[Web Pod] -->|allowed| API
    WEB -. blocked .-> DB
```
Isso permite implementar segmentaÃ§Ã£o de rede dentro do cluster.
***
# 38. ResourceQuota
ResourceQuota limita o consumo total de recursos dentro de um Namespace.
Exemplo:
```text
Namespace dev
CPU total: 10 cores
Memory total: 20Gi
Pods: 50
```
***
# 39. LimitRange
LimitRange define valores padrÃ£o ou limites para recursos dentro de um Namespace.
Pode estabelecer:
- requests padrÃ£o;
- limits padrÃ£o;
- mÃ­nimo;
- mÃ¡ximo.
DiferenÃ§a:
```text
ResourceQuota
    -> limite total do Namespace
LimitRange
    -> regras por recurso/Pod/container
```
***
# 40. CRD â€” Custom Resource Definition
CRD permite criar novos tipos de recursos Kubernetes.
Por exemplo:
```text
kind: Database
```
Depois de registrar um CRD, podemos criar:
```yaml
apiVersion: example.com/v1
kind: Database
metadata:
  name: postgres
spec:
  version: "17"
```
Isso permite estender a API do Kubernetes.
***
# 41. Controller
Controller observa recursos e tenta fazer o estado real convergir para o estado desejado.
```mermaid
flowchart LR
    D[Desired State] --> C[Controller]
    C --> A[Actual State]
    A --> C
```
Essa Ã© uma das ideias fundamentais do Kubernetes.
Deployment, StatefulSet e muitos outros recursos funcionam atravÃ©s de controllers.
***
# 42. Operator
Operator combina:
- CRDs;
- Controllers;
- lÃ³gica especÃ­fica de uma aplicaÃ§Ã£o.
Por exemplo, um Operator pode saber como:
- criar um banco;
- configurar replicaÃ§Ã£o;
- realizar backup;
- recuperar uma instÃ¢ncia;
- fazer upgrade.
```text
Custom Resource
      |
      v
Operator
      |
      +-- Deployment
      +-- Service
      +-- PVC
      +-- Secret
```
***
# 43. Admission Controllers
Admission Controllers interceptam requisiÃ§Ãµes Ã  API antes que sejam persistidas.
Podem:
- validar;
- modificar;
- bloquear recursos.
Fluxo:
```mermaid
flowchart LR
    U[User] --> API[API Server]
    API --> AC[Admission]
    AC --> ETCD[(etcd)]
```
SÃ£o usados para polÃ­ticas de seguranÃ§a e governanÃ§a.
***
# 44. Helm
Helm Ã© um gerenciador de pacotes para Kubernetes.
Um Chart agrupa:
- templates;
- values;
- manifests;
- configuraÃ§Ãµes.
```text
Chart
â”œâ”€â”€ templates/
â”œâ”€â”€ values.yaml
â””â”€â”€ Chart.yaml
```
Exemplo:
```bash
helm install my-api ./chart
```
Helm resolve principalmente o problema de **empacotamento e parametrizaÃ§Ã£o**.
***
# 45. Kustomize
Kustomize permite customizar manifests Kubernetes sem necessariamente utilizar templates.
Um padrÃ£o comum:
```text
base/
overlays/
â”œâ”€â”€ dev/
â”œâ”€â”€ staging/
â””â”€â”€ prod/
```
***
# 46. Argo CD e GitOps
Argo CD implementa um modelo GitOps para Kubernetes.
A ideia:
```mermaid
flowchart LR
    G[Git] --> A[Argo CD]
    A --> K[Kubernetes Cluster]
```
O Git representa o estado desejado.
Argo CD compara:
```text
Git state
    vs
Cluster state
```
e trabalha para eliminar o drift.
***
# 47. Observabilidade
Observabilidade normalmente envolve:
- logs;
- mÃ©tricas;
- traces;
- eventos.
```mermaid
flowchart TD
    A[Application]
    A --> L[Logs]
    A --> M[Metrics]
    A --> T[Traces]
    A --> E[Kubernetes Events]
    M --> HPA[HPA]
    L --> O[Observability Platform]
    M --> O
    T --> O
```
***
# 48. Como os conceitos se relacionam
A visÃ£o completa pode ser representada assim:
```mermaid
flowchart TD
    C[Cluster]
    C --> CP[Control Plane]
    C --> N[Worker Nodes]
    C --> NS[Namespaces]
    NS --> W[Workloads]
    W --> D[Deployment]
    W --> SS[StatefulSet]
    W --> DS[DaemonSet]
    W --> J[Job]
    W --> CJ[CronJob]
    D --> RS[ReplicaSet]
    RS --> P[Pods]
    SS --> SP[Stateful Pods]
    DS --> DP[Daemon Pods]
    J --> JP[Job Pods]
    CJ --> J
    P --> CT[Containers]
    S[Service] --> P
    I[Ingress / Gateway] --> S
    CM[ConfigMap] --> P
    SEC[Secret] --> P
    P --> PVC[PVC]
    PVC --> PV[PV]
    PV --> SC[StorageClass]
    HPA[HPA] --> D
    VPA[VPA] --> P
    PDB[PDB] --> D
    SA[ServiceAccount] --> RBAC[RBAC]
    RBAC --> P
    NP[NetworkPolicy] --> P
    SCH[Scheduler] --> N
    D --> SCH
```
***
# 49. Hierarquia mental recomendada
A melhor forma de estudar Kubernetes nÃ£o Ã© tratar todos os recursos como uma lista.
Pense primeiro nesta hierarquia:
```text
Cluster
â”‚
â”œâ”€â”€ Control Plane
â”‚
â”œâ”€â”€ Nodes
â”‚   â”‚
â”‚   â””â”€â”€ Pods
â”‚       â”‚
â”‚       â””â”€â”€ Containers
â”‚
â””â”€â”€ Namespaces
    â”‚
    â””â”€â”€ Workloads
        â”‚
        â”œâ”€â”€ Deployment
        â”‚   â””â”€â”€ ReplicaSet
        â”‚       â””â”€â”€ Pods
        â”‚
        â”œâ”€â”€ StatefulSet
        â”‚   â””â”€â”€ Pods
        â”‚
        â”œâ”€â”€ DaemonSet
        â”‚   â””â”€â”€ Pods
        â”‚
        â””â”€â”€ Jobs
            â””â”€â”€ Pods
```
Depois adicione os recursos que **se relacionam** com essa hierarquia:
```text
Service
    â””â”€â”€ seleciona Pods
Ingress / Gateway
    â””â”€â”€ direciona trÃ¡fego para Services
ConfigMap
    â””â”€â”€ fornece configuraÃ§Ã£o aos Pods
Secret
    â””â”€â”€ fornece dados sensÃ­veis aos Pods
PVC
    â””â”€â”€ fornece armazenamento aos Pods
HPA
    â””â”€â”€ controla escala de Workloads
PDB
    â””â”€â”€ protege disponibilidade dos Pods
Scheduler
    â””â”€â”€ decide em qual Node o Pod executarÃ¡
RBAC
    â””â”€â”€ controla permissÃµes
NetworkPolicy
    â””â”€â”€ controla comunicaÃ§Ã£o entre Pods
```
***
# 50. Arquitetura de uma aplicaÃ§Ã£o tÃ­pica
Uma aplicaÃ§Ã£o .NET poderia ser representada assim:
```mermaid
flowchart TD
    Internet --> Gateway
    Gateway --> APIService
    APIService --> APIPods
    APIPods --> ConfigMap
    APIPods --> Secret
    APIPods --> PVC
    HPA --> Deployment
    Deployment --> APIPods
    APIPods --> DatabaseService
    DatabaseService --> DatabasePods
    DatabasePods --> DatabasePVC
```
Uma estrutura mais realista:
```text
Cluster
â”‚
â”œâ”€â”€ Namespace: application
â”‚   â”‚
â”‚   â”œâ”€â”€ Gateway / Ingress
â”‚   â”‚
â”‚   â”œâ”€â”€ Service: API
â”‚   â”‚   â””â”€â”€ Deployment
â”‚   â”‚       â””â”€â”€ Pods
â”‚   â”‚           â””â”€â”€ .NET containers
â”‚   â”‚
â”‚   â”œâ”€â”€ ConfigMap
â”‚   â”œâ”€â”€ Secret
â”‚   â”œâ”€â”€ HPA
â”‚   â”œâ”€â”€ PDB
â”‚   â””â”€â”€ ServiceAccount
â”‚
â””â”€â”€ Namespace: database
    â”‚
    â”œâ”€â”€ Service
    â”œâ”€â”€ StatefulSet
    â”‚   â””â”€â”€ PostgreSQL Pods
    â”‚
    â””â”€â”€ PVC
```
***
# 51. Ordem recomendada para estudar
### NÃ­vel 1 â€” Estrutura
1. Cluster
2. Control Plane
3. Node
4. Namespace
5. Pod
6. Container
### NÃ­vel 2 â€” Workloads
7. Deployment
8. ReplicaSet
9. StatefulSet
10. DaemonSet
11. Job
12. CronJob
### NÃ­vel 3 â€” ComunicaÃ§Ã£o
13. Labels
14. Selectors
15. Service
16. Ingress
17. Gateway API
### NÃ­vel 4 â€” ConfiguraÃ§Ã£o e dados
18. ConfigMap
19. Secret
20. Volume
21. PVC
22. PV
23. StorageClass
### NÃ­vel 5 â€” Scheduling e recursos
24. Requests
25. Limits
26. Scheduler
27. Affinity
28. Anti-affinity
29. Taints
30. Tolerations
31. Topology Spread Constraints
### NÃ­vel 6 â€” ResiliÃªncia e escala
32. Probes
33. HPA
34. VPA
35. PDB
36. PriorityClass
### NÃ­vel 7 â€” SeguranÃ§a
37. ServiceAccount
38. RBAC
39. SecurityContext
40. NetworkPolicy
41. ResourceQuota
42. LimitRange
### NÃ­vel 8 â€” Extensibilidade
43. CRD
44. Controller
45. Operator
46. Admission Controller
### NÃ­vel 9 â€” Deploy e operaÃ§Ã£o
47. Helm
48. Kustomize
49. Argo CD
50. GitOps
51. Observabilidade
***
# 52. Resumo final
A principal visÃ£o mental deve ser:
```text
CLUSTER
â”‚
â”œâ”€â”€ CONTROL PLANE
â”‚   â”œâ”€â”€ API Server
â”‚   â”œâ”€â”€ Scheduler
â”‚   â”œâ”€â”€ Controllers
â”‚   â””â”€â”€ etcd
â”‚
â”œâ”€â”€ NODES
â”‚   â””â”€â”€ PODS
â”‚       â””â”€â”€ CONTAINERS
â”‚
â””â”€â”€ NAMESPACES
    â””â”€â”€ WORKLOADS
        â”œâ”€â”€ DEPLOYMENT
        â”‚   â””â”€â”€ REPLICASET
        â”‚       â””â”€â”€ PODS
        â”‚
        â”œâ”€â”€ STATEFULSET
        â”‚   â””â”€â”€ PODS
        â”‚
        â”œâ”€â”€ DAEMONSET
        â”‚   â””â”€â”€ PODS
        â”‚
        â””â”€â”€ JOB / CRONJOB
            â””â”€â”€ PODS

RELACIONAMENTOS TRANSVERSAIS
SERVICE
    â””â”€â”€ seleciona PODS
INGRESS / GATEWAY
    â””â”€â”€ direciona para SERVICES
CONFIGMAP / SECRET
    â””â”€â”€ configuram PODS
PVC / PV / STORAGECLASS
    â””â”€â”€ fornecem STORAGE aos PODS
HPA / VPA / PDB
    â””â”€â”€ controlam comportamento dos WORKLOADS
SCHEDULER
    â””â”€â”€ coloca PODS em NODES
RBAC / SERVICEACCOUNT
    â””â”€â”€ controlam permissÃµes
NETWORKPOLICY
    â””â”€â”€ controla comunicaÃ§Ã£o
CRD / OPERATOR
    â””â”€â”€ estendem o prÃ³prio Kubernetes
HELM / KUSTOMIZE / ARGO CD
    â””â”€â”€ empacotam, configuram e entregam recursos
```
A regra mental mais importante Ã©:
> **Cluster Ã© o maior contexto. Nodes e Namespaces existem dentro dele. Workloads existem nos Namespaces e geram Pods. Pods executam Containers. Os demais recursos normalmente se relacionam com esses elementos, em vez de simplesmente ficarem abaixo deles em uma hierarquia.**

# Exemplos prÃ¡ticos e comparaÃ§Ãµes

Os exemplos completos, traduzidos e com botÃ£o de cÃ³pia estÃ£o na seÃ§Ã£o **Exemplos prÃ¡ticos** da Wiki. Os manifests abaixo usam nomes/imagens ilustrativos; confira requisitos do controller e da StorageClass do seu cluster antes de aplicar.

## Deployment x StatefulSet x DaemonSet

Deployment Ã© para rÃ©plicas intercambiÃ¡veis. StatefulSet dÃ¡ identidade ordinal e pode criar um PVC por rÃ©plica; nÃ£o replica dados nem torna a aplicaÃ§Ã£o altamente disponÃ­vel sozinho. DaemonSet executa em cada Node elegÃ­vel. Use Job para tarefa finita e CronJob para criar Jobs por agenda. ReplicaSet normalmente Ã© gerenciado por Deployment.

```yaml
apiVersion: apps/v1
kind: Deployment
metadata: { name: api }
spec:
  replicas: 3
  selector: { matchLabels: { app: api } }
  template:
    metadata: { labels: { app: api } }
    spec:
      containers: [{ name: api, image: example/api:1.0 }]
```

ReferÃªncias: [Deployment](https://kubernetes.io/docs/concepts/workloads/controllers/deployment/), [StatefulSet](https://kubernetes.io/docs/concepts/workloads/controllers/statefulset/), [DaemonSet](https://kubernetes.io/docs/concepts/workloads/controllers/daemonset/) e [Jobs](https://kubernetes.io/docs/concepts/workloads/controllers/).

## Service, Ingress e Gateway API

Service cria um endereÃ§o estÃ¡vel e seleciona Pods por labels. Ingress/Gateway definem rotas externas para Services. Ingress requer controller; a API estÃ¡ congelada e Kubernetes recomenda considerar Gateway API para novos recursos de roteamento.

```yaml
apiVersion: v1
kind: Service
metadata: { name: web }
spec:
  selector: { app: web }
  ports: [{ port: 80, targetPort: 8080 }]
```

ReferÃªncias: [Service](https://kubernetes.io/docs/concepts/services-networking/service/), [Ingress](https://kubernetes.io/docs/concepts/services-networking/ingress/) e [Gateway API](https://gateway-api.sigs.k8s.io/).

## ConfiguraÃ§Ã£o, identidade e permissÃµes

ConfigMap guarda configuraÃ§Ã£o nÃ£o sensÃ­vel; Secret representa dado sensÃ­vel, mas Base64 nÃ£o Ã© criptografia. ServiceAccount dÃ¡ identidade ao Pod. Role/ClusterRole definem permissÃµes; RoleBinding/ClusterRoleBinding associam sujeitos aos papÃ©is.

```yaml
apiVersion: v1
kind: ConfigMap
metadata: { name: api-config }
data: { LOG_LEVEL: Information }
---
apiVersion: v1
kind: Secret
metadata: { name: api-credentials }
type: Opaque
stringData: { password: replace-me }
```

Use apenas credenciais fictÃ­cias em exemplos. ReferÃªncias: [ConfigMap](https://kubernetes.io/docs/concepts/configuration/configmap/), [Secret](https://kubernetes.io/docs/concepts/configuration/secret/), [ServiceAccount](https://kubernetes.io/docs/concepts/security/service-accounts/) e [RBAC](https://kubernetes.io/docs/reference/access-authn-authz/rbac/).

## PVC, PV e StorageClass

AplicaÃ§Ãµes solicitam armazenamento com PVC. O cluster vincula a claim a um PV existente ou o provisiona dinamicamente conforme a StorageClass. Em geral, o usuÃ¡rio cria PVC; administradores e provisionadores gerenciam PVs. Volumes locais dependem de Node especÃ­fico.

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata: { name: app-data }
spec:
  accessModes: [ReadWriteOnce]
  resources:
    requests: { storage: 5Gi }
```

ReferÃªncias: [Persistent Volumes](https://kubernetes.io/docs/concepts/storage/persistent-volumes/), [StorageClass](https://kubernetes.io/docs/concepts/storage/storage-classes/) e [provisionamento dinÃ¢mico](https://kubernetes.io/docs/concepts/storage/dynamic-provisioning/).

## Escala, saÃºde e disponibilidade

HPA altera o nÃºmero de rÃ©plicas com base em mÃ©tricas; VPA recomenda ou altera requests/limits por Pod e costuma ser instalado Ã  parte; PDB limita interrupÃ§Ãµes voluntÃ¡rias. Requests/limits e probes sÃ£o configuraÃ§Ãµes do container/Pod, nÃ£o recursos controladores.

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata: { name: api }
spec:
  scaleTargetRef: { apiVersion: apps/v1, kind: Deployment, name: api }
  minReplicas: 2
  maxReplicas: 8
  metrics:
    - type: Resource
      resource:
        name: cpu
        target: { type: Utilization, averageUtilization: 70 }
```

ReferÃªncias: [HPA](https://kubernetes.io/docs/tasks/run-application/horizontal-pod-autoscale/), [probes](https://kubernetes.io/docs/concepts/workloads/pods/pod-lifecycle/#container-probes), [disrupÃ§Ãµes/PDB](https://kubernetes.io/docs/concepts/workloads/pods/disruptions/) e [VPA](https://github.com/kubernetes/autoscaler/tree/master/vertical-pod-autoscaler).

## PolÃ­ticas e agendamento

NetworkPolicy seleciona Pods e depende de um plugin que implemente sua aplicaÃ§Ã£o. ResourceQuota limita o consumo total do namespace; LimitRange estabelece defaults e limites por objeto. PriorityClass influencia a prioridade de scheduling; affinity, taints/tolerations e topology spread influenciam a colocaÃ§Ã£o dos Pods.

```yaml
apiVersion: v1
kind: ResourceQuota
metadata: { name: team-budget }
spec:
  hard: { pods: "20", requests.cpu: "4", requests.memory: 8Gi }
```

ReferÃªncias: [NetworkPolicy](https://kubernetes.io/docs/concepts/services-networking/network-policies/), [ResourceQuota](https://kubernetes.io/docs/concepts/policy/resource-quotas/), [LimitRange](https://kubernetes.io/docs/concepts/policy/limit-range/) e [agendamento](https://kubernetes.io/docs/concepts/scheduling-eviction/).

## Extensibilidade e entrega

CRD adiciona um tipo Ã  API; o controller/operator implementa seu comportamento. Helm empacota templates, Kustomize compÃµe overlays e Argo CD reconcilia manifests do Git. SÃ£o ferramentas com papÃ©is diferentes, nÃ£o substitutos do Deployment/StatefulSet.

```yaml
apiVersion: apiextensions.k8s.io/v1
kind: CustomResourceDefinition
metadata: { name: databases.example.com }
spec:
  group: example.com
  scope: Namespaced
  names: { plural: databases, singular: database, kind: Database }
  versions:
    - name: v1
      served: true
      storage: true
      schema:
        openAPIV3Schema:
          type: object
          properties:
            spec: { type: object }
```

ReferÃªncias: [Custom Resources/CRD](https://kubernetes.io/docs/concepts/extend-kubernetes/api-extension/custom-resources/), [Helm](https://helm.sh/docs/), [Kustomize](https://kubectl.docs.kubernetes.io/references/kustomize/) e [Argo CD](https://argo-cd.readthedocs.io/).

## Namespace, Pod, ReplicaSet, labels e selectors

Namespace separa nomes e polÃ­ticas; Pod Ã© a unidade executÃ¡vel; ReplicaSet mantÃ©m Pods selecionados, mas normalmente deve ser gerenciado por Deployment. Labels sÃ£o metadados e sÃ³ tÃªm efeito quando um selector as utiliza.

Cluster e Node aparecem no grafo, mas sua criaÃ§Ã£o depende da distribuiÃ§Ã£o e do provedor de infraestrutura; nÃ£o existe um manifesto de aplicaÃ§Ã£o portÃ¡til para criÃ¡-los em qualquer ambiente. Consulte a documentaÃ§Ã£o do provedor e [Nodes](https://kubernetes.io/docs/concepts/architecture/nodes/).

```yaml
apiVersion: v1
kind: Namespace
metadata: { name: team-a }
---
apiVersion: apps/v1
kind: ReplicaSet
metadata:
  name: api-example
  namespace: team-a
spec:
  replicas: 2
  selector: { matchLabels: { app: api } }
  template:
    metadata: { labels: { app: api } }
    spec:
      containers: [{ name: api, image: example/api:1.0 }]
```

ReferÃªncias: [Namespaces](https://kubernetes.io/docs/concepts/overview/working-with-objects/namespaces/), [Pods](https://kubernetes.io/docs/concepts/workloads/pods/), [ReplicaSets](https://kubernetes.io/docs/concepts/workloads/controllers/replicaset/) e [labels/selectors](https://kubernetes.io/docs/concepts/overview/working-with-objects/labels/).

## PriorityClass e regras de posicionamento

PriorityClass ordena prioridade relativa, nÃ£o reserva capacidade. Affinity e topology spread guiam ou restringem posicionamento; tolerations permitem tolerar taints sem forÃ§ar um Node especÃ­fico.

```yaml
apiVersion: scheduling.k8s.io/v1
kind: PriorityClass
metadata: { name: batch-low }
value: 1000
globalDefault: false
description: Low-priority batch workloads
```

ReferÃªncias: [PriorityClass](https://kubernetes.io/docs/concepts/scheduling-eviction/pod-priority-preemption/), [Node affinity](https://kubernetes.io/docs/concepts/scheduling-eviction/assign-pod-node/) e [topology spread](https://kubernetes.io/docs/concepts/scheduling-eviction/topology-spread-constraints/).

## Comandos para empacotar e entregar

```bash
helm upgrade --install api ./chart --namespace team-a --create-namespace
kubectl apply -k ./overlays/dev
argocd app get api-team-a
```

O primeiro comando instala/atualiza um release; o segundo aplica uma overlay Kustomize; o terceiro consulta uma Application Argo CD jÃ¡ configurada. Revise alteraÃ§Ãµes antes de sincronizar GitOps.

## Exemplos de criação de recursos

Use os exemplos como ponto de partida e ajuste nomes, imagens, classes de armazenamento, campos específicos do controlador e permissões ao seu cluster.

### [Cluster](/wiki#cluster)

```bash
kubectl config current-context
kubectl cluster-info
kubectl get nodes
```

### [Node](/wiki#worker-node)

```bash
kubectl get nodes -o wide
kubectl describe node <node-name>
```

### [Namespace](/wiki#namespace)

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: demo
```

### [Containers](/wiki#containers)

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: app-with-container
spec:
  containers:
    - name: app
      image: nginx:1.27
```

### [Deployment](/wiki#deployment)

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: api
spec:
  replicas: 3
  selector:
    matchLabels:
      app: api
  template:
    metadata:
      labels:
        app: api
    spec:
      containers:
        - name: api
          image: my-api:1.0
```

### [ReplicaSet](/wiki#replicaset)

```yaml
apiVersion: apps/v1
kind: ReplicaSet
metadata:
  name: api
spec:
  replicas: 2
  selector:
    matchLabels:
      app: api
  template:
    metadata:
      labels:
        app: api
    spec:
      containers:
        - name: api
          image: nginx:1.27
```

### [Pod](/wiki#pod)

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: standalone
  labels:
    app: standalone
spec:
  containers:
    - name: app
      image: nginx:1.27
```

### [Service](/wiki#service)

```yaml
apiVersion: v1
kind: Service
metadata:
  name: web
spec:
  selector:
    app: web
  ports:
    - name: http
      port: 80
      targetPort: 8080
  type: ClusterIP
```

### [Ingress and Gateway API](/wiki#ingress-e-gateway-api)

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: web
spec:
  ingressClassName: nginx
  rules:
    - host: web.example.test
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: web
                port:
                  number: 80
```

```yaml
apiVersion: gateway.networking.k8s.io/v1
kind: Gateway
metadata:
  name: public-gateway
  namespace: demo
spec:
  gatewayClassName: example-gateway-class
  listeners:
    - name: http
      protocol: HTTP
      port: 80
      allowedRoutes:
        namespaces:
          from: Same
---
apiVersion: gateway.networking.k8s.io/v1
kind: HTTPRoute
metadata:
  name: web
  namespace: demo
spec:
  parentRefs:
    - name: public-gateway
  hostnames:
    - web.example.test
  rules:
    - backendRefs:
        - name: web
          port: 80
```

### [ConfigMap](/wiki#configmap)

```yaml
apiVersion: v1
kind: ConfigMap
metadata:
  name: api-config
data:
  ASPNETCORE_ENVIRONMENT: Production
```

### [Secret](/wiki#secret)

```yaml
apiVersion: external-secrets.io/v1
kind: ExternalSecret
metadata:
  name: payments-db
  namespace: platform
spec:
  refreshInterval: 1h
  secretStoreRef:
    name: production-secrets
    kind: ClusterSecretStore
  target:
    name: payments-db
    creationPolicy: Owner
  data:
    - secretKey: password
      remoteRef:
        key: apps/payments
        property: database-password
```

```yaml
apiVersion: secrets-store.csi.x-k8s.io/v1
kind: SecretProviderClass
metadata:
  name: payments-db
  namespace: platform
spec:
  provider: vault
  parameters:
    roleName: payments
    objects: |
      - objectName: database-password
        secretPath: secret/data/apps/payments
        secretKey: password
---
apiVersion: apps/v1
kind: Deployment
metadata:
  name: payments
  namespace: platform
spec:
  selector:
    matchLabels: { app: payments }
  template:
    metadata:
      labels: { app: payments }
    spec:
      containers:
        - name: app
          image: example/payments:1.0
          volumeMounts:
            - name: external-secrets
              mountPath: /mnt/secrets-store
              readOnly: true
      volumes:
        - name: external-secrets
          csi:
            driver: secrets-store.csi.k8s.io
            readOnly: true
            volumeAttributes:
              secretProviderClass: payments-db
```

### [Volume in a Pod](/wiki#storage)

```yaml
apiVersion: v1
kind: Pod
metadata:
  name: app-with-volume
spec:
  containers:
    - name: app
      image: nginx:1.27
      volumeMounts:
        - name: shared-data
          mountPath: /data
  volumes:
    - name: shared-data
      emptyDir: {}
```

### [PersistentVolumeClaim](/wiki#persistentvolumeclaim)

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: postgres-data
spec:
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 20Gi
```

### [PersistentVolume](/wiki#persistentvolume)

```yaml
apiVersion: v1
kind: PersistentVolume
metadata:
  name: local-data
spec:
  capacity:
    storage: 5Gi
  accessModes:
    - ReadWriteOnce
  persistentVolumeReclaimPolicy: Retain
  hostPath:
    path: /mnt/data
```

### [StorageClass](/wiki#storageclass)

```yaml
apiVersion: storage.k8s.io/v1
kind: StorageClass
metadata:
  name: fast
provisioner: example.com.csi.driver
reclaimPolicy: Delete
volumeBindingMode: WaitForFirstConsumer
```

### [StatefulSet](/wiki#statefulset)

```yaml
apiVersion: v1
kind: Service
metadata:
  name: database
spec:
  clusterIP: None
  selector:
    app: database
  ports:
    - name: db
      port: 5432
      targetPort: 5432
---
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: database
spec:
  serviceName: database
  replicas: 2
  selector:
    matchLabels:
      app: database
  template:
    metadata:
      labels:
        app: database
    spec:
      containers:
        - name: postgres
          image: postgres:16
          ports:
            - containerPort: 5432
          volumeMounts:
            - name: data
              mountPath: /var/lib/postgresql/data
  volumeClaimTemplates:
    - metadata:
        name: data
      spec:
        accessModes: ["ReadWriteOnce"]
        resources:
          requests:
            storage: 5Gi
```

### [DaemonSet](/wiki#daemonset)

```yaml
apiVersion: apps/v1
kind: DaemonSet
metadata:
  name: node-agent
spec:
  selector:
    matchLabels:
      app: node-agent
  template:
    metadata:
      labels:
        app: node-agent
    spec:
      containers:
        - name: agent
          image: example/node-agent:1.0
```

### [Job and CronJob](/wiki#job-e-cronjob)

```yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: cleanup
spec:
  schedule: "0 2 * * *"
  jobTemplate:
    spec:
      template:
        spec:
          restartPolicy: OnFailure
          containers:
            - name: cleanup
              image: busybox:1.36
              command: ["sh", "-c", "echo cleanup"]
```

```yaml
apiVersion: batch/v1
kind: Job
metadata:
  name: one-time-report
spec:
  backoffLimit: 2
  template:
    spec:
      restartPolicy: Never
      containers:
        - name: report
          image: busybox:1.36
          command: ["sh", "-c", "echo generate report"]
```

### [HorizontalPodAutoscaler](/wiki#horizontal-pod-autoscaler-hpa)

```yaml
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata:
  name: web
spec:
  scaleTargetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: web
  minReplicas: 2
  maxReplicas: 10
  metrics:
    - type: Resource
      resource:
        name: cpu
        target:
          type: Utilization
          averageUtilization: 70
```

### [VerticalPodAutoscaler](/wiki#vertical-pod-autoscaler-vpa)

```yaml
apiVersion: autoscaling.k8s.io/v1
kind: VerticalPodAutoscaler
metadata:
  name: web
spec:
  targetRef:
    apiVersion: apps/v1
    kind: Deployment
    name: web
  updatePolicy:
    updateMode: Off
```

### [PodDisruptionBudget](/wiki#poddisruptionbudget-pdb)

```yaml
minAvailable: 2
```

### [PriorityClass](/wiki#priorityclass)

```yaml
apiVersion: scheduling.k8s.io/v1
kind: PriorityClass
metadata:
  name: high-priority
value: 100000
globalDefault: false
description: "Priority for latency-sensitive workloads"
```

### [ServiceAccount](/wiki#serviceaccount)

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: app-reader
  namespace: demo
```

### [RBAC](/wiki#rbac)

```yaml
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: pod-reader
  namespace: demo
rules:
  - apiGroups: [""]
    resources: ["pods"]
    verbs: ["get", "list", "watch"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: app-pod-reader
  namespace: demo
subjects:
  - kind: ServiceAccount
    name: app-reader
    namespace: demo
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: Role
  name: pod-reader
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRole
metadata:
  name: node-reader
rules:
  - apiGroups: [""]
    resources: ["nodes"]
    verbs: ["get", "list", "watch"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: ClusterRoleBinding
metadata:
  name: app-node-reader
subjects:
  - kind: ServiceAccount
    name: app-reader
    namespace: demo
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: ClusterRole
  name: node-reader
```

### [NetworkPolicy](/wiki#networkpolicy)

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: allow-web
  namespace: demo
spec:
  podSelector:
    matchLabels:
      app: web
  policyTypes: [Ingress]
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app: frontend
      ports:
        - protocol: TCP
          port: 8080
```

### [ResourceQuota](/wiki#resourcequota)

```yaml
apiVersion: v1
kind: ResourceQuota
metadata:
  name: compute-budget
  namespace: demo
spec:
  hard:
    requests.cpu: "4"
    requests.memory: 8Gi
    limits.cpu: "8"
    limits.memory: 16Gi
    pods: "20"
```

### [LimitRange](/wiki#limitrange)

```yaml
apiVersion: v1
kind: LimitRange
metadata:
  name: container-defaults
  namespace: demo
spec:
  limits:
    - type: Container
      defaultRequest:
        cpu: 100m
        memory: 128Mi
      default:
        cpu: 500m
        memory: 512Mi
```

### [CustomResourceDefinition](/wiki#crd-custom-resource-definition)

```yaml
apiVersion: example.com/v1
kind: Database
metadata:
  name: postgres
spec:
  version: "17"
```

## Controladores e recursos que eles criam

- [Deployment → ReplicaSet → Pod](/wiki#deployment)

```mermaid
flowchart LR
    D[Deployment] --> RS[ReplicaSet] --> P[Pods]
```

- [CronJob → Job → Pod](/wiki#job-e-cronjob)

```mermaid
flowchart LR
    C[CronJob] --> J[Jobs] --> P[Pods]
```

- [StatefulSet → Pod + PVC por réplica](/wiki#statefulset)

```mermaid
flowchart LR
    S[StatefulSet] --> P[Pods] --> PVC[PersistentVolumeClaims]
```

- [DaemonSet → um Pod em cada Node elegível](/wiki#daemonset)

```mermaid
flowchart LR
    D[DaemonSet] --> P[Pod per eligible Node]
```
