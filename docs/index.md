# Iag.Unity.DataAccess — Documentation

> **v2.3.0** · MIT · Built on Microsoft.Data.SqlClient 7.0.2 · Targets **net462**, **net472**, **netstandard2.0**, **net8.0**

A thin, honest wrapper over `SqlConnection` and `SqlCommand`. Iag.Unity.DataAccess turns stored procedures and ad-hoc SQL into a few small objects — parameters, object mapping, transactions, and full async — without hiding what ADO.NET is actually doing underneath.

```csharp
using (var sp = new StoredProcedure("dbo.GetCustomers"))
{
    var customers = (await sp.GetObjectsAsync<Customer>()).ToList();
}
```

### Contents

- [Overview](#overview)
- [Installation](#installation)
- [Getting started](#getting-started)
- [Microsoft Entra ID (Azure) authentication](#microsoft-entra-id-azure-authentication)
- [Command types](#command-types)
- [Parameters](#parameters)
- [Executing commands](#executing-commands)
- [Async](#async)
- [Object mapping](#object-mapping)
- [Transactions](#transactions)
- [Error handling](#error-handling)
- [Performance](#performance)
- [Security](#security)
- [API reference](#api-reference)
- [What's new in 2.3.0](#whats-new-in-230)
- [What's new in 2.2.0](#whats-new-in-220)

---

## Overview

The library exposes two command objects — `StoredProcedure` and `UnitySqlCommand` — that share a common base, `BaseCommand`. You supply parameters through a simple dictionary, call an execution method, and get back a `DataTable`, `DataSet`, reader, scalar, or a list of mapped objects. A static `DataLibrary` holds the default connection string so you rarely pass connections around.

| Capability | What it gives you |
| --- | --- |
| **Simple params** | Set `Parameters["@Id"] = 123`. No `SqlParameter` ceremony for the common case. |
| **Optional Prepare** | Skip the server round-trip for input-only procs; call `Prepare()` when you want derived types and output values. |
| **Object mapping** | `GetObjects<T>()` maps rows to typed objects by name, by attribute, or by a custom function. |
| **Full async** | Every execute method has an `...Async` twin with `CancellationToken` support. |
| **Entra ID auth** | Connect to Azure SQL with a `TokenCredential`; tokens are acquired per connection and refreshed automatically. |

---

## Installation

Install from NuGet. The single package multi-targets .NET Framework 4.6.2, .NET Framework 4.7.2, .NET Standard 2.0, and .NET 8.0, and is built on `Microsoft.Data.SqlClient` 7.0.2.

> **Note** — the [Microsoft Entra ID](#microsoft-entra-id-azure-authentication) APIs are available on every target **except** .NET Framework 4.6.2, where the `netstandard2.0` build of `Azure.Identity` would require extensive binding redirects. NuGet prefers a same-family asset over `netstandard2.0`, so .NET Framework consumers on **4.7.2 or later** resolve the `net472` asset and have those APIs; 4.6.2 – 4.7.1 consumers resolve `net462` and do not.

```bash
dotnet add package Iag.Unity.DataAccess.dll
```

> **Note** — The package id carries a `.dll` suffix (`Iag.Unity.DataAccess.dll`) — that is the id on NuGet, not a filename. The assembly and namespace are plain `Iag.Unity.DataAccess`.

---

## Getting started

Initialize the library once at startup with a connection string. Every command created afterward uses it automatically.

```csharp
using Iag.Unity.DataAccess;

DataLibrary.Initialize("Server=.;Database=MyDb;Integrated Security=true;");
```

`Initialize` also tests the connection and sets `IsInitialized`. Overloads build the connection string for you from parts — these now use `SqlConnectionStringBuilder` internally, so values containing `;` or `"` are escaped safely rather than injected.

```csharp
// Integrated security
DataLibrary.Initialize("sql01", "MyDb");

// SQL login
DataLibrary.Initialize("sql01", "MyDb", "app_user", "s3cr3t;value");

// With a logging callback for internal diagnostics
DataLibrary.Initialize(connectionString, ex => _logger.LogError(ex, "DataAccess"));
```

### Managing connections yourself

If you manage connections directly — multiple databases, an ambient connection — pass a `SqlConnection` to the constructor. The library opens it on first use if it is not already open, and (importantly) will **not** dispose a connection it did not create.

```csharp
using (var conn = new SqlConnection(connectionString))
using (var sp = new StoredProcedure(conn, "dbo.GetCustomer"))
{
    sp.Parameters["@Id"] = 123;
    DataTable tbl = sp.OpenTable();
} // conn is disposed by its own using, not by sp
```

> **Since 2.2** — An internally-created connection is no longer opened in the constructor; it opens lazily on the first execute/prepare call. Constructing a command no longer reserves a pooled connection you might never use.

---

## Microsoft Entra ID (Azure) authentication

*New in 2.3. Available on net472, netstandard2.0 and net8.0 — see [Installation](#installation).*

For a database in Azure, initialize with an `Azure.Core.TokenCredential` rather than a connection string that carries credentials. Nothing else about using the library changes: `StoredProcedure`, `UnitySqlCommand`, transactions and every async method work exactly as they do with a plain connection string.

```csharp
using Azure.Identity;
using Iag.Unity.DataAccess;

await DataLibrary.InitializeWithAzureCredentialsAsync(
    new DefaultAzureCredential(),
    "Server=tcp:my-server.database.windows.net,1433;Database=MyDb;");
```

### How the token is managed

The library stores the **credential**, not a token. A token grabbed once at startup would expire in about an hour and leave every later connection failing, so instead each connection created by `DataLibrary.GetConnection` — including the ones commands create for themselves — gets SqlClient's `AccessTokenCallback` attached, and SqlClient invokes it as tokens expire. `Azure.Identity` credentials cache tokens in memory and refresh near expiry, so the per-connection cost after the first acquisition is negligible.

A useful consequence: because commands create their connection unopened and open it on first use, the async execute methods acquire the token asynchronously as well, with no extra work on your part.

A connection you construct yourself and pass to a command ([Managing connections yourself](#managing-connections-yourself)) is never modified by the library, so it carries no token callback — set `AccessTokenCallback` (or `AccessToken`) on it before use if it needs Entra authentication.

### Choosing a credential

```csharp
// Explicit is better in production: no probing, no ambiguity about which identity is used
await DataLibrary.InitializeWithAzureCredentialsAsync(
    new ManagedIdentityCredential(clientId: "00000000-0000-0000-0000-000000000000"),
    "my-server.database.windows.net", "MyDb");

// Or trim DefaultAzureCredential's chain
var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
{
    ExcludeInteractiveBrowserCredential = true,
    ExcludeAzurePowerShellCredential = true,
});
```

`DefaultAzureCredential` walks a chain — environment variables, workload identity, managed identity, Visual Studio, Azure CLI, Azure PowerShell — which makes it excellent for local development and slow, occasionally surprising, in production.

> **Prefer the `…Async` overloads.** A synchronous `Open()` has to block on token acquisition, and the first `DefaultAzureCredential` acquisition can take seconds while it probes that chain.

### Connection-string rules

The connection string must not carry credentials of its own, because SqlClient refuses an access token alongside them. Rather than letting that surface as an opaque driver error, initialization rejects it up front with a message naming what to remove:

| Keyword | Result |
| --- | --- |
| `Integrated Security` / `Trusted_Connection` | `ArgumentException` |
| `User ID` / `Password` | `ArgumentException` |
| `Authentication` | `ArgumentException` |
| missing `Server` / `Data Source` | `ArgumentException` |
| `Encrypt=false` | upgraded to `Mandatory` — a bearer token is never sent unencrypted |
| `Encrypt=Strict` | left as-is |

Calling a plain `Initialize(...)` afterwards disarms Entra authentication and clears `AzureCredential`, so the new connection string's own credentials take effect.

### Sovereign clouds

`AzureSqlScope` defaults to `DefaultAzureSqlScope` (`https://database.windows.net/.default`). Set it before initializing for other clouds:

```csharp
DataLibrary.AzureSqlScope = "https://database.usgovcloudapi.net/.default";
```

### Failures

A failed token acquisition is wrapped in an `InvalidOperationException` naming the credential type and the scope, with the original `Azure.Identity` exception (typically `AuthenticationFailedException`) as its inner exception, and is also handed to `LoggingCallback`. The token itself never appears in a message or a log.

---

## Command types

Both command types derive from `BaseCommand` and share the entire execution and mapping surface. The only difference is how the command text is interpreted.

| Type | CommandType | Constructor argument |
| --- | --- | --- |
| `StoredProcedure` | `StoredProcedure` | A stored procedure name, e.g. `"dbo.GetCustomer"`. |
| `UnitySqlCommand` | `Text` | A raw SQL statement with `@named` parameters. |

Each has three constructors: parameterless (uses the default connection), name/text only, and `(SqlConnection, name)`. `StoredProcedure` additionally offers a helper to fetch a procedure's source text:

```csharp
string body = StoredProcedure.GetProcedureText("dbo.GetCustomer");
```

---

## Parameters

The common path is the `Parameters` dictionary — case-insensitive, keyed by parameter name. Values are sent as-is; nulls become `DBNull`.

```csharp
using (var sp = new StoredProcedure("dbo.CreateCustomer"))
{
    sp.Parameters["@Name"] = "Ada";
    sp.Parameters["@Region"] = null;      // sent as DBNull
    sp.Execute();
}
```

### Prepare(): derive metadata from the server

`Prepare()` asks the server for the procedure's parameter metadata — exact types, sizes, and **OUTPUT/RETURN** directions. It is opt-in. Skip it for input-only procedures; call it when you need precise typing or want to read output/return values back.

```csharp
using (var sp = new StoredProcedure("dbo.CreateOrder"))
{
    sp.Prepare();                          // one metadata round-trip (cached)
    sp.Parameters["@CustomerId"] = 123;
    sp.Execute();
    int newId = (int)sp.Parameters["@NewOrderId"];   // output captured
}
```

> **Cache** — Derived metadata is cached per *data source + database + procedure*, so repeated `Prepare()` calls avoid further round-trips. Each command gets its own parameter clones — the cache is never mutated. A cache entry is evicted automatically when a command using it fails, so a changed procedure signature self-heals on the next call.

### Output & return values without Prepare()

To capture outputs on the fast path, declare them explicitly. Read outputs back from `Parameters[name]` and the procedure's return code from `ReturnValue`.

```csharp
using (var sp = new StoredProcedure("dbo.CreateOrder"))
{
    sp.Parameters["@CustomerId"] = 123;                   // input
    sp.AddOutputParameter("@NewOrderId", SqlDbType.Int);  // output
    sp.AddReturnParameter();                              // return value

    sp.Execute();

    int newId = (int)sp.Parameters["@NewOrderId"];        // output  -> Parameters
    int code  = sp.ReturnValue ?? 0;                      // return  -> ReturnValue
}
```

Seeding a value for a declared output parameter promotes it to `InputOutput` automatically. `DeclareParameter(name, type, direction, size)` is the general form; it returns the underlying `SqlParameter` so you can set precision, scale, and so on. `SetParameterType(name, SqlDbType)` overrides the inferred type of an existing parameter.

---

## Executing commands

Pick the execution method by the shape of the result you want. All of them open the connection if needed, apply parameters, and read output/return values back afterward.

| Method | Returns | Use for |
| --- | --- | --- |
| `Execute()` | `void` | Commands with no result set (INSERT/UPDATE/DELETE, or output-only procs). |
| `OpenTable(name?)` | `DataTable` | A single result set. |
| `OpenDataSet(name?)` | `DataSet` | Multiple result sets. |
| `GetRows()` | `IEnumerable<DataRow>` | Rows of the first result set. |
| `GetRowSets()` | `IEnumerable<IEnumerable<DataRow>>` | Rows grouped by result set. |
| `ExecuteScalar()` | `object` | A single value. |
| `ExecuteScalar<T>(def)` | `T` | A single value converted to `T`, with a default for null/DBNull. |
| `ExecuteReader(behavior?)` | `SqlDataReader` | Streaming access; you control the reader lifetime. |
| `GetDataReader()` | `SqlDataReader` | Reader with `CommandBehavior.CloseConnection`. |

```csharp
using (var cmd = new UnitySqlCommand("SELECT COUNT(*) FROM Customer WHERE Region = @r"))
{
    cmd.Parameters["@r"] = "West";
    int count = cmd.ExecuteScalar<int>(0);
}
```

> **Timing** — After any execute call, `ExecuteTime` (and `PrepareTime` after `Prepare()`) hold the elapsed `TimeSpan` for that round-trip — handy for lightweight profiling.

---

## Async

Every command-executing method has an `...Async` counterpart that accepts an optional `CancellationToken`. These do genuine asynchronous I/O: the connection is opened with `OpenAsync`, and result sets are streamed with `ExecuteReaderAsync`/`ReadAsync` rather than a blocking `SqlDataAdapter.Fill`.

```csharp
using (var sp = new StoredProcedure("dbo.GetCustomers"))
{
    sp.Parameters["@Region"] = "West";
    List<Customer> customers = (await sp.GetObjectsAsync<Customer>(cancellationToken: ct)).ToList();
}
```

| Synchronous | Asynchronous |
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

> **net8.0** — On .NET 8.0, `BaseCommand` implements `IAsyncDisposable`, so you can write `await using var sp = new StoredProcedure(...)` and the connection (if owned) is disposed asynchronously.

> **Note** — `Prepare()` and the transaction methods stay synchronous — `SqlCommandBuilder.DeriveParameters` has no async API. Async execute methods run correctly inside a transaction that was begun synchronously.

---

## Object mapping

`GetObjects<T>()` materializes a result set into a list of `T`; `GetObject<T>()` returns the first row (or null). The column→property plan is built once per call, not per row. Three mapping strategies are available.

### 1 · By name

Columns match properties by name. Matching is case-insensitive by default; pass `strict: true` for case-sensitive matching.

```csharp
var customers = sp.GetObjects<Customer>().ToList();
var exact     = sp.GetObjects<Customer>(strict: true).ToList();
```

### 2 · By [FieldToProperty] attribute

Decorate properties when column names differ from property names.

```csharp
public class Customer
{
    [FieldToProperty("cust_id")]   public int    Id   { get; set; }
    [FieldToProperty("cust_name")] public string Name { get; set; }
}

var customers = sp.GetObjects<Customer>().ToList();
```

### 3 · By custom map function

Provide a function that maps each column name to a property name (return null to skip a column).

```csharp
Func<string, string> map = col => col switch
{
    "STR" => "StringValue",
    "INT" => "IntValue",
    _     => null            // ignore everything else
};

var rows = sp.GetObjects<Dto>(map).ToList();
```

### Per-value translation

Pass a `translationAction` to intercept individual values — decrypt a column, coerce a legacy sentinel, map codes to enums. Set `Handled = true` to take over assignment for that value; otherwise the default conversion runs.

```csharp
var rows = sp.GetObjects<Dto>(translationAction: th =>
{
    if (th.PropertyInfo.Name == "Status")
    {
        th.TranslatedValue = MapStatus(th.DatabaseValue);
        th.Handled = true;
    }
}).ToList();
```

> **Types** — Default conversion honors nullable types: a `DBNull` maps to `null` for reference and `Nullable<T>` properties, and non-nullable value-type properties are left at their default rather than throwing. Otherwise the value is converted to the target type with `Convert.ChangeType`.

---

## Transactions

Begin a transaction on one command and enlist the others by preparing them with the same transaction. Commit or roll back through any enlisted command.

```csharp
using (var cmd1 = new UnitySqlCommand("..."))
using (var cmd2 = new UnitySqlCommand("..."))
{
    SqlTransaction trans = null;
    try
    {
        cmd1.Prepare();
        trans = cmd1.BeginTransaction();   // ReadCommitted by default
        cmd2.Connection = cmd1.Connection;
        cmd2.Prepare(trans);

        cmd1.Execute();
        cmd2.Execute();
        trans.Commit();
    }
    catch
    {
        trans?.Rollback();
        throw;
    }
}
```

> **⚠️ Changed in 2.2** — `BeginTransaction()` now defaults to `IsolationLevel.ReadCommitted` instead of `Serializable`. If a workflow relied on serializable semantics, request it explicitly with the new overload: `cmd.BeginTransaction(IsolationLevel.Serializable);`

### CreateTransaction()

The static helper wires a set of `StoredProcedure` objects onto one connection and one transaction, preparing each for you. Do not call `Prepare()` afterward — it is already done. Set parameters after the call.

```csharp
using (var sp1 = new StoredProcedure("dbo.Debit"))
using (var sp2 = new StoredProcedure("dbo.Credit"))
{
    var trans = BaseCommand.CreateTransaction(sp1, sp2);
    try
    {
        sp1.Parameters["@Amount"] = 100m;  sp1.Execute();
        sp2.Parameters["@Amount"] = 100m;  sp2.Execute();
        trans.Commit();
    }
    catch { trans?.Rollback(); throw; }
}
```

> **Note** — `CreateTransaction` takes `params StoredProcedure[]`. To coordinate `UnitySqlCommand` objects (or a mix), use the manual `BeginTransaction`/`Prepare(trans)` pattern shown above.

---

## Error handling

When a command fails, the raised `SqlException` is wrapped in a `ContextualSqlException` whose `Context` property holds the command text and the parameters involved — enough to reproduce the failure from a log entry. The original exception is preserved as the inner exception.

```csharp
try
{
    sp.Execute();
}
catch (ContextualSqlException ex)
{
    _logger.LogError(ex, "Query failed:\n{Context}", ex.Context);
}
```

> **⚠️ Privacy** — By default, `Context` lists parameter **names and types** only. Parameter *values* are omitted because they routinely carry PII, tokens, or credentials. Opt in deliberately — ideally only in non-production diagnostics: `DataLibrary.IncludeParameterValuesInErrors = true;`

A failed command also invalidates its cached parameter metadata, so the next call re-derives it. Set `DataLibrary.LoggingCallback` to observe internal diagnostic exceptions (for example, a failure while building the error context itself).

---

## Performance

- **Parameter-metadata cache.** `Prepare()` derives metadata once per data source + database + procedure and reuses pristine clones thereafter — no repeated `DeriveParameters` round-trips across command instances.
- **Lazy connections (2.2).** An internally-created connection opens on first use, not at construction, so short-lived or never-executed commands don't hold a pooled connection.
- **Skip Prepare on the hot path.** For input-only procedures, don't call `Prepare()`; declare any outputs explicitly instead. That removes the metadata round-trip entirely.
- **Async reader path.** Async table/object methods stream rows via `ReadAsync` rather than blocking a thread on `SqlDataAdapter.Fill`.
- **One mapping plan per call.** `GetObjects<T>()` resolves columns to properties once, then reuses the plan for every row.

---

## Security

- **Always parameterize.** Use `Parameters["@x"]` and never concatenate user input into a `UnitySqlCommand` statement. Parameters are sent as real `SqlParameter`s.
- **Connection-string safety (2.2).** The part-based `Initialize` overloads build the string with `SqlConnectionStringBuilder`, so special characters in a password or server name can't inject additional keywords.
- **Entra ID over stored credentials (2.3).** `InitializeWithAzureCredentials` removes passwords from configuration entirely. It rejects a connection string that carries its own credentials, upgrades `Encrypt=false` to `Mandatory` so a bearer token is never sent unencrypted, and never writes an access token to a log or exception message.
- **Sensitive data in errors.** Parameter values stay out of exception context unless you explicitly enable `IncludeParameterValuesInErrors`. Keep it off in production.

---

## API reference

### DataLibrary (static)

| Member | Description |
| --- | --- |
| `Initialize(connectionString, loggingCallback?)` | Set the default connection string and test the connection. |
| `Initialize(server, db)` | Integrated-security connection, built via `SqlConnectionStringBuilder`. |
| `Initialize(server, db, user, password)` | SQL-login connection, built safely from parts. |
| `InitializeWithAzureCredentials(credential, connectionString, loggingCallback?)` | Entra ID connection using a `TokenCredential`. Not on net462. |
| `InitializeWithAzureCredentials(credential, server, db, loggingCallback?)` | Same, built from parts. Not on net462. |
| `InitializeWithAzureCredentials(connectionString, loggingCallback?)` | Same, using `new DefaultAzureCredential()`. Not on net462. |
| `InitializeWithAzureCredentialsAsync(…, cancellationToken?)` | Async counterparts of the three above; preferred. Not on net462. |
| `AzureCredential` | The `TokenCredential` in use, or null when initialized from a plain connection string. |
| `AzureSqlScope` | Token scope; defaults to `DefaultAzureSqlScope`. Set for sovereign clouds. |
| `DefaultAzureSqlScope` | `https://database.windows.net/.default` (public cloud). |
| `ConnectionString` | The active default connection string. |
| `IsInitialized` | True once a connection has been established successfully. |
| `LoggingCallback` | `Action<Exception>` invoked for internal diagnostics. |
| `IncludeParameterValuesInErrors` | Opt-in switch to include parameter values in error context. Default `false`. |
| `GetConnection(doNotOpen?)` | Create a `SqlConnection` from the default string. |

### BaseCommand — properties

| Property | Type | Description |
| --- | --- | --- |
| `Parameters` | `Dictionary<string,object>` | Input values and, after execution, output values (case-insensitive). |
| `ReturnValue` | `int?` | The procedure's RETURN code, when a return parameter is present. |
| `Connection` | `SqlConnection` | The underlying connection; setting it resets prepared state. |
| `Command` | `SqlCommand` | The underlying command object. |
| `CommandText` | `string` | The command's text (prepares on set if needed). |
| `Timeout` | `int` | Command timeout in seconds (default 300). |
| `IsPrepared` | `bool` | Whether metadata has been derived. |
| `ExecuteTime` / `PrepareTime` | `TimeSpan` | Elapsed time of the last execute / prepare. |
| `DerivedParameters` | `List<SqlParameter>` | Server-derived parameter templates after `Prepare()`. |

### BaseCommand — parameter & transaction methods

| Method | Description |
| --- | --- |
| `Prepare()` / `Prepare(trans)` | Derive parameter metadata; optionally enlist in a transaction. |
| `DeclareParameter(name, type, direction, size?)` | Declare a typed parameter; returns the `SqlParameter`. |
| `AddOutputParameter(name, type, size?)` | Declare an OUTPUT parameter, read back from `Parameters`. |
| `AddReturnParameter(name?)` | Declare the RETURN value parameter, read back from `ReturnValue`. |
| `SetParameterType(name, SqlDbType)` | Override the inferred type of a parameter. |
| `BeginTransaction()` / `BeginTransaction(level)` | Begin a transaction (default `ReadCommitted`). |
| `Commit()` / `Rollback()` | Complete or abort the current transaction. |
| `static CreateTransaction(params StoredProcedure[])` | Enlist procedures on one connection + transaction and prepare them. |
| `Dispose()` / `DisposeAsync()` | Dispose the command and any owned connection. `DisposeAsync` is net8.0-only. |

Execution methods and their async twins are listed under [Executing commands](#executing-commands) and [Async](#async).

---

## What's new in 2.3.0

### Added

- **Microsoft Entra ID (Azure AD) authentication.** `InitializeWithAzureCredentials` / `InitializeWithAzureCredentialsAsync` accept an `Azure.Core.TokenCredential` such as `DefaultAzureCredential`. Tokens are acquired per connection through SqlClient's `AccessTokenCallback` and refreshed automatically. See [Microsoft Entra ID (Azure) authentication](#microsoft-entra-id-azure-authentication).
- `DataLibrary.AzureCredential`, `DataLibrary.AzureSqlScope` and `DataLibrary.DefaultAzureSqlScope`.
- **A `net472` target.** NuGet prefers a same-family asset over `netstandard2.0`, so without it every .NET Framework consumer would resolve `net462`; `net472` is what gives modern .NET Framework consumers the Entra APIs.

### Changed / fixed

- `Initialize(...)` now clears any Entra credential from a previous initialization, so a connection string carrying its own credentials is not paired with a stale access-token callback.

> **⚠️ Note for .NET Framework 4.6.2 – 4.7.1 consumers**
> The Entra ID APIs are absent from the `net462` build — `Azure.Identity` ships no .NET Framework asset, and its `netstandard2.0` build needs extensive binding redirects on 4.6.2. Retarget to 4.7.2 or later to use them. Everything else in the library is unchanged on `net462`.

---

## What's new in 2.2.0

### Added

- Async counterparts for all execution methods, each with `CancellationToken` support and genuine async I/O.
- `IAsyncDisposable` on `BaseCommand` (net8.0).
- `BeginTransaction(IsolationLevel)` overload.
- `DataLibrary.IncludeParameterValuesInErrors` switch.

### Changed / fixed

- Part-based `Initialize` overloads now build connection strings with `SqlConnectionStringBuilder` (connection-string-injection fix).
- Internally-created connections open lazily instead of in the constructor.
- `SqlDataAdapter` in `OpenDataSet` is now disposed; `ExecuteScalar<T>` no longer double-measures `ExecuteTime`.

> **⚠️ Review before upgrading**
> - **Transactions default to `ReadCommitted`** (was `Serializable`) — request the stricter level explicitly if you depended on it.
> - **Error context omits parameter values** by default — set `IncludeParameterValuesInErrors = true` if your diagnostics relied on seeing them.

---

*Iag.Unity.DataAccess v2.3.0 · MIT License · Built on Microsoft.Data.SqlClient 7.0.2 · Targets net462, net472, netstandard2.0, net8.0.*
