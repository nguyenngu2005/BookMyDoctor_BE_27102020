// Magic. Don't touch
using BookMyDoctor_WebAPI.Models;

namespace BookMyDoctor_WebAPI.Data.Repositories
{
    public interface IUserRepository
    {
        Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct = default);
        Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
        Task<bool> ExistsByPhoneAsync(string phone, CancellationToken ct = default);
        Task<int> AddAsync(User user, CancellationToken ct = default); // return new UserId
    }
}
