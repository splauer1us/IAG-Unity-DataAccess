using System;
using System.Data.SqlClient;

namespace Iag.Unity.DataAccess
{
    public class UnitySqlCommand : BaseCommand
    {
        public UnitySqlCommand() : this(null, String.Empty) { }

        public UnitySqlCommand(string procedureName)
            : this(null, procedureName) { }

        public UnitySqlCommand(SqlConnection connection, string commandText): base(connection, commandText)
        {
        }
    }
}