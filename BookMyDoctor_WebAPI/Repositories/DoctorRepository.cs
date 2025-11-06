using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Repositories
{
    public interface IDoctorRepository
    {
        Task<IReadOnlyList<Doctor>> GetAllDoctorsAsync(CancellationToken ct = default);
        Task<Doctor?> GetDoctorByIdAsync(int doctorId, CancellationToken ct = default);

        Task<IReadOnlyList<Doctor>> SearchDoctorAsync(
            string? name = null,
            string? department = null,
            string? gender = null,
            string? phone = null,
            DateOnly? workDate = null,
            CancellationToken ct = default);

        Task<Doctor> AddDoctorAsync(Doctor doctor, CancellationToken ct = default);
        Task<bool> DeleteDoctorAsync(int doctorId, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct = default);
    }

    public class DoctorRepository : IDoctorRepository
    {
        private readonly DBContext _context;
        public DoctorRepository(DBContext context) => _context = context;

        public async Task<IReadOnlyList<Doctor>> GetAllDoctorsAsync(CancellationToken ct = default)
        {
            return await _context.Doctors
                .Where(d => d.IsActive)
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<Doctor?> GetDoctorByIdAsync(int doctorId, CancellationToken ct = default)
        {
            return await _context.Doctors.FindAsync(new object[] { doctorId }, ct);
        }

        // CHỈ GIỮ 1 HÀM ĐÚNG CHỮ KÝ INTERFACE (tất cả tham số nullable)
        public async Task<IReadOnlyList<Doctor>> SearchDoctorAsync(
            string? name = null,
            string? department = null,
            string? gender = null,
            string? phone = null,
            DateOnly? workDate = null,
            CancellationToken ct = default)
        {
            var query = _context.Doctors
                .Include(d => d.Schedules)
                .Where(d => d.IsActive)
                .AsQueryable();

            // Name (null-safe)
            if (!string.IsNullOrWhiteSpace(name))
            {
                var needle = name.Trim().ToLower();
                query = query.Where(d => ((d.Name ?? "").ToLower()).Contains(needle));
            }

            // Department (null-safe + Like)
            if (!string.IsNullOrWhiteSpace(department))
            {
                var dep = department.Trim();
                query = query.Where(d => d.Department != null &&
                                         EF.Functions.Like(d.Department, $"%{dep}%"));
            }

            // Gender (null-safe)
            if (!string.IsNullOrWhiteSpace(gender))
            {
                var g = gender.Trim().ToLower();
                query = query.Where(d => ((d.Gender ?? "").ToLower()).Contains(g));
            }

            // Phone (null-safe)
            if (!string.IsNullOrWhiteSpace(phone))
            {
                var p = phone.Trim();
                query = query.Where(d => (d.Phone ?? "").Contains(p));
            }

            // WorkDate
            if (workDate.HasValue)
            {
                var dOnly = workDate.Value; // đã là DateOnly, không cần chuyển vòng
                query = query.Where(d => d.Schedules.Any(s => s.WorkDate == dOnly));
            }

            return await query.AsNoTracking().ToListAsync(ct);
        }

        public async Task<Doctor> AddDoctorAsync(Doctor doctor, CancellationToken ct = default)
        {
            await _context.Doctors.AddAsync(doctor, ct);
            await _context.SaveChangesAsync(ct);
            return doctor;
        }

        public async Task<bool> DeleteDoctorAsync(int doctorId, CancellationToken ct = default)
        {
            var doctor = await _context.Doctors.FindAsync(new object[] { doctorId }, ct);
            if (doctor == null) return false;

            // Soft-delete
            doctor.IsActive = false;

            var relatedSchedules = await _context.Schedules
                .Where(s => s.DoctorId == doctorId)
                .ToListAsync(ct);

            foreach (var s in relatedSchedules)
                s.IsActive = false;

            await _context.SaveChangesAsync(ct);
            return true;
        }

        public async Task SaveChangesAsync(CancellationToken ct = default)
            => await _context.SaveChangesAsync(ct);
    }
}
