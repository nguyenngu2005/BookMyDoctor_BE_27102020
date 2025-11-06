using BookMyDoctor_WebAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BookMyDoctor_WebAPI.RequestModel;

namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PatientsController : ControllerBase
    {
        private readonly IPatientService _service;
        private readonly ILogger<PatientsController> _logger;

        public PatientsController(IPatientService service, ILogger<PatientsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        // ==================== LẤY DANH SÁCH ====================
        // GET: api/patient?search=abc&workDate=2025-10-10
        [HttpGet("AllPatientsAndSearch")]
        [Authorize(Roles = "R01, R02")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetAllPatients(
        [FromQuery] string? name,
        [FromQuery] DateTime? appointDate,
        [FromQuery] string? status,
        [FromQuery] int? doctorId,                 // ✅ thêm filter theo bác sĩ
        CancellationToken ct)
        {
            try
            {
                var result = await _service.GetAllPatientsAsync(name, appointDate, status, doctorId, ct);

                if (result == null || !result.Any())
                    return NotFound(new { message = "Không tìm thấy bệnh nhân nào." });

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy danh sách bệnh nhân");
                return StatusCode(500, new { message = "Đã xảy ra lỗi nội bộ. Vui lòng thử lại sau." });
            }
        }


        // ==================== LẤY CHI TIẾT ====================
        //// GET: api/patient/5
        //[HttpGet("DetailPatient")]
        //[Authorize(Roles = "R01, R02")]
        //public async Task<IActionResult> GetPatientDetail(int id, CancellationToken ct)
        //{
        //    try
        //    {
        //        var patient = await _service.GetPatientDetailAsync(id, ct);
        //        if (patient == null)
        //            return NotFound(new { message = "Không tìm thấy thông tin bệnh nhân." });

        //        return Ok(patient);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, $"Lỗi khi lấy thông tin bệnh nhân ID = {id}");
        //        return StatusCode(500, new { message = ex.Message });
        //    }
        //}

        // =============== HISTORY CUA MOT USER ===============

        [HttpGet("MyHistoryAppoint")]
        [Authorize(Roles = "R03")]
        public async Task<IActionResult> GetPatientHistoryAsync(CancellationToken ct)
        {
            // 🧠 Lấy userId từ token
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized("Không xác định được người dùng đăng nhập.");

            int userId = int.Parse(userIdClaim);

            var patients = await _service.GetPatientHistoryAsync(userId, ct);

            if (patients == null || !patients.Any())
                return NotFound("Không có bệnh nhân nào thuộc tài khoản này.");

            return Ok(patients);
        }

        // ==================== CẬP NHẬT ====================
        // PUT: api/patient/5
        [HttpPut("UpdatePatient")]
        [Authorize(Roles = "R02")]
        public async Task<IActionResult> UpdatePatient(int patientId, DateOnly appointDate, TimeOnly appointHour, [FromBody] PatientUpdateRequest dto, CancellationToken ct)
        {
            if (dto == null)
                return BadRequest(new { message = "Dữ liệu gửi lên không hợp lệ." });

            try
            {
                var result = await _service.UpdatePatientAsync(patientId, appointDate, appointHour, dto, ct);
                if (!result.Success)
                    return NotFound(new { message = result.Message});

                return Ok(new { message = "Cập nhật triệu chứng và toa thuốc thành công." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi cập nhật bệnh nhân ID hoac gio kham = {patientId}, {appointDate}, {appointHour}");
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // ==================== XÓA ====================
        // DELETE: api/patient/5
        [HttpDelete("DeletePatient")]
        [Authorize(Roles = "R01")]
        public async Task<IActionResult> DeletePatient(int id, CancellationToken ct)
        {
            try
            {
                var deleted = await _service.DeletePatientAsync(id, ct);
                if (!deleted)
                    return NotFound(new { message = "Không tìm thấy bệnh nhân để xóa." });

                return Ok(new { message = "Xóa bệnh nhân thành công." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Lỗi khi xóa bệnh nhân ID = {id}");
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
