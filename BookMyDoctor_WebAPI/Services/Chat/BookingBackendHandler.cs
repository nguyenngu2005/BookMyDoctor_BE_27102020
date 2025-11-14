using System.Collections.Concurrent;
using BookMyDoctor_WebAPI.RequestModel.Chat;

namespace BookMyDoctor_WebAPI.Services.Chat
{
    public class BookingBackendHandler
    {
        private readonly IBookingBackend _api;

        // Dùng ConcurrentDictionary để tránh lỗi race-condition
        private static readonly ConcurrentDictionary<string, SessionState> _sessions = new();

        public BookingBackendHandler(IBookingBackend api)
        {
            _api = api;
        }

        /// <summary>
        /// Xử lý intent từ LLM và gọi backend tương ứng.
        /// </summary>
        public async Task<string> HandleAsync(
    LlmStructuredOutput llm,
    string sessionId,
    int? userId,
    CancellationToken ct = default)
        {
            var state = _sessions.GetOrAdd(sessionId, _ => new SessionState());

            // Nếu LLM không hiểu intent → trả lời tự nhiên
            if (llm.Intent == Intent.Unknown)
            {
                return string.IsNullOrWhiteSpace(llm.NaturalReply)
                    ? "Xin lỗi, tôi chưa hiểu yêu cầu của bạn."
                    : llm.NaturalReply;
            }

            var extra = llm.Extra ?? new Dictionary<string, object?>();

            // ============= SearchDoctors =============
            if (llm.Intent == Intent.SearchDoctors)
            {
                var name = extra.TryGetValue("doctorName", out var nameObj)
                    ? nameObj?.ToString()
                    : null;

                var dept = extra.TryGetValue("department", out var deptObj)
                    ? deptObj?.ToString()
                    : null;

                var doctors = await _api.SearchDoctorsAsync(
                    name,
                    dept,
                    gender: null,
                    phone: null,
                    workDate: null,
                    ct);

                if (doctors.Count == 0)
                    return "Không tìm thấy bác sĩ phù hợp.";

                return string.Join("\n", doctors.Select(d => $"• {d.Name} – {d.Department}"));
            }

            // ============= GetBusySlots =============
            if (llm.Intent == Intent.GetBusySlots)
            {
                if (!extra.TryGetValue("doctorId", out var docObj) ||
                    !int.TryParse(docObj?.ToString(), out var doctorId))
                {
                    return "Bạn cần cho tôi biết ID bác sĩ (số nguyên).";
                }

                if (!extra.TryGetValue("date", out var dateObj) ||
                    string.IsNullOrWhiteSpace(dateObj?.ToString()))
                {
                    return "Bạn muốn kiểm tra lịch ngày nào vậy?";
                }

                if (!DateOnly.TryParse(dateObj!.ToString(), out var date))
                {
                    return "Ngày bạn cung cấp không hợp lệ. Vui lòng nhập yyyy-MM-dd.";
                }

                var slots = await _api.GetBusySlotsAsync(doctorId, date, ct);

                if (slots.Count == 0)
                    return "Không có lịch bận trong ngày này.";

                return string.Join("\n", slots.Select(s => $"• {s.AppointHour} – {s.Status}"));
            }

            // ============= CreatePublicBooking =============
            if (llm.Intent == Intent.CreatePublicBooking)
            {
                // 1) Hỏi thêm triệu chứng nếu chưa có
                if (!extra.ContainsKey("symptom") && state.Symptom == null)
                {
                    state.IsWaitingSymptom = true;
                    return "Bạn gặp triệu chứng gì để tôi chọn bác sĩ phù hợp?";
                }

                // 2) Nếu đang chờ symptom và lần này LLM đã gửi symptom
                if (state.IsWaitingSymptom && extra.TryGetValue("symptom", out var symObj))
                {
                    state.Symptom = symObj?.ToString();
                    state.IsWaitingSymptom = false;
                }

                // ===== LẤY DOCTOR ID =====
                int doctorId;

                if (extra.TryGetValue("doctorId", out var docIdObj) &&
                    int.TryParse(docIdObj?.ToString(), out var parsedId))
                {
                    doctorId = parsedId;
                }
                else
                {
                    if (!extra.TryGetValue("doctorName", out var docNameObj))
                    {
                        return "Dạ anh/chị muốn khám với bác sĩ nào ạ? (Tên bác sĩ)";
                    }

                    var name = docNameObj?.ToString() ?? string.Empty;

                    var found = await _api.SearchDoctorsAsync(name, null, null, null, null, ct);

                    if (found.Count == 0)
                        return $"Dạ em không tìm thấy bác sĩ nào tên '{name}' ạ.";

                    if (found.Count > 1)
                        return $"Dạ tên '{name}' có nhiều bác sĩ, anh/chị cho em thêm khoa hoặc giới tính của bác giúp em với ạ.";

                    doctorId = found[0].DoctorId;
                }

                // ===== LẤY NGÀY KHÁM =====
                if (!extra.TryGetValue("date", out var dateObj) ||
                    string.IsNullOrWhiteSpace(dateObj?.ToString()))
                {
                    return "Bạn muốn khám ngày nào?";
                }

                if (!DateOnly.TryParse(dateObj!.ToString(), out var date2))
                {
                    return "Ngày bạn cung cấp không hợp lệ. Vui lòng nhập yyyy-MM-dd.";
                }

                // ===== LẤY GIỜ KHÁM =====
                if (!extra.TryGetValue("time", out var timeObj) ||
                    string.IsNullOrWhiteSpace(timeObj?.ToString()))
                {
                    return "Bạn muốn khám lúc mấy giờ?";
                }

                if (!TimeOnly.TryParse(timeObj!.ToString(), out var time))
                {
                    return "Giờ bạn cung cấp không hợp lệ. Vui lòng nhập HH:mm.";
                }

                var payload = new PublicBookingRequestDto(
                    FullName: "Khách Online",
                    Phone: "0000000000",
                    Email: "guest@example.com",
                    Date: date2,
                    DoctorId: doctorId,
                    AppointHour: time,
                    Gender: null,
                    DateOfBirth: null,
                    Symptom: state.Symptom
                );

                var result = await _api.CreatePublicAsync(payload, ct);

                state.Symptom = null;
                state.IsWaitingSymptom = false;

                return $"Đặt lịch thành công lúc {time} ngày {date2:yyyy-MM-dd} với {result.DoctorName}. Mã lịch: {result.AppointmentCode}.";
            }

            // ============= CancelBooking =============
            if (llm.Intent == Intent.CancelBooking)
            {
                if (!extra.TryGetValue("bookingId", out var idObj) ||
                    !int.TryParse(idObj?.ToString(), out var id))
                {
                    return "Bạn cần cung cấp mã lịch hẹn hợp lệ để hủy.";
                }

                await _api.CancelAsync(id, ct);
                return $"Đã hủy lịch hẹn mã {id}.";
            }

            // ===== Fallback: nếu có naturalReply thì dùng, không thì trả câu mặc định =====
            if (!string.IsNullOrWhiteSpace(llm.NaturalReply))
                return llm.NaturalReply;

            return "Hiện tôi chưa hỗ trợ yêu cầu này, bạn có thể diễn đạt lại giúp tôi không?";
        }
    }

    public class SessionState
    {
        public bool IsWaitingSymptom { get; set; }
        public string? Symptom { get; set; }
    }
}
