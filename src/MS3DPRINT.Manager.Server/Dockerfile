FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props global.json ./
COPY src/MS3DPRINT.Manager.Core/MS3DPRINT.Manager.Core.csproj src/MS3DPRINT.Manager.Core/
COPY src/MS3DPRINT.Manager.Server/MS3DPRINT.Manager.Server.csproj src/MS3DPRINT.Manager.Server/
RUN dotnet restore src/MS3DPRINT.Manager.Server/MS3DPRINT.Manager.Server.csproj -bl:/tmp/restore-{}.binlog
COPY src/MS3DPRINT.Manager.Core/ src/MS3DPRINT.Manager.Core/
COPY src/MS3DPRINT.Manager.Server/ src/MS3DPRINT.Manager.Server/
RUN dotnet publish src/MS3DPRINT.Manager.Server/MS3DPRINT.Manager.Server.csproj --no-restore -c Release -o /app -bl:/tmp/publish-{}.binlog

FROM build AS verify
COPY MS3DPRINT.Manager.sln ./
COPY tests/MS3DPRINT.Manager.Server.Tests/ tests/MS3DPRINT.Manager.Server.Tests/
RUN dotnet test tests/MS3DPRINT.Manager.Server.Tests --nologo --verbosity quiet -bl:/tmp/tests-{}.binlog

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir -p /data/state /data/workspace && chown -R app:app /data
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Server__StateRoot=/data/state \
    Server__WorkspaceRoot=/data/workspace
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "MS3DPRINT.Manager.Server.dll"]
