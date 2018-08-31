using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Provider;
using System.Linq;
using System.Security;
using Examine;
using System.Xml.Linq;
using Examine.LuceneEngine;

namespace Examine.Providers
{
    /// <summary>
    /// Base class for an Examine Index Provider. You must implement this class to create an IndexProvider
    /// </summary>
    public abstract class BaseIndexProvider : ProviderBase, IIndexer
    {
        /// <summary>
        /// Used to store a non-tokenized key for the document
        /// </summary>
        public const string IndexTypeFieldName = "__IndexType";

        /// <summary>
        /// Used to store a non-tokenized type for the document
        /// </summary>
        public const string IndexNodeIdFieldName = "__NodeId";

        /// <summary>
        /// The prefix characters denoting a special field stored in the lucene index for use internally
        /// </summary>
        public const string SpecialFieldPrefix = "__";

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseIndexProvider"/> class.
        /// </summary>
        protected BaseIndexProvider() { }
        /// <summary>
        /// Initializes a new instance of the <see cref="BaseIndexProvider"/> class.
        /// </summary>
        /// <param name="indexerData">The indexer data.</param>
        protected BaseIndexProvider(IIndexCriteria indexerData)
        {
            IndexerData = indexerData;
        }

        /// <summary>
        /// Initializes the provider.
        /// </summary>
        /// <param name="name">The friendly name of the provider.</param>
        /// <param name="config">A collection of the name/value pairs representing the provider-specific attributes specified in the configuration for this provider.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// The name of the provider is null.
        /// </exception>
        /// <exception cref="T:System.ArgumentException">
        /// The name of the provider has a length of zero.
        /// </exception>
        /// <exception cref="T:System.InvalidOperationException">
        /// An attempt is made to call <see cref="M:System.Configuration.Provider.ProviderBase.Initialize(System.String,System.Collections.Specialized.NameValueCollection)"/> on a provider after the provider has already been initialized.
        /// </exception>
        [SecuritySafeCritical]
        public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
        {
            base.Initialize(name, config);
            
        }

        
        #region IIndexer members
        
        
        /// <summary>
        /// Forces a particular XML node to be reindexed
        /// </summary>
        /// <param name="node">XML node to reindex</param>
        /// <param name="type">Type of index to use</param>
        public abstract void ReIndexNode(XElement node, string type);

        /// <summary>
        /// Deletes a node from the index
        /// </summary>
        /// <param name="nodeId">Node to delete</param>
        public abstract void DeleteFromIndex(string nodeId);

        /// <summary>
        /// Re-indexes all data for the index type specified
        /// </summary>
        /// <param name="type"></param>
        public abstract void IndexAll(string type);

        /// <summary>
        /// Rebuilds the entire index from scratch for all index types
        /// </summary>
        public abstract void RebuildIndex();

        /// <summary>
        /// Gets/sets the index criteria to create the index with
        /// </summary>
        public IIndexCriteria IndexerData
        {
            get => _indexerData;
            set
            {
                _indexerData = value;
                //reset the combined data 
                _combinedIndexerDataFields = null;
            }
        }

        private CombinedIndexerDataFields _combinedIndexerDataFields;
        private IIndexCriteria _indexerData;

        protected CombinedIndexerDataFields CombinedIndexerDataFields => _combinedIndexerDataFields ?? (_combinedIndexerDataFields = new CombinedIndexerDataFields(IndexerData.UserFields.Concat(IndexerData.StandardFields.ToList())));

        /// <summary>
        /// Check if the index exists
        /// </summary>
        /// <returns></returns>
        public abstract bool IndexExists();

        #endregion

        #region Events
        /// <summary>
        /// Occurs for an Indexing Error
        /// </summary>
        public event EventHandler<IndexingErrorEventArgs> IndexingError;

        /// <summary>
        /// Occurs when a node is in its Indexing phase
        /// </summary>
        public event EventHandler<IndexingNodeEventArgs> NodeIndexing;
        /// <summary>
        /// Occurs when a node is in its Indexed phase
        /// </summary>
        public event EventHandler<IndexedNodeEventArgs> NodeIndexed;
        /// <summary>
        /// Occurs when a collection of nodes are in their Indexing phase (before a single node is processed)
        /// </summary>
        public event EventHandler<IndexingNodesEventArgs> NodesIndexing;
        /// <summary>
        /// Occurs when the collection of nodes have been indexed
        /// </summary>
        public event EventHandler<IndexedNodesEventArgs> NodesIndexed;

        /// <summary>
        /// Occurs when the indexer is gathering the fields and their associated data for the index
        /// </summary>
        public event EventHandler<IndexingNodeDataEventArgs> GatheringNodeData;
        /// <summary>
        /// Occurs when a node is deleted from the index
        /// </summary>
        public event EventHandler<DeleteIndexEventArgs> IndexDeleted;
        /// <summary>
        /// Occurs when a particular field is having its data obtained
        /// </summary>
        public event EventHandler<IndexingFieldDataEventArgs> GatheringFieldData;
        /// <summary>
        /// Occurs when node is found but outside the supported node set
        /// </summary>
        public event EventHandler<IndexingNodeDataEventArgs> IgnoringNode;
        #endregion

        #region Protected Event callers

        /// <summary>
        /// Called when a node is ignored by the ValidateDocument method.
        /// </summary>
        /// <param name="e"></param>
        protected virtual void OnIgnoringNode(IndexingNodeDataEventArgs e)
        {
            if (IgnoringNode != null)
                IgnoringNode(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:IndexingError"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Examine.IndexingErrorEventArgs"/> instance containing the event data.</param>
        protected virtual void OnIndexingError(IndexingErrorEventArgs e)
        {
            if (IndexingError != null)
                IndexingError(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:NodeIndexed"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Examine.IndexedNodeEventArgs"/> instance containing the event data.</param>
        protected virtual void OnNodeIndexed(IndexedNodeEventArgs e)
        {
            if (NodeIndexed != null)
                NodeIndexed(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:NodeIndexing"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Examine.IndexingNodeEventArgs"/> instance containing the event data.</param>
        protected virtual void OnNodeIndexing(IndexingNodeEventArgs e)
        {
            if (NodeIndexing != null)
                NodeIndexing(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:IndexDeleted"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Examine.DeleteIndexEventArgs"/> instance containing the event data.</param>
        protected virtual void OnIndexDeleted(DeleteIndexEventArgs e)
        {
            if (IndexDeleted != null)
                IndexDeleted(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:GatheringNodeData"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Examine.IndexingNodeDataEventArgs"/> instance containing the event data.</param>
        protected virtual void OnGatheringNodeData(IndexingNodeDataEventArgs e)
        {
            if (GatheringNodeData != null)
                GatheringNodeData(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:GatheringFieldData"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Examine.IndexingFieldDataEventArgs"/> instance containing the event data.</param>
        [Obsolete("Generally not used, will be removed in future versions, use GatheringNodeData instead")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        protected virtual void OnGatheringFieldData(IndexingFieldDataEventArgs e)
        {
            if (GatheringFieldData != null)
                GatheringFieldData(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:NodesIndexed"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Examine.IndexedNodesEventArgs"/> instance containing the event data.</param>
        protected virtual void OnNodesIndexed(IndexedNodesEventArgs e)
        {
            if (NodesIndexed != null)
                NodesIndexed(this, e);
        }

        /// <summary>
        /// Raises the <see cref="E:NodesIndexing"/> event.
        /// </summary>
        /// <param name="e">The <see cref="Examine.IndexingNodesEventArgs"/> instance containing the event data.</param>
        protected virtual void OnNodesIndexing(IndexingNodesEventArgs e)
        {
            if (NodesIndexing != null)
                NodesIndexing(this, e);
        }

        #endregion

        /// <summary>
        /// Ensures that the node being indexed is of a correct type and is a descendent of the parent id specified.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        protected virtual bool ValidateDocument(XElement node)
        {
            //check if this document is of a correct type of node type alias
            if (IndexerData.IncludeNodeTypes.Any())
                if (!IndexerData.IncludeNodeTypes.Contains(node.ExamineNodeTypeAlias()))
                    return false;

            //if this node type is part of our exclusion list, do not validate
            if (IndexerData.ExcludeNodeTypes.Any())
                if (IndexerData.ExcludeNodeTypes.Contains(node.ExamineNodeTypeAlias()))
                    return false;

            return true;
        }

        /// <summary>
        /// Translates the XElement structure into a dictionary object to be indexed.
        /// </summary>
        /// <remarks>
        /// This is used when re-indexing an individual node since this is the way the provider model works.
        /// For this provider, it will use a very similar XML structure as umbraco 4.0.x:
        /// </remarks>
        /// <example>
        /// <code>
        /// <![CDATA[
        /// <root>
        ///     <node id="1234" nodeTypeAlias="yourIndexType">
        ///         <data alias="fieldName1">Some data</data>
        ///         <data alias="fieldName2">Some other data</data>
        ///     </node>
        ///     <node id="345" nodeTypeAlias="anotherIndexType">
        ///         <data alias="fieldName3">More data</data>
        ///     </node>
        /// </root>
        /// ]]>
        /// </code>        
        /// </example>
        /// <param name="node"></param>
        /// <param name="type"></param>
        /// <param name="onDuplicate"></param>
        /// <returns></returns>
        protected virtual Dictionary<string, string> GetDataToIndex(XElement node, string type, Action<int, string> onDuplicate)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!node.IsExamineElement())
                return values;

            //resolve all attributes now it is much faster to do this than to relookup all of the XML data
            //using Linq and the node.Attributes() methods re-gets all of them.
            var attributeValues = node.Attributes().ToDictionary(x => x.Name.LocalName, x => x.Value);

            var nodeId = int.Parse(attributeValues["id"]);

            // Add umbraco node properties 
            foreach (var field in IndexerData.StandardFields)
            {
                string val = node.SelectExaminePropertyValue(attributeValues, field.Name);
                if (val == null) continue;

                var args = new IndexingFieldDataEventArgs(node, field.Name, val, true, nodeId);
                OnGatheringFieldData(args);
                val = args.FieldValue;

                //don't add if the value is empty/null                
                if (!string.IsNullOrEmpty(val))
                {
                    if (values.ContainsKey(field.Name))
                    {
                        onDuplicate?.Invoke(nodeId, field.Name);
                    }
                    else
                    {
                        values.Add(field.Name, val);
                    }
                }

            }

            //resolve all element data now it is much faster to do this than to relookup all of the XML data
            //using Linq and the node.Elements() methods re-gets all of them.
            var elementValues = node.SelectExamineDataValues();

            // Get all user data that we want to index and store into a dictionary 
            foreach (var field in IndexerData.UserFields)
            {
                // Get the value of the data       
                if (!elementValues.TryGetValue(field.Name, out var value))
                    continue;

                //raise the event and assign the value to the returned data from the event
                var indexingFieldDataArgs = new IndexingFieldDataEventArgs(node, field.Name, value, false, nodeId);
                OnGatheringFieldData(indexingFieldDataArgs);
                value = indexingFieldDataArgs.FieldValue;

                //don't add if the value is empty/null
                if (string.IsNullOrEmpty(value)) continue;

                if (values.ContainsKey(field.Name))
                {
                    onDuplicate?.Invoke(nodeId, field.Name);
                }
                else
                {
                    values.Add(field.Name, value);
                }
            }

            //raise the event and assign the value to the returned data from the event
            var indexingNodeDataArgs = new IndexingNodeDataEventArgs(node, nodeId, values, type);
            OnGatheringNodeData(indexingNodeDataArgs);
            values = indexingNodeDataArgs.Fields;

            //ensure the special fields are added to the dictionary
            if (!values.ContainsKey(IndexNodeIdFieldName))
                values.Add(IndexNodeIdFieldName, attributeValues["id"]);
            if (!values.ContainsKey(IndexTypeFieldName))
                values.Add(IndexTypeFieldName, type);

            return values;
        }

        protected virtual Dictionary<string, string> GetDataToIndex(IndexDocument doc, string type)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var nodeId = doc.NodeId;

            foreach (var field in IndexerData.StandardFields)
            {
                if (!doc.RowData.TryGetValue(field.Name, out var val)) continue;
                if (string.IsNullOrEmpty(val)) continue;
                values.Add(field.Name, val);
            }
            
            foreach (var field in IndexerData.UserFields)
            {
                if (!doc.RowData.TryGetValue(field.Name, out var val)) continue;
                if (string.IsNullOrEmpty(val)) continue;
                values.Add(field.Name, val);
            }

            //raise the event and assign the value to the returned data from the event
            var indexingNodeDataArgs = new IndexingNodeDataEventArgs(nodeId, values, type);
            OnGatheringNodeData(indexingNodeDataArgs);
            values = indexingNodeDataArgs.Fields;

            //ensure the special fields are added to the dictionary
            if (!values.ContainsKey(IndexNodeIdFieldName))
                values.Add(IndexNodeIdFieldName, doc.NodeId.ToString());
            if (!values.ContainsKey(IndexTypeFieldName))
                values.Add(IndexTypeFieldName, type);

            return values;
        }

    }
}
