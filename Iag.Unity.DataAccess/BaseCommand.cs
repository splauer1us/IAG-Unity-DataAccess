using Iag.Unity.DataAccess.Exceptions;
using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace Iag.Unity.DataAccess
{
    public abstract class BaseCommand : IDisposable
    {
        private Dictionary<string, object> parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, SqlDbType> explicitParameterTypes = new Dictionary<string, SqlDbType>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, SqlParameter> declaredParameters = new Dictionary<string, SqlParameter>(StringComparer.OrdinalIgnoreCase);

        private int timeout = 300;
        private bool isPrepared;
        private bool isDisposed;
        private string procedureName;
        private int? returnValue;
        protected bool externalConnection { get; set; } = false;

        public TimeSpan ExecuteTime { get; set; }
        public TimeSpan PrepareTime { get; set; }

        protected SqlConnection sqlConnection { get; set; }
        private SqlCommand sqlCommand;

        private List<SqlParameter> derivedParameters = new List<SqlParameter>();
        private string parameterCacheKey;

        // Cache of derived stored-procedure parameter templates, keyed by data source + database + procedure.
        // The templates are never mutated or attached to a live command — each command gets its own clones —
        // and an entry is evicted whenever a command using it fails (InvalidateParameterCache).
        private static readonly ConcurrentDictionary<string, List<SqlParameter>> derivedParameterCache
            = new ConcurrentDictionary<string, List<SqlParameter>>(StringComparer.OrdinalIgnoreCase);

        public List<SqlParameter> DerivedParameters
        {
            get { return derivedParameters; }
        }

        public Dictionary<string, object> Parameters
        {
            get { return parameters; }
        }

        public int? ReturnValue
        {
            get { return returnValue; }
        }
        public SqlConnection Connection
        {
            get { return sqlConnection; }
            set
            {
                if (this.sqlConnection != null)
                    CloseConnection();
                isPrepared = false;
                this.sqlConnection = value;
            }
        }

        public string CommandText
        {
            get { return (sqlCommand == null ? String.Empty : sqlCommand.CommandText); }
            set
            {
                if (!IsPrepared)
                    Prepare();
                if (sqlCommand == null)
                    throw new InvalidOperationException("The sql command has not been initialized.  Prepare() before setting the command text.");
                sqlCommand.CommandText = value;
            }
        }

        public SqlCommand Command
        {
            get { return sqlCommand; }
        }

        public bool IsPrepared
        {
            get { return isPrepared; }
            set { isPrepared = value; }
        }

        public int Timeout
        {
            get { return timeout; }
            set
            {
                if (timeout != value)
                    isPrepared = false;
                timeout = value;
            }
        }

        public BaseCommand() : this(null, String.Empty) { }

        public BaseCommand(string procedureName)
            : this(null, procedureName) { }

        public BaseCommand(SqlConnection connection, string procedureName)
        {
            this.procedureName = procedureName;
            if (connection == null)
            {

                connection = DataLibrary.GetConnection();

            }
            else
                externalConnection = true;
            this.sqlConnection = connection;
        }

        public void Prepare()
        {
            Prepare(null);
        }

        public void Prepare(SqlTransaction trans)
        {
            var start = Stopwatch.StartNew();
            if (sqlConnection == null)
                throw new InvalidOperationException("The sql connection is null.");
            if (this.sqlCommand == null || (this.sqlCommand.Transaction == null && trans != null) || (this.sqlCommand.Transaction != null && trans == null))
                this.sqlCommand = GetCommand(procedureName, this.sqlConnection, trans);

            isPrepared = true;
            PrepareTime = start.Elapsed;
        }

        // Ensures a SqlCommand exists WITHOUT deriving parameters from the server. This lets
        // callers execute a stored procedure with manually-supplied parameters (or none) without
        // paying for a Prepare()/DeriveParameters round-trip. Call Prepare() explicitly when you
        // want derived parameter metadata (exact types/sizes and output/return values).
        private void EnsureCommand()
        {
            if (sqlConnection == null)
                throw new InvalidOperationException("The sql connection is null.");
            if (this.sqlCommand == null)
                this.sqlCommand = BuildCommand(procedureName, this.sqlConnection, null);
        }

        private SqlCommand BuildCommand(string procedureName, SqlConnection sqlConnection, SqlTransaction transaction)
        {
            SqlCommand cmd = new SqlCommand(procedureName, sqlConnection, transaction);
            cmd.CommandType = (this is UnitySqlCommand ? CommandType.Text : CommandType.StoredProcedure);
            cmd.CommandTimeout = timeout;
            cmd.Parameters.Clear();
            return cmd;
        }

        private SqlCommand GetCommand(string procedureName, SqlConnection sqlConnection, SqlTransaction transaction)
        {
            SqlCommand cmd = BuildCommand(procedureName, sqlConnection, transaction);

            if (!(this is UnitySqlCommand))
                FillParameters(cmd);

            return cmd;
        }

        private void FillParameters(SqlCommand command)
        {
            OpenConnection();

            this.parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            this.parameterCacheKey = BuildParameterCacheKey(command);

            if (derivedParameterCache.TryGetValue(this.parameterCacheKey, out List<SqlParameter> cachedTemplates))
            {
                // Cache hit: no server round-trip. Give this instance its own clones so the
                // cached templates are never mutated or attached to a live command.
                this.derivedParameters = CloneParameters(cachedTemplates);
            }
            else
            {
                // Cache miss: derive from the server (one round-trip), then cache pristine clones.
                try
                {
                    SqlCommandBuilder.DeriveParameters(command);
                }
                catch
                {
                    InvalidateParameterCache();
                    throw;
                }

                this.derivedParameters = new List<SqlParameter>(command.Parameters.Count);
                foreach (SqlParameter parm in command.Parameters)
                    this.derivedParameters.Add(parm);

                derivedParameterCache[this.parameterCacheKey] = CloneParameters(this.derivedParameters);
            }

            // Reflect the derived parameters on the command regardless of cache hit/miss.
            command.Parameters.Clear();
            foreach (SqlParameter parm in this.derivedParameters)
                command.Parameters.Add(parm);
        }

        private string BuildParameterCacheKey(SqlCommand command)
        {
            SqlConnection conn = command.Connection;
            string dataSource = conn != null ? conn.DataSource : String.Empty;
            string database = conn != null ? conn.Database : String.Empty;
            return String.Concat(dataSource, "|", database, "|", this.procedureName);
        }

        private static List<SqlParameter> CloneParameters(IEnumerable<SqlParameter> source)
        {
            var clones = new List<SqlParameter>();
            foreach (SqlParameter parm in source)
                clones.Add((SqlParameter)((ICloneable)parm).Clone());
            return clones;
        }

        private void InvalidateParameterCache()
        {
            string key = this.parameterCacheKey;
            if (!String.IsNullOrEmpty(key))
                derivedParameterCache.TryRemove(key, out _);
        }

        public void Execute()
        {
            try
            {
                var start = Stopwatch.StartNew();
                EnsureCommand();

                OpenConnection();
                TransferParameters();

                this.sqlCommand.ExecuteNonQuery();
                TransferParametersPost();
                ExecuteTime = start.Elapsed;
            }
            catch (SqlException ex)
            {
                InvalidateParameterCache();
                throw BuildContextException(ex);
            }
            catch
            {
                InvalidateParameterCache();
                throw;
            }
        }

        private ContextualSqlException BuildContextException(SqlException ex)
        {
            string context;

            try
            {
                StringBuilder builder = new StringBuilder();

                builder.AppendLine("Query:");
                builder.AppendLine(this.sqlCommand.CommandText);
                builder.AppendLine();

                builder.AppendLine("Parameters:");

                Func<object, string> isNull = (value) => (value == null || value == DBNull.Value ? "(null)" : Convert.ToString(value));

                this.sqlCommand.Parameters.Cast<SqlParameter>().ToList().ForEach(parameter =>
                {
                    builder.Append(parameter.ParameterName).Append(" = ").Append(isNull(parameter.Value)).AppendLine();
                });

                context = builder.ToString();
            }
            catch (Exception ex2)
            {
                DataLibrary.LoggingCallback(ex2);
                context = ("Exception building context:" + Environment.NewLine + DataLibrary.BuildExceptionMessage(ex2));
            }

            return new ContextualSqlException(ex.Message, ex, context);
        }

        private void TransferParameters()
        {
            this.sqlCommand.Parameters.Clear();
            if (!(this is UnitySqlCommand) && derivedParameters != null && derivedParameters.Any())
            {
                foreach (SqlParameter dparm in derivedParameters)
                {
                    this.sqlCommand.Parameters.Add(dparm);
                    if (parameters.ContainsKey(dparm.ParameterName))
                        dparm.Value = parameters[dparm.ParameterName];

                }
            }
            else
            {
                // No derived metadata: send explicitly-declared parameters (output/return/typed)
                // first, then any remaining plain input values from the Parameters dictionary.
                foreach (KeyValuePair<string, SqlParameter> kvp in this.declaredParameters)
                {
                    SqlParameter parm = (SqlParameter)((ICloneable)kvp.Value).Clone();
                    if (parameters.ContainsKey(kvp.Key))
                    {
                        // A value was supplied for a declared output => make it input/output.
                        parm.Value = parameters[kvp.Key] ?? (object)DBNull.Value;
                        if (parm.Direction == ParameterDirection.Output)
                            parm.Direction = ParameterDirection.InputOutput;
                    }
                    this.sqlCommand.Parameters.Add(parm);
                }

                foreach (KeyValuePair<string, object> kvp in this.Parameters)
                {
                    if (this.declaredParameters.ContainsKey(kvp.Key))
                        continue;
                    this.sqlCommand.Parameters.AddWithValue(kvp.Key, kvp.Value ?? (object)DBNull.Value);
                }
            }

            foreach (var kvp in this.explicitParameterTypes)
            {
                if (this.sqlCommand.Parameters.Contains(kvp.Key))
                    this.sqlCommand.Parameters[kvp.Key].SqlDbType = kvp.Value;
            }
        }

        public SqlTransaction BeginTransaction()
        {
            OpenConnection();
            Prepare(sqlConnection.BeginTransaction(IsolationLevel.Serializable));
            return sqlCommand.Transaction;
        }

        public void Commit()
        {
            if (sqlCommand.Transaction != null)
                sqlCommand.Transaction.Commit();
        }

        public void Rollback()
        {
            if (sqlCommand.Transaction != null)
                sqlCommand.Transaction.Rollback();
        }

        private void CloseConnection()
        {
            if (sqlConnection == null)
                throw new InvalidOperationException("The sql connection is null.");
            if (externalConnection || sqlCommand.Transaction != null) return;
            if (sqlConnection.State != System.Data.ConnectionState.Closed)
                sqlConnection.Close();
        }

        private void OpenConnection()
        {
            if (sqlConnection == null)
                throw new InvalidOperationException("The sql connection is null.");

            if (sqlConnection.State != System.Data.ConnectionState.Open)
                sqlConnection.Open();
        }

        public DataSet OpenDataSet(string name = null)
        {
            try
            {
                EnsureCommand();

                var start = Stopwatch.StartNew();
                TransferParameters();

                OpenConnection();
                SqlDataAdapter da = new SqlDataAdapter(this.sqlCommand);
                DataSet ds = String.IsNullOrWhiteSpace(name) ? new DataSet() : new DataSet(name);
                da.Fill(ds);
                TransferParametersPost();
                ExecuteTime = start.Elapsed;

                return ds;
            }
            catch (SqlException ex)
            {
                InvalidateParameterCache();
                throw BuildContextException(ex);
            }
            catch
            {
                InvalidateParameterCache();
                throw;
            }
        }

        private void TransferParametersPost()
        {
            foreach (SqlParameter param in this.sqlCommand.Parameters)
            {
                if (param.Direction == ParameterDirection.Input) continue;
                if (param.Direction == ParameterDirection.ReturnValue)
                {
                    if (param.Value is Int32)
                        this.returnValue = (int)param.Value;
                    else
                        this.returnValue = null;
                    continue;
                }
                if (this.parameters.ContainsKey(param.ParameterName))
                    this.parameters[param.ParameterName] = param.Value;
                else
                    this.parameters.Add(param.ParameterName, param.Value);
            }
        }

        public IEnumerable<DataRow> GetRows()
        {
            return OpenTable().Rows.Cast<DataRow>();
        }

        public IEnumerable<IEnumerable<DataRow>> GetRowSets()
        {
            return OpenDataSet().Tables.Cast<DataTable>().Select(table => table.Rows.Cast<DataRow>());
        }

        public DataTable OpenTable(string name = null)
        {
            DataSet ds = OpenDataSet();
            if (ds.Tables.Count > 0)
            {
                if (!String.IsNullOrWhiteSpace(name))
                    ds.Tables[0].TableName = name;
                DataTable tble = ds.Tables[0];
                ds.Tables.Remove(tble);
                return tble;
            }
            else
                return new DataTable();
        }

        public SqlDataReader GetDataReader()
        {
            try
            {
                EnsureCommand();
                TransferParameters();

                OpenConnection();
                return this.sqlCommand.ExecuteReader(CommandBehavior.CloseConnection);
            }
            catch (SqlException ex)
            {
                InvalidateParameterCache();
                throw BuildContextException(ex);
            }
            catch
            {
                InvalidateParameterCache();
                throw;
            }
        }

        public object ExecuteScalar()
        {
            try
            {
                EnsureCommand();
                TransferParameters();

                OpenConnection();

                var start = Stopwatch.StartNew();
                object ret = this.sqlCommand.ExecuteScalar();
                TransferParametersPost();
                ExecuteTime = start.Elapsed;

                return ret;
            }
            catch (SqlException ex)
            {
                InvalidateParameterCache();
                throw BuildContextException(ex);
            }
            catch
            {
                InvalidateParameterCache();
                throw;
            }
        }

        public T ExecuteScalar<T>(T defaultValue)
        {
            var start = Stopwatch.StartNew();
            object val = ExecuteScalar();
            ExecuteTime = start.Elapsed;

            if (val == null || val == DBNull.Value)
                return defaultValue;
            else
                return (T)Convert.ChangeType(val, typeof(T));
        }

        public SqlDataReader ExecuteReader(CommandBehavior commandBehavior = CommandBehavior.Default)
        {
            try
            {
                EnsureCommand();
                TransferParameters();

                OpenConnection();
                return this.sqlCommand.ExecuteReader(commandBehavior);
            }
            catch (SqlException ex)
            {
                InvalidateParameterCache();
                throw BuildContextException(ex);
            }
            catch
            {
                InvalidateParameterCache();
                throw;
            }
        }

        public void SetParameterType(string parameterName, SqlDbType parameterType)
        {
            explicitParameterTypes[parameterName] = parameterType;
        }

        /// <summary>
        /// Declares a parameter with an explicit type and direction so it can be sent to the
        /// server without calling <see cref="Prepare()"/> (which would derive metadata from a
        /// round-trip). Output/return values are read back after execution via
        /// <see cref="Parameters"/> and <see cref="ReturnValue"/> respectively. Returns the
        /// underlying <see cref="SqlParameter"/> so callers can set precision, scale, etc.
        /// </summary>
        public SqlParameter DeclareParameter(string parameterName, SqlDbType type, ParameterDirection direction, int size = 0)
        {
            var parm = new SqlParameter
            {
                ParameterName = parameterName,
                SqlDbType = type,
                Direction = direction
            };
            if (size > 0)
                parm.Size = size;

            declaredParameters[parameterName] = parm;
            return parm;
        }

        /// <summary>
        /// Declares an OUTPUT parameter. Read the result after execution from
        /// <see cref="Parameters"/>[parameterName]. Seeding a value via
        /// <see cref="Parameters"/> promotes it to INPUT/OUTPUT automatically.
        /// </summary>
        public SqlParameter AddOutputParameter(string parameterName, SqlDbType type, int size = 0)
        {
            return DeclareParameter(parameterName, type, ParameterDirection.Output, size);
        }

        /// <summary>
        /// Declares the procedure's RETURN value parameter. Read the result after execution
        /// from <see cref="ReturnValue"/>.
        /// </summary>
        public SqlParameter AddReturnParameter(string parameterName = "@RETURN_VALUE")
        {
            return DeclareParameter(parameterName, SqlDbType.Int, ParameterDirection.ReturnValue);
        }

        public T GetObject<T>(Func<string, string> mapFunction = null, Action<TranslationHandler> translationAction = null, bool strict = false) where T : class, new()
        {
            var results = GetObjects<T>(mapFunction, translationAction, strict);
            return results.FirstOrDefault();
        }

        public IEnumerable<T> GetObjects<T>(Func<string, string> mapFunction = null, Action<TranslationHandler> translationAction = null, bool strict = false) where T : class, new()
        {
            DataTable table = OpenTable();

            // Build the column->property plan once, not once per row.
            List<PropertyMapping> plan = BuildMappingPlan<T>(table, mapFunction, strict);

            var results = new List<T>(table.Rows.Count);
            foreach (DataRow row in table.Rows)
            {
                T obj = Activator.CreateInstance<T>();
                foreach (PropertyMapping map in plan)
                {
                    var th = new TranslationHandler(map.Property, row[map.FieldName]);
                    if (translationAction != null)
                        translationAction(th);
                    if (th.Handled)
                        map.Property.SetValue(obj, th.TranslatedValue, null);
                    else
                        SetValue(obj, row, map.Property, map.FieldName);
                }
                results.Add(obj);
            }
            return results;
        }

        private sealed class PropertyMapping
        {
            public string FieldName;
            public PropertyInfo Property;
        }

        private static List<PropertyMapping> BuildMappingPlan<T>(DataTable table, Func<string, string> mapFunction, bool strict) where T : class
        {
            Type type = typeof(T);
            var plan = new List<PropertyMapping>();

            if (mapFunction != null)
            {
                // Caller maps each column name to a property name.
                foreach (DataColumn col in table.Columns)
                {
                    var propName = mapFunction(col.ColumnName);
                    if (String.IsNullOrWhiteSpace(propName)) continue;

                    var prop = type.GetProperty(propName);
                    if (prop == null) continue;

                    plan.Add(new PropertyMapping { FieldName = col.ColumnName, Property = prop });
                }
            }
            else
            {
                // Match by [FieldToProperty] attribute, or by property name against the columns.
                var sc = strict ? StringComparer.CurrentCulture : StringComparer.CurrentCultureIgnoreCase;
                var columnNames = new HashSet<string>(table.Columns.Cast<DataColumn>().Select(c => c.ColumnName), sc);

                foreach (var prop in type.GetProperties())
                {
                    var attr = prop.GetCustomAttributes(true).OfType<FieldToPropertyAttribute>().FirstOrDefault();
                    if (attr == null && !columnNames.Contains(prop.Name)) continue;

                    plan.Add(new PropertyMapping { FieldName = attr != null ? attr.FieldName : prop.Name, Property = prop });
                }
            }

            return plan;
        }

        private void SetValue<T>(T obj, DataRow row, PropertyInfo prop, string fieldName) where T : class
        {
            object value = row[fieldName];
            if (value == DBNull.Value)
                value = null;

            Type propType = prop.PropertyType;
            Type underlying = Nullable.GetUnderlyingType(propType);

            object newValue;
            if (value == null)
            {
                // A non-nullable value type can't take null; leave it at its default.
                if (propType.IsValueType && underlying == null)
                    return;
                newValue = null;
            }
            else
            {
                Type target = underlying ?? propType;
                newValue = target.IsInstanceOfType(value) ? value : Convert.ChangeType(value, target);
            }

            // This is what sets the class properties of the class
            prop.SetValue(obj, newValue, null);
        }


        public static SqlTransaction CreateTransaction(params StoredProcedure[] procedures)
        {
            SqlTransaction trans = null;
            SqlConnection conn = null;
            for (int x = 0; x < procedures.Length; x++)
            {
                var procedure = procedures[x];
                if (x == 0)
                {
                    trans = procedure.BeginTransaction();
                    conn = procedure.Connection;
                }
                else
                    procedure.Connection = conn;

                procedure.Prepare(trans);
            }
            return trans;
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (this.isDisposed)
                return;

            if (this.sqlCommand != null)
                this.sqlCommand.Dispose();

            // only dispose of the connection if we created it
            if (this.sqlConnection != null && !externalConnection)
                this.sqlConnection.Dispose();

            this.isDisposed = true;
        }

        #endregion
    }
}
