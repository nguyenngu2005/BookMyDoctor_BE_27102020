using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
using Microsoft.EntityFrameworkCore;

public class ScheduleGeneratorService
{
    private readonly DBContext _context;
    private readonly ILogger<ScheduleGeneratorService> _logger;

    public ScheduleGeneratorService(DBContext context, ILogger<ScheduleGeneratorService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task GenerateNextMonthSchedulesAsync()
    {
        _logger.LogInformation("Bắt đầu sinh lịch làm việc mới cho bác sĩ...");

        var doctors = await _context.Doctors.ToListAsync();
        var today = DateTime.Today;

        foreach (var doctor in doctors)
        {
            var latest = await _context.Schedules
                .Where(s => s.DoctorId == doctor.DoctorId)
                .OrderByDescending(s => s.WorkDate)
                .FirstOrDefaultAsync();

            if (latest == null)
                continue;

            // Nếu lịch sắp hết (chỉ còn 1 tuần) thì sinh thêm
            if (latest.WorkDate.AddDays(7) <= DateOnly.FromDateTime(today))
            {
                var defaultDays = new[]
                {
                    DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
                };
                var startTime = new TimeOnly(8, 0);
                var endTime = new TimeOnly(17, 0);
                int weeksToGenerate = 4;

                var newSchedules = new List<Schedule>();
                var startDate = latest.WorkDate.ToDateTime(TimeOnly.MinValue).AddDays(1);

                foreach (var day in defaultDays)
                {
                    var offset = ((int)day - (int)startDate.DayOfWeek + 7) % 7;
                    var firstWorkDate = startDate.AddDays(offset);

                    for (int i = 0; i < weeksToGenerate; i++)
                    {
                        var workDate = firstWorkDate.AddDays(i * 7);
                        var dateOnly = DateOnly.FromDateTime(workDate);

                        if (await _context.Schedules.AnyAsync(s =>
                            s.DoctorId == doctor.DoctorId && s.WorkDate == dateOnly))
                            continue;

                        newSchedules.Add(new Schedule
                        {
                            DoctorId = doctor.DoctorId,
                            WorkDate = dateOnly,
                            StartTime = startTime,
                            EndTime = endTime,
                            Status = "Scheduled",
                            IsActive = true
                        });
                    }
                }

                await _context.Schedules.AddRangeAsync(newSchedules);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Sinh {Count} lịch mới cho bác sĩ {Name}", newSchedules.Count, doctor.Name);
            }
        }

        _logger.LogInformation("Hoàn tất sinh lịch làm việc tự động.");
    }
}
