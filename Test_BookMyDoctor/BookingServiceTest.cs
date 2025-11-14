using Moq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using BookMyDoctor_WebAPI.Repositories;

namespace Test_BookMyDoctor
{
    public class BookingServiceTests
    {
        //1.Invalid Email_email sai 
        [Test]
        public async Task PublicBookAsync_InvalidEmail_ThrowsAppException()
        {
            var mockRepo = new Mock<IBookingRepository>();
            var mockConfig = new Mock<IConfiguration>();
            var service = new BookingService(mockRepo.Object, mockConfig.Object);

            var req = new PublicBookingRequest { Email = "invalid", Phone = "0901234567", DoctorId = 1, Date = DateOnly.FromDateTime(DateTime.Now), AppointHour = new TimeOnly(9, 0) };

            await FluentActions.Invoking(() => service.PublicBookAsync(req, CancellationToken.None))
                .Should().ThrowAsync<AppException>()
                .WithMessage("*Email không hợp lệ*");
        }

        // 2. Invalid Phone _ sdt sai
        [Test]
        public async Task PublicBookAsync_InvalidPhone_ThrowsAppException()
        {
            var mockRepo = new Mock<IBookingRepository>();
            var mockConfig = new Mock<IConfiguration>();
            var service = new BookingService(mockRepo.Object, mockConfig.Object);

            var req = new PublicBookingRequest { Email = "a@a.com", Phone = "abc", DoctorId = 1, Date = DateOnly.FromDateTime(DateTime.Now), AppointHour = new TimeOnly(9, 0) };

            await FluentActions.Invoking(() => service.PublicBookAsync(req, CancellationToken.None))
                .Should().ThrowAsync<AppException>()
                .WithMessage("*Số điện thoại không hợp lệ*");
        }

        //3. Date < Today _ ngày cũ
        [Test]
        public async Task PublicBookAsync_PastDate_ThrowsAppException()
        {
            var mockRepo = new Mock<IBookingRepository>();
            var mockConfig = new Mock<IConfiguration>();
            var service = new BookingService(mockRepo.Object, mockConfig.Object);

            var req = new PublicBookingRequest { Email = "a@a.com", Phone = "0901234567", DoctorId = 1, Date = DateOnly.FromDateTime(DateTime.Now.AddDays(-1)), AppointHour = new TimeOnly(9, 0) };

            await FluentActions.Invoking(() => service.PublicBookAsync(req, CancellationToken.None))
                .Should().ThrowAsync<AppException>()
                .WithMessage("*phải từ hôm nay trở đi*");
        }

        // 4. Invalid gender _ sai gender
        [Test]
        public async Task PublicBookAsync_InvalidGender_ThrowsAppException()
        {
            var mockRepo = new Mock<IBookingRepository>();
            var mockConfig = new Mock<IConfiguration>();
            var service = new BookingService(mockRepo.Object, mockConfig.Object);

            var req = new PublicBookingRequest { Email = "a@a.com", Phone = "0901234567", Gender = "Other", DoctorId = 1, Date = DateOnly.FromDateTime(DateTime.Now), AppointHour = new TimeOnly(9, 0) };

            await FluentActions.Invoking(() => service.PublicBookAsync(req, CancellationToken.None))
                .Should().ThrowAsync<AppException>()
                .WithMessage("*Male hoặc Female*");
        }

        // 5. Schedule Not Found _ ko có schedule
        [Test]
        public async Task PublicBookAsync_NoSchedule_ThrowsAppException()
        {
            // Arrange
            var mockRepo = new Mock<IBookingRepository>();
            var mockConfig = new Mock<IConfiguration>();
            var service = new BookingService(mockRepo.Object, mockConfig.Object);

            mockRepo.Setup(r => r.FindScheduleAsync(
                        It.IsAny<int>(),
                        It.IsAny<DateOnly>(),
                        It.IsAny<CancellationToken>()))
                .ReturnsAsync((ScheduleInfo?)null); // ✅ không có schedule

            var req = new PublicBookingRequest
            {
                Email = "a@b.com",
                Phone = "0901234567",
                DoctorId = 1,
                Date = DateOnly.FromDateTime(DateTime.Now),
                AppointHour = new TimeOnly(9, 0)
            };

            // Act & Assert
            await FluentActions.Invoking(() => service.PublicBookAsync(req, CancellationToken.None))
                .Should().ThrowAsync<AppException>()
                .WithMessage("*Không có ca làm việc*");
        }

        // 6. Appointment Hour Out of Range _ giờ ngoài khung
        [Test]
        public async Task PublicBookAsync_AppointHourOutOfRange_ThrowsAppException() 
        {
            // Khởi tạo dữ liệu giả (mock schedule)
            var schedule = new ScheduleInfo(
                1,                                // ScheduleId
                1,                                // DoctorId
                "Dr A",                           // DoctorName
                DateOnly.FromDateTime(DateTime.Now), // Date
                new TimeOnly(8, 0),               // StartTime
                new TimeOnly(12, 0)               // EndTime
            );

            // Khi BookingService gọi FindScheduleAsync(...), mock sẽ trả về schedule ở trên _ Mock Repository
            var mockRepo = new Mock<IBookingRepository>();
            mockRepo.Setup(r => r.FindScheduleAsync(
                It.IsAny<int>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()
            )).ReturnsAsync(schedule);

            // Mock IsSlotAvailableAsync to return true (Available), 
            // the logic in PublicBookAsync should check if AppointHour is out of range 
            // using StartTime/EndTime of the ScheduleInfo object.
            // Since this test is about 'Out of Range', we are mocking the schedule.

            // Mock config và tạo service thật
            var mockConfig = new Mock<IConfiguration>();
            var service = new BookingService(mockRepo.Object, mockConfig.Object);

            // Set AppointHour outside the range (e.g., 7:00, while StartTime is 8:00)
            var req = new PublicBookingRequest
            {
                Email = "a@a.com",
                Phone = "0901234567",
                DoctorId = 1,
                Date = DateOnly.FromDateTime(DateTime.Now),
                AppointHour = new TimeOnly(7, 0) // Giờ ngoài khung 8:00 - 12:00
            };

            await FluentActions.Invoking(() => service.PublicBookAsync(req, CancellationToken.None))
                .Should().ThrowAsync<AppException>()
                .WithMessage("Giờ hẹn nằm ngoài khung giờ làm việc"); // Cập nhật message cho phù hợp với logic 'Out of Range'
        }

        // 7. Slot Already Taken _ slot đã có người đặt (Lỗi CS0111)
        [Test]
        public async Task PublicBookAsync_SlotAlreadyTaken_ThrowsAppException() // Đổi tên để tránh CS0111
        {
            var schedule = new ScheduleInfo(
                1,                                // ScheduleId
                1,                                // DoctorId
                "Dr A",                           // DoctorName
                DateOnly.FromDateTime(DateTime.Now), // Date
                new TimeOnly(8, 0),               // StartTime
                new TimeOnly(12, 0)               // EndTime
            );

            var mockRepo = new Mock<IBookingRepository>();
            mockRepo.Setup(r => r.FindScheduleAsync(
                It.IsAny<int>(),
                It.IsAny<DateOnly>(),
                It.IsAny<CancellationToken>()
            )).ReturnsAsync(schedule);

            // Mock IsSlotAvailableAsync to return false (Not Available)
            mockRepo.Setup(r => r.IsSlotAvailableAsync(
                schedule.ScheduleId, // Sử dụng ScheduleId từ ScheduleInfo đã setup
                It.IsAny<TimeOnly>(),
                It.IsAny<CancellationToken>()
            )).ReturnsAsync(false);

            var mockConfig = new Mock<IConfiguration>();
            var service = new BookingService(mockRepo.Object, mockConfig.Object);

            var req = new PublicBookingRequest
            {
                Email = "a@a.com",
                Phone = "0901234567",
                DoctorId = 1,
                Date = DateOnly.FromDateTime(DateTime.Now),
                AppointHour = new TimeOnly(9, 0) // Giờ trong khung
            };

            await FluentActions.Invoking(() => service.PublicBookAsync(req, CancellationToken.None))
                .Should().ThrowAsync<AppException>()
                .WithMessage("*đã có người đặt*");
        }
    }
}