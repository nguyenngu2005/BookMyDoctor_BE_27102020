using BookMyDoctor_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Data.Repositories
{
    public interface IPatientRepository
    {
        /// Tìm bệnh nhân theo name (có thể là tên đầy đủ hoặc một phần).
        Task<IReadOnlyList<Patient>> SearchPatientByNameAsync(
            string name,
            CancellationToken ct = default);

        Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);

        /// Lấy thông tin bệnh nhân theo số điện thoại.
        Task<Patient?> GetByPhoneAsync(
            string phone,
            CancellationToken ct = default);

        /// Lấy thông tin bệnh nhân theo UserId (nếu có liên kết tài khoản user).
        Task<Patient?> GetByUserIdAsync(
            int userId,
            CancellationToken ct = default);

        /// Lấy thông tin bệnh nhân theo PatientId.
        Task<Patient?> GetByIdAsync(
            int patientId,
            CancellationToken ct = default);

        /// Lấy toàn bộ danh sách bệnh nhân (phục vụ admin/tra cứu).
        Task<IReadOnlyList<Patient>> GetAllAsync(
            CancellationToken ct = default);

        /// Thêm mới bệnh nhân vào DB.
        Task AddAsync(
            Patient patient,
            CancellationToken ct = default);

        /// Cập nhật thông tin bệnh nhân (tên, email, địa chỉ...).
        Task UpdateAsync(
            Patient patient,
            CancellationToken ct = default);

        /// Xóa bệnh nhân theo ID.
        Task<bool> DeleteAsync(
            int patientId,
            CancellationToken ct = default);
    }
    /// Repository xử lý các thao tác với bảng patients.
    public sealed class PatientRepository : IPatientRepository
    {
        private readonly DBContext _context;

        public PatientRepository(DBContext context)
        {
            _context = context;
        }

        /// Tìm bệnh nhân theo name (có thể là tên đầy đủ hoặc một phần).
        public async Task<IReadOnlyList<Patient>> SearchPatientByNameAsync(
            string name,
            CancellationToken ct = default)
        {
            //if (string.IsNullOrWhiteSpace(name))
            //    return Array.Empty<Patient>();
            return await _context.Patients
                .AsNoTracking()
                .Include(p => p.User)
                .Where(p => EF.Functions.Like(p.Name, $"%{name}%"))
                .OrderByDescending(p => p.PatientId)
                .ToListAsync(ct);
        }

        /// Tìm user theo username (dùng để kiểm tra trùng username khi tạo tài khoản).
        public async Task<User?> GetByUsernameAsync(
            string username,
            CancellationToken ct = default)
        {
            return await _context.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Username == username, ct);
        }

        /// Lấy bệnh nhân theo số điện thoại.
        public async Task<Patient?> GetByPhoneAsync(
            string phone,
            CancellationToken ct = default)
        {
            return await _context.Patients
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Phone == phone, ct);
        }
        /// Lấy bệnh nhân theo UserId (nếu có liên kết tài khoản user).
        public async Task<Patient?> GetByUserIdAsync(
            int userId,
            CancellationToken ct = default)
        {
            return await _context.Patients
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        }
        /// Lấy thông tin bệnh nhân theo PatientId.
        public async Task<Patient?> GetByIdAsync(
            int patientId,
            CancellationToken ct = default)
        {
            return await _context.Patients
                .AsNoTracking()
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.PatientId == patientId, ct);
        }
        /// Lấy toàn bộ danh sách bệnh nhân.
        public async Task<IReadOnlyList<Patient>> GetAllAsync(
            CancellationToken ct = default)
        {
            return await _context.Patients
                .AsNoTracking()
                .Include(p => p.User)
                .OrderByDescending(p => p.PatientId)
                .ToListAsync(ct);
        }
        /// Thêm mới bệnh nhân vào DB.
        public async Task AddAsync(
            Patient patient,
            CancellationToken ct = default)
        {
            // Validate cơ bản
            if (string.IsNullOrWhiteSpace(patient.Name))
                throw new ArgumentException("Patient name is required.");

            if (string.IsNullOrWhiteSpace(patient.Gender) ||
                !(patient.Gender.Equals("Male", StringComparison.OrdinalIgnoreCase) ||
                  patient.Gender.Equals("Female", StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Gender must be 'Male' or 'Female'.");

            if (patient.DateOfBirth > DateOnly.FromDateTime(DateTime.Today))
                throw new ArgumentException("Date of birth cannot be in the future.");

            await _context.Patients.AddAsync(patient, ct);
            await _context.SaveChangesAsync(ct);
        }
        /// Cập nhật thông tin bệnh nhân.
        public async Task UpdateAsync(
            Patient patient,
            CancellationToken ct = default)
        {
            var existing = await _context.Patients
                .FirstOrDefaultAsync(p => p.PatientId == patient.PatientId, ct);

            if (existing == null)
                throw new InvalidOperationException("Patient not found.");

            // Cập nhật các trường được phép
            existing.Name = patient.Name;
            existing.Gender = patient.Gender;
            existing.DateOfBirth = patient.DateOfBirth;
            existing.Phone = patient.Phone;
            existing.Email = patient.Email;
            existing.Address = patient.Address;
            existing.UserId = patient.UserId;

            await _context.SaveChangesAsync(ct);
        }
        /// Xóa bệnh nhân theo ID.
        public async Task<bool> DeleteAsync(
            int patientId,
            CancellationToken ct = default)
        {
            var patient = await _context.Patients.FindAsync(new object[] { patientId }, ct);
            if (patient == null)
                return false;

            // Xóa mềm thay vì xóa thật
            patient.IsActive = false;

            // Nếu có liên quan đến appointments, schedules, ta cũng có thể set IsActive = false cho chúng
            var relatedAppointments = await _context.Appointments
                .Where(a => a.PatientId == patientId)
                .ToListAsync(ct);

            var relatedScheduleIds = relatedAppointments
                .Select(a => a.ScheduleId)
                .Distinct()
                .ToList();

            var relatedSchedules = await _context.Schedules
                .Where(s => relatedScheduleIds.Contains(s.ScheduleId))
                .ToListAsync(ct);

            foreach (var appointment in relatedAppointments)
            {
                appointment.IsActive = false;
            }

            foreach (var schedule in relatedSchedules)
            {
                schedule.IsActive = false;
            }

            await _context.SaveChangesAsync(ct);
            return true;
        }
    }
}
