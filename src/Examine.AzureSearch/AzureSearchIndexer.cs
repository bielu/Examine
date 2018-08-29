
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Examine.LuceneEngine;
using Examine.LuceneEngine.Config;
using Examine.LuceneEngine.Providers;
using Examine.Providers;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

namespace Examine.AzureSearch
{
    public class AzureSearchIndexer : IndexProvider, IDisposable
    {
        private string _indexName;
        private string _searchServiceName;
        private string _apiKey;
        private bool? _exists;
        private ISearchIndexClient _indexer;
        private readonly Lazy<ISearchServiceClient> _client;
        
        public AzureSearchIndexer()
        {
            _client = new Lazy<ISearchServiceClient>(CreateSearchServiceClient);
        }

        public AzureSearchIndexer(string indexName, string searchServiceName, string apiKey, string analyzer, 
            IIndexCriteria indexerData, IIndexDataService dataService)
            : base(dataService)
        {   
            //TODO: Need to 'clean' the name according to Azure Search rules
            _indexName = indexName.ToLowerInvariant();
            _searchServiceName = searchServiceName;
            _apiKey = apiKey;
            Analyzer = analyzer;
            IndexerData = indexerData;
            IndexerData = indexerData;

            _client = new Lazy<ISearchServiceClient>(CreateSearchServiceClient);
        }
        
        /// <summary>
        /// The name of the analyzer to use by default for fields
        /// </summary>
        public string Analyzer { get; private set; }
        
        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);

            if (!this.ConfigureIndexSet(name, config, out var indexerData, out var indexSetName))
                throw new ArgumentNullException("indexSet on LuceneExamineIndexer provider has not been set in configuration and/or the IndexerData property has not been explicitly set");

            IndexerData = indexerData;
            //TODO: Need to 'clean' the name according to Azure Search rules
            _indexName = indexSetName.ToLowerInvariant();

            if (config["analyzer"] != null)
            {
                Analyzer = config["analyzer"];
            }
            
            var azureSearchConfig = AzureSearchConfig.GetConfig(_indexName);
            _searchServiceName = azureSearchConfig.SearchServiceName;
            _apiKey = azureSearchConfig.ApiKey;
        }

        private ISearchServiceClient CreateSearchServiceClient()
        {
            var serviceClient = new SearchServiceClient(_searchServiceName, new SearchCredentials(_apiKey));
            return serviceClient;
        }
        
        protected override void DeleteItem(string id, Action<KeyValuePair<string, string>> onComplete)
        {
            var indexer = GetIndexer();

            //TODO: Check exception: https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk
            var result = indexer.Documents.Index(IndexBatch.Delete(PrefixSpecialFieldName(IndexNodeIdFieldName), new[] { id }));

            onComplete(new KeyValuePair<string, string>(IndexNodeIdFieldName, id));
        }

        public override bool IndexExists()
        {
            return _exists ?? (_exists = _client.Value.Indexes.Exists(_indexName)).Value;
        }
        
        private ISearchIndexClient GetIndexer()
        {
            return _indexer ?? (_indexer = _client.Value.Indexes.GetClient(_indexName));
        }
        
        protected override void EnsureIndex(bool forceOverwrite)
        {
            if (!forceOverwrite && _exists.HasValue && _exists.Value) return;

            var indexExists = IndexExists();
            if (indexExists && !forceOverwrite) return;

            if (indexExists)
            {
                _client.Value.Indexes.Delete(_indexName);
            }

            CreateIndex();
        }

        protected override void IndexItem(string id, string type, IDictionary<string, string> values, Action onComplete)
        {
            //TODO: Run this on a background thread

            var indexer = GetIndexer();

            var doc = new Document();
            foreach (var r in values)
            {
                doc[r.Key] = r.Value;
            }

            //TODO: Check exception: https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk
            //TODO: move this to a method which includes an event
            var result = indexer.Documents.Index(IndexBatch.Upload(new[] { doc }));

            onComplete();
        }

        protected override void IndexItems(string type, IEnumerable<IndexDocument> docs, Action<IEnumerable<IndexedNode>> batchComplete)
        {
            //TODO: Run this on a background thread

            var indexer = GetIndexer();
            DeleteAllDocumentsOfType(indexer, type);

            //batches can only contain 1000 records
            foreach (var rowGroup in docs.InGroupsOf(1000))
            {
                var batch = IndexBatch.Upload(ToAzureSearchDocs(rowGroup));

                try
                {
                    var indexResult = indexer.Documents.Index(batch);
                    //TODO: Do we need to check for errors in any of the results?

                    batchComplete(indexResult.Results.Select(x => new IndexedNode
                    {
                        NodeId = int.Parse(x.Key), //TODO: error check
                        Type = type
                    }));
                }
                catch (IndexBatchException e)
                {
                    //TODO: Check exception: https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk and retry

                    // Sometimes when your Search service is under load, indexing will fail for some of the documents in
                    // the batch. Depending on your application, you can take compensating actions like delaying and
                    // retrying. For this simple demo, we just log the failed document keys and continue.
                    
                    //TODO: Output to abstract ILogger
                    Console.WriteLine(
                        "Failed to index some of the documents: {0}",
                        string.Join(", ", e.IndexingResults.Where(r => !r.Succeeded).Select(r => r.Key)));
                }
            }
        }

        private static void DeleteAllDocumentsOfType(ISearchIndexClient indexer, string type)
        {
            // Query all
            var searchResult = indexer.Documents.Search<Document>($"{PrefixSpecialFieldName(IndexTypeFieldName)}:{type}");

            if (searchResult.Results.Count == 0)
                return;

            var toDelete =
                searchResult
                    .Results
                    .Select(r => r.Document["id"].ToString());

            // Delete all
            try
            {
                var batch = IndexBatch.Delete(PrefixSpecialFieldName(IndexNodeIdFieldName), toDelete);
                var result = indexer.Documents.Index(batch);
            }
            catch (IndexBatchException ex)
            {
                //TODO: Check exception: https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk and retry

                //TODO: Output to abstract ILogger
                Console.WriteLine($"Failed to delete documents: {string.Join(", ", ex.IndexingResults.Where(r => !r.Succeeded).Select(r => r.Key))}");
                throw;
            }
        }

        private static IEnumerable<Document> ToAzureSearchDocs(IEnumerable<IndexDocument> docs)
        {
            foreach (var d in docs)
            {
                var ad = new Document();
                foreach (var i in d.RowData)
                {
                    if (i.Key.StartsWith(SpecialFieldPrefix))
                    {
                        ad[PrefixSpecialFieldName(i.Key)] = i.Value;
                    }
                    else
                    {
                        ad[i.Key] = i.Value;
                    }
                }
                yield return ad;
            }
        }

        private void CreateIndex()
        {
            var fields = CombinedIndexerDataFields.SelectMany(x =>
            {
                return x.Value.Select(f =>
                {
                    var dataType = FromExamineType(f.Type);
                    return new Field(x.Key, dataType)
                    {
                        IsSearchable = dataType == DataType.String,
                        IsSortable = f.EnableSorting,
                        Analyzer = FromLuceneAnalyzer(Analyzer)
                    };
                });
            }).ToList();

            //id must be string
            fields.Add(new Field(PrefixSpecialFieldName(IndexNodeIdFieldName), DataType.String)
            {
                IsKey = true,
                IsSortable = true,
                Analyzer = AnalyzerName.Whitespace
            });

            fields.Add(new Field(PrefixSpecialFieldName(IndexTypeFieldName), DataType.String)
            {
                IsSearchable = true,
                Analyzer = AnalyzerName.Whitespace
            });

            //TODO: We should have a custom event for devs to modify the AzureSearch data directly here

            var index = _client.Value.Indexes.Create(new Index(_indexName, fields));
        }

        private static AnalyzerName FromLuceneAnalyzer(string analyzer)
        {
            if (!analyzer.Contains(",")) 
                return AnalyzerName.Create(analyzer);
            
            //if it contains a comma, we'll assume it's an assembly typed name

            if (analyzer.Contains("StandardAnalyzer"))
                return AnalyzerName.StandardLucene;
            if (analyzer.Contains("WhitespaceAnalyzer"))
                return AnalyzerName.Whitespace;
            if (analyzer.Contains("SimpleAnalyzer"))
                return AnalyzerName.Simple;
            if (analyzer.Contains("KeywordAnalyzer"))
                return AnalyzerName.Keyword;
            if (analyzer.Contains("StopAnalyzer"))
                return AnalyzerName.Stop;

            if (analyzer.Contains("ArabicAnalyzer"))
                return AnalyzerName.ArLucene;
            if (analyzer.Contains("BrazilianAnalyzer"))
                return AnalyzerName.PtBRLucene;
            if (analyzer.Contains("ChineseAnalyzer"))
                return AnalyzerName.ZhHansLucene;
            //if (analyzer.Contains("CJKAnalyzer")) //TODO: Not sure where this maps
            //    return AnalyzerName.ZhHansLucene;
            if (analyzer.Contains("CzechAnalyzer"))
                return AnalyzerName.CsLucene;
            if (analyzer.Contains("DutchAnalyzer"))
                return AnalyzerName.NlLucene;
            if (analyzer.Contains("FrenchAnalyzer"))
                return AnalyzerName.FrLucene;
            if (analyzer.Contains("GermanAnalyzer"))
                return AnalyzerName.DeLucene;
            if (analyzer.Contains("RussianAnalyzer"))
                return AnalyzerName.RuLucene;

            //if the above fails, return standard
            return AnalyzerName.StandardLucene;

        }

        private static string PrefixSpecialFieldName(string fieldName)
        {
            //azure search requires that it starts with a letter
            return $"z{fieldName}";
        }

        private static DataType FromExamineType(string type)
        {
            switch (type)
            {
                case DataTypes.Date:
                    return DataType.DateTimeOffset;
                case DataTypes.Double:
                case DataTypes.Float:
                    return DataType.Double;
                case DataTypes.Long:
                    return DataType.Int64;
                case DataTypes.Int:
                case DataTypes.Number:
                    return DataType.Int32;
                case DataTypes.DateDay:
                case DataTypes.DateHour:
                case DataTypes.DateMinute:
                case DataTypes.DateMonth:
                case DataTypes.DateYear:
                    throw new NotSupportedException($"Azure search doesn't support the data type {type}, use DateTime instead");
                default:
                    return DataType.String;
            }
        }

        public void Dispose()
        {
            _indexer?.Dispose();
            if (_client.IsValueCreated)
                _client.Value.Dispose();
        }
    }
}
