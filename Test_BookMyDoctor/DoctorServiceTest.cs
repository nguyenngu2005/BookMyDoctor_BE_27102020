using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace Test_BookMyDoctor
{
    public class DoctorServiceTests
    {
        //UseInMemoryDatabase giúp tạo database giả trong RAM.
        public DBContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<DBContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new DBContext(options);
        }

        [Test]
        public async Task GetAllDoctorsAsync_ReturnsAllDoctors()
        {
            using var db = CreateInMemoryDbContext();
            db.Doctors.AddRange(
                new Doctor { Name = "Alice" },
                new Doctor { Name = "Bob" }
            );
            await db.SaveChangesAsync();

            var service = new DoctorService(db);
            var result = await service.GetAllDoctorsAsync();

            Assert.That(result.Count, Is.EqualTo(2));

            bool foundAlice = false;
            bool foundBob = false;
            foreach (var doctor in result)
            {
                if (doctor.Name == "Alice") foundAlice = true;
                if (doctor.Name == "Bob") foundBob = true;
            }
            Assert.IsTrue(foundAlice, "Expected to find Alice");
            Assert.IsTrue(foundBob, "Expected to find Bob");
        }

        [Test]
        public async Task DeleteDoctorAsync_ReturnsFalse_WhenNotFound()
        {
            using var db = CreateInMemoryDbContext();
            var service = new DoctorService(db);

            try
            {
                var result = await service.DeleteDoctorAsync(999);
                // Nếu doctor không tồn tại, service ném AppException theo code hiện tại
                Assert.Fail("Expected AppException but method completed normally");
            }
            catch (AppException ex)
            {
                Assert.That(ex.StatusCode, Is.EqualTo(404)); // theo code hiện tại
            }
        }

        [Test]
        public async Task GetDoctorByIdAsync_ReturnsNull_WhenNotFound()
        {
            using var db = CreateInMemoryDbContext();
            var service = new DoctorService(db);

            var doctor = await service.GetDoctorByIdAsync(999);

            Assert.IsNull(doctor);
        }

        [Test]
        public async Task AddDoctorAsync_CreatesDoctorAndSchedules()
        {
            // Arrange
            using var db = CreateInMemoryDbContext();
            var service = new DoctorService(db);

            var doctor = new Doctor
            {
                Name = "John",
                Department = "Cardiology",
                Gender = "Male"
            };

            var days = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday };
            var start = new TimeOnly(9, 0);
            var end = new TimeOnly(17, 0);

            // Act
            var added = await service.AddDoctorAsync(doctor, days, start, end);

            // Assert
            var doctors = await db.Doctors.ToListAsync();
            var schedules = await db.Schedules.ToListAsync();

            // Kiểm tra số lượng doctor
            int doctorCount = 0;
            foreach (var d in doctors)
            {
                doctorCount++;
            }
            Assert.That(doctorCount, Is.EqualTo(1));

            // Kiểm tra có ít nhất 1 schedule
            Assert.True(schedules.Count > 0);

            // Kiểm tra từng schedule
            foreach (var s in schedules)
            {
                Assert.That(s.DoctorId, Is.EqualTo(doctor.DoctorId));

                bool isValidDay = s.WorkDate.DayOfWeek == DayOfWeek.Monday ||
                                  s.WorkDate.DayOfWeek == DayOfWeek.Wednesday;
                Assert.True(isValidDay);

                Assert.That(s.ScheduleId, Is.Not.EqualTo(0));
            }
        }
    }
}