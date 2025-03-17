using System.ComponentModel.DataAnnotations;

namespace MobileGnollHackLogger.Areas.API
{
    public class SaveFileTrackingCreateModel : LoginInfoModel
    {
        [Required]
        public long TimeStamp { get; set; }

        public SaveFileTrackingCreateModel()
        {
            
        }
    }
}
