namespace BookMyDoctor_WebAPI.RequestModel
{
    public class DoctorScheduleRequest
    {
        public int DoctorId { get; set; }
        public string DoctorName { get; set; } = null!;
        public DateOnly WorkDate { get; set; }
        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }
        public string Status { get; set; } = null!;
        public bool IsActive { get; set; }
    }
}
