using BookMyDoctor_WebAPI.RequestModel.Chat;
using System.Text;
using System.Text.RegularExpressions;

namespace BookMyDoctor_WebAPI.Services.Chat;

public interface IChatService
{
    Task<string> HandleAsync(ChatRequest req, CancellationToken ct = default);
}

public class ChatService : IChatService
{
    private readonly IGeminiClient _nlu;
    private readonly IBookingBackend _backend;

    public ChatService(IGeminiClient nlu, IBookingBackend backend)
    {
        _nlu = nlu;
        _backend = backend;
    }

    public async Task<string> HandleAsync(ChatRequest req, CancellationToken ct = default)
    {
        // 1) Hiểu ý định (NLU)
        var (intent, entities, raw) = await _nlu.InferAsync(req.Messages, ct);

        // <<< added: Heuristic fallback nếu NLU nhận nhầm
        var lastUtter = req.Messages?.LastOrDefault()?.Content ?? string.Empty;
        if (intent == Intent.Unknown || intent == Intent.GreetingHelp)
        {
            var h = HeuristicDetect(lastUtter);
            if (h is not null)
            {
                intent = h.Value.intent;
                foreach (var kv in h.Value.entities) entities[kv.Key] = kv.Value;
            }
        }

        // 2) Điều phối theo intent
        switch (intent)
        {
            case Intent.GreetingHelp:
                return "Mình có thể giúp bạn tìm bác sĩ, xem giờ trống, đặt hoặc hủy lịch. Bạn cần gì ạ?";

            case Intent.SearchDoctors:
                {
                    entities.TryGetValue("doctorName", out var name);
                    entities.TryGetValue("department", out var dep);
                    var list = await _backend.SearchDoctorsAsync(name, dep, null, null, null, ct);
                    if (list.Count == 0) return "Mình chưa tìm thấy bác sĩ phù hợp. Bạn thử nhập tên hoặc chuyên khoa khác nhé.";
                    var sb = new StringBuilder("Mình tìm được vài bác sĩ phù hợp:\n");
                    foreach (var d in list.Take(5))
                        sb.AppendLine($"• {d.Name} – {d.Department} (ID {d.DoctorId})");
                    sb.Append("Bạn muốn xem giờ trống của bác sĩ nào và ngày nào (YYYY-MM-DD)?");
                    return sb.ToString();
                }

            case Intent.GetBusySlots:
                {
                    // cần doctorId + date (hoặc bạn có thể mở rộng hỗ trợ doctorName)
                    if (!entities.TryGetValue("doctorId", out var idStr) || !int.TryParse(idStr, out var doctorId))
                        return "Bạn cho mình ID hoặc tên bác sĩ và ngày (YYYY-MM-DD) để mình kiểm tra giờ trống nhé.";
                    if (!entities.TryGetValue("date", out var dateStr) || !DateOnly.TryParse(dateStr, out var date))
                        return "Bạn cho mình ngày (YYYY-MM-DD) để mình kiểm tra giờ trống nhé.";

                    var busy = await _backend.GetBusySlotsAsync(doctorId, date, ct);
                    var busySet = busy.Select(b => b.AppointHour).ToHashSet();
                    var hours = new[] { "08:00", "09:00", "10:00", "13:00", "14:00", "15:00", "16:00", "17:00" };
                    var free = hours.Where(h => !busySet.Contains(h)).ToList();
                    if (free.Count == 0) return $"Ngày {date:yyyy-MM-dd} bác sĩ (ID {doctorId}) đã kín lịch. Bạn thử ngày khác nhé?";
                    return $"Ngày {date:yyyy-MM-dd} bác sĩ (ID {doctorId}) còn trống: {string.Join(", ", free)}. Bạn chọn khung giờ nào ạ?";
                }

            case Intent.CreatePublicBooking:
                {
                    if (!TryGetBookingPayload(entities, out var payload, out var err))
                        return err ?? "Thiếu thông tin đặt lịch. Bạn cho mình Họ tên, SĐT, Email, bác sĩ (ID), ngày (YYYY-MM-DD) và giờ (HH:mm) nhé.";
                    var result = await _backend.CreatePublicAsync(payload!, ct);
                    return $"Đặt lịch thành công! Mã: {result.AppointmentId}. Bác sĩ: {result.DoctorName}. Ngày {result.Date:yyyy-MM-dd} lúc {result.AppointHour}.";
                }

            case Intent.CancelBooking:
                {
                    if (!entities.TryGetValue("bookingId", out var id) || !int.TryParse(id, out var bookingId))
                        return "Bạn vui lòng cung cấp mã lịch (bookingId) để hủy nhé.";
                    var ok = await _backend.CancelAsync(bookingId, ct);
                    return ok ? $"Đã hủy lịch mã {bookingId}. Bạn cần đặt lịch mới không?"
                              : $"Không hủy được lịch mã {bookingId}. Bạn kiểm tra lại giúp mình nhé.";
                }

            case Intent.Faq:
                return "Giờ làm việc: 08:00–17:30 (T2–T7). Đặt/hủy miễn phí trước giờ khám 24h. Trường hợp khẩn cấp vui lòng gọi 115.";

            default:
                return "Mình có thể giúp bạn tìm bác sĩ, xem giờ trống, đặt hoặc hủy lịch. Bạn muốn bắt đầu với tính năng nào?";
        }
    }

    private static bool TryGetBookingPayload(Dictionary<string, string> e, out PublicBookingRequestDto? payload, out string? error)
    {
        payload = null; error = null;
        if (!e.TryGetValue("fullName", out var name) || string.IsNullOrWhiteSpace(name))
        { error = "Bạn cho mình Họ tên để tạo lịch nhé."; return false; }
        if (!e.TryGetValue("phone", out var phone) || string.IsNullOrWhiteSpace(phone))
        { error = "Bạn vui lòng cung cấp SĐT nhé."; return false; }
        if (!e.TryGetValue("email", out var email) || string.IsNullOrWhiteSpace(email))
        { error = "Bạn vui lòng cung cấp Email nhé."; return false; }
        if (!e.TryGetValue("doctorId", out var didStr) || !int.TryParse(didStr, out var doctorId))
        { error = "Bạn cho mình ID bác sĩ nhé (ví dụ: 12)."; return false; }
        if (!e.TryGetValue("date", out var dateStr) || !DateOnly.TryParse(dateStr, out var date))
        { error = "Ngày chưa đúng định dạng YYYY-MM-DD."; return false; }
        if (!e.TryGetValue("hour", out var hourStr) || !TimeOnly.TryParse(hourStr, out var hour))
        { error = "Giờ chưa đúng định dạng HH:mm."; return false; }

        e.TryGetValue("symptom", out var symptom);
        e.TryGetValue("gender", out var gender);
        DateOnly? dob = null;
        if (e.TryGetValue("dateOfBirth", out var dobStr) && DateOnly.TryParse(dobStr, out var dobVal))
            dob = dobVal;

        payload = new PublicBookingRequestDto(name, phone, email, date, doctorId, hour, gender, dob, symptom);
        return true;
    }

    // <<< added: Heuristic đơn giản nhận intent từ text nếu NLU nhầm
    private static (Intent intent, Dictionary<string, string> entities)? HeuristicDetect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant();

        // đặt lịch
        if (t.Contains("đặt lịch") || t.Contains("book") || t.Contains("hẹn khám"))
        {
            var ent = new Dictionary<string, string>();
            var mId = Regex.Match(t, @"\b(id|bác sĩ id)\s*(\d+)");
            if (mId.Success) ent["doctorId"] = mId.Groups[2].Value;

            var mDate = Regex.Match(t, @"\b(20\d{2}-\d{2}-\d{2})\b");
            if (mDate.Success) ent["date"] = mDate.Value;

            var mTime = Regex.Match(t, @"\b([01]\d|2[0-3]):[0-5]\d\b");
            if (mTime.Success) ent["hour"] = mTime.Value;

            return (Intent.CreatePublicBooking, ent);
        }

        // hủy lịch
        if (t.Contains("hủy lịch") || t.Contains("cancel") || t.Contains("xóa lịch"))
        {
            var ent = new Dictionary<string, string>();
            var m = Regex.Match(t, @"mã\s*(\d+)");
            if (m.Success) ent["bookingId"] = m.Groups[1].Value;
            return (Intent.CancelBooking, ent);
        }

        // giờ trống
        if (t.Contains("giờ trống") || t.Contains("còn giờ nào"))
        {
            var ent = new Dictionary<string, string>();
            var mId = Regex.Match(t, @"\bid\s*(\d+)");
            if (mId.Success) ent["doctorId"] = mId.Groups[1].Value;
            var mDate = Regex.Match(t, @"\b(20\d{2}-\d{2}-\d{2})\b");
            if (mDate.Success) ent["date"] = mDate.Value;
            return (Intent.GetBusySlots, ent);
        }

        // tìm bác sĩ theo khoa
        if (t.Contains("bác sĩ khoa") || t.Contains("khoa "))
        {
            // lấy phần sau từ "khoa "
            var depIdx = t.IndexOf("khoa ");
            if (depIdx >= 0)
            {
                var dep = text.Substring(depIdx + 5).Trim().TrimEnd('.', '!', '?');
                if (!string.IsNullOrWhiteSpace(dep))
                    return (Intent.SearchDoctors, new Dictionary<string, string> { ["department"] = dep });
            }
            return (Intent.SearchDoctors, new Dictionary<string, string>());
        }

        return null;
    }
}