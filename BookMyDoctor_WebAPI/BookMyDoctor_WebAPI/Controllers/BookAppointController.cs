using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/booking")]
public class BookingController : ControllerBase
{
    private readonly IBookingService _svc;
    public BookingController(IBookingService svc) => _svc = svc;

    // Helper trả lỗi theo format FE yêu cầu
    private IActionResult ErrorResponse(string message, int status = 400)
        => StatusCode(status, new { message });

    /// 1) Đặt lịch không login
    [HttpPost("public")]
    [AllowAnonymous]
    public async Task<IActionResult> PublicBook([FromBody] PublicBookingRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _svc.PublicBookAsync(req, ct);
            return Ok(result);
        }
        catch (AppException ex)
        {
            return ErrorResponse(ex.Message, ex.StatusCode);
        }
        catch (Exception)
        {
            return ErrorResponse("Đã xảy ra lỗi, vui lòng thử lại.");
        }
    }

    /// 2) Đặt lịch khi login (R03)
    [HttpPost("private")]
    [Authorize(Roles = "R03")]
    public async Task<IActionResult> PrivateBook([FromBody] PrivateBookingRequest req, CancellationToken ct)
    {
        try
        {
            var result = await _svc.PrivateBookAsync(User, req, ct);
            return Ok(result);
        }
        catch (ForbiddenException ex)
        {
            return ErrorResponse(ex.Message, 403);
        }
        catch (AppException ex)
        {
            return ErrorResponse(ex.Message, ex.StatusCode);
        }
        catch (Exception)
        {
            return ErrorResponse("Đã xảy ra lỗi, vui lòng thử lại.");
        }
    }

    [HttpGet("info_slot_busy")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBusySlots(
        [FromQuery] int doctorId,
        [FromQuery] DateTime date,
        CancellationToken ct)
    {
        try
        {
            if (doctorId <= 0)
                return ErrorResponse("doctorId is required and must be > 0");

            var busySlots = await _svc.GetBusySlotsAsync(doctorId, date, ct);
            return Ok(busySlots ?? Enumerable.Empty<BusySlot>());
        }
        catch
        {
            return ErrorResponse("Không thể tải dữ liệu lịch hẹn.");
        }
    }

    [HttpDelete("cancel/{bookingId}")]
    public async Task<IActionResult> CancelBooking(
        [FromRoute] int bookingId,
        CancellationToken ct)
    {
        try
        {
            var result = await _svc.DeleteAppointmentAsync(bookingId, ct);
            return result ? NoContent() : ErrorResponse("Không tìm thấy lịch hẹn.", 404);
        }
        catch
        {
            return ErrorResponse("Không thể huỷ lịch hẹn.");
        }
    }
}