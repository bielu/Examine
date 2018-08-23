using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Examine.LuceneEngine;
using Examine.LuceneEngine.Providers;
using Examine.Providers;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;

namespace Examine.AzureSearch
{
    public class AzureSearchIndexer : BaseIndexProvider, IDisposable
    {
        private readonly string _name;
        private bool? _exists;
        private ISearchIndexClient _indexer;
        private readonly ISearchServiceClient _client = CreateSearchServiceClient();

        public AzureSearchIndexer(string name, ISimpleDataService dataService, IIndexCriteria indexerData) : base(indexerData)
        {
            //TODO: Need to 'clean' the name according to Azure Search rules
            _name = name;
            DataService = dataService;
        }

        /// <summary>
        /// The data service used to retrieve all of the data for an index type
        /// </summary>
        public ISimpleDataService DataService { get; private set; }

        private static ISearchServiceClient CreateSearchServiceClient()
        {
            //TODO: refactor this

            var searchServiceName = "examine-test";
            var adminApiKey = "F72FFB987CF9FEAF57EC007B7A2A592D";

            var serviceClient = new SearchServiceClient(searchServiceName, new SearchCredentials(adminApiKey));
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
            return _exists ?? (_exists = _client.Indexes.Exists(_name)).Value;
        }

        private ISearchIndexClient GetIndexer()
        {
            return _indexer ?? (_indexer = _client.Indexes.GetClient(_name));
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
                _client.Indexes.Delete(_name);
            }

            CreateIndex();
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

            var index = _client.Indexes.Create(new Index(_name, fields));
        }

        private DataType FromExamineType(string type)
        {
            //TODO: Fill this in
            return DataType.String;
        }

        public void Dispose()
        {
            _indexer?.Dispose();
            _client?.Dispose();
        }
    }
}
