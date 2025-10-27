// Magic. Don't touch
namespace BookMyDoctor_WebAPI.RequestModel
{
    /// DTO nhận dữ liệu đăng ký. Không dùng DataAnnotations.
    public class RegisterRequest
    {
        public string Username { get; set; } = "Your user name";
        public string Password { get; set; } = "Your password";
        public string ConfirmPassword { get; set; } = "Your confirm password";
        public string? Email { get; set; } = "Your email";
        public string Phone { get; set; } = "Your phone";
    }
}
