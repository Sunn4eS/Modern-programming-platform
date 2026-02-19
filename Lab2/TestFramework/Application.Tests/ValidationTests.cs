using Application.Logic;
using TestFramework;
using System;
using System.ComponentModel;

namespace Application.Tests
{
    [TestClass(Description = "Модульные тесты валидации пользователя")]
    public class ValidationTests
    {
        private UserService _service;

        [Setup]
        public void Setup()
        {
            _service = new UserService();
        }

        [TestMethod(Description = "Успешная регистрация")]
        public void Test_Register_ValidUser_Success()
        {
            _service.RegisterUser("Ivan", "ivan@test.com", 25);
            var user = _service.GetByEmail("ivan@test.com");

            Assert.IsNotNull(user);
            Assert.AreEqual("Ivan", user.Username);
        }

        [TestMethod(Description = "Регистрация нескольких пользователей")]
        public void Test_Register_Users()
        {
            for (int i = 0; i < 6; i++)
            {
                _service.RegisterUser($"User{i}", $"u{i}@test.com", i + 20);
            }
        }

        [TestMethod(Description = "Ошибка при регистрации ребенка")]
        public void Test_Register_Underage_ThrowsException()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                _service.RegisterUser("Kid", "kid@test.com", 15);
            });
        }

        [TestMethod(Description = "Ошибка при неверном Email")]
        [TestCase("bademail")]
        [TestCase("email@nodot")]
        [TestCase("noat.com")]
        public void Test_Register_InvalidEmail(string badEmail)
        {
            Assert.Throws<FormatException>(() =>
            {
                _service.RegisterUser("User", badEmail, 20);
            });
        }

        [TestMethod]
        [Timeout(1000)]
        public void Test_Report_Timeout_Fail()
        {
            _service.ProcessHugeReport();
        }
    }
}