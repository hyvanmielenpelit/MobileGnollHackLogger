using Azure.Core;
using System.Data;

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

        public string? RequestMethod { get; set; }
        public string? UserIPAddress { get; set; }
        public LogType? LogType { get; set; }
        public RequestLogSubType? LogSubType { get; set; }
        public string? RequestData { get; set; }
        public Guid? LastRequestId { get; set; }
        public string? RequestPath { get; set; }
        public string? RequestUserName { get; set; }
        public string? RequestAntiForgeryToken { get; set; }
        public bool? LoginSucceeded { get; set; }

        public LogLevel MinLogLevel { get; set; }

        public async Task LogRequestAsync(string message)
        {
            await LogRequestToDatabaseAsync(message, LogLevel.Info);
        }

        public async Task LogRequestAsync(string message, LogLevel level, int? responseCode = null)
        {
            await LogRequestToDatabaseAsync(message, level, responseCode);
        }

        public async Task LogRequestAsync(RequestInfo reqInfo)
        {
            await AddFailToDatabaseAsync(reqInfo);
        }

        private async Task LogRequestToDatabaseAsync(string message, LogLevel level, int? responseCode = null)
        {
            if (level < MinLogLevel)
            {
                //Do nothing
                return;
            }

            var req = InitializeReq(message, level, responseCode);

            await AddFailToDatabaseAsync(req);
        }

        public async Task AddFailToDatabaseAsync(RequestInfo req)
        {
            if (req.Level < MinLogLevel)
            {
                //Do nothing
                return;
            }

            var req2 = GetReqFromDatabase(req);

            if (req2 != null)
            {
                try
                {
                    req2.Count++;
                    req2.LastDate = DateTime.Now;
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    throw new Exception($"Incrementing count of row {req2.Id} to database reqed.", ex);
                }
            }
            else
            {
                try
                {
                    req.Count = 1;
                    _dbContext.Add(req);
                    await _dbContext.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    throw new Exception("Saving new FailedRequestInfo to database reqed.", ex);
                }
            }
        }

        public void LogRequest(string message)
        {
            LogRequestToDatabase(message, LogLevel.Info);
        }

        public void LogRequest(string message, LogLevel level, int? responseCode = null)
        {
            LogRequestToDatabase(message, level, responseCode);
        }

        public void LogRequest(RequestInfo reqInfo)
        {
            AddFailToDatabase(reqInfo);
        }

        private void LogRequestToDatabase(string message, LogLevel level, int? responseCode = null)
        {
            if (level < MinLogLevel)
            {
                //Do nothing
                return;
            }

            var req = InitializeReq(message, level, responseCode);

            AddFailToDatabase(req);
        }

        public void AddFailToDatabase(RequestInfo req)
        {
            if (req.Level < MinLogLevel)
            {
                //Do nothing
                return;
            }

            var req2 = GetReqFromDatabase(req);

            if (req2 != null)
            {
                try
                {
                    req2.Count++;
                    req2.LastDate = DateTime.Now.ToUniversalTime();
                    _dbContext.SaveChanges();
                }
                catch (Exception ex)
                {
                    throw new Exception($"Incrementing count of row {req2.Id} to database reqed.", ex);
                }
            }
            else
            {
                try
                {
                    req.Count = 1;
                    _dbContext.Add(req);
                    _dbContext.SaveChanges();
                }
                catch (Exception ex)
                {
                    throw new Exception("Saving new FailedRequestInfo to database reqed.", ex);
                }
            }
        }

        private RequestInfo? GetReqFromDatabase(RequestInfo req)
        {
            return _dbContext.RequestLogs.FirstOrDefault(f => f.RequestUserName == req.RequestUserName
                && f.Message == req.Message
                && f.Type == req.Type
                && f.SubType == req.SubType
                && f.RequestData == req.RequestData
                && f.RequestMethod == req.RequestMethod
                && f.RequestAntiForgeryToken == req.RequestAntiForgeryToken
                && f.RequestPath == req.RequestPath
                && f.RequestUserName == req.RequestUserName
                //&& f.UserIPAddress == req.UserIPAddress //IP Adress seems to change often
                );
        }

        private RequestInfo InitializeReq(string message, LogLevel level, int? responseCode)
        {
            RequestInfo req;

            if (!string.IsNullOrEmpty(RequestUserName))
            {
                req = new RequestInfo(RequestUserName, _dbContext);
            }
            else
            {
                req = new RequestInfo();
            }

            req.Message = message;
            req.ResponseCode = responseCode;
            req.Level = level;
            req.RequestData = RequestData;
            req.RequestMethod = RequestMethod;
            req.RequestPath = RequestPath;
            req.RequestUserName = RequestUserName;
            req.RequestAntiForgeryToken = RequestAntiForgeryToken;
            req.Type = LogType;
            req.SubType = LogSubType;
            req.LastRequestId = LastRequestId;
            req.UserIPAddress = UserIPAddress;
            req.LoginSucceeded = LoginSucceeded;

            return req;
        }
    }
}
