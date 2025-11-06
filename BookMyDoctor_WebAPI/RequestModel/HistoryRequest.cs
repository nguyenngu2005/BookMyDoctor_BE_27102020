namespace BookMyDoctor_WebAPI.RequestModel
{
    public class HistoryRequest
    {
        public string? NamePatient { get; set; }
        public string NameDoctor { get; set; } = null!;
        public string? PhoneDoctor { get; set; }
        public string Department { get; set; } = null!;
        public TimeOnly AppointHour { get; set; }
        public DateOnly AppointDate { get; set; }
        public string Status { get; set; } = null!;
        public string Symptoms { get; set; } = null!;
        public string Prescription { get; set; } = null!;
    }
}
