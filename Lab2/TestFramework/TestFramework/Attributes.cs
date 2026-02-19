using System;

namespace TestFramework
{
    //Lab2
    [AttributeUsage(AttributeTargets.Method)]
    public class TimeoutAttribute : Attribute
    {
        public int TimeoutMs { get; }

        public TimeoutAttribute(int timeoutMs)
        {
            TimeoutMs = timeoutMs;
        }
    }
    //


    [AttributeUsage(AttributeTargets.Class)]
    public class TestClassAttribute : Attribute
    {
        public string Description { get; set; }
        public bool Parralel {  get; set; } = true;
        public TestClassAttribute() { }
    }


    [AttributeUsage(AttributeTargets.Method)]
    public class SetupAttribute : Attribute { }
   
    [AttributeUsage(AttributeTargets.Method)]
    public class TeardownAttribute : Attribute { }


    [AttributeUsage(AttributeTargets.Method)]
    public class TestMethodAttribute : Attribute
    {
        public bool Skip { get; set; } = false;

        public string Description { get; set; }

        public int Priority { get; set; } = 5;
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TestCaseAttribute : Attribute
    {
        public object[] Arguments { get; }

        public TestCaseAttribute(params object[] args)
        {
            Arguments = args;
        }
    }
}