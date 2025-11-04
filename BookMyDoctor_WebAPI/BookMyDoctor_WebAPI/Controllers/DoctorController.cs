using BookMyDoctor_WebAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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

        [HttpGet("Get-A-Doctor")]
        [Authorize]
        public async Task<IActionResult> GetById([FromQuery] int id, CancellationToken ct)
        {
            if (id <= 0)
                return BadRequest(new { message = "Tham số id không hợp lệ." });

            var result = await _service.GetDoctorByIdAsync(id, ct);
            if (result is null)
                return NotFound(new { message = $"Không tìm thấy bác sĩ với mã id = {id}." });

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

            var success = await _service.DeleteDoctorAsync(id, ct);

            if (!success)
                return NotFound(new { message = $"Không tìm thấy bác sĩ để xóa (id = {id})." });

            return Ok(new { message = "Xóa bác sĩ thành công." });
        }
    }
}
