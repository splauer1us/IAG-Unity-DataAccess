# IAG-Unity-DataAccess
## NuGet Package Page
https://www.nuget.org/packages/Iag.Unity.DataAccess.Standard.dll

## First things first.  Initialize your data library:
```c#
DataLibrary.Initialize("<Your connection string here>");
```
Initializing the DataLibrary will let you avoid having to specify a connection string or SqlConnection on each StoredProcedure or UnitySqlCommand object. If you elect to not generally initialize the connection (possibly because you are managing multiple connections) the constructors for StoredProcedure and UnitySqlCommand take a SqlConnection as the first parameter:
```c#
using (SqlConnection conn1 = new SqlConnection("<Your connection string here>"))
using (SqlConnection conn2 = new SqlConnection("<Your connection string here>"))
using (StoredProcedure sp = new StoredProcedure(conn1, "<Your procedure name>")) 
using (UnitySqlCommand cmd = new UnitySqlCommand(conn2 "<Your procedure name>"))
{
     // The data access library takes care of opening the SqlConnection object if it isn't already open.
     ...
}
```

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

## Utilizing GetObject<T>()
The GetObject<T>() function allows you to take a single table and convert them into a list of objects.  This is a simple implementation that currently does not support collection properties automatically filled with a child data.  There are ways the GetObject<T>() method can be used:
### Simply mapping field name in the data table to property name
This example assumes that the object to be loaded with data has exact matching of the field name to the property.  The method can be invoked using an optional parameter indicating whether to evaluate property and field names in a case-insensitive or case-sensitive manner.
```c#
using (UnitySqlCommand sp = new UnitySqlCommand("SELECT CAST('2015-12-12 12:12:12 -08:00' as datetimeoffset) as 'DateTimeOffsetValue', 'string' as StringValue, 123 as IntValue, 12.34 as DoubleValue, '2015-10-12 12:12:44' as DateTimeValue, 1 as BoolValue"))
{
	/// This implementation will evaluate field->property mapping in a case-insensitive manner.
	var tlc1 = sp.GetObject<TestLoadClass>();
	
	/// This implementation will evaluate field->property mapping in a case-sensitive manner.
	var tlc2 = sp.GetObject<TestLoadClass>(true);
}
```

## Utilizing GetObjects<T>()
The GetObjects<T>() function allows you to take a single table and convert them into a list of objects.  This is a simple implementation that currently does not support collection properties automatically filled with a child data.  There are ways the GetObjects<T>() method can be used:
### Simply mapping field name in the data table to property name
This example assumes that the object to be loaded with data has exact matching of the field name to the property.  The method can be invoked using an optional parameter indicating whether to evaluate property and field names in a case-insensitive or case-sensitive manner.
```c#
using (UnitySqlCommand sp = new UnitySqlCommand("SELECT CAST('2015-12-12 12:12:12 -08:00' as datetimeoffset) as 'DateTimeOffsetValue', 'string' as StringValue, 123 as IntValue, 12.34 as DoubleValue, '2015-10-12 12:12:44' as DateTimeValue, 1 as BoolValue"))
{
	/// This implementation will evaluate field->property mapping in a case-insensitive manner.
	List<TestLoadClass> tlc1 = sp.GetObjects<TestLoadClass>().ToList();
	
	/// This implementation will evaluate field->property mapping in a case-sensitive manner.
	List<TestLoadClass> tlc2 = sp.GetObjects<TestLoadClass>(true).ToList();
}
```

### Utilizing the ```c#FieldToProperty``` attribute
In this case, the target object class definition has properties decorated with ```c#[FieldToProperty('<fieldName>')```].
```c#
public class TestLoadClassMapped
{
	[FieldToProperty("STR")]
	public string StringValue { get; set; }
	[FieldToProperty("INT")]
	public int? IntValue { get; set; }
	[FieldToProperty("DBL")]
	public double? DoubleValue { get; set; }
	[FieldToProperty("DT")]
	public DateTime? DateTimeValue { get; set; }
	[FieldToProperty("DTO")]
	public DateTimeOffset? DateTimeOffsetValue { get; set; }
	[FieldToProperty("B")]
	public bool? BoolValue { get; set; }
}

using (UnitySqlCommand sp = new UnitySqlCommand("SELECT 'string' as STR, 123 as INT, 12.34 as DBL, CAST('2015-10-12 12:12:44' as DateTime) as DT, CAST('2015-12-12 12:12:12 -08:00' as DateTimeOffset) as DTO, 0 as B"))
{
	List<TestLoadClassMapped> tlc = sp.GetObjects<TestLoadClassMapped>().ToList();
}


```

### Utilizing a custom mapping function
Create a function that receives a string parameter that indicates the table's field name to be mapped.  The result of the function is to return the property name to be populated.
```c#            
using (UnitySqlCommand sp = new UnitySqlCommand("SELECT 'string' as STR, 123 as INT, 12.34 as DBL, CAST('2015-10-12 12:12:44' as DateTime) as DT, CAST('2015-12-12 12:12:12 -08:00' as DateTimeOffset) as DTO, 0 as B"))
{

	Func<string, string> func = (x) =>
	{
		if (x == "STR") return "StringValue";
		else if (x == "INT") return "IntValue";
		return null;
	};

	List<TestLoadClassNullable> tlc = sp.GetObjects<TestLoadClassNullable>(func).ToList();
}
```

## Other return types and execution methods for StoredProcedure and UnitySqlCommand:
```c#
void Execute();
DataSet OpenDataSet([name = null]);
IEnumerable<DataRow> GetRows();
IEnumerable<IEnumerable<DataRow>> GetRowSets();
object ExecuteScalar();
T ExecuteScalar<T>(T defaultValue);
SqlDataReader ExecuteReader(CommandBehavior commandBehavior = CommandBehavior.Default);
```

The following calls have been marked [Obsolete].  Use ```ExecuteScalar<T>()``` instead.
```c#
bool ExecuteScalar(bool defaultValue = false);
byte[] ExecuteScalar(byte[] defaultValue = null);
DateTime? ExecuteScalar(DateTime? defaultValue = null);
long ExecuteScalar(long defaultValue = null);
int ExecuteScalar(int defaultValue = null);
string ExecuteScalar(string defaultValue = null);

```

## Executing within a transaction
```c#
using (UnitySqlCommand cmd1 = new UnitySqlCommand("<Your sql statement>"))
using (UnitySqlCommand cmd2 = new UnitySqlCommand("<Your sql statement>"))
using (UnitySqlCommand cmd3 = new UnitySqlCommand("<Your sql statement>"))
{
    SqlTransaction trans = null;
    try
    {
         cmd1.Prepare();
         trans = cmd1.BeginTransaction();
         cmd2.Prepare(trans); // Prepare with the transaction
         cmd3.Prepare(trans); // Prepare with the transaction
         
         cmd1.Execute();
         cmd2.Execute();
         cmd3.Execute();
         trans.Commit();
    }
    catch
    {
       trans?.Rollback();
    }
}
```

Or utilize the CreateTransaction() method. This method will take all the commands/procedures passed to it and set them up with the same connection and into the same transaction.  DO NOT Prepare() after creating the transaction! The CreateTransaction() method will Prepare() for you. After the CreateTransaction(), you can set any parameters necessary.
```c#
using (UnitySqlCommand cmd1 = new UnitySqlCommand("<Your sql statement>"))
using (UnitySqlCommand cmd2 = new UnitySqlCommand("<Your sql statement>"))
using (UnitySqlCommand cmd3 = new UnitySqlCommand("<Your sql statement>"))
using (UnitySqlCommand cmd4 = new UnitySqlCommand("<Your sql statement>"))
{
     SqlTransaction trans = null;
     try
     {
	  trans = UnitySqlCommand.CreateTransaction(cmd1, cmd2, cmd3, cmd4);
          cmd1.Execute();
          cmd2.Execute();
          cmd3.Execute();
          cmd4.Execute();
	  trans.Commit();
     }
     catch
     {
          trans?.Rollback();
     }
}
```
### License
The MIT License (MIT)
Copyright (c) 2016 IAG Unity, LLC

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
