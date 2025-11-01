namespace BookMyDoctor_WebAPI.RequestModel
{
    public class CreateDoctorRequest
    {
        // Save users table
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
        public string Phone { get; set; } = null!;
        public string Email { get; set; } = null!;

        // Save doctors table
        public string Name { get; set; } = null!;
        public string Gender { get; set; } = null!;
        public DateOnly DateOfBirth { get; set; }
        public string? Identification { get; set; }
        public string Department { get; set; } = null!;
        public int ExperienceYears { get; set; }

        // Dung de set sinh lich tu dong 
        //public List<string>? DefaultDays { get; set; }
        //public string? DefaultStart { get; set; }
        //public string? DefaultEnd { get; set; }
    }
}
