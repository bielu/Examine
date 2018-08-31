using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Xml.Linq;

namespace Examine.Providers
{
    /// <summary>
    /// A base index provider encompassing all standard logic and ensuring that the correct events are raised
    /// </summary>
    public abstract class IndexProvider : BaseIndexProvider
    {
        public IndexProvider()
        {
        }

        public IndexProvider(IIndexDataService dataService)
        {
            DataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        }

        public string[] IndexTypes { get; private set; }
        public virtual IIndexDataService DataService { get; private set; }

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(name, config);

            if (config["indexTypes"] != null && !string.IsNullOrEmpty(config["indexTypes"]))
            {
                IndexTypes = config["indexTypes"].Split(',');
            }

            if (config["dataService"] != null && !string.IsNullOrEmpty(config["dataService"]))
            {
                //this should be a fully qualified type
                var serviceType = TypeHelper.FindType(config["dataService"]);
                DataService = (IIndexDataService)Activator.CreateInstance(serviceType);
            }
        }

        public override void ReIndexNode(XElement node, string type)
        {
            var nodeId = (int) node.Attribute("id");

            if (IndexTypes != null && !IndexTypes.Contains(type))
            {
                OnIgnoringNode(new IndexingNodeDataEventArgs(node, (int)node.Attribute("id"), type));
                return;
            }

            if (!ValidateDocument(node))
            {
                var ignoreArgs = new IndexingNodeDataEventArgs(node, (int) node.Attribute("id"), type);
                OnIgnoringNode(ignoreArgs);
                return;
            }   

            var values = GetDataToIndex(node, type, null);

            var args = new IndexingNodeEventArgs(nodeId, values, type);
            OnNodeIndexing(args);
            if (args.Cancel)
                return;

            IndexItem(nodeId.ToString(), type, values, () =>
            {
                OnNodeIndexed(new IndexedNodeEventArgs(nodeId));
            });
        }

        public sealed override void IndexAll(string type)
        {
            if (IndexTypes != null && !IndexTypes.Contains(type))
            {
                return;
            }

            var data = DataService?.GetAllData(this, type)
                .Select(x =>
                {
                    x.RowData = GetDataToIndex(x, type);
                    return x;
                });

            if (data == null) return;

            var indexingArgs = new IndexingNodesEventArgs(IndexerData, type);
            OnNodesIndexing(indexingArgs);
            if (indexingArgs.Cancel) return;

            IndexItems(type, data, (indexedData) =>
            {
                OnNodesIndexed(new IndexedNodesEventArgs(IndexerData, indexedData));
            });
        }

        public sealed override void DeleteFromIndex(string nodeId)
        {
            DeleteItem(nodeId, pair => { OnIndexDeleted(new DeleteIndexEventArgs(pair)); });
        }

        public override void RebuildIndex()
        {
            if (IndexTypes == null)
                return;

            EnsureIndex(true);

            foreach (var t in IndexTypes)
            {
                IndexAll(t);
            }
        }

        protected abstract void DeleteItem(string id, Action<KeyValuePair<string, string>> onComplete);
        protected abstract void EnsureIndex(bool forceOverwrite);
        protected abstract void IndexItem(string id, string type, IDictionary<string, string> values, Action onComplete);
        protected abstract void IndexItems(string type, IEnumerable<IndexDocument> docs, Action<IEnumerable<IndexedNode>> batchComplete);
    }
}