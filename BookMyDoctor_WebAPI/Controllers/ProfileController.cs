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

        // LẤY THÔNG TIN PROFILE HIỆN TẠI

        [HttpGet("profile-me")]
        [Authorize] // Cookie-based auth
        public async Task<IActionResult> Me()
        {
            // Lấy userId từ cookie claim
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim))
                return Unauthorized(new { message = "User not logged in" });

            int userId = int.Parse(userIdClaim);

            // Lấy profile theo Role
            var profile = await _profileService.GetProfileAsync(userId);
            if (profile == null)
                return NotFound(new { message = "Profile not found" });

            return Ok(profile);
        }


        [HttpPut("Update_Profile_Me")]
        //[Authorize] // Cookie-based auth
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileRequest request)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var message = await _profileService.UpdateProfileAsync(userId, request);
            return Ok(new { message });
        }


        //public async Task<IActionResult> UpdateProfile([FromBody] ProfileRequest request)
        //{
        //    var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        //    var role = User.FindFirstValue(ClaimTypes.Role);

        //    Console.WriteLine($"[DEBUG] Role from cookie/token: {role}");

        //    string message;

        //    if (role == "Patient")
        //        message = await _profileService.UpdateProfileAsync(userId, request);
        //    else if (role == "Doctor")
        //        message = await _profileService.UpdateProfileAsync(userId, request);
        //    else
        //        return Forbid();

        //    return Ok(new { message });
        //}
    }
}
