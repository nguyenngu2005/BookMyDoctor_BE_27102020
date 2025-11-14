using System.Threading;
using System.Threading.Tasks;
using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Services
{
    // ===== Interface gộp trong cùng file =====
    public interface IProfileService
    {
        Task<ProfileResponse?> GetProfileAsync(int userId, CancellationToken ct = default);
        Task<string> UpdateProfileAsync(int userId, ProfileRequest req, CancellationToken ct = default);
    }

    // DTO trả về cho /profile-me
    public class ProfileResponse
    {
        public int UserId { get; set; }
        public string? Username { get; set; }
        public string? RoleId { get; set; }   // R01/R02/R03
        public string? Name { get; set; }
        public string? Gender { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Department { get; set; } // Chuyên khoa (Doctor only)
        public int? ExperienceYear { get; set; } // Năm kinh nghiệm (Doctor only)
    }

    // ===== Triển khai service =====
    public class ProfileService : IProfileService
    {
        private readonly DBContext _db;
        public ProfileService(DBContext db) => _db = db;

        public async Task<ProfileResponse?> GetProfileAsync(int userId, CancellationToken ct = default)
        {
            var u = await _db.Users.AsNoTracking()
                                   .FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (u == null) return null;

            if (u.RoleId == "R02") // Doctor
            {
                var d = await _db.Doctors.AsNoTracking()
                                         .FirstOrDefaultAsync(x => x.UserId == userId, ct);
                return new ProfileResponse
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    RoleId = u.RoleId,
                    Name = d?.Name,
                    Gender = d?.Gender,
                    DateOfBirth = d?.DateOfBirth,
                    Phone = d?.Phone,
                    Email = d?.Email,
                    Address = d?.Address,
                    Department = d?.Department,
                    ExperienceYear = d?.Experience_year
                };
            }

            // Mặc định Patient (R03); có thể mở rộng R01 nếu cần.
            var p = await _db.Patients.AsNoTracking()
                                      .FirstOrDefaultAsync(x => x.UserId == userId, ct);
            return new ProfileResponse
            {
                UserId = u.UserId,
                Username = u.Username,
                RoleId = u.RoleId,
                Name = p?.Name,
                Gender = p?.Gender,
                DateOfBirth = p?.DateOfBirth,
                Phone = p?.Phone,
                Email = p?.Email,
                Address = p?.Address
            };
        }

        public async Task<string> UpdateProfileAsync(int userId, ProfileRequest req, CancellationToken ct = default)
        {
            var u = await _db.Users.FirstOrDefaultAsync(x => x.UserId == userId, ct);
            if (u == null) return "User not found";

            if (u.RoleId == "R02") // Doctor
            {
                var d = await _db.Doctors.FirstOrDefaultAsync(x => x.UserId == userId, ct);
                if (d == null) return "Doctor profile not found";

                if (!string.IsNullOrWhiteSpace(req.Name)) d.Name = req.Name.Trim();
                if (!string.IsNullOrWhiteSpace(req.Gender)) d.Gender = req.Gender.Trim();
                if (req.DateOfBirth.HasValue) d.DateOfBirth = req.DateOfBirth.Value;
                if (!string.IsNullOrWhiteSpace(req.Phone)) d.Phone = req.Phone.Trim();
                if (!string.IsNullOrWhiteSpace(req.Email)) d.Email = req.Email.Trim();
                if (!string.IsNullOrWhiteSpace(req.Address)) d.Address = req.Address.Trim();
                if (!string.IsNullOrWhiteSpace(req.Department)) d.Department = req.Department.Trim();
                if (req.ExperienceYear.HasValue) d.Experience_year = req.ExperienceYear.Value;

                await _db.SaveChangesAsync(ct);
                return "Doctor profile updated";
            }
            else if (u.RoleId == "R01" || u.RoleId == "R03") // Patient (R03 default)
            {
                var p = await _db.Patients.FirstOrDefaultAsync(x => x.UserId == userId, ct);
                if (p == null) return "Patient profile not found";

                if (!string.IsNullOrWhiteSpace(req.Name)) p.Name = req.Name.Trim();
                if (!string.IsNullOrWhiteSpace(req.Gender)) p.Gender = req.Gender.Trim();
                if (req.DateOfBirth.HasValue) p.DateOfBirth = req.DateOfBirth.Value;
                if (!string.IsNullOrWhiteSpace(req.Phone)) p.Phone = req.Phone.Trim();
                if (!string.IsNullOrWhiteSpace(req.Email)) p.Email = req.Email.Trim();
                if (!string.IsNullOrWhiteSpace(req.Address)) p.Address = req.Address.Trim();

                await _db.SaveChangesAsync(ct);
                return "Patient profile updated";
            }
            else return "Không có vai trò phù hợp để update";
        }
    }
}