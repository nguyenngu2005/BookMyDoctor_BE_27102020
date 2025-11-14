using BookMyDoctor_WebAPI.RequestModel;
using BookMyDoctor_WebAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BookMyDoctor_WebAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ScheduleController : ControllerBase
    {
        private readonly IScheduleService _scheduleService;
        private readonly ILogger<ScheduleController> _logger;

        public ScheduleController(IScheduleService scheduleService, ILogger<ScheduleController> logger)
        {
            _scheduleService = scheduleService;
            _logger = logger;
        }

        // ===========================================
        // 1) Lấy tất cả lịch của tất cả bác sĩ
        // ===========================================
        [HttpGet("List_All_Schedules_Doctors")]
        public async Task<IActionResult> GetAllSchedules(CancellationToken ct = default)
        {
            var schedules = await _scheduleService.GetAllDoctorSchedulesAsync(ct);
            if (!schedules.Any())
                return NotFound(new { message = "Không có lịch làm việc nào được tìm thấy." });

            return Ok(schedules);
        }

        // ===========================================
        // 2) Lấy lịch theo tên bác sĩ hoặc theo ngày
        // ===========================================
        [HttpGet("List_Schedules_1_Doctor")]
        public async Task<IActionResult> GetSchedules(
            [FromQuery] string? doctorName = null,
            [FromQuery] DateOnly? date = null,
            CancellationToken ct = default)
        {
            var schedules = await _scheduleService.GetDoctorSchedulesByNameAsync(doctorName, date, ct);
            return Ok(schedules);
        }

        // ===========================================
        // 3) Thêm lịch mới (DÙNG DTO)
        // ===========================================
        [HttpPost("Add_Schedule_Doctor")]
        [Authorize(Roles = "R02")]
        public async Task<IActionResult> AddSchedule([FromBody] AddScheduleRequest? req, CancellationToken ct = default)
        {
            try
            {
                // 1) Body rỗng / đọc JSON fail
                if (req is null)
                {
                    return BadRequest(new { message = "Dữ liệu gửi lên rỗng hoặc không đúng định dạng JSON." });
                }

                // 2) Validate model
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                                        ? e.Exception?.Message
                                        : e.ErrorMessage)
                        .Where(m => !string.IsNullOrWhiteSpace(m));

                    return BadRequest(new { message = string.Join("; ", errors) });
                }

                var schedule = new Models.Schedule
                {
                    DoctorId = req.DoctorId,
                    WorkDate = req.WorkDate,
                    StartTime = req.StartTime,
                    EndTime = req.EndTime,
                    Status = req.Status,
                    IsActive = req.IsActive
                };

                var createdSchedule = await _scheduleService.AddScheduleAsync(schedule, ct);

                return CreatedAtAction(nameof(GetSchedules),
                    new { doctorName = createdSchedule.DoctorId },
                    createdSchedule);
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "Sai định dạng khi thêm lịch");
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Lỗi khi thêm vào lịch: {Message}", ex.Message);
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Các lỗi khác khi thêm vào hệ thống");
                return StatusCode(500, new { message = "Lỗi hệ thống. Vui lòng thử lại." });
            }
        }


        // ===========================================
        // 4) Lấy chi tiết 1 lịch theo ScheduleId
        // ===========================================
        [HttpGet("Get_Schedule_ById")]
        public async Task<IActionResult> GetScheduleById([FromQuery] int scheduleId, CancellationToken ct = default)
        {
            var all = await _scheduleService.GetAllDoctorSchedulesAsync(ct);
            var schedule = all.FirstOrDefault(s => s.ScheduleId == scheduleId);

            if (schedule == null)
                return NotFound(new { message = "Không tìm thấy lịch." });

            return Ok(schedule);
        }

        // ===========================================
        // 5) Cập nhật lịch (DÙNG DTO)
        // ===========================================
        [HttpPut("Update_Schedule_Doctor")]
        [Authorize(Roles = "R02")]
        public async Task<IActionResult> UpdateSchedule([FromBody] UpdateScheduleRequest req, CancellationToken ct = default)
        {
            try
            {
                var schedule = new Models.Schedule
                {
                    ScheduleId = req.ScheduleId,
                    DoctorId = req.DoctorId,
                    WorkDate = req.WorkDate,
                    StartTime = req.StartTime,
                    EndTime = req.EndTime,
                    Status = req.Status,
                    IsActive = req.IsActive
                };

                bool updated = await _scheduleService.UpdateScheduleAsync(schedule, ct);
                if (!updated)
                    return NotFound(new { message = "Không tìm thấy lịch để cập nhật." });

                return Ok(new { message = "Cập nhật lịch thành công." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating schedule.");
                return StatusCode(500, new { message = "Lỗi hệ thống." });
            }
        }

        // ===========================================
        // 6) Xóa lịch
        // ===========================================
        [HttpDelete("Delete_Schedule_Doctor")]
        [Authorize(Roles = "R01,R02")]
        public async Task<IActionResult> DeleteSchedule([FromQuery] int scheduleId, CancellationToken ct = default)
        {
            try
            {
                bool deleted = await _scheduleService.DeleteScheduleAsync(scheduleId, ct);
                if (!deleted)
                    return NotFound(new { message = "Không tìm thấy lịch để xoá." });

                return Ok(new { message = "Xóa lịch thành công." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting schedule.");
                return StatusCode(500, new { message = "Lỗi hệ thống." });
            }
        }
    }
}
