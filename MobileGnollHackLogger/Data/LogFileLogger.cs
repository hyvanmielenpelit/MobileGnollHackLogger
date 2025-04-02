namespace MobileGnollHackLogger.Data
{
    public class LogFileLogger
    {
        private string? _logFile = null;

        public LogFileLogger(IConfiguration configuration)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            _logFile = configuration["LogFile"];

            if (string.IsNullOrEmpty(_logFile))
            {
                throw new Exception("LogFile in Configuration is null.");
            }
        }

        public async Task WriteLogAsync(string message)
        {
            if (string.IsNullOrEmpty(_logFile))
            {
                throw new Exception("_logFile is null.");
            }
            try
            {
                await System.IO.File.AppendAllTextAsync(_logFile, DateTime.Now.ToString("s") + "\t" + message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                throw new Exception("Error writing to log file.", ex);
            }
        }
    }
}
