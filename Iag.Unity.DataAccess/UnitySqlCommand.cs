using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;

namespace Iag.Unity.DataAccess
{
    public class UnitySqlCommand : StoredProcedure
    {
        public UnitySqlCommand(SqlConnection conn, string text)
            : base(conn, text)
        { }

        public UnitySqlCommand(SqlConnection conn, string text, params object[] parameters)
            : base(conn, String.Format(text, parameters))
        { }

        public UnitySqlCommand(string text)
            : base(text)
        { }

        public UnitySqlCommand(string text, params object[] parameters): base(String.Format(text, parameters))
        {
        }

        public UnitySqlCommand(SqlConnection conn) : base(conn, String.Empty) { }

        public UnitySqlCommand():base()
        {
        }
    }
}
