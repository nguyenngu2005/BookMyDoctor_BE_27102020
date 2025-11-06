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

        // Luôn trả 200 + message khi lỗi
        private IActionResult OkError(string msg) => Ok(new { message = msg });

        // ===== 1) BOOK PUBLIC =====
        [HttpPost("public")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(BookingResult), StatusCodes.Status200OK)]
        public async Task<IActionResult> PublicBook([FromBody] PublicBookingRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? e.Exception?.Message : e.ErrorMessage)
                    .Where(m => !string.IsNullOrWhiteSpace(m)));

                return OkError(string.IsNullOrWhiteSpace(errors) ? "Invalid request." : errors);
            }

            try
            {
                // Lấy userId nếu có login (không login = null)
                int? currentUserId = null;
                if (User?.Identity?.IsAuthenticated == true)
                {
                    var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier)
                               ?? User.FindFirstValue("uid")
                               ?? User.FindFirstValue("sub");
                    if (int.TryParse(idStr, out var uid)) currentUserId = uid;
                }

                // Gọi unified flow
                var result = await _svc.BookAsync(req, currentUserId, ct);
                return Ok(result);
            }
            catch (DbUpdateException)
            {
                return OkError("Khung giờ này vừa có người đặt. Vui lòng chọn giờ khác.");
            }
            catch (Exception ex)
            {
                return OkError(ex.Message);
            }
        }

        // ===== 2) SLOT BẬN =====
        [HttpGet("info_slot_busy")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(IEnumerable<BusySlot>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetBusySlots([FromQuery] int doctorId, [FromQuery] string date, CancellationToken ct)
        {
            try
            {
                if (doctorId <= 0) return OkError("doctorId is required and must be > 0");
                if (!DateOnly.TryParse(date, out var d)) return OkError("date must be yyyy-MM-dd");

                var busy = await _svc.GetBusySlotsAsync(doctorId, d.ToDateTime(TimeOnly.MinValue), ct);
                return Ok(busy);
            }
            catch (Exception ex)
            {
                return OkError(ex.Message);
            }
        }

        // ===== 3) CANCEL APPOINTMENT =====
        [HttpDelete("cancel/{bookingId:int}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<IActionResult> CancelBooking([FromRoute] int bookingId, CancellationToken ct)
        {
            try
            {
                var ok = await _svc.DeleteAppointmentAsync(bookingId, ct);
                return ok ? Ok(new { message = "Cancelled." }) : OkError("Không tìm thấy lịch hẹn.");
            }
            catch (Exception ex)
            {
                return OkError(ex.Message);
            }
        }
    }
}
