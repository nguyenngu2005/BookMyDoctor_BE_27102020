using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;

namespace BookMyDoctor_WebAPI.Data.Repositories
{
    /// Repository cho bảng schedules – quản lý lịch làm việc của bác sĩ.
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
}
