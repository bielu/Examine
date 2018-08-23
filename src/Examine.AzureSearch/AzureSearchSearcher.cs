using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using Examine.LuceneEngine;
using Examine.LuceneEngine.Config;
using Examine.LuceneEngine.SearchCriteria;
using Examine.Providers;
using Examine.SearchCriteria;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using StandardAnalyzer = Lucene.Net.Analysis.Standard.StandardAnalyzer;
using Version = Lucene.Net.Util.Version;

namespace Examine.AzureSearch
{
    public class AzureSearchSearcher : BaseSearchProvider, IDisposable
    {
        private string _indexName;
        private string _searchServiceName;
        private string _apiKey;
        private readonly Lazy<ISearchIndexClient> _searchClient;
        private readonly Lazy<ISearchServiceClient> _indexClient;
        //TODO: maybe simple analyzer would be better just for query parsing?
        private readonly StandardAnalyzer _standardAnalyzer = new StandardAnalyzer(Version.LUCENE_29);
        private readonly Lazy<string[]> _allFields;

        /// <summary>
        /// Constructor used for provider model
        /// </summary>
        public AzureSearchSearcher()
        {
            _searchClient = new Lazy<ISearchIndexClient>(CreateSearchIndexClient);
            _indexClient = new Lazy<ISearchServiceClient>(CreateSearchServiceClient);
            _allFields = new Lazy<string[]>(() =>
            {
                var index = _indexClient.Value.Indexes.Get(_indexName);
                var fields = index.Fields.Select(x => x.Name);
                return fields.ToArray();
            });
        }

        /// <summary>
        /// Constructor used for runtime based instances
        /// </summary>
        /// <param name="indexName"></param>
        /// <param name="searchServiceName"></param>
        /// <param name="apiKey"></param>
        public AzureSearchSearcher(string indexName, string searchServiceName, string apiKey)
            : this()
        {
            //TODO: Need to 'clean' the name according to Azure Search rules
            _indexName = indexName.ToLowerInvariant();
            _searchServiceName = searchServiceName;
            _apiKey = apiKey;
            
        }

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);
            
            //need to check if the index set is specified, if it's not, we'll see if we can find one by convension
            //if the folder is not null and the index set is null, we'll assume that this has been created at runtime.
            //NOTE: Don't proceed if the _luceneDirectory is set since we already know where to look.
            if (config["indexSet"] == null)
            {
                //if we don't have either, then we'll try to set the index set by naming convensions
                var found = false;
                if (name.EndsWith("Searcher"))
                {
                    var setNameByConvension = name.Remove(name.LastIndexOf("Searcher")) + "IndexSet";
                    //check if we can assign the index set by naming convension
                    var set = IndexSets.Instance.Sets.Cast<IndexSet>().SingleOrDefault(x => x.SetName == setNameByConvension);

                    if (set != null)
                    {
                        //we've found an index set by naming convensions :)
                        //TODO: Need to 'clean' the name according to Azure Search rules
                        _indexName = set.SetName.ToLowerInvariant();
                        found = true;
                    }
                }

                if (!found)
                    throw new ArgumentNullException("indexSet on AzureSearchSearcher provider has not been set in configuration");
            }
            else if (config["indexSet"] != null)
            {
                if (IndexSets.Instance.Sets[config["indexSet"]] == null)
                    throw new ArgumentException("The indexSet specified for the AzureSearchSearcher provider does not exist");

                //TODO: Need to 'clean' the name according to Azure Search rules
                _indexName = config["indexSet"].ToLowerInvariant();
            }

            var azureSearchConfig = AzureSearchConfig.GetConfig(_indexName);
            _searchServiceName = azureSearchConfig.SearchServiceName;
            _apiKey = azureSearchConfig.ApiKey;
        }

        public override ISearchResults Search(string searchText, bool useWildcards)
        {
            //if (!_indexClient.Value.Indexes.Exists(_indexName))
            //    return EmptySearchResults.Instance;

            if (!useWildcards)
            {
                //just do a simple azure search
                return new AzureSearchResults(_searchClient.Value.Documents, searchText);
            }

            var sc = CreateSearchCriteria();
            return TextSearchAllFields(searchText, true, sc);
        }

        public override ISearchResults Search(string searchText, bool useWildcards, string indexType)
        {
            //if (!_indexClient.Value.Indexes.Exists(_indexName))
            //    return EmptySearchResults.Instance;

            var sc = CreateSearchCriteria(indexType);
            return TextSearchAllFields(searchText, useWildcards, sc);
        }

        public override ISearchResults Search(ISearchCriteria searchParams, int maxResults)
        {
            if (searchParams == null) throw new ArgumentNullException(nameof(searchParams));

            //if (!_indexClient.Value.Indexes.Exists(_indexName))
            //    return EmptySearchResults.Instance;

            var luceneParams = searchParams as LuceneSearchCriteria;
            if (luceneParams == null)
                throw new ArgumentException("Provided ISearchCriteria dos not match the allowed ISearchCriteria. Ensure you only use an ISearchCriteria created from the current SearcherProvider");

            var pagesResults = new AzureSearchResults(_searchClient.Value.Documents, luceneParams.Query, maxResults == 0 ? null : (int?)maxResults);
            return pagesResults;
        }

        public override ISearchResults Search(ISearchCriteria searchParams)
        {
            return Search(searchParams, 0);
        }

        public override ISearchCriteria CreateSearchCriteria()
        {
            return CreateSearchCriteria(string.Empty, BooleanOperation.And);
        }

        public override ISearchCriteria CreateSearchCriteria(BooleanOperation defaultOperation)
        {
            return CreateSearchCriteria(string.Empty, defaultOperation);
        }

        public override ISearchCriteria CreateSearchCriteria(string type, BooleanOperation defaultOperation)
        {
            return new LuceneSearchCriteria(type, _standardAnalyzer, _allFields.Value, true, defaultOperation);
        }

        public override ISearchCriteria CreateSearchCriteria(string type)
        {
            return CreateSearchCriteria(type, BooleanOperation.And);
        }



        private ISearchResults TextSearchAllFields(string searchText, bool useWildcards, ISearchCriteria sc)
        {
            var splitSearch = searchText.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (useWildcards)
            {
                sc = sc.GroupedOr(_allFields.Value,
                    splitSearch.Select(x =>
                        new ExamineValue(Examineness.ComplexWildcard, x.MultipleCharacterWildcard().Value)).Cast<IExamineValue>().ToArray()
                ).Compile();
            }
            else
            {
                sc = sc.GroupedOr(_allFields.Value, splitSearch).Compile();
            }

            return Search(sc);
        }

        private ISearchIndexClient CreateSearchIndexClient()
        {
            return new SearchIndexClient(_searchServiceName, _indexName, new SearchCredentials(_apiKey));
        }
        
        private ISearchServiceClient CreateSearchServiceClient()
        {
            return new SearchServiceClient(_searchServiceName, new SearchCredentials(_apiKey));
        }

        public void Dispose()
        {
            if (_searchClient.IsValueCreated)
                _searchClient.Value.Dispose();
            if (_indexClient.IsValueCreated)
                _indexClient.Value.Dispose();
        }
    }
}