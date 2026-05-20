# Advanced C# (ASP.NET Core) Backend API

Uma arquitetura robusta e moderna construída com **C#**, **ASP.NET Core 10.0** e **Entity Framework Core (EF Core)**, adotando o padrão de **Vertical Slice Architecture (VSA)** para garantir alta performance, baixo acoplamento entre módulos e facilidade de manutenção.

---

## 🚀 Tecnologias Core

O projeto utiliza o estado da arte do ecossistema .NET:

- **Runtime & SDK:** [.NET 10.0](https://dotnet.microsoft.com/) (Performance extrema, recursos modernos de linguagem C# e compilação nativa)
- **Framework Web:** [ASP.NET Core Web API](https://learn.microsoft.com/en-us/aspnet/core/) (Estrutura de alta performance para APIs REST)
- **ORM:** [Entity Framework Core (EF Core)](https://learn.microsoft.com/en-us/ef/core/) (Mapeamento relacional de banco de dados idiomático)
- **Database:** SQL Server (Principal) & Redis (Cache de Sessões e Rate Limit)
- **Documentação:** Swagger (OpenAPI 3.0) com UI integrada em `/v1/docs`
- **Métricas:** Prometheus-net (Coleta de telemetria nativa da API)
- **Testes:** xUnit + Testcontainers.NET — **49 cenários de integração** (paridade total com a suite de compliance Python)


---

## ✨ Principais Funcionalidades (Implementadas)

### 🔐 Segurança e Autenticação
- **RBAC Avançado (Baseado em Permissões):** Controle de acesso granular por funcionalidade (`view`, `create`, `delete`, `activate`) via atributo personalizado `[CheckPermission]` em nível de endpoint.
- **Gerenciamento de Sessões via Redis:** Rastreamento e armazenamento de tokens de sessão ativos com suporte a invalidação instantânea por usuário ou por cargo (Role) usando o `SessionManager`.
- **Rate Limiting Inteligente:** Proteção contra abusos configurada de forma global e com verificação em memória de alto desempenho.

### 🏗️ Arquitetura de Fatias Verticais (Vertical Slice Architecture)
- **Feature Folders:** Todo o fluxo de uma funcionalidade (definição do Controller, DTOs de Request/Response, mapeamentos e interações com o banco de dados) fica contido de forma coesa dentro de sua respectiva pasta em `src/Features`.
- **Record Types & FluentValidation:** DTOs de Request/Response imutáveis usando C# `record` types e validações de payload declarativas desacopladas com `FluentValidation`.
- **Filtragem Dinâmica & Paginação:** Sistema integrado de busca e filtros complexos (incluindo ranges de data e ordenação) processados diretamente no banco de dados via métodos de extensão `IQueryable` (`QueryableExtensions.cs`).
- **Soft Delete:** Suporte nativo a exclusão lógica (`is_deleted` e `deleted_at`) nas entidades de banco de dados.

### 📝 Auditoria, Métricas e Logs
- **Audit Logs Automáticos:** Middleware dedicado (`AuditLogMiddleware`) que intercepta e registra automaticamente no banco de dados todas as mutações com metadados do usuário, IP, payload e rota.
- **Error Logs no DB:** Tratamento global de exceções (`ErrorHandlerMiddleware`) que captura falhas não tratadas e as registra na tabela `tb_error_log` do schema `audit`.
- **Prometheus Metrics:** Exposição nativa de métricas de requests no endpoint `/metrics` utilizando `prometheus-net`.
- **Audit Explorer:** Rotas administrativas integradas para consulta rápida de logs de auditoria e erros do sistema.

### 📄 PDF Service Integration
- **PDF Debug Endpoints:** Rotas prontas para geração e envio de PDFs com suporte a streaming bypass.

### 📊 Real-time Observability (Prometheus & Grafana)
- **Stack Docker de Métricas:** Configuração inclusa de containers Prometheus e Grafana para coletar métricas de performance da API em tempo real.

---

## ⚙️ Configuração Local

### Pré-requisitos
- .NET 10 SDK
- Docker & Docker Compose
- Ferramenta EF Core CLI instalada globalmente (`dotnet tool install --global dotnet-ef`)

### Instalação e Restauração de Pacotes
```bash
dotnet restore src
```

### Variáveis de Ambiente
Crie ou ajuste o arquivo `.env` na raiz do projeto (o projeto lerá automaticamente usando o `dotenv.net`):
```env
PORT=8888
DATABASE_URL="Server=localhost,1433;Database=backend_c_sharp;User Id=sa;Password=YourPassword123;TrustServerCertificate=True;"
REDIS_URL="localhost:6379"
JWT_SECRET="super-secret-key-that-is-very-long-and-secure-123456"
```

### Infraestrutura (Docker)
Para iniciar os serviços de banco de dados (SQL Server) e cache (Redis):
```bash
make infra-up       # Sobe SQL Server e Redis
make infra-down     # Para e remove os containers de infra
make metrics-up     # Sobe Prometheus e Grafana (Acesse Grafana na porta 3001)
make metrics-down   # Para e remove os containers de métricas
```

### Executar Migrations do Banco
```bash
make db-migrate
```

### Rodando o Projeto
```bash
make dev            # Inicia o servidor local em modo watch (Hot Reload)
```

---

## 📖 API Documentation

A documentação interativa e os endpoints de observabilidade ficam disponíveis em:
- **Swagger UI:** `http://localhost:8888/v1/docs`
- **Prometheus UI:** `http://localhost:9090`
- **Grafana Dashboard:** `http://localhost:3001` (User: `admin` / Pass: `admin`)
- **Health Check:** `http://localhost:8888/health`
- **PDF Debug (GET):** `http://localhost:8888/v1/debug/pdf`
- **PDF Debug (POST):** `http://localhost:8888/v1/debug/pdf`

---

## 🧪 Testes de Integração (49 Cenários de Compliance)

O projeto possui uma suite completa de **49 testes de integração** que replicam localmente todos os cenários da suite de compliance Python (`mage-backend-compliance`). Os testes usam **Testcontainers.NET** para subir containers isolados de SQL Server e Redis automaticamente.

```bash
make test           # Executa os 49 cenários de integração
```

### Cobertura dos Cenários

| Módulo | Cenários | Descrição |
|--------|----------|-----------|
| **01. Auth & Session** | 9 | Login, refresh token, /me, session Redis, invalidação, user/role inativos |
| **02. RBAC** | 2 | Permissões bloqueadas (403) e permitidas por feature |
| **03. Schema Validation** | 2 | Campos obrigatórios ausentes, rejeição de campos desconhecidos |
| **04. Dynamic Filters** | 9 | Busca, paginação, ordenação, filtro por status, range de datas, limites |
| **05. Audit Logs** | 3 | Registro de mutações, exclusão de requests não autenticados, scrubbing de senha |
| **06. Soft Delete** | 2 | Anonimização LGPD de usuários, soft delete de roles |
| **07. Observability** | 2 | Health check (/health) e Prometheus (/metrics) |
| **08. Rate Limit** | 1 | Headers x-ratelimit-limit e x-ratelimit-remaining |
| **09. Status Toggle** | 9 | Toggle de product/role/user com RBAC (forbidden/allowed) |
| **10. Role Features** | 4 | Listagem de features, RBAC, schema de role por ID |
| **11. Session Invalidation** | 4 | Invalidação ao desativar/atualizar role e user |
| **12. Error Logs** | 1 | Registro de erros de validação no banco |
| **13. PDF Debug** | 1 | Endpoint de geração de PDF |
| **Total** | **49** | **Paridade total com compliance Python** |

---

## 📋 Checklist de Paridade de Infraestrutura com o Node.js Backend

Para atingir a paridade total de funcionalidades e DX (Developer Experience) com o projeto em Node.js, os seguintes itens devem ser implementados no boilerplate C# utilizando boas práticas .NET:

### 1. 📦 Storage Providers (Multi-Provider)
- [ ] **Criar Abstração `IStorageProvider`:** Definir o contrato para upload, download e exclusão de arquivos.
- [ ] **Implementar Local Storage Driver:** Driver para persistir arquivos localmente em disco.
- [ ] **Drivers de Nuvem (AWS S3, Google Cloud Storage, Azure Blob):** Desenvolver drivers integrados e prontos para uso em produção.

### 2. 📩 Mensageria (RabbitMQ Integration)
- [x] **Provedor de Mensageria:** Desenvolver um serviço integrado com RabbitMQ para publicação e consumo de mensagens assíncronas, ativado condicionalmente via variável de ambiente `MESSAGING_ENABLED=true` no `.env`.

### 3. 🛠️ CLI de Geração de Código (Generator)
- [ ] **Scaffolder de Feature Slices (CLI):** Criar uma CLI ou script (ex: .NET tool customizada ou script de terminal) capaz de criar automaticamente a pasta de Features (Controller, DTOs, Entidades) ao informar o nome do novo recurso, acelerando a criação de CRUDs seguindo o padrão de fatias verticais.

### 4. 🧪 Testes de Integração e Testcontainers
- [x] **Suite de Testes de Integração (49 cenários):** Todos os 49 cenários da suite de compliance Python foram replicados localmente em C# com xUnit, independentes de qualquer infra externa.
- [x] **Testcontainers.NET:** Containers reais de SQL Server e Redis são provisionados automaticamente em cada execução de teste.
- [x] **Paridade com Compliance Python:** Cobertura completa dos 13 módulos de compliance (Auth, RBAC, Schema, Filters, Audit, Soft Delete, Observability, Rate Limit, Status, Role Features, Session Invalidation, Error Logs, PDF Debug).

### 5. ⚙️ Pre-commit Hooks & Linter
- [ ] **Husky.NET & Git Hooks:** Configurar githooks para formatar automaticamente o código C# (usando `dotnet format`) e rodar testes unitários locais antes de permitir cada commit.
