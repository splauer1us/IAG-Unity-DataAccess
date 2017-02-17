using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Iag.Unity.Enumerations
{
    [Serializable]
    public enum SimpleDataType
    {
        Unknown = 0,
        Numeric = 1,
        String = 2,
        Boolean = 3,
        DateTime = 4,
        Guid = 5
    }
}
