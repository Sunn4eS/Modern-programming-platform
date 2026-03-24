
using Application.Logic;
using TestFramework;
using System.Threading;

namespace Application.Tests
{
    [TestClass(RunParallel = true)]
    public class UserPropertiesTests
    {
        [Setup]
        public void Setup()
        {
            Thread.Sleep(50);
        }
        [TestMethod(Priority = 1)]
        public void ValidateUserDefaults()
        {
            var user = new User();
            Assert.AreEqual(0, user.Id);
            Assert.IsNull(user.Username);
            Thread.Sleep(200);
        }

        [TestMethod(Priority = 2)]
        [TestCase("user1@test.com", 25)]
        [TestCase("user2@test.com", 30)]
        [TestCase("user3@test.com", 45)]
        [TestCase("user4@test.com", 50)]
        [TestCase("user5@test.com", 60)]
        public void ValidateUserAssignment(string email, int age)
        {
            var user = new User { Email = email, Age = age };
            Assert.StringContains(user.Email, "@");
            Assert.IsTrue(user.Age >= 18);
            Thread.Sleep(400);
        }

        [TestMethod(Priority = 3)]
        public void ValidateToStringFormat()
        {
            var user = new User { Username = "TestUser", Email = "test@test.com" };
            Assert.AreEqual("TestUser (test@test.com)", user.ToString());
            Thread.Sleep(150);
        }

        [TestMethod(Priority = 4)]
        public void ValidateUserAgeModification()
        {
            var user = new User { Age = 20 };
            user.Age += 5;
            Assert.AreEqual(25, user.Age);
            Thread.Sleep(100);
        }
    }
}