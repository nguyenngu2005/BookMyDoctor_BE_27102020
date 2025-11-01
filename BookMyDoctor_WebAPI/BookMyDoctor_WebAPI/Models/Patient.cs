//Magic. Don't touch
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BookMyDoctor_WebAPI.Models
{
    public class Patient
    {
        [Key]
        public int PatientId { get; set; }

        public int? UserId { get; set; }              // FK -> users (NULLABLE)

        [JsonIgnore]
        public User? User { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = null!;

        [Required]
        [RegularExpression("Male|Female")]
        public string Gender { get; set; } = null!;

        [Required]
        [Column(TypeName = "date")]
        public DateOnly DateOfBirth { get; set; }     // DATE

        [MaxLength(15)]
        public string? Phone { get; set; }

        [MaxLength(250)]
        public string? Email { get; set; }

        public string? Address { get; set; }

        [JsonIgnore]
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
        public bool IsActive { get; set; } = true; // default true
    }
}
