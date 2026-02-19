//using Application.Tests; 
//using System.Diagnostics;
//using System.Reflection;
//using TestFramework;

//namespace TestRunner
//{
//    class Program
//    {
//        private static readonly object _consoleLock = new object();

//        static void Main(string[] args)
//        {
//            int maxDegree = 6;
//            Console.WriteLine($"MaxDegreeOfParallelism: {maxDegree}\n");

//           var testAssembly = Assembly.GetAssembly(typeof(ValidationTests));
//            var testClasses = testAssembly.GetTypes()
//                .Where(t => t.GetCustomAttribute<TestClassAttribute>() != null)
//                .ToArray();

//            Stopwatch sw = Stopwatch.StartNew();

//            int total = 0, passed = 0, failed = 0;

//            foreach (var classType in testClasses)
//            {
//                RunClassParallel(classType, maxDegree, ref total, ref passed, ref failed);
//            }

//            sw.Stop();

//            Console.WriteLine("--------------------------------------------------");
//            Console.WriteLine($"ВРЕМЯ ВЫПОЛНЕНИЯ: {sw.Elapsed.TotalSeconds:F3} сек.");
//            Console.WriteLine($"Всего: {total}, Успешно: {passed}, Провалено: {failed}");
//            Console.ReadLine();
//        }

//        static void RunClassParallel(Type classType, int maxDegree, ref int total, ref int passed, ref int failed)
//        {
//            var classAttr = classType.GetCustomAttribute<TestClassAttribute>();

//            SafePrint($"\nЗАПУСК КЛАССА: {classType.Name}", ConsoleColor.Cyan);

//            object sharedContext = null;
//            ISharedContext ctxInterface = null;
//            var ctxAttr = classType.GetCustomAttribute<UseSharedContextAttribute>();
//            if (ctxAttr != null)
//            {
//                try
//                {
//                    sharedContext = Activator.CreateInstance(ctxAttr.ContextType);
//                    ctxInterface = sharedContext as ISharedContext;
//                    ctxInterface?.Init();
//                }
//                catch (Exception ex)
//                {
//                    SafePrint($"Ошибка инициализации Context: {ex.Message}", ConsoleColor.Red);
//                    return;
//                }
//            }

//            var setupMethod = classType.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<SetupAttribute>() != null);
//            var teardownMethod = classType.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<TeardownAttribute>() != null);
//            var testMethods = classType.GetMethods()
//                .Where(m => m.GetCustomAttribute<TestMethodAttribute>() != null)
//                .ToList();

//            int localTotal = 0, localPassed = 0, localFailed = 0;
//            var options = new ParallelOptions { MaxDegreeOfParallelism = maxDegree };

//            if (classAttr.Parralel)
//            {
//                Parallel.ForEach(testMethods, options, method =>
//                {
//                    ProcessMethod(classType, method, setupMethod, teardownMethod, sharedContext,
//                                  ref localTotal, ref localPassed, ref localFailed);
//                });
//            }
//            else
//            {
//                foreach (var method in testMethods)
//                {
//                    ProcessMethod(classType, method, setupMethod, teardownMethod, sharedContext,
//                                  ref localTotal, ref localPassed, ref localFailed);
//                }
//            }

//            total += localTotal;
//            passed += localPassed;
//            failed += localFailed;

//            ctxInterface?.Cleanup();
//        }

//        static void ProcessMethod(Type classType, MethodInfo method, MethodInfo setup, MethodInfo teardown,
//                          object sharedContext, ref int lTotal, ref int lPassed, ref int lFailed)
//        {
//            var testCases = method.GetCustomAttributes<TestCaseAttribute>().ToArray();

//            if (testCases.Length > 0)
//            {
//                foreach (var tc in testCases)
//                {
//                    Interlocked.Increment(ref lTotal);
//                    bool success = RunSingleTest(classType, method, setup, teardown, sharedContext, tc.Arguments);
//                    if (success) Interlocked.Increment(ref lPassed);
//                    else Interlocked.Increment(ref lFailed);
//                }
//            }
//            else
//            {
//                Interlocked.Increment(ref lTotal);
//                bool success = RunSingleTest(classType, method, setup, teardown, sharedContext, null);
//                if (success) Interlocked.Increment(ref lPassed);
//                else Interlocked.Increment(ref lFailed);
//            }
//        }

//        static bool RunSingleTest(Type classType, MethodInfo method, MethodInfo setup, MethodInfo teardown, object sharedContext, object[] args)
//        {
//            string testName = method.Name + (args != null ? $"({string.Join(", ", args)})" : "");

//            var timeoutAttr = method.GetCustomAttribute<TimeoutAttribute>();
//            var attr = method.GetCustomAttribute<TestMethodAttribute>();

//            if (attr != null && attr.Skip)
//            {
//                SafePrint($"[{testName}] SKIPPED", ConsoleColor.Yellow);
//                return true;
//            }

//            object instance;
//            try
//            {
//                if (sharedContext != null) instance = Activator.CreateInstance(classType, new object[] { sharedContext });
//                else instance = Activator.CreateInstance(classType);
//            }
//            catch (Exception ex)
//            {
//                SafePrint($"[{testName}] CRASH Constructor: {ex.Message}", ConsoleColor.DarkRed);
//                return false;
//            }

//            Action runTest = () =>
//            {
//                try { 
//                    setup?.Invoke(instance, null);
//                }
//                catch (Exception ex) 
//                { 
//                    throw new Exception($"Setup failed: {ex.InnerException?.Message ?? ex.Message}"); 
//                }

//                try
//                {
//                    method.Invoke(instance, args);
//                }
//                catch (TargetInvocationException ex)
//                {
//                    throw ex.InnerException;
//                }

//                try { 
//                    teardown?.Invoke(instance, null); 
//                }
//                catch {
//                }
//            };

//            // Запуск с Timeout
//            try
//            {
//                int timeout = timeoutAttr?.TimeoutMs ?? int.MaxValue;

//                var task = Task.Run(runTest);

//                try
//                {
//                    bool completed = task.Wait(timeout);

//                    if (!completed)
//                    {
//                        SafePrint($"[{testName}] FAILED (Timeout {timeout}ms)", ConsoleColor.Red);
//                        return false;
//                    }
//                }
//                catch (AggregateException ae)
//                {
//                    var inner = ae.Flatten().InnerException;

//                    if (inner is TestAssertionException)
//                    {
//                        SafePrint($"[{testName}] FAILED: {inner.Message}", ConsoleColor.Red);
//                    }
//                    else
//                    {
//                        SafePrint($"[{testName}] ERROR: {inner.GetType().Name} - {inner.Message}", ConsoleColor.DarkRed);
//                    }
//                    return false;
//                }

//                SafePrint($"[{testName}] PASSED (Thread {Thread.CurrentThread.ManagedThreadId})", ConsoleColor.Green);
//                return true;
//            }
//            catch (Exception ex)
//            {
//                SafePrint($"[{testName}] RUNNER CRASH: {ex.Message}", ConsoleColor.DarkRed);
//                return false;
//            }
//        }

//        static void SafePrint(string message, ConsoleColor color)
//        {
//            lock (_consoleLock)
//            {
//                Console.ForegroundColor = color;
//                Console.WriteLine(message);
//                Console.ResetColor();
//            }
//        }
//    }
//}

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Application.Tests;
using TestFramework;

namespace TestRunner
{
    class Program
    {
        private static readonly object _consoleLock = new();

        static void Main()
        {
            const int maxDegree = 1;
            Console.WriteLine($"MaxDegreeOfParallelism: {maxDegree}\n");

            var testClasses = GetTestClasses(typeof(ValidationTests));
            var timer = Stopwatch.StartNew();

            int total = 0, passed = 0, failed = 0;

            foreach (var type in testClasses)
            {
                var result = RunClass(type, maxDegree);
                total += result.t; passed += result.p; failed += result.f;
            }

            PrintSummary(timer.Elapsed, total, passed, failed);
        }

        // --- ЛОГИКА ЗАПУСКА КЛАССА ---

        static (int t, int p, int f) RunClass(Type type, int maxDegree)
        {
            Log($"\nЗАПУСК КЛАССА: {type.Name}", ConsoleColor.Cyan);

            var context = CreateSharedContext(type);
            context?.Init();

            var testMethods = type.GetMethods().Where(m => m.Has<TestMethodAttribute>()).ToList();

            int t = 0, p = 0, f = 0;

            // Определяем действие для каждого метода
            Action<MethodInfo> runMethod = method =>
            {
                var cases = method.GetCustomAttributes<TestCaseAttribute>()
                                  .Select(a => a.Arguments)
                                  .DefaultIfEmpty(null);

                foreach (var args in cases)
                {
                    Interlocked.Increment(ref t);
                    bool ok = ExecuteTestWithTimeout(type, method, context, args);
                    Interlocked.Add(ref (ok ? ref p : ref f), 1);
                }
            };

            var isParallel = type.GetCustomAttribute<TestClassAttribute>()?.Parralel ?? false;

            if (isParallel)
                Parallel.ForEach(testMethods, new ParallelOptions { MaxDegreeOfParallelism = maxDegree }, runMethod);
            else
                testMethods.ForEach(runMethod);

            context?.Cleanup();
            return (t, p, f);
        }

        // --- ЛОГИКА ОДИНОЧНОГО ТЕСТА ---

        static bool ExecuteTestWithTimeout(Type type, MethodInfo method, object? context, object[]? args)
        {
            var testAttr = method.GetCustomAttribute<TestMethodAttribute>()!;
            string testName = FormatTestName(method, args);

            if (testAttr.Skip)
            {
                Log($"[{testName}] SKIPPED", ConsoleColor.Yellow);
                return true;
            }

            int timeout = method.GetCustomAttribute<TimeoutAttribute>()?.TimeoutMs ?? int.MaxValue;

            try
            {
                // Запускаем тест в отдельной задаче для контроля времени
                var task = Task.Run(() => InvokeTestMethod(type, method, context, args));

                if (task.Wait(timeout)) return task.Result;

                Log($"[{testName}] FAILED: Превышен лимит времени ({timeout}ms)", ConsoleColor.Red);
                return false;
            }
            catch (Exception ex)
            {
                HandleException(testName, ex);
                return false;
            }
        }

        private static bool InvokeTestMethod(Type type, MethodInfo method, object? context, object[]? args)
        {
            // Создаем экземпляр (поддержка DI через конструктор)
            var instance = context != null
                ? Activator.CreateInstance(type, context)
                : Activator.CreateInstance(type);

            var setup = type.GetMethods().FirstOrDefault(m => m.Has<SetupAttribute>());
            var teardown = type.GetMethods().FirstOrDefault(m => m.Has<TeardownAttribute>());

            try
            {
                setup?.Invoke(instance, null);
                method.Invoke(instance, args);
                Log($"[{FormatTestName(method, args)}] PASSED", ConsoleColor.Green);
                return true;
            }
            catch (TargetInvocationException tie)
            {
                throw tie.InnerException ?? tie;
            }
            finally
            {
                teardown?.Invoke(instance, null);
            }
        }

        // --- ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ---

        private static void HandleException(string testName, Exception ex)
        {
            var actualEx = ex is AggregateException ae ? ae.Flatten().InnerException : ex;
            var isAssert = actualEx is TestAssertionException;

            string prefix = isAssert ? "FAILED" : "ERROR";
            ConsoleColor color = isAssert ? ConsoleColor.Red : ConsoleColor.DarkRed;

            Log($"[{testName}] {prefix}: {actualEx?.Message}", color);
        }

        private static ISharedContext? CreateSharedContext(Type type)
        {
            var attr = type.GetCustomAttribute<UseSharedContextAttribute>();
            return attr != null ? Activator.CreateInstance(attr.ContextType) as ISharedContext : null;
        }

        private static Type[] GetTestClasses(Type referenceType) =>
            Assembly.GetAssembly(referenceType)!
                .GetTypes()
                .Where(t => t.Has<TestClassAttribute>())
                .ToArray();

        private static string FormatTestName(MethodInfo m, object[]? args) =>
            $"{m.Name}{(args != null ? $"({string.Join(", ", args)})" : "")}";

        private static void Log(string message, ConsoleColor color)
        {
            lock (_consoleLock)
            {
                Console.ForegroundColor = color;
                Console.WriteLine(message);
                Console.ResetColor();
            }
        }

        private static void PrintSummary(TimeSpan elapsed, int t, int p, int f)
        {
            Console.WriteLine($"\n{new string('-', 30)}");
            Console.WriteLine($"ВРЕМЯ: {elapsed.TotalSeconds:F2} сек.");
            Console.WriteLine($"ИТОГ: Всего {t}, Пройдено {p}, Упало {f}");
        }
    }

    public static class MemberInfoExtensions
    {
        public static bool Has<T>(this MemberInfo mi) where T : Attribute => mi.IsDefined(typeof(T));
    }
}