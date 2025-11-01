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
        private readonly ILogger<DoctorsController> _logger; // match the injected generic type

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
            var result = await _service.GetDoctorByIdAsync(id, ct);
            return result is null ? NotFound() : Ok(result);
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
                    return NotFound("No doctors found matching the criteria.");

                return Ok(doctors);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while searching doctors");
                return StatusCode(500, "An error occurred while processing your request.");
            }
        }

        [HttpDelete("DeleteDoctor")]
        [Authorize(Roles = "R01")]
        public async Task<IActionResult> Delete([FromQuery] int id, CancellationToken ct)
        {
            var success = await _service.DeleteDoctorAsync(id, ct);
            return success ? NoContent() : NotFound();
        }
    }
}
