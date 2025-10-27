using BookMyDoctor_WebAPI.Models;

namespace BookMyDoctor_WebAPI.Repositories
{
    public interface IDoctorRepository
    {
        Task<IReadOnlyList<Doctor>> GetAllDoctorsAsync(CancellationToken ct = default);
        Task<Doctor?> GetDoctorByIdAsync(int doctorId, CancellationToken ct = default);
        Task<IReadOnlyList<Doctor>> SearchDoctorAsync(
            string? name = null,
            string? department = null,
            string? gender = null,
            string? phone = null,
            DateOnly? workDate = null,
            CancellationToken ct = default);
        Task<Doctor> AddDoctorAsync(Doctor doctor, CancellationToken ct = default);

        //Task UpdateDoctorAsync(Doctor doctor, CancellationToken ct = default); -> Đã được set trong Profile

        Task<bool> DeleteDoctorAsync(int doctorId, CancellationToken ct = default);
        Task SaveChangesAsync(CancellationToken ct);
    }
}
