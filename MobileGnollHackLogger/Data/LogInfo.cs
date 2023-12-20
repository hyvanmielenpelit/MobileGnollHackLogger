using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MobileGnollHackLogger.Data
{
    public enum LogLevel : int
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public enum LogType : int
    {
        Other = 0,
        GameLog = 1,
        Bones = 2
    }

    [PrimaryKey(nameof(Id))]
    public class LogInfo
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public DateTime CreationDate { get; set; }
        public Guid? RequestId { get; set; }

        [MaxLength(2000)]
        public string? RequestPath {  get; set; }

        public LogLevel Level { get; set; }

        public LogType? Type { get; set; }

        [MaxLength(128)]
        public string? RequestCommand { get; set; }

        public string? Message { get; set; }

        public string? RequestData { get; set; }

        [MaxLength(256)]
        public string? RequestUserName { get; set; }

        [MaxLength(256)]
        public string? RequestAntiForgeryToken { get; set; }

        public int? ResponseCode { get; set; }

        [ForeignKey("AspNetUser")]
        public string? AspNetUserId { get; set; }
        public ApplicationUser? AspNetUser { get; set; }

        public LogInfo()
        {
            
        }

        public LogInfo(string userName, ApplicationDbContext dbContext)
        {
            var user = dbContext.Users.First(u => u.UserName == userName);
            AspNetUserId = user.Id;
            AspNetUser = (ApplicationUser)user;
        }
    }
}
