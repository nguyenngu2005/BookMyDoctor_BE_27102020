using BookMyDoctor_WebAPI.Services;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OwnerController : ControllerBase
    {
        private readonly IOwnerService _ownerService;

        public OwnerController(IOwnerService adminService)
        {
            _ownerService = adminService;
        }

        [HttpPost("create-doctor")]
        [Authorize(Roles = "R01")]
        public async Task<IActionResult> CreateDoctorAccount([FromBody] CreateDoctorRequest request, CancellationToken ct = default)
        {
            try
            {
                // Nếu muốn tự kiểm soát format lỗi thay vì ProblemDetails mặc định
                if (!ModelState.IsValid)
                {
                    // Lấy thông báo lỗi đầu tiên (giữ code gọn, tránh thay đổi Service)
                    var firstError = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage
                                     ?? "Dữ liệu không hợp lệ.";
                    return BadRequest(new { message = firstError });
                }

                var result = await _ownerService.CreateDoctorAccountAsync(request);

                if (result.Success)
                    return Ok(new { message = result.Message });

                // Thất bại nghiệp vụ → 400 với JSON { message }
                return BadRequest(new { message = result.Message });
            }
            catch (Exception)
            {
                // Ẩn chi tiết exception, trả JSON đồng nhất
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "Đã xảy ra lỗi phía máy chủ khi xử lý yêu cầu.",
                    traceId = HttpContext.TraceIdentifier
                });
            }
        }
    }
}
