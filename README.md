# IAG-Unity-DataAccess
## First things first.  Initialize your data library:
```c#
DataLibrary.Initialize("<Your connection string here>");
```
Initializing the DataLibrary will let you avoid having to specify a connection string or SqlConnection on each StoredProcedure or UnitySqlCommand object. 

## Executing a stored procedure without parameters (returning a single table):
```c#
using (StoredProcedure sp = new StoredProcedure("<Your procedure name>"))
{
     DataTable tbl = sp.OpenTable();
}
```

## Executing a stored procedure with parameters (returning a single table):
```c#
using (StoredProcedure sp = new StoredProcedure("<Your procedure name>"))
{
     sp.Prepare(); // Initialize the parameter dictionaries, etc.
     sp.Parameters["@Id"] = 123;
     DataTable tbl = sp.OpenTable();
}
```


## Executing a sql command without parameters (returning a single table):
```c#
using (UnitySqlCommand cmd = new UnitySqlCommand("<Your sql statement>"))
{
     DataTable tbl = cmd.OpenTable();
}
```

## Executing a stored procedure with parameters (returning a single table):
```c#
using (UnitySqlCommand cmd = new UnitySqlCommand("<Your sql statement WHERE Id = @Id>"))
{
     cmd.Prepare();
     cmd.Parameters["@Id"] = 123;
     DataTable tbl = cmd.OpenTable();
}
```

## Other return types for StoredProcedure and UnitySqlCommand:
```c#
void Execute();
DataSet OpenDataSet([name = null]);
IEnumerable<DataRow> GetRows();
IEnumerable<IEnumerable<DataRow>> GetRowSets();
T ExecuteScalar([defaultValue]);
```

## Executing within a transaction
```c#
using (UnitySqlCommand cmd1 = new UnitySqlCommand("<Your sql statement>"))
using (UnitySqlCommand cmd2 = new UnitySqlCommand("<Your sql statement>"))
using (UnitySqlCommand cmd3 = new UnitySqlCommand("<Your sql statement>"))
{
    try
    {
         cmd1.Prepare();
         SqlTransaction trans = cmd1.BeginTransaction();
         cmd2.Prepare(trans); // Prepare with the transaction
         cmd3.Prepare(trans); // Prepare with the transaction
         
         cmd1.Execute();
         cmd2.Execute();
         cmd3.Execute();
         trans.Commit();
    }
    catch
    {
       trans.Rollback();
    }
}
```
