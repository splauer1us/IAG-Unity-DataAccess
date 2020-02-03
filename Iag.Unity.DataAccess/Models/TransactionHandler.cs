using System.Reflection;

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
