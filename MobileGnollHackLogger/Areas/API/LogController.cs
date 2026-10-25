using Azure.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Build.ObjectModelRemoting;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using Sentry;
using System.IO.Pipelines;
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
        private readonly string _newLine = "\n"; //Use Unix line endings, the same as what Hardfought.org does
        private readonly GameApiAuthenticator _authenticator;

        public LogController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            IConfiguration configuration, ApplicationDbContext dbContext, GameApiAuthenticator authenticator)
        {
            _authenticator = authenticator;
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
        }

        /// <summary>
        /// Retrieves game log entries in plain text format, supporting HTTP Range requests.
        /// </summary>
        /// <returns>
        /// A task representing the asynchronous operation. The response body is written to as "text/plain" 
        /// and contains newline-separated game log entries in xlog format matching any requested byte ranges.
        /// </returns>
        [Route("xlogfile")]
        [HttpGet]
        public async Task GetAsync()
        {
            await GetAsyncNew();
        }

        private async Task GetAsyncNew()
        {
            long minRange = -1;
            long maxRange = -1;
            Response.ContentType = "text/plain";

            // Range Header Byte Range
            var rangeHeader = Request.Headers.Range;
            var bytesRange = rangeHeader.FirstOrDefault(s => s?.StartsWith("bytes") ?? false);
            if (bytesRange != null)
            {
                var rangeValueSplit = bytesRange.Split('=');
                if (rangeValueSplit != null && rangeValueSplit.Length == 2)
                {
                    var bytesRangeValue = rangeValueSplit[1].Trim();
                    var bytesRangeValueSplit = bytesRangeValue.Split('-');
                    if (bytesRangeValueSplit != null && bytesRangeValueSplit.Length == 2)
                    {
                        if (string.IsNullOrWhiteSpace(bytesRangeValueSplit[0]))
                        {
                            //No min range
                        }
                        else if (!long.TryParse(bytesRangeValueSplit[0].Trim(), out minRange))
                        {
                            //This error seems to be handled by CloudFlare already
                            Response.StatusCode = 400;
                            await WriteErrorStringToResponse("Range Header bytes min value is not a 64-bit integer.");
                            return;
                        }
                        if (string.IsNullOrWhiteSpace(bytesRangeValueSplit[1]))
                        {
                            //No max range
                        }
                        else if (!long.TryParse(bytesRangeValueSplit[1].Trim(), out maxRange))
                        {
                            //This error seems to be handled by CloudFlare already
                            Response.StatusCode = 400;
                            await WriteErrorStringToResponse("Range Header bytes max value is not a 64-bit integer.");
                            return;
                        }
                        if (maxRange > -1 && minRange > -1 && maxRange < minRange)
                        {
                            Response.StatusCode = 416;
                            await WriteErrorStringToResponse("Range Header bytes max value is less than min value.");
                            return;
                        }
                    }
                    else
                    {
                        //This error seems to be handled by CloudFlare already
                        Response.StatusCode = 400;
                        await WriteErrorStringToResponse("Range Header bytes value is malformed. Error when splitting at -.");
                        return;
                    }
                }
                else
                {
                    //This error seems to be handled by CloudFlare already
                    Response.StatusCode = 400;
                    await WriteErrorStringToResponse("Range Header bytes field is malformed. Error when splitting at =.");
                    return;
                }
            }

            IQueryable<GameLog> gameLogs = _dbContext.GameLog;
            IQueryable<GameLog> gameLogs2 = _dbContext.GameLog;

            long currentCharIndex = 0;

            if (minRange > 0)
            {
                var gameLogsWithByteRanges = gameLogs
                    .Where(gl => gl.ByteStart != null && gl.ByteEnd != null);
                var gameLogHit = await gameLogsWithByteRanges
                    .FirstOrDefaultAsync(gl => gl.ByteStart <= minRange && gl.ByteEnd >= minRange);

                if (gameLogHit != null)
                {
                    //GameLog found
                    gameLogs2 = gameLogs.Where(gl => gl.Id >= gameLogHit.Id);
                    currentCharIndex = gameLogHit.ByteStart!.Value;
                }
                else
                {
                    //GameLog not found, search for last with byte ranges
                    var lastGameLogWithByteRanges = await gameLogsWithByteRanges
                        .OrderByDescending(gl => gl.Id)
                        .FirstOrDefaultAsync();

                    if (lastGameLogWithByteRanges != null)
                    {
                        currentCharIndex = lastGameLogWithByteRanges.ByteEnd!.Value + 1;
                        gameLogs2 = gameLogs.Where(gl => gl.Id > lastGameLogWithByteRanges.Id);
                    }
                    else
                    {
                        //No GameLog with byte ranges found, start from the beginning
                        gameLogs2 = gameLogs;
                        currentCharIndex = 0;
                    }
                }
            }

            int updateCount = 0;

            foreach (var gameLog in gameLogs2)
            {
                var xlogLine = gameLog.ToXLogString() + _newLine;
                long charIndexWithLogLine = currentCharIndex + xlogLine.Length;

                if (gameLog.ByteStart == null || gameLog.ByteEnd == null || gameLog.ByteLength == null)
                {
                    gameLog.ByteStart = currentCharIndex;
                    gameLog.ByteEnd = currentCharIndex + xlogLine.Length - 1;
                    gameLog.ByteLength = xlogLine.Length;
                    updateCount++;
                }

                if (minRange > -1 && charIndexWithLogLine <= minRange)
                {
                    //Skip, Min Range greater than

                    currentCharIndex += xlogLine.Length;
                }
                else if (minRange > -1 && minRange > currentCharIndex && minRange < charIndexWithLogLine)
                {
                    //Min Range within current line
                    long subStrMin = minRange - currentCharIndex;

                    if (maxRange > -1 && maxRange < charIndexWithLogLine)
                    {
                        //Min Range and Max Range within current line
                        //We need to include part of the line and drop start and end
                        long subStrLength = maxRange - minRange + 1;
                        string line = xlogLine.Substring((int)subStrMin, (int)subStrLength);
                        currentCharIndex += subStrMin + line.Length;
                        await WriteStringToResponse(line);
                        break;
                    }
                    else
                    {
                        //Only Min Range within current line, Max Range greater
                        //We need to skip first part of the line and include the last part
                        string line = xlogLine.Substring((int)subStrMin);
                        currentCharIndex += subStrMin + line.Length;
                        await WriteStringToResponse(line);
                    }
                }
                else if (maxRange > -1 && maxRange < charIndexWithLogLine)
                {
                    //Min Range has happened before
                    //This time we have only max range inside the line
                    //So, drop the end part of the line
                    long subStrLength = maxRange - currentCharIndex + 1;
                    string line = xlogLine.Substring(0, (int)subStrLength);
                    currentCharIndex += line.Length;
                    await WriteStringToResponse(line);
                    break;
                }
                else
                {
                    currentCharIndex += xlogLine.Length;
                    await WriteStringToResponse(xlogLine);
                }

                if (charIndexWithLogLine - 1 == maxRange)
                {
                    break;
                }
            }

            //if (minRange > -1 && minRange > currentCharIndex)
            //{
            //    Response.StatusCode = 416;
            //    await WriteErrorStringToResponse("Range Header bytes min value is equal to or greater than the file size.");
            //    return;
            //}

            if(updateCount > 0)
            {
                //Update the game log entries with byte ranges
                try
                {
                    await _dbContext.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    //Ignore concurrency exceptions, they are expected
                }
            }

            await Response.CompleteAsync();
        }

        /// <summary>
        /// Retrieves a single game log entry by its database identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the game log entry.</param>
        /// <returns>
        /// A task representing the asynchronous operation. The response body is written to as "text/plain" 
        /// and contains the single game log entry in xlog format followed by a newline.
        /// </returns>
        [Route("xlogfile/{id}")]
        [HttpGet]
        public async Task GetAsync(long id)
        {
            Response.ContentType = "text/plain";

            IQueryable<GameLog> gameLogs = _dbContext.GameLog.Where(gl => gl.Id == id);

            foreach (var gameLog in gameLogs)
            {
                var xlogLine = gameLog.ToXLogString() + _newLine;
                await WriteStringToResponse(xlogLine);
            }

            await Response.CompleteAsync();
        }

        /// <summary>
        /// Retrieves game log entries with a database identifier greater than or equal to the specified minimum identifier.
        /// </summary>
        /// <param name="minId">The minimum game log database identifier.</param>
        /// <returns>
        /// A task representing the asynchronous operation. The response body is written to as "text/plain" 
        /// and contains newline-separated game log entries in xlog format starting from the specified minimum identifier.
        /// </returns>
        [Route("xlogfile/min/{minId}")]
        [HttpGet]
        public async Task GetAsyncMin(long minId)
        {
            Response.ContentType = "text/plain";

            IQueryable<GameLog> gameLogs = _dbContext.GameLog.Where(gl => gl.Id >= minId);

            foreach (var gameLog in gameLogs)
            {
                var xlogLine = gameLog.ToXLogString() + _newLine;
                await WriteStringToResponse(xlogLine);
            }

            await Response.CompleteAsync();
        }

        /// <summary>
        /// Retrieves game log entries with a database identifier less than or equal to the specified maximum identifier.
        /// </summary>
        /// <param name="maxId">The maximum game log database identifier.</param>
        /// <returns>
        /// A task representing the asynchronous operation. The response body is written to as "text/plain" 
        /// and contains newline-separated game log entries in xlog format up to the specified maximum identifier.
        /// </returns>
        [Route("xlogfile/max/{maxId}")]
        [HttpGet]
        public async Task GetAsyncMax(long maxId)
        {
            Response.ContentType = "text/plain";

            IQueryable<GameLog> gameLogs = _dbContext.GameLog.Where(gl => gl.Id <= maxId);

            foreach (var gameLog in gameLogs)
            {
                var xlogLine = gameLog.ToXLogString() + _newLine;
                await WriteStringToResponse(xlogLine);
            }

            await Response.CompleteAsync();
        }

        /// <summary>
        /// Retrieves game log entries within a range defined by minimum and maximum database identifiers.
        /// </summary>
        /// <param name="minId">The minimum game log database identifier.</param>
        /// <param name="maxId">The maximum game log database identifier.</param>
        /// <returns>
        /// A task representing the asynchronous operation. The response body is written to as "text/plain" 
        /// and contains newline-separated game log entries in xlog format within the specified identifier range.
        /// </returns>
        [Route("xlogfile/minmax/{minId}/{maxId}")]
        [HttpGet]
        public async Task GetAsyncMinMax(long minId, long maxId)
        {
            Response.ContentType = "text/plain";

            IQueryable<GameLog> gameLogs = _dbContext.GameLog.Where(gl => gl.Id >= minId && gl.Id <= maxId);

            foreach (var gameLog in gameLogs)
            {
                var xlogLine = gameLog.ToXLogString() + _newLine;
                await WriteStringToResponse(xlogLine);
            }

            await Response.CompleteAsync();
        }

        private async ValueTask<FlushResult> WriteStringToResponse(string s)
        {
            var readOnlyMem = new ReadOnlyMemory<byte>(Encoding.ASCII.GetBytes(s));
            var res = await Response.BodyWriter.WriteAsync(readOnlyMem);
            return res;
        }

        private async Task WriteErrorStringToResponse(string s)
        {
            await WriteStringToResponse(s);
            await Response.CompleteAsync();
        }

        //[Route("api/games/csv")]
        //[HttpGet]
        //public async Task<IActionResult> GetCsvAsync()
        //{
        //    return await GetCsvAsync(0);
        //}

        //[Route("api/games/csv/{lastId}")]
        //[HttpGet]
        //public async Task<IActionResult> GetCsvAsync(long? lastId)
        //{
        //    var gameLogs = await _dbContext.GameLog.Where(gl => gl.Id > (lastId ?? 0)).ToListAsync();
        //    StringBuilder sb = new StringBuilder();
        //    sb.AppendLine(GameLog.GetCsvHeader(true));
        //    foreach (var gameLog in gameLogs)
        //    {
        //        sb.AppendLine(gameLog.ToCsvString());
        //    }
        //    return Content(sb.ToString(), "text/plain", Encoding.UTF8);
        //}

        /// <summary>
        /// Submits a new game log entry along with character dump logs, or verifies connection and credentials.
        /// </summary>
        /// <param name="model">The model containing the authentication data and log payloads.</param>
        /// <returns>
        /// An <see cref="IActionResult"/> indicating the result of the operation.
        /// On success for a connection test, returns an empty 200 OK.
        /// On success for a new log entry, writes a serialized <see cref="LogPostResponseInfo"/> JSON object to the response body as "text/plain".
        /// On failure, returns a non-200 status code, potentially containing a plain text error message in the response body.
        /// </returns>
        [Route("xlogfile")]
        [HttpPost]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(GameApiRateLimits.GameApiFlood)]
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

                var auth = await _authenticator.AuthenticateAsync(HttpContext, _dbLogger, model.UserName, model.Password, model.AntiForgeryToken, "xlogfile");
                if (!auth.Succeeded)
                {
                    return auth.Response!;
                }

                {
                    //Sign in succeedeed

                    //Check next whether the user is not banned
                    string userId;
                    try
                    {
                        var user = await _userManager.FindByNameAsync(model.UserName);
                        if (user != null)
                        {
                            userId = user.Id;
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
                        SentrySdk.CaptureException(ex);
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

                        //A game already stored for this account with the same starttimeUTC is a repeat when its points and turns match
                        var storedGameLogs = await _dbContext.GameLog
                            .AsNoTracking()
                            .Where(gl => gl.AspNetUserId == userId && gl.StartTimeUTC == xLogFileLine.StartTimeUTC)
                            .OrderBy(gl => gl.Id)
                            .ToListAsync();

                        if (storedGameLogs.Count > 0)
                        {
                            var storedGameLog = storedGameLogs.FirstOrDefault(gl => gl.Points == xLogFileLine.Points && gl.Turns == xLogFileLine.Turns);
                            if (storedGameLog != null)
                            {
                                await _dbLogger.LogRequestAsync("GameLog already stored as ID " + storedGameLog.Id + "; returning the stored entry.",
                                    Data.LogLevel.Info, 200);
                                return await BuildPostResponseAsync(storedGameLog);
                            }

                            int responseCode = 409;
                            await _dbLogger.LogRequestAsync("Character already exists. GameLog ID " + storedGameLogs[0].Id
                                + " has the same starttimeUTC with different points or turns.", Data.LogLevel.Warning, responseCode);
                            return StatusCode(responseCode); //Character already exists
                        }

                        // Write Dumplog Files; files left without a row by an earlier failed attempt are replaced
                        string dir = Path.Combine(_dumplogBasePath, xLogFileLine.Name);
                        if (!System.IO.Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        string filePathWithoutExtension = dir + @"\" + "gnollhack." + xLogFileLine.Name + "." + xLogFileLine.StartTimeUTC;
                        string plainTextDumpLogPath = filePathWithoutExtension + ".txt";
                        string htmlDumpLogPath = filePathWithoutExtension + ".html";

                        var t1 = WriteDumplogAsync(model.PlainTextDumpLog, plainTextDumpLogPath);
                        var t2 = WriteDumplogAsync(model.HtmlDumpLog, htmlDumpLogPath);

                        await Task.WhenAll(t1, t2);

                        GameLog? gameLog = null;
                        try
                        {
                            gameLog = new GameLog(xLogFileLine, _dbContext);
                            gameLog.CreatedDate = DateTime.Now;
                            await _dbContext.GameLog.AddAsync(gameLog);
                            await _dbContext.SaveChangesAsync();
                        }
                        catch(InvalidOperationException invEx)
                        {
                            DetachGameLog(gameLog);
                            await DeleteDumplogFilesAsync(410, plainTextDumpLogPath, htmlDumpLogPath);
                            int responseCode = 410;
                            await _dbLogger.LogRequestAsync("GameLog insertion to database failed. Message: " + invEx.Message, Data.LogLevel.Error, responseCode);
                            return StatusCode(responseCode); //Gone
                        }
                        catch(Exception ex)
                        {
                            SentrySdk.CaptureException(ex);
                            DetachGameLog(gameLog);
                            await DeleteDumplogFilesAsync(500, plainTextDumpLogPath, htmlDumpLogPath);
                            int responseCode = 500;
                            await _dbLogger.LogRequestAsync("GameLog insertion to database failed. Message: " + ex.Message, Data.LogLevel.Error, responseCode);
                            Response.StatusCode = responseCode; //Server Error
                            return Content(ex.Message.ToString());
                        }

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

                        return await BuildPostResponseAsync(gameLog);
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

                        string logMsg = string.IsNullOrEmpty(model.XLogEntry) ? "XLogEntry is empty." : "";
                        logMsg += (!string.IsNullOrEmpty(logMsg) && model.PlainTextDumpLog == null ? " " : "") + (model.PlainTextDumpLog == null ? "PlainTextDumpLog is missing." : "");
                        logMsg += (!string.IsNullOrEmpty(logMsg) && model.HtmlDumpLog == null ? " " : "") + (model.HtmlDumpLog == null ? "HtmlDumpLog is missing." : "");

                        await _dbLogger.LogRequestAsync("Login succeeded but there is missing data: " + logMsg, Data.LogLevel.Error, responseCode);
                        return StatusCode(responseCode); //Bad Request
                    }
                }
            }
            catch (Exception ex) 
            {
                SentrySdk.CaptureException(ex);
                int responseCode = 500;
                string message = (ex.InnerException ?? ex).GetType().FullName + ", Message: " + ex.Message;
                await _dbLogger.LogRequestAsync(message, Data.LogLevel.Error, responseCode);
                Response.StatusCode = responseCode;
                return Content(message);
            }
        }

        /// <summary>
        /// Builds the 200 response of <see cref="Post"/> for a stored game log row. The top score indexes are 0 when the
        /// row's mode is not a known mode or the top score lookup fails.
        /// </summary>
        private async Task<IActionResult> BuildPostResponseAsync(GameLog gameLog)
        {
            long topScoreIndex = 0;
            long topScoreDisplayIndex = 0;

            if (gameLog.Mode != null && GnollHackHelper.Modes.ContainsKey(gameLog.Mode))
            {
                try
                {
                    var topScoreNumberData = await _dbContext.GetTopScoreNumberAsync(gameLog.Id, gameLog.Mode);
                    topScoreIndex = topScoreNumberData.Index;
                    topScoreDisplayIndex = topScoreNumberData.DisplayIndex;
                }
                catch (Exception ex)
                {
                    SentrySdk.CaptureException(ex);
                    await _dbLogger.LogRequestAsync("Top score lookup failed for GameLog ID " + gameLog.Id + ". Message: " + ex.Message,
                        Data.LogLevel.Warning, 200);
                }
            }

            var resposeInfo = new LogPostResponseInfo()
            {
                DatabaseRowId = gameLog.Id,
                TopScoreDisplayIndex = topScoreDisplayIndex,
                TopScoreIndex = topScoreIndex,
                TopScorePageUrl = GetTopScorePageUrl(gameLog.Mode)
            };

            var responseText = System.Text.Json.JsonSerializer.Serialize(resposeInfo);

            await _dbLogger.LogRequestAsync("ResponseText: " + responseText, Data.LogLevel.Info, 200);

            return Content(responseText, "text/plain", Encoding.UTF8); //OK
        }

        /// <summary>
        /// Writes an uploaded dumplog to the path, replacing any existing file there.
        /// </summary>
        private static async Task WriteDumplogAsync(IFormFile dumplog, string path)
        {
            await using var outStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            await dumplog.CopyToAsync(outStream);
        }

        /// <summary>
        /// Stops tracking a game log whose insert failed, so later saves of the request log do not retry it.
        /// </summary>
        private void DetachGameLog(GameLog? gameLog)
        {
            if (gameLog != null)
            {
                _dbContext.Entry(gameLog).State = EntityState.Detached;
            }
        }

        /// <summary>
        /// Deletes the dumplog files written by this request, best effort; a failure is logged, not thrown.
        /// </summary>
        private async Task DeleteDumplogFilesAsync(int responseCode, params string[] paths)
        {
            foreach (var path in paths)
            {
                try
                {
                    System.IO.File.Delete(path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    await _dbLogger.LogRequestAsync("Dumplog file " + path + " could not be deleted. Message: " + ex.Message,
                        Data.LogLevel.Warning, responseCode);
                }
            }
        }

        private string GetTopScorePageUrl(string? mode = null, string? death = null)
        {
            var url = $"{Request.Scheme}://{Request.Host}/TopScores";
            int count = 0;
            if(!string.IsNullOrEmpty(mode) && GnollHackHelper.Modes.ContainsKey(mode))
            {
                url += "?mode=" + mode;
                count++;
            }
            if (death == "ascended")
            {
                url += (count == 0 ? "?" : "&") + "death=" + death;
            }
            return url;
        }

    }
}
