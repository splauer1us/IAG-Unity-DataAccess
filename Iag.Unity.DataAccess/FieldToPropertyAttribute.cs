using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
