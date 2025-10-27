//Magic. Don't touch
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BookMyDoctor_WebAPI.Models
{
    public class Prescription
    {
        [Key]
        public int PresId { get; set; }

        [Required]
        public int AppointId { get; set; }            // FK -> appointments

        [ForeignKey(nameof(AppointId))]
        [JsonIgnore]
        public Appointment? Appointment { get; set; }

        public string? Description { get; set; }

        [Column(TypeName = "datetime2")]
        public DateTime? DateCreated { get; set; }    // DEFAULT GETDATE() (config trong DbContext nếu muốn)
        public bool IsActive { get; set; } = true; // default true
    }
}
