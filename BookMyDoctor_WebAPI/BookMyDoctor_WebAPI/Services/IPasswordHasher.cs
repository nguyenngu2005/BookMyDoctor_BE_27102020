// BookMyDoctor_WebAPI/Services/IPasswordHasher.cs
namespace BookMyDoctor_WebAPI.Services
{
    public interface IPasswordHasher
    {
        // Cho mật khẩu (salted)
        (byte[] Hash, byte[] Salt) CreateHash(string password);
        bool Verify(string password, byte[] storedSalt, byte[] storedHash, out bool needsUpgrade);
        (byte[] Hash, byte[] Salt) UpgradeToSha512(string password);

        // Cho OTP / chuỗi ngắn (không salt, SHA-512 -> HEX 128 ký tự)
        string HashString(string value);
        bool VerifyString(string value, string storedHash);
    }
}
