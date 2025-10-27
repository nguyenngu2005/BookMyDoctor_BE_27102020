using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.Repositories;

namespace BookMyDoctor_WebAPI.Services
{
    public interface IDoctorService
    {
        Task<IEnumerable<Doctor>> GetAllDoctorsAsync(CancellationToken ct = default);
        Task<Doctor?> GetDoctorByIdAsync(int doctorId, CancellationToken ct = default);
        Task<IEnumerable<Doctor>> SearchDoctorAsync(string? name = null,
            string? department = null,
            string? gender = null,
            string? phone = null,
            DateOnly? workDate = null,
            CancellationToken ct = default);
        Task<Doctor> AddDoctorAsync(Doctor doctor,
            IEnumerable<DayOfWeek>? defaultDays = null,
            TimeOnly? defaultStart = null,
            TimeOnly? defaultEnd = null);

        //Task<bool> UpdateDoctorAsync(Doctor doctor, CancellationToken ct = default);

        Task<bool> DeleteDoctorAsync(int doctorId, CancellationToken ct = default);
    }
}
