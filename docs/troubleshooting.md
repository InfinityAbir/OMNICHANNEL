# Troubleshooting

## `dotnet test` fails on `AppDbContextConnectivityTests.CanConnect_ToConfiguredPostgres`

Postgres isn't running. Start it:

```bash
docker compose up -d postgres
```

Wait for the container healthcheck to pass (`docker compose ps`) before re-running tests.

## Docker Compose can't reach the daemon on Windows

`failed to connect to the docker API at npipe:////./pipe/dockerDesktopLinuxEngine` — Docker
Desktop isn't running yet. Start Docker Desktop and wait for "Engine running" in its status bar,
then retry.

## `ng lint` warns about angular-eslint version mismatch

`angular-eslint v22 is intended for Angular v22` — this workspace is on Angular 21 (current CLI
default at scaffold time). Cosmetic warning, not a failure; linting still runs correctly.
Revisit when upgrading Angular.
