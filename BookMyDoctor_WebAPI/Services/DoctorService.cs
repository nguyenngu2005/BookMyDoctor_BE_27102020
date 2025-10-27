using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.Repositories;
using BookMyDoctor_WebAPI.Services;
using Microsoft.EntityFrameworkCore;
using System.Numerics;

// Ensure DoctorService implements IDoctorService
public class DoctorService : IDoctorService
{
    private readonly DBContext _context;
    private readonly IDoctorRepository _repo;
    private readonly ILogger<DoctorService> _logger;

    public DoctorService(DBContext context, IDoctorRepository repo, ILogger<DoctorService> logger)
    {
        _context = context;
        _repo = repo;
        _logger = logger;
    }

    public async Task<IEnumerable<Doctor>> GetAllDoctorsAsync(CancellationToken ct = default)
    {
        return await _repo.GetAllDoctorsAsync(ct);
    }

    public async Task<Doctor?> GetDoctorByIdAsync(int doctorId, CancellationToken ct = default)
    {
        if (doctorId <= 0)
            throw new ArgumentException("Invalid doctor ID.");
        return await _repo.GetDoctorByIdAsync(doctorId, ct);
    }

    public async Task<IEnumerable<Doctor>> SearchDoctorAsync(
            string? name = null,
            string? department = null,
            string? gender = null,
            string? phone = null,
            DateOnly? workDate = null,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new List<Doctor>();

        var result = await _repo.SearchDoctorAsync(name, department, gender, phone, workDate, ct);
        return result.ToList();
    }

    /// Thêm bác sĩ mới kèm lịch mặc định (T2–CN, 08:00–17:00)
    public async Task<Doctor> AddDoctorAsync(
    Doctor doctor,
    IEnumerable<DayOfWeek>? defaultDays = null,
    TimeOnly? defaultStart = null,
    TimeOnly? defaultEnd = null)
    {
        defaultDays ??= new[]
        {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
    };
        defaultStart ??= new TimeOnly(8, 0);
        defaultEnd ??= new TimeOnly(17, 0);

        await using var tx = await _context.Database.BeginTransactionAsync();

        try
        {
            if (doctor == null)
                throw new ArgumentNullException(nameof(doctor));

            if (string.IsNullOrWhiteSpace(doctor.Name))
                throw new ArgumentException("Doctor name is required.");

            // Kiểm tra trùng email/phone
            var existingDoctors = await _repo.GetAllDoctorsAsync();
            if (existingDoctors.Any(d =>
                (!string.IsNullOrEmpty(d.Email) && d.Email.Equals(doctor.Email, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(d.Phone) && d.Phone == doctor.Phone)))
            {
                throw new InvalidOperationException("A doctor with the same email or phone already exists.");
            }

            await _repo.AddDoctorAsync(doctor);
            await _context.SaveChangesAsync();

            var today = DateTime.Today;
            var schedules = new List<Schedule>();
            int weeksToGenerate = 4;

            foreach (var day in defaultDays)
            {
                var offset = ((int)day - (int)today.DayOfWeek + 7) % 7;
                var firstDate = today.AddDays(offset);

                for (int i = 0; i < weeksToGenerate; i++)
                {
                    var workDate = firstDate.AddDays(i * 7);
                    var dateOnly = DateOnly.FromDateTime(workDate);

                    // Check tránh trùng ngày
                    if (await _context.Schedules.AnyAsync(s =>
                        s.DoctorId == doctor.DoctorId && s.WorkDate == dateOnly))
                        continue;

                    schedules.Add(new Schedule
                    {
                        DoctorId = doctor.DoctorId,
                        WorkDate = dateOnly,
                        StartTime = defaultStart.Value,
                        EndTime = defaultEnd.Value,
                        Status = "Scheduled",
                        IsActive = true
                    });
                }
            }

            await _context.Schedules.AddRangeAsync(schedules);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            _logger.LogInformation("Created Doctor {Id} ({Name}) with {Count} schedules.",
                doctor.DoctorId, doctor.Name, schedules.Count);

            return doctor;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating doctor {Name}", doctor?.Name);
            await tx.RollbackAsync();
            throw;
        }
    }

    //public async Task<bool> UpdateDoctorAsync(Doctor doctor, CancellationToken ct = default)
    //{
    //    if (doctor == null)
    //        throw new ArgumentNullException(nameof(doctor));

    //    var existing = await _repo.GetDoctorByIdAsync(doctor.DoctorId, ct);
    //    if (existing == null)
    //        return false;

    //    // Chỉ cập nhật các field cho phép (tùy bạn muốn)
    //    existing.Name = doctor.Name;
    //    existing.Department = doctor.Department;
    //    existing.Phone = doctor.Phone;
    //    existing.Email = doctor.Email;
    //    existing.Address = doctor.Address;
    //    existing.Experience_year = doctor.Experience_year;

    //    await _repo.UpdateDoctorAsync(existing, ct);
    //    return true;
    //}

    public async Task<bool> DeleteDoctorAsync(int doctorId, CancellationToken ct = default)
    {
        if (doctorId <= 0)
            throw new ArgumentException("Invalid doctor ID.");

        return await _repo.DeleteDoctorAsync(doctorId, ct);
    }

    //public async Task<IEnumerable<Doctor>> GetDoctorsByCurrentUserAsync(int userId)
    //{
    //    return await _context.Doctors
    //        .Where(d => d.UserId == userId)
    //        .ToListAsync();
    //}
}
