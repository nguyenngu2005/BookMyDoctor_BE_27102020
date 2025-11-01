//Magic. Don't touch
using System;
using System.Security.Cryptography;
using System.Text;

namespace BookMyDoctor_WebAPI.Helpers
{
    /// Hash mật khẩu:
    /// - CHUẨN MỚI: SHA-512(salt(32B) || UTF8(password)) -> Hash 64B, Salt 32B
    /// - LEGACY: hỗ trợ xác thực SHA-256(password) không salt (kiểu seed bằng HASHBYTES('SHA2_256','pwd'))
    ///   => Cho đăng nhập 1 lần rồi nâng cấp lên chuẩn mới để không khóa user.
    public static class PasswordHasher
    {
        public static (byte[] Hash, byte[] Salt) CreateHashSha512(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be empty.", nameof(password));

            // Salt 32 bytes cho SHA-512
            byte[] salt = RandomNumberGenerator.GetBytes(32);
            byte[] hash = ComputeHash(password, salt, useSha512: true); // 64 bytes
            return (hash, salt);
        }

        /// Verify:
        /// - Hỗ trợ 3 trường hợp:
        ///   (A) Legacy unsalted SHA-256: storedHash = SHA256(password), storedSalt == storedHash (hoặc null/0-length)
        ///       => ok => needsUpgrade = true
        ///   (B) Salted SHA-256 cũ: storedHash 32B, storedSalt != null
        ///       => ok => needsUpgrade = true
        ///   (C) Chuẩn mới salted SHA-512: storedHash 64B, storedSalt 32B
        public static bool Verify(string password, byte[]? storedSalt, byte[]? storedHash, out bool needsUpgrade)
        {
            needsUpgrade = false;

            if (string.IsNullOrWhiteSpace(password) || storedHash is null)
                return false;

            bool isSha512 = storedHash.Length == 64;   // 64 bytes = SHA-512
            bool isSha256 = storedHash.Length == 32;   // 32 bytes = SHA-256

            // ===== (A) LEGACY: unsalted SHA-256 (seed kiểu HASHBYTES('SHA2_256','pwd'))
            // Dấu hiệu thường gặp: PasswordSalt == PasswordHash (hoặc Salt null/0)
            bool saltMissingOrMirrorsHash =
                storedSalt is null || storedSalt.Length == 0 ||
                (storedSalt.Length == storedHash.Length &&
                 CryptographicOperations.FixedTimeEquals(storedSalt, storedHash));

            if (isSha256 && saltMissingOrMirrorsHash)
            {
                // 1) Thử UTF-8 (trùng với VARCHAR '123456')
                byte[] pwdUtf8 = Encoding.UTF8.GetBytes(password);
                byte[] u8 = SHA256.HashData(pwdUtf8);

                bool ok = CryptographicOperations.FixedTimeEquals(u8, storedHash);

                // 2) (tuỳ chọn) fallback UTF-16LE nếu ai đó seed bằng NVARCHAR (N'...')
                if (!ok)
                {
                    byte[] pwdUtf16 = Encoding.Unicode.GetBytes(password); // UTF-16LE
                    byte[] u16 = SHA256.HashData(pwdUtf16);
                    ok = CryptographicOperations.FixedTimeEquals(u16, storedHash);
                    Array.Clear(pwdUtf16, 0, pwdUtf16.Length);
                    Array.Clear(u16, 0, u16.Length);
                }

                Array.Clear(pwdUtf8, 0, pwdUtf8.Length);
                Array.Clear(u8, 0, u8.Length);

                if (ok) needsUpgrade = true; // đăng nhập hợp lệ -> sẽ nâng cấp lên SHA-512 + salt
                return ok;
            }

            // ===== (B) / (C): salted (SHA-256 cũ hoặc SHA-512 mới)
            if (storedSalt is null || storedSalt.Length == 0)
                return false;

            byte[] computed = ComputeHash(password, storedSalt, useSha512: isSha512);
            bool match = CryptographicOperations.FixedTimeEquals(computed, storedHash);

            if (match && isSha256)
                needsUpgrade = true; // salted SHA-256 cũ -> nâng cấp

            Array.Clear(computed, 0, computed.Length);
            return match;
        }

        /// Dùng khi cần rehash ngay lên SHA-512 sau khi Verify (needsUpgrade = true).
        public static (byte[] NewHash, byte[] NewSalt) UpgradeToSha512(string password)
        {
            return CreateHashSha512(password);
        }

        // ------------ Helpers ------------
        private static byte[] ComputeHash(string password, byte[] salt, bool useSha512)
        {
            byte[] pwdBytes = Encoding.UTF8.GetBytes(password);
            byte[] combined = Combine(salt, pwdBytes);
            byte[] hash = useSha512 ? SHA512.HashData(combined) : SHA256.HashData(combined);

            Array.Clear(pwdBytes, 0, pwdBytes.Length);
            Array.Clear(combined, 0, combined.Length);
            return hash;
        }

        private static byte[] Combine(byte[] first, byte[] second)
        {
            byte[] result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }
    }
}
