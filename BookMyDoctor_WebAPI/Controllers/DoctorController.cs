using BookMyDoctor_WebAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DoctorsController : ControllerBase
    {
        private readonly IDoctorService _service;
        private readonly ILogger<DoctorsController> _logger;

        public DoctorsController(IDoctorService service, ILogger<DoctorsController> logger)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [HttpGet("All-Doctors")]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            var result = await _service.GetAllDoctorsAsync(ct);
            return Ok(result);
        }
        [HttpGet("Search-Doctors")]
        public async Task<IActionResult> SearchDoctors(
            [FromQuery] string? name,
            [FromQuery] string? department,
            [FromQuery] string? gender,
            [FromQuery] string? phone,
            [FromQuery] DateTime? workDate,
            CancellationToken ct = default)
        {
            try
            {
                DateOnly? dateOnly = workDate.HasValue
                    ? DateOnly.FromDateTime(workDate.Value)
                    : null;

                var doctors = await _service.SearchDoctorAsync(
                    name, department, gender, phone, dateOnly, ct);

                if (doctors == null || !doctors.Any())
                    return NotFound(new { message = "Không tìm thấy bác sĩ phù hợp với tiêu chí tìm kiếm." });

                return Ok(doctors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tìm kiếm bác sĩ - TraceId: {TraceId}", HttpContext.TraceIdentifier);

                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Đã xảy ra lỗi phía máy chủ khi xử lý yêu cầu.",
                    traceId = HttpContext.TraceIdentifier
                });
            }
        }

        [HttpDelete("DeleteDoctor")]
        [Authorize(Roles = "R01")]
        public async Task<IActionResult> Delete([FromQuery] int id, CancellationToken ct)
        {
            if (id <= 0)
                return BadRequest(new { message = "Tham số id không hợp lệ." });

            try
            {
                await _service.DeleteDoctorAsync(id, ct);
                return Ok(new { message = "Xóa bác sĩ thành công." });
            }
            catch (AppException ax)
            {
                return StatusCode(ax.StatusCode, new { message = ax.Message });
            }
        }

        // Lay toan bo appoint cua doctor
        [HttpGet("GetDoctorAppointments")]
        [Authorize(Roles = "R01, R02")]
        public async Task<IActionResult> GetAppointments(
            [FromQuery] int doctorId,
            [FromQuery] string patientName,
            [FromQuery] string patientPhone,
            CancellationToken ct = default)
        {
            if (doctorId <= 0)
                return BadRequest(new { message = "doctorId phải > 0" });

            try
            {
                var appointments = await _service.GetDoctorAppointmentsAsync(doctorId, patientName, patientPhone, ct);
                if (appointments == null || !appointments.Any())
                    return NotFound(new { message = "Không tìm thấy cuộc hẹn nào cho bác sĩ này." });

                return Ok(appointments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy appointments của bác sĩ ID = {DoctorId}", doctorId);
                return StatusCode(500, new { message = "Đã xảy ra lỗi nội bộ. Vui lòng thử lại sau." });
            }
        }
    }
}
