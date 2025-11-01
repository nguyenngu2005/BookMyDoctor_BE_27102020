// Magic. Don't touch
using BookMyDoctor_WebAPI.Data.Repositories;
using BookMyDoctor_WebAPI.Models;
using Microsoft.Extensions.Configuration;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace BookMyDoctor_WebAPI.Services.Register
{
    public enum RegisterOutcome
    {
        Success = 0,
        UsernameExists,
        EmailExists,
        PhoneExists,
        UnknownError,
        InvalidUsername,
        InvalidEmail
    }

    public sealed record RegisterResult(RegisterOutcome Outcome, int? UserId = default);

    public interface IRegisterService
    {
        Task<RegisterResult> CreateUserAsync(
            string username, string? email, string phone,
            byte[] passwordHash, byte[] passwordSalt,
            string roleId = "R03",
            CancellationToken ct = default);
    }

    public sealed class RegisterService : IRegisterService
    {
        private readonly IUserRepository _users;
        private readonly IConfiguration _config;

        public RegisterService(IUserRepository users, IConfiguration config)
        {
            _users = users;
            _config = config;
        }

        public async Task<RegisterResult> CreateUserAsync(
            string username, string? email, string phone,
            byte[] passwordHash, byte[] passwordSalt,
            string roleId = "R03",
            CancellationToken ct = default)
        {
            // 1) Chuẩn hoá
            var normalizedUsername = username?.Trim() ?? string.Empty;
            var normalizedEmail = NormalizeGmailOrNull(email);   // chỉ nhận @gmail.com nếu có
            var normalizedPhone = NormalizePhone(phone);

            // Nếu client có gửi email nhưng sai quy tắc Gmail → InvalidEmail
            if (!string.IsNullOrWhiteSpace(email) && normalizedEmail is null)
                return new RegisterResult(RegisterOutcome.InvalidEmail);

            // 2) Kiểm tra trùng
            if (await _users.ExistsByUsernameAsync(normalizedUsername, ct))
                return new RegisterResult(RegisterOutcome.UsernameExists);

            if (!string.IsNullOrEmpty(normalizedEmail) && await _users.ExistsByEmailAsync(normalizedEmail!, ct))
                return new RegisterResult(RegisterOutcome.EmailExists);

            if (await _users.ExistsByPhoneAsync(normalizedPhone, ct))
                return new RegisterResult(RegisterOutcome.PhoneExists);

            if (!Regex.IsMatch(normalizedUsername, @"^[a-zA-Z0-9_]+$"))
                return new RegisterResult(RegisterOutcome.InvalidUsername);

            // 3) Tạo entity
            var user = new User
            {
                Username = normalizedUsername,
                Email = normalizedEmail,
                Phone = normalizedPhone,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                RoleId = roleId
            };

            try
            {
                var id = await _users.AddAsync(user, ct);

                // 4) Gửi email chào mừng (non-blocking)
                try
                {
                    if (!string.IsNullOrWhiteSpace(user.Email))
                    {
                        using var client = new SmtpClient
                        {
                            Host = _config["Smtp:Host"]!,
                            Port = int.Parse(_config["Smtp:Port"]!),
                            EnableSsl = bool.Parse(_config["Smtp:EnableSsl"]!),
                            Credentials = new NetworkCredential(_config["Smtp:User"], _config["Smtp:Password"])
                        };

                        var from = new MailAddress(_config["Smtp:User"]!, "BookMyDoctor System");
                        var to = new MailAddress(user.Email);
                        using var mail = new MailMessage(from, to)
                        {
                            Subject = "Chào mừng bạn đến với BookMyDoctor!",
                            IsBodyHtml = true,
                            Body =
                                $"<p>Xin chào <b>{WebUtility.HtmlEncode(user.Username)}</b>,</p>" +
                                $"<p>Bạn đã đăng ký tài khoản BookMyDoctor thành công.</p>" +
                                $"<p>Chúc bạn có trải nghiệm thật tuyệt!</p>"
                        };

                        await client.SendMailAsync(mail, ct);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SMTP-Register] {ex.Message}");
                }

                return new RegisterResult(RegisterOutcome.Success, id);
            }
            catch (Exception ex) when (IsUniqueConstraintViolation(ex))
            {
                // Double-check kiểu trùng
                if (await _users.ExistsByUsernameAsync(normalizedUsername, ct))
                    return new RegisterResult(RegisterOutcome.UsernameExists);
                if (!string.IsNullOrEmpty(normalizedEmail) && await _users.ExistsByEmailAsync(normalizedEmail!, ct))
                    return new RegisterResult(RegisterOutcome.EmailExists);
                if (await _users.ExistsByPhoneAsync(normalizedPhone, ct))
                    return new RegisterResult(RegisterOutcome.PhoneExists);

                return new RegisterResult(RegisterOutcome.UnknownError);
            }
        }

        // ===== Helpers =====
        private static string? NormalizeGmailOrNull(string? email)
        {
            if (string.IsNullOrWhiteSpace(email)) return null;

            var e = email.Trim().ToLowerInvariant();
            if (!e.EndsWith("@gmail.com", StringComparison.Ordinal)) return null;

            var at = e.LastIndexOf('@');
            if (at <= 0) return null;

            var local = e[..at];
            var domainOk = e[(at + 1)..] == "gmail.com";

            var localOk =
local.Length >= 1 && local.Length <= 64 &&
                !local.StartsWith('.') && !local.EndsWith('.') &&
                !local.Contains("..") &&
                Regex.IsMatch(local, @"^[a-z0-9.+]+$");

            return (domainOk && localOk) ? e : null;
        }

        private static string NormalizePhone(string phone)
        {
            var p = (phone ?? string.Empty).Trim().Replace(" ", "");
            if (p.StartsWith("+84")) p = "0" + p.Substring(3);
            return p;
        }

        private static bool IsUniqueConstraintViolation(Exception ex)
        {
            var msg = ex.ToString();
            return msg.Contains("IX_", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("2601") || msg.Contains("2627");
        }
    }
}