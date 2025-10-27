using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Data.Repositories;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Services
{
    public class ScheduleService : IScheduleService
    {
        private readonly IScheduleRepository _repo;
        private readonly DBContext _context;
        private readonly ILogger<ScheduleService> _logger;

        private static readonly HashSet<string> _validStatuses =
            new(StringComparer.OrdinalIgnoreCase) { "Scheduled", "Completed", "Cancelled" };

        public ScheduleService(IScheduleRepository repo, DBContext context, ILogger<ScheduleService> logger)
        {
            _repo = repo;
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<DoctorScheduleRequest>> GetAllDoctorSchedulesAsync(CancellationToken ct)
        {
            return await _repo.GetAllDoctorSchedulesAsync(ct);
        }

        public async Task<IReadOnlyList<Schedule>> GetDoctorSchedulesAsync(
            int doctorId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
        {
            return await _repo.GetByDoctorAsync(doctorId, from, to, ct);
        }

        public async Task<List<Schedule>> GetDoctorSchedulesByNameAsync(string? doctorName, DateOnly? date, CancellationToken ct)
        {
            var query = _context.Schedules
                .Include(s => s.Doctor)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(doctorName))
            {
                query = query.Where(s => s.Doctor != null &&
                                         s.Doctor.Name != null &&
                                         s.Doctor.Name.Contains(doctorName));
            }

            if (date.HasValue)
                query = query.Where(s => s.WorkDate == date.Value);

            return await query.ToListAsync(ct);
        }


        public async Task<Schedule> AddScheduleAsync(Schedule schedule, CancellationToken ct = default)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            ValidateSchedule(schedule);

            // Kiểm tra bác sĩ tồn tại
            bool doctorExists = await _context.Doctors
                .AsNoTracking()
                .AnyAsync(d => d.DoctorId == schedule.DoctorId, ct);
            if (!doctorExists)
                throw new InvalidOperationException("Doctor not found.");

            // Kiểm tra trùng ngày (chỉ cho phép 1 WorkDate/doctor)
            bool exists = await _repo.CheckWorkDateAsync(schedule.DoctorId, schedule.WorkDate, ct);
            if (exists)
                throw new InvalidOperationException("This doctor already has a schedule for that date.");

            // Kiểm tra chồng thời gian trong cùng ngày
            var existingSchedules = await _repo.GetByDoctorAndDateAsync(schedule.DoctorId, schedule.WorkDate, ct);
            if (existingSchedules.Any(s => IsOverlap(schedule.StartTime, schedule.EndTime, s.StartTime, s.EndTime)))
                throw new InvalidOperationException("Schedule time overlaps with an existing schedule.");

            schedule.Status ??= "Scheduled";
            await _repo.AddAsync(schedule, ct);

            _logger.LogInformation("Added schedule for Doctor {Id} at {Date}", schedule.DoctorId, schedule.WorkDate);
            return schedule;
        }

        public async Task<bool> UpdateScheduleAsync(Schedule schedule, CancellationToken ct = default)
        {
            if (schedule == null) throw new ArgumentNullException(nameof(schedule));
            ValidateSchedule(schedule);

            // Kiểm tra chồng chéo trước khi update
            var sameDay = await _repo.GetByDoctorAndDateAsync(schedule.DoctorId, schedule.WorkDate, ct);
            if (sameDay.Any(s => s.ScheduleId != schedule.ScheduleId &&
                                 IsOverlap(schedule.StartTime, schedule.EndTime, s.StartTime, s.EndTime)))
                throw new InvalidOperationException("Updated schedule overlaps with another.");

            return await _repo.UpdateAsync(schedule, ct);
        }

        public async Task<bool> DeleteScheduleAsync(int scheduleId, CancellationToken ct = default)
        {
            return await _repo.DeleteAsync(scheduleId, ct);
        }

        // ================= HELPER =================
        private static void ValidateSchedule(Schedule schedule)
        {
            if (schedule.StartTime >= schedule.EndTime)
                throw new ArgumentException("Start time must be before end time.");

            if (schedule.WorkDate == default)
                throw new ArgumentException("WorkDate is required.");

            if (!_validStatuses.Contains(schedule.Status ?? "Scheduled"))
                throw new ArgumentException("Invalid status. Allowed: Scheduled, Completed, Cancelled.");
        }

        private static bool IsOverlap(TimeOnly aStart, TimeOnly aEnd, TimeOnly bStart, TimeOnly bEnd)
        {
            return aStart < bEnd && bStart < aEnd;
        }
    }
}
