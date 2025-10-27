using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/booking")]
public class BookingController : ControllerBase
{
    private readonly IBookingService _svc;
    public BookingController(IBookingService svc) => _svc = svc;

    /// 1) Đặt lịch khi CHƯA đăng nhập (AllowAnonymous): lưu theo Email, gửi mail xác nhận
    [HttpPost("public")]
    [AllowAnonymous]
    public async Task<IActionResult> PublicBook([FromBody] PublicBookingRequest req, CancellationToken ct)
    {
        var result = await _svc.PublicBookAsync(req, ct);
        return Ok(result);
    }

    /// 2) Đặt lịch khi ĐÃ đăng nhập (chỉ R03): theo PrivateBookingRequest, gửi mail xác nhận
    [HttpPost("private")]
    [Authorize(Roles = "R03")]
    public async Task<IActionResult> PrivateBook([FromBody] PrivateBookingRequest req, CancellationToken ct)
    {
        var result = await _svc.PrivateBookAsync(User, req, ct);
        return Ok(result);
    }

    [HttpGet("info_slot_busy")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBusySlots(
    [FromQuery] int doctorId,
    [FromQuery] DateTime date,
    CancellationToken ct)
    {
        if (doctorId <= 0)
            return BadRequest("doctorId is required and must be greater than 0.");

        var busySlots = await _svc.GetBusySlotsAsync(doctorId, date, ct);

        if (busySlots == null || !busySlots.Any())
            return Ok(new List<object>());

        return Ok(busySlots);
    }

    [HttpDelete("cancel/{bookingId}")]
    public async Task<IActionResult> CancelBooking(
        [FromRoute] int bookingId,
        CancellationToken ct)
    {
        var result = await _svc.DeleteAppointmentAsync(bookingId, ct);
        return result ? NoContent() : NotFound();
    }
}
