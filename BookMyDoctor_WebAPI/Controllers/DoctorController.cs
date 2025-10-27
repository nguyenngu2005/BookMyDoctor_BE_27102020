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
    private readonly ILogger _logger;

    public DoctorsController(IDoctorService service, ILogger<DoctorsController> logger)
    {
        _service = service;
        _logger = logger;
    }

        [HttpGet("All-Doctors")]
        public async Task<IActionResult> GetAll(CancellationToken ct)
        {
            var result = await _service.GetAllDoctorsAsync(ct);
            return Ok(result);
        }

        [HttpGet("Get-A-Doctor")]
        [Authorize]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
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
            [FromQuery] DateTime? workDate,   // nhận DateTime để parse từ query string
            CancellationToken ct = default)
        {
            try
            {
                DateOnly? dateOnly = null;
                if (workDate.HasValue)
                    dateOnly = DateOnly.FromDateTime(workDate.Value);

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
        //[HttpPost("Add-Doctor")]
        //[Authorize(Roles = "Admin")]
        //public async Task<IActionResult> Add([FromBody] Doctor doctor, CancellationToken ct)
        //{
        //    var created = await _service.AddDoctorAsync(doctor);
        //    return CreatedAtAction(nameof(GetById), new { id = created.DoctorId }, created);
        //}

        //[HttpPut("Update-Doctor/{id}")]
        //[Authorize/*(Roles = "Doctor, Admin")*/]
        //public async Task<IActionResult> Update(int id, [FromBody] Doctor doctor, CancellationToken ct)
        //{
        //    doctor.DoctorId = id;
        //    var success = await _service.UpdateDoctorAsync(doctor, ct);
        //    return success ? NoContent() : NotFound();
        //}

        [HttpDelete("DeleteDoctor")]
        [Authorize(Roles = "R01")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var success = await _service.DeleteDoctorAsync(id, ct);
            return success ? NoContent() : NotFound();
        }
    }

}
