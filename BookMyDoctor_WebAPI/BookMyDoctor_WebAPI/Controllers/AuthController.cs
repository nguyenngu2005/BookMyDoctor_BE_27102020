// Controllers/AuthController.cs
using BookMyDoctor_WebAPI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http; // CookieOptions, SameSiteMode
using System.Net;
using System.Security.Claims;

namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IAuthService _svc;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService svc, IConfiguration config, ILogger<AuthController> logger)
        {
            _config = config;
            _svc = svc;
            _logger = logger;
        }

        private IActionResult SimpleError(string field, string message, int status = 400)
            => StatusCode(status, new { field, message });

        private IActionResult MapError(AuthError err, string? message, string defaultField = "common")
            => err switch
            {
                AuthError.MissingInput => SimpleError(defaultField, message ?? "Thiếu dữ liệu.", (int)HttpStatusCode.BadRequest),
                AuthError.NotFound => SimpleError(defaultField, message ?? "Không tìm thấy.", (int)HttpStatusCode.NotFound),
                AuthError.InvalidCredential => SimpleError(defaultField, message ?? "Thông tin không hợp lệ.", (int)HttpStatusCode.Unauthorized),
                AuthError.InvalidToken => SimpleError(defaultField, message ?? "Mã/phiên không hợp lệ hoặc đã hết hạn.", (int)HttpStatusCode.Unauthorized),
                AuthError.WeakPassword => SimpleError("newPassword", message ?? "Mật khẩu quá yếu.", (int)HttpStatusCode.BadRequest),
                AuthError.AccountDisabled => SimpleError("account", message ?? "Tài khoản bị khóa hoặc không hoạt động.", (int)HttpStatusCode.Forbidden),
                AuthError.BadRequest => SimpleError(defaultField, message ?? "Yêu cầu không hợp lệ.", (int)HttpStatusCode.BadRequest),
                AuthError.ServerError => SimpleError("common", message ?? "Lỗi máy chủ.", (int)HttpStatusCode.InternalServerError),
                AuthError.Expired => SimpleError(defaultField, message ?? "Đã hết hạn.", (int)HttpStatusCode.Unauthorized),
                AuthError.Internal => SimpleError("common", message ?? "Lỗi máy chủ nội bộ.", (int)HttpStatusCode.InternalServerError),
                _ => SimpleError("common", "Đã có lỗi xảy ra.", (int)HttpStatusCode.InternalServerError)
            };

        private IActionResult MapException(Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in AuthController");
            return SimpleError("common", "Lỗi máy chủ nội bộ.", (int)HttpStatusCode.InternalServerError);
        }

        // ===================== LOGIN/LOGOUT =====================
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct = default)
        {
            try
            {
                var rs = await _svc.ValidateLoginAsync(req, ct);
                if (!rs.Success) return MapError(rs.Error, rs.Message, defaultField: "usernameOrPassword");

                var user = rs.Data!.User;
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new(ClaimTypes.Name, user.Username ?? string.Empty),
                    new(ClaimTypes.Role, $"{user.RoleId}")
                };
                var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                        AllowRefresh = true
                    });

                return Ok(new { message = "Đăng nhập thành công!" });
            }
            catch (Exception ex) { return MapException(ex); }
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return NoContent();
        }

        // ===================== CHANGE PASSWORD (AUTHED) =====================
        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct = default)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
                if (!int.TryParse(userIdStr, out var userId))
                    return SimpleError("auth", "Không xác định được người dùng.", 401);

                if (string.IsNullOrWhiteSpace(req.NewPassword) || string.IsNullOrWhiteSpace(req.ConfirmNewPassword))
                    return SimpleError("newPassword", "Thiếu mật khẩu mới.", 400);

                if (req.NewPassword != req.ConfirmNewPassword)
                    return SimpleError("confirmNewPassword", "Mật khẩu xác nhận không khớp.", 400);

                var rs = await _svc.ChangePasswordAsync(userId, req, ct);
                if (!rs.Success) return MapError(rs.Error, rs.Message, defaultField: "currentPassword");

                return NoContent();
            }
            catch (Exception ex) { return MapException(ex); }
        }

        // ===================== OTP FLOW (TÁCH BƯỚC) =====================
        // B1: request-otp  → gửi mã
        [HttpPost("request-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> RequestOtp([FromBody] OtpRequest req, CancellationToken ct = default)
        {
            try
            {
                var rs = await _svc.SendOtpAsync(req, ct);
                if (!rs.Success) return MapError(rs.Error, rs.Message, defaultField: "destination");
                return Ok(new { message = "Mã OTP đã được gửi." });
            }
            catch (Exception ex) { return MapException(ex); }
        }

        // B2: verify-otp  → xác thực mã, lưu otp_token vào HttpOnly cookie (FE không phải nhập)
        [HttpPost("verify-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpOnlyRequest req, CancellationToken ct = default)
        {
            try
            {
                var rs = await _svc.VerifyOtpAsync(req, ct);
                if (!rs.Success) return MapError(rs.Error, rs.Message, defaultField: "otpCode");

                var token = rs.Data!.OtpToken;

                // Nếu đang test qua HTTP (không HTTPS), cookie Secure=true sẽ không set được.
                // Dùng Request.IsHttps để tự động phù hợp môi trường dev/prod.
                Response.Cookies.Append("otp_token", token, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = Request.IsHttps,                 // true trên HTTPS, false khi dev HTTP
                    SameSite = SameSiteMode.Strict,          // đổi sang Lax/None nếu cần cross-site
                    Expires = DateTimeOffset.UtcNow.AddMinutes(10),
                    IsEssential = true
                });

                return Ok(new { message = "Xác thực OTP thành công." });
            }
            catch (Exception ex) { return MapException(ex); }
        }

        // B3: change-password-otp  → BE tự đọc otpToken từ cookie nếu body không có
        [HttpPost("change-password-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> ChangePasswordByOtp([FromBody] ChangePasswordByTokenRequest req, CancellationToken ct = default)
        {
            try
            {
                // Fallback: tự lấy từ cookie để người dùng KHÔNG phải nhập
                if (string.IsNullOrWhiteSpace(req.OtpToken))
                {
                    if (Request.Cookies.TryGetValue("otp_token", out var cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
                        req.OtpToken = cookieToken;
                    else
                        return SimpleError("otpToken", "Thiếu otpToken. Vui lòng xác thực OTP lại.", 400);
                }

                if (string.IsNullOrWhiteSpace(req.NewPassword) || string.IsNullOrWhiteSpace(req.ConfirmNewPassword))
                    return SimpleError("newPassword", "Thiếu mật khẩu mới.", 400);

                if (req.NewPassword != req.ConfirmNewPassword)
                    return SimpleError("confirmNewPassword", "Mật khẩu xác nhận không khớp.", 400);

                var rs = await _svc.ResetPasswordWithOtpTokenAsync(new OtpChangePasswordByTokenRequest
                {
                    OtpToken = req.OtpToken!,
                    NewPassword = req.NewPassword,
                    ConfirmNewPassword = req.ConfirmNewPassword
                }, ct);

                if (!rs.Success) return MapError(rs.Error, rs.Message, defaultField: "otpToken");

                // Xóa cookie sau khi dùng xong
                Response.Cookies.Delete("otp_token");

                return Ok(new { message = "Đổi mật khẩu thành công." });
            }
            catch (Exception ex) { return MapException(ex); }
        }

        // ===================== Utils =====================
        [HttpGet("unauthorized")]
        [AllowAnonymous]
        public IActionResult UnauthorizedEndpoint()
            => SimpleError("auth", "Bạn chưa đăng nhập hoặc phiên đã hết hạn.", 401);

        [HttpGet("check-role")]
        [Authorize]
        public IActionResult CheckRole()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
            var roleClaim = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            string roleId = roleClaim.StartsWith("R", StringComparison.OrdinalIgnoreCase)
                ? roleClaim.ToUpperInvariant()
                : (int.TryParse(roleClaim, out var rid) ? $"R{rid:00}" : roleClaim);

            string roleName = roleId switch
            {
                "R01" => "Admin",
                "R02" => "Doctor",
                "R03" => "Patient",
                _ => "Unknown"
            };

            return Ok(new { userId, username, roleId, roleName });
        }
    }

    // ====== REQUEST MODELS PHỤ CHO FLOW OTP (đặt ngay dưới controller) ======
    public sealed class VerifyOtpOnlyRequest
    {
        public string Destination { get; set; } = default!; // email
        public string OtpCode { get; set; } = default!;     // 6 digits
        public string Purpose { get; set; } = "RESET_PASSWORD";
        public string Channel { get; set; } = "EMAIL";
    }

    // OtpToken trở thành OPTIONAL với FE (BE sẽ tự lấy từ cookie nếu không gửi)
    public sealed class ChangePasswordByTokenRequest
    {
        public string? OtpToken { get; set; }
        public string NewPassword { get; set; } = default!;
        public string ConfirmNewPassword { get; set; } = default!;
    }
}
