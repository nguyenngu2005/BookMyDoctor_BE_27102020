using System.Security.Claims;
using BookMyDoctor_WebAPI.RequestModel;
using BookMyDoctor_WebAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/patients")]
    public sealed class PatientController : ControllerBase
    {
        private readonly IPatientService _svc;
        public PatientController(IPatientService svc) => _svc = svc;

        // Helper: chỉ trả { message }
        private IActionResult Error(string message, int status = 400)
            => StatusCode(status, new { message });

        // ==================== LIST ====================
        // GET /api/patients?search=&appointDate=2025-11-01&status=Scheduled
        [HttpGet]
        [Authorize(Roles = "R01,R02")] // ví dụ: R01-Admin, R02-Doctor
        public async Task<IActionResult> GetAll(
            [FromQuery(Name = "search")] string? search,
            [FromQuery(Name = "appointDate")] DateTime? appointDate,
            [FromQuery(Name = "status")] string? status,
            CancellationToken ct)
        {
            var data = await _svc.GetAllPatientsAsync(search, appointDate, status, ct);
            return Ok(data);
        }

        // ==================== DETAIL ====================
        // GET /api/patients/123
        [HttpGet("{patientId:int}")]
        [Authorize(Roles = "R01,R02")]
        public async Task<IActionResult> GetDetail([FromRoute] int patientId, CancellationToken ct)
        {
            if (patientId <= 0)
                return Error("patientId phải lớn hơn 0.", 400);

            var data = await _svc.GetPatientDetailAsync(patientId, ct);
            if (data is null)
                return Error("Không tìm thấy bệnh nhân.", 404);

            return Ok(data);
        }

        // ==================== HISTORY (theo user hiện tại) ====================
        // GET /api/patients/history/me
        [HttpGet("history/me")]
        [Authorize(Roles = "R03")] // R03: Patient
        public async Task<IActionResult> GetHistoryOfCurrentUser(CancellationToken ct)
        {
            // Lấy userId từ token (ưu tiên 'uid', fallback 'sub')
            var userIdStr = User.FindFirstValue("uid") ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
            if (!int.TryParse(userIdStr, out var userId) || userId <= 0)
                return Error("Không xác định được userId từ token.", 401);

            var history = await _svc.GetPatientHistoryAsync(userId, ct);
            return Ok(history);
        }

        // ==================== UPDATE (triệu chứng/đơn thuốc lịch gần nhất) ====================
        // PUT /api/patients/123/update?appointDate=2025-11-01&appointHour=09:00
        [HttpPut("{patientId:int}/update")]
        [Authorize(Roles = "R01,R02")]
        public async Task<IActionResult> Update(
            [FromRoute] int patientId,
            [FromQuery] DateOnly? appointDate,
            [FromQuery] TimeOnly? appointHour,
            [FromBody] PatientUpdateRequest dto,
            CancellationToken ct)
        {
            // Validate cơ bản → chỉ trả {message}
            if (patientId <= 0)
                return Error("patientId phải lớn hơn 0.", 400);

            if (!appointDate.HasValue)
                return Error("Thiếu appointDate (yyyy-MM-dd).", 400);

            if (!appointHour.HasValue)
                return Error("Thiếu appointHour (HH:mm).", 400);

            if (!string.IsNullOrWhiteSpace(dto.Prescription) && dto.Prescription!.Length > 2000)
                return Error("Đơn thuốc tối đa 2000 ký tự.", 400);

            if (!string.IsNullOrWhiteSpace(dto.Symptoms) && dto.Symptoms!.Length > 1000)
                return Error("Triệu chứng tối đa 1000 ký tự.", 400);

            var result = await _svc.UpdatePatientAsync(
                patientId,
                appointDate.Value,
                appointHour.Value,
                dto,
                ct);

            if (!result.Success)
            {
                // Map lỗi service → chỉ {message}
                var status = result.Error switch
                {
                    AuthError.NotFound => 404,
                    AuthError.BadRequest => 400,
                    _ => 500
                };

                var message = result.Message ?? (status == 404
                    ? "Không tìm thấy bệnh nhân hoặc chưa có cuộc hẹn để cập nhật."
                    : status == 400
                        ? "Yêu cầu không hợp lệ."
                        : "Lỗi hệ thống. Vui lòng thử lại sau.");

                return Error(message, status);
            }

            return Ok(new { message = "Cập nhật thành công." });
        }

        // ==================== DELETE ====================
        // DELETE /api/patients/123
        [HttpDelete("{patientId:int}")]
        [Authorize(Roles = "R01")] // chỉ Admin
        public async Task<IActionResult> Delete([FromRoute] int patientId, CancellationToken ct)
        {
            if (patientId <= 0)
                return Error("patientId phải lớn hơn 0.", 400);

            var ok = await _svc.DeletePatientAsync(patientId, ct);
            if (!ok)
                return Error("Không tìm thấy bệnh nhân để xoá.", 404);

            return Ok(new { message = "Xoá thành công." });
        }
    }
}
