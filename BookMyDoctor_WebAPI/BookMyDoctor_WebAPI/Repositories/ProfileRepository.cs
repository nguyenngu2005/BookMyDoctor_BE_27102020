using BookMyDoctor_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Data.Repositories
{
    public interface IProfileRepository
    {
        Task<User?> GetUserProfileAsync(int userId, CancellationToken ct = default);
        Task<Doctor?> GetDoctorByUserIdAsync(int userId, CancellationToken ct = default);
        Task<bool> UpdateDoctorProfileAsync(Doctor doctor, CancellationToken ct = default);
        Task<bool> UpdateUserProfileAsync(User user, CancellationToken ct = default);
    }
    public class ProfileRepository : IProfileRepository
    {
        private readonly DBContext _context;

        public ProfileRepository(DBContext context)
        {
            _context = context;
        }

        public async Task<User?> GetUserProfileAsync(int userId, CancellationToken ct = default)
        {
            return await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserId == userId);
        }

        public async Task<Doctor?> GetDoctorByUserIdAsync(int userId, CancellationToken ct = default)
        {
            return await _context.Doctors
                .Include(d => d.Schedules)
                .FirstOrDefaultAsync(d => d.UserId == userId);
        }

        //public async Task<Patient?> GetUserByUserIdAsync(int userId, CancellationToken ct = default)
        //{
        //    return await _context.Patients
        //        .FirstOrDefaultAsync(p => p.UserId == userId);
        //}

        public async Task<bool> UpdateDoctorProfileAsync(Doctor doctor, CancellationToken ct = default)
        {
            _context.Doctors.Update(doctor);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> UpdateUserProfileAsync(User patient, CancellationToken ct = default)
        {
            _context.Users.Update(patient);
            return await _context.SaveChangesAsync() > 0;
        }
    }
}
