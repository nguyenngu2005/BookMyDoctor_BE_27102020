using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BookMyDoctor_WebAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public sealed class BookingController : ControllerBase
    {
        private readonly IBookingService _svc;
        public BookingController(IBookingService svc) => _svc = svc;

        // ============================
        // 1) CREATE PUBLIC BOOKING
        // ============================
        [HttpPost("public")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(BookingResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> PublicBook([FromBody] PublicBookingRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage)
                    .Where(m => !string.IsNullOrWhiteSpace(m));

                return BadRequest(new { message = string.Join("; ", errors) });
            }

            try
            {
                // Lấy userId nếu có login
                int? currentUserId = null;
                if (User?.Identity?.IsAuthenticated == true)
                {
                    var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                            ?? User.FindFirstValue("uid")
                            ?? User.FindFirstValue("sub");

                    if (int.TryParse(idStr, out var uid)) currentUserId = uid;
                }

                var result = await _svc.BookAsync(req, currentUserId, ct);

                return Ok(result); // Booking thành công
            }
            catch (DbUpdateException)
            {
                // Lỗi trùng lịch (unique constraint)
                return Conflict(new { message = "Khung giờ này vừa có người đặt. Vui lòng chọn giờ khác." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // ============================
        // 2) GET BUSY SLOT BY DATE
        // ============================
        [HttpGet("info_slot_busy")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(IEnumerable<BusySlot>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetBusySlots([FromQuery] int doctorId, [FromQuery] string date, CancellationToken ct)
        {
            try
            {
                if (doctorId <= 0)
                    return BadRequest(new { message = "doctorId phải > 0" });

                if (!DateOnly.TryParse(date, out var d))
                    return BadRequest(new { message = "ngày phải có định dạng yyyy-MM-dd" });

                var busy = await _svc.GetBusySlotsAsync(doctorId, d.ToDateTime(TimeOnly.MinValue), ct);
                return Ok(busy);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }

        // ============================
        // 3) CANCEL BOOKING
        // ============================
        [HttpDelete("cancel/{bookingId:int}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> CancelBooking([FromRoute] int bookingId, CancellationToken ct)
        {
            try
            {
                var ok = await _svc.DeleteAppointmentAsync(bookingId, ct);

                if (!ok)
                    return NotFound(new { message = "Không tìm thấy lịch hẹn." });

                return Ok(new { message = "Cancelled." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = ex.Message });
            }
        }
    }
}
