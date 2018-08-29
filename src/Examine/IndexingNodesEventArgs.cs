using System;
using System.ComponentModel;


namespace Examine
{
    public class IndexingNodesEventArgs : CancelEventArgs
    {
        public IndexingNodesEventArgs(IIndexCriteria indexData, string type)
        {
            this.IndexData = indexData;
            this.Type = type;
        }

        [Obsolete("This should not be used")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public IndexingNodesEventArgs(IIndexCriteria indexData, string xPath, string type)
        {
            this.IndexData = indexData;
            this.XPath = xPath;
            this.Type = type;
        }

        public IIndexCriteria IndexData { get; }

        [Obsolete("This should not be used")]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string XPath { get; private set; }

        public string Type {get; }

    }
}