using System.Collections.Concurrent;
using BookMyDoctor_WebAPI.RequestModel.Chat;

namespace BookMyDoctor_WebAPI.Services.Chat
{
    public class BookingBackendHandler
    {
        private readonly IBookingBackend _api;

        // Lưu state cho từng SessionId
        private static readonly ConcurrentDictionary<string, SessionState> _sessions = new();

        public BookingBackendHandler(IBookingBackend api)
        {
            _api = api;
        }

        public async Task<string> HandleAsync(
            LlmStructuredOutput llm,
            string sessionId,
            int? userId,
            CancellationToken ct = default)
        {
            var state = _sessions.GetOrAdd(sessionId, _ => new SessionState());

            // ================== Đồng bộ các field đơn giản từ LLM vào state ==================
            if (!string.IsNullOrWhiteSpace(llm.Symptom))
            {
                state.Symptom = llm.Symptom;
                state.IsWaitingSymptom = false;
            }

            if (!string.IsNullOrWhiteSpace(llm.FullName))
                state.FullName = llm.FullName;

            if (!string.IsNullOrWhiteSpace(llm.Email))
                state.Email = llm.Email;

            if (!string.IsNullOrWhiteSpace(llm.Phone))
                state.Phone = llm.Phone;

            // Nếu LLM không hiểu intent -> trả naturalReply
            if (llm.Intent == Intent.Unknown)
            {
                return string.IsNullOrWhiteSpace(llm.NaturalReply)
                    ? "Xin lỗi, em chưa hiểu rõ yêu cầu của anh/chị, anh/chị nói rõ hơn giúp em với ạ. 🥹"
                    : llm.NaturalReply;
            }

            var extra = llm.Extra ?? new Dictionary<string, object?>();

            // =====================================================================
            // 1) SEARCH DOCTORS
            // =====================================================================
            if (llm.Intent == Intent.SearchDoctors)
            {
                var name = !string.IsNullOrWhiteSpace(llm.DoctorName)
                    ? llm.DoctorName
                    : extra.TryGetValue("doctorName", out var nObj) ? nObj?.ToString() : null;

                var dept = !string.IsNullOrWhiteSpace(llm.Department)
                    ? llm.Department
                    : extra.TryGetValue("department", out var dObj) ? dObj?.ToString() : null;

                var doctors = await _api.SearchDoctorsAsync(
                    name,
                    dept,
                    gender: null,
                    phone: null,
                    workDate: null,
                    ct);

                if (doctors.Count == 0)
                    return "Dạ em không tìm thấy bác sĩ phù hợp với thông tin anh/chị đưa ra ạ.";

                if (doctors.Count == 1)
                {
                    state.LastDoctorId = doctors[0].DoctorId;
                    state.LastDoctorName = doctors[0].Name;
                }

                var lines = doctors.Select(d => $"• {d.Name} – {d.Department}");
                return "Dạ em đã kiểm tra xong rồi ạ, em tìm được các bác sĩ sau:\n" +
                       string.Join("\n", lines);
            }

            // =====================================================================
            // 2) GET BUSY SLOTS
            // =====================================================================
            if (llm.Intent == Intent.GetBusySlots)
            {
                int doctorId;

                // Ưu tiên doctorId nếu có
                if (extra.TryGetValue("doctorId", out var idObj) &&
                    int.TryParse(idObj?.ToString(), out var parsedId))
                {
                    doctorId = parsedId;
                }
                else
                {
                    // Lấy theo tên
                    var doctorName = !string.IsNullOrWhiteSpace(llm.DoctorName)
                        ? llm.DoctorName
                        : extra.TryGetValue("doctorName", out var dnObj) ? dnObj?.ToString() : state.LastDoctorName;

                    if (string.IsNullOrWhiteSpace(doctorName))
                        return "Anh/chị cho em xin tên bác sĩ muốn xem lịch với ạ. (VD: bác sĩ Long khoa Tim mạch)";

                    var found = await _api.SearchDoctorsAsync(
                        doctorName,
                        department: llm.Department,
                        gender: null,
                        phone: null,
                        workDate: null,
                        ct);

                    if (found.Count == 0)
                        return $"Em không tìm thấy bác sĩ nào tên gần giống “{doctorName}” ạ, anh/chị kiểm tra lại tên giúp em nhé.";

                    if (found.Count > 1)
                    {
                        var list = string.Join("\n",
                            found.Select(d => $"• ID {d.DoctorId} – {d.Name} ({d.Department})"));

                        return $"Tên “{doctorName}” có nhiều bác sĩ, anh/chị chọn giúp em 1 bác (dùng ID) nhé:\n{list}";
                    }

                    doctorId = found[0].DoctorId;
                    state.LastDoctorId = doctorId;
                    state.LastDoctorName = found[0].Name;
                }

                // Ngày
                var dateStr = !string.IsNullOrWhiteSpace(llm.Date)
                    ? llm.Date
                    : extra.TryGetValue("date", out var dtObj) ? dtObj?.ToString() : null;

                if (string.IsNullOrWhiteSpace(dateStr))
                    return "Anh/chị muốn xem lịch bận ngày nào ạ? Anh/chị nhắn giúp em dạng yyyy-MM-dd nhé.";

                if (!DateOnly.TryParse(dateStr, out var date))
                    return "Ngày anh/chị gửi chưa đúng định dạng, anh/chị gửi lại giúp em theo dạng yyyy-MM-dd (VD: 2025-11-20) ạ.";

                var slots = await _api.GetBusySlotsAsync(doctorId, date, ct);

                if (slots.Count == 0)
                    return $"Trong ngày {date:yyyy-MM-dd} bác sĩ hiện tại chưa có lịch bận nào ạ.";

                var lines2 = slots.Select(s => $"• {s.AppointHour:HH\\:mm} – {s.Status}");
                return $"Các khung giờ đã có người đặt của bác sĩ trong ngày {date:yyyy-MM-dd}:\n" +
                       string.Join("\n", lines2);
            }

            // =====================================================================
            // 3) CREATE PUBLIC BOOKING
            // =====================================================================
            if (llm.Intent == Intent.CreatePublicBooking)
            {
                // ---------- 3.1: xử lý symptom ----------
                if (string.IsNullOrWhiteSpace(state.Symptom) && llm.NeedSymptom)
                {
                    state.IsWaitingSymptom = true;
                    return "Anh/chị cho em xin triệu chứng hiện tại (VD: ho, sổ mũi, sốt nhẹ...) để em tư vấn bác sĩ/khoa phù hợp hơn ạ.";
                }

                // ---------- 3.2: chọn bác sĩ ----------
                int doctorId;

                // Nếu LLM parse được doctorId
                if (extra.TryGetValue("doctorId", out var docIdObj) &&
                    int.TryParse(docIdObj?.ToString(), out var parsedId2))
                {
                    doctorId = parsedId2;
                }
                else
                {
                    // lấy tên bác sĩ nếu có
                    var doctorName = !string.IsNullOrWhiteSpace(llm.DoctorName)
                        ? llm.DoctorName
                        : extra.TryGetValue("doctorName", out var dnObj) ? dnObj?.ToString() : null;

                    if (string.IsNullOrWhiteSpace(doctorName))
                    {
                        // nếu trước đó đã chọn bác sĩ thì dùng lại
                        if (state.LastDoctorId.HasValue)
                        {
                            doctorId = state.LastDoctorId.Value;
                        }
                        else
                        {
                            return "Dạ anh/chị muốn khám với bác sĩ nào ạ? (VD: bác sĩ Long khoa Nội tổng quát)";
                        }
                    }
                    else
                    {
                        var found = await _api.SearchDoctorsAsync(
                            doctorName,
                            llm.Department,
                            gender: null,
                            phone: null,
                            workDate: null,
                            ct);

                        if (found.Count == 0)
                            return $"Dạ em không tìm thấy bác sĩ nào tên “{doctorName}” ạ, anh/chị cho em xin tên khác hoặc khoa khám giúp em nhé.";

                        if (found.Count > 1)
                            return $"Dạ tên “{doctorName}” có nhiều bác sĩ, anh/chị giúp em chọn thêm khoa hoặc gửi ID của bác sĩ ạ.";

                        doctorId = found[0].DoctorId;
                        state.LastDoctorId = doctorId;
                        state.LastDoctorName = found[0].Name;
                    }
                }

                // ---------- 3.3: lấy ngày khám ----------
                var dateStr = !string.IsNullOrWhiteSpace(llm.Date)
                    ? llm.Date
                    : extra.TryGetValue("date", out var dtObj) ? dtObj?.ToString() : null;

                if (string.IsNullOrWhiteSpace(dateStr))
                    return "Anh/chị muốn đặt lịch ngày nào ạ? Anh/chị nhắn giúp em dạng yyyy-MM-dd (VD: 2025-11-20).";

                if (!DateOnly.TryParse(dateStr, out var date2))
                    return "Ngày anh/chị cung cấp chưa đúng định dạng. Anh/chị gửi lại giúp em dạng yyyy-MM-dd (VD: 2025-11-20) ạ.";

                // ---------- 3.4: lấy giờ khám ----------
                var timeStr = !string.IsNullOrWhiteSpace(llm.Time)
                    ? llm.Time
                    : extra.TryGetValue("time", out var tmObj) ? tmObj?.ToString() : null;

                if (string.IsNullOrWhiteSpace(timeStr))
                    return "Anh/chị muốn khám lúc mấy giờ ạ? Anh/chị nhắn giúp em dạng HH:mm (VD: 09:00).";

                if (!TimeOnly.TryParse(timeStr, out var time))
                    return "Giờ anh/chị cung cấp chưa đúng định dạng. Anh/chị gửi lại giúp em dạng HH:mm (VD: 09:00) ạ.";

                // ---------- 3.5: hỏi thông tin liên hệ (cho khách chưa login) ----------
                var isGuest = userId == null;

                if (isGuest)
                {
                    if (string.IsNullOrWhiteSpace(state.FullName) ||
                        string.IsNullOrWhiteSpace(state.Email) ||
                        string.IsNullOrWhiteSpace(state.Phone))
                    {
                        return "Trước khi đặt lịch, anh/chị cho em xin HỌ TÊN, SỐ ĐIỆN THOẠI và EMAIL để em gửi mã xác nhận lịch khám ạ. ❤️";
                    }
                }

                // ---------- 3.6: tạo payload gửi xuống WebAPI ----------
                var payload = new PublicBookingRequest
                {
                    FullName = isGuest ? (state.FullName ?? "Khách Online") : string.Empty,
                    Phone = isGuest ? (state.Phone ?? "0000000000") : string.Empty,
                    Email = isGuest ? (state.Email ?? "guest@example.com") : string.Empty,
                    Date = date2,
                    DoctorId = doctorId,
                    AppointHour = time,
                    Gender = null,
                    DateOfBirth = null,
                    Symptom = state.Symptom
                };

                var result = await _api.CreatePublicAsync(payload, ct);

                // Clear symptom sau khi book xong
                state.Symptom = null;
                state.IsWaitingSymptom = false;

                var timeText = result.AppointHour.ToString("HH\\:mm");
                var dateText = result.Date.ToString("yyyy-MM-dd");

                return $"Dạ em đã đặt lịch thành công cho anh/chị lúc {timeText} ngày {dateText} với bác sĩ {result.DoctorName} ạ. Mã lịch hẹn của anh/chị là {result.AppointmentCode}. ❤️";
            }

            // =====================================================================
            // 4) CANCEL BOOKING
            // =====================================================================
            if (llm.Intent == Intent.CancelBooking)
            {
                var idText = llm.BookingId != 0
                    ? llm.BookingId.ToString()
                    : extra.TryGetValue("bookingId", out var idObj) ? idObj?.ToString() : null;

                if (string.IsNullOrWhiteSpace(idText) ||
                    !int.TryParse(idText, out var id))
                {
                    return "Anh/chị cho em xin mã lịch hẹn cần hủy (dãy số trong mã đặt lịch) giúp em với ạ.";
                }

                await _api.CancelAsync(id, ct);
                return $"Dạ em đã hủy lịch hẹn mã {id} cho anh/chị rồi ạ.";
            }

            // =====================================================================
            // 5) COUNT DOCTORS
            // =====================================================================
            if (llm.Intent == Intent.CountDoctors)
            {
                var count = await _api.CountDoctorsAsync(ct);

                if (count <= 0)
                    return "Hiện tại trong hệ thống chưa có bác sĩ nào được kích hoạt ạ.";

                if (count == 1)
                    return "Hiện tại phòng khám có 1 bác sĩ đang làm việc trong hệ thống ạ.";

                return $"Hiện tại phòng khám có {count} bác sĩ đang làm việc trong hệ thống ạ.";
            }

            // =====================================================================
            // 6) DOCTOR SCHEDULE RANGE – hôm nay / ngày X bác sĩ nào có lịch
            // =====================================================================
            if (llm.Intent == Intent.DoctorScheduleRange)
            {
                var dateStr = !string.IsNullOrWhiteSpace(llm.Date)
                    ? llm.Date
                    : extra.TryGetValue("date", out var dtObj) ? dtObj?.ToString() : null;

                if (string.IsNullOrWhiteSpace(dateStr) || !DateOnly.TryParse(dateStr, out var date))
                {
                    return "Anh/chị muốn xem lịch vào ngày nào ạ? Anh/chị nhắn giúp em dạng yyyy-MM-dd (VD: 2025-11-20).";
                }

                var doctors = await _api.GetAvailableDoctorsAsync(date, ct);

                if (doctors.Count == 0)
                    return $"Trong ngày {date:dd/MM/yyyy} hiện tại em chưa thấy bác sĩ nào còn lịch trống ạ.";

                var lines = doctors.Select(d => $"• {d.Name} – {d.Department}");
                return $"Dạ em đã kiểm tra xong rồi ạ. Trong ngày {date:dd/MM/yyyy}, các bác sĩ đang có lịch làm việc là:\n" +
                       string.Join("\n", lines);
            }

            // =====================================================================
            // Fallback: ưu tiên naturalReply
            // =====================================================================
            if (!string.IsNullOrWhiteSpace(llm.NaturalReply))
                return llm.NaturalReply;

            return "Hiện tại em chưa hỗ trợ yêu cầu này, anh/chị có thể nói rõ hơn giúp em không ạ?";
        }
    }

    public sealed class SessionState
    {
        // Triệu chứng
        public bool IsWaitingSymptom { get; set; }
        public string? Symptom { get; set; }

        // Thông tin liên hệ (cho guest)
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }

        // Ghi nhớ bác sĩ gần nhất
        public int? LastDoctorId { get; set; }
        public string? LastDoctorName { get; set; }
    }
}
