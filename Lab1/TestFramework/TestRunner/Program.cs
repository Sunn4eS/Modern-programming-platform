using Application.Tests;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using TestFramework;

namespace TestRunner
{
    class Program
    {
        static int _total, _passed, _failed;

        static void Main()
        {
            Console.WriteLine("=== МОДЕЛИРОВАНИЕ НАГРУЗКИ (CUSTOM THREAD POOL) ===\n");

            var testTypes = GetTestClasses();
            var loadTasks = CreateTestActions(testTypes);

            int currentTasksCount = loadTasks.Count;
            for (int i = 1; loadTasks.Count < 60; i++)
            {
                int id = i;
                loadTasks.Add(() => {
                    Interlocked.Increment(ref _total);
                    Thread.Sleep(300);
                    Interlocked.Increment(ref _passed);

                    PrintStatus($"Нагрузочный_Синтетический_{id}", "PASSED", ConsoleColor.DarkGray);
                });
            }

            Console.WriteLine($"Всего задач (тесты + нагрузка) подготовлено: {loadTasks.Count}");

            using (var pool = new CustomThreadPool(minThreads: 2, maxThreads: 20, idleTimeoutMs: 1000, stuckTimeoutMs: 3000))
            {
                var sw = Stopwatch.StartNew();

                Console.WriteLine("\n[Сценарий] Этап 1: Пиковая нагрузка (мгновенно подаем 20 тестов)...");
                for (int i = 0; i < 20; i++) 
                    pool.Enqueue(loadTasks[i]);

                Console.WriteLine("\n[Сценарий] Этап 2: Простой системы (ждем 4 секунды для срабатывания адаптивного сжатия)...");
                Thread.Sleep(4000);

                Console.WriteLine("\n[Сценарий] Этап 3: Единичные подачи (подаем 10 тестов с паузами)...");
                for (int i = 20; i < 30; i++)
                {
                    pool.Enqueue(loadTasks[i]);
                    Thread.Sleep(400);
                }

                Console.WriteLine("\n[Сценарий] Этап 4: Вторая пиковая нагрузка (подаем оставшиеся тесты)...");
                for (int i = 30; i < loadTasks.Count; i++) pool.Enqueue(loadTasks[i]);

                Console.WriteLine("\n[Ожидание] Ждем завершения всех тестов...\n");
                pool.WaitAll();
                sw.Stop();

                Console.WriteLine($"\nИТОГО: Запущено: {_total}, Успешно: {_passed}, Провалено: {_failed}");
                Console.WriteLine($"ВРЕМЯ ВЫПОЛНЕНИЯ: {sw.Elapsed.TotalSeconds:F3} сек.");
            }

            Console.ReadLine();
        }

        private static Type[] GetTestClasses()
        {
            return Assembly.GetAssembly(typeof(ValidationTests))!
                .GetTypes()
                .Where(t => t.HasAttr<TestClassAttribute>())
                .ToArray();
        }

        private static List<Action> CreateTestActions(Type[] classes)
        {
            var actions = new List<Action>();
            foreach (var type in classes)
            {
                var classAttr = type.GetCustomAttribute<TestClassAttribute>();
                bool runParallel = classAttr?.RunParallel ?? true;

                var context = CreateSharedContext(type);
                context?.Init();

                var methods = type.GetMethods();
                var setup = methods.FirstOrDefault(m => m.HasAttr<SetupAttribute>());
                var teardown = methods.FirstOrDefault(m => m.HasAttr<TeardownAttribute>());

                var tests = methods.Where(m => m.HasAttr<TestMethodAttribute>())
                                   .OrderBy(m => m.GetCustomAttribute<TestMethodAttribute>()!.Priority)
                                   .ToList();

                if (runParallel)
                {
                    foreach (var method in tests)
                    {
                        var attr = method.GetCustomAttribute<TestMethodAttribute>()!;
                        if (attr.Skip) continue;

                        var testCases = method.GetCustomAttributes<TestCaseAttribute>().Select(a => a.Arguments).DefaultIfEmpty(null);

                        foreach (var args in testCases)
                        {
                            actions.Add(() => ExecuteTest(type, method, setup, teardown, context, args));
                        }
                    }
                }
                else
                {
                    actions.Add(() =>
                    {
                        foreach (var method in tests)
                        {
                            var attr = method.GetCustomAttribute<TestMethodAttribute>()!;
                            if (attr.Skip) continue;

                            var testCases = method.GetCustomAttributes<TestCaseAttribute>().Select(a => a.Arguments).DefaultIfEmpty(null);

                            foreach (var args in testCases)
                            {
                                ExecuteTest(type, method, setup, teardown, context, args);
                            }
                        }
                    });
                }
            }
            return actions;
        }

        private static void ExecuteTest(Type type, MethodInfo m, MethodInfo? s, MethodInfo? td, object? ctx, object[]? args)
        {
            Interlocked.Increment(ref _total);
            string name = FormatTestName(m, args);

            try
            {
                object? instance = ctx != null ? Activator.CreateInstance(type, ctx) : Activator.CreateInstance(type);
                s?.Invoke(instance, null);

                try
                {
                    m.Invoke(instance, args);
                }
                catch (TargetInvocationException ex)
                {
                    if (ex.InnerException is ThreadInterruptedException)
                    {
                        Interlocked.Increment(ref _failed);
                        PrintStatus(name, "TIMEOUT", ConsoleColor.Magenta, "Поток завис и был заменен пулом");
                        return;
                    }
                    throw ex.InnerException!;
                }
                finally
                {
                    try { td?.Invoke(instance, null); } catch { }
                }

                Interlocked.Increment(ref _passed);
                PrintStatus(name, "PASSED", ConsoleColor.Green);
            }
            catch (TestAssertionException ex)
            {
                Interlocked.Increment(ref _failed);
                PrintStatus(name, "FAILED", ConsoleColor.Red, ex.Message);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failed);
                PrintStatus(name, "ERROR", ConsoleColor.DarkRed, ex.Message);
            }
        }

        private static ISharedContext? CreateSharedContext(Type type)
        {
            var attr = type.GetCustomAttribute<UseSharedContextAttribute>();
            return attr != null ? Activator.CreateInstance(attr.ContextType) as ISharedContext : null;
        }

        private static string FormatTestName(MethodInfo m, object[]? args)
        {
            return args == null ? m.Name : $"{m.Name}({string.Join(", ", args)})";
        }

        private static void PrintStatus(string testName, string status, ConsoleColor color, string? msg = null)
        {
            int workerId = CustomThreadPool.CurrentWorkerId.Value;
            string wIdStr = workerId > 0 ? workerId.ToString("D2") : "--";

            string outputLine = $"[Поток {wIdStr}] {testName,-35} {status}";
            if (msg != null) outputLine += $" -> {msg}";

            lock (Console.Out)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(outputLine);
                Console.ResetColor();
            }
        }
    }

    public static class Ext
    {
        public static bool HasAttr<T>(this MemberInfo m) where T : Attribute => m.GetCustomAttribute<T>() != null;
    }
}