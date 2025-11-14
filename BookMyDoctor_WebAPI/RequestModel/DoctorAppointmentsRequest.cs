namespace BookMyDoctor_WebAPI.RequestModel
{
    public sealed class DoctorAppointmentsRequest
    {
        public int AppointId { get; set; }
        public int? DoctorId { get; set; }
        public int? PatientId { get; set; }
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
    }
}
