// Magic. Don't touch
using BookMyDoctor_WebAPI.Helpers;           // PasswordHasher
using BookMyDoctor_WebAPI.RequestModel;      // RegisterRequest
using BookMyDoctor_WebAPI.Services.Register; // IRegisterService, RegisterOutcome
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RegisterController : ControllerBase
    {
        private static readonly string[] _fieldPriority = new[]
        {
            nameof(RegisterRequest.Username),
            nameof(RegisterRequest.Password),
            nameof(RegisterRequest.ConfirmPassword),
            nameof(RegisterRequest.Email),
            nameof(RegisterRequest.Phone)
        };

        private readonly IRegisterService _service;
        public RegisterController(IRegisterService service) => _service = service;

        [HttpPost("user")]
        public async Task<IActionResult> RegisterUser([FromBody] RegisterRequest r, CancellationToken ct)
        {
            // 1) Validate — chỉ trả 1 lỗi {field, message}
            var errors = Validate(r);
            if (errors.Count > 0)
            {
                var first = PickFirstError(errors);
                return BadRequest(new { field = first.Key, message = first.Value });
            }

            // 2) Chuẩn hoá dữ liệu đầu vào
            string username = r.Username.Trim();
            string phone = (r.Phone ?? string.Empty).Trim();
            string? email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email!.Trim().ToLowerInvariant();

            // 3) Băm mật khẩu (SHA-512)
            var (hash, salt) = PasswordHasher.CreateHashSha512(r.Password);

            // 4) Gọi Service
            var result = await _service.CreateUserAsync(username, email, phone, hash, salt, "R03", ct);

            // 5) Map outcome → HTTP + body (đồng nhất {field, message} cho lỗi)
            return result.Outcome switch
            {
                RegisterOutcome.Success => Created(string.Empty, new
                {
                    userId = result.UserId,
                    username,
                    email,
                    phone,
                    roleId = "R03",
                    message = "Đăng ký thành công."
                }),

                RegisterOutcome.UsernameExists => Conflict(new { field = "username", message = "Tên đăng nhập đã tồn tại." }),
                RegisterOutcome.EmailExists => Conflict(new { field = "email", message = "Email đã tồn tại." }),
                RegisterOutcome.PhoneExists => Conflict(new { field = "phone", message = "Số điện thoại đã tồn tại." }),

                RegisterOutcome.InvalidEmail => BadRequest(new { field = "email", message = "Email không hợp lệ (chỉ chấp nhận @gmail.com, đúng quy tắc Gmail)." }),
                RegisterOutcome.InvalidUsername => BadRequest(new { field = "username", message = "Tên đăng nhập không hợp lệ." }),

                _ => StatusCode(500, new { field = "general", message = "Lỗi không xác định." })
            };
        }

        // ===== Validator ngắn gọn (trả về dictionary field->message) =====
        private static Dictionary<string, string> Validate(RegisterRequest r)
        {
            var e = new Dictionary<string, string>();

            // Username
            if (string.IsNullOrWhiteSpace(r.Username) || r.Username.Length < 4 || r.Username.Length > 100)
            {
                e[nameof(r.Username)] = "Tên đăng nhập phải từ 4–100 ký tự.";
            }
            else if (!Regex.IsMatch(r.Username.Trim(), @"^[a-zA-Z0-9_]+$"))
            {
                e[nameof(r.Username)] = "Tên đăng nhập chỉ cho phép chữ, số, và dấu gạch dưới (_), không khoảng trắng.";
            }

            // Password
            if (string.IsNullOrWhiteSpace(r.Password) || r.Password.Length < 8 || r.Password.Length > 100)
                e[nameof(r.Password)] = "Mật khẩu phải từ 8–100 ký tự.";

            // Confirm
            if (string.IsNullOrWhiteSpace(r.ConfirmPassword) || r.Password != r.ConfirmPassword)
                e[nameof(r.ConfirmPassword)] = "Mật khẩu xác nhận không trùng khớp.";

            // Email (nullable) – nếu có thì chỉ nhận đúng Gmail
            if (!string.IsNullOrWhiteSpace(r.Email))
            {
                var email = r.Email.Trim().ToLowerInvariant();

                // Domain phải đúng @gmail.com
                if (!email.EndsWith("@gmail.com", StringComparison.Ordinal))
                {
                    e[nameof(r.Email)] = "Chỉ chấp nhận email đuôi @gmail.com.";
                }
                else
                {
                    // Tách local-part
                    var at = email.LastIndexOf('@');
                    var local = email[..at];

                    // Luật Gmail:
                    // - hợp lệ: a-z, 0-9, ., +
                    // - không bắt đầu/kết thúc bằng '.'
                    // - không chứa ".."
                    // - 1..64 ký tự
                    var localOk =
                        local.Length >= 1 && local.Length <= 64 &&
                        !local.StartsWith('.') && !local.EndsWith('.') &&
                        !local.Contains("..") &&
                        Regex.IsMatch(local, @"^[a-z0-9.+]+$");

                    if (!localOk)
                        e[nameof(r.Email)] = "Email không hợp lệ theo định dạng Gmail.";
                }

                // Tổng độ dài an toàn
                if (email.Length > 250)
                    e[nameof(r.Email)] = "Email quá dài (tối đa 250 ký tự).";
            }

            // Phone (nullable)
            if (!string.IsNullOrWhiteSpace(r.Phone))
            {
                var p = r.Phone.Trim();
                if (p.Length < 9 || p.Length > 15 || !Regex.IsMatch(p, @"^\+?\d{9,15}$"))
                    e[nameof(r.Phone)] = "Số điện thoại chỉ gồm 9–15 chữ số (có thể có + ở đầu).";
            }

            return e;
        }

        // Lấy lỗi ưu tiên đầu tiên để trả về dạng {field, message}
        private static KeyValuePair<string, string> PickFirstError(Dictionary<string, string> errors)
        {
            foreach (var f in _fieldPriority)
            {
                if (errors.TryGetValue(f, out var msg))
                    return new KeyValuePair<string, string>(ToJsonFieldName(f), msg);
            }
            // fallback: lấy cặp đầu tiên bất kỳ
            var kv = errors.First();
            return new KeyValuePair<string, string>(ToJsonFieldName(kv.Key), kv.Value);
        }

        // Convert tên property C# sang tên field JSON FE đang dùng (nếu cần)
        private static string ToJsonFieldName(string propertyName)
        {
            // Ở đây FE đang dùng camelCase: username, password, confirmPassword, email, phone
            return propertyName switch
            {
                nameof(RegisterRequest.Username) => "username",
                nameof(RegisterRequest.Password) => "password",
                nameof(RegisterRequest.ConfirmPassword) => "confirmPassword",
                nameof(RegisterRequest.Email) => "email",
                nameof(RegisterRequest.Phone) => "phone",
                _ => propertyName
            };
        }
    }
}