using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Iag.Unity.DataAccess
{
    public class TranslationHandler
    {
        public bool Handled { get; set; } = false;
        public PropertyInfo PropertyInfo { get; private set; }
        public object DatabaseValue { get; private set; }
        public object TranslatedValue { get; set; }
        public TranslationHandler(PropertyInfo propertyInfo, object databaseValue)
        {
            this.PropertyInfo = propertyInfo;
            this.DatabaseValue = databaseValue;
        }
    }
}
