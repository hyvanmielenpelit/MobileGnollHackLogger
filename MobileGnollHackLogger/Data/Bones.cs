using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data.Migrations;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MobileGnollHackLogger.Data
{
    [PrimaryKey(nameof(Id))]
    public class Bones
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [ForeignKey("AspNetUser")]
        public string? AspNetUserId { get; set; }
        public ApplicationUser? AspNetUser { get; set; }

        public int DifficultyLevel { get; set; }

        [MaxLength(4096)]
        public string? BonesFilePath { get; set; }

        [MaxLength(256)]
        public string? OriginalFileName { get; set; }

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

        public DateTime? Created { get; set; }

        public Bones()
        {

        }

        public Bones(string username, string plat, string platver, string port, string portver, string portbuild, ulong vernum, ulong vercompatnum, int difficulty, string bonesfilepath, string originalfilename, ApplicationDbContext dbContext)
        {
            var user = dbContext.Users.First(u => u.UserName == username);
            AspNetUserId = user.Id;
            AspNetUser = (ApplicationUser)user;
            DifficultyLevel = difficulty;
            Platform = plat;
            PlatformVersion = platver;
            Port = port;
            PortVersion = portver;
            PortBuild = portbuild;
            VersionNumber = vernum;
            VersionCompatibilityNumber = vercompatnum;            
            BonesFilePath = bonesfilepath;
            OriginalFileName = originalfilename;
        }
    }
}
