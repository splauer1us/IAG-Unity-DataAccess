using System;
using System.Data.SqlClient;

namespace Iag.Unity.DataAccess{
    public class StoredProcedure: BaseCommand
    {
        private static string GetProcedureTextSql = "SELECT OBJECT_DEFINITION(OBJECT_ID(@ProcName, 'P')) AS objectText";

        public static string GetProcedureText(string procedureName, SqlConnection conn = null)
        {
            bool externalConnection = conn != null;
            try
            {
                if (conn == null)
                    conn = DataLibrary.GetConnection();
                using (UnitySqlCommand cmd = new UnitySqlCommand(conn, GetProcedureTextSql))
                {
                    cmd.Prepare();
                    cmd.Parameters["@ProcName"] = procedureName;
                    return cmd.ExecuteScalar<string>(String.Empty);
                }
            }
            finally
            {
                if (externalConnection && conn != null)
                    conn.Dispose();
            }
        }

        public StoredProcedure() : this(null, String.Empty) { }

        public StoredProcedure(string procedureName)
            : this(null, procedureName) { }

        public StoredProcedure(SqlConnection connection, string procedureName): base(connection, procedureName)
        {

        }
    }
}