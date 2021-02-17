using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Examine.Logging;
using Lucene.Net.Store;

namespace Examine.RemoteDirectory
{
    public class RemoteReadOnlyLuceneSyncDirectory : RemoteSyncDirectory
    {
        private readonly string _cacheDirectoryPath;
        private readonly string _cacheDirectoryName;
        private NoDedicatedThreadRebuildQueue _rebuildQueue;
        private string _oldIndexFolderName;

        public RemoteReadOnlyLuceneSyncDirectory(IRemoteDirectory remoteDirectory,
            string cacheDirectoryPath,
            string cacheDirectoryName,
            ILoggingService loggingService,
            bool compressBlobs = false) : base(remoteDirectory,loggingService, compressBlobs)
        {
            _cacheDirectoryPath = cacheDirectoryPath;
            _cacheDirectoryName = cacheDirectoryName;
            IsReadOnly = true;
            if (CacheDirectory == null)
            {
                LoggingService.Log(new LogEntry(LogLevel.Error,null,$"CacheDirectory null. Creating or rebuilding cache"));

                CreateOrReadCache();
            }
            else
            {
                CheckDirty();
            }
        }
        public RemoteReadOnlyLuceneSyncDirectory(IRemoteDirectory remoteDirectory,
            string cacheDirectoryPath,
            string cacheDirectoryName,
            bool compressBlobs = false) : base(remoteDirectory, compressBlobs)
        {
            _cacheDirectoryPath = cacheDirectoryPath;
            _cacheDirectoryName = cacheDirectoryName;
            IsReadOnly = true;
            if (CacheDirectory == null)
            {
                LoggingService.Log(new LogEntry(LogLevel.Error,null,$"CacheDirectory null. Creating or rebuilding cache"));

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
            lock (RebuildLock)
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
                        _oldIndexFolderName = directory.Name;
                        CacheDirectory = new SimpleFSDirectory(directory);
                        _lockFactory = CacheDirectory.LockFactory;
                    }
                    else
                    {
                        HandleOutOfSync();
                    }
                }
                else
                {
                    HandleOutOfSync();
                }
            }
        }
        public override IndexOutput CreateOutput(string name)
        {
            SetDirty();
            CheckDirty();
            LoggingService.Log(new LogEntry(LogLevel.Info,null,$"Opening output for {_oldIndexFolderName}"));
            return CacheDirectory.CreateOutput(name);
        }
        public override IndexInput OpenInput(string name)
        {
            SetDirty();
            CheckDirty();
            LoggingService.Log(new LogEntry(LogLevel.Info,null,$"Opening input for {_oldIndexFolderName}"));
            return CacheDirectory.OpenInput(name);
        }
        protected override void HandleOutOfSync()
        
        {
            lock (RebuildLock)
            {
                _rebuildQueue.Enqueue(RebuildCache(true));
            }
        }

        //todo: make that as background task. Need input from someone how to handle that correctly as now it is as sync task to avoid issues, but need be change
        protected async Task RebuildCache(bool handle = false)
        {
            lock (RebuildLock)
            {
                //Needs locking
                LoggingService.Log(new LogEntry(LogLevel.Info,null,$"Rebuilding cache"));

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
                        LoggingService.Log(new LogEntry(LogLevel.Error,ex,$"Exception on unlocking old cache index folder"));

                    }

                    
                    oldIndex.Dispose();
                    try
                    {
                        DirectoryInfo oldindexDir = new DirectoryInfo(Path.Combine(_cacheDirectoryPath,
                            _cacheDirectoryName, _oldIndexFolderName));
                        foreach (var file in oldindexDir.GetFiles())
                        {
                            file.Delete();
                        }


                        oldindexDir.Delete();
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Log(new LogEntry(LogLevel.Error,ex,$"Cleaning of old directory failed."));

                    }
                }

                _oldIndexFolderName = tempDir.Name;
                if (handle)
                {
                    HandleOutOfSyncDirectory();
                }
            }
        }
        internal override string[] GetAllBlobFiles()
        {
            lock (RebuildLock)
            {
             return  base.GetAllBlobFiles();
            }
        }
        public class NoDedicatedThreadRebuildQueue
        {
            private Queue<Task> _jobs = new Queue<Task>();
            private bool _delegateQueuedOrRunning = false;
 
            public void Enqueue(Task job)
            {
                lock (_jobs)
                {
                    _jobs.Enqueue(job);
                    if (_jobs.Count > 1)
                    {
                        _jobs.Clear();
                    }
                    if (!_delegateQueuedOrRunning)
                    {
                        _delegateQueuedOrRunning = true;
                        ThreadPool.UnsafeQueueUserWorkItem(ProcessQueuedItems, null);
                    }
                }
            }
 
            private void ProcessQueuedItems(object ignored)
            {
                while (true)
                {
                    Task item;
                    lock (_jobs)
                    {
                        if (_jobs.Count == 0)
                        {
                            _delegateQueuedOrRunning = false;
                            break;
                        }
 
                        item = _jobs.Dequeue();
                    }
 
                    try
                    {
                       item.RunSynchronously();
                       
                    }
                    catch
                    {
                        ThreadPool.UnsafeQueueUserWorkItem(ProcessQueuedItems, null);
                        throw;
                    }
                }
            }
        }
    }
}