using System;
using System.Collections.Generic;
using Application.Logic;
using TestFramework;

namespace Application.Tests
{
    [TestClass(Description = "Продвинутые тесты функциональности")]
    public class AdvancedTests
    {
        private UserService _service;

        [Setup]
        public void Init()
        {
            _service = new UserService();
        }

        public static IEnumerable<object[]> UserDataGenerator()
        {
            yield return new object[] { "Admin", "admin@mail.com", 30 };
            yield return new object[] { "Manager", "manager@mail.com", 25 };
            yield return new object[] { "Support", "support@mail.com", 20 };
        }

        [TestMethod(Priority = 1)]
        [Category("Smoke")] 
        [TestCaseSource(nameof(UserDataGenerator))]
        public void Test_RegisterMultipleUsers(string name, string email, int age)
        {
            _service.RegisterUser(name, email, age);
            var user = _service.GetByEmail(email);

            Assert.IsNotNull(user);
            Assert.AreEqual(name, user.Username);
        }


        [TestMethod(Priority = 10)]
        [Category("Critical")] 
        public void Test_CheckPensionerStatus()
        {
            var user = new User { Username = "OldMan", Age = 70 };

            Assert.That(() => _service.IsUserPensioner(user), "Пользователь должен быть пенсионером");
        }


        [TestMethod(Priority = 5)]
        [Category("Debug")]
        public void Test_ShouldFailWithDetails()
        {
            int actualAge = 16;
            int requiredAge = 18;

            Assert.That(() => actualAge >= requiredAge, "Регистрация несовершеннолетних запрещена");
        }

        [TestMethod]
        public void Test_ReportFormat()
        {
            string email = "test@test.com";
            _service.RegisterUser("Tester", email, 22);

            string report = _service.GetUserReport(email);

            Assert.StringContains(report, "REPORT:");
            Assert.StringContains(report, "Tester");
        }
    }
}