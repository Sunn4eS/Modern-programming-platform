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
            // Инициализируем сервис перед каждым тестом
            _service = new UserService();
        }

        // --- 1. ГЕНЕРАТОР ДАННЫХ (Requirement: yield return) ---
        // Этот метод возвращает наборы данных по очереди. 
        // Runner вызовет его и создаст отдельный тест для каждой строки.
        public static IEnumerable<object[]> UserDataGenerator()
        {
            yield return new object[] { "Admin", "admin@mail.com", 30 };
            yield return new object[] { "Manager", "manager@mail.com", 25 };
            yield return new object[] { "Support", "support@mail.com", 20 };
        }

        [TestMethod(Priority = 1)]
        [Category("Smoke")] // Атрибут для фильтрации
        [TestCaseSource(nameof(UserDataGenerator))]
        public void Test_RegisterMultipleUsers(string name, string email, int age)
        {
            _service.RegisterUser(name, email, age);
            var user = _service.GetByEmail(email);

            // Используем стандартный ассерт
            Assert.IsNotNull(user);
            Assert.AreEqual(name, user.Username);
        }


        // --- 2. ПРОВЕРКА ФИЛЬТРАЦИИ (Requirement: Фильтрация) ---
        [TestMethod(Priority = 10)]
        [Category("Critical")] // Этот тест можно запустить отдельно через фильтр по категории
        public void Test_CheckPensionerStatus()
        {
            var user = new User { Username = "OldMan", Age = 70 };

            // Используем новый Assert.That с Expression Tree
            // Если упадет, мы увидим в консоли: [70] >= [65]
            Assert.That(() => _service.IsUserPensioner(user), "Пользователь должен быть пенсионером");
        }


        // --- 3. ДЕМОНСТРАЦИЯ ОШИБКИ (Requirement: Expression Tree) ---
        [TestMethod(Priority = 5)]
        [Category("Debug")]
        public void Test_ShouldFailWithDetails()
        {
            int actualAge = 16;
            int requiredAge = 18;

            // Специально валим тест, чтобы увидеть магию деревьев выражений в консоли.
            // Вместо скучного "False", наш Assert.That выведет значения операндов: 16 >= 18
            Assert.That(() => actualAge >= requiredAge, "Регистрация несовершеннолетних запрещена");
        }

        [TestMethod]
        public void Test_ReportFormat()
        {
            string email = "test@test.com";
            _service.RegisterUser("Tester", email, 22);

            string report = _service.GetUserReport(email);

            // Проверка вхождения строки
            Assert.StringContains(report, "REPORT:");
            Assert.StringContains(report, "Tester");
        }
    }
}