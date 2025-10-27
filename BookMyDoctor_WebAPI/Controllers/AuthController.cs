//Magic. Don't touch
// Controllers/AuthController.cs
using BookMyDoctor_WebAPI.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
//using Microsoft.IdentityModel.Tokens;
//using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
//using System.Text;


namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class AuthController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly IAuthService _svc;
        public AuthController(IAuthService svc, IConfiguration config)
        {
            _config = config;
            _svc = svc;
        }

        // Chuẩn hoá lỗi JSON
        private IActionResult DetailError(string code, string message, string? detail = null, int status = 400)
            => StatusCode(status, new { error = new { code, message, detail } });

        // Map lỗi từ ServiceResult<AuthError>
        private IActionResult MapError(AuthError err, string? message)
            => err switch
            {
                AuthError.MissingInput => DetailError("common.validation_error", message ?? "Thiếu dữ liệu.", null, (int)HttpStatusCode.BadRequest),
                AuthError.NotFound => DetailError("common.not_found", message ?? "Không tìm thấy.", null, (int)HttpStatusCode.NotFound),
                AuthError.InvalidCredential => DetailError("auth.invalid_credential", message ?? "Tài khoản hoặc mật khẩu không đúng.", null, (int)HttpStatusCode.Unauthorized),
                AuthError.InvalidToken => DetailError("auth.invalid_token", message ?? "Token không hợp lệ hoặc đã hết hạn.", null, (int)HttpStatusCode.Unauthorized),
                AuthError.WeakPassword => DetailError("common.validation_error", message ?? "Mật khẩu quá yếu.", null, (int)HttpStatusCode.BadRequest),
                _ => DetailError("common.internal_error", "Đã có lỗi xảy ra.", message, (int)HttpStatusCode.InternalServerError)
            };

        // Dự phòng: map Exception bất ngờ
        private IActionResult MapException(Exception ex) =>
            DetailError("common.internal_error", "Đã có lỗi xảy ra.", ex.Message, (int)HttpStatusCode.InternalServerError);

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct = default)
        {
            try
            {
                var rs = await _svc.ValidateLoginAsync(req, ct); // ServiceResult<LoginResult>
                if (!rs.Success) return MapError(rs.Error, rs.Message);

                var user = rs.Data!.User;

                // PHẢI convert sang string, tránh lỗi overload Claim(BinaryReader)
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                    new Claim(ClaimTypes.Name, user.Username ?? string.Empty),
                    new Claim(ClaimTypes.Role, $"{user.RoleId}")
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                // ---------------
                //var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
                //var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                //var token = new JwtSecurityToken(
                //    issuer: _config["Jwt:Issuer"],
                //    audience: _config["Jwt:Audience"],
                //    claims: claims,
                //    expires: DateTime.UtcNow.AddHours(1),
                //    signingCredentials: creds);
                //------------------------
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true,
                        ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30),
                        AllowRefresh = true
                    });

                return Ok(
                    new
                    {
                        message = "Đăng nhập thành công!",
                        //token = new JwtSecurityTokenHandler().WriteToken(token),
                        //expiration = token.ValidTo
                    });
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

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct = default)
        {
            try
            {
                var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";
                if (!int.TryParse(userIdStr, out var userId))
                    return DetailError("auth.unauthorized", "Không xác định được người dùng.", null, 401);
                if (req.NewPassword != req.ConfirmNewPassword)
                {
                    // Nếu không khớp, trả về lỗi 400 Bad Request
                    return DetailError("auth.passwordMismatch", "Mật khẩu xác nhận không khớp.", null, 400);
                }
                var rs = await _svc.ChangePasswordAsync(userId, req, ct); // ServiceResult<EmptyResult>
                if (!rs.Success) return MapError(rs.Error, rs.Message);

                return NoContent();
            }
            catch (Exception ex) { return MapException(ex); }
        }
        // Gửi OTP (quên mật khẩu qua OTP)
        [HttpPost("request-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> RequestOtp([FromBody] OtpRequest req, CancellationToken ct = default)
        {
            try
            {
                var rs = await _svc.SendOtpAsync(req, ct); // ServiceResult<EmptyResult>
                if (!rs.Success) return MapError(rs.Error, rs.Message);

                // có thể dùng 202 Accepted nếu bạn queue gửi email/sms
                return Ok(new { message = "Mã OTP đã được gửi." });
            }
            catch (Exception ex) { return MapException(ex); }
        }

        // Xác thực OTP và đổi mật khẩu
        [HttpPost("reset-password-otp")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPasswordByOtp([FromBody] VerifyOtpRequest req, CancellationToken ct = default)
        {
            try
            {
                var rs = await _svc.VerifyOtpAndResetPasswordAsync(req, ct); // ServiceResult<EmptyResult>
                if (!rs.Success) return MapError(rs.Error, rs.Message);
                if (req.NewPassword != req.ConfirmNewPassword)
                {
                    // Nếu không khớp, trả về lỗi 400 Bad Request
                    return DetailError("auth.passwordMismatch", "Mật khẩu xác nhận không khớp.", null, 400);
                }
                return NoContent();
            }
            catch (Exception ex) { return MapException(ex); }
        }

        [HttpGet("unauthorized")]
        [AllowAnonymous]
        public IActionResult UnauthorizedEndpoint()
            => DetailError("auth.unauthorized", "Bạn chưa đăng nhập hoặc phiên đã hết hạn.", null, 401);

        [HttpGet("me")]
        [Authorize]
        public IActionResult Me()
        {
            var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var name = User.FindFirstValue(ClaimTypes.Name);
            var role = User.FindFirstValue(ClaimTypes.Role);
            return Ok(new { userId = id, username = name, role });
        }
        // Check role of current signed-in user
        [HttpGet("check-role")]
        [Authorize]
        public IActionResult CheckRole()
        {
            // Lấy thông tin từ Claims
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = User.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
            var roleClaim = User.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

            // Chuẩn hoá roleId về dạng Rxx
            // - Nếu claim đã là "R03" -> giữ nguyên (upper)
            // - Nếu claim là số "3" -> đổi thành "R03"
            string roleId = roleClaim.StartsWith("R", StringComparison.OrdinalIgnoreCase)
                ? roleClaim.ToUpperInvariant()
                : (int.TryParse(roleClaim, out var rid) ? $"R{rid:00}" : roleClaim);

            // Map roleId -> roleName
            string roleName = roleId switch
            {
                "R01" => "Admin",
                "R02" => "Doctor",
                "R03" => "Patient",
                _ => "Unknown"
            };

            return Ok(new
            {
                userId,
                username,
                roleId,
                roleName
            });
        }
    }
}
