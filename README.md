# Vela

"Vela" is a code name. Flutter app (`app/`) plus .NET API (`api/`).

## Prerequisites
- .NET 10 SDK, Flutter 3.47+, Docker with compose.
- In this repo's dev shell: `source scripts/dev-env.sh`.

## Local services
```
docker compose up -d     # postgres:16 (db "vela") + Azurite
docker compose ps        # both should report healthy
docker compose down
```

## API
```
dotnet test api/Vela.sln
dotnet run --project api/src/Vela.Api   # GET /health
```

## App
```
cd app && flutter test
```

Layout: `contract/` API contract, `api/` .NET solution, `app/` Flutter, `infra/` Bicep, `docs/` specs.
