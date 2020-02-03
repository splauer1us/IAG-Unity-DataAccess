using System;
using System.Data.SqlClient;
using System.Text;
using System.Data;
using Iag.Unity.Core.Enumerations;

namespace Iag.Unity.DataAccess
{
    public static class DataLibrary
    {
        public static object sqlVersionLock = new object();
        public static string _sqlVersionText = String.Empty;

        public static string ConnectionString { get; set; }
        public static bool IsInitialized { get; set; }
        public static Action<Exception> LoggingCallback { get; set; }
        public static void Initialize(string connectionString, Action<Exception> loggingCallback = null)
        {
            try
            {
                LoggingCallback = loggingCallback ?? ((ex)=>{ });
                DataLibrary.ConnectionString = connectionString;
                lock (sqlVersionLock)
                    _sqlVersionText = null;

                //Test the connection. 
                using (SqlConnection conn = GetConnection())
                {
                    IsInitialized = true;
                }
            }
            catch (Exception)
            {
                IsInitialized = false;
                throw;
            }
        }


        public static string BuildExceptionMessage(Exception ex)
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendFormat("<{0}> {1}", ex.GetType().Name, ex.Message).AppendLine();
            builder.AppendLine(ex.StackTrace);

            var innerException = ex.InnerException;
            int counter = 1;

            while ((innerException != null) || (counter > 20))
            {
                builder
                    .AppendLine()
                    .AppendFormat("[Inner Exception #{0}]", (counter++).ToString()).AppendLine()
                    .AppendLine()
                    .AppendFormat("<{0}> {1}", innerException.GetType().Name, innerException.Message).AppendLine()
                    .AppendLine(innerException.StackTrace);

                innerException = innerException.InnerException;
            }

            builder.AppendLine();

            return builder.ToString();
        }

        public static System.Data.SqlClient.SqlConnection GetConnection(bool doNotOpen = false)
        {
            if (String.IsNullOrWhiteSpace(DataLibrary.ConnectionString))
                throw new InvalidOperationException("Unable to retrieve default connection.  The DataLibrary has not been initialized.");
            SqlConnection connection = new SqlConnection(DataLibrary.ConnectionString);
            if (!doNotOpen)
                connection.Open();
            return connection;
        }

        public static void Initialize(string serverName, string databaseName, bool useIntegratedSecurity, string userName, string password)
        {
            if (useIntegratedSecurity)
                Initialize(String.Format("Server={0}; Database={1}; Integrated Security=true;", serverName, databaseName));
            else
                Initialize(String.Format("Server={0}; Database={1}; User Id={2}; Password={3};", serverName, databaseName, userName, password));
        }

        public static void Initialize(string serverName, string databaseName)
        {
            Initialize(serverName, databaseName, true, null, null);
        }

        public static void Initialize(string serverName, string databaseName, string username, string password)
        {
            Initialize(serverName, databaseName, false, username, password);
        }

        public static SimpleDataType GetSimpleDataType(SqlDbType dbType)
        {
            switch (dbType)
            {
                case SqlDbType.BigInt:
                case SqlDbType.Decimal:
                case SqlDbType.Float:
                case SqlDbType.Int:
                case SqlDbType.Money:
                case SqlDbType.Real:
                case SqlDbType.SmallInt:
                case SqlDbType.SmallMoney:
                case SqlDbType.TinyInt:
                    return SimpleDataType.Numeric;
                case SqlDbType.Bit:
                    return SimpleDataType.Boolean;
                case SqlDbType.DateTime:
                case SqlDbType.Date:
                case SqlDbType.DateTime2:
                case SqlDbType.DateTimeOffset:
                case SqlDbType.SmallDateTime:
                    return SimpleDataType.DateTime;
                case SqlDbType.UniqueIdentifier:
                    return SimpleDataType.Guid;
                default:
                    return SimpleDataType.String;
            }
        }

        public static SimpleDataType GetSimpleDataType(Type type)
        {
            if (type == typeof(int) ||
                type == typeof(int?) ||
                type == typeof(long) ||
                type == typeof(long?) ||
                type == typeof(decimal) ||
                type == typeof(decimal?) ||
                type == typeof(double) ||
                type == typeof(double?) ||
                type == typeof(float) ||
                type == typeof(float?) ||
                type == typeof(short) ||
                type == typeof(short?) ||
                type == typeof(byte) ||
                type == typeof(byte?))
            {
                return SimpleDataType.Numeric;
            }
            else if (type == typeof(bool) || type == typeof(bool?))
                return SimpleDataType.Boolean;
            else if (type == typeof(Guid) || type == typeof(Guid?))
                return SimpleDataType.Guid;
            else if (type == typeof(DateTime) ||
                type == typeof(DateTime?) ||
                type == typeof(DateTimeOffset) ||
                type == typeof(DateTimeOffset?))
            {
                return SimpleDataType.DateTime;
            }
            else
                return SimpleDataType.String;
        }

        public static Type GetDotNetType(SqlDbType type)
        {
            switch (type)
            {
                case SqlDbType.Decimal:
                case SqlDbType.SmallMoney:
                case SqlDbType.Money:
                    return typeof(Decimal);
                case SqlDbType.Float:
                case SqlDbType.Real:
                    return typeof(Double);
                case SqlDbType.BigInt:
                    return typeof(Int64);
                case SqlDbType.Int:
                    return typeof(Int32);
                case SqlDbType.SmallInt:
                    return typeof(Int16);
                case SqlDbType.TinyInt:
                    return typeof(Byte);
                case SqlDbType.Bit:
                    return typeof(Boolean);
                case SqlDbType.DateTime:
                case SqlDbType.Date:
                case SqlDbType.DateTime2:
                case SqlDbType.SmallDateTime:
                    return typeof(DateTime);
                case SqlDbType.DateTimeOffset:
                    return typeof(DateTimeOffset);
                case SqlDbType.UniqueIdentifier:
                    return typeof(Guid);
                default:
                    return typeof(string);
            }
        }
    }
}
