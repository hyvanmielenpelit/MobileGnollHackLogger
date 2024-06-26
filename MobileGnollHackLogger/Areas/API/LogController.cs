﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using System.Net;
using System.Text;

namespace MobileGnollHackLogger.Areas.API
{
    [ApiController]
    public class LogController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly string _dumplogBasePath = "";
        private readonly DbLogger _dbLogger;

        public LogController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager, 
            IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
            _dbContext = dbContext;
            _dbLogger = new DbLogger(_dbContext);
            _dbLogger.LogType = LogType.GameLog;
            _dbLogger.LogSubType = RequestLogSubType.Default;

            _dumplogBasePath = _configuration["DumpLogPath"] ?? "";

            if (string.IsNullOrEmpty(_dumplogBasePath))
            {
                throw new Exception("DumpLogPath is null");
            }
            _userManager = userManager;
        }

        [Route("xlogfile")]
        [HttpGet]
        public async Task<IActionResult> GetAsync()
        {
            return await GetAsync(0);
        }
        
        [Route("xlogfile/{lastId}")]
        [HttpGet]
        public async Task<IActionResult> GetAsync(long? lastId)
        {
            var gameLogs = await _dbContext.GameLog.Where(gl => gl.Id > (lastId ?? 0)).ToListAsync();
            StringBuilder sb = new StringBuilder();
            foreach(var gameLog in gameLogs)
            {
                sb.AppendLine(gameLog.ToXLogString());
            }

            var xlogfileString = sb.ToString();

            // Range Header Byte Range
            var rangeHeader = Request.Headers.Range;
            var bytesRange = rangeHeader.FirstOrDefault(s => s.StartsWith("bytes"));
            if(bytesRange != null)
            {
                var errorResult = new ContentResult();
                errorResult.ContentType = "text/plain";

                var rangeValueSplit = bytesRange.Split('=');
                if(rangeValueSplit != null && rangeValueSplit.Length == 2)
                {
                    var bytesRangeValue = rangeValueSplit[1].Trim();
                    var bytesRangeValueSplit = bytesRangeValue.Split('-');
                    if(bytesRangeValueSplit != null && bytesRangeValueSplit.Length == 2)
                    {
                        int minRange;
                        if(!int.TryParse(bytesRangeValueSplit[0].Trim(), out minRange))
                        {
                            //This error seems to be handled by CloudFlare already
                            errorResult.StatusCode = 400;
                            errorResult.Content = "Range Header bytes min value is not an integer.";
                            return errorResult;
                        }
                        if(minRange >= xlogfileString.Length)
                        {
                            errorResult.StatusCode = 416;
                            errorResult.Content = "Range Header bytes min value is equal to or greater than the file size.";
                            return errorResult;
                        }
                        int maxRange;
                        if (!int.TryParse(bytesRangeValueSplit[1].Trim(), out maxRange))
                        {
                            //This error seems to be handled by CloudFlare already
                            errorResult.StatusCode = 400;
                            errorResult.Content = "Range Header bytes max value is not an integer.";
                            return errorResult;
                        }
                        if (maxRange < minRange)
                        {
                            errorResult.StatusCode = 416;
                            errorResult.Content = "Range Header bytes max value is less than min value.";
                            return errorResult;
                        }
                        if(maxRange >= xlogfileString.Length)
                        {
                            xlogfileString = xlogfileString.Substring(minRange);
                        }
                        else
                        {
                            int length = maxRange - minRange + 1;
                            xlogfileString = xlogfileString.Substring(minRange, length);
                        }
                    }
                    else
                    {
                        //This error seems to be handled by CloudFlare already
                        errorResult.StatusCode = 400;
                        errorResult.Content = "Range Header bytes value is malformed. Error when splitting at -.";
                        return errorResult;
                    }
                }
                else
                {
                    //This error seems to be handled by CloudFlare already
                    errorResult.StatusCode = 400;
                    errorResult.Content = "Range Header bytes field is malformed. Error when splitting at =.";
                    return errorResult;
                }
            }

            return Content(xlogfileString, "text/plain", Encoding.ASCII);
        }

        [Route("api/games/csv")]
        [HttpGet]
        public async Task<IActionResult> GetCsvAsync()
        {
            return await GetCsvAsync(0);
        }

        [Route("api/games/csv/{lastId}")]
        [HttpGet]
        public async Task<IActionResult> GetCsvAsync(long? lastId)
        {
            var gameLogs = await _dbContext.GameLog.Where(gl => gl.Id > (lastId ?? 0)).ToListAsync();
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(GameLog.GetCsvHeader(true));
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
                    //Sign in succeedeed
                    _dbLogger.LoginSucceeded = true;

                    //Check next whether the user is not banned
                    try
                    {
                        var user = await _userManager.FindByNameAsync(model.UserName);
                        if (user != null)
                        {
                            if (user.IsBanned == true)
                            {
                                int responseCode = 423;
                                string msg = "User " + model.UserName + " is banned.";
                                await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                Response.StatusCode = responseCode; //Server Error
                                return Content(msg);
                            }
                            else if (user.IsGameLogBanned == true)
                            {
                                int responseCode = 423;
                                string msg = "User " + model.UserName + " is not allowed to make GameLog entries.";
                                await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                Response.StatusCode = responseCode; //Server Error
                                return Content(msg);
                            }
                        }
                        else
                        {
                            int responseCode = 500;
                            string msg = "Server error occurred while verifying user: User is null.";
                            await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                            Response.StatusCode = responseCode; //Server Error
                            return Content(msg);
                        }
                    }
                    catch (Exception ex)
                    {
                        int responseCode = 500;
                        string msg = "Server error occurred while verifying user: " + ex.Message;
                        await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                        Response.StatusCode = responseCode; //Server Error
                        return Content(msg);
                    }

                    //OK, now proceed to making the log entry
                    if (!string.IsNullOrEmpty(model.XLogEntry) && model.PlainTextDumpLog != null && model.HtmlDumpLog != null)
                    {
                        _dbLogger.LogSubType = RequestLogSubType.MainFunctionality;

                        //Sign in succeeded
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
                                string msg = "Inserted Id is 0.";
                                await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                Response.StatusCode = responseCode;
                                return Content(msg);
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
                await _dbLogger.LogRequestAsync(message, Data.LogLevel.Error, responseCode);
                Response.StatusCode = responseCode;
                return Content(message);
            }
        }
    }
}
