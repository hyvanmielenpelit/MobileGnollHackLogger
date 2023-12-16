using System.ComponentModel.DataAnnotations.Schema;

namespace MobileGnollHackLogger.Data
{
    public class Bones
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [ForeignKey("AspNetUser")]
        public string? AspNetUserId { get; set; }
        public ApplicationUser? AspNetUser { get; set; }

        public Bones()
        {

        }

    }
}
