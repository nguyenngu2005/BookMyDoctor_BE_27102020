﻿using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

public record ScheduleInfo(
    int ScheduleId,
    int DoctorId,
    string DoctorName,
    DateOnly WorkDate,
    TimeOnly StartTime,
    TimeOnly EndTime
);

public sealed class PatientLite
{
    public int PatientId { get; set; }
    public int? UserId { get; set; }
    public string? Email { get; set; }
    public string? Name { get; set; }
}

public interface IBookingRepository
{
    Task<ScheduleInfo?> FindScheduleAsync(int doctorId, DateOnly date, CancellationToken ct);
    Task<ScheduleInfo?> GetScheduleByIdAsync(int scheduleId, CancellationToken ct);
    Task<bool> IsSlotAvailableAsync(int scheduleId, TimeOnly hour, CancellationToken ct);

    Task<int> GetOrCreatePatientByEmailAsync(string email, string name, string phone, string? gender, DateOnly? dob, CancellationToken ct);
    Task<PatientLite> GetPatientByIdAsync(int patientId, CancellationToken ct);
    Task<bool> VerifyPatientOwnedByUserAsync(int patientId, int userId, CancellationToken ct);

    Task<int> CreateAppointmentAsync(int patientId, int scheduleId, TimeOnly appointHour, string? symptom, CancellationToken ct);
    Task SetAppointmentCodeIfSupportedAsync(int appointId, string code, CancellationToken ct);

    // ↓↓↓ Không tạo model mới
    Task<int?> FindUserIdByEmailAsync(string email, CancellationToken ct);
    Task<int> GetOrCreatePatientByUserAsync(int userId, string name, string phone, string? gender, DateOnly? dob, CancellationToken ct);
    Task MergeAnonymousPatientsByEmailIntoUserAsync(string email, int userId, int targetPatientId, CancellationToken ct);
    Task<IEnumerable<BusySlot>> GetBusySlotsAsync(int doctorId, DateOnly date, CancellationToken ct);
    Task<bool> DeleteAppointmentAsync(int AppointId, CancellationToken ct = default);
}


public sealed class BookingRepository : IBookingRepository
{
    private readonly DBContext _db;
    public BookingRepository(DBContext db) => _db = db;

    public async Task<ScheduleInfo?> FindScheduleAsync(int doctorId, DateOnly date, CancellationToken ct)
        => await _db.Schedules
            .Where(s => s.DoctorId == doctorId
                // dùng overload DateOnly của DateDiffDay (EF Core 7/8)
                && EF.Functions.DateDiffDay(s.WorkDate, date) == 0)
            .Select(s => new ScheduleInfo(
                s.ScheduleId,
                s.DoctorId,
                // tránh CS8602: ép non-null hoặc coalesce
                s.Doctor!.Name,
                // s.WorkDate đã là DateOnly
                s.WorkDate,
                s.StartTime,
                s.EndTime))
            .FirstOrDefaultAsync(ct);

    public async Task<ScheduleInfo?> GetScheduleByIdAsync(int scheduleId, CancellationToken ct)
        => await _db.Schedules
            .Where(s => s.ScheduleId == scheduleId)
            .Select(s => new ScheduleInfo(
                s.ScheduleId,
                s.DoctorId,
                s.Doctor!.Name,
                s.WorkDate,
                s.StartTime,
                s.EndTime))
            .FirstOrDefaultAsync(ct);

    public async Task<bool> IsSlotAvailableAsync(int scheduleId, TimeOnly hour, CancellationToken ct)
        => !await _db.Appointments.AnyAsync(a => a.ScheduleId == scheduleId && a.AppointHour == hour, ct);

    public async Task<int> GetOrCreatePatientByEmailAsync(string email, string name, string phone, string? gender, DateOnly? dob, CancellationToken ct)
    {
        var p = await _db.Patients.FirstOrDefaultAsync(x => x.Email == email, ct);
        if (p is null)
        {
            p = new Patient
            {
                UserId = null,
                Name = name,
                Phone = phone,
                Email = email,
                Gender = (gender is "Male" or "Female") ? gender : "Male",
                // property kiểu DateOnly → gán trực tiếp
                DateOfBirth = dob ?? DateOnly.FromDateTime(DateTime.Today.AddYears(-20)),
                Address = null
            };
            _db.Patients.Add(p);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = name;
            if (string.IsNullOrWhiteSpace(p.Phone)) p.Phone = phone;
            await _db.SaveChangesAsync(ct);
        }
        return p.PatientId;
    }

    public async Task<PatientLite> GetPatientByIdAsync(int patientId, CancellationToken ct)
        => await _db.Patients
            .Where(p => p.PatientId == patientId)
            .Select(p => new PatientLite
            {
                PatientId = p.PatientId,
                UserId = p.UserId,
                Email = p.Email!,
                Name = p.Name!
            })
            .FirstAsync(ct);

    public async Task<bool> VerifyPatientOwnedByUserAsync(int patientId, int userId, CancellationToken ct)
        => await _db.Patients.AnyAsync(p => p.PatientId == patientId && p.UserId == userId, ct);

    public async Task<int> CreateAppointmentAsync(int patientId, int scheduleId, TimeOnly appointHour, string? symptom, CancellationToken ct)
    {
        var a = new Appointment
        {
            PatientId = patientId,
            ScheduleId = scheduleId,
            AppointHour = appointHour,
            Status = "Scheduled",
            Symptom = symptom
        };
        _db.Appointments.Add(a);
        await _db.SaveChangesAsync(ct); // tôn trọng UNIQUE(ScheduleId, AppointHour)
        return a.AppointId;
    }

    public async Task SetAppointmentCodeIfSupportedAsync(int appointId, string code, CancellationToken ct)
    {
        var a = await _db.Appointments.FindAsync(new object?[] { appointId }, ct);
        if (a is null) return;

        try
        {
            // Không tham chiếu trực tiếp property để tránh CS1061.
            // Nếu entity có cột Code (hoặc shadow property) thì dòng dưới sẽ hoạt động.
            _db.Entry(a).Property("Code").CurrentValue = code;
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Bỏ qua nếu chưa cấu hình cột "Code".
        }
    }
    // === THÊM VÀO BookingRepository ===
    private static string NormalizeEmail(string email)
        => (email ?? string.Empty).Trim().ToLowerInvariant();

    public async Task<int?> FindUserIdByEmailAsync(string email, CancellationToken ct)
    {
        var norm = NormalizeEmail(email);

        // DbSet tên Users phải tồn tại trong DBContext
        return await _db.Users
            .Where(u => u.Email != null && u.Email.ToLower() == norm)
            .Select(u => (int?)u.UserId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> GetOrCreatePatientByUserAsync(
        int userId, string name, string phone, string? gender, DateOnly? dob, CancellationToken ct)
    {
        var p = await _db.Patients.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (p is null)
        {
            // Lấy email từ user để gán cho patient
            var userEmail = await _db.Users
                .Where(u => u.UserId == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);

            p = new Patient
            {
                UserId = userId,
                Name = name,
                Phone = phone,
                Email = userEmail, // có thể null
                Gender = (gender is "Male" or "Female") ? gender : "Male",
                DateOfBirth = dob ?? DateOnly.FromDateTime(DateTime.Today.AddYears(-20)),
                Address = null
            };
            _db.Patients.Add(p);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            // Cập nhật mềm các field rỗng
            if (string.IsNullOrWhiteSpace(p.Name)) p.Name = name;
            if (string.IsNullOrWhiteSpace(p.Phone)) p.Phone = phone;
            if ((p.Gender is null) && (gender is "Male" or "Female")) p.Gender = gender;
            if (p.DateOfBirth == default && dob.HasValue) p.DateOfBirth = dob.Value;
            await _db.SaveChangesAsync(ct);
        }
        return p.PatientId;
    }

    public async Task MergeAnonymousPatientsByEmailIntoUserAsync(
        string email, int userId, int targetPatientId, CancellationToken ct)
    {
        var norm = NormalizeEmail(email);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Các patient vô danh (UserId NULL) có cùng email, khác target
        var anonIds = await _db.Patients
            .Where(p => p.UserId == null
                     && p.Email != null
                     && p.Email.ToLower() == norm
                     && p.PatientId != targetPatientId)
            .Select(p => p.PatientId)
            .ToListAsync(ct);

        if (anonIds.Count > 0)
        {
            // Chuyển appointment sang patient đích
            var appts = await _db.Appointments
                .Where(a => anonIds.Contains(a.PatientId))
                .ToListAsync(ct);

            foreach (var a in appts)
                a.PatientId = targetPatientId;

            await _db.SaveChangesAsync(ct);

            // Xoá các patient vô danh sau khi đã chuyển
            var victims = await _db.Patients
                .Where(p => anonIds.Contains(p.PatientId))
                .ToListAsync(ct);

            _db.Patients.RemoveRange(victims);
            await _db.SaveChangesAsync(ct);
        }

        // Đảm bảo patient đích gắn đúng UserId + có email nếu đang trống
        var target = await _db.Patients.FirstAsync(p => p.PatientId == targetPatientId, ct);
        if (target.UserId != userId) target.UserId = userId;

        if (string.IsNullOrWhiteSpace(target.Email))
        {
            var userEmail = await _db.Users
                .Where(u => u.UserId == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(userEmail))
                target.Email = userEmail;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<IEnumerable<BusySlot>> GetBusySlotsAsync(int doctorId, DateOnly date, CancellationToken ct)
    {
        // Giả sử Schedule có các trường DoctorId và WorkDate
        return await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Schedule)
            .Where(a => a.IsActive
                        && a.Schedule!.DoctorId == doctorId
                        && a.Schedule.WorkDate == date)
            .Select(a => new BusySlot
            {
                Name = a.Patient != null ? a.Patient.Name : "(Unknown)",
                Phone = a.Patient != null ? a.Patient.Phone : null,
                AppointHour = a.AppointHour,
                Status = a.Status
            })
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteAppointmentAsync(int AppointId, CancellationToken ct = default)
    {
        var appointment = await _db.Appointments.FindAsync(new object[] { AppointId }, ct);
        if (appointment == null)
            return false;
        // Xóa mềm thay vì xóa thật
        appointment.IsActive = false;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
