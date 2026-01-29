# OvuDB

Minimal embedded-style database with TCP server and SQL-like query language (ovuRequests).

## Solution structure

| Project | Description |
|---------|-------------|
| **sovudb** | Server library: storage, network, auth, query parser/executor, system DB. |
| **ovudb** | CLI client and entry point; connects to server, runs queries. |
| **ovudb.Tests** | Unit and integration tests. |

## Projects in detail

### sovudb (server library)

- **Core** — `Database`, `Table<T>`, `Column`, `Index`, `DataType`.
- **Storage** — `BinaryStorage`, `FileStorage`, `BufferPool`, `QueryCache`, `MetadataCache`; binary/JSON persistence.
- **Network** — `OvuDbServer`, `Connection`, `ConnectionPool`; auth in `Authentication/`.
- **OvuRequests** — `Parser`, `Executor`, `Optimizer`; AST in `Ast/`.
- **SystemDatabase** — system tables (users, DBs, models); `SystemDatabaseService`, `ModelService`.
- **Configuration** — `ConfigLoader`, `ServerConfig` (YAML).
- **Query** — `QueryBuilder<T>` for programmatic queries.
- **Tools** — `OvuDbSecureInstallation` (initial setup wizard).

Server entry point: `sovudb/Program.cs` (loads config, runs setup if needed, starts `OvuDbServer`). Config file: `ovudbc.yml` (see `ovudbc.yml.example`).

### ovudb (client)

- **Program.cs** — CLI: connect (`-h`, `-P`, `-u`, `-p`), authenticate, run one-off query or interactive mode; commands: `USE db`, `\q`, etc.

### ovudb.Tests

- **Core** — Database, Table, Column, Index, DataType.
- **Storage** — BinaryStorage, FileStorage, BufferPool, QueryCache, Dump, Metadata.
- **Network** — Server, connections, auth, security.
- **OvuRequests** — Parser, Executor, Optimizer, integration, load tests.
- **Integration** — Full-cycle and integration tests.
- **SystemDatabase** — System DB and model service.

## Build and run

```bash
dotnet build
dotnet run --project ovudb          # CLI client (default: localhost:47015)
dotnet run --project sovudb        # Server (uses ovudbc.yml)
dotnet test --project ovudb.Tests  # Tests
```

Default server config: `ovudbc.yml` (port 47015, data in `data/`). First run can start the secure installation wizard if the system database is missing.
