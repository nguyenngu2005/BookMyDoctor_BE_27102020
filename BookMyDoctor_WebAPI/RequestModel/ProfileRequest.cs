
namespace BookMyDoctor_WebAPI.RequestModel
{
    public class ProfileRequest
    {
        public string? Username { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }

        // Profile Doctor will add
        public string? Name { get; set; }
        public string? Gender { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Address { get; set; }
        public string? Department { get; set; }
        public int? ExperienceYear { get; set; }
        public string? WorkTime { get; set; }

        // Dung de set sinh lich tu dong 
        public List<string>? WorkingDays { get; set; }
        public string? WorkingTimeStart { get; set; }
        public string? WorkingTimeEnd { get; set; }
    }
}
