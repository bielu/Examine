﻿using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Examine;
using Lucene.Net.Documents;
using Lucene.Net.Search;
using Examine.LuceneEngine.Providers;

namespace Examine.LuceneEngine
{
    /// <summary>
    /// An implementation of the search results returned from Lucene.Net
    /// </summary>
    public class SearchResults : ISearchResults
    {


        /// <summary>
        /// Exposes the internal Lucene searcher
        /// </summary>
        public Searcher LuceneSearcher { get; private set; }

        /// <summary>
        /// Exposes the internal lucene query to run the search
        /// </summary>
        public Query LuceneQuery { get; private set; }


        private AllHitsCollector _collector;

        internal SearchResults(Query query, IEnumerable<SortField> sortField, Searcher searcher)
        {
            LuceneQuery = query;

            LuceneSearcher = searcher;
            DoSearch(query, sortField);
        }

        private void DoSearch(Query query, IEnumerable<SortField> sortField)
        {
            if (sortField.Count() == 0)
            {
                var topDocs = LuceneSearcher.Search(query, null, LuceneSearcher.MaxDoc(), new Sort());
                _collector = new AllHitsCollector(topDocs.scoreDocs);
                topDocs = null;
            }
            else
            {
                var topDocs = LuceneSearcher.Search(query, null, LuceneSearcher.MaxDoc(), new Sort(sortField.ToArray()));
                _collector = new AllHitsCollector(topDocs.scoreDocs);
                topDocs = null;
            }
            TotalItemCount = _collector.Count;
        }

        /// <summary>
        /// Gets the total number of results for the search
        /// </summary>
        /// <value>The total items from the search.</value>
        public int TotalItemCount { get; private set; }

        /// <summary>
        /// Internal cache of search results
        /// </summary>
        protected Dictionary<int, SearchResult> Docs = new Dictionary<int, SearchResult>();

        /// <summary>
        /// Creates the search result from a <see cref="Lucene.Net.Documents.Document"/>
        /// </summary>
        /// <param name="doc">The doc to convert.</param>
        /// <param name="score">The score.</param>
        /// <returns>A populated search result object</returns>
        protected SearchResult CreateSearchResult(Document doc, float score)
        {
            string id = doc.Get("id");
            if (string.IsNullOrEmpty(id))
            {
				id = doc.Get(LuceneIndexer.IndexNodeIdFieldName);
            }
            var sr = new SearchResult()
            {
                Id = int.Parse(id),
                Score = score
            };

            //we can use lucene to find out the fields which have been stored for this particular document
            //I'm not sure if it'll return fields that have null values though
            var fields = doc.GetFields();
            
            //ignore our internal fields though
            foreach (Field field in fields.Cast<Field>())
            {
                sr.Fields.Add(field.Name(), doc.Get(field.Name()));
            }

            return sr;
        }

        /// <summary>
        /// Skips to a particular point in the search results.
        /// </summary>
        /// <remarks>
        /// This allows for lazy loading of the results paging. We don't go into Lucene until we have to.
        /// </remarks>
        /// <param name="skip">The number of items in the results to skip.</param>
        /// <returns>A collection of the search results</returns>
        public IEnumerable<SearchResult> Skip(int skip)
        {
            for (int i = skip; i < this.TotalItemCount; i++)
            {
                //first check our own cache to make sure it's not there
                if (!Docs.ContainsKey(i))
                {
                    var docId = _collector.GetDocId(i);
                    var doc = LuceneSearcher.Doc(docId);
                    var score = _collector.GetDocScore(i);

                    Docs.Add(i, CreateSearchResult(doc, score));
                }
                //using yield return means if the user breaks out we wont keep going
                //only load what we need to load!
                //and we'll get it from our cache, this means you can go 
                //forward/ backwards without degrading performance
                yield return Docs[i];
            }
        }

        #region IEnumerable<SearchResult> Members

        /// <summary>
        /// Gets the enumerator starting at position 0
        /// </summary>
        /// <returns>A collection of the search results</returns>
        public IEnumerator<SearchResult> GetEnumerator()
        {
            //if we're going to Enumerate from this itself we're not going to be skipping
            //so unless we made it IQueryable we can't do anything better than start from 0
            return this.Skip(0).GetEnumerator();
        }

        #endregion

        #region IEnumerable Members

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IEnumerator"/> object that can be used to iterate through the collection.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }

        #endregion
    }
}
