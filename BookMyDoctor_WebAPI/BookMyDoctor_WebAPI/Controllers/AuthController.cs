// Services/AuthService.cs
using BookMyDoctor_WebAPI.Controllers;
using BookMyDoctor_WebAPI.Data.Repositories;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using static BookMyDoctor_WebAPI.Data.Repositories.AuthRepository;

namespace BookMyDoctor_WebAPI.Services
{
    // ====================== RESULT MODELS (service -> controller) ======================
    public enum AuthError
    {
        None = 0, MissingInput, NotFound, InvalidCredential, InvalidToken, WeakPassword, Internal,
        BadRequest,
        ServerError,
        AccountDisabled,
        Expired
    }

    public sealed record ServiceResult<T>(bool Success, T? Data, AuthError Error, string? Message)
    {
        public static ServiceResult<T> Ok(T data) => new(true, data, AuthError.None, null);
        public static ServiceResult<T> Fail(AuthError err, string msg) => new(false, default, err, msg);
    }

    public sealed record LoginResult(User User, bool Upgraded);
    public sealed record EmptyResult;

    // ===== NEW MODELS for 2-step OTP =====
    public sealed record OtpVerifiedResult(string OtpToken);
    public sealed record OtpChangePasswordByTokenRequest
    {
        public string OtpToken { get; init; } = default!;
        public string NewPassword { get; init; } = default!;
        public string ConfirmNewPassword { get; init; } = default!;
    }

    public sealed record ResetPayload(int UserId, string PwdFingerprint);

    // ====================== INTERFACES ======================
    public interface IAuthService
    {
        Task<ServiceResult<LoginResult>> ValidateLoginAsync(LoginRequest req, CancellationToken ct = default);
        Task<ServiceResult<EmptyResult>> ChangePasswordAsync(int userId, ChangePasswordRequest req, CancellationToken ct = default);

        Task<ServiceResult<EmptyResult>> SendOtpAsync(OtpRequest req, CancellationToken ct = default);

        // Old combined flow (giữ để tương thích – không dùng cho UI tách bước)
        Task<ServiceResult<EmptyResult>> VerifyOtpAndResetPasswordAsync(VerifyOtpRequest req, CancellationToken ct = default);

        // New 2-step flow
        Task<ServiceResult<OtpVerifiedResult>> VerifyOtpAsync(VerifyOtpOnlyRequest req, CancellationToken ct = default);
        Task<ServiceResult<EmptyResult>> ResetPasswordWithOtpTokenAsync(OtpChangePasswordByTokenRequest req, CancellationToken ct = default);
    }

    // ====================== IMPLEMENTATION ======================
    public sealed class AuthService : IAuthService
    {
        private readonly IAuthRepository _repo;
        private readonly IPasswordHasher _hasher;
        private readonly IOtpRepository _otpRepo;
        private readonly IConfiguration _config;

        private static readonly TimeZoneInfo _tzVN =
            (Environment.OSVersion.Platform == PlatformID.Win32NT)
            ? TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")
            : TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

        public AuthService(
            IAuthRepository repo,
            IPasswordHasher hasher,
            ITimeLimitedToken timeToken, // kept for DI compatibility, not used below
            IOtpRepository otpRepo,
            IConfiguration config)
        {
            _repo = repo;
            _hasher = hasher;
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
            if (!user.IsActive)
                return ServiceResult<LoginResult>.Fail(AuthError.AccountDisabled, "Tài khoản không hoạt động.");

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

        // ------------------ CHANGE PASSWORD (AUTHED) ------------------
        public async Task<ServiceResult<EmptyResult>> ChangePasswordAsync(int userId, ChangePasswordRequest req, CancellationToken ct = default)
        {
            if (req is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu dữ liệu đầu vào.");
            if (string.IsNullOrWhiteSpace(req.NewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mật khẩu mới.");
            if (string.IsNullOrWhiteSpace(req.ConfirmNewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mật khẩu xác nhận.");
            if (req.NewPassword.Length < 6)
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới phải ≥ 6 ký tự.");
            if (req.NewPassword != req.ConfirmNewPassword)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidCredential, "Mật khẩu mới và xác nhận không khớp.");

            var user = await _repo.FindByIdAsync(userId, ct);
            if (user is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.NotFound, "Không tìm thấy người dùng.");

            var ok = _hasher.Verify(req.CurrentPassword ?? string.Empty, user.PasswordSalt, user.PasswordHash, out _);
            if (!ok)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidCredential, "Mật khẩu hiện tại không đúng.");

            if (req.CurrentPassword == req.NewPassword)
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới không được trùng mật khẩu cũ.");

            var (hash, salt) = _hasher.CreateHash(req.NewPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            await _repo.UpdateUserAsync(user, ct);

            TrySendChangedEmail(user);
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
            var otpHash = _hasher.HashString(otp);
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

            try
            {
                using var client = BuildSmtpClient();
                var expiredVn = TimeZoneInfo.ConvertTime(ticket.ExpireAtUtc, _tzVN);
                var mail = new System.Net.Mail.MailMessage
                {
                    From = new System.Net.Mail.MailAddress(_config["Smtp:User"]!, "BookMyDoctor System"),
                    Subject = "Mã OTP đặt lại mật khẩu",
                    IsBodyHtml = true,
                    Body = $"<p>Mã OTP của bạn: <b>{otp}</b></p>" +
                           $"<p>Hết hạn: <b>{expiredVn:HH:mm:ss dd/MM/yyyy} (GMT+7)</b></p>"
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

        // -------------------- OLD: OTP VERIFY + RESET (giữ nguyên) --------------------
        public async Task<ServiceResult<EmptyResult>> VerifyOtpAndResetPasswordAsync(VerifyOtpRequest req, CancellationToken ct = default)
        {
            if (req is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu dữ liệu đầu vào.");
            if (string.IsNullOrWhiteSpace(req.Destination))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu destination.");
            if (string.IsNullOrWhiteSpace(req.OtpCode))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mã OTP.");
            if (string.IsNullOrWhiteSpace(req.NewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mật khẩu mới.");
            if (string.IsNullOrWhiteSpace(req.ConfirmNewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mật khẩu xác nhận.");
            if (req.NewPassword.Length < 6)
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới phải ≥ 6 ký tự.");
            if (req.NewPassword != req.ConfirmNewPassword)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidCredential, "Mật khẩu mới và xác nhận không khớp.");

            var dest = req.Destination.Trim();
            var otpTicket = await _otpRepo.GetLatestValidAsync(dest, req.Purpose, ct);
            if (otpTicket is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.NotFound, "Không tìm thấy OTP hợp lệ.");
            if (DateTimeOffset.UtcNow > otpTicket.ExpireAtUtc)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "Mã OTP đã hết hạn.");
            if (otpTicket.Attempts >= 5)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "Nhập sai OTP quá nhiều.");

            if (!_hasher.VerifyString(req.OtpCode, otpTicket.OtpHash))
            {
                otpTicket.Attempts++;
                await _otpRepo.UpdateAsync(otpTicket, ct);
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "Mã OTP không chính xác.");
            }

            var user = await _repo.FindByLoginKeyAsync(dest, ct);
            if (user is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.NotFound, "Không tìm thấy người dùng.");

            var isSameAsOld = _hasher.Verify(req.NewPassword, user.PasswordSalt, user.PasswordHash, out _);
            if (isSameAsOld)
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới không được trùng mật khẩu cũ.");

            var (hash, salt) = _hasher.CreateHash(req.NewPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            otpTicket.Used = true;

            await _repo.UpdateUserAsync(user, ct);
            await _otpRepo.UpdateAsync(otpTicket, ct);

            TrySendChangedEmail(user);
            return ServiceResult<EmptyResult>.Ok(new EmptyResult());
        }

        // -------------------- NEW: STEP-2 VERIFY → otpToken --------------------
        public async Task<ServiceResult<OtpVerifiedResult>> VerifyOtpAsync(VerifyOtpOnlyRequest req, CancellationToken ct = default)
        {
            if (req is null)
                return ServiceResult<OtpVerifiedResult>.Fail(AuthError.MissingInput, "Thiếu dữ liệu đầu vào.");
            if (string.IsNullOrWhiteSpace(req.Destination))
                return ServiceResult<OtpVerifiedResult>.Fail(AuthError.MissingInput, "Thiếu email.");
            if (string.IsNullOrWhiteSpace(req.OtpCode) || req.OtpCode.Length != 6 || !req.OtpCode.All(char.IsDigit))
                return ServiceResult<OtpVerifiedResult>.Fail(AuthError.BadRequest, "Mã OTP phải gồm 6 chữ số.");

            var dest = req.Destination.Trim();
            var otpTicket = await _otpRepo.GetLatestValidAsync(dest, req.Purpose, ct);
            if (otpTicket is null)
                return ServiceResult<OtpVerifiedResult>.Fail(AuthError.NotFound, "Không tìm thấy OTP hợp lệ.");
            if (DateTimeOffset.UtcNow > otpTicket.ExpireAtUtc)
                return ServiceResult<OtpVerifiedResult>.Fail(AuthError.Expired, "Mã OTP đã hết hạn.");
            if (otpTicket.Attempts >= 5)
                return ServiceResult<OtpVerifiedResult>.Fail(AuthError.InvalidToken, "Nhập sai OTP quá nhiều.");

            if (!_hasher.VerifyString(req.OtpCode, otpTicket.OtpHash))
            {
                otpTicket.Attempts++;
                await _otpRepo.UpdateAsync(otpTicket, ct);
                return ServiceResult<OtpVerifiedResult>.Fail(AuthError.InvalidToken, "Mã OTP không chính xác.");
            }

            var user = await _repo.FindByLoginKeyAsync(dest, ct);
            if (user is null)
                return ServiceResult<OtpVerifiedResult>.Fail(AuthError.NotFound, "Không tìm thấy người dùng.");

            // otpToken ký HMAC: userId.expUnix.signature
            var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
            var sig = Sign($"{user.UserId}.{exp}");
            var token = $"{user.UserId}.{exp}.{sig}";

            return ServiceResult<OtpVerifiedResult>.Ok(new OtpVerifiedResult(token));
        }

        // -------------------- NEW: STEP-3 RESET by otpToken --------------------
        public async Task<ServiceResult<EmptyResult>> ResetPasswordWithOtpTokenAsync(OtpChangePasswordByTokenRequest req, CancellationToken ct = default)
        {
            if (req is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu dữ liệu đầu vào.");
            if (string.IsNullOrWhiteSpace(req.OtpToken))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu otpToken.");
            if (string.IsNullOrWhiteSpace(req.NewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mật khẩu mới.");
            if (string.IsNullOrWhiteSpace(req.ConfirmNewPassword))
                return ServiceResult<EmptyResult>.Fail(AuthError.MissingInput, "Thiếu mật khẩu xác nhận.");
            if (req.NewPassword.Length < 6)
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới phải ≥ 6 ký tự.");
            if (req.NewPassword != req.ConfirmNewPassword)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidCredential, "Mật khẩu mới và xác nhận không khớp.");

            // Parse token: userId.exp.sig
            var parts = req.OtpToken.Split('.', 3);
            if (parts.Length != 3)
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "otpToken không hợp lệ.");
            if (!int.TryParse(parts[0], out var userId))
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "otpToken không hợp lệ.");
            if (!long.TryParse(parts[1], out var exp))
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "otpToken không hợp lệ.");

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
                return ServiceResult<EmptyResult>.Fail(AuthError.Expired, "otpToken đã hết hạn.");

            var expected = Sign($"{userId}.{exp}");
            if (!CryptographicEquals(expected, parts[2]))
                return ServiceResult<EmptyResult>.Fail(AuthError.InvalidToken, "otpToken không hợp lệ.");

            var user = await _repo.FindByIdAsync(userId, ct);
            if (user is null)
                return ServiceResult<EmptyResult>.Fail(AuthError.NotFound, "Không tìm thấy người dùng.");

            var same = _hasher.Verify(req.NewPassword, user.PasswordSalt, user.PasswordHash, out _);
            if (same)
                return ServiceResult<EmptyResult>.Fail(AuthError.WeakPassword, "Mật khẩu mới không được trùng mật khẩu cũ.");

            var (hash, salt) = _hasher.CreateHash(req.NewPassword);
            user.PasswordHash = hash;
            user.PasswordSalt = salt;
            await _repo.UpdateUserAsync(user, ct);

            TrySendChangedEmail(user);
            return ServiceResult<EmptyResult>.Ok(new EmptyResult());
        }

        // ====================== Helpers ======================
        private System.Net.Mail.SmtpClient BuildSmtpClient() => new()
        {
            Host = _config["Smtp:Host"]!,
            Port = int.Parse(_config["Smtp:Port"]!),
            EnableSsl = bool.Parse(_config["Smtp:EnableSsl"]!),
            Credentials = new System.Net.NetworkCredential(_config["Smtp:User"], _config["Smtp:Password"])
        };

        private void TrySendChangedEmail(User user)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(user.Email)) return;
                var whenVn = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tzVN);
                using var client = BuildSmtpClient();
                var mail = new System.Net.Mail.MailMessage
                {
                    From = new System.Net.Mail.MailAddress(_config["Smtp:User"]!, "BookMyDoctor System"),
                    Subject = "Xác nhận thay đổi mật khẩu",
                    IsBodyHtml = true,
                    Body = $"<p>Xin chào {System.Net.WebUtility.HtmlEncode(user.Username)},</p>" +
                           $"<p>Mật khẩu của bạn đã được cập nhật lúc <b>{whenVn:HH:mm:ss dd/MM/yyyy} (GMT+7)</b>.</p>"
                };
                mail.To.Add(user.Email);
                client.Send(mail);
            }
            catch { /* ignore */ }
        }

        // HMAC SHA256 ký token ngắn hạn
        private string Sign(string data)
        {
            var secret = _config["Otp:Secret"];
            if (string.IsNullOrEmpty(secret))
                throw new InvalidOperationException("Thiếu cấu hình Otp:Secret.");

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Base64UrlEncode(bytes);
        }

        private static bool CryptographicEquals(string a, string b)
        {
            var ba = Encoding.UTF8.GetBytes(a);
            var bb = Encoding.UTF8.GetBytes(b);
            if (ba.Length != bb.Length) return false;
            var diff = 0;
            for (int i = 0; i < ba.Length; i++) diff |= ba[i] ^ bb[i];
            return diff == 0;
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
