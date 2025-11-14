namespace BookMyDoctor_WebAPI.RequestModel
{
    public class AddScheduleRequest
    {
        public int DoctorId { get; set; }
        public DateOnly WorkDate { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string Status { get; set; } = "Scheduled";
        public bool IsActive { get; set; } = true;
    }
}
