namespace BookMyDoctor_WebAPI.RequestModel
{
    public class PatientDetailRequest
    {
        public int? DoctorId { get; set; }
        public string? FullName { get; set; }
        public string? Username { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? Status { get; set; }
        public string? Symptoms { get; set; }
        public string? Prescription { get; set; }
        public DateOnly? AppointDate { get; set; }
        public TimeOnly? AppointHour { get; set; }

        // Thông tin mở rộng từ các bảng khác
        //public int TotalAppointments { get; set; }
        //public int TotalPrescriptions { get; set; }
    }

    public class PatientUpdateRequest
    {
        public string? Status { get; set; }
        public string? Symptoms { get; set; }
        public string? Prescription { get; set; }
    }
}
