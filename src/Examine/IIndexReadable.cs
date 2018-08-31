using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Examine
{
    public interface IIndexReadable
    {
        bool IsReadable(out Exception ex);
    }
}
