//Magic. Don't touch
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BookMyDoctor_WebAPI.Models
{
    [Table("OtpTickets")]
    public sealed class OtpTicket
    {
        [Key]
        public int OtpId { get; set; }

        [Required]
        public int UserId { get; set; }

        [Required, MaxLength(30)]
        public string Purpose { get; set; } = string.Empty;

        [Required, MaxLength(10)]
        public string Channel { get; set; } = string.Empty;

        // SHA-512 HEX (128 ký tự)
        [Required, MaxLength(128)]
        public string OtpHash { get; set; } = string.Empty;

        // === Dùng timestamp UTC: DateTimeOffset + SQL datetimeoffset ===
        [Required, Column(TypeName = "datetimeoffset")]
        public DateTimeOffset CreatedAtUtc { get; set; }

        [Required, Column(TypeName = "datetimeoffset")]
        public DateTimeOffset ExpireAtUtc { get; set; }

        [MaxLength(200)]
        public string? Destination { get; set; }

        public int Attempts { get; set; } = 0;

        public bool Used { get; set; } = false;

        [ForeignKey(nameof(UserId))]
        public User? User { get; set; }
    }
}
