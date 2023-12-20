namespace MobileGnollHackLogger.Data
{
    public class DbLogger
    {
        private readonly ApplicationDbContext _dbContext;

        public DbLogger(ApplicationDbContext dbContext, LogLevel minLogLevel = LogLevel.Debug)
        {
            _dbContext = dbContext;
            MinLogLevel = minLogLevel;
        }

        public string? UserName { get; set; }
        public string? RequestCommand { get; set; }
        public LogType? LogType { get; set; }
        public string? RequestData { get; set; }
        public Guid? RequestId { get; set; }
        public string? RequestPath { get; set; }
        public string? RequestUserName { get; set; }
        public string? RequestAntiForgeryToken { get; set; }

        public LogLevel MinLogLevel { get; set; }

        public async Task LogAsync(string message) 
        {
            await LogToDatabaseAsync(message, LogLevel.Info);
        }

        public async Task LogAsync(string message, LogLevel level, int? responseCode = null)
        {
            await LogToDatabaseAsync(message, level, responseCode);
        }

        public async Task LogAsync(LogInfo logInfo) 
        {
            await AddToDatabaseAsync(logInfo);
        }

        private async Task LogToDatabaseAsync(string message, LogLevel level, int? responseCode = null)
        {
            if(level < MinLogLevel)
            {
                //Do nothing
                return;
            }

            LogInfo log;
            
            if(!string.IsNullOrEmpty(UserName))
            {
                log = new LogInfo(UserName, _dbContext);
            }
            else
            {
                log = new LogInfo();
            }

            log.Message = message;
            log.ResponseCode = responseCode;
            log.Level = level;
            log.RequestData = RequestData;
            log.RequestCommand = RequestCommand;
            log.RequestId = RequestId;
            log.RequestPath = RequestPath;
            log.RequestUserName = RequestUserName;
            log.RequestAntiForgeryToken = RequestAntiForgeryToken;
            log.Type = LogType;

            await AddToDatabaseAsync(log);
        }

        public async Task AddToDatabaseAsync(LogInfo log)
        {
            if (log.Level < MinLogLevel)
            {
                //Do nothing
                return;
            }

            try
            {
                _dbContext.Add(log);
                await _dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw new Exception("Saving LogInfo to database failed.", ex);
            }
        }
    }
}
