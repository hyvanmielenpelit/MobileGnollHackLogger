using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Areas.API
{
    public class BonesModel
    {
        public IFormFile? BonesFile { get; set; }

        public string? Data { get; set; }

        [Required]
        [RegularExpression(@"^[A-Za-z0-9_]{1,31}$")]
        public string? UserName { get; set; }

        [Required]
        [MaxLength(63)]
        public string? Password { get; set; }

        [Required]
        public string? AntiForgeryToken { get; set; }
    }
}
