using System.Collections.Concurrent;
using BookMyDoctor_WebAPI.RequestModel.Chat;
// Nếu PublicBookingRequest, BookingResult, BusySlot ở namespace khác thì thêm using tương ứng:
// using BookMyDoctor_WebAPI.Controllers; 
// hoặc using BookMyDoctor_WebAPI.RequestModel;  // tuỳ bạn đang đặt

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

                // Lưu lại bác sĩ cuối cùng user vừa hỏi, để dùng cho bước sau (GetBusySlots, booking...)
                if (doctors.Count == 1)
                {
                    state.LastDoctorId = doctors[0].DoctorId;
                    state.LastDoctorName = doctors[0].Name;
                }

                return string.Join("\n", doctors.Select(d => $"• {d.Name} – {d.Department}"));
            }

            // ============= GetBusySlots =============
            if (llm.Intent == Intent.GetBusySlots)
            {
                int doctorId;

                // 1) Nếu LLM đã parse được doctorId thì dùng luôn
                if (extra.TryGetValue("doctorId", out var docIdObj) &&
                    int.TryParse(docIdObj?.ToString(), out var parsedId))
                {
                    doctorId = parsedId;
                }
                else
                {
                    // 2) Nếu không có doctorId, thử lấy doctorName
                    string? doctorName = null;

                    if (extra.TryGetValue("doctorName", out var docNameObj))
                    {
                        doctorName = docNameObj?.ToString();
                    }
                    else if (!string.IsNullOrWhiteSpace(state.LastDoctorName))
                    {
                        // Dùng lại bác sĩ vừa được nhắc đến ở câu trước
                        doctorName = state.LastDoctorName;
                    }

                    if (string.IsNullOrWhiteSpace(doctorName))
                    {
                        return "Anh/chị muốn xem lịch của bác sĩ nào ạ? (VD: bác sĩ Long khoa Nội tổng quát)";
                    }

                    var found = await _api.SearchDoctorsAsync(
                        doctorName,
                        department: null,
                        gender: null,
                        phone: null,
                        workDate: null,
                        ct);

                    if (found.Count == 0)
                    {
                        return $"Em không tìm thấy bác sĩ nào tên gần giống \"{doctorName}\" ạ. Anh/chị cho em xin tên đầy đủ hoặc khoa của bác với nhé.";
                    }

                    if (found.Count > 1)
                    {
                        var list = string.Join("\n", found.Select(d => $"• ID {d.DoctorId} – {d.Name} ({d.Department})"));
                        return $"Tên \"{doctorName}\" có nhiều bác sĩ, anh/chị có thể chọn 1 trong các bác sau (nhớ ID giúp em nhé):\n{list}";
                    }

                    doctorId = found[0].DoctorId;
                    state.LastDoctorId = doctorId;
                    state.LastDoctorName = found[0].Name;
                }

                // 3) Lấy ngày cần xem
                if (!extra.TryGetValue("date", out var dateObj) ||
                    string.IsNullOrWhiteSpace(dateObj?.ToString()))
                {
                    return "Anh/chị muốn xem lịch bận ngày nào ạ? (định dạng yyyy-MM-dd)";
                }

                if (!DateOnly.TryParse(dateObj!.ToString(), out var date))
                {
                    return "Ngày anh/chị cung cấp chưa đúng định dạng. Anh/chị nhập giúp em dạng yyyy-MM-dd (VD: 2025-11-20) nhé.";
                }

                var slots = await _api.GetBusySlotsAsync(doctorId, date, ct);

                if (slots.Count == 0)
                    return $"Trong ngày {date:yyyy-MM-dd}, bác sĩ hiện chưa có lịch bận nào ạ.";

                var lines = slots.Select(s => $"• {s.AppointHour:HH\\:mm} – {s.Status}");
                return $"Các khung giờ đã bận của bác sĩ trong ngày {date:yyyy-MM-dd}:\n" +
                       string.Join("\n", lines);
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
                    int.TryParse(docIdObj?.ToString(), out var parsedId2))
                {
                    doctorId = parsedId2;
                }
                else
                {
                    if (!extra.TryGetValue("doctorName", out var docNameObj))
                    {
                        // Nếu trước đó đã từng chọn bác sĩ, dùng lại
                        if (state.LastDoctorId.HasValue)
                        {
                            doctorId = state.LastDoctorId.Value;
                        }
                        else
                        {
                            return "Dạ anh/chị muốn khám với bác sĩ nào ạ? (Tên bác sĩ)";
                        }
                    }
                    else
                    {
                        var name = docNameObj?.ToString() ?? string.Empty;

                        var found = await _api.SearchDoctorsAsync(name, null, null, null, null, ct);

                        if (found.Count == 0)
                            return $"Dạ em không tìm thấy bác sĩ nào tên '{name}' ạ.";

                        if (found.Count > 1)
                            return $"Dạ tên '{name}' có nhiều bác sĩ, anh/chị cho em thêm khoa hoặc giới tính của bác giúp em với ạ.";

                        doctorId = found[0].DoctorId;
                        state.LastDoctorId = doctorId;
                        state.LastDoctorName = found[0].Name;
                    }
                }

                // ===== LẤY NGÀY KHÁM =====
                if (!extra.TryGetValue("date", out var dateObj2) ||
                    string.IsNullOrWhiteSpace(dateObj2?.ToString()))
                {
                    return "Bạn muốn khám ngày nào? (định dạng yyyy-MM-dd)";
                }

                if (!DateOnly.TryParse(dateObj2!.ToString(), out var date2))
                {
                    return "Ngày bạn cung cấp không hợp lệ. Vui lòng nhập yyyy-MM-dd (VD: 2025-11-20).";
                }

                // ===== LẤY GIỜ KHÁM =====
                if (!extra.TryGetValue("time", out var timeObj) ||
                    string.IsNullOrWhiteSpace(timeObj?.ToString()))
                {
                    return "Bạn muốn khám lúc mấy giờ? (định dạng HH:mm, VD: 09:00)";
                }

                if (!TimeOnly.TryParse(timeObj!.ToString(), out var time))
                {
                    return "Giờ bạn cung cấp không hợp lệ. Vui lòng nhập HH:mm (VD: 09:00).";
                }

                var payload = new PublicBookingRequest
                {
                    FullName = "Khách Online",
                    Phone = "0000000000",
                    Email = "guest@example.com",
                    Date = date2,
                    DoctorId = doctorId,
                    AppointHour = time,
                    Gender = null,
                    DateOfBirth = null,
                    Symptom = state.Symptom
                };

                var result = await _api.CreatePublicAsync(payload, ct);

                state.Symptom = null;
                state.IsWaitingSymptom = false;

                return $"Đặt lịch thành công lúc {result.AppointHour:HH\\:mm} ngày {result.Date:yyyy-MM-dd} với {result.DoctorName}. Mã lịch: {result.AppointmentCode}.";
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

            // ===== Fallback: ưu tiên dùng naturalReply của LLM =====
            if (!string.IsNullOrWhiteSpace(llm.NaturalReply))
                return llm.NaturalReply;

            return "Hiện tôi chưa hỗ trợ yêu cầu này, bạn có thể diễn đạt lại giúp tôi không?";
        }
    }

    public class SessionState
    {
        public bool IsWaitingSymptom { get; set; }
        public string? Symptom { get; set; }

        // Để nhớ bác sĩ vừa nhắc tới (hỗ trợ flow nhiều bước)
        public int? LastDoctorId { get; set; }
        public string? LastDoctorName { get; set; }
    }
}
