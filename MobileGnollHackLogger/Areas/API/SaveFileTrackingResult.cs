namespace MobileGnollHackLogger.Areas.API
{
    public class SaveFileTrackingResult
    {
        public long Id { get; set; }
        public string UniqueId { get; set; }

        public SaveFileTrackingResult(long id, Guid uniqueId)
        {
            Id = id;
            UniqueId = uniqueId.ToString();
        }
    }
}
