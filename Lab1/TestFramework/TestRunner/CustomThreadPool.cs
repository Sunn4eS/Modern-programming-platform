using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TestFramework
{
    public class CustomThreadPool : IDisposable
    {
        public static readonly ThreadLocal<int> CurrentWorkerId = new ThreadLocal<int>();

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

            for (int i = 0; i < _minThreads; i++) AddWorker();

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

            SafePrint($"[Мониторинг] Потоков: {_workers.Count}/{_maxThreads}", ConsoleColor.Cyan);
            thread.Start();
        }

        public void WaitAll()
        {
            while (Volatile.Read(ref _pendingTasks) > 0) Thread.Sleep(100);
        }

        public void Dispose()
        {
            _isShuttingDown = true;
            lock (_lock) Monitor.PulseAll(_lock);
        }

        private void SupervisorLoop()
        {
            while (!_isShuttingDown)
            {
                Thread.Sleep(500);
                lock (_lock)
                {
                    int workingCount = 0;
                    for (int i = _workers.Count - 1; i >= 0; i--)
                    {
                        var w = _workers[i];
                        if (w.IsWorking)
                        {
                            workingCount++;
                            if (w.TaskStartTime.HasValue && (DateTime.Now - w.TaskStartTime.Value).TotalMilliseconds > _stuckTimeoutMs)
                            {
                                SafePrint($"[Пул] Отказ: Поток {w.Id:D2} завис. Удаление и пересоздание...", ConsoleColor.Magenta);

                                w.Thread.Interrupt();
                                _workers.RemoveAt(i);
                                _availableIds.Push(w.Id);
                                AddWorker();
                            }
                        }
                    }

                    SafePrint($"[Мониторинг] Потоков: {_workers.Count}/{_maxThreads} (Активно: {workingCount}), Задач в очереди: {_queue.Count}", ConsoleColor.Cyan);
                }
            }
        }

        private static void SafePrint(string message, ConsoleColor color)
        {
            lock (Console.Out)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(message);
                Console.ResetColor();
            }
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
                            bool signaled = Monitor.Wait(_pool._lock, _pool._idleTimeoutMs);

                            if (!signaled)
                            {
                                if (_pool._workers.Count > _pool._minThreads)
                                {
                                    _pool._workers.Remove(this);
                                    _pool._availableIds.Push(Id);

                                    SafePrint($"[Пул] Сжатие: Поток {Id:D2} простаивал и был удален.", ConsoleColor.DarkYellow);
                                    return;
                                }
                            }
                        }

                        if (_pool._isShuttingDown && _pool._queue.Count == 0) return;

                        task = _pool._queue.Dequeue();
                        IsWorking = true;
                        TaskStartTime = DateTime.Now;
                    }

                    try { task(); }
                    finally
                    {
                        lock (_pool._lock)
                        {
                            IsWorking = false;
                            TaskStartTime = null;
                        }
                        Interlocked.Decrement(ref _pool._pendingTasks);
                    }
                }
            }
        }
    }
}