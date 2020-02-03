using System;
using System.Runtime.Serialization;

namespace Iag.Unity.DataAccess.Exceptions
{
    public class ContextualSqlException : Exception, IContextualException
    {
        private string context;

        public string Context { get { return context; } set { context = value; } }

        public ContextualSqlException()
        {
        }

        public ContextualSqlException(string message) : base(message)
        {
        }

        public ContextualSqlException(string message, string context) : base(message)
        {
            Context = context;
        }

        public ContextualSqlException(string message, Exception inner) : this(message, inner, null)
        {
        }

        public ContextualSqlException(string message, Exception inner, string context) : base(message, inner)
        {
            Context = context;
        }

        protected ContextualSqlException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            if (info != null)
                this.context = info.GetString("context");
        }

        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);

            if (info != null)
                info.AddValue("context", this.context);
        }
    }
}
