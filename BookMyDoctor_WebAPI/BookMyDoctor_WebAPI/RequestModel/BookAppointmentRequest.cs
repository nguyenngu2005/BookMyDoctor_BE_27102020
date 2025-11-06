// Request/public booking
//using Org.BouncyCastle.Cms;
// Request/private booking (khi đã đăng nhập)
public sealed class PublicBookingRequest
{
    public string FullName { get; set; } = default!;
    public string Phone { get; set; } = default!;
    public string Email { get; set; } = default!;
    public DateOnly Date { get; set; }                  // 15/10/2025
    public int DoctorId { get; set; }
    public TimeOnly AppointHour { get; set; }           // 17:00
    public string? Gender { get; set; }                 // "Male"/"Female" (optional cho public)
    public DateOnly? DateOfBirth { get; set; }
    public string? Symptom { get; set; }                // ≤ 500 chars (giống UI)
}
// Phản hồi booking
public sealed class BookingResult
{
    public int AppointmentId { get; set; }
    public string AppointmentCode { get; set; } = default!;  // ví dụ: BK-20251015-7F3C
    public int PatientId { get; set; }
    public int ScheduleId { get; set; }
    public string DoctorName { get; set; } = default!;
    public DateOnly Date { get; set; }
    public TimeOnly AppointHour { get; set; }
}
public class BusySlot
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public TimeOnly AppointHour { get; set; }
    public string? Status { get; set; }
}

