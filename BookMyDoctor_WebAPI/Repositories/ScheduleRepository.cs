using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Data.Repositories
{
    public interface IScheduleRepository
    {
        /// Lấy tất cả lịch làm việc của tất cả bác sĩ.
        Task<IEnumerable<DoctorScheduleRequest>> GetAllDoctorSchedulesAsync(CancellationToken ct);

        /// Lấy danh sách lịch làm việc của một bác sĩ trong một ngày cụ thể.
        Task<IReadOnlyList<Schedule>> GetByDoctorAndDateAsync(
            int doctorId,
            DateOnly workDate,
            CancellationToken ct = default);

        /// Lấy một lịch làm việc duy nhất (nếu có) của bác sĩ trong ngày.
        Task<Schedule?> GetOneByDoctorAndDateAsync(
            int doctorId,
            DateOnly date,
            CancellationToken ct = default);

        /// Lấy tất cả lịch làm việc của một bác sĩ (có thể lọc theo khoảng ngày).
        Task<IReadOnlyList<Schedule>> GetByDoctorAsync(
            int doctorId,
            DateOnly? fromDate = null,
            DateOnly? toDate = null,
            CancellationToken ct = default);

        /// Kiểm tra trùng ngày làm việc
        Task<bool> CheckWorkDateAsync(int doctorId, DateOnly workDate, CancellationToken ct = default);

        /// Thêm mới lịch làm việc. Trả về Schedule đã tạo (cho tiện lấy Id).
        Task<Schedule> AddAsync(
            Schedule schedule,
            CancellationToken ct = default);

        /// Cập nhật thông tin lịch làm việc (thời gian, trạng thái...).
        Task<bool> UpdateAsync(
            Schedule schedule,
            CancellationToken ct = default);

        /// Xóa lịch làm việc theo ScheduleId.
        Task<bool> DeleteAsync(
            int scheduleId,
            CancellationToken ct = default);
    }
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
                    ScheduleId = s.ScheduleId,          // 🔥 FIX QUAN TRỌNG
                    DoctorId = s.DoctorId,
                    DoctorName = s.Doctor!.Name,
                    WorkDate = s.WorkDate,
                    StartTime = s.StartTime,
                    EndTime = s.EndTime,
                    Status = s.Status,
                    IsActive = s.IsActive
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
            // ?? Tìm l?ch làm vi?c c?n xóa
            var entity = await _ctx.Schedules.FirstOrDefaultAsync(s => s.ScheduleId == scheduleId, ct);
            if (entity is null)
                return false;

            // ?? Xóa m?m (ch? ?ánh d?u là không ho?t ??ng)
            entity.IsActive = false;

            // ?? N?u c?n, có th? vô hi?u hóa các cu?c h?n (appointments) liên quan
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