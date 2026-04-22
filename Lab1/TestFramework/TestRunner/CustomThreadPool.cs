using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TestFramework
{
    public class ThreadPoolEventArgs : EventArgs
    {
        public string Message { get; }
        public int WorkerId { get; }
        public DateTime Timestamp { get; }

        public ThreadPoolEventArgs(string message, int workerId = 0)
        {
            Message = message;
            WorkerId = workerId;
            Timestamp = DateTime.Now;
        }
    }

    public class CustomThreadPool : IDisposable
    {
        public static readonly ThreadLocal<int> CurrentWorkerId = new ThreadLocal<int>();

        public event EventHandler<ThreadPoolEventArgs> WorkerCreated;  
        public event EventHandler<ThreadPoolEventArgs> WorkerRemoved;  
        public event EventHandler<ThreadPoolEventArgs> TaskStarted;    
        public event EventHandler<ThreadPoolEventArgs> TaskCompleted;  
        public event EventHandler<ThreadPoolEventArgs> ThreadStuck;    

        private readonly int _minThreads;
        private readonly int _maxThreads;
        private readonly int _idleTimeoutMs;
        private readonly int _stuckTimeoutMs;

        private readonly Queue<Action> _queue = new Queue<Action>();
        private readonly List<Worker> _workers = new List<Worker>();
        private readonly Stack<int> _availableIds = new Stack<int>();
        private readonly object _lock = new object();

        private bool _isShuttingDown;
        private int _pendingTasks;
        private Thread _supervisor;

        public CustomThreadPool(int minThreads, int maxThreads, int idleTimeoutMs, int stuckTimeoutMs)
        {
            _minThreads = minThreads;
            _maxThreads = maxThreads;
            _idleTimeoutMs = idleTimeoutMs;
            _stuckTimeoutMs = stuckTimeoutMs;

            for (int i = maxThreads; i >= 1; i--) _availableIds.Push(i);

            lock (_lock)
            {
                for (int i = 0; i < _minThreads; i++) AddWorker();
            }

            _supervisor = new Thread(SupervisorLoop) { IsBackground = true };
            _supervisor.Start();
        }

        public void Enqueue(Action action)
        {
            Interlocked.Increment(ref _pendingTasks);
            lock (_lock)
            {
                _queue.Enqueue(action);

                int workingThreads = _workers.Count(w => w.IsWorking);
                if (_queue.Count > (_workers.Count - workingThreads) && _workers.Count < _maxThreads)
                {
                    AddWorker();
                }

                Monitor.Pulse(_lock);
            }
        }

        private void AddWorker()
        {
            int workerId = _availableIds.Pop();
            var worker = new Worker(this, workerId);

            var thread = new Thread(worker.Run) { IsBackground = true };
            worker.Thread = thread;
            _workers.Add(worker);

           
            WorkerCreated?.Invoke(this, new ThreadPoolEventArgs("Поток создан", workerId));
            thread.Start();
        }

        public void WaitAll()
        {
            while (Volatile.Read(ref _pendingTasks) > 0) Thread.Sleep(100);
        }

        private void SupervisorLoop()
        {
            while (!_isShuttingDown)
            {
                Thread.Sleep(500);
                lock (_lock)
                {
                    for (int i = _workers.Count - 1; i >= 0; i--)
                    {
                        var w = _workers[i];
                        if (w.IsWorking && w.TaskStartTime.HasValue)
                        {
                            var duration = (DateTime.Now - w.TaskStartTime.Value).TotalMilliseconds;
                            if (duration > _stuckTimeoutMs)
                            {
                                ThreadStuck?.Invoke(this, new ThreadPoolEventArgs($"Поток завис ({duration:F0}мс)", w.Id));

                                w.Thread.Interrupt(); 
                                _workers.RemoveAt(i);
                                _availableIds.Push(w.Id);
                                AddWorker(); 
                            }
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            _isShuttingDown = true;
            lock (_lock) Monitor.PulseAll(_lock);
        }

        private class Worker
        {
            private readonly CustomThreadPool _pool;
            public int Id { get; }
            public Thread Thread { get; set; }
            public bool IsWorking { get; set; }
            public DateTime? TaskStartTime { get; set; }

            public Worker(CustomThreadPool pool, int id)
            {
                _pool = pool;
                Id = id;
            }

            public void Run()
            {
                CurrentWorkerId.Value = Id;

                while (true)
                {
                    Action task = null;
                    lock (_pool._lock)
                    {
                        while (_pool._queue.Count == 0 && !_pool._isShuttingDown)
                        {
                            if (!Monitor.Wait(_pool._lock, _pool._idleTimeoutMs))
                            {
                                if (_pool._workers.Count > _pool._minThreads)
                                {
                                    _pool._workers.Remove(this);
                                    _pool._availableIds.Push(Id);
                                    _pool.WorkerRemoved?.Invoke(_pool, new ThreadPoolEventArgs("Поток удален по простою", Id));
                                    return;
                                }
                            }
                        }

                        if (_pool._isShuttingDown && _pool._queue.Count == 0) return;

                        task = _pool._queue.Dequeue();
                        IsWorking = true;
                        TaskStartTime = DateTime.Now;
                    }

                    _pool.TaskStarted?.Invoke(_pool, new ThreadPoolEventArgs("Задача запущена", Id));

                    try { task(); }
                    catch (ThreadInterruptedException) {}
                    catch (Exception) { }
                    finally
                    {
                        lock (_pool._lock)
                        {
                            IsWorking = false;
                            TaskStartTime = null;
                        }
                        _pool.TaskCompleted?.Invoke(_pool, new ThreadPoolEventArgs("Задача завершена", Id));
                        Interlocked.Decrement(ref _pool._pendingTasks);
                    }
                }
            }
        }
    }
}