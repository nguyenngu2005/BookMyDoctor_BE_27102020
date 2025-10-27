using BookMyDoctor_WebAPI.Models;

namespace BookMyDoctor_WebAPI.Repositories
{
    public interface IOwnerRepository
    {
        // Create doctor
        Task<User?> GetUserByUsernameAsync(string username);
        //Task AddUserAsync(User user);
        Task AddDoctorAsync(Doctor doctor);
        Task SaveAsync();
    }
}
