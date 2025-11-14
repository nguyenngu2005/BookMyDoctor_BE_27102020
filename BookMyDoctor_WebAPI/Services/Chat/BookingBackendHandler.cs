using BookMyDoctor_WebAPI.RequestModel.Chat;
using BookMyDoctor_WebAPI.Services.Chat;

namespace BookMyDoctor_WebAPI.Services.Chat
{
    public class BookingBackendHandler
    {
        private readonly BookingBackend _api; // backend thật để gọi REST API

        private static readonly Dictionary<string, SessionState> _sessions = new();

        public BookingBackendHandler(BookingBackend api)
        {
            _api = api;
        }

        public async Task<string> HandleAsync(LlmStructuredOutput llm, string sessionId, int? userId)
        {
            if (!_sessions.TryGetValue(sessionId, out var state))
            {
                state = new SessionState();
                _sessions[sessionId] = state;
            }

            if (llm.Intent == Intent.Unknown)
                return llm.NaturalReply;

            // ===== SearchDoctors =====
            if (llm.Intent == Intent.SearchDoctors)
            {
                var name = llm.Extra?.GetValueOrDefault("doctorName")?.ToString();
                var dept = llm.Extra?.GetValueOrDefault("department")?.ToString();

                var doctors = await _api.SearchDoctorsAsync(
                    name, dept, null, null, null, CancellationToken.None);

                if (doctors.Count == 0)
                    return "Không tìm thấy bác sĩ phù hợp.";

                return string.Join("\n", doctors.Select(d => $"• {d.Name} – {d.Department}"));
            }

            // ===== GetBusySlots =====
            if (llm.Intent == Intent.GetBusySlots)
            {
                if (!llm.Extra!.TryGetValue("doctorId", out var docObj))
                    return "Bạn cần cho tôi biết ID bác sĩ.";

                if (!llm.Extra.TryGetValue("date", out var dateObj) ||
                    string.IsNullOrWhiteSpace(dateObj?.ToString()))
                    return "Bạn muốn kiểm tra lịch ngày nào vậy?";

                if (!DateOnly.TryParse(dateObj!.ToString(), out var date))
                    return "Ngày bạn cung cấp không hợp lệ.";

                var doctorId = Convert.ToInt32(docObj);

                var slots = await _api.GetBusySlotsAsync(doctorId, date, CancellationToken.None);

                if (slots.Count == 0)
                    return "Không có lịch bận trong ngày này.";

                return string.Join("\n", slots.Select(s => $"• {s.AppointHour} – {s.Status}"));
            }
            // ===== CreatePublicBooking =====
            if (llm.Intent == Intent.CreatePublicBooking)
            {
                if (!llm.Extra!.ContainsKey("symptom") && state.Symptom == null)
                {
                    state.IsWaitingSymptom = true;
                    return "Bạn gặp triệu chứng gì để tôi chọn bác sĩ phù hợp?";
                }

                if (state.IsWaitingSymptom && llm.Extra.ContainsKey("symptom"))
                {
                    state.Symptom = llm.Extra["symptom"]?.ToString();
                    state.IsWaitingSymptom = false;
                }

                if (!llm.Extra.TryGetValue("doctorId", out var docIdObj))
                    return "Bạn muốn đặt với bác sĩ nào?";

                if (!llm.Extra.TryGetValue("date", out var dateObj) ||
                    string.IsNullOrWhiteSpace(dateObj?.ToString()))
                    return "Bạn muốn khám ngày nào?";

                if (!llm.Extra.TryGetValue("time", out var timeObj) ||
                    string.IsNullOrWhiteSpace(timeObj?.ToString()))
                    return "Bạn muốn khám lúc mấy giờ?";

                if (!DateOnly.TryParse(dateObj!.ToString(), out var date))
                    return "Ngày bạn cung cấp không hợp lệ.";

                if (!TimeOnly.TryParse(timeObj!.ToString(), out var time))
                    return "Giờ bạn cung cấp không hợp lệ.";

                var doctorId = Convert.ToInt32(docIdObj);

                var payload = new PublicBookingRequestDto(
                    FullName: "Khách Online",
                    Phone: "0000000000",
                    Email: "guest@example.com",
                    Date: date,
                    DoctorId: doctorId,
                    AppointHour: time,
                    Gender: null,
                    DateOfBirth: null,
                    Symptom: state.Symptom
                );

                var result = await _api.CreatePublicAsync(payload, CancellationToken.None);

                return $"Đặt lịch thành công lúc {time} ngày {date} với {result.DoctorName}. Mã lịch: {result.AppointmentCode}.";
            }

            // ===== CancelBooking =====
            if (llm.Intent == Intent.CancelBooking)
            {
                var id = Convert.ToInt32(llm.Extra!["bookingId"]);
                await _api.CancelAsync(id, CancellationToken.None);
                return $"Đã hủy lịch hẹn mã {id}.";
            }

            return llm.NaturalReply;
        }
    }

    public class SessionState
    {
        public bool IsWaitingSymptom { get; set; }
        public string? Symptom { get; set; }
    }
}
