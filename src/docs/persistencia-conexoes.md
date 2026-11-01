# Persistência de conexões

Os perfis cadastrados na página Configurações ficam no banco SQLite `App_Data/kubernetes-visualizer.db`. O banco guarda apenas o nome do perfil, o caminho do kubeconfig, o contexto Kubernetes e a última conexão/namespace selecionados; tokens e certificados continuam no kubeconfig e não são copiados para o banco. Na inicialização interativa, a aplicação reconecta ao último perfil e restaura o namespace salvo quando ele ainda existe.

As conexões, a última seleção e as configurações editáveis da aplicação são armazenadas somente no SQLite. O app não lê nem grava um arquivo JSON de conexões. Os labels padrão para relacionar recursos no grafo são definidos em `GraphGrouping:DefaultRelatedLabelKeys` no `appsettings.json` e sempre fazem parte da lista efetiva. Os labels adicionais são configurados na página Configurações e armazenados em `application_settings`.

## Execução local

O caminho do banco é resolvido a partir de `IWebHostEnvironment.ContentRootPath`, em `App_Data/kubernetes-visualizer.db`. A aplicação cria o diretório `App_Data`, o arquivo SQLite e suas tabelas automaticamente na inicialização. O banco permanece entre reinicializações do processo.

## Docker Compose

1. No PowerShell, defina `KUBECONFIG_HOST_PATH` para o caminho do kubeconfig no host, por exemplo `$env:KUBECONFIG_HOST_PATH = "$env:USERPROFILE\.kube\config"`. No Bash, use `export KUBECONFIG_HOST_PATH="$HOME/.kube/config"`.
2. Execute `docker compose up --build`.
3. O Compose monta o arquivo do host como `/run/kubeconfig/config` no container e define `KUBECONFIG` com esse caminho. Na página Configurações, use `/run/kubeconfig/config` ao criar o perfil; o caminho do host não existe dentro do container.
4. A aplicação ficará em `http://localhost:5080`; o volume nomeado `visualizer-data` preserva SQLite e chaves de Data Protection quando o container é recriado.

O volume só é apagado com uma remoção explícita, por exemplo `docker compose down --volumes`. Evite esse comando se quiser manter os perfis cadastrados. Reiniciar ou recriar o container preserva a seleção enquanto o volume nomeado continuar existindo.

## Kubernetes

Se a aplicação for executada dentro de um Pod, monte um PersistentVolumeClaim em `/app/App_Data`. Sem volume persistente, o SQLite sobrevive ao reinício do processo dentro do mesmo container, mas não à substituição do Pod.
