using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Repositories
{
    public class DoctorRepository : IDoctorRepository
    {
        private readonly DBContext _context;
        public DoctorRepository(DBContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<Doctor>> GetAllDoctorsAsync(CancellationToken ct = default)
        {
            return await _context.Doctors
                .Where(d => d.IsActive)   // chỉ lấy bác sĩ còn hoạt động
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<Doctor?> GetDoctorByIdAsync(int doctorId, CancellationToken ct = default)
        {
            return await _context.Doctors.FindAsync([doctorId], ct);
        }

        public async Task<IReadOnlyList<Doctor>> SearchDoctorsAsync(
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

            // 🔍 Tìm theo tên bác sĩ
            if (!string.IsNullOrWhiteSpace(name))
            {
                string lowerName = name.ToLower();
                query = query.Where(d => d.Name.ToLower().Contains(lowerName));
            }

            // 🔍 Tìm theo khoa
            if (!string.IsNullOrWhiteSpace(department))
            {
                query = query.Where(d => d.Department.ToLower().Trim().Contains(department.ToLower().Trim()));
            }

            // 🔍 Tìm theo giới tính
            if (!string.IsNullOrWhiteSpace(gender))
            {
                gender = gender.Trim().ToLower();
                query = query.Where(d => d.Gender.ToLower().Trim().Contains(gender));
            }

            // 🔍 Tìm theo số điện thoại
            if (!string.IsNullOrWhiteSpace(phone))
            {
                query = query.Where(d => d.Phone != null && d.Phone.Contains(phone.Trim()));
            }

            // 🔍 Tìm theo ngày làm việc (WorkDate trong Schedule)
            if (workDate.HasValue)
            {
                var dateOnly = DateOnly.FromDateTime(workDate.Value.ToDateTime(TimeOnly.MinValue));
                query = query.Where(d => d.Schedules.Any(s => s.WorkDate == dateOnly));
            }

            // Trả về danh sách kết quả
            return await query.ToListAsync(ct);
        }

        public async Task<Doctor> AddDoctorAsync(Doctor doctor, CancellationToken ct = default)
        {
            await _context.Doctors.AddAsync(doctor, ct);
            await _context.SaveChangesAsync(ct);
            return doctor;
        }

        //public async Task UpdateDoctorAsync(Doctor doctor, CancellationToken ct = default)
        //{
        //    _context.Doctors.Update(doctor);
        //    await _context.SaveChangesAsync(ct);
        //}

        public async Task<bool> DeleteDoctorAsync(int doctorId, CancellationToken ct = default)
        {
            var doctor = await _context.Doctors.FindAsync(new object[] { doctorId }, ct);
            if (doctor == null)
                return false;

            // Xóa mềm thay vì xóa thật
            doctor.IsActive = false;

            // Nếu có liên quan đến schedules, ta cũng có thể set IsActive = false cho chúng
            var relatedSchedules = await _context.Schedules
                .Where(s => s.DoctorId == doctorId)
                .ToListAsync(ct);

            foreach (var schedule in relatedSchedules)
            {
                schedule.IsActive = false;
            }

            await _context.SaveChangesAsync(ct);
            return true;
        }

        //public async Task AddSchedulesAsync(IEnumerable<Schedule> schedules)
        //{
        //    _context.Schedules.AddRange(schedules);
        //    await _context.SaveChangesAsync();
        //}

        public async Task SaveChangesAsync(CancellationToken ct)
                          => await _context.SaveChangesAsync(ct);

        public async Task<IReadOnlyList<Doctor>> SearchDoctorAsync(
            string? name = null,
            string? department = null,
            string? gender = null,
            string? phone = null,
            DateOnly? workDate = null,
            CancellationToken ct = default)
        {
            // Delegate sang hàm đã có
            return await SearchDoctorsAsync(name, department, gender, phone, workDate, ct);
        }
    }
}
