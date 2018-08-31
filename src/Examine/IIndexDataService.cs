using System.Collections.Generic;

namespace Examine
{
    public interface IIndexDataService
    {
        
        //TODO: We need to modify this and pass in the indexer instance so that a data service can lazily be created
        // and be created based on the indexer's properties

        /// <summary>
        /// Returns a collection of <see cref="IndexDocument"/>
        /// </summary>
        /// <param name="indexer"></param>
        /// <param name="indexType"></param>
        /// <returns></returns>
        IEnumerable<IndexDocument> GetAllData(IIndexer indexer, string indexType);


    }
}