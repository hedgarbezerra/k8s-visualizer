FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER root
RUN mkdir -p /app/App_Data/DataProtection-Keys && chown -R $APP_UID /app/App_Data
USER $APP_UID
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/KubernetesVisualizer.csproj", "src/"]
RUN dotnet restore "src/KubernetesVisualizer.csproj"
COPY src/ src/
WORKDIR /src
RUN dotnet publish "src/KubernetesVisualizer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "KubernetesVisualizer.dll"]
