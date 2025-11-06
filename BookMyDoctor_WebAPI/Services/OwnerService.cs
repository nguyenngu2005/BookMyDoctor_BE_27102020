using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Data.Repositories;
using BookMyDoctor_WebAPI.Helpers;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.Repositories;
using BookMyDoctor_WebAPI.RequestModel;

namespace BookMyDoctor_WebAPI.Services
{
    public interface IOwnerService
    {
        Task<(bool Success, string Message)> CreateDoctorAccountAsync(CreateDoctorRequest request);
    }
    public class OwnerService : IOwnerService
    {
        private readonly IUserRepository _userRepo;
        private readonly IDoctorRepository _doctorRepo;
        private readonly DBContext _context;

        public OwnerService(IUserRepository userRepo, IDoctorRepository doctorRepo, DBContext context)
        {
            _userRepo = userRepo;
            _doctorRepo = doctorRepo;
            _context = context;
        }

        public async Task<(bool Success, string Message)> CreateDoctorAccountAsync(CreateDoctorRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return (false, "Username and password are required.");

            // Check username already exists
            var existingUser = await _userRepo.ExistsByUsernameAsync(request.Username);
            if (existingUser)
                return (false, "Username already exists.");

            var existingEmail = await _userRepo.ExistsByEmailAsync(request.Email);
            if (existingEmail)
                return (false, "Email number already in use.");

            var existingPhone = await _userRepo.ExistsByPhoneAsync(request.Phone);
            if (existingPhone)
                return (false, "Phone number already in use.");

            // Hash password
            var (hash, salt) = PasswordHasher.CreateHashSha512(request.Password);

            // Create new User
            var newUser = new User
            {
                Username = request.Username,
                PasswordHash = hash,
                PasswordSalt = salt,
                Phone = request.Phone,
                Email = request.Email,
                RoleId = "R02" // Doctor
            };

            await _userRepo.AddAsync(newUser);
            await _context.SaveChangesAsync();

            // Create Doctor entity linked to new user
            var newDoctor = new Doctor
            {
                UserId = newUser.UserId,
                Name = request.Name,
                Phone = request.Phone,
                Email = request.Email,
                Gender = request.Gender,
                DateOfBirth = request.DateOfBirth,
                Department = request.Department,
                Experience_year = request.ExperienceYears,
                Identification = request.Identification
            };

            await _doctorRepo.AddDoctorAsync(newDoctor);    
            await _context.SaveChangesAsync();

            var defaultDays = new[]
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
            };
            var defaultStart = new TimeOnly(8, 0);
            var defaultEnd = new TimeOnly(17, 0);
            var weeksToGenerate = 1;
            var today = DateTime.Today;
            var schedules = new List<Schedule>();

            foreach (var day in defaultDays)
            {
                var offset = ((int)day - (int)today.DayOfWeek + 7) % 7;
                var firstDate = today.AddDays(offset);

                for (int i = 0; i < weeksToGenerate; i++)
                {
                    var workDate = firstDate.AddDays(i * 7);
                    var dateOnly = DateOnly.FromDateTime(workDate);
                    schedules.Add(new Schedule
                    {
                        DoctorId = newDoctor.DoctorId,
                        WorkDate = dateOnly,
                        StartTime = defaultStart,
                        EndTime = defaultEnd,
                        Status = "Scheduled",
                        IsActive = true
                    });
                }
            }

            // **Use IScheduleRepository to add the schedules**
            if (schedules.Any())
            {
                await _context.Schedules.AddRangeAsync(schedules);
                await _context.SaveChangesAsync();
            }

            return (true, "Doctor account created successfully.");
        }
    }
}
