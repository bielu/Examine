using System;
using System.Collections.Specialized;
using System.Linq;
using Examine.LuceneEngine.Config;
using Examine.Providers;

namespace Examine.LuceneEngine.Providers
{
    public static class IndexProviderExtensions
    {
        /// <summary>
        /// Returns IIndexCriteria object from the IndexSet
        /// </summary>
        /// <param name="indexSet"></param>
        public static  IIndexCriteria GetIndexerData(IndexSet indexSet)
        {
            return new IndexCriteria(
                indexSet.IndexAttributeFields.Cast<IIndexField>().ToArray(),
                indexSet.IndexUserFields.Cast<IIndexField>().ToArray(),
                indexSet.IncludeNodeTypes.ToList().Select(x => x.Name).ToArray(),
                indexSet.ExcludeNodeTypes.ToList().Select(x => x.Name).ToArray(),
                indexSet.IndexParentId);
        }

        public static bool ConfigureIndexSet(this BaseIndexProvider provider, 
            string name, NameValueCollection config, 
            out IIndexCriteria indexerData, out string indexSetName)
        {
            //Need to check if the index set or IndexerData is specified...
            indexSetName = null;
            indexerData = null;

            if (config["indexSet"] == null)
            {
                //if we don't have either, then we'll try to set the index set by naming conventions
                if (name.EndsWith("Indexer"))
                {
                    var setNameByConvension = name.Remove(name.LastIndexOf("Indexer")) + "IndexSet";
                    //check if we can assign the index set by naming convention
                    var set = IndexSets.Instance.Sets.Cast<IndexSet>().SingleOrDefault(x => x.SetName == setNameByConvension);

                    if (set != null)
                    {
                        //we've found an index set by naming conventions :)
                        indexSetName = set.SetName;

                        var indexSet = IndexSets.Instance.Sets[indexSetName];

                        //if tokens are declared in the path, then use them (i.e. {machinename} )
                        indexSet.ReplaceTokensInIndexPath();

                        //get the index criteria and ensure folder
                        indexerData = GetIndexerData(indexSet);

                        return true;
                    }
                }

                return false;

            }

            if (config["indexSet"] != null)
            {
                //if an index set is specified, ensure it exists and initialize the indexer based on the set

                if (IndexSets.Instance.Sets[config["indexSet"]] == null)
                {
                    throw new ArgumentException("The indexSet specified for the LuceneExamineIndexer provider does not exist");
                }

                indexSetName = config["indexSet"];

                var indexSet = IndexSets.Instance.Sets[indexSetName];

                //if tokens are declared in the path, then use them (i.e. {machinename} )
                indexSet.ReplaceTokensInIndexPath();

                //get the index criteria and ensure folder
                indexerData = GetIndexerData(indexSet);

                return true;
            }

            return false;
        }
    }
}