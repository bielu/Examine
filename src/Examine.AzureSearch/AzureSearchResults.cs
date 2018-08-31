using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Lucene.Net.Search;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

namespace Examine.AzureSearch
{
    public class AzureSearchResults : ISearchResults
    {
        private readonly IDocumentsOperations _docs;
        private readonly BooleanQuery _luceneQuery;
        private readonly int? _maxResults;
        private readonly string _query;

        public AzureSearchResults(IDocumentsOperations docs, string query, int? maxResults = null)
        {
            if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("Value cannot be null or whitespace.", nameof(query));
            _docs = docs;
            _query = query;
            _maxResults = maxResults;
        }

        public AzureSearchResults(IDocumentsOperations docs, BooleanQuery luceneQuery, int? maxResults = null)
        {
            _docs = docs;
            _luceneQuery = luceneQuery ?? throw new ArgumentNullException(nameof(luceneQuery));
            _maxResults = maxResults;
        }

        public IEnumerator<SearchResult> GetEnumerator()
        {
            var result = DoSearch(null);
            return ConvertResult(result).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int TotalItemCount { get; private set; }

        public IEnumerable<SearchResult> Skip(int skip)
        {
            var result = DoSearch(skip);
            return ConvertResult(result);
        }

        private static IEnumerable<SearchResult> ConvertResult(DocumentSearchResult result)
        {
            return result.Results.Select(x =>
            {
                var id = int.Parse((string) x.Document["id"]);
                var r = new SearchResult
                {
                    DocId = id,
                    Id = id,
                    Score = Convert.ToInt64((double) x.Score)
                };
                foreach (var d in x.Document)
                {
                    if (d.Key == null || d.Value == null) continue;
                    r.Fields[d.Key] = d.Value.ToString();
                }

                return r;
            });
        }

        private DocumentSearchResult DoSearch(int? skip)
        {
            var query = _query;
            var isLucene = false;
            if (string.IsNullOrWhiteSpace(query))
            {
                //it's a lucene query    
                query = _luceneQuery.ToString();

                //HACK! we need to prefix any field starting with __ with z__ due to Azure Search not supporting fields starting with __
                //TODO: We need to fix this properly, there could be __ in the search text so this could cause problems, we can probably do 
                //this by iterating over the clauses of the _luceneQuery
                
                query = query.Replace("__", "z__");

                isLucene = true;
            }

            //TODO: Get sorting working


            var result = _docs.Search(query, new SearchParameters
            {
                IncludeTotalResultCount = true,
                Skip = skip,
                Top = _maxResults,
                QueryType = isLucene ? QueryType.Full : QueryType.Simple
            });
            if (result.Count != null)
                TotalItemCount = Convert.ToInt32(result.Count.Value);
            return result;
        }

    }
}