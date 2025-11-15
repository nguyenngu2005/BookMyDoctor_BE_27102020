using System.Text.Json;

namespace BookMyDoctor_WebAPI.RequestModel.Chat
{
    public sealed class LlmStructuredOutput
    {
        // ==== Intent chính ====
        public Intent Intent { get; set; } = Intent.Unknown;

        // ==== Thông tin hẹn khám / y tế ====
        public string Symptom { get; set; } = string.Empty;
        public string DoctorName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;   // YYYY-MM-DD
        public string Time { get; set; } = string.Empty;   // HH:mm
        public int BookingId { get; set; }

        // ==== Thông tin liên hệ bệnh nhân (cho gửi mail) ====
        public string Email { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;

        // ==== Các flag thiếu thông tin ====
        public bool NeedSymptom { get; set; }
        public bool NeedDate { get; set; }
        public bool NeedTime { get; set; }
        public bool NeedDoctor { get; set; }

        // ==== Câu chat dễ thương gửi cho user ====
        public string NaturalReply { get; set; } = string.Empty;

        // ==== Extra: để chứa các key phụ (phòng khi prompt thêm field mới) ====
        public Dictionary<string, object?> Extra { get; set; } = new();

        // Lưu lại raw JSON string (nếu cần debug)
        public string? Raw { get; set; }
    }
}
