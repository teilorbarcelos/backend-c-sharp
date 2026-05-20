.PHONY: infra-up infra-down dev db-migrate db-seed metrics-up metrics-stop metrics-down test

infra-up:
	docker compose -f docker-compose.infra.yml up -d

infra-down:
	docker compose -f docker-compose.infra.yml down

dev:
	DOTNET_USE_POLLING_FILE_WATCHER=1 dotnet watch --project src/MageBackend.csproj run

db-migrate:
	dotnet ef database update --project src/MageBackend.csproj --startup-project src/MageBackend.csproj

metrics-up:
	@echo "📈 Subindo stack de métricas (Prometheus & Grafana)..."
	docker compose -f docker-compose.metrics.yml up -d

metrics-stop:
	@echo "🛑 Parando stack de métricas..."
	docker compose -f docker-compose.metrics.yml stop

metrics-down:
	@echo "🗑️ Removendo stack de métricas..."
	docker compose -f docker-compose.metrics.yml down

test:
	dotnet test tests/MageBackend.Tests.csproj -m:1

coverage:
	@echo "📊 Gerando relatório de cobertura de código..."
	dotnet test tests/MageBackend.Tests.csproj -m:1 /p:CollectCoverage=true
	@echo "\n--- Resumo de Cobertura ---"
