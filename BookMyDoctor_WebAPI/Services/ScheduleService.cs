using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Data.Repositories; // IScheduleRepository
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Services
{
    public interface IScheduleService
    {
        Task<IEnumerable<DoctorScheduleRequest>> GetAllDoctorSchedulesAsync(CancellationToken ct = default);

        Task<IReadOnlyList<Schedule>> GetDoctorSchedulesAsync(
            int doctorId,
            DateOnly? from = null,
            DateOnly? to = null,
            CancellationToken ct = default);

        Task<List<DoctorScheduleRequest>> GetDoctorSchedulesByNameAsync(
            string? doctorName,
            DateOnly? date,
            CancellationToken ct = default);

        Task<Schedule> AddScheduleAsync(Schedule schedule, CancellationToken ct = default);

        Task<bool> UpdateScheduleAsync(Schedule schedule, CancellationToken ct = default);

        Task<bool> DeleteScheduleAsync(int scheduleId, CancellationToken ct = default);
    }

    public sealed class ScheduleService : IScheduleService
    {
        private readonly IScheduleRepository _repo;
        private readonly DBContext _context;
        private readonly ILogger<ScheduleService> _logger;

        private static readonly HashSet<string> _validStatuses =
            new(StringComparer.OrdinalIgnoreCase) { "Scheduled", "Completed", "Cancelled" };

        public ScheduleService(
            IScheduleRepository repo,
            DBContext context,
            ILogger<ScheduleService> logger)
        {
            _repo = repo;
            _context = context;
            _logger = logger;
        }

        // ========== Queries ==========

        public async Task<IEnumerable<DoctorScheduleRequest>> GetAllDoctorSchedulesAsync(CancellationToken ct)
            => await _repo.GetAllDoctorSchedulesAsync(ct);

        public async Task<IReadOnlyList<Schedule>> GetDoctorSchedulesAsync(
            int doctorId,
            DateOnly? from = null,
            DateOnly? to = null,
            CancellationToken ct = default)
        {
            return await _repo.GetByDoctorAsync(doctorId, from, to, ct);
        }

        public async Task<List<DoctorScheduleRequest>> GetDoctorSchedulesByNameAsync(
            string? doctorName,
            DateOnly? date,
            CancellationToken ct = default)
        {
            var query = _context.Schedules
                .Include(s => s.Doctor)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(doctorName))
            {
                var needle = doctorName.Trim();
                query = query.Where(s =>
                    s.Doctor != null &&
                    s.Doctor.Name != null &&
                    EF.Functions.Like(s.Doctor.Name, $"%{needle}%"));
            }

            if (date.HasValue)
                query = query.Where(s => s.WorkDate == date.Value);

            var result = await query
                .Select(s => new DoctorScheduleRequest
                {
                    ScheduleId = s.ScheduleId,
                    DoctorId = s.DoctorId,
                    DoctorName = s.Doctor != null ? (s.Doctor.Name ?? string.Empty) : string.Empty,
                    WorkDate = s.WorkDate,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    Status = s.Status ?? "Scheduled",
                    IsActive = s.IsActive
                })
                .OrderBy(s => s.WorkDate)
                .ThenBy(s => s.StartTime)
                .ToListAsync(ct);

            return result;
        }

        // ========== Commands ==========

        public async Task<Schedule> AddScheduleAsync(Schedule schedule, CancellationToken ct = default)
        {
            if (schedule is null)
                throw new ArgumentNullException(nameof(schedule));

            ValidateSchedule(schedule);

            // Bác sĩ phải tồn tại
            var doctorExists = await _context.Doctors
                .AsNoTracking()
                .AnyAsync(d => d.DoctorId == schedule.DoctorId, ct);
            if (!doctorExists)
                throw new InvalidOperationException("Không tìm thấy bác sĩ.");

            // Không trùng khung giờ trong cùng ngày
            var sameDay = await _repo.GetByDoctorAndDateAsync(schedule.DoctorId, schedule.WorkDate, ct);
            if (sameDay.Any(s => IsOverlap(schedule.StartTime, schedule.EndTime, s.StartTime, s.EndTime)))
                throw new InvalidOperationException("Khung giờ này bị trùng với một lịch làm việc khác của bác sĩ.");

            schedule.Status ??= "Scheduled";

            await _repo.AddAsync(schedule, ct);

            _logger.LogInformation("Added schedule for Doctor {DoctorId} @ {Date} {Start}-{End}",
                schedule.DoctorId, schedule.WorkDate, schedule.StartTime, schedule.EndTime);

            return schedule;
        }

        public async Task<bool> UpdateScheduleAsync(Schedule schedule, CancellationToken ct = default)
        {
            if (schedule is null)
                throw new ArgumentNullException(nameof(schedule));

            ValidateSchedule(schedule);

            // Không chồng chéo với lịch khác trong cùng ngày
            var sameDay = await _repo.GetByDoctorAndDateAsync(schedule.DoctorId, schedule.WorkDate, ct);
            if (sameDay.Any(s => s.ScheduleId != schedule.ScheduleId &&
                                 IsOverlap(schedule.StartTime, schedule.EndTime, s.StartTime, s.EndTime)))
                throw new InvalidOperationException("Lịch sau khi cập nhật đang bị trùng với một lịch khác.");

            var ok = await _repo.UpdateAsync(schedule, ct);
            if (ok)
            {
                _logger.LogInformation("Updated schedule {ScheduleId} for Doctor {DoctorId}", schedule.ScheduleId, schedule.DoctorId);
            }
            return ok;
        }

        public async Task<bool> DeleteScheduleAsync(int scheduleId, CancellationToken ct = default)
        {
            var ok = await _repo.DeleteAsync(scheduleId, ct);
            if (ok)
                _logger.LogInformation("Deleted schedule {ScheduleId}", scheduleId);

            return ok;
        }

        // ========== Helpers ==========

        private static void ValidateSchedule(Schedule schedule)
        {
            if (schedule.WorkDate == default)
                throw new ArgumentException("Ngày làm việc (WorkDate) là bắt buộc.");

            if (schedule.StartTime >= schedule.EndTime)
                throw new ArgumentException("Giờ bắt đầu phải sớm hơn giờ kết thúc.");

            var status = schedule.Status ?? "Scheduled";
            if (!_validStatuses.Contains(status))
                throw new ArgumentException("Trạng thái không hợp lệ. Chỉ cho phép: Scheduled, Completed, Cancelled.");
        }

        private static bool IsOverlap(TimeOnly aStart, TimeOnly aEnd, TimeOnly bStart, TimeOnly bEnd)
            => aStart < bEnd && bStart < aEnd;
    }
}
