using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MobileGnollHackLogger.Data
{
    [Index(nameof(TimeStamp), nameof(AspNetUserId), IsUnique = true)]
    public class SaveFileTracking
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public long TimeStamp { get; set; }
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime? UsedDate { get; set; }

        [ForeignKey("AspNetUser")]
        public string? AspNetUserId { get; set; }
        public ApplicationUser? AspNetUser { get; set; }

        [MaxLength(64)]
        public string? Sha256 { get; set; }
        public long FileLength { get; set; }


        public SaveFileTracking()
        {
            
        }

        public SaveFileTracking(string userName, ApplicationDbContext dbContext)
        {
            var user = dbContext.Users.First(u => u.UserName == userName);
            AspNetUserId = user.Id;
            AspNetUser = (ApplicationUser)user;
        }
    }
}
