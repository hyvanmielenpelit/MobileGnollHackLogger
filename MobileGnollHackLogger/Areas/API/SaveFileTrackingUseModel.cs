using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Areas.API
{
    public class SaveFileTrackingUseModel : LoginInfoModel
    {
        [Required]
        public string? EncryptedId { get; set; }

        [Required]
        public long TimeStamp { get; set; }

        [Required]
        public string? Sha256 { get; set; }

        [Required]
        public long FileLength { get; set; }

        public SaveFileTrackingUseModel()
        {
            
        }

        public string GetData()
        {
            return EncryptedId + "|" + TimeStamp.ToString() + "|" + Sha256 + "|" + FileLength.ToString();
        }

    }
}
