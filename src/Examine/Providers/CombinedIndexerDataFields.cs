using System.Collections.Generic;
using System.Linq;

namespace Examine.Providers
{
    /// <summary>
    /// The dictionary will be for keys but each key could contain multiple IIndexField
    /// </summary>
    public class CombinedIndexerDataFields : Dictionary<string, IReadOnlyList<IIndexField>>
    {
        public CombinedIndexerDataFields(IEnumerable<IIndexField> allFields)
        {
            foreach (var f in allFields.GroupBy(x => x.Name))
            {
                Add(f.Key, f.ToList());
            }
        }
        
    }
}