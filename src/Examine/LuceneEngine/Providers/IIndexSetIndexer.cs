using Examine.LuceneEngine.Config;

namespace Examine.LuceneEngine.Providers
{
    public interface IIndexSetIndexer
    {
        /// <summary>
        /// The index set name which references an Examine <see cref="IndexSet"/>
        /// </summary>
        string IndexSetName { get; }

        /// <summary>
        /// Returns IIndexCriteria object from the IndexSet, used to configure the indexer during initialization
        /// </summary>
        /// <param name="indexSet"></param>
        IIndexCriteria CreateIndexerData(IndexSet indexSet);

    }
}