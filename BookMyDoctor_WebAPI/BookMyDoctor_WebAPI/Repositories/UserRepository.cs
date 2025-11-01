using BookMyDoctor_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

// Magic. Don't touch

namespace BookMyDoctor_WebAPI.Data.Repositories
{
    public interface IUserRepository
    {
        Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct = default);
        Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default);
        Task<bool> ExistsByPhoneAsync(string phone, CancellationToken ct = default);
        Task<int> AddAsync(User user, CancellationToken ct = default); // return new UserId
    }
    public sealed class UserRepository : IUserRepository
    {
        private readonly DBContext _ctx;
        public UserRepository(DBContext ctx) => _ctx = ctx;

        public Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct = default)
            => _ctx.Users.AnyAsync(u => u.Username == username, ct);

        public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct = default)
            => _ctx.Users.AnyAsync(u => u.Email == email, ct);

        public Task<bool> ExistsByPhoneAsync(string phone, CancellationToken ct = default)
            => _ctx.Users.AnyAsync(u => u.Phone == phone, ct);

        public async Task<int> AddAsync(User user, CancellationToken ct = default)
        {
            await _ctx.Users.AddAsync(user, ct);
            await _ctx.SaveChangesAsync(ct);
            return user.UserId;
        }
    }
}
