using System;
using System.Collections.Generic;
using System.Threading;

namespace TestRunner
{
    public class DynamicThreadPool : IDisposable
    {
        private readonly Queue<Action> _taskQueue = new Queue<Action>();
        private readonly object _queueLock = new object();

        private readonly List<WorkerInfo> _workers = new List<WorkerInfo>();
        private readonly object _workersLock = new object();

        private readonly int _minThreads;
        private readonly int _maxThreads;
        private readonly int _idleTimeoutMs;     
        private readonly int _maxTaskDurationMs; 

        private volatile bool _isStopping;
        private readonly ManualResetEvent _stopEvent = new ManualResetEvent(false);

        private Thread _monitorThread;

        private int _totalTasksProcessed;
        private int _peakThreadCount;

        public DynamicThreadPool(int minThreads = 2, int maxThreads = 10, int idleTimeoutMs = 5000, int maxTaskDurationMs = 10000)
        {
            _minThreads = minThreads;
            _maxThreads = maxThreads;
            _idleTimeoutMs = idleTimeoutMs;
            _maxTaskDurationMs = maxTaskDurationMs;

            for (int i = 0; i < minThreads; i++)
            {
                CreateWorker();
            }

            _monitorThread = new Thread(MonitorLoop) { IsBackground = true, Name = "PoolMonitor" };
            _monitorThread.Start();

            Console.WriteLine($"[Пул] Инициализирован. Min={_minThreads}, Max={_maxThreads}");
        }

        public void EnqueueTask(Action task)
        {
            if (_isStopping)
                throw new InvalidOperationException("Пул остановлен, нельзя добавлять новые задачи.");

            lock (_queueLock)
            {
                _taskQueue.Enqueue(task);
                Monitor.Pulse(_queueLock);
            }
        }

        public void Stop()
        {
            _isStopping = true;
            _stopEvent.Set();

            if (_monitorThread != null && _monitorThread.IsAlive)
                _monitorThread.Join(2000);

            lock (_queueLock)
            {
                Monitor.PulseAll(_queueLock);
            }

            lock (_workersLock)
            {
                foreach (var w in _workers)
                {
                    if (w.Thread.IsAlive)
                        w.Thread.Join(1000);
                }
                _workers.Clear();
            }
        }

        public void Dispose()
        {
            Stop();
            _stopEvent.Dispose();
        }

        private void CreateWorker()
        {
            var worker = new WorkerInfo(this);
            lock (_workersLock)
            {
                _workers.Add(worker);
                if (_workers.Count > _peakThreadCount)
                    _peakThreadCount = _workers.Count;
            }
            worker.Start();
        }

        private void RemoveWorker(WorkerInfo worker)
        {
            lock (_workersLock)
            {
                _workers.Remove(worker);
            }
        }

        private void MonitorLoop()
        {
            while (!_isStopping)
            {
                try
                {
                    Thread.Sleep(500);

                    lock (_queueLock)
                    {
                        int queueCount = _taskQueue.Count;
                        int workerCount;
                        lock (_workersLock) workerCount = _workers.Count;

                        if (queueCount > 0 && workerCount < _maxThreads)
                        {
                            Console.WriteLine($"[Пул] Очередь {queueCount}, потоков {workerCount} < {_maxThreads}. Создаём новый поток.");
                            CreateWorker();
                        }

                    }

                    List<WorkerInfo> workersSnapshot;
                    lock (_workersLock)
                    {
                        workersSnapshot = new List<WorkerInfo>(_workers);
                    }

                    foreach (var w in workersSnapshot)
                    {
                        TimeSpan? duration = w.GetCurrentTaskDuration();
                        if (duration.HasValue && duration.Value.TotalMilliseconds > _maxTaskDurationMs)
                        {
                            Console.WriteLine($"[Пул] Поток {w.Id} выполняет задачу уже {duration.Value.TotalSeconds:F1} сек. Считаем зависшим и заменяем.");
                            lock (_workersLock)
                            {
                                _workers.Remove(w);
                            }
                            lock (_workersLock)
                            {
                                if (_workers.Count < _maxThreads && !_isStopping)
                                    CreateWorker();
                            }
                        }
                    }

                    foreach (var w in workersSnapshot)
                    {
                        if (!w.Thread.IsAlive)
                        {
                            Console.WriteLine($"[Пул] Поток {w.Id} неожиданно завершился. Удаляем и создаём новый.");
                            lock (_workersLock)
                            {
                                _workers.Remove(w);
                                if (_workers.Count < _maxThreads && !_isStopping)
                                    CreateWorker();
                            }
                        }
                    }

                    if (Environment.TickCount % 3000 < 500) 
                    {
                        int queueCount;
                        int workerCount;
                        lock (_queueLock) queueCount = _taskQueue.Count;
                        lock (_workersLock) workerCount = _workers.Count;
                        Console.WriteLine($"[Пул] Монитор: рабочих={workerCount}, очередь={queueCount}, обработано задач={_totalTasksProcessed}, пик потоков={_peakThreadCount}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Пул] Ошибка в мониторинге: {ex.Message}");
                }
            }
        }

        private bool TryDequeueTask(out Action task, int timeoutMs)
        {
            lock (_queueLock)
            {
                if (_taskQueue.Count > 0)
                {
                    task = _taskQueue.Dequeue();
                    Interlocked.Increment(ref _totalTasksProcessed);
                    return true;
                }

                if (_isStopping)
                {
                    task = null;
                    return false;
                }

                Monitor.Wait(_queueLock, timeoutMs);

                if (_taskQueue.Count > 0)
                {
                    task = _taskQueue.Dequeue();
                    Interlocked.Increment(ref _totalTasksProcessed);
                    return true;
                }

                task = null;
                return false;
            }
        }

        
        private class WorkerInfo
        {
            private static int _nextId = 0;
            public int Id { get; }
            public Thread Thread { get; private set; }
            private readonly DynamicThreadPool _pool;

            private DateTime? _taskStartTime;
            private readonly object _taskLock = new object();

            private DateTime _lastTaskEndTime = DateTime.UtcNow;

            public WorkerInfo(DynamicThreadPool pool)
            {
                Id = Interlocked.Increment(ref _nextId);
                _pool = pool;
                Thread = new Thread(WorkLoop) { IsBackground = true, Name = $"PoolWorker-{Id}" };
            }

            public void Start()
            {
                Thread.Start();
            }

            private void WorkLoop()
            {
                Console.WriteLine($"[Пул] Поток {Id} запущен.");

                while (!_pool._isStopping)
                {
                    bool dequeued = _pool.TryDequeueTask(out Action task, _pool._idleTimeoutMs);

                    if (!dequeued)
                    {
                        lock (_pool._workersLock)
                        {
                            if (_pool._workers.Count > _pool._minThreads && !_pool._isStopping)
                            {
                                Console.WriteLine($"[Пул] Поток {Id} завершается по простою (нет задач).");
                                _pool.RemoveWorker(this);
                                break;
                            }
                        }
                        continue;
                    }

                    lock (_taskLock)
                    {
                        _taskStartTime = DateTime.UtcNow;
                    }
                    try
                    {
                        task();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Пул] Поток {Id} поймал исключение при выполнении задачи: {ex.Message}");
                    }
                    finally
                    {
                        lock (_taskLock)
                        {
                            _taskStartTime = null;
                            _lastTaskEndTime = DateTime.UtcNow;
                        }
                    }

                    bool stillInPool;
                    lock (_pool._workersLock)
                    {
                        stillInPool = _pool._workers.Contains(this);
                    }
                    if (!stillInPool)
                    {
                        Console.WriteLine($"[Пул] Поток {Id} обнаружен удалённым из пула (зависший?). Завершаемся.");
                        break;
                    }
                }

                Console.WriteLine($"[Пул] Поток {Id} завершён.");
            }

            public TimeSpan? GetCurrentTaskDuration()
            {
                lock (_taskLock)
                {
                    if (_taskStartTime.HasValue)
                        return DateTime.UtcNow - _taskStartTime.Value;
                    return null;
                }
            }
        }
    }
}