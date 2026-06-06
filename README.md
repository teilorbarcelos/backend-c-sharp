# Advanced C# (ASP.NET Core) Backend API Boilerplate

Arquitetura robusta, moderna e de alta performance construída com **C#**, **ASP.NET Core 10.0** e **Entity Framework Core**. Adota **Vertical Slice Architecture (VSA)** com fatias funcionais de baixo acoplamento e alta coesão.

---

## Tecnologias Core

- **Runtime & SDK:** .NET 10.0
- **Framework Web:** ASP.NET Core Web API
- **ORM:** Entity Framework Core 10
- **Banco de Dados:** SQL Server + Redis (cache de sessões, rate limit)
- **Mensageria:** RabbitMQ com DLX/DLQ, retry, Publisher Confirms
- **Documentação:** OpenAPI 3.0 via `v1/docs`
- **Observabilidade:** OpenTelemetry (OTLP) + Prometheus + Grafana
- **Qualidade:** SonarQube + Husky + dotnet format
- **Testes:** xUnit + Testcontainers (SQL/Redis/RabbitMQ reais)
- **CI/CD:** GitHub Actions

---

## Arquitetura

### Vertical Slice Architecture (VSA)

Código organizado em **Feature Slices** em `src/Features/`. Cada pasta contém todo o ciclo da funcionalidade: Controller, Command/Query DTOs, Validadores, Handlers e Mappers. Sem camadas Service/Repository genéricas.

```
src/
├── Core/             → Primitivas de domínio e CQRS
├── Domain/           → BaseEntity, SearchRequest
├── Shared/           → QueryableExtensions, Cqrs, DateTimeHelper
├── Web/              → Controllers base, Middleware, Filters
├── Database/         → DbContext, Migrations, Entities
├── Infrastructure/   → Auth, Messaging, Pdf, Storage, Auditing, HealthChecks, Configuration
├── Features/         → Auth, User, Role, Feature, Product, Storage, Dashboard, AuditExplorer
└── Migrations/       → EF Core migrations versionadas
```

### Padrões e Práticas

- **CQRS** com MediatR — commands e queries separados por slice
- **FluentValidation** — validação desacoplada dos handlers
- **Registros Imutáveis** (`record`) para DTOs de request/response
- **Soft Delete & LGPD** — exclusão lógica com anonimização
- **RBAC Granular** — `[CheckPermission("feature", "action")]` com permissões view/create/delete/activate

---

## Segurança

### Autenticação JWT + Refresh Token
- Tokens JWT com `jti` único, `sv` (session version) e `kid` (key id)
- Refresh tokens armazenados em Redis por usuário (multi-device)
- Invalidação O(1) via `SessionVersion` — desativar usuário/role invalida todas as sessões instantaneamente

### Anti-Enumeração
- Todos os endpoints de auth (`/login`, `/validate`, `/change`) retornam a **mesma mensagem genérica** independente do erro real
- A causa real é logada internamente para o SOC

### Rate Limit Distribuído
- Limites por endpoint via Redis + Lua script atômico
- Buckets independentes: login (5/min), password_request (3/min), export (10/min), default (100/min)
- Headers: `x-ratelimit-limit`, `x-ratelimit-remaining`, `x-ratelimit-reset`
- Fail-open: Redis indisponível não bloqueia requests

### CORS
- Allowlist explícita por ambiente via `CORS_ALLOWED_ORIGINS`
- `AllowAnyOrigin` + `AllowCredentials` removido (vetor CSRF)

---

## Observabilidade

### OpenTelemetry Tracing
- Tracing distribuído com auto-instrumentação para ASP.NET Core, EF Core, Redis e HTTP Client
- Exportação OTLP para Jaeger/Tempo/Grafana Cloud
- Ativado via `OTEL_ENABLED=true`

### Métricas Prometheus
- `prometheus-net` com métricas nativas do .NET (GC, CPU, ThreadPool)
- Endpoint `/metrics` exposto para scrape
- Dashboard Grafana pré-configurado em `docker-compose.metrics.yml`

### Health Checks
- Endpoint `/health` com checks profundos para SQL, Redis, RabbitMQ e PDF Service
- Status individual por dependência e latência em ms
- Resposta 200 mesmo em degraded (ideal para k8s liveness probe)

---

## Mensageria (RabbitMQ)

- **Publisher Confirms** — garantia de entrega no broker (env `RABBIT_PUBLISHER_CONFIRMS`)
- **DLX/DLQ** — mensagens reprovadas vão para Dead Letter Queue
- **Retry Queue** — fila com TTL de 5s e reenvio automático
- **Prefetch Count** controlado (`RABBIT_PREFETCH_COUNT`, default 16)
- **Message Versioning** — header `x-message-version` em todas as publicações
- **Consumer BackgroundService** — `RabbitMQConsumerService` gerenciado pelo ASP.NET Core lifecycle
- Desabilitável via `MESSAGING_ENABLED=false`

---

## Performance e Banco de Dados

### Índices Hot-Path
10 índices na migration inicial para as queries mais frequentes:
- `User.Email` (unique), `User.CognitoId`, `User.Document`, `User.IdRole`
- `Product.Sku` (unique), `Product.Category`
- `tb_audit` composite `(IdUser, CreatedAt)`

### NoTracking por Default
- `QueryTrackingBehavior.NoTracking` no DbContext — ganho de 20-30% em queries de leitura
- `.AsTracking()` explícito apenas nos 12 handlers que escrevem
- Teste de regressão `DatabaseOptimizationTests` protege contra reverter

### Pipeline de Resiliência (Polly v8)
- PDF Provider com retry (3x), circuit breaker, timeout (10s attempt, 30s total)
- Configurável via env vars `PDF_RESILIENCE_*`
- Aplicado a qualquer cliente HTTP via `AddStandardResilienceHandler()`

---

## Auditoria

- `AuditLogMiddleware` captura toda mutação (POST/PUT/DELETE/PATCH)
- Fila `Channel<T>` com `BackgroundService` — sem ThreadPool starvation
- Batching de 50 registros por transação
- Senhas e campos sensíveis são redactados automaticamente
- Capacidade configurável via `AUDIT_QUEUE_CAPACITY` (default 10000)

---

## Docker

### Dockerfile Multi-stage
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
FROM mcr.microsoft.com/dotnet/aspnet:10.0-jammy-chiseled AS runtime
```

Apenas ~100MB na imagem final (chiseled = sem shell, sem pacotes extras).

### Graceful Shutdown
- Timeout configurável via `SHUTDOWN_TIMEOUT_SECONDS` (default 30s)
- `ApplicationStopping` registrado para drenar requests in-flight e desconectar RabbitMQ antes de parar
- Logs informam início e fim do shutdown

### Init-Container
O mesmo container pode rodar como init job para migrations:
```bash
docker run -e DATABASE_URL=... -e MIGRATE_ONLY=true magebackend
```

---

## Instalação e Execução Local

### Pré-requisitos
- .NET 10 SDK
- Docker & Docker Compose
- EF Core CLI: `dotnet tool install --global dotnet-ef`

### Setup
```bash
make setup                # Ferramentas locais + Husky hooks
dotnet restore src        # Dependências NuGet
make infra-up             # SQL + Redis + RabbitMQ
make db-migrate           # Aplica migrations
make dev                  # Inicia com Hot Reload em :8888
```

### Stack de Observabilidade
```bash
make metrics-up           # Prometheus + Grafana (localhost:3001)
```

### Ambiente
Configure o `.env` na raiz:
```env
PORT=8888
DATABASE_URL="Server=localhost,1433;..."
REDIS_URL="localhost:6379"
RABBIT_URL="amqp://guest:guest@localhost:5672/"
MESSAGING_ENABLED=true
JWT_SECRET="sua-chave-secreta-aqui"
CORS_ALLOWED_ORIGINS="http://localhost:3000,http://localhost:4200"
```

---

## Testes

### Suíte completa (358 testes)
```bash
make test                 # Testes com Testcontainers (SQL/Redis/RabbitMQ)
```

### Cobertura
```bash
make coverage             # Coverlet > 99% line coverage
```

### Qualidade
```bash
make lint                 # Husky + dotnet format + verificação de comentários
make sonar                # SonarQube scan (requer servidor em :9000)
```

### Compliance E2E
```bash
# Projeto separado: mage-backend-compliance
make test-c-sharp         # 50 testes de ponta a ponta
```

---

## Comandos Úteis (Makefile)

| Comando | Descrição |
|---|---|
| `make infra-up` | Sobe SQL + Redis + RabbitMQ |
| `make dev` | Hot Reload em :8888 |
| `make test` | Testes com Testcontainers |
| `make coverage` | Testes + relatório Coverlet |
| `make lint` | Verifica comentários `//` + dotnet format |
| `make db-migrate` | Aplica migrations EF Core |
| `make migration name=...` | Cria nova migration |
| `make generate name=...` | Scaffold de CRUD completo |
| `make generate-storage` | Scaffold de Storage Provider |
| `make metrics-up` | Prometheus + Grafana |
| `make sonar` | SonarQube scan |
| `make setup` | Instala ferramentas + hooks |

---

## Qualidade e CI/CD

### SonarQube Quality Gate
- `new_coverage >= 80%` ✅
- `new_duplicated_lines_density <= 3%` ✅
- `new_violations = 0` ✅
- `caycStatus = compliant` ✅

### GitHub Actions
- CI roda lint + testes + build em cada PR para main/develop
- SonarQube Scan integrado (token via `SONAR_TOKEN`)

### Pre-commit Hook (Husky)
- Bloqueia `// comments` no código-fonte (use `/* */` ou `///`)
- `dotnet format --verify-no-changes` — formatação consistente
