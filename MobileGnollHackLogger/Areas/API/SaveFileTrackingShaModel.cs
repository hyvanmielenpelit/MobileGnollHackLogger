using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Areas.API
{
    public class SaveFileTrackingShaModel : LoginInfoModel
    {
        [Required]
        public long Id { get; set; }

        [Required]
        public long TimeStamp { get; set; }

        [Required]
        public string? Sha256 { get; set; }

        [Required]
        public long FileLength { get; set; }

        public SaveFileTrackingShaModel()
        {
            
        }
    }
}