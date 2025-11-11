namespace BookMyDoctor_WebAPI.RequestModel.Chat;

// Ánh xạ nhanh các DTO cần dùng theo swagger.json
public record DoctorDto(int DoctorId, string? Name, string? Department, string? Gender, string? Phone);
public record BusySlotDto(string? Name, string? Phone, string AppointHour, string? Status);

public record PublicBookingRequestDto(
    string? FullName, string? Phone, string? Email,
    DateOnly Date, int DoctorId, TimeOnly AppointHour,
    string? Gender = null, DateOnly? DateOfBirth = null, string? Symptom = null);

public record BookingResultDto(
    int AppointmentId, string? AppointmentCode, int PatientId,
    int ScheduleId, string? DoctorName, DateOnly Date, TimeOnly AppointHour);