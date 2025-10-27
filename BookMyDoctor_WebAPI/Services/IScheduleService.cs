using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.RequestModel;

namespace BookMyDoctor_WebAPI.Services
{
    public interface IScheduleService
    {
        Task<IEnumerable<DoctorScheduleRequest>> GetAllDoctorSchedulesAsync(CancellationToken ct);
        Task<IReadOnlyList<Schedule>> GetDoctorSchedulesAsync(
            int doctorId, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);
        Task<List<Schedule>> GetDoctorSchedulesByNameAsync(string? doctorName, DateOnly? date, CancellationToken ct);

        Task<Schedule> AddScheduleAsync(Schedule schedule, CancellationToken ct = default);

        Task<bool> UpdateScheduleAsync(Schedule schedule, CancellationToken ct = default);

        Task<bool> DeleteScheduleAsync(int scheduleId, CancellationToken ct = default);
    }
}
