// Magic. Don't touch
// Services/TimeLimitedToken.cs
using System;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace BookMyDoctor_WebAPI.Services
{
    public interface ITimeLimitedToken
    {
        string Protect<T>(T payload, TimeSpan lifetime);
        T Unprotect<T>(string token);
    }

    public sealed class TimeLimitedToken : ITimeLimitedToken
    {
        private readonly ITimeLimitedDataProtector _protector;
        private readonly JsonSerializerOptions _jsonOpt = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public TimeLimitedToken(IDataProtectionProvider provider)
        {
            _protector = provider
                .CreateProtector("BookMyDoctor.ResetPassword")
                .ToTimeLimitedDataProtector();
        }

        // Khớp chữ ký interface generic
        public string Protect<T>(T payload, TimeSpan lifetime)
        {
            if (payload is null) throw new ArgumentNullException(nameof(payload));
            var json = JsonSerializer.Serialize(payload, _jsonOpt);
            return _protector.Protect(json, lifetime);
        }

        // Khớp chữ ký interface generic
        public T Unprotect<T>(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Token không hợp lệ.", nameof(token));

            try
            {
                var json = _protector.Unprotect(token); // sẽ throw nếu hết hạn/giả mạo
                return JsonSerializer.Deserialize<T>(json, _jsonOpt)
                       ?? throw new UnauthorizedAccessException("Token không hợp lệ hoặc đã hết hạn.");
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException("Token không hợp lệ hoặc đã hết hạn.", ex);
            }
        }
    }

    // Đặt record dùng chung ở 1 file (vd: Models/ResetPayload.cs) để tránh trùng lặp:
    // public sealed record ResetPayload(int UserId, string PwdFingerprint);
}
