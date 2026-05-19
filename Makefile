.PHONY: infra-up infra-down dev db-migrate db-seed

infra-up:
	docker compose -f docker-compose.infra.yml up -d

infra-down:
	docker compose -f docker-compose.infra.yml down

dev:
	DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet watch --project src/MageBackend.csproj run

db-migrate:
	dotnet ef database update --project src/MageBackend.csproj --startup-project src/MageBackend.csproj
