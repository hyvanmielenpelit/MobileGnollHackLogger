using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MobileGnollHackLogger.Data
{
    public enum TransactionType : int
    { 
        Upload = 0, 
        Download = 1,
        Deletion = 2
    }

    [PrimaryKey(nameof(Id))]
    public class BonesTransaction
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [ForeignKey("AspNetUser")]
        public string? AspNetUserId { get; set; }
        public ApplicationUser? AspNetUser { get; set; }

        public DateTime Date { get; set; }

        public TransactionType Type { get; set; }

        public long BonesId { get; set; }

        public int DifficultyLevel { get; set; }

        [MaxLength(32)]
        public string? Platform { get; set; }

        [MaxLength(32)]
        public string? PlatformVersion { get; set; }

        [MaxLength(128)]
        public string? Port { get; set; }

        [MaxLength(32)]
        public string? PortVersion { get; set; }

        [MaxLength(32)]
        public string? PortBuild { get; set; }

        public ulong VersionNumber { get; set; }

        public ulong VersionCompatibilityNumber { get; set; }

        public BonesTransaction()
        {
            
        }

        public BonesTransaction(string userName, TransactionType type, Bones? bones, ApplicationDbContext dbContext)
            : this(userName, type, bones, (ApplicationUser)dbContext.Users.First(u => u.UserName == userName))
        {

        }

        public BonesTransaction(string userName, TransactionType type, Bones? bones, ApplicationUser user)
        {
            AspNetUserId = user.Id;
            AspNetUser = user;
            Type = type;

            if (bones != null)
            {
                BonesId = bones.Id;
                DifficultyLevel = bones.DifficultyLevel;
                Platform = bones.Platform;
                PlatformVersion = bones.PlatformVersion;
                Port = bones.Port;
                PortVersion = bones.PortVersion;
                PortBuild = bones.PortBuild;
                VersionNumber = bones.VersionNumber;
                VersionCompatibilityNumber = bones.VersionCompatibilityNumber;
            }
        }
    }
}
