using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using Lucene.Net.Analysis;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Index;

namespace Examine.LuceneEngine
{
    /// <summary>
    /// Extension methods for Lucene
    /// </summary>
    public static class LuceneExtensions
    {
        /// <summary>
        /// Return the number of indexed documents in Lucene
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        [SecuritySafeCritical]
        public static int GetIndexDocumentCount(this IndexWriter writer)
        {
            try
            {
                using (var reader = writer.GetReader())
                {
                    return reader.NumDocs();
                }
            }
            catch (AlreadyClosedException)
            {
                return 0;
            }
        }

        /// <summary>
        /// Return the total number of fields in the index
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        [SecuritySafeCritical]
        public static int GetIndexFieldCount(this IndexWriter writer)
        {
            //TODO: check for closing! and AlreadyClosedException

            try
            {
                using (var reader = writer.GetReader())
                {
                    return reader.GetFieldNames(IndexReader.FieldOption.ALL).Count;
                }
            }
            catch (AlreadyClosedException)
            {
                return 0;
            }
        }

        [SecuritySafeCritical]
        public static bool IsReadable(this IndexWriter writer, Func<Directory> getLuceneDirectory, out Exception ex)
        {
            if (writer != null)
            {
                try
                {
                    using (writer.GetReader())
                    {
                        ex = null;
                        return true;
                    }
                }
                catch (Exception e)
                {
                    ex = e;
                    return false;
                }
            }

            try
            {
                using (IndexReader.Open(getLuceneDirectory(), true))
                {
                }
                ex = null;
                return true;
            }
            catch (Exception e)
            {
                ex = e;
                return false;
            }
        }

        /// <summary>
        /// Used internally to create a brand new index, this is not thread safe
        /// </summary>
        [SecuritySafeCritical]
        public static void CreateNewIndex(this Directory dir, Analyzer analyzer)
        {
            IndexWriter writer = null;
            try
            {
                if (IndexWriter.IsLocked(dir))
                {
                    //unlock it!
                    IndexWriter.Unlock(dir);
                }
                //create the writer (this will overwrite old index files)
                writer = new IndexWriter(dir, analyzer, true, IndexWriter.MaxFieldLength.UNLIMITED);
            }            
            finally
            {
                writer?.Close();
            }
        }

        /// <summary>
        /// Copies from IndexInput to IndexOutput
        /// </summary>
        /// <param name="indexInput"></param>
        /// <param name="indexOutput"></param>
        /// <param name="name"></param>
        /// <remarks>
        /// From Another interesting project I found: 
        /// http://www.compass-project.org/
        /// which has some interesting bits like:
        /// https://github.com/kimchy/compass/blob/master/src/main/src/org/apache/lucene/index/LuceneUtils.java
        /// 
        /// </remarks>
        [SecuritySafeCritical]
        internal static void CopyTo(this IndexInput indexInput, IndexOutput indexOutput, string name)
        {
            var buffer = new byte[32768];

            long length = indexInput.Length();
            long remainder = length;
            int chunk = buffer.Length;

            while (remainder > 0)
            {
                int len = (int)Math.Min(chunk, remainder);
                indexInput.ReadBytes(buffer, 0, len);
                indexOutput.WriteBytes(buffer, len);
                remainder -= len;
            }

            // Verify that remainder is 0
            if (remainder != 0)
                throw new InvalidOperationException(
                        "Non-zero remainder length after copying [" + remainder
                                + "] (id [" + name + "] length [" + length
                                + "] buffer size [" + chunk + "])");
        }

        [SecuritySafeCritical]
        public static ReaderStatus GetReaderStatus(this IndexSearcher searcher)
        {
            return searcher.GetIndexReader().GetReaderStatus();
        }        

		[SecuritySafeCritical]
        public static ReaderStatus GetReaderStatus(this IndexReader reader)
        {
            ReaderStatus status = ReaderStatus.NotCurrent;
            try
            {
                status = reader.IsCurrent() ? ReaderStatus.Current : ReaderStatus.NotCurrent;
            }
            catch (AlreadyClosedException)
            {
                status = ReaderStatus.Closed;
            }
            return status;
        }

    }
}
