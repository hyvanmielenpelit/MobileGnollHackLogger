using System.ComponentModel.DataAnnotations.Schema;

namespace MobileGnollHackLogger.Data
{
    public class SaveFileTracking
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public Guid UniqueId { get; set; }
        
        public DateTime CreatedDate { get; set; }
        
        public DateTime? UsedDate { get; set; }

        [ForeignKey("AspNetUser")]
        public string? AspNetUserId { get; set; }
        public ApplicationUser? AspNetUser { get; set; }

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
