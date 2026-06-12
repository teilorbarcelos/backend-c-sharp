.PHONY: infra-up infra-down infra-clean dev db-migrate db-seed metrics-up metrics-stop metrics-down test coverage setup lint generate migration db-update sonar

infra-up:
	docker compose -f docker-compose.infra.yml up -d

infra-down:
	docker compose -f docker-compose.infra.yml down

infra-clean:
	@echo "🧹 Removendo containers e volumes (resetando banco)..."
	docker compose -f docker-compose.infra.yml down -v

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

setup:
	@echo "⚙️ Instalando ferramentas e hooks..."
	dotnet tool restore
	dotnet husky install
	@echo "✅ Setup completo!"

lint:
	@echo "🔍 Verificando comentários // no código-fonte..."
	@! grep -rn '[^:/]//\|^//' src/ --include='*.cs' | grep -v '///' | grep -v '://' || \
		(echo "❌ Encontrados comentários // no código-fonte" && exit 1)
	@echo "✅ Nenhum comentário // encontrado"
	@echo ""
	@echo "🎨 Executando dotnet format..."
	dotnet format src/MageBackend.csproj --verify-no-changes

generate:
	@python3 scripts/generate_crud.py $(name)

generate-storage:
	@python3 scripts/generate_storage.py

migration:
	@if [ -z "$(name)" ]; then \
		read -p "Enter migration name: " MIGN; \
		if [ -z "$$MIGN" ]; then echo "❌ Migration name cannot be empty"; exit 1; fi; \
		dotnet ef migrations add $$MIGN -p src/MageBackend.csproj; \
	else \
		dotnet ef migrations add $(name) -p src/MageBackend.csproj; \
	fi

db-update:
	dotnet ef database update -p src/MageBackend.csproj

sonar:
	@echo "🔍 Rodando scan do SonarQube (análise C# + cobertura)..."
	./scripts/sonar-scan.sh "teilorbarcelos_backend-csharp" "Backend CSharp"

