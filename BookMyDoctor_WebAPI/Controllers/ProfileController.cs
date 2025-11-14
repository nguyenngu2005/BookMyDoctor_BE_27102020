using BookMyDoctor_WebAPI.RequestModel;
using BookMyDoctor_WebAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ProfileController : ControllerBase
    {
        private readonly IProfileService _profileService;

        public ProfileController(IProfileService profileService)
        {
            _profileService = profileService;
        }

        // ============================
        // LẤY PROFILE HIỆN TẠI
        // ============================
        [HttpGet("profile-me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            try
            {
                var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(idClaim))
                    return Unauthorized(new { message = "Bạn chưa đăng nhập." });

                int userId;
                if (!int.TryParse(idClaim, out userId))
                    return BadRequest(new { message = "UserId không hợp lệ trong token/cookie." });

                var profile = await _profileService.GetProfileAsync(userId);
                if (profile == null)
                    return NotFound(new { message = "Không tìm thấy hồ sơ người dùng." });

                return Ok(profile);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }

        // ============================
        // UPDATE PROFILE
        // ============================
        [HttpPut("update-profile-me")]
        [Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileRequest request)
        {
            try
            {
                // Lấy userId từ claim
                var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrWhiteSpace(idClaim))
                    return Unauthorized(new { message = "Bạn chưa đăng nhập." });

                if (!int.TryParse(idClaim, out int userId))
                    return BadRequest(new { message = "UserId không hợp lệ trong token/cookie." });

                // Validate request model
                if (!ModelState.IsValid)
                {
                    var errors = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .Where(m => !string.IsNullOrWhiteSpace(m));

                    return BadRequest(new { message = string.Join("; ", errors) });
                }

                // Gọi service update
                var result = await _profileService.UpdateProfileAsync(userId, request);

                // Nếu service return string lỗi → BadRequest
                if (result.Contains("not found", StringComparison.InvariantCultureIgnoreCase))
                    return NotFound(new { message = result });

                return Ok(new { message = result });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Lỗi hệ thống: {ex.Message}" });
            }
        }
    }
}
