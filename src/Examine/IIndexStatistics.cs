namespace Examine
{
    public interface IIndexStatistics
    {
        int GetDocumentCount();
        int GetFieldCount();
    }
}