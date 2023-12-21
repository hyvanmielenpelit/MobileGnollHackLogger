using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MobileGnollHackLogger.Data;
using System.Net;
using System.Text;

namespace MobileGnollHackLogger.Areas.API
{
    [ApiController]
    public class LogController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LogModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly string _dumplogBasePath = "";
        private readonly DbLogger _dbLogger;

        public LogController(SignInManager<ApplicationUser> signInManager, ILogger<LogModel> logger, 
            IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _logger = logger;
            _configuration = configuration;
            _dbContext = dbContext;
            _dbLogger = new DbLogger(_dbContext);
            _dbLogger.LogType = LogType.GameLog;
            _dbLogger.LogSubType = RequestLogSubType.Default;

            _dumplogBasePath = _configuration["DumpLogPath"] ?? "";

            if(string.IsNullOrEmpty(_dumplogBasePath))
            {
                throw new Exception("DumpLogPath is null");
            }
        }

        [Route("xlogfile")]
        [HttpGet]
        public IActionResult Get()
        {
            return Get(0);
        }
        
        [Route("xlogfile/{lastId}")]
        [HttpGet]
        public IActionResult Get(long? lastId)
        {
            var gameLogs = _dbContext.GameLog.Where(gl => gl.Id > (lastId ?? 0));
            StringBuilder sb = new StringBuilder();
            foreach(var gameLog in gameLogs)
            {
                sb.AppendLine(gameLog.ToXLogString());
            }
            return Content(sb.ToString(), "text/plain", Encoding.ASCII);
        }


        [Route("api/games/csv")]
        [HttpGet]
        public IActionResult GetCsv()
        {
            return GetCsv(0);
        }

        [Route("api/games/csv/{lastId}")]
        [HttpGet]
        public IActionResult GetCsv(long? lastId)
        {
            var gameLogs = _dbContext.GameLog.Where(gl => gl.Id > (lastId ?? 0));
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(GameLog.GetCsvHeader());
            foreach (var gameLog in gameLogs)
            {
                sb.AppendLine(gameLog.ToCsvString());
            }
            return Content(sb.ToString(), "text/plain", Encoding.UTF8);
        }

        [Route("xlogfile")]
        [HttpPost]
        public async Task<IActionResult> Post([FromForm] LogModel model)
        {
            try
            {
                _dbLogger.RequestMethod = Request.Method;
                _dbLogger.LastRequestId = Guid.NewGuid();
                _dbLogger.RequestPath = Request.GetEncodedUrl();
                _dbLogger.UserIPAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString();
                
                if (model == null)
                {
                    int responseCode = 400;
                    await _dbLogger.LogRequestAsync("model is null", Data.LogLevel.Error, responseCode);
                    return StatusCode(responseCode); //Bad Request
                }

                _dbLogger.RequestData = model.XLogEntry;

                if (model.UserName == null)
                {
                    int responseCode = 400;
                    await _dbLogger.LogRequestAsync("model.UserName is null", Data.LogLevel.Error, responseCode);
                    return StatusCode(responseCode); //Bad Request
                }

                _dbLogger.RequestUserName = model.UserName;

                if (model.Password == null)
                {
                    int responseCode = 400;
                    await _dbLogger.LogRequestAsync("model.Password is null", Data.LogLevel.Error, responseCode);
                    return StatusCode(responseCode); //Bad Request
                }
                if(model.AntiForgeryToken == null)
                {
                    int responseCode = 400;
                    await _dbLogger.LogRequestAsync("model.AntiForgeryToken is null", Data.LogLevel.Error, responseCode);
                    return StatusCode(responseCode); //Bad Request
                }

                _dbLogger.RequestAntiForgeryToken = model.AntiForgeryToken;

                var antiForgeryToken = _configuration["AntiForgeryToken"];
                if (antiForgeryToken != model.AntiForgeryToken)
                {
                    int responseCode = 401;
                    await _dbLogger.LogRequestAsync($"AntiForgeryToken is invalid. Request: '{model.AntiForgeryToken}'. Server: '{antiForgeryToken}'.",
                        Data.LogLevel.Warning, responseCode);
                    return StatusCode(responseCode); //Not Authorized
                }
                
                var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, false, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    _dbLogger.LoginSucceeded = true;

                    if (!string.IsNullOrEmpty(model.XLogEntry) && model.PlainTextDumpLog != null && model.HtmlDumpLog != null)
                    {
                        _dbLogger.LogSubType = RequestLogSubType.MainFunctionality;

                        //Sign in succeedeed
                        XLogFileLine xLogFileLine = new XLogFileLine(model.XLogEntry);

                        //Change user name to the account name
                        xLogFileLine.Name = model.UserName;

                        // Write Dumplog Files
                        string dir = Path.Combine(_dumplogBasePath, xLogFileLine.Name);
                        if (!System.IO.Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        string filePathWithoutExtension = dir + @"\" + "gnollhack." + xLogFileLine.Name + "." + xLogFileLine.StartTimeUTC;
                        string plainTextDumpLogPath = filePathWithoutExtension + ".txt";
                        string htmlDumpLogPath = filePathWithoutExtension + ".html";

                        if (System.IO.File.Exists(plainTextDumpLogPath))
                        {
                            int responseCode = 409;
                            await _dbLogger.LogRequestAsync("Character already exists. Plain Text Dumplog Path found: " + plainTextDumpLogPath,
                                Data.LogLevel.Warning, responseCode);
                            return StatusCode(responseCode); //Character already exists
                        }

                        if (System.IO.File.Exists(htmlDumpLogPath))
                        {
                            int responseCode = 409;
                            await _dbLogger.LogRequestAsync("Character already exists. HTML Dumplog Path found: " + htmlDumpLogPath,
                                Data.LogLevel.Warning, responseCode);
                            return StatusCode(responseCode); //Character already exists
                        }

                        using var plainTextOutStream = System.IO.File.OpenWrite(plainTextDumpLogPath);
                        var t1 = model.PlainTextDumpLog.CopyToAsync(plainTextOutStream);

                        using var htmlOutStream = System.IO.File.OpenWrite(htmlDumpLogPath);
                        var t2 = model.HtmlDumpLog.CopyToAsync(htmlOutStream);

                        Task.WaitAll(t1, t2);

                        try
                        {
                            GameLog gameLog = new GameLog(xLogFileLine, _dbContext);
                            await _dbContext.GameLog.AddAsync(gameLog);
                            await _dbContext.SaveChangesAsync();
                            long id = gameLog.Id;
                            if (id == 0)
                            {
                                int responseCode = 500;
                                await _dbLogger.LogRequestAsync("Inserted Id is 0.", Data.LogLevel.Error, responseCode);
                                Response.StatusCode = responseCode;
                                return Content("Inserted Id is 0.");
                            }
                            await _dbLogger.LogRequestAsync("GameLog successfully inserted to the database", Data.LogLevel.Info, 200);
                            return Content(id.ToString(), "text/plain", Encoding.UTF8); //OK
                        }
                        catch(InvalidOperationException invEx)
                        {
                            int responseCode = 410;
                            await _dbLogger.LogRequestAsync("GameLog insertion to database failed. Message: " + invEx.Message, Data.LogLevel.Error, responseCode);
                            return StatusCode(responseCode); //Gone
                        }
                        catch(Exception ex)
                        {
                            int responseCode = 500;
                            await _dbLogger.LogRequestAsync("GameLog insertion to database failed. Message: " + ex.Message, Data.LogLevel.Error, responseCode);
                            Response.StatusCode = responseCode; //Server Error
                            return Content(ex.Message.ToString());
                        }
                    }
                    else if(string.IsNullOrEmpty(model.XLogEntry) && model.PlainTextDumpLog == null && model.HtmlDumpLog == null)
                    {
                        //Test Connection
                        _dbLogger.LogSubType = RequestLogSubType.TestConnection;
                        await _dbLogger.LogRequestAsync("Test connection and login succeeded.", Data.LogLevel.Info, 200);
                        return Ok();
                    }
                    else
                    {
                        _dbLogger.LogSubType = RequestLogSubType.PartialDataError;
                        int responseCode = 400;
                        await _dbLogger.LogRequestAsync("Login succeeded but there is missing data.", Data.LogLevel.Error, responseCode);
                        return StatusCode(responseCode); //Bad Request
                    }
                }
                if (result.RequiresTwoFactor)
                {
                    _dbLogger.LoginSucceeded = false;
                    int responseCode = 412;
                    await _dbLogger.LogRequestAsync("Login requires two factor authentication. Error.", Data.LogLevel.Error, responseCode);
                    return StatusCode(responseCode);
                }
                if (result.IsLockedOut)
                {
                    _dbLogger.LoginSucceeded = false;
                    int responseCode = 423;
                    await _dbLogger.LogRequestAsync("User is locked out.", Data.LogLevel.Warning, responseCode);
                    return StatusCode(responseCode);
                }
                else
                {
                    _dbLogger.LoginSucceeded = false;
                    int responseCode = 403;
                    await _dbLogger.LogRequestAsync($"Login failed for user '{model.UserName}'.", Data.LogLevel.Warning, responseCode);
                    return StatusCode(responseCode);
                }
            }
            catch (Exception ex) 
            {
                int responseCode = 500;
                string message = (ex.InnerException ?? ex).GetType().FullName + ", Message: " + ex.Message;
                await _dbLogger.LogRequestAsync(message, Data.LogLevel.Warning, responseCode);
                Response.StatusCode = responseCode;
                return Content(message);
            }
        }
    }
}
