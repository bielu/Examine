using System;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Examine
{
    /// <summary>
    /// Simple class to store the definition of an indexed node
    /// </summary>
    public class IndexedNode : IEquatable<IndexedNode>
    {
        //TODO: This should be a struct

        public int NodeId { get; set; }
        public string Type { get; set; }

        public bool Equals(IndexedNode other)
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
            return Equals((IndexedNode) obj);
        }

        public override int GetHashCode()
        {
            return NodeId;
        }

        public static bool operator ==(IndexedNode left, IndexedNode right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(IndexedNode left, IndexedNode right)
        {
            return !Equals(left, right);
        }
    }
}
