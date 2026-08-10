# Iag.Unity.DataAccess

A lightweight data-access library that simplifies working with Microsoft SQL Server. It wraps `SqlConnection` and `SqlCommand` in a small set of objects and methods that make stored procedures, ad-hoc SQL, parameter handling, and object mapping straightforward.

📖 **[Full documentation guide](docs/index.html)** — a browsable reference covering every feature (open locally, or serve the `docs/` folder via GitHub Pages).

## Target frameworks

A single package multi-targets:

- **.NET Framework 4.6.2**
- **.NET Standard 2.0**
- **.NET 8.0**

Built on [`Microsoft.Data.SqlClient`](https://www.nuget.org/packages/Microsoft.Data.SqlClient) `7.0.2`.

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

## Full API documentation

See the [package README](Iag.Unity.DataAccess/README.md) for the complete API — all execution methods (`OpenDataSet`, `GetRows`, `ExecuteScalar<T>`, `ExecuteReader`, …), object-mapping options (`[FieldToProperty]`, custom map functions, translation handlers), and additional transaction patterns.

## License

[MIT](https://opensource.org/licenses/MIT)
