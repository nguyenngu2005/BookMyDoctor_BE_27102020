//Magic. Don't touch
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
//using BookMyDoctor_WebAPI.Models;

namespace BookMyDoctor_WebAPI.Models
{
    [Index(nameof(ScheduleId), nameof(AppointHour), IsUnique = true)] // UNIQUE (ScheduleId, AppointHour)
    public class Appointment
    {
        [Key]
        public int AppointId { get; set; }

        [Required]
        public int PatientId { get; set; }            // FK -> patients

        [JsonIgnore]
        public Patient? Patient { get; set; }

        [Required]
        public int ScheduleId { get; set; }           // FK -> schedules

        [JsonIgnore]
        public Schedule? Schedule { get; set; }

        [Required]
        public TimeOnly AppointHour { get; set; }     // TIME

        [Required, MaxLength(10)]
        public string Status { get; set; } = "Scheduled";

        public string? Symptom { get; set; }

        [JsonIgnore]
        public ICollection<Prescription> Prescriptions { get; set; } = new List<Prescription>();
        public bool IsActive { get; set; } = true; // default true
    }
}
