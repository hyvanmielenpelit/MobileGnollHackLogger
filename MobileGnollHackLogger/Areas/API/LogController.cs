using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
        private IWebHostEnvironment _environment;
        private IConfiguration _configuration;
        private ApplicationDbContext _dbContext;

        private string _dumplogBasePath = "";

        public LogController(SignInManager<ApplicationUser> signInManager, ILogger<LogModel> logger, IWebHostEnvironment environment, 
            IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _logger = logger;
            _environment = environment;
            _dumplogBasePath = _environment.WebRootPath + @"\dumplogs";
            _configuration = configuration;
            _dbContext = dbContext;
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
                sb.AppendLine(gameLog.ToString());
            }
            return Content(sb.ToString(), "text/plain", Encoding.ASCII);
        }

        [Route("xlogfile")]
        [HttpPost]
        public async Task<IActionResult> Post([FromForm] LogModel model)
        {
            try
            {
                if (model == null)
                {
                    return StatusCode(400); //Bad Request
                }
                if(model.UserName == null)
                {
                    return StatusCode(400); //Bad Request
                }
                if (model.Password == null)
                {
                    return StatusCode(400); //Bad Request
                }
                if(model.AntiForgeryToken == null)
                {
                    return StatusCode(400); //Bad Request
                }

                var antiForgeryToken = _configuration["AntiForgeryToken"];
                if (antiForgeryToken != model.AntiForgeryToken)
                {
                    return StatusCode(401); //Not Authorized
                }
                
                var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, false, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    if (!string.IsNullOrEmpty(model.XLogEntry) && model.PlainTextDumpLog != null && model.HtmlDumpLog != null)
                    {
                        //Sign in succeedeed
                        XLogFileLine xLogFileLine = new XLogFileLine(model.XLogEntry);

                        //Change user name to the account name
                        xLogFileLine.Name = model.UserName;

                        // Write Dumplog Files
                        string dir = _dumplogBasePath + @"\" + xLogFileLine.Name;
                        if (!System.IO.Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        string filePathWithoutExtension = dir + @"\" + "gnollhack." + xLogFileLine.Name + "." + xLogFileLine.StartTime;
                        string plainTextDumpLogPath = filePathWithoutExtension + ".txt";
                        string htmlDumpLogPath = filePathWithoutExtension + ".html";

                        if (System.IO.File.Exists(plainTextDumpLogPath))
                        {
                            return StatusCode(409); //Character already exists
                        }

                        if (System.IO.File.Exists(htmlDumpLogPath))
                        {
                            return StatusCode(409); //Character already exists
                        }

                        using var plainTextOutStream = System.IO.File.OpenWrite(plainTextDumpLogPath);
                        var t1 = model.PlainTextDumpLog.CopyToAsync(plainTextOutStream);

                        using var htmlOutStream = System.IO.File.OpenWrite(htmlDumpLogPath);
                        var t2 = model.HtmlDumpLog.CopyToAsync(htmlOutStream);

                        Task.WaitAll(t1, t2);

                        _logger.LogInformation("Dumplog files written for " + xLogFileLine.Name + " at " + dir);

                        try
                        {
                            GameLog gameLog = new GameLog(xLogFileLine, _dbContext);
                            await _dbContext.GameLog.AddAsync(gameLog);
                            await _dbContext.SaveChangesAsync();
                            return StatusCode(200); //OK
                        }
                        catch(InvalidOperationException)
                        {
                            return StatusCode(410); //Gone
                        }
                        catch(Exception ex)
                        {
                            Response.StatusCode = 500; //Server Error
                            return Content(ex.Message.ToString());
                        }
                    }
                    else if(string.IsNullOrEmpty(model.XLogEntry) && model.PlainTextDumpLog == null && model.HtmlDumpLog == null)
                    {
                        //Test Connection
                        return Ok();
                    }
                    else
                    {
                        return StatusCode(400); //Bad Request
                    }
                }
                if (result.RequiresTwoFactor)
                {
                    return StatusCode(412);
                }
                if (result.IsLockedOut)
                {
                    return StatusCode(423);
                }
                else
                {
                    return StatusCode(403);
                }
            }
            catch (Exception ex) 
            {
                Response.StatusCode = 500;
                return Content((ex.InnerException ?? ex).GetType().FullName + ", Message: " + ex.Message);
            }
        }
    }
}
