namespace BookMyDoctor_WebAPI.RequestModel
{
    public class DoctorScheduleRequest
    {
        public int ScheduleId { get; set; }
        public int DoctorId { get; set; }
        public string DoctorName { get; set; } = string.Empty;
        public DateOnly WorkDate { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string Status { get; set; } = "Scheduled";
        public bool IsActive { get; set; }
    }
}
