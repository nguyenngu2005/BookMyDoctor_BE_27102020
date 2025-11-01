using System.Linq;
using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Services
{
    public interface IDoctorService
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

        Task<Doctor> AddDoctorAsync(
            Doctor doctor,
            IEnumerable<DayOfWeek>? defaultDays = null,
            TimeOnly? defaultStart = null,
            TimeOnly? defaultEnd = null,
            CancellationToken ct = default);

        Task<bool> DeleteDoctorAsync(int doctorId, CancellationToken ct = default);
    }

    public class DoctorService : IDoctorService
    {
        private readonly DBContext _db;

        public DoctorService(DBContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlyList<Doctor>> GetAllDoctorsAsync(CancellationToken ct = default)
        {
            return await _db.Doctors
                .AsNoTracking()
                .OrderBy(d => d.Name)
                .ToListAsync(ct);
        }

        public async Task<Doctor?> GetDoctorByIdAsync(int doctorId, CancellationToken ct = default)
        {
            return await _db.Doctors
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.DoctorId == doctorId, ct);
        }

        public async Task<IReadOnlyList<Doctor>> SearchDoctorAsync(
            string? name = null,
            string? department = null,
            string? gender = null,
            string? phone = null,
            DateOnly? workDate = null,
            CancellationToken ct = default)
        {
            IQueryable<Doctor> query = _db.Doctors.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(name))
            {
                var k = name.Trim().ToLowerInvariant();
                query = query.Where(d =>
                    (d.Name != null && d.Name.ToLower().Contains(k)) ||
                    (d.Email != null && d.Email.ToLower().Contains(k)));
            }

            if (!string.IsNullOrWhiteSpace(department))
            {
                var dep = department.Trim().ToLowerInvariant();
                query = query.Where(d => d.Department != null && d.Department.ToLower() == dep);
            }

            if (!string.IsNullOrWhiteSpace(gender))
            {
                var g = gender.Trim().ToLowerInvariant();
                query = query.Where(d => d.Gender != null && d.Gender.ToLower() == g);
            }

            if (!string.IsNullOrWhiteSpace(phone))
            {
                var p = phone.Trim();
                query = query.Where(d => d.Phone != null && d.Phone.Contains(p));
            }

            if (workDate.HasValue)
            {
                var date = workDate.Value;
                // Chỉ lấy bác sĩ có lịch làm việc đúng ngày đó
                query = query.Where(d =>
                    _db.Schedules.Any(s => s.DoctorId == d.DoctorId && s.WorkDate == date));
            }

            return await query
                .OrderBy(d => d.Name)
                .ToListAsync(ct);
        }

        public async Task<Doctor> AddDoctorAsync(
            Doctor doctor,
            IEnumerable<DayOfWeek>? defaultDays = null,
            TimeOnly? defaultStart = null,
            TimeOnly? defaultEnd = null,
            CancellationToken ct = default)
        {
            await _db.Doctors.AddAsync(doctor, ct);
            await _db.SaveChangesAsync(ct); // cần để lấy DoctorId

            // Tạo lịch mặc định (nếu có yêu cầu)
            if (defaultDays != null && defaultDays.Any())
            {
                var start = defaultStart ?? new TimeOnly(8, 0);
                var end = defaultEnd ?? new TimeOnly(17, 0);

                var daySet = new HashSet<DayOfWeek>(defaultDays);

                var today = DateOnly.FromDateTime(DateTime.Today);
                var until = today.AddDays(30); // 30 ngày tới

                var schedules = new List<Schedule>();
                for (var d = today; d <= until; d = d.AddDays(1))
                {
                    if (!daySet.Contains(d.ToDateTime(TimeOnly.MinValue).DayOfWeek))
                        continue;

                    schedules.Add(new Schedule
                    {
                        DoctorId = doctor.DoctorId,
                        WorkDate = d,
                        StartTime = start,
                        EndTime = end,
                        Status = "Scheduled",
                        IsActive = true
                    });
                }

                if (schedules.Count > 0)
                {
                    await _db.Schedules.AddRangeAsync(schedules, ct);
                    await _db.SaveChangesAsync(ct);
                }
            }

            return doctor;
        }

        public async Task<bool> DeleteDoctorAsync(int doctorId, CancellationToken ct = default)
        {
            var existing = await _db.Doctors.FirstOrDefaultAsync(d => d.DoctorId == doctorId, ct);
            if (existing == null) return false;

            _db.Doctors.Remove(existing);
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}
