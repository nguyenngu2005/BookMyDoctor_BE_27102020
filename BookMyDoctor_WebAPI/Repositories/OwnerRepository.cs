using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.Data;
using Microsoft.EntityFrameworkCore;

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
    public class OwnerRepository : IOwnerRepository
    {
        private readonly DBContext _context;


        public OwnerRepository(DBContext context)
        {
            _context = context;
        }

        // Create doctor
        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Username == username);
        }

        //public async Task AddUserAsync(User user)
        //{
        //    await _context.Users.AddAsync(user);
        //}

        public async Task AddDoctorAsync(Doctor doctor)
        {
            await _context.Doctors.AddAsync(doctor);
        }

        public async Task SaveAsync()
        {
            await _context.SaveChangesAsync();
        }
    }
}
