//Magic. Don't touch
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BookMyDoctor_WebAPI.Models
{
    public class Doctor
    {
        [Key]
        public int DoctorId { get; set; }

        [Required]
        public int UserId { get; set; }               // FK -> users

        [JsonIgnore]
        public User? User { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = null!;

        [Required]
        [RegularExpression("Male|Female")]
        public string Gender { get; set; } = null!;

        [Required]
        [Column(TypeName = "date")]                   // DATE
        public DateOnly DateOfBirth { get; set; }

        [MaxLength(20)]
        public string? Identification { get; set; }   // nullable theo schema

        [MaxLength(15)]
        public string? Phone { get; set; }

        [MaxLength(250)]
        public string? Email { get; set; }

        public string? Address { get; set; }

        [Required, MaxLength(50)]
        public string Department { get; set; } = null!;

        [Range(0, int.MaxValue)]
        public int Experience_year { get; set; }      // map tên cột theo DB
        
        [StringLength(500)]
        public string? Image { get; set; } // hoặc AvatarUrl

        [JsonIgnore]
        public ICollection<Schedule> Schedules { get; set; } = new List<Schedule>();
        public bool IsActive { get; set; } = true; // default true
    }
}
