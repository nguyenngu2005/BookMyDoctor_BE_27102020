// Magic. Don't touch
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using BookMyDoctor_WebAPI.Models;
using BookMyDoctor_WebAPI.Repositories;

#region Custom Exceptions
public class AppException : Exception
{
    public int StatusCode { get; }
    public AppException(string message, int statusCode = 400) : base(message)
        => StatusCode = statusCode;
}
public sealed class ForbiddenException : AppException
{
    public ForbiddenException(string message) : base(message, 403) { }
}
#endregion

public interface IBookingService
{
    Task<BookingResult> BookAsync(PublicBookingRequest req, int? currentUserId, CancellationToken ct);
    Task<BookingResult> PublicBookAsync(PublicBookingRequest req, CancellationToken ct);
    Task<IEnumerable<BusySlot>> GetBusySlotsAsync(int doctorId, DateTime date, CancellationToken ct);
    Task<bool> DeleteAppointmentAsync(int appointmentId, CancellationToken ct);
}


public sealed partial class BookingService : IBookingService
{
    private readonly IBookingRepository _repo;
    private readonly IConfiguration _config;

    // Windows: "SE Asia Standard Time"; Linux: "Asia/Ho_Chi_Minh"
    private static TimeZoneInfo ResolveTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
            catch { return TimeZoneInfo.Local; }
        }
    }
    private readonly TimeZoneInfo _tzVN = ResolveTz();

    public BookingService(IBookingRepository repo, IConfiguration config)
    { _repo = repo; _config = config; }

    // ==============================
    // Unified flow cho endpoint /public
    // ==============================
    public async Task<BookingResult> BookAsync(PublicBookingRequest req, int? currentUserId, CancellationToken ct)
    {
        // 1) Validate cơ bản
        GuardEmail(req.Email);
        GuardPhone(req.Phone);

        var todayVn = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTime.UtcNow, _tzVN).Date);
        if (req.Date < todayVn)
            throw new AppException("Ngày đặt phải từ hôm nay trở đi.");

        if (!string.IsNullOrWhiteSpace(req.Gender) && req.Gender is not ("Male" or "Female"))
            throw new AppException("Giới tính phải là Male hoặc Female.");
        if (req.DateOfBirth is not null && req.DateOfBirth > todayVn)
            throw new AppException("Ngày sinh không hợp lệ.");
        if (req.Symptom?.Length > 500)
            throw new AppException("Triệu chứng tối đa 500 ký tự.");

        // 2) Tìm schedule theo bác sĩ + ngày và kiểm tra giờ
        var schedule = await _repo.FindScheduleAsync(req.DoctorId, req.Date, ct)
                      ?? throw new AppException("Không có ca làm việc cho bác sĩ vào ngày này.");
        if (req.AppointHour < schedule.StartTime || req.AppointHour >= schedule.EndTime)
            throw new AppException("Giờ hẹn nằm ngoài khung giờ làm việc.");

        // 3) Kiểm tra slot còn trống
        if (!await _repo.IsSlotAvailableAsync(schedule.ScheduleId, req.AppointHour, ct))
            throw new AppException("Khung giờ này đã có người đặt.");

        // 4) Xác định/khởi tạo Patient
        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        int patientId;

        if (currentUserId.HasValue)
        {
            // User đã đăng nhập → tạo/ghép Patient theo UserId
            patientId = await _repo.GetOrCreatePatientByUserAsync(
                userId: currentUserId.Value,
                name: req.FullName.Trim(),
                phone: req.Phone.Trim(),
                gender: req.Gender,
                dob: req.DateOfBirth,
                ct: ct);

            // Gộp mọi patient ẩn danh (đặt bằng email trước đó) vào patient chính của user
            await _repo.MergeAnonymousPatientsByEmailIntoUserAsync(
                email: normalizedEmail,
                userId: currentUserId.Value,
                targetPatientId: patientId,
                ct: ct);
        }
        else
        {
            // Khách chưa login → create/find theo email
            patientId = await _repo.GetOrCreatePatientByEmailAsync(
                email: normalizedEmail,
                name: req.FullName.Trim(),
                phone: req.Phone.Trim(),
                gender: req.Gender,
                dob: req.DateOfBirth,
                ct: ct);
        }

        // 5) Tạo appointment
        int appointId;
        try
        {
            appointId = await _repo.CreateAppointmentAsync(
                patientId: patientId,
                scheduleId: schedule.ScheduleId,
                appointHour: req.AppointHour,
                symptom: req.Symptom,
                ct: ct);
        }
        catch (DbUpdateException)
        {
            // chống race condition UNIQUE (ScheduleId, AppointHour)
            throw new AppException("Khung giờ này vừa được đặt bởi người khác. Vui lòng chọn giờ khác.", 409);
        }

        // 6) Sinh mã booking (nếu DB có cột Code/shadow property)
        var bookingCode = BuildAppointmentCode(req.Date);
        await _repo.SetAppointmentCodeIfSupportedAsync(appointId, bookingCode, ct);

        // 7) Gửi email xác nhận (best-effort)
        await SendBookingEmailAsync(
            toEmail: req.Email,
            toDisplayName: req.FullName,
            doctorName: schedule.DoctorName,
            date: req.Date,
            hour: req.AppointHour,
            bookingCode: bookingCode,
            ct: ct);

        // 8) Kết quả
        return new BookingResult
        {
            AppointmentId = appointId,
            AppointmentCode = bookingCode,
            PatientId = patientId,
            ScheduleId = schedule.ScheduleId,
            DoctorName = schedule.DoctorName,
            Date = req.Date,
            AppointHour = req.AppointHour
        };
    }

    // ==============================
    // Wrapper cho tương thích cũ (không dùng userId)
    // ==============================
    public Task<BookingResult> PublicBookAsync(PublicBookingRequest req, CancellationToken ct)
        => BookAsync(req, currentUserId: null, ct);

    // ==============================
    // BUSY SLOTS
    // ==============================
    public async Task<IEnumerable<BusySlot>> GetBusySlotsAsync(int doctorId, DateTime date, CancellationToken ct)
    {
        var dateOnly = DateOnly.FromDateTime(date);
        return await _repo.GetBusySlotsAsync(doctorId, dateOnly, ct);
    }

    // ==============================
    // DELETE (soft)
    // ==============================
    public Task<bool> DeleteAppointmentAsync(int appointmentId, CancellationToken ct)
        => _repo.DeleteAppointmentAsync(appointmentId, ct);

    // ==============================
    // Helpers
    // ==============================
    private async Task SendBookingEmailAsync(
        string toEmail,
        string toDisplayName,
        string doctorName,
        DateOnly date,
        TimeOnly hour,
        string bookingCode,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return;

            var whenVn = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tzVN);
            using var client = new System.Net.Mail.SmtpClient
            {
                Host = _config["Smtp:Host"]!,
                Port = int.Parse(_config["Smtp:Port"]!),
                EnableSsl = bool.Parse(_config["Smtp:EnableSsl"]!),
                Credentials = new System.Net.NetworkCredential(
                    _config["Smtp:User"], _config["Smtp:Password"])
            };
            var mail = new System.Net.Mail.MailMessage
            {
                From = new System.Net.Mail.MailAddress(_config["Smtp:User"]!, "BookMyDoctor System"),
                Subject = $"Xác nhận đặt lịch khám #{bookingCode}",
                IsBodyHtml = true,
                Body =
                    $"<p>Xin chào {System.Net.WebUtility.HtmlEncode(toDisplayName)},</p>" +
                    $"<p>Bạn đã đặt lịch khám thành công tại <b>BookMyDoctor</b>.</p>" +
                    $"<ul>" +
                    $"<li>Bác sĩ: <b>{System.Net.WebUtility.HtmlEncode(doctorName)}</b></li>" +
                    $"<li>Ngày khám: <b><span style='color:red; font-weight:700;'>{date:dd/MM/yyyy}</span></b></li>" +
                    $"<li>Giờ hẹn: <b><span style='color:red; font-weight:700;'>{hour:HH\\:mm}</span></b></li>" +
                    $"<li>Mã đặt lịch: <b>{bookingCode}</b></li>" +
                    $"</ul>" +
                    $"<p style='font-size:10px; color:#555;'>Thời gian đặt: <b>{whenVn:HH:mm:ss dd/MM/yyyy} (GMT+7)</b>.</p>" +
                    $"<p>Vui lòng đến đúng giờ và mang theo giấy tờ cần thiết.</p>"
            };
            mail.To.Add(toEmail);
            await client.SendMailAsync(mail, ct);
        }
        catch
        {
            // nuốt lỗi gửi mail để không chặn flow booking
        }
    }

    private static string BuildAppointmentCode(DateOnly date)
        => $"BK-{date:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}";

    private static void GuardEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !EmailRegex().IsMatch(email))
            throw new AppException("Email không hợp lệ.");
    }
    private static void GuardPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || !PhoneVNRegex().IsMatch(phone))
            throw new AppException("Số điện thoại không hợp lệ.");
    }

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex EmailRegex();
    [GeneratedRegex(@"^0\d{8,10}$")]
    private static partial Regex PhoneVNRegex();
}
