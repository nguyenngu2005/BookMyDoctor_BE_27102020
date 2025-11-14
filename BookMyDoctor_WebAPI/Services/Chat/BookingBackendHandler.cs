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
            // Lấy hoặc tạo mới state cho session hiện tại
            var state = _sessions.GetOrAdd(sessionId, _ => new SessionState());

            // Nếu LLM không hiểu intent → trả về naturalReply như chatbot thuần
            if (llm.Intent == Intent.Unknown)
            {
                return string.IsNullOrWhiteSpace(llm.NaturalReply)
                    ? "Xin lỗi, tôi chưa hiểu yêu cầu của bạn."
                    : llm.NaturalReply;
            }

            // Đảm bảo Extra không bị null
            var extra = llm.Extra ?? new Dictionary<string, object?>();

            // ================== SearchDoctors ==================
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

                return string.Join("\n",
                    doctors.Select(d => $"• {d.Name} – {d.Department}"));
            }

            // ================== GetBusySlots ==================
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
                    return "Ngày bạn cung cấp không hợp lệ. Vui lòng nhập theo định dạng yyyy-MM-dd.";
                }

                var slots = await _api.GetBusySlotsAsync(doctorId, date, ct);

                if (slots.Count == 0)
                    return "Không có lịch bận trong ngày này.";

                return string.Join("\n",
                    slots.Select(s => $"• {s.AppointHour} – {s.Status}"));
            }

            // ================== CreatePublicBooking ==================
            // 1) Nếu LLM đã trả doctorId → dùng luôn
            int doctorId = -1;

            if (extra.TryGetValue("doctorId", out var docIdObj) &&
                int.TryParse(docIdObj?.ToString(), out var parsedId))
            {
                doctorId = parsedId;
            }
            else
            {
                // 2) Nếu không có doctorId → Lấy doctorName
                if (!extra.TryGetValue("doctorName", out var docNameObj))
                {
                    return "Dạ anh/chị muốn khám với bác sĩ nào ạ? (Tên bác sĩ)";
                }

                var name = docNameObj?.ToString() ?? "";

                // 3) Tự động tìm bác sĩ theo tên
                var found = await _api.SearchDoctorsAsync(name, null, null, null, null, ct);

                if (found.Count == 0)
                    return $"Dạ em không tìm thấy bác sĩ nào tên '{name}' ạ.";

                if (found.Count > 1)
                    return $"Dạ tên '{name}' có nhiều bác sĩ, anh/chị mô tả thêm khoa hoặc giới tính giúp em nhé.";

                doctorId = found[0].DoctorId;
            }


            // Fallback: nếu có naturalReply thì dùng luôn
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
