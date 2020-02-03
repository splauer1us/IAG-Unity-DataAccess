using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Text;
using Iag.Unity.DataAccess.Exceptions;

namespace Iag.Unity.DataAccess
{
    public abstract class BaseCommand : IDisposable
    {
        private Dictionary<string, object> parameters = new Dictionary<string, object>(StringComparer.CurrentCultureIgnoreCase);
        private Dictionary<string, SqlDbType> explicitParameterTypes = new Dictionary<string, SqlDbType>(StringComparer.CurrentCultureIgnoreCase);

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
            DateTime start = DateTime.Now;
            if (sqlConnection == null)
                throw new InvalidOperationException("The sql connection is null.");
            if (this.sqlCommand == null || (this.sqlCommand.Transaction == null && trans != null) || (this.sqlCommand.Transaction != null && trans == null))
                this.sqlCommand = GetCommand(procedureName, this.sqlConnection, trans);

            isPrepared = true;
            PrepareTime = DateTime.Now.Subtract(start);
        }

        private SqlCommand GetCommand(string procedureName, SqlConnection sqlConnection, SqlTransaction transaction)
        {
            SqlCommand cmd = new SqlCommand(procedureName, sqlConnection, transaction);
            cmd.CommandType = (this is UnitySqlCommand ? CommandType.Text : CommandType.StoredProcedure);
            cmd.CommandTimeout = timeout;
            cmd.Parameters.Clear();

            if (!(this is UnitySqlCommand))
                FillParameters(cmd);

            return cmd;
        }

        private void FillParameters(SqlCommand command)
        {
            OpenConnection();
            SqlCommandBuilder.DeriveParameters(command);

            this.parameters = new Dictionary<string, object>(StringComparer.CurrentCultureIgnoreCase);
            foreach (SqlParameter parm in command.Parameters)
            {
                this.derivedParameters.Add(parm);
            }
        }

        public void Execute()
        {
            try
            {
                DateTime start = DateTime.Now;
                if (!isPrepared)
                    Prepare();

                OpenConnection();
                TransferParameters();

                this.sqlCommand.ExecuteNonQuery();
                TransferParametersPost();
                ExecuteTime = DateTime.Now.Subtract(start);
            }
            catch (SqlException ex)
            {
                throw BuildContextException(ex);
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
            if (!(this is UnitySqlCommand))
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
                foreach (KeyValuePair<string, object> kvp in this.Parameters)
                {
                    this.sqlCommand.Parameters.AddWithValue(kvp.Key, kvp.Value);
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
                if (!isPrepared)
                    Prepare();

                DateTime start = DateTime.Now;
                TransferParameters();

                OpenConnection();
                SqlDataAdapter da = new SqlDataAdapter(this.sqlCommand);
                DataSet ds = String.IsNullOrWhiteSpace(name) ? new DataSet() : new DataSet(name);
                da.Fill(ds);
                TransferParametersPost();
                ExecuteTime = DateTime.Now.Subtract(start);

                return ds;
            }
            catch (SqlException ex)
            {
                throw BuildContextException(ex);
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
                if (!isPrepared)
                    Prepare();
                TransferParameters();

                OpenConnection();
                return this.sqlCommand.ExecuteReader(CommandBehavior.CloseConnection);
            }
            catch (SqlException ex)
            {
                throw BuildContextException(ex);
            }
        }

        public object ExecuteScalar()
        {
            try
            {
                if (!isPrepared)
                    Prepare();
                TransferParameters();

                OpenConnection();

                DateTime start = DateTime.Now;
                object ret = this.sqlCommand.ExecuteScalar();
                TransferParametersPost();
                ExecuteTime = DateTime.Now.Subtract(start);

                return ret;
            }
            catch (SqlException ex)
            {
                throw BuildContextException(ex);
            }
        }

        public T ExecuteScalar<T>(T defaultValue)
        {
            DateTime start = DateTime.Now;
            object val = ExecuteScalar();
            ExecuteTime = DateTime.Now.Subtract(start);

            if (val == null || val == DBNull.Value)
                return defaultValue;
            else
                return (T)Convert.ChangeType(val, typeof(T));
        }

        public SqlDataReader ExecuteReader(CommandBehavior commandBehavior = CommandBehavior.Default)
        {
            try
            {
                if (!isPrepared)
                    Prepare();
                TransferParameters();

                OpenConnection();
                return this.sqlCommand.ExecuteReader(commandBehavior);
            }
            catch (SqlException ex)
            {
                throw BuildContextException(ex);
            }
        }

        public void SetParameterType(string parameterName, SqlDbType parameterType)
        {
            explicitParameterTypes[parameterName] = parameterType;
        }

        public T GetObject<T>(Func<string, string> mapFunction = null, Action<TranslationHandler> translationAction = null, bool strict = false) where T : class, new()
        {
            var results = GetObjects<T>(mapFunction, translationAction, strict);
            return results.FirstOrDefault();
        }

        public IEnumerable<T> GetObjects<T>(Func<string, string> mapFunction = null, Action<TranslationHandler> translationAction = null, bool strict = false) where T : class, new()
        {
            return GetRows().ToList().Select(row =>
            {
                T obj = Activator.CreateInstance<T>();

                if (mapFunction != null)
                    FillFromFunction<T>(obj, row, mapFunction, translationAction);
                else
                    FillFromMapping<T>(obj, row, strict, translationAction);
                return obj;
            });
        }


        // http://stackoverflow.com/questions/374651/how-to-check-if-an-object-is-nullable
        private bool IsNullable(Type type)
        {
            return (!type.IsValueType || Nullable.GetUnderlyingType(type) != null);
        }

        private void FillFromFunction<T>(T obj, DataRow row, Func<string, string> mapFunction, Action<TranslationHandler> translationAction) where T : class
        {
            Type type = obj.GetType();
            row.Table.Columns.Cast<DataColumn>().ToList().ForEach(col =>
            {
                var propName = mapFunction(col.ColumnName);
                if (String.IsNullOrWhiteSpace(propName)) return;

                var propInfo = type.GetProperty(propName);
                if (propInfo == null)
                {
                    return;
                    //throw new InvalidOperationException($"The property {propName} does not exist on type {type}.");
                }

                var th = new TranslationHandler(propInfo, row[col.ColumnName]);
                if (translationAction != null)
                    translationAction(th);
                if (th.Handled)
                    propInfo.SetValue(obj, th.TranslatedValue, null);
                else
                    SetValue<T>(obj, row, propInfo, col.ColumnName);
            });
        }

        private void FillFromMapping<T>(T obj, DataRow row, bool strict, Action<TranslationHandler> translationAction) where T : class
        {
            Type type = obj.GetType();

            var sc = strict ? StringComparer.CurrentCulture : StringComparer.CurrentCultureIgnoreCase;

            type.GetProperties()
                .ToList()
                .ForEach(prop =>
                {
                    var attr = prop.GetCustomAttributes(true).OfType<FieldToPropertyAttribute>().FirstOrDefault();

                    if (attr == null
                        && !row.Table.Columns.Cast<DataColumn>().Any(col => sc.Equals(col.ColumnName, prop.Name))) return;

                    string fieldName = attr != null ? attr.FieldName : prop.Name;

                    var th = new TranslationHandler(prop, row[fieldName]);
                    if (translationAction != null)
                        translationAction(th);
                    if (th.Handled)
                        prop.SetValue(obj, th.TranslatedValue, null);
                    else
                        SetValue<T>(obj, row, prop, fieldName);
                });
        }

        private void SetValue<T>(T obj, DataRow row, PropertyInfo prop, string fieldName) where T : class
        {
            object value = row[fieldName];
            if (value == DBNull.Value)
                value = null;

            object newValue = null;

            if (IsNullable(prop.PropertyType))
                newValue = (Nullable.GetUnderlyingType(prop.PropertyType) != null && value != null ? Convert.ChangeType(value, Nullable.GetUnderlyingType(prop.PropertyType)) : value);
            else
                newValue = Convert.ChangeType(value, prop.PropertyType);

            // This is what sets the class properties of the class
            prop.SetValue(obj, newValue, null);
        }


        public static SqlTransaction CreateTransaction(params StoredProcedure[] procedures)
        {
            SqlTransaction trans = null;
            SqlConnection conn = null;
            for (int x = 0; x < procedures.Length; x++)
            {
                var procedure = procedures[0];
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!this.isDisposed)
            {
                if (disposing)
                {
                    DisposeObjects();
                }
            }
            this.isDisposed = true;
        }

        private void DisposeObjects()
        {

            if (this.sqlCommand != null)
                this.sqlCommand.Dispose();

            //only dispose of connection if we created it
            if (this.sqlConnection != null && !externalConnection)
            {
                this.sqlConnection.Dispose();
            }
        }

        #endregion
        /// <summary>
        /// Finalizer for this class
        /// </summary>
        ~BaseCommand()
        {
            Dispose(false);
        }
    }
}
