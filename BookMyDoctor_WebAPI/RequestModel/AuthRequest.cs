// RequestModel/AuthRequests.cs  (tuỳ chọn: thêm validation, không đổi tên field)
using System.ComponentModel.DataAnnotations;

namespace BookMyDoctor_WebAPI.RequestModel
{
    public sealed class ResetPasswordRequest
    {
        [Required] public string Token { get; set; } = default!;
        [Required, MinLength(6)] public string NewPassword { get; set; } = default!;
    }
}
;

public sealed class LoginRequest
{
    [Required] public string UsernameOrPhoneOrEmail { get; set; } = "yourmail@gmail.com/phone";
    [Required] public string Password { get; set; } = "Your phone";
}

public sealed class ChangePasswordRequest
{
    [Required] public string CurrentPassword { get; set; } = "Your old pass";
    [Required, MinLength(6)] public string NewPassword { get; set; } = "Your new pass";
    [Required, MinLength(6)] public string ConfirmNewPassword { get; set; } = "Your confirm new pass";
}

public sealed class ForgotPasswordRequest
{
    [Required] public string EmailOrUsername { get; set; } = "yourmail@gmail.com/phone";
}


// ==================== OTP REQUESTS ====================

public sealed class OtpRequest
{
    [Required] public string Destination { get; set; } = "yourmail@gmail.com";     // email hoặc phone
    [Required, MaxLength(30)] public string Purpose { get; set; } = string.Empty;
    [Required, RegularExpression("^(email|sms)$", ErrorMessage = "Channel phải là 'email' hoặc 'sms'.")]
    public string Channel { get; set; } = "email";
}

public sealed class VerifyOtpRequest
{
    [Required] public string? Destination { get; set; } 
    [Required, MaxLength(30)] public string Purpose { get; set; } = "reset_password";
    [Required, RegularExpression(@"^\d{6}$", ErrorMessage = "OTP phải gồm 6 chữ số.")]
    public string OtpCode { get; set; } = default!;
    [Required, MinLength(6)] public string NewPassword { get; set; } = default!;
    [Required, MinLength(6)] public string ConfirmNewPassword { get; set; } = default!;
}
