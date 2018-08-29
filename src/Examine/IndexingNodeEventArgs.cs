using System.ComponentModel;
using System.Collections.Generic;

namespace Examine
{
    public class IndexingNodeEventArgs : CancelEventArgs, INodeEventArgs
    {
        public IndexingNodeEventArgs(int nodeId, Dictionary<string, string> fields, string indexType)
        {
            NodeId = nodeId;
            Fields = fields;
            IndexType = indexType;
        }

        public int NodeId { get; }
        public Dictionary<string, string> Fields { get; }
        public string IndexType { get; }
    }
}