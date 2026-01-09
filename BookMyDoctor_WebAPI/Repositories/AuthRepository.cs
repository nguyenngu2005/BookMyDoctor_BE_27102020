// Magic. Don't touch
// Data/Repositories/AuthRepository.cs
using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Data.Repositories;

public interface IAuthRepository
{
    Task<User?> FindByLoginKeyAsync(string key, CancellationToken ct = default);
    Task<User?> FindByIdAsync(int id, CancellationToken ct = default);
    Task UpdateUserAsync(User user, CancellationToken ct = default);
}

public sealed class AuthRepository : IAuthRepository
dshahdká
dsađasd 
{
    private readonly DBContext _ctx;
    public AuthRepository(DBContext ctx) => _ctx = ctx;

    public Task<User?> FindByLoginKeyAsync(string key, CancellationToken ct = default)
        => _ctx.Users
               .AsNoTracking() // chỉ đọc
                               //.Include(u => u.Role) // mở nếu cần Role trong claims
               .FirstOrDefaultAsync(u =>
                    u.Username == key || u.Phone == key || u.Email == key, ct);

    public Task<User?> FindByIdAsync(int id, CancellationToken ct = default)
        => _ctx.Users
               .AsNoTracking() // chỉ đọc
                               //.Include(u => u.Role) // mở nếu cần
               .FirstOrDefaultAsync(u => u.UserId == id, ct);

    public async Task UpdateUserAsync(User user, CancellationToken ct = default)
    {
        _ctx.Users.Update(user);           // đã có entity từ Service (đã chỉnh hash/salt)
        await _ctx.SaveChangesAsync(ct);   // commit tại đây => đơn giản hoá flow
    }
    // ================== IOtp + OtpRepo ===========================
    public interface IOtpRepository
    {
        Task<OtpTicket?> GetLatestValidAsync(string destination, string purpose, CancellationToken ct = default);
        Task AddAsync(OtpTicket ticket, CancellationToken ct = default);
        Task UpdateAsync(OtpTicket ticket, CancellationToken ct = default);
    }

    public sealed class OtpRepository : IOtpRepository
    {
        private readonly DBContext _ctx;
        public OtpRepository(DBContext ctx) => _ctx = ctx;

        // Lấy OTP mới nhất, chưa dùng, cùng mục đích, đúng email/sđt
        public Task<OtpTicket?> GetLatestValidAsync(string destination, string purpose, CancellationToken ct = default)
            => _ctx.OtpTickets
                   .Where(x => x.Destination == destination &&
                               x.Purpose == purpose &&
                               !x.Used)
                   .OrderByDescending(x => x.OtpId)
                   .FirstOrDefaultAsync(ct);

        // Tạo mới 1 OTP ticket
        public async Task AddAsync(OtpTicket ticket, CancellationToken ct = default)
        {
            _ctx.OtpTickets.Add(ticket);
            await _ctx.SaveChangesAsync(ct);
        }

        // Cập nhật trạng thái (Used, Attempts, ...)
        public async Task UpdateAsync(OtpTicket ticket, CancellationToken ct = default)
        {
            _ctx.OtpTickets.Update(ticket);
            await _ctx.SaveChangesAsync(ct);
        }
    }
}
