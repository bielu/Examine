using System.Collections.Generic;

namespace Examine
{
    public interface IIndexDataService
    {
        ///// <summary>
        ///// The Supported index types for this data source
        ///// </summary>
        //IEnumerable<string> IndexTypes { get; }

        /// <summary>
        /// Returns a collection of <see cref="IndexDocument"/>
        /// </summary>
        /// <param name="indexType"></param>
        /// <returns></returns>
        IEnumerable<IndexDocument> GetAllData(string indexType);


    }
}