﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Lucene.Net.Store;

namespace Examine.RemoteDirectory
{
    public class RemoteReadOnlyLuceneSyncDirectory : RemoteSyncDirectory
    {
        private readonly string _cacheDirectoryPath;
        private readonly string _cacheDirectoryName;
        private string OldIndexFolderName;

        public RemoteReadOnlyLuceneSyncDirectory(
            string cacheDirectoryPath,
            string cacheDirectoryName,
            IRemoteDirectory azurelper,
            bool compressBlobs = false) : base(azurelper, null, compressBlobs)
        {
            _cacheDirectoryPath = cacheDirectoryPath;
            _cacheDirectoryName = cacheDirectoryName;
            IsReadOnly = true;
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

                RemoteDirectory.SyncFile(CacheDirectory, file, CompressBlobs);
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
                var lockprefix = LockFactory.LockPrefix;
                foreach (string file in GetAllBlobFiles())
                {
                    //   newIndex.TouchFile(file);
                    if (file.EndsWith(".lock"))
                    {
                        continue;
                    }

                    RemoteDirectory.SyncFile(newIndex, file, CompressBlobs);
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
    }
}