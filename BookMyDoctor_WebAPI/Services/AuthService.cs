// Magic. Don't touch
// Services/AuthService.cs
using BookMyDoctor_WebAPI.Data.Repositories;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading;
using System.Threading.Tasks;
using BookMyDoctor_WebAPI.Helpers;
using static BookMyDoctor_WebAPI.Data.Repositories.AuthRepository;

namespace BookMyDoctor_WebAPI.Services
{
    // ====================== RESULT MODELS (service -> controller) ======================
    public enum AuthError { None = 0, MissingInput, NotFound, InvalidCredential, InvalidToken, WeakPassword, Internal,
        BadRequest,
        ServerError
    }

    public sealed record ServiceResult<T>(bool Success, T? Data, AuthError Error, string? Message)
    {
        public static ServiceResult<T> Ok(T data) => new(true, data, AuthError.None, null);
        public static ServiceResult<T> Fail(AuthError err, string msg) => new(false, default, err, msg);
    }

    public sealed record LoginResult(User User, bool Upgraded);
    public sealed record EmptyResult;

    // ====================== INTERFACES ======================
    public interface IAuthService
    {
        Task<ServiceResult<LoginResult>> ValidateLoginAsync(LoginRequest req, CancellationToken ct = default);
        Task<ServiceResult<EmptyResult>> ChangePasswordAsync(int userId, ChangePasswordRequest req, CancellationToken ct = default);
        Task<ServiceResult<EmptyResult>> SendOtpAsync(OtpRequest req, CancellationToken ct = default);
        Task<ServiceResult<EmptyResult>> VerifyOtpAndResetPasswordAsync(VerifyOtpRequest req, CancellationToken ct = default);
    }

    public sealed record ResetPayload(int UserId, string PwdFingerprint);

    // ====================== IMPLEMENTATION ======================
    public sealed class AuthService : IAuthService
    {
        private readonly IAuthRepository _repo;
        private readonly IPasswordHasher _hasher;          // <-- dùng interface (password + OTP)
        private readonly ITimeLimitedToken _timeToken;
        private readonly IOtpRepository _otpRepo;
        private readonly IConfiguration _config;

        private static readonly TimeZoneInfo _tzVN =
            (Environment.OSVersion.Platform == PlatformID.Win32NT)
            ? TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")
            : TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

        public AuthService(
            IAuthRepository repo,
            IPasswordHasher hasher,
            ITimeLimitedToken timeToken,
            IOtpRepository otpRepo,
            IConfiguration config)
        {
            _repo = repo;
            _hasher = hasher;
            _timeToken = timeToken;
            _otpRepo = otpRepo;
            _config = config;
        }

        // ---------------------- LOGIN ----------------------
        public async Task<ServiceResult<LoginResult>> ValidateLoginAsync(LoginRequest req, CancellationToken ct = default)
        {
            if (req is null || string.IsNullOrWhiteSpace(req.UsernameOrPhoneOrEmail) || string.IsNullOrWhiteSpace(req.Password))
                return ServiceResult<LoginResult>.Fail(AuthError.MissingInput, "Thiếu thông tin đăng nhập.");

            var user = await _repo.FindByLoginKeyAsync(req.UsernameOrPhoneOrEmail.Trim(), ct);
            if (user is null)
                return ServiceResult<LoginResult>.Fail(AuthError.NotFound, "Tài khoản không tồn tại.");

            var ok = _hasher.Verify(req.Password, user.PasswordSalt, user.PasswordHash, out bool needsUpgrade);
            if (!ok)
                return ServiceResult<LoginResult>.Fail(AuthError.InvalidCredential, "Tài khoản hoặc mật khẩu không đúng.");

            var upgraded = false;
            if (needsUpgrade)
            {
                var (newHash, newSalt) = _hasher.UpgradeToSha512(req.Password);
                user.PasswordHash = newHash;
                user.PasswordSalt = newSalt;
                await _repo.UpdateUserAsync(user, ct);
                upgraded = true;
            }

            return ServiceResult<LoginResult>.Ok(new LoginResult(user, upgraded));
        }

        // ------------------ CHANGE PASSWORD ------------------
        public async Task<ServiceResult<EmptyResult>> ChangePasswordAsync(int userId, ChangePasswordRequest req, CancellationToken ct = default)
        {
            // --- CÁC KIỂM TRA VALIDATION MỚI ---
            if (req is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu dữ liệu đầu vào.");

            if (string.IsNullOrWhiteSpace(req.NewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mật khẩu mới.");

            if (string.IsNullOrWhiteSpace(req.ConfirmNewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Vui lòng nhập mật khẩu xác nhận.");

            if (req.NewPassword.Length < 6)
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới phải ≥ 6 ký tự.");

            if (req.NewPassword != req.ConfirmNewPassword)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidCredential, "Mật khẩu mới và mật khẩu xác nhận không khớp.");
            // --- KẾT THÚC KIỂM TRA MỚI ---

            var user = await _repo.FindByIdAsync(userId, ct);
            if (user is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.NotFound, "Không tìm thấy người dùng.");

            var ok = _hasher.Verify(req.CurrentPassword ?? string.Empty, user.PasswordSalt, user.PasswordHash, out _);
            if (!ok)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidCredential, "Mật khẩu hiện tại không đúng.");

            // --- KIỂM TRA LOGIC BỔ SUNG ---
            // Ngăn người dùng đặt mật khẩu mới giống hệt mật khẩu cũ
            if (req.CurrentPassword == req.NewPassword)
            {
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới không được trùng với mật khẩu cũ.");
            }
            // --- KẾT THÚC KIỂM TRA BỔ SUNG ---

            var (hash, salt) = _hasher.CreateHash(req.NewPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            await _repo.UpdateUserAsync(user, ct);

            // Gửi email thông báo (không làm hỏng flow nếu lỗi)
            try
            {
                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    var whenVn = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tzVN);
                    using var client = new System.Net.Mail.SmtpClient
                    {
                        Host = _config["Smtp:Host"]!,
                        Port = int.Parse(_config["Smtp:Port"]!),
                        EnableSsl = bool.Parse(_config["Smtp:EnableSsl"]!),
                        Credentials = new System.Net.NetworkCredential(_config["Smtp:User"], _config["Smtp:Password"])
                    };
                    var mail = new System.Net.Mail.MailMessage
                    {
                        From = new System.Net.Mail.MailAddress(_config["Smtp:User"]!, "BookMyDoctor System"),
                        Subject = "Xác nhận: Bạn vừa đổi mật khẩu",
                        IsBodyHtml = true,
                        Body = $"<p>Xin chào {System.Net.WebUtility.HtmlEncode(user.Username)},</p>" +
                               $"<p>Mật khẩu của bạn đã được cập nhật lúc <b>{whenVn:HH:mm:ss dd/MM/yyyy} (GMT+7)</b>.</p>" +
                               $"<p>Nếu không phải bạn thực hiện, vui lòng liên hệ hỗ trợ ngay.</p>"
                    };
                    mail.To.Add(user.Email);
                    await client.SendMailAsync(mail, ct);
                }
            }
            catch { /* log nếu cần */ }

            return ServiceResult<EmptyResult>.Ok(new EmptyResult());
        }

        // ------------------------ OTP SEND ------------------------
        public async Task<ServiceResult<EmptyResult>> SendOtpAsync(OtpRequest req, CancellationToken ct = default)
        {
            if (req is null || string.IsNullOrWhiteSpace(req.Destination))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu thông tin gửi OTP.");

            var dest = req.Destination.Trim();
            var user = await _repo.FindByLoginKeyAsync(dest, ct);
            if (user is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.NotFound, "Không tìm thấy người dùng.");

            var otp = Random.Shared.Next(100000, 999999).ToString();
            var otpHash = _hasher.HashString(otp); // dùng IPasswordHasher cho OTP (SHA-512 hex)
            var now = DateTimeOffset.UtcNow;
            var ticket = new OtpTicket
            {
                UserId = user.UserId,
                Purpose = req.Purpose,
                Channel = req.Channel,
                OtpHash = otpHash,
                Destination = dest,
                CreatedAtUtc = now,
                ExpireAtUtc = now.AddMinutes(5),
                Attempts = 0,
                Used = false
            };
            await _otpRepo.AddAsync(ticket, ct);

            // Gửi email OTP
            try
            {
                using var client = new System.Net.Mail.SmtpClient
                {
                    Host = _config["Smtp:Host"]!,
                    Port = int.Parse(_config["Smtp:Port"]!),
                    EnableSsl = bool.Parse(_config["Smtp:EnableSsl"]!),
                    Credentials = new System.Net.NetworkCredential(_config["Smtp:User"], _config["Smtp:Password"])
                };
                var expiredVn = TimeZoneInfo.ConvertTime(ticket.ExpireAtUtc, _tzVN);
                var mail = new System.Net.Mail.MailMessage
                {
                    From = new System.Net.Mail.MailAddress(_config["Smtp:User"]!, "BookMyDoctor System"),
                    Subject = "Mã OTP đặt lại mật khẩu của bạn",
                    IsBodyHtml = true,
                    Body = $"<p>Xin chào,</p><p>Mã OTP hiện tại của bạn là: <b>{otp}</b>.</p>" +
                           $"<p>Hết hạn lúc (giờ Việt Nam): <b>{expiredVn:HH:mm:ss dd/MM/yyyy}</b>.</p>"
                };
                mail.To.Add(dest);
                await client.SendMailAsync(mail, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SMTP ERROR] {ex.Message}");
                return ServiceResult<EmptyResult>.Fail(AuthError.Internal, "Không gửi được email OTP.");
            }

            return ServiceResult<EmptyResult>.Ok(new EmptyResult());
        }

        // -------------------- OTP VERIFY + RESET --------------------
        public async Task<ServiceResult<EmptyResult>> VerifyOtpAndResetPasswordAsync(VerifyOtpRequest req, CancellationToken ct = default)
        {
            // --- CÁC KIỂM TRA VALIDATION MỚI ---
            if (req is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu dữ liệu đầu vào.");

            if (string.IsNullOrWhiteSpace(req.Destination))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu destination (email/SĐT).");

            if (string.IsNullOrWhiteSpace(req.OtpCode))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mã OTP.");

            if (string.IsNullOrWhiteSpace(req.NewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mật khẩu mới.");

            if (string.IsNullOrWhiteSpace(req.ConfirmNewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Vui lòng nhập mật khẩu xác nhận.");

            if (req.NewPassword.Length < 6)
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới phải ≥ 6 ký tự.");

            if (req.NewPassword != req.ConfirmNewPassword)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidCredential, "Mật khẩu mới và mật khẩu xác nhận không khớp.");
            // --- KẾT THÚC KIỂM TRA MỚI ---

            var dest = req.Destination.Trim();
            var otpTicket = await _otpRepo.GetLatestValidAsync(dest, req.Purpose, ct);
            if (otpTicket is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.NotFound, "Không tìm thấy OTP hợp lệ.");
            if (DateTimeOffset.UtcNow > otpTicket.ExpireAtUtc)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "Mã OTP đã hết hạn.");
            if (otpTicket.Attempts >= 5)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "Nhập sai OTP quá nhiều.");

            if (!_hasher.VerifyString(req.OtpCode, otpTicket.OtpHash)) // dùng IPasswordHasher cho OTP
            {
                otpTicket.Attempts++;
                await _otpRepo.UpdateAsync(otpTicket, ct);
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "Mã OTP không chính xác.");
            }

            var user = await _repo.FindByLoginKeyAsync(dest, ct);
            if (user is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.NotFound, "Không tìm thấy người dùng.");

            // --- KIỂM TRA MẬT KHẨU MỚI TRÙNG MẬT KHẨU CŨ ---
            // Sau khi đã xác thực OTP thành công, kiểm tra xem mật khẩu mới có trùng mật khẩu cũ không
            var isSameAsOld = _hasher.Verify(req.NewPassword, user.PasswordSalt, user.PasswordHash, out _);
            if (isSameAsOld)
            {
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới không được trùng với mật khẩu cũ.");
            }
            // --- KẾT THÚC KIỂM TRA ---

            var (hash, salt) = _hasher.CreateHash(req.NewPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            otpTicket.Used = true; // Đánh dấu OTP này đã được sử dụng

            await _repo.UpdateUserAsync(user, ct);
            await _otpRepo.UpdateAsync(otpTicket, ct);

            // Email xác nhận
            try
            {
                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    var whenVn = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tzVN);
                    using var client = new System.Net.Mail.SmtpClient
                    {
                        Host = _config["Smtp:Host"]!,
                        Port = int.Parse(_config["Smtp:Port"]!),
                        EnableSsl = bool.Parse(_config["Smtp:EnableSsl"]!),
                        Credentials = new System.Net.NetworkCredential(_config["Smtp:User"], _config["Smtp:Password"])
                    };
                    var mail = new System.Net.Mail.MailMessage
                    {
                        From = new System.Net.Mail.MailAddress(_config["Smtp:User"]!, "BookMyDoctor System"),
                        Subject = "Xác nhận: Mật khẩu của bạn đã được thay đổi",
                        IsBodyHtml = true,
                        Body = $"<p>Xin chào {System.Net.WebUtility.HtmlEncode(user.Username)},</p>" +
                               $"<p>Mật khẩu của bạn đã được cập nhật (đặt lại) lúc <b>{whenVn:HH:mm:ss dd/MM/yyyy} (GMT+7)</b>.</p>" +
                               $"<p>Nếu không phải bạn thực hiện, vui lòng liên hệ hỗ trợ ngay.</p>"
                    };
                    mail.To.Add(user.Email);
                    await client.SendMailAsync(mail, ct);
                }
            }
            catch { /* log nếu cần */ }

            return ServiceResult<EmptyResult>.Ok(new EmptyResult());
        }
    }
}
