using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Data.Repositories
{
    public sealed class ScheduleRepository : IScheduleRepository
    {
        private readonly DBContext _ctx;
        public ScheduleRepository(DBContext ctx) => _ctx = ctx;

        public async Task<IEnumerable<DoctorScheduleRequest>> GetAllDoctorSchedulesAsync(CancellationToken ct)
        {
            return await _ctx.Schedules
                .Include(s => s.Doctor)
                .Where(s => s.IsActive && s.Doctor!.IsActive)
                .Select(s => new DoctorScheduleRequest
                {
                    DoctorId = s.DoctorId,
                    DoctorName = s.Doctor!.Name,
                    WorkDate = s.WorkDate,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    Status = s.Status
                })
                .OrderBy(s => s.WorkDate)
                .ThenBy(s => s.StartTime)
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<Schedule>> GetByDoctorAndDateAsync(
            int doctorId, DateOnly date, CancellationToken ct = default)
        {
            return await _ctx.Schedules
                .AsNoTracking()
                .Where(s => s.DoctorId == doctorId && s.WorkDate == date)
                .OrderBy(s => s.StartTime)
                .ToListAsync(ct);
        }

        public async Task<Schedule?> GetOneByDoctorAndDateAsync(
            int doctorId, DateOnly date, CancellationToken ct = default)
        {
            return await _ctx.Schedules
                .AsNoTracking()
                .Where(s => s.DoctorId == doctorId && s.WorkDate == date)
                .OrderBy(s => s.StartTime)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<IReadOnlyList<Schedule>> GetByDoctorAsync(
            int doctorId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
        {
            var q = _ctx.Schedules.AsNoTracking()
                .Where(s => s.DoctorId == doctorId);

            if (fromDate.HasValue) q = q.Where(s => s.WorkDate >= fromDate.Value);
            if (toDate.HasValue) q = q.Where(s => s.WorkDate <= toDate.Value);

            return await q.OrderBy(s => s.WorkDate)
                          .ThenBy(s => s.StartTime)
                          .ToListAsync(ct);
        }

        public async Task<bool> CheckWorkDateAsync(int doctorId, DateOnly workDate, CancellationToken ct = default)
        {
            return await _ctx.Schedules
                .AsNoTracking()
                .AnyAsync(s => s.DoctorId == doctorId && s.WorkDate == workDate, ct);
        }

        public async Task<Schedule> AddAsync(Schedule schedule, CancellationToken ct = default)
        {
            await _ctx.Schedules.AddAsync(schedule, ct);
            await _ctx.SaveChangesAsync(ct);
            return schedule;
        }

        public async Task<bool> UpdateAsync(Schedule schedule, CancellationToken ct = default)
        {
            var existing = await _ctx.Schedules.FirstOrDefaultAsync(s => s.ScheduleId == schedule.ScheduleId, ct);
            if (existing is null) return false;

            existing.WorkDate = schedule.WorkDate;
            existing.StartTime = schedule.StartTime;
            existing.EndTime = schedule.EndTime;
            existing.Status = schedule.Status;
            existing.DoctorId = schedule.DoctorId;

            await _ctx.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(int scheduleId, CancellationToken ct = default)
        {
            // 🔍 Tìm lịch làm việc cần xóa
            var entity = await _ctx.Schedules.FirstOrDefaultAsync(s => s.ScheduleId == scheduleId, ct);
            if (entity is null)
                return false;

            // 🔸 Xóa mềm (chỉ đánh dấu là không hoạt động)
            entity.IsActive = false;

            // 🔸 Nếu cần, có thể vô hiệu hóa các cuộc hẹn (appointments) liên quan
            var relatedAppointments = await _ctx.Appointments
                .Where(a => a.ScheduleId == scheduleId)
                .ToListAsync(ct);

            foreach (var appointment in relatedAppointments)
            {
                appointment.IsActive = false;
            }

            await _ctx.SaveChangesAsync(ct);
            return true;
        }
    }
}
