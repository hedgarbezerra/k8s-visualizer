# Arquitetura da Wiki Kubernetes

## Conteúdo e idioma

- `Resources/WikiResources.resx` contém textos em português; `WikiResources.en.resx` contém as traduções em inglês.
- `WikiResources.cs` é o marcador de `IStringLocalizer<WikiResources>`.
- A cultura é escolhida pelo mecanismo global compartilhado com Settings. A Wiki não tem lógica própria de idioma.
- YAML, comandos e Mermaid não são traduzidos: são raw string literals nos componentes de seção.
- Os Markdown desta pasta são espelhos de leitura; o runtime não os consulta. Atualize-os na mesma mudança que os componentes e recursos.

## Componentes

- `Components/Pages/Wiki/Wiki.razor` apresenta o cabeçalho.
- `Components/Shared/KubernetesWikiContent.razor` mantém o registro ordenado `WikiSectionDefinition[]`, o índice, os breadcrumbs e instancia as seções.
- `Components/Shared/WikiSections/*Section.razor` apresenta cada seção.
- `WikiSectionFrame.razor` compartilha título e âncora; `WikiContentBlock.razor` renderiza blocos tipados; `WikiCodeBlock.razor`, `WikiDiagramBlock.razor` e `WikiComparison.razor` encapsulam apresentações reutilizadas.

## Decisões e limites

- A ordem do registro controla a sequência de leitura, não representa hierarquia Kubernetes.
- IDs são estáveis entre idiomas; títulos e grupos são localizados.
- ESO e Secrets Store CSI exigem instalação/configuração no cluster e no provedor. Os exemplos são ilustrativos e não incluem credenciais reais.
