# Fundamentos do Kubernetes

Esta wiki reúne explicações locais dos recursos apresentados pelo Kubernetes Visualizer.

## Cluster e namespace

Um cluster é o conjunto de componentes de controle e máquinas que executam aplicações Kubernetes. Um namespace organiza recursos namespaced dentro do cluster.

## Pod

Um Pod é a menor unidade implantável do Kubernetes. Ele agrupa um ou mais containers que compartilham rede e volumes.

## Deployment

Um Deployment declara quantas réplicas de uma aplicação devem existir e coordena a atualização e a substituição dos Pods gerenciados.

## Service

Um Service fornece um endereço estável para alcançar Pods que correspondem a um seletor.

## ConfigMap

Um ConfigMap armazena dados de configuração não confidenciais que podem ser consumidos por aplicações em containers.
