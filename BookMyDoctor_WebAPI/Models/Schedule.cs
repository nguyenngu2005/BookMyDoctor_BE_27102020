// Magic. Don't touch
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace BookMyDoctor_WebAPI.Models
{
    public class Schedule
    {
        [Key]
        public int ScheduleId { get; set; }

        [Required]
        public int DoctorId { get; set; }             // FK -> doctors

        [JsonIgnore]
        public Doctor? Doctor { get; set; }

        [Required, Column(TypeName = "date")]
        public DateOnly WorkDate { get; set; }

        public TimeOnly StartTime { get; set; }
        public TimeOnly EndTime { get; set; }

        [Required, MaxLength(10)]
        public string Status { get; set; } = "Scheduled"; // CHECK (Scheduled, Completed, Cancelled)

        [JsonIgnore]
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

        [Column(TypeName = "bit")]
        public bool IsActive { get; set; } = true;
    }
}
