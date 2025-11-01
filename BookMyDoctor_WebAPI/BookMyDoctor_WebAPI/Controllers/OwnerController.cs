using BookMyDoctor_WebAPI.Services;
using BookMyDoctor_WebAPI.RequestModel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BookMyDoctor_WebAPI.Repositories;

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
        public async Task<IActionResult> CreateDoctorAccount([FromBody] CreateDoctorRequest request)
        {
            var result = await _ownerService.CreateDoctorAccountAsync(request);

            if (result.Success)
                return Ok(new { result.Message });
            else
                return StatusCode(400, new { result.Message});
        }
    }
}
