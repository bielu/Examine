using System;
using System.Linq;
using Examine.LuceneEngine;
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
        private readonly string _name;
        private readonly Lazy<ISearchIndexClient> _searchClient;
        private readonly Lazy<ISearchServiceClient> _indexClient = new Lazy<ISearchServiceClient>(CreateSearchServiceClient);
        //TODO: maybe simple analyzer would be better just for query parsing?
        private readonly StandardAnalyzer _standardAnalyzer = new StandardAnalyzer(Version.LUCENE_29);
        private readonly Lazy<string[]> _allFields;

        public AzureSearchSearcher(string name)
        {
            //TODO: Need to 'clean' the name according to Azure Search rules
            _name = name;
            _searchClient = new Lazy<ISearchIndexClient>(CreateSearchIndexClient);
            _allFields = new Lazy<string[]>(() =>
            {
                var index = _indexClient.Value.Indexes.Get(_name);
                var fields = index.Fields.Select(x => x.Name);
                return fields.ToArray();
            });
        }

        public override ISearchResults Search(string searchText, bool useWildcards)
        {
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
            var sc = CreateSearchCriteria(indexType);
            return TextSearchAllFields(searchText, useWildcards, sc);
        }

        public override ISearchResults Search(ISearchCriteria searchParams, int maxResults)
        {
            if (searchParams == null) throw new ArgumentNullException(nameof(searchParams));

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
            //TODO: refactor this

            var searchServiceName = "examine-test";
            var adminApiKey = "F72FFB987CF9FEAF57EC007B7A2A592D";

            return new SearchIndexClient(searchServiceName, _name, new SearchCredentials(adminApiKey));
        }
        
        private static ISearchServiceClient CreateSearchServiceClient()
        {
            //TODO: refactor this

            var searchServiceName = "examine-test";
            var adminApiKey = "F72FFB987CF9FEAF57EC007B7A2A592D";

            return new SearchServiceClient(searchServiceName, new SearchCredentials(adminApiKey));
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