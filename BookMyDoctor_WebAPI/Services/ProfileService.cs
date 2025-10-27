using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.Repositories;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.EntityFrameworkCore;

namespace BookMyDoctor_WebAPI.Services
{
    public class ProfileService : IProfileService
    {
        private readonly IProfileRepository _repo;
        private readonly DBContext _context;

        public ProfileService (IProfileRepository repo, DBContext context)
        {
            _repo = repo;
            _context = context;
        }

        public async Task<object?> GetProfileAsync(int userId)
        {
            var user = await _repo.GetUserProfileAsync(userId);
            if (user == null) return null;

            switch (user.RoleId)
            {
                case "R02": // Doctor
                    var doctor = await _repo.GetDoctorByUserIdAsync(user.UserId);
                    if (doctor == null) return null;

                    return new
                    {
                        doctor.DoctorId,
                        doctor.Name,
                        doctor.Gender,
                        doctor.DateOfBirth,
                        doctor.Phone,
                        doctor.Email,
                        doctor.Address,
                        doctor.Department,
                        doctor.Experience_year,
                        Role = user.Role?.RoleName,
                        Schedules = doctor.Schedules
                            .Select(s => new
                            {
                                s.WorkDate,
                                s.StartTime,
                                s.EndTime,
                                s.Status
                            })
                            .OrderBy(s => s.WorkDate)
                            .ToList()
                    };

                case "R03": // Patient
                    //var patient = await _repo.GetUserByUserIdAsync(user.UserId);
                    //if (patient == null) return null;

                    return new
                    {
                        user.Username,
                        user.Phone,
                        user.Email,
                        Role = user.Role?.RoleName
                    };

                case "R01": // Admin / Clinic Owner
                default:
                    return new
                    {
                        user.UserId,
                        user.Username,
                        user.Email,
                        user.Phone,
                        Role = user.Role?.RoleName
                    };
            }
        }

        public async Task<string> UpdateProfileAsync(int userId, ProfileRequest request)
        {
            var user = await _repo.GetUserProfileAsync(userId);
            if (user == null) return "User not found";

            switch (user.RoleId)
            {
                case "R02": // Doctor
                    var doctor = await _repo.GetDoctorByUserIdAsync(userId);
                    if (doctor == null) return "Doctor profile not found";

                    // 🔹 Lấy danh sách lịch làm việc hiện tại
                    var schedules = await _context.Schedules
                        .Where(s => s.DoctorId == doctor.DoctorId)
                        .OrderBy(s => s.WorkDate)
                        .ToListAsync();

                    // 🔹 Nếu chưa có lịch, sinh mặc định (VD: 5 ngày kế tiếp từ 8h–17h)
                    if (schedules == null || schedules.Count == 0)
                    {
                        var newSchedules = new List<Schedule>();
                        var today = DateTime.Today;

                        for (int i = 0; i < 5; i++)
                        {
                            newSchedules.Add(new Schedule
                            {
                                DoctorId = doctor.DoctorId,
                                WorkDate = DateOnly.FromDateTime(today.AddDays(i)),
                                StartTime = new TimeOnly(8, 0, 0),
                                EndTime = new TimeOnly(17, 0, 0),
                            });
                        }

                        _context.Schedules.AddRange(newSchedules);
                        await _context.SaveChangesAsync();
                        schedules = newSchedules;
                    }

                    // 🔹 Tạo chuỗi tổng hợp thời gian làm việc
                    var workTime = string.Join(", ",
                        schedules.Select(s => $"{s.WorkDate}: {s.StartTime:hh\\:mm}-{s.EndTime:hh\\:mm}"));

                    // 🔹 Cập nhật hồ sơ bác sĩ
                    doctor.Name = request.Name ?? doctor.Name;
                    doctor.Gender = request.Gender ?? doctor.Gender;
                    doctor.DateOfBirth = request.DateOfBirth ?? doctor.DateOfBirth;
                    doctor.Phone = request.Phone ?? doctor.Phone;
                    doctor.Email = request.Email ?? doctor.Email;
                    doctor.Address = request.Address ?? doctor.Address;
                    doctor.Department = request.Department ?? doctor.Department;
                    doctor.Experience_year = request.ExperienceYear ?? doctor.Experience_year;

                    // Nếu bạn muốn gửi thông tin lịch về client:
                    request.WorkTime = workTime;

                    await _repo.UpdateDoctorProfileAsync(doctor);
                    return "Doctor profile updated successfully";

                case "R03": // Patient
                    user.Username = request.Username ?? user.Username;
                    user.Phone = request.Phone ?? user.Phone;
                    user.Email = request.Email ?? user.Email;

                    await _repo.UpdateUserProfileAsync(user);
                    return "Patient profile updated successfully";

                default:
                    return "This role cannot update profile";
            }
        }
    }
}
