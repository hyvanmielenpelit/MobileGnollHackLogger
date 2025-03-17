namespace MobileGnollHackLogger.Areas.API
{
    public class SaveFileTrackingUseModel : LoginInfoModel
    {
        public long Id { get; set; }
        public string? UniqueId { get; set; }

        public SaveFileTrackingUseModel()
        {
            
        }

        public SaveFileTrackingUseModel(long id, Guid uniqueId)
        {
            Id = id;
            UniqueId = uniqueId.ToString();
        }
    }
}
