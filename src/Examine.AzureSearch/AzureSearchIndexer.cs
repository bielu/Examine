
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
    public class AzureSearchIndexer : BaseIndexProvider, IDisposable
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

        public AzureSearchIndexer(string indexName, string searchServiceName, string apiKey, string indexingAnalyzer, IIndexCriteria indexerData, ISimpleDataService dataService) 
            : this()
        {   
            //TODO: Need to 'clean' the name according to Azure Search rules
            _indexName = indexName.ToLowerInvariant();
            _searchServiceName = searchServiceName;
            _apiKey = apiKey;
            IndexingAnalyzer = indexingAnalyzer;
            IndexerData = indexerData;
            DataService = dataService;
            IndexerData = indexerData;
        }

        //TODO: Use this
        public string[] IndexTypes { get; private set; }

        //TODO: Use this
        public string IndexingAnalyzer { get; private set; }

        /// <summary>
        /// The data service used to retrieve all of the data for an index type
        /// </summary>
        public ISimpleDataService DataService { get; private set; }

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);

            IndexTypes = config["indexTypes"].Split(',');

            if (config["dataService"] != null && !string.IsNullOrEmpty(config["dataService"]))
            {
                //this should be a fully qualified type
                var serviceType = TypeHelper.FindType(config["dataService"]);
                DataService = (ISimpleDataService)Activator.CreateInstance(serviceType);
            }
            else
            {
                throw new ArgumentNullException("The dataService property must be specified for the AzureSearchIndexer provider");
            }

            if (config["indexSet"] == null && IndexerData == null)
            {
                //if we don't have either, then we'll try to set the index set by naming conventions
                var found = false;
                if (name.EndsWith("Indexer"))
                {
                    var setNameByConvension = name.Remove(name.LastIndexOf("Indexer")) + "IndexSet";
                    //check if we can assign the index set by naming convention
                    var set = IndexSets.Instance.Sets.Cast<IndexSet>().SingleOrDefault(x => x.SetName == setNameByConvension);

                    if (set != null)
                    {
                        //we've found an index set by naming conventions :)
                        //TODO: Need to 'clean' the name according to Azure Search rules
                        _indexName = set.SetName.ToLowerInvariant();

                        var indexSet = IndexSets.Instance.Sets[set.SetName];
                        
                        //get the index criteria and ensure folder
                        IndexerData = GetIndexerData(indexSet);

                        found = true;
                    }
                }

                if (!found)
                    throw new ArgumentNullException("indexSet on AzureSearchIndexer provider has not been set in configuration and/or the IndexerData property has not been explicitly set");

            }
            else if (config["indexSet"] != null)
            {
                //if an index set is specified, ensure it exists and initialize the indexer based on the set

                if (IndexSets.Instance.Sets[config["indexSet"]] == null)
                {
                    throw new ArgumentException("The indexSet specified for the AzureSearchIndexer provider does not exist");
                }

                //TODO: Need to 'clean' the name according to Azure Search rules
                _indexName = config["indexSet"].ToLowerInvariant();

                var indexSet = IndexSets.Instance.Sets[_indexName];

                //get the index criteria and ensure folder
                IndexerData = GetIndexerData(indexSet);
            }

            if (config["analyzer"] != null)
            {
                IndexingAnalyzer = config["analyzer"];
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

        public override void ReIndexNode(XElement node, string type)
        {
            var values = node.SelectExamineDataValues();
            var nodeTypeAlias = node.ExamineNodeTypeAlias();
            var id = (int)node.Attribute("id");

            var indexer = GetIndexer();

            var doc = new Document
            {
                ["id"] = id,
                ["nodeTypeAlias"] = nodeTypeAlias
            };
            foreach(var r in values)
            {
                doc[r.Key] = r.Value;
            }

            //TODO: Check exception: https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk
            //TODO: move this to a method which includes an event
            var result = indexer.Documents.Index(IndexBatch.MergeOrUpload(new[] {doc}));
        }

        public override void DeleteFromIndex(string nodeId)
        {
            var indexer = GetIndexer();

            //TODO: Check exception: https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk
            var result = indexer.Documents.Index(IndexBatch.Delete("id", new[] {nodeId}));
        }

        public override void IndexAll(string type)
        {
            //TODO: Support types
            if (type != "content") throw new InvalidOperationException("Invalid type");

            //TODO: First we should delete all data of this type

            var indexer = GetIndexer();
            //batches can only contain 1000 records
            foreach(var rowGroup in GetAllData(type).InGroupsOf(1000))
            {
                //TODO: move this to a method which includes an event
                var batch = IndexBatch.Upload(rowGroup);
                
                try
                {
                    var result = indexer.Documents.Index(batch);
                }
                catch (IndexBatchException e)
                {
                    //TODO: Check exception: https://docs.microsoft.com/en-us/azure/search/search-howto-dotnet-sdk and retry

                    // Sometimes when your Search service is under load, indexing will fail for some of the documents in
                    // the batch. Depending on your application, you can take compensating actions like delaying and
                    // retrying. For this simple demo, we just log the failed document keys and continue.
                    Console.WriteLine(
                        "Failed to index some of the documents: {0}",
                        string.Join(", ", e.IndexingResults.Where(r => !r.Succeeded).Select(r => r.Key)));
                }

            }
            
        }

        public override void RebuildIndex()
        {
            EnsureIndex(true);

            PerformIndexRebuild();
        }

        public override bool IndexExists()
        {
            return _exists ?? (_exists = _client.Value.Indexes.Exists(_indexName)).Value;
        }

        /// <summary>
        /// Returns IIndexCriteria object from the IndexSet
        /// </summary>
        /// <param name="indexSet"></param>
        protected virtual IIndexCriteria GetIndexerData(IndexSet indexSet)
        {
            return new IndexCriteria(
                indexSet.IndexAttributeFields.Cast<IIndexField>().ToArray(),
                indexSet.IndexUserFields.Cast<IIndexField>().ToArray(),
                indexSet.IncludeNodeTypes.ToList().Select(x => x.Name).ToArray(),
                indexSet.ExcludeNodeTypes.ToList().Select(x => x.Name).ToArray(),
                indexSet.IndexParentId);
        }

        private ISearchIndexClient GetIndexer()
        {
            return _indexer ?? (_indexer = _client.Value.Indexes.GetClient(_indexName));
        }

        private IEnumerable<Document> GetAllData(string type)
        {
            foreach (var row in DataService.GetAllData(type))
            {
                var doc = new Document
                {
                    ["id"] = row.NodeDefinition.NodeId.ToString(),
                    ["nodeTypeAlias"] = row.NodeDefinition.Type
                };
                foreach (var r in row.RowData)
                {
                    doc[r.Key] = r.Value;
                }

                yield return doc;
            }
        }

        /// <summary>
        /// Indexes each index type defined in IndexTypes property
        /// </summary>
        private void PerformIndexRebuild()
        {
            //TODO: Support types
            IndexAll("content");
        }

        private void EnsureIndex(bool forceOverwrite)
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

        private void CreateIndex()
        {
            //TODO: Support analyzer

            var fields = CombinedIndexerDataFields.SelectMany(x =>
            {
                return x.Value.Select(f =>
                {
                    var dataType = FromExamineType(f.Type);
                    return new Field(x.Key, dataType)
                    {
                        IsSearchable = dataType == DataType.String,
                        IsSortable = f.EnableSorting
                    };
                });
            }).ToList();

            //id must be string
            fields.Add(new Field("id", DataType.String)
            {
                IsKey = true,
                IsSortable = true
            });

            fields.Add(new Field("nodeTypeAlias", DataType.String)
            {
                IsSearchable = true,
            });

            var index = _client.Value.Indexes.Create(new Index(_indexName, fields));
        }

        private DataType FromExamineType(string type)
        {
            //TODO: Fill this in
            return DataType.String;
        }

        public void Dispose()
        {
            _indexer?.Dispose();
            if (_client.IsValueCreated)
                _client.Value.Dispose();
        }
    }
}
