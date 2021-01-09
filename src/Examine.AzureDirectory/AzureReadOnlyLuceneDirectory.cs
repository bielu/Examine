﻿using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Azure.Storage.Blobs;
using Examine.LuceneEngine;
using Examine.LuceneEngine.Directories;
using Lucene.Net.Store;
using Directory = Lucene.Net.Store.Directory;

namespace Examine.AzureDirectory
{
    public class AzureReadOnlyLuceneDirectory : AzureLuceneDirectory
    {
        private readonly string _cacheDirectoryPath;
        private readonly string _cacheDirectoryName;
        private string OldIndexFolderName;

        public AzureReadOnlyLuceneDirectory(
            string storageAccount,
            string containerName,
            string cacheDirectoryPath,
            string cacheDirectoryName,
            bool compressBlobs = false,
            string rootFolder = null) : base(storageAccount, containerName, null, compressBlobs, rootFolder, true)
        {
            _cacheDirectoryPath = cacheDirectoryPath;
            _cacheDirectoryName = cacheDirectoryName;
            if (CacheDirectory == null)
            {
                Trace.WriteLine("INFO CacheDirectory null. Creating or rebuilding cache");
                CreateOrReadCache();
            }
            else
            {
                CheckDirty();
            }
        }

        protected override void GuardCacheDirectory(Lucene.Net.Store.Directory cacheDirectory)
        {
            //Do nothing
        }
        private void CreateOrReadCache()
        {
            var indexParentFolder = new DirectoryInfo(
                Path.Combine(_cacheDirectoryPath,
                    _cacheDirectoryName));
            if (indexParentFolder.Exists)
            {
                var subDirectories = indexParentFolder.GetDirectories();
                if (subDirectories.Any())
                {
                    var directory = subDirectories.FirstOrDefault();
                    OldIndexFolderName = directory.Name;
                    CacheDirectory = new SimpleFSDirectory(directory);
                    _lockFactory = CacheDirectory.LockFactory;
                }
                else
                {
                    RebuildCache();
                }
            }
            else
            {
                RebuildCache();

            }
        }

        public void ResyncCache()
        {
           
            foreach (string file in GetAllBlobFiles())
            {
                if (CacheDirectory.FileExists(file))
                {
                    CacheDirectory.TouchFile(file);
                }
                var blob = GetBlobClient(RootFolder + file);
                SyncFile(blob, file);
            }
        }
        protected override void HandleOutOfSync()
        {
            ResyncCache();
        }
        private object _rebuildLock = new object();
        public override void RebuildCache()
        {
            lock (_rebuildLock)
            {

                //Needs locking
                Trace.WriteLine("INFO Rebuilding cache");
                var tempDir = new DirectoryInfo(
                    Path.Combine(_cacheDirectoryPath,
                        _cacheDirectoryName, DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffff")));
                if (tempDir.Exists == false)
                    tempDir.Create();
                Lucene.Net.Store.Directory newIndex = new SimpleFSDirectory(tempDir);
                foreach (string file in GetAllBlobFiles())
                {
                    //   newIndex.TouchFile(file);
                    if (file.EndsWith(".lock"))
                    {
                        continue;
                    }
                    var blob = _blobContainer.GetBlobClient(RootFolder + file);
                    SyncFile(newIndex, blob, file);
                }

                var oldIndex = CacheDirectory;
                newIndex.Dispose();
                newIndex = new SimpleFSDirectory(tempDir);

                CacheDirectory = newIndex;
                _lockFactory = newIndex.LockFactory;
                if (oldIndex != null)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(LockFactory.LockPrefix))
                        {
                            oldIndex.ClearLock(LockFactory.LockPrefix + "-write.lock");
                        }
                        else
                        {
                            oldIndex.ClearLock("write.lock");
                        }
                            
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine($"Error: {ex.ToString()}");
                    }
                    try
                    {
                        foreach (var file in oldIndex.ListAll())
                        {
                            if (oldIndex.FileExists(file))
                            {
                                oldIndex.DeleteFile(file);
                            }
                        }
                    }
                    catch (NoSuchDirectoryException ex)
                    {
                        Trace.WriteLine($"Error: Old Local Sync Directory Empty. {ex.ToString()}");
                    }
                    oldIndex.Dispose();
                    DirectoryInfo oldindex = new DirectoryInfo(Path.Combine(_cacheDirectoryPath,
                        _cacheDirectoryName, OldIndexFolderName));
                    oldindex.Delete();
                }
                OldIndexFolderName = tempDir.Name;
            }
        }
       
        protected void SyncFile(Lucene.Net.Store.Directory newIndex, BlobClient blob, string fileName)
        {
            Trace.WriteLine($"INFO Syncing file {fileName} for {RootFolder}");
            // then we will get it fresh into local deflatedName 
            // StreamOutput deflatedStream = new StreamOutput(CacheDirectory.CreateOutput(deflatedName));
            using (var deflatedStream = new MemoryStream())
            {
                // get the deflated blob
                blob.DownloadTo(deflatedStream);

#if FULLDEBUG
                Trace.WriteLine($"GET {fileName} RETREIVED {deflatedStream.Length} bytes");
#endif

                // seek back to begininng
                deflatedStream.Seek(0, SeekOrigin.Begin);

                if (ShouldCompressFile(fileName))
                {
                    // open output file for uncompressed contents
                    using (var fileStream = new StreamOutput(newIndex.CreateOutput(fileName)))
                    using (var decompressor = new DeflateStream(deflatedStream, CompressionMode.Decompress))
                    {
                        var bytes = new byte[65535];
                        var nRead = 0;
                        do
                        {
                            nRead = decompressor.Read(bytes, 0, 65535);
                            if (nRead > 0)
                                fileStream.Write(bytes, 0, nRead);
                        } while (nRead == 65535);
                    }
                }
                else
                {
                    using (var fileStream = new StreamOutput(newIndex.CreateOutput(fileName)))
                    {
                        // get the blob
                        blob.DownloadTo(fileStream);

                        fileStream.Flush();
#if FULLDEBUG
                        Trace.WriteLine($"GET {fileName} RETREIVED {fileStream.Length} bytes");
#endif
                    }
                }
            }
        }

        public override Lock MakeLock(string name)
        {
            return base.MakeLock(name);
        }

    }
}