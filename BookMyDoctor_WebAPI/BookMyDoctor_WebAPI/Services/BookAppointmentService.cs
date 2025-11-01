// Magic. Don't touch
using System.Security.Claims;
using System.Text.RegularExpressions;
//using BookMyDoctor_WebAPI.Data;
using BookMyDoctor_WebAPI.Models;
//using BookMyDoctor_WebAPI.RequestModel;
//using Microsoft.Extensions.Configuration;

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
    Task<BookingResult> PublicBookAsync(PublicBookingRequest req, CancellationToken ct);
    Task<BookingResult> PrivateBookAsync(ClaimsPrincipal user, PrivateBookingRequest req, CancellationToken ct);
    Task<IEnumerable<BusySlot>> GetBusySlotsAsync(int doctorId, DateTime date, CancellationToken ct);
    Task<bool> DeleteAppointmentAsync(int appointmentId, CancellationToken ct);
}
public sealed partial class BookingService : IBookingService
{
    private readonly IBookingRepository _repo;
    private readonly IConfiguration _config;

    private readonly TimeZoneInfo _tzVN =
        TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); // GMT+7

    public BookingService(IBookingRepository repo, IConfiguration config)
    { _repo = repo; _config = config; }

    // ============ PUBLIC BOOKING (không login) ============
    public async Task<BookingResult> PublicBookAsync(PublicBookingRequest req, CancellationToken ct)
    {
        // 1. Validate
        GuardEmail(req.Email);
        GuardPhone(req.Phone);
        if (req.Date < DateOnly.FromDateTime(DateTime.Now.Date))
            throw new AppException("Ngày đặt phải từ hôm nay trở đi.");
        //var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
        //            TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")));
        //if (req.Date < today)
        //    throw new AppException("Ngày đặt phải từ hôm nay trở đi.");

        if (!string.IsNullOrWhiteSpace(req.Gender) && req.Gender is not ("Male" or "Female"))
            throw new AppException("Giới tính phải là Male hoặc Female.");
        if (req.DateOfBirth is not null && req.DateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow.Date))
            throw new AppException("Ngày sinh không hợp lệ.");
        if (req.Symptom?.Length > 500) throw new AppException("Triệu chứng tối đa 500 ký tự.");

        // 2. Tìm ca làm việc theo bác sĩ + ngày
        var schedule = await _repo.FindScheduleAsync(req.DoctorId, req.Date, ct)
                      ?? throw new AppException("Không có ca làm việc cho bác sĩ vào ngày này.");
        if (!(req.AppointHour >= schedule.StartTime && req.AppointHour < schedule.EndTime))
            throw new AppException("Giờ hẹn nằm ngoài khung giờ làm việc.");

        // 3. Check slot
        if (!await _repo.IsSlotAvailableAsync(schedule.ScheduleId, req.AppointHour, ct))
            throw new AppException("Khung giờ này đã có người đặt.");

        // 4. Attach/Merge patient theo email -> ưu tiên user nếu đã đăng ký
        var normalizedEmail = req.Email.Trim().ToLowerInvariant();
        var userIdMaybe = await _repo.FindUserIdByEmailAsync(normalizedEmail, ct);

        int patientId;

        if (userIdMaybe is int userId) // đã có tài khoản
        {
            // (A) Lấy/tao patient gắn với UserId
            patientId = await _repo.GetOrCreatePatientByUserAsync(
                userId: userId,
                name: req.FullName.Trim(),
                phone: req.Phone.Trim(),
                gender: req.Gender,
                dob: req.DateOfBirth,
                ct: ct);

            // (B) Merge mọi patient vô danh (UserId NULL) có cùng email về patient ở (A)
            await _repo.MergeAnonymousPatientsByEmailIntoUserAsync(
                email: normalizedEmail,
                userId: userId,
                targetPatientId: patientId,
                ct: ct);
        }
        else
        {
            // (C) Chưa có tài khoản -> giữ logic cũ
            patientId = await _repo.GetOrCreatePatientByEmailAsync(
                email: normalizedEmail,
                name: req.FullName.Trim(),
                phone: req.Phone.Trim(),
                gender: req.Gender,
                dob: req.DateOfBirth,
                ct: ct);
        }


        // 5. Tạo Appointment
        var appointId = await _repo.CreateAppointmentAsync(
            patientId: patientId,
            scheduleId: schedule.ScheduleId,
            appointHour: req.AppointHour,
            symptom: req.Symptom,
            ct: ct);

        // 6. Sinh mã booking (optional lưu DB nếu bạn có cột Code)
        var bookingCode = BuildAppointmentCode(req.Date);
        await _repo.SetAppointmentCodeIfSupportedAsync(appointId, bookingCode, ct);

        // 7. Gửi email xác nhận (template SMTP của bạn)
        await SendBookingEmailAsync(
            toEmail: req.Email,
            toDisplayName: req.FullName,
            doctorName: schedule.DoctorName,
            department: req.Department ?? "",
            date: req.Date,
            hour: req.AppointHour,
            bookingCode: bookingCode,
            ct: ct);

        // 8. Map kết quả trả về
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

    // ============ PRIVATE BOOKING (đã login, role R03) ============
    public async Task<BookingResult> PrivateBookAsync(ClaimsPrincipal user, PrivateBookingRequest req, CancellationToken ct)
    {
        // 1. Validate role
        var role = user.FindFirstValue(ClaimTypes.Role);
        if (role != "R03") throw new ForbiddenException("Chỉ tài khoản R03 (bệnh nhân) được đặt lịch.");

        // 2. Validate input
        if (req.Date < DateOnly.FromDateTime(DateTime.UtcNow.Date))
            throw new AppException("Ngày đặt phải từ hôm nay trở đi.");
        if (req.Symptom?.Length > 500) throw new AppException("Triệu chứng tối đa 500 ký tự.");

        // 3. Lấy schedule theo ScheduleId và kiểm tra khớp bác sĩ + ngày
        var schedule = await _repo.GetScheduleByIdAsync(req.ScheduleId, ct)
                      ?? throw new AppException("Không tìm thấy Schedule.");
        if (schedule.DoctorId != req.DoctorId)
            throw new AppException("Schedule không thuộc về bác sĩ đã chọn.");
        if (schedule.WorkDate != req.Date)
            throw new AppException("Schedule không khớp ngày làm việc.");
        if (!(req.AppointHour >= schedule.StartTime && req.AppointHour < schedule.EndTime))
            throw new AppException("Giờ hẹn nằm ngoài khung giờ làm việc.");

        // 4. Slot rảnh?
        if (!await _repo.IsSlotAvailableAsync(schedule.ScheduleId, req.AppointHour, ct))
            throw new AppException("Khung giờ này đã có người đặt.");

        // 5. đảm bảo PatientId thuộc về user hiện tại
        var userId = int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var patientOk = await _repo.VerifyPatientOwnedByUserAsync(req.PatientId, userId, ct);
        if (!patientOk)
            throw new ForbiddenException("PatientId không thuộc tài khoản hiện tại.");

        // 6. Tạo Appointment
        var appointId = await _repo.CreateAppointmentAsync(
            patientId: req.PatientId,
            scheduleId: schedule.ScheduleId,
            appointHour: req.AppointHour,
            symptom: req.Symptom,
            ct: ct);

        // 7. Sinh mã booking + (optional) lưu DB
        var bookingCode = BuildAppointmentCode(req.Date);
        await _repo.SetAppointmentCodeIfSupportedAsync(appointId, bookingCode, ct);

        // 8. Lấy email + tên hiển thị để gửi mail
        var patient = await _repo.GetPatientByIdAsync(req.PatientId, ct);
        var toEmail = !string.IsNullOrWhiteSpace(patient.Email)
                        ? patient.Email
                        : (user.FindFirstValue(ClaimTypes.Email) ?? "");
        var toName = !string.IsNullOrWhiteSpace(patient.Name)
                        ? patient.Name
                        : (user.Identity?.Name ?? "Quý khách");

        if (!string.IsNullOrWhiteSpace(toEmail))
        {
            await SendBookingEmailAsync(
                toEmail: toEmail,
                toDisplayName: toName,
                doctorName: schedule.DoctorName,
department: req.Department ?? "",
                date: req.Date,
                hour: req.AppointHour,
                bookingCode: bookingCode,
                ct: ct);
        }

        // 9. Trả kết quả
        return new BookingResult
        {
            AppointmentId = appointId,
            AppointmentCode = bookingCode,
            PatientId = req.PatientId,
            ScheduleId = schedule.ScheduleId,
            DoctorName = schedule.DoctorName,
            Date = req.Date,
            AppointHour = req.AppointHour
        };
    }
    //============= Slot busy ==========
    //public async Task<IEnumerable<Appointment>> GetBusySlotsAsync(int doctorId, DateTime date, CancellationToken ct)
    //{
    //    var dateOnly = DateOnly.FromDateTime(date);

    //    var appointments = await _repo.GetBusySlotsAsync(doctorId, dateOnly, ct);

    //    return appointments.Select(a => new Appointment
    //    {
    //        AppointHour = a.AppointHour,
    //        Status = a.Status
    //    });
    //}
    public async Task<IEnumerable<BusySlot>> GetBusySlotsAsync(int doctorId, DateTime date, CancellationToken ct)
    {
        var dateOnly = DateOnly.FromDateTime(date);
        return await _repo.GetBusySlotsAsync(doctorId, dateOnly, ct);
    }


    //============ Delete Appointment ============
    public async Task<bool> DeleteAppointmentAsync(int appointmentId, CancellationToken ct)
    {
        var result = await _repo.DeleteAppointmentAsync(appointmentId, ct);
        return result;
    }

    // ============ Helpers ============

    private async Task SendBookingEmailAsync(
        string toEmail,
        string toDisplayName,
        string doctorName,
        string department,
        DateOnly date,
        TimeOnly hour,
        string bookingCode,
        CancellationToken ct)
    {
        // Email xác nhận (theo mẫu bạn gửi)
        try
        {
            if (!string.IsNullOrWhiteSpace(toEmail))
            {
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
                    From = new System.Net.Mail.MailAddress(
                        _config["Smtp:User"]!, "BookMyDoctor System"),
                    Subject = $"Xác nhận đặt lịch khám #{bookingCode}",
                    IsBodyHtml = true,
                    Body =
                        $"<p>Xin chào {System.Net.WebUtility.HtmlEncode(toDisplayName)},</p>" +
                        $"<p>Bạn đã đặt lịch khám thành công tại <b>BookMyDoctor</b>.</p>" +
                        $"<ul>" +
                        $"<li>Bác sĩ: <b>{System.Net.WebUtility.HtmlEncode(doctorName)}</b></li>" +
                        $"<li>Khoa khám bệnh: <b>{System.Net.WebUtility.HtmlEncode(department)}</b></li>" +
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
        }
        catch { /* log nếu cần */ }
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