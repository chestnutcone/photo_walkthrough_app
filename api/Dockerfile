# Build context: repository root.  docker build -f api/Dockerfile -t vela-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY api/Vela.sln api/
COPY api/src/Vela.Api/Vela.Api.csproj api/src/Vela.Api/
COPY api/tests/Vela.Api.Tests/Vela.Api.Tests.csproj api/tests/Vela.Api.Tests/
RUN dotnet restore api/src/Vela.Api/Vela.Api.csproj
COPY api/src api/src
RUN dotnet publish api/src/Vela.Api/Vela.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Vela.Api.dll"]
