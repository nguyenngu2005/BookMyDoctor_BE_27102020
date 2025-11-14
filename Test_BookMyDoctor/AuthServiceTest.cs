using BookMyDoctor_WebAPI.Data.Repositories;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.Services;
using Microsoft.Extensions.Configuration;
using Moq;
using static BookMyDoctor_WebAPI.Data.Repositories.AuthRepository;

namespace BookMyDoctor_WebAPI.Tests.Services
{
    [TestFixture]
    public class AuthServiceTests
    {
        //Giả lập truy vấn DB (trả về User)
        private Mock<IAuthRepository> _mockRepo;

        //Giả lập logic kiểm tra mật khẩu
        private Mock<IPasswordHasher> _mockHasher;

        //Giả lập config (SMTP, secret key)
        private Mock<IOtpRepository> _mockOtpRepo;
        private Mock<IConfiguration> _mockConfig;
        private AuthService _service;

        //Tạo lại các mock trước mỗi test
        [SetUp]
        public void Setup()
        {
            _mockRepo = new Mock<IAuthRepository>();
            _mockHasher = new Mock<IPasswordHasher>();
            _mockOtpRepo = new Mock<IOtpRepository>();
            _mockConfig = new Mock<IConfiguration>();

            // tạo config giả cho SMTP và Otp Secret
            _mockConfig.SetupGet(c => c["Otp:Secret"]).Returns("test_secret_key");
            _mockConfig.SetupGet(c => c["Smtp:User"]).Returns("no-reply@bookmydoctor.com");
            _mockConfig.SetupGet(c => c["Smtp:Password"]).Returns("password");
            _mockConfig.SetupGet(c => c["Smtp:Host"]).Returns("smtp.gmail.com");
            _mockConfig.SetupGet(c => c["Smtp:Port"]).Returns("587");
            _mockConfig.SetupGet(c => c["Smtp:EnableSsl"]).Returns("true");

            // khởi tạo service (tham số timeToken null, chỉ để tương thích DI)
            _service = new AuthService(_mockRepo.Object, _mockHasher.Object, null!, _mockOtpRepo.Object, _mockConfig.Object);
        }

        // ✅ Case 1: Đăng nhập hợp lệ
        [Test]
        public async Task ValidateLoginAsync_ValidCredentials_ReturnsSuccess()
        {
            //Encoding.UTF8.GetBytes() dùng để giả lập salt và hash dạng byte[].
            // Arrange
            var user = new User
            {
                UserId = 1,
                Username = "test",
                PasswordSalt = System.Text.Encoding.UTF8.GetBytes("salt"),
                PasswordHash = System.Text.Encoding.UTF8.GetBytes("hash"),
                IsActive = true
            };

            _mockRepo.Setup(r => r.FindByLoginKeyAsync("test", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

            _mockHasher.Setup(h => h.Verify(
                    "123456",
                    It.IsAny<byte[]>(),
                    It.IsAny<byte[]>(),
                    out It.Ref<bool>.IsAny)) //It.Ref<bool>.IsAny là cú pháp đặc biệt cho out parameter trong Moq.
                .Returns(true);

            var req = new LoginRequest { UsernameOrPhoneOrEmail = "test", Password = "123456" };

            // Act
            var result = await _service.ValidateLoginAsync(req);

            // Assert
            Assert.IsTrue(result.Success);
            Assert.NotNull(result.Data);
            Assert.That(result.Error, Is.EqualTo(AuthError.None));
        }

        // ❌ Case 2: Thiếu thông tin đăng nhập
        [Test]
        public async Task ValidateLoginAsync_MissingInput_ReturnsFail()
        {
            // Arrange
            var req = new LoginRequest { UsernameOrPhoneOrEmail = "", Password = "" };

            // Act
            var result = await _service.ValidateLoginAsync(req);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Is.EqualTo(AuthError.InvalidCredential));
        }

        // ❌ Case 3: Sai mật khẩu
        [Test]
        public async Task ValidateLoginAsync_InvalidPassword_ReturnsFail()
        {
            // Arrange
            var user = new User
            {
                UserId = 1,
                Username = "test",
                PasswordSalt = System.Text.Encoding.UTF8.GetBytes("salt"),
                PasswordHash = System.Text.Encoding.UTF8.GetBytes("hash"),
                IsActive = true
            };

            _mockRepo.Setup(r => r.FindByLoginKeyAsync("test", It.IsAny<CancellationToken>()))
                     .ReturnsAsync(user);

            // Giả lập hàm Verify trả về false vì sai mật khẩu
            _mockHasher.Setup(h => h.Verify(
                    "wrong",
                    It.IsAny<byte[]>(),
                    It.IsAny<byte[]>(),
                    out It.Ref<bool>.IsAny))
                .Returns(false);

            var req = new LoginRequest { UsernameOrPhoneOrEmail = "test", Password = "wrong" };

            // Act
            var result = await _service.ValidateLoginAsync(req);

            // Assert
            Assert.IsFalse(result.Success);
            Assert.That(result.Error, Is.EqualTo(AuthError.MissingInput));

        }
    }
}
