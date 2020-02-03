using System;

namespace Iag.Unity.DataAccess
{
    public class FieldToPropertyAttribute : Attribute
    {
        public string FieldName { get; private set; }
        
        public FieldToPropertyAttribute(string fieldName)
        {
            this.FieldName = fieldName;
        }
    }
}
