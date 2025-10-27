using BookMyDoctor_WebAPI.Models;

namespace BookMyDoctor_WebAPI.Repositories
{
    public interface IProfileRepository
    {
        Task<User?> GetUserProfileAsync(int userId, CancellationToken ct = default);
        Task<Doctor?> GetDoctorByUserIdAsync(int userId, CancellationToken ct = default);
        Task<bool> UpdateDoctorProfileAsync(Doctor doctor, CancellationToken ct = default);
        Task<bool> UpdateUserProfileAsync(User user, CancellationToken ct = default);
    }
}
