//Magic. Don't touch
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

namespace BookMyDoctor_WebAPI.Models
{
    [Index(nameof(Username), IsUnique = true)]
    public class User
    {
        [Key]
        public int UserId { get; set; }               // INT IDENTITY

        [Required, MaxLength(100)]
        public string Username { get; set; } = null!; // UNIQUE

        [Required]
        public byte[] PasswordHash { get; set; } = Array.Empty<byte>(); // VARBINARY(64)
        [JsonIgnore]
        public byte[] PasswordSalt { get; set; } = Array.Empty<byte>(); // VARBINARY(32)
        [Required, MaxLength(15)]
        public string Phone { get; set; } = null!;

        [MaxLength(250)]
        public string? Email { get; set; }

        [Required, MaxLength(10)]
        public string RoleId { get; set; } = "R03";   // DEFAULT 'R03'

        [JsonIgnore]
        public Role? Role { get; set; }               // FK -> roles

        [JsonIgnore]
        public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();

        [JsonIgnore]
        public ICollection<Patient> Patients { get; set; } = new List<Patient>();
        public ICollection<OtpTicket> OtpTickets { get; set; } = new List<OtpTicket>();
        public bool IsActive { get; set; } = true; // default true

    }
}
