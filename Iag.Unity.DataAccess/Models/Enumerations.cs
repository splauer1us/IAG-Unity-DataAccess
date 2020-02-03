using System;

namespace Iag.Unity.Core.Enumerations
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
