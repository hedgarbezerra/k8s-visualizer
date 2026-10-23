# Persistência de conexões

Os perfis cadastrados na página Configurações ficam no banco SQLite `App_Data/kubernetes-visualizer.db`. O banco guarda apenas o nome do perfil, o caminho do kubeconfig, o contexto Kubernetes e a última conexão/namespace selecionados; tokens e certificados continuam no kubeconfig e não são copiados para o banco. Na inicialização interativa, a aplicação reconecta ao último perfil e restaura o namespace salvo quando ele ainda existe.

Na primeira inicialização após a migração, os perfis existentes em `App_Data/connections.json` são importados uma única vez. O JSON antigo é mantido como cópia local e não volta a ser usado como armazenamento.

## Execução local

O diretório `App_Data` fica dentro da raiz da aplicação e o SQLite permanece entre reinicializações do processo. Para mudar o caminho do banco, configure `ConnectionProfiles__DatabasePath` ou `ConnectionProfiles:DatabasePath`.

## Docker Compose

1. Copie `.env.example` para `.env` e ajuste `KUBECONFIG_HOST_PATH` para o kubeconfig do host.
2. Execute `docker compose up --build`.
3. A aplicação ficará em `http://localhost:5080`; o volume nomeado `visualizer-data` preserva SQLite e chaves de Data Protection quando o container é recriado.

O volume só é apagado com uma remoção explícita, por exemplo `docker compose down --volumes`. Evite esse comando se quiser manter os perfis cadastrados. Reiniciar ou recriar o container preserva a seleção enquanto o volume nomeado continuar existindo.

## Kubernetes

Se a aplicação for executada dentro de um Pod, monte um PersistentVolumeClaim em `/app/App_Data` (ou configure `ConnectionProfiles__DatabasePath` para um caminho persistente). Sem volume persistente, o SQLite sobrevive ao reinício do processo dentro do mesmo container, mas não à substituição do Pod.
