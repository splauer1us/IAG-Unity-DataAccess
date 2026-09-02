# Iag.Unity.DataAccess

A lightweight data-access library that simplifies working with Microsoft SQL Server. It wraps `SqlConnection` and `SqlCommand` in a small set of objects and methods that make stored procedures, ad-hoc SQL, parameter handling, and object mapping straightforward.

📖 **[Full documentation guide](docs/index.md)** — a complete reference covering every feature. A styled HTML version ([docs/index.html](docs/index.html)) is also available to open locally or serve via GitHub Pages.

## Target frameworks

A single package multi-targets:

- **.NET Framework 4.6.2**
- **.NET Framework 4.7.2**
- **.NET Standard 2.0**
- **.NET 8.0**

Built on [`Microsoft.Data.SqlClient`](https://www.nuget.org/packages/Microsoft.Data.SqlClient) `7.0.2`.

> **Note** — the [Microsoft Entra ID](#microsoft-entra-id-azure-authentication) APIs are available on every target **except** .NET Framework 4.6.2. .NET Framework consumers on **4.7.2 or later** resolve the `net472` asset and have them; 4.6.2 – 4.7.1 consumers do not. See [Microsoft Entra ID (Azure) authentication](#microsoft-entra-id-azure-authentication).

## Install

```bash
dotnet add package Iag.Unity.DataAccess.dll
```

## Quick start

Initialize once with a connection string:

```csharp
using Iag.Unity.DataAccess;

DataLibrary.Initialize("Server=.;Database=MyDb;Integrated Security=true;");
```

…or, for a database in Azure, with a Microsoft Entra ID credential:

```csharp
using Azure.Identity;

await DataLibrary.InitializeWithAzureCredentialsAsync(
    new DefaultAzureCredential(),
    "Server=tcp:my-server.database.windows.net,1433;Database=MyDb;");
```

Run a stored procedure and get a `DataTable`:

```csharp
using (var sp = new StoredProcedure("dbo.GetCustomer"))
{
    sp.Parameters["@Id"] = 123;
    DataTable table = sp.OpenTable();
}
```

Run ad-hoc SQL:

```csharp
using (var cmd = new UnitySqlCommand("SELECT * FROM Customer WHERE Id = @Id"))
{
    cmd.Parameters["@Id"] = 123;
    DataTable table = cmd.OpenTable();
}
```

Map rows straight to objects:

```csharp
using (var sp = new StoredProcedure("dbo.GetCustomers"))
{
    List<Customer> customers = sp.GetObjects<Customer>().ToList();
}
```

## Microsoft Entra ID (Azure) authentication

For a database in Azure, initialize with an `Azure.Core.TokenCredential` instead of a connection string that carries credentials. The library stores the **credential**, not a token, and acquires a fresh access token for each connection as it opens — so it keeps working past the ~1 hour lifetime of any single token.

```csharp
using Azure.Identity;

// DefaultAzureCredential: environment → workload identity → managed identity →
// Visual Studio / Azure CLI / Azure PowerShell
await DataLibrary.InitializeWithAzureCredentialsAsync(
    new DefaultAzureCredential(),
    "Server=tcp:my-server.database.windows.net,1433;Database=MyDb;");

// A specific credential is better in production — see the note below
await DataLibrary.InitializeWithAzureCredentialsAsync(
    new ManagedIdentityCredential(clientId: "…"),
    "my-server.database.windows.net", "MyDb");
```

Commands are then used exactly as before — `StoredProcedure`, `UnitySqlCommand`, transactions and the async methods all work unchanged, and the async execute methods acquire the token asynchronously too.

| Member | Description |
| --- | --- |
| `InitializeWithAzureCredentials(credential, connectionString, loggingCallback?)` | Initialize and test the connection. |
| `InitializeWithAzureCredentials(credential, server, db, loggingCallback?)` | Same, building the connection string from parts. |
| `InitializeWithAzureCredentials(connectionString, loggingCallback?)` | Same, using `new DefaultAzureCredential()`. |
| `InitializeWithAzureCredentialsAsync(…, cancellationToken?)` | Async counterparts — **preferred**, see below. |
| `AzureCredential` | The credential in use, or `null` when initialized from a plain connection string. |
| `AzureSqlScope` | Token scope. Defaults to `DefaultAzureSqlScope`; change it for a sovereign cloud. |
| `DefaultAzureSqlScope` | `https://database.windows.net/.default` (public cloud). |

Things worth knowing:

- **Prefer the `…Async` overloads.** A synchronous `Open()` blocks on token acquisition, and the first acquisition through `DefaultAzureCredential` can take seconds while it probes its chain of sources.
- **Prefer a specific credential in production.** `ManagedIdentityCredential`, or a `DefaultAzureCredential` built with `DefaultAzureCredentialOptions` that excludes the sources you don't use — the full chain is slow and can pick up an unintended identity.
- **The connection string must not carry credentials of its own.** `Integrated Security`, `User ID`, `Password` and `Authentication` are rejected with a message saying what to remove, because SqlClient will not accept an access token alongside them. `Encrypt` is upgraded to `Mandatory` if the string explicitly opted out; an explicit `Encrypt=Strict` is left alone.
- **A connection you supply yourself is untouched.** Passing your own `SqlConnection` to a command bypasses `DataLibrary.GetConnection`, so set `AccessTokenCallback` on it yourself if it needs Entra authentication.
- **A later plain `Initialize(...)` disarms Entra authentication**, clearing `AzureCredential` so the new connection string's own credentials are used.
- **Access tokens are never logged.** A failed acquisition surfaces as an `InvalidOperationException` naming the credential type and scope, with the `Azure.Identity` exception as its inner exception, and is passed to `LoggingCallback`.
- **Not available on .NET Framework 4.6.2** — see [Target frameworks](#target-frameworks).

## `Prepare()` is optional

`Prepare()` derives a stored procedure's parameter metadata from the server (types, sizes, and **OUTPUT/RETURN** directions). It is now opt-in:

- **Skip `Prepare()`** — supply parameters by name and execute directly. No metadata round-trip; parameters are sent as-is. Best for input-only procedures.
- **Call `Prepare()`** — when you need exact parameter typing or want to read back output/return values.

```csharp
// No Prepare(): fast path, input parameters only
using (var sp = new StoredProcedure("dbo.Touch"))
{
    sp.Parameters["@Id"] = 123;
    sp.Execute();
}

// Prepare(): derived metadata, output parameters captured
using (var sp = new StoredProcedure("dbo.CreateOrder"))
{
    sp.Prepare();
    sp.Parameters["@CustomerId"] = 123;
    sp.Execute();
    int newId = (int)sp.Parameters["@NewOrderId"];
}
```

Derived parameter metadata is cached per server/database/procedure, so repeated `Prepare()` calls avoid additional round-trips. A cache entry is automatically invalidated if a command using it fails, so a changed procedure signature self-heals on the next call.

### Output & return-value parameters without `Prepare()`

To capture outputs on the fast path, declare them explicitly and read them back through the normal channels:

```csharp
using (var sp = new StoredProcedure("dbo.CreateOrder"))
{
    sp.Parameters["@CustomerId"] = 123;                  // input
    sp.AddOutputParameter("@NewOrderId", SqlDbType.Int); // output
    sp.AddReturnParameter();                             // return value

    sp.Execute();

    int newId = (int)sp.Parameters["@NewOrderId"];       // output → Parameters
    int code  = sp.ReturnValue ?? 0;                     // return → ReturnValue
}
```

Seeding a value for a declared output parameter promotes it to `InputOutput` automatically. `DeclareParameter(name, type, direction, size)` is the general form if you need a specific direction or size.

## Async

Every command-executing method has an `...Async` counterpart that accepts an optional `CancellationToken`. They perform genuine asynchronous I/O — the connection is opened with `OpenAsync`, and result sets are read with `ExecuteReaderAsync`/`ReadAsync` (not a blocking `SqlDataAdapter.Fill`).

```csharp
using (var sp = new StoredProcedure("dbo.GetCustomers"))
{
    List<Customer> customers = (await sp.GetObjectsAsync<Customer>(cancellationToken: ct)).ToList();
}
```

| Sync | Async |
| --- | --- |
| `Execute()` | `ExecuteAsync(ct)` |
| `ExecuteScalar()` | `ExecuteScalarAsync(ct)` |
| `ExecuteScalar<T>(def)` | `ExecuteScalarAsync<T>(def, ct)` |
| `ExecuteReader(behavior)` | `ExecuteReaderAsync(behavior, ct)` |
| `GetDataReader()` | `GetDataReaderAsync(ct)` |
| `OpenDataSet(name)` | `OpenDataSetAsync(name, ct)` |
| `OpenTable(name)` | `OpenTableAsync(name, ct)` |
| `GetRows()` | `GetRowsAsync(ct)` |
| `GetRowSets()` | `GetRowSetsAsync(ct)` |
| `GetObject<T>(...)` | `GetObjectAsync<T>(..., ct)` |
| `GetObjects<T>(...)` | `GetObjectsAsync<T>(..., ct)` |

On **.NET 8.0**, `BaseCommand` also implements `IAsyncDisposable`, so commands can be used with `await using`. `Prepare()` and the transaction methods (`BeginTransaction`/`Commit`/`Rollback`) remain synchronous — `SqlCommandBuilder.DeriveParameters` has no async API — but async execute methods work fine inside a transaction begun synchronously.

## Transactions

```csharp
using (var cmd1 = new UnitySqlCommand("..."))
using (var cmd2 = new UnitySqlCommand("..."))
{
    SqlTransaction trans = UnitySqlCommand.CreateTransaction(cmd1, cmd2);
    try
    {
        cmd1.Execute();
        cmd2.Execute();
        trans.Commit();
    }
    catch
    {
        trans?.Rollback();
    }
}
```

`BeginTransaction()` uses `IsolationLevel.ReadCommitted` by default. Pass an explicit level with the `BeginTransaction(IsolationLevel)` overload when you need something stricter.

## Error context and sensitive data

When a command fails, the resulting `ContextualSqlException.Context` includes the command text and the parameter **names and types**. Parameter *values* are omitted by default because they often carry PII, tokens, or credentials. To include values (e.g. in a non-production diagnostic build), opt in:

```csharp
DataLibrary.IncludeParameterValuesInErrors = true;
```

## Tests

`Iag.Unity.DataAccess.Tests` (xunit + Shouldly, net8.0) holds both unit and integration tests. Unit tests need nothing but the SDK:

```bash
dotnet test --filter "Category!=Integration"
```

Integration tests are marked `[Trait("Category", "Integration")]` and stand up a real SQL Server 2022 in Docker via [Testcontainers](https://testcontainers.com/), creating a `Tests` database with the widget table and stored procedures the tests use:

```bash
dotnet test --filter "Category=Integration"
```

They need a reachable Docker daemon. If Docker lives in WSL rather than on the Windows host, run the command from inside WSL (`wsl -e bash -lc "cd /mnt/<repo path> && dotnet test …"`) — a Windows-side test host cannot reach a WSL-only daemon.

`DataLibrary` is static, so the assembly runs test collections serially (`AssemblyInfo.cs`); don't re-enable parallelization without giving the connection-string state somewhere per-collection to live.

## Full API documentation

See the [package README](Iag.Unity.DataAccess/README.md) for the complete API — all execution methods (`OpenDataSet`, `GetRows`, `ExecuteScalar<T>`, `ExecuteReader`, …), object-mapping options (`[FieldToProperty]`, custom map functions, translation handlers), and additional transaction patterns.

## License

[MIT](https://opensource.org/licenses/MIT)
