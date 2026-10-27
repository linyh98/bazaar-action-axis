using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using BazaarActionAxis.Engine;

namespace BazaarActionAxis.Plugin
{
    public sealed class ValidationReplayWriter : IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new BlockingCollection<Action>(32);
        private readonly ConcurrentQueue<string> _errors = new ConcurrentQueue<string>();
        private readonly Thread _thread;
        private readonly string _folder;
        public ValidationReplayWriter(string folder)
        {
            _folder = folder;
            Directory.CreateDirectory(folder);
            _thread = new Thread(Run) { IsBackground = true, Name = "ActionAxis replay writer" };
            _thread.Start();
        }
        public bool TryGetError(out string error) { return _errors.TryDequeue(out error); }
        public void Save(string name, JsonValue data) { Enqueue(delegate { Write(name, data); }); }
        public void Catalog(string name, object manager)
        { Enqueue(delegate { Write(name, ValidationSnapshot.Catalog(manager)); }); }
        private void Enqueue(Action action)
        {
            if (!_queue.TryAdd(action))
                throw new InvalidOperationException("验证记录写入队列已满；本次采集失败，请检查磁盘。已排队记录仍会保存。");
        }
        private void Run()
        {
            foreach (Action action in _queue.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception error) { _errors.Enqueue(ValidationSnapshot.Error(error)); }
            }
        }
        private void Write(string name, JsonValue data)
        {
            string path = Path.Combine(_folder, name);
            string temporary = path + ".tmp";
            using (FileStream file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Compress))
            using (StreamWriter writer = new StreamWriter(gzip, new UTF8Encoding(false)))
                writer.Write(LiveSkillReader.BuildDebugJson((object)data));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        public void Dispose()
        {
            _queue.CompleteAdding();
            if (!_thread.Join(2000)) _errors.Enqueue("退出时验证记录尚未写完；未完成文件不会标记完整。");
        }
    }
}
