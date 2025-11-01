//Magic. Don't touch
// Services/PasswordHasherAdapter.cs
using System.Security.Cryptography;
using System.Text;
using BookMyDoctor_WebAPI.Helpers;

namespace BookMyDoctor_WebAPI.Services
{
    // Adapter bọc helper static để dùng được với DI
    public sealed class PasswordHasherAdapter : IPasswordHasher
    {
        // ======== DÙNG CHO PASSWORD (SHA512 + SALT) ========
        public (byte[] Hash, byte[] Salt) CreateHash(string password)
        {
            var (h, s) = PasswordHasher.CreateHashSha512(password);
            return (h, s);
        }

        public bool Verify(string password, byte[] storedSalt, byte[] storedHash, out bool needsUpgrade)
        {
            return PasswordHasher.Verify(password, storedSalt, storedHash, out needsUpgrade);
        }

        public (byte[] Hash, byte[] Salt) UpgradeToSha512(string password)
        {
            var (h, s) = PasswordHasher.UpgradeToSha512(password);
            return (h, s);
        }

        // ======== DÙNG CHO OTP (SHA512 không SALT) ========
        public string HashString(string value)
        {
            using var sha = SHA512.Create();
            var bytes = Encoding.UTF8.GetBytes(value);
            var hash = sha.ComputeHash(bytes);
            // Trả về Hex in hoa 128 ký tự (phù hợp NVARCHAR(128))
            return Convert.ToHexString(hash);
        }

        public bool VerifyString(string value, string storedHash)
        {
            var computedHex = HashString(value);
            // So sánh hằng thời gian trên byte[]
            var a = Encoding.UTF8.GetBytes(computedHex);
            var b = Encoding.UTF8.GetBytes(storedHash ?? string.Empty);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
    }
}
