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

        // Get all doctor schedules
        [HttpGet("List_All_Schedules_Doctors")]
        public async Task<IActionResult> GetAllSchedules(CancellationToken ct = default)
        {
            var schedules = await _scheduleService.GetAllDoctorSchedulesAsync(ct);
            if (!schedules.Any())
                return NotFound("Không có lịch làm việc nào được tìm thấy.");
            return Ok(schedules);
        }

        // Lấy danh sách lịch làm việc của bác sĩ (có thể lọc theo ngày/ tên)
        [HttpGet("List_Schedules_1_Doctor")]
        public async Task<IActionResult> GetSchedules(
            [FromQuery] string? doctorName = null,
            [FromQuery] DateOnly? date = null,
            CancellationToken ct = default)
        {
            var schedules = await _scheduleService.GetDoctorSchedulesByNameAsync(doctorName, date, ct);
            return Ok(schedules);
        }


        // Thêm mới lịch làm việc cho bác sĩ
        [HttpPost("Add_Schedule_Doctor")]
        [Authorize (Roles = "R02")]
        public async Task<IActionResult> AddSchedule([FromBody] Models.Schedule schedule, CancellationToken ct = default)
        {
            try
            {
                var createdSchedule = await _scheduleService.AddScheduleAsync(schedule, ct);
                return CreatedAtAction(nameof(GetSchedules), new { id = createdSchedule.ScheduleId }, createdSchedule);
            }
            catch (ArgumentNullException ex)
            {
                _logger.LogError(ex, "Invalid input for adding schedule.");
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Error adding schedule: {Message}", ex.Message);
                return Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error adding schedule.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        // Lay chi tiet lich lam viec theo ID
        [HttpGet("Get_Schedule_ById")]
        //[Authorize(Roles = "R01, R02")]
        public async Task<IActionResult> GetScheduleById([FromQuery] int scheduleId, CancellationToken ct = default)
        {
            var schedules = await _scheduleService.GetDoctorSchedulesAsync(
                scheduleId,
                null,
                null,
                ct);
            if (schedules == null || !schedules.Any())
            {
                return NotFound("Schedule not found.");
            }
            return Ok(schedules.First());
        }

        // Cập nhật thông tin lịch làm việc
        [HttpPut("Update_Schedule_Doctor")]
        [Authorize (Roles = "R02")]
        public async Task<IActionResult> UpdateSchedule([FromBody] Models.Schedule schedule, CancellationToken ct = default)
        {
            try
            {
                bool updated = await _scheduleService.UpdateScheduleAsync(schedule, ct);
                if (updated)
                    return NoContent();
                else
                    return NotFound("Schedule not found.");
            }
            catch (ArgumentNullException ex)
            {
                _logger.LogError(ex, "Invalid input for updating schedule.");
                return BadRequest(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "Error updating schedule: {Message}", ex.Message);
                return Conflict(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error updating schedule.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }

        // Xóa lịch làm việc
        [HttpDelete("Delete_Schedule_Doctor")]
        [Authorize (Roles = "R01, R02")]
        public async Task<IActionResult> DeleteSchedule([FromQuery] int scheduleId, CancellationToken ct = default)
        {
            try
            {
                bool deleted = await _scheduleService.DeleteScheduleAsync(scheduleId, ct);
                if (deleted)
                    return Ok(new { message = "Xóa lịch thành công." });
                else
                    return NotFound("Schedule not found.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error deleting schedule.");
                return StatusCode(StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
            }
        }


    }
}
