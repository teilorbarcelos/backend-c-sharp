# Advanced C# (ASP.NET Core) Backend API Boilerplate

Uma arquitetura robusta, moderna e de alta performance construída com **C#**, **ASP.NET Core 10.0** e **Entity Framework Core (EF Core)**. O projeto adota o padrão de **Vertical Slice Architecture (VSA)** para garantir fatias funcionais de baixo acoplamento, alta coesão e facilidade de manutenção.

---

## 🚀 Tecnologias Core

- **Runtime & SDK:** [.NET 10.0](https://dotnet.microsoft.com/) (Extrema performance e recursos modernos do C#)
- **Framework Web:** [ASP.NET Core Web API](https://learn.microsoft.com/en-us/aspnet/core/)
- **ORM:** [Entity Framework Core (EF Core)](https://learn.microsoft.com/en-us/ef/core/)
- **Banco de Dados:** SQL Server (Produção/Desenv) & Redis (Cache de Sessões e Rate Limit)
- **Mensageria:** RabbitMQ (Comunicação Assíncrona e Filas de Mensagem)
- **Documentação:** Swagger (OpenAPI 3.0) com UI integrada em `/v1/docs`
- **Métricas e Observabilidade:** Prometheus-net + Grafana (com Dashboards pré-configurados)
- **CI/CD:** GitHub Actions configurado para Build e Testes Automatizados
- **Qualidade de Código:** Husky (Git Hooks) + dotnet format (Linting)
- **Testes:** xUnit + Testcontainers.NET (Criação dinâmica de containers SQL Server, Redis e RabbitMQ durante o ciclo de testes)

---

## 🏗️ Arquitetura e Padrões de Projeto

### Vertical Slice Architecture (VSA)
Em vez de organizar o código por camadas técnicas (Controllers, Services, Repositories), o projeto organiza o código em **Feature Slices** (Fatias Verticais) localizadas na pasta `src/Features`.
- Cada pasta representa uma funcionalidade de negócios (Ex: `Feature`, `Auth`, `Role`, `User`, `Storage`).
- Todo o ciclo da funcionalidade (Controller, Request/Response DTOs, Validador, Lógica e Mapeamento de Entidades) fica agrupado, facilitando a alteração e diminuindo acoplamento.

### Record Types & FluentValidation
Uso de C# `record` types imutáveis para DTOs de Request e Response, garantindo segurança na transferência de dados. As validações de payload são desacopladas da lógica de negócios e declaradas usando `FluentValidation`.

### Filtros Dinâmicos, Paginação e Ordenação
Um mecanismo robusto construído sobre métodos de extensão `IQueryable` (`QueryableExtensions.cs`). Permite que os controladores recebam filtros de busca flexíveis, ranges de datas, ordenação e paginação, compilando tudo dinamicamente em instruções SQL nativas executadas no banco de dados.

### Soft Delete & LGPD Compliance
- Suporte nativo a exclusão lógica via campos `is_deleted` e `deleted_at`.
- Funcionalidade para anonimização LGPD de informações sensíveis (como nome e email de usuários deletados).

### Dashboard Analítico com T-SQL Nativo
Endpoint analítico (`GET /v1/dashboard/stats`) projetado para retornar métricas consolidadas em séries temporais diárias e rankings de atividade.
- **T-SQL Parametrizado:** Para obter máxima performance de execução no banco de dados SQL Server, as consultas de agrupamento e ordenação analítica utilizam comandos SQL nativos via `SqlQueryRaw`.

---

## 🔐 Segurança e Controle de Acesso (RBAC)

### Autenticação JWT e Sessões via Redis
- Autenticação baseada em tokens JWT (com suporte a Refresh Token).
- Rastreamento em tempo real de sessões de usuário no Redis.
- Controle centralizado de invalidação de tokens via `SessionManager`. Desativar um usuário ou alterar suas permissões invalida instantaneamente suas sessões ativas no Redis.

### RBAC Granular (Role-Based Access Control)
- Controle de acesso fino por funcionalidade baseado em permissões (ex: `view`, `create`, `delete`, `activate`).
- Verificação feita de forma declarativa nos métodos dos controladores usando o atributo personalizado `[CheckPermission("recurso", "acao")]`.

### Rate Limiting Inteligente
- Middleware integrado que protege a API contra abusos (DoS/Brute Force).
- Resposta automática com headers HTTP adequados (`x-ratelimit-limit`, `x-ratelimit-remaining`).

---

## 📝 Auditoria e Logs do Sistema

### Trilha de Auditoria Automática
O `AuditLogMiddleware` intercepta e grava automaticamente na tabela `tb_audit_log` todas as operações de mutação do banco de dados (POST, PUT, DELETE, PATCH). Registra o IP do solicitante, rota, ID do usuário logado, timestamps e oculta dados confidenciais (como senhas).
O projeto também conta com o módulo `AuditExplorerController`, que permite aos administradores buscar e filtrar esse histórico de ponta a ponta.

### Logs de Erro Centrais
O `ErrorHandlerMiddleware` gerencia globalmente falhas de execução e validação da API. Exceções não tratadas são transformadas em respostas HTTP estruturadas amigáveis e salvas de forma detalhada na tabela `tb_error_log`.

---

## 🛠️ CLI de Geração Automática de Código (Scaffolder)

O projeto dispõe de scripts geradores interativos e automatizados para acelerar o desenvolvimento, garantindo paridade com a arquitetura do projeto.

### 1. Gerador de CRUD Completo
O script `scripts/generate_crud.py` lê novas entidades definidas no arquivo `src/Database/Entities.cs` e constrói fatias verticais de código completas, incluindo testes automatizados de ponta a ponta e integração com o banco de dados.

#### Passo a Passo:
1. **Defina a Entidade:**
   Adicione a nova classe de modelo no arquivo `src/Database/Entities.cs`:
   ```csharp
   public class Category
   {
       public string Id { get; set; } = string.Empty;
       public string Name { get; set; } = string.Empty;
       public bool IsDeleted { get; set; }
   }
   ```
2. **Execute o Comando do Scaffolder:**
   ```bash
   make generate name=Category
   ```
3. **Configure o RBAC de forma interativa:**
   O terminal solicitará informações de registro da feature no banco:
   - Se deseja adicionar as permissões ao banco.
   - Nome amigável e descrição da feature.
   - Se deseja associar a nova feature automaticamente ao perfil Administrador.
4. **Aplique as Migrations do EF Core:**
   O script perguntará se você deseja criar e aplicar a migration imediatamente no banco de dados.
   
O resultado é um módulo completo contendo rotas REST, RBAC integrado, testes cobrindo rotas CRUD, paginação, filtros e auditoria.

### 2. Gerador de Storage Providers
O script `scripts/generate_storage.py` permite alternar dinamicamente o provedor de persistência de arquivos da API, instalando dependências NuGet e gerando a classe concreta do driver e seus respectivos testes unitários mockados.

#### Passo a Passo:
1. **Execute o Comando:**
   ```bash
   make generate-storage
   ```
2. **Selecione a opção desejada:**
   - `[1] Local Storage` (Persistência no diretório local `StorageData`)
   - `[2] AWS S3` (Integração com Amazon S3)
   - `[3] Google Cloud Storage` (Integração com GCS)
   - `[4] Azure Blob Storage` (Integração com Azure Blobs)
3. **Atualize o arquivo `.env` com as credenciais do provedor selecionado:**
   - **AWS S3:** `AWS_ACCESS_KEY`, `AWS_SECRET_KEY`, `AWS_BUCKET_NAME`
   - **GCS:** `GCS_BUCKET_NAME` (e caminho das credenciais Google)
   - **Azure:** `AZURE_STORAGE_CONNECTION_STRING`, `AZURE_CONTAINER_NAME`

Tanto o driver de nuvem selecionado quanto seus respectivos testes mockados com `Moq` são gerados na hora, mantendo a cobertura de código da aplicação.

---

## 📩 Mensageria (RabbitMQ)

A integração com o RabbitMQ permite publicar e subscrever a eventos de fila de forma assíncrona por meio do `RabbitMQProvider`.
- Ativação controlada pela variável `.env` `MESSAGING_ENABLED=true`.
- Rastreia e reconecta a filas de forma automática caso o broker caia.
- Totalmente testado localmente via containers isolados.

---

## 📊 Observabilidade e Métricas (Prometheus & Grafana)

A aplicação conta com uma stack completa de observabilidade para monitoramento em tempo real, já provisionada (Infrastructure as Code).

- **Prometheus-net:** Expõe e captura automaticamente métricas do .NET 10 (Garbage Collector, CPU, Memória, Threads) e das requisições HTTP (latência, status code, taxa de erros).
- **Grafana Dashboard Pré-configurado:** O repositório já inclui configurações e dashboards profissionais do Grafana! Ao rodar a stack, você ganha acesso instantâneo a gráficos de uso de CPU, requisições por segundo, taxa de sucesso e tempos de resposta, sem precisar de nenhuma configuração manual.

### 📈 Métricas Avançadas para Alta Concorrência (Nativas do .NET)
Para ambientes de alta volumetria, a nossa stack já suporta e exporta métricas essenciais de nível enterprise, que podem ser facilmente adicionadas aos dashboards:
- **Thread Pool Starvation:** Monitoramento de threads em uso vs. na fila, crucial para evitar gargalos em operações assíncronas.
- **Database Connection Pool:** Acompanhamento de conexões ativas e em espera com o SQL Server para prevenir *Connection Pool Exhaustion*.
- **Garbage Collection (GC):** Frequência de coletas por geração (Gen 0, 1 e 2) e uso da *Large Object Heap (LOH)*, essenciais para evitar *GC Pauses* em picos de tráfego.
- **Hit Ratio de Cache (Redis):** Taxa de acertos vs erros (*hits/misses*) nas sessões e rate limiters.
- **Saúde das Filas (RabbitMQ):** Profundidade de filas e backpressure (mensagens *Unacked* e prontas).

### Como acessar:
1. Suba a stack de métricas:
   ```bash
   make metrics-up
   ```
2. Acesse o **Grafana** em `http://localhost:3001`
   - **Login padrão:** `admin` / **Senha padrão:** `admin`
3. O **Prometheus** (scraper) roda de forma invisível no background em `http://localhost:9090`.

---

## ⚙️ Instalação e Execução Local

### Pré-requisitos
- .NET 10 SDK
- Docker & Docker Compose
- EF Core CLI instalado globalmente:
  ```bash
  dotnet tool install --global dotnet-ef
  ```

### Configuração Inicial
1. **Instale as ferramentas locais (Husky, formatadores):**
   ```bash
   make setup
   ```
2. **Restaure as dependências NuGet:**
   ```bash
   dotnet restore src
   ```
2. **Configure suas variáveis de ambiente:**
   Crie ou edite o arquivo `.env` na raiz do projeto:
   ```env
   PORT=8888
   DATABASE_URL="Server=localhost,1433;Database=backend_c_sharp;User Id=sa;Password=YourPassword123;TrustServerCertificate=True;"
   REDIS_URL="localhost:6379"
   RABBIT_URL="amqp://guest:guest@localhost:5672/"
   MESSAGING_ENABLED=true
   JWT_SECRET="sua-chave-secreta-muito-longa-e-segura-de-exemplo-aqui"
   ```

### Execução da Infraestrutura
Suba os containers de banco de dados, cache e mensageria:
```bash
make infra-up
```

### Executar Migrations
Crie e atualize as tabelas do banco de dados local:
```bash
make db-migrate
```

### Iniciar a API
Inicie o servidor de desenvolvimento com Hot Reload ativo:
```bash
make dev
```

A API estará disponível no endereço: `http://localhost:8888/v1/docs` (Swagger UI).

---

## 🧪 Rodando Testes e Cobertura

O projeto garante o funcionamento correto através de testes de integração agressivos baseados em **Testcontainers.NET**. Toda vez que os testes rodam, instâncias reais do SQL Server, Redis e RabbitMQ sobem isoladamente dentro do Docker para executar as rotinas.

### Executar a suíte de testes:
```bash
make test
```

### Gerar relatório de cobertura de código (Coverlet):
```bash
make coverage
```
> O Git Hook pré-commit (`Husky`) impedirá o envio de códigos que não atendam aos requisitos mínimos de cobertura de código configurados (Mínimo: 95% de Cobertura de Linha).

### Linting e Padrão de Código
Para garantir a qualidade, o projeto possui comandos de lint e formatação que barram commits com código sujo:
```bash
make lint
```
