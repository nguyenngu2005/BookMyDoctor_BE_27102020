// Magic. Don't touch
using BookMyDoctor_WebAPI.Helpers;          // PasswordHasher
using BookMyDoctor_WebAPI.RequestModel;     // RegisterRequest
using BookMyDoctor_WebAPI.Services.Register; // IRegisterService, RegisterOutcome
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RegisterController : ControllerBase
    {
        private readonly IRegisterService _service;
        public RegisterController(IRegisterService service) => _service = service;

        [HttpPost("user")]
        public async Task<IActionResult> RegisterUser([FromBody] RegisterRequest r, CancellationToken ct)
        {
            // 1) Validate (controller chịu trách nhiệm thông báo)
            var errors = Validate(r);
            if (errors.Count > 0)
                return BadRequest(new { message = "Xác thực thất bại.", errors });

            // Chuẩn hoá
            string username = r.Username.Trim();
            string phone = (r.Phone ?? string.Empty).Trim();
            string? email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email!.Trim();

            // 2) Băm mật khẩu tại Controller
            //    Dùng chuẩn mới: SHA-512(salt(32B) || UTF8(password)) → Hash 64B, Salt 32B
            var (hash, salt) = PasswordHasher.CreateHashSha512(r.Password);

            // 3) Gọi Service
            var result = await _service.CreateUserAsync(username, email, phone, hash, salt, "R03", ct);

            // 4) Map outcome → HTTP + message
            return result.Outcome switch
            {
                RegisterOutcome.Success => Created(string.Empty, new { userId = result.UserId, username, email, phone, roleId = "R03", message = "Đăng ký thành công." }),
                RegisterOutcome.UsernameExists => Conflict(new { field = "username", message = "Tên đăng nhập đã tồn tại." }),
                RegisterOutcome.EmailExists => Conflict(new { field = "email", message = "Email đã tồn tại." }),
                RegisterOutcome.PhoneExists => Conflict(new { field = "phone", message = "Số điện thoại đã tồn tại." }),
                _ => StatusCode(500, new { message = "Lỗi không xác định." })
            };
        }

        // ===== Validator ngắn gọn =====
        private static Dictionary<string, string> Validate(RegisterRequest r)
        {
            var e = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(r.Username) || r.Username.Length < 4 || r.Username.Length > 100)
                e[nameof(r.Username)] = "Tên đăng nhập phải từ 4–100 ký tự.";
            else
            {
                if (!Regex.IsMatch(r.Username.Trim(), @"^[a-zA-Z0-9_]+$"))
                    e[nameof(r.Username)] = "Tên đăng nhập chỉ cho phép chữ, số, và dấu gạch dưới (_), không khoảng trắng.";
            }
            if (string.IsNullOrWhiteSpace(r.Password) || r.Password.Length < 6 || r.Password.Length > 100)
                e[nameof(r.Password)] = "Mật khẩu phải từ 6–100 ký tự.";

            if (string.IsNullOrWhiteSpace(r.ConfirmPassword) || r.Password != r.ConfirmPassword)
                e[nameof(r.ConfirmPassword)] = "Mật khẩu xác nhận không trùng khớp.";

            if (!string.IsNullOrWhiteSpace(r.Email))
            {
                try
                {
                    var m = new System.Net.Mail.MailAddress(r.Email);
                    if (m.Address != r.Email || r.Email.Length > 250) e[nameof(r.Email)] = "Email không hợp lệ.";
                }
                catch { e[nameof(r.Email)] = "Email không hợp lệ."; }
            }

            if (!string.IsNullOrWhiteSpace(r.Phone))
            {
                var p = r.Phone.Trim();
                if (p.Length < 9 || p.Length > 15 || !Regex.IsMatch(p, @"^\+?\d{9,15}$"))
                    e[nameof(r.Phone)] = "Số điện thoại chỉ gồm 9–15 chữ số (có thể có + ở đầu).";
            }
            return e;
        }
    }
}
