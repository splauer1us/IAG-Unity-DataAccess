using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Iag.Unity.DataAccess.Exceptions
{
    public interface IContextualException
    {
        string Context { get; }
    }
}
