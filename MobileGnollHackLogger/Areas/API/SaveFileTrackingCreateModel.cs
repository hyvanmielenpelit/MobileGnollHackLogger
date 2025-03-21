using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Areas.API
{
    public class SaveFileTrackingCreateModel : LoginInfoModel
    {
        [Required]
        public long TimeStamp { get; set; }

        [Required]
        public string? Sha256 { get; set; }

        [Required]
        public long FileLength { get; set; }

        public SaveFileTrackingCreateModel()
        {
            
        }

        public string GetData()
        {
            return TimeStamp.ToString() + "|" + Sha256 + "|" + FileLength.ToString();
        }
    }
}
