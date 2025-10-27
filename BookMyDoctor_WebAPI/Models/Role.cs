//Magic. Don't touch
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace BookMyDoctor_WebAPI.Models
{
    public class Role
    {
        [Key]
        [MaxLength(10)]
        public string RoleId { get; set; } = null!;   // PK, VARCHAR(10)

        [Required, MaxLength(100)]
        public string RoleName { get; set; } = null!; // UNIQUE (config trong DbContext)

        [JsonIgnore]
        public ICollection<User> Users { get; set; } = new List<User>();
    }
}
