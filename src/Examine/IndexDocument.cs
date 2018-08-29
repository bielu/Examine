using System;
using System.Collections.Generic;

namespace Examine
{
    /// <summary>
    /// Represents a document to index
    /// </summary>
    public class IndexDocument : IEquatable<IndexDocument>
    {
        public IndexDocument(int nodeId, string type, IDictionary<string, string> rowData)
        {
            NodeId = nodeId;
            Type = type;
            RowData = rowData;
        }

        public int NodeId { get; }
        public string Type { get; }

        /// <summary>
        /// The data contained in the rows for the item
        /// </summary>
        public IDictionary<string, string> RowData { get; set; }

        public bool Equals(IndexDocument other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return NodeId == other.NodeId;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((IndexDocument) obj);
        }

        public override int GetHashCode()
        {
            return NodeId;
        }

        public static bool operator ==(IndexDocument left, IndexDocument right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(IndexDocument left, IndexDocument right)
        {
            return !Equals(left, right);
        }
    }
}