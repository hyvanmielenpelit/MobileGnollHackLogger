using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MobileGnollHackLogger.Data;
using System.Net;
using System.Text;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MobileGnollHackLogger.Areas.API
{
    [Route("xlogfile")]
    [ApiController]
    public class LogController : ControllerBase
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<LogModel> _logger;
        private IWebHostEnvironment _environment;
        private IConfiguration _configuration;
        private string _logFilePath = "";
        private string _logFileDir = "";
        private string _dumplogBasePath = "";

        public LogController(SignInManager<IdentityUser> signInManager, ILogger<LogModel> logger, IWebHostEnvironment environment, 
            IConfiguration configuration)
        {
            _signInManager = signInManager;
            _logger = logger;
            _environment = environment;
            _logFileDir = _environment.WebRootPath + @"\logs";
            _logFilePath = _logFileDir + @"\xlogfile";
            _dumplogBasePath = _environment.WebRootPath + @"\dumplogs";
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Get()
        {
            if(!System.IO.File.Exists(_logFilePath)) 
            {
                return Ok();
            }
            try
            {
                var text = System.IO.File.ReadAllText(_logFilePath, Encoding.ASCII);
                var text2 = text.Replace("\r", ""); //Ensure Linux line endings
                //var bytes = Encoding.UTF8.GetBytes(text2);
                //return File(bytes, "text/plain", "xlogfile",);
                return Content(text2, "text/plain", Encoding.ASCII);
            }
            catch(Exception ex) 
            { 
                return StatusCode(500, ex?.Message ?? "");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromForm] LogModel model)
        {
            try
            {
                if (model == null)
                { 
                    throw new ArgumentNullException(nameof(model));
                }
                if(model.UserName == null)
                {
                    throw new ArgumentNullException(nameof(model.UserName));
                }
                if (model.Password == null)
                {
                    throw new ArgumentNullException(nameof(model.Password));
                }
                if(model.AntiForgeryToken == null)
                {
                    throw new ArgumentNullException(nameof(model.AntiForgeryToken));
                }
                if (model.XLogEntry == null)
                {
                    throw new ArgumentNullException(nameof(model.XLogEntry));
                }
                if (model.PlainTextDumpLog == null)
                {
                    throw new ArgumentNullException(nameof(model.PlainTextDumpLog));
                }
                if (model.HtmlDumpLog == null)
                {
                    throw new ArgumentNullException(nameof(model.HtmlDumpLog));
                }

                var antiForgeryToken = _configuration["AntiForgeryToken"];
                if (antiForgeryToken != model.AntiForgeryToken)
                {
                    return StatusCode(401);
                }
                
                var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, false, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    //Sign in succeedeed
                    XLogFileLine xLogFileLine = new XLogFileLine(model.XLogEntry);

                    //Change user name to the account name
                    xLogFileLine.Name = model.UserName;

                    // Write Dumplog Files
                    string dir = _dumplogBasePath + @"\" + xLogFileLine.Name;
                    if(!System.IO.Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    string filePathWithoutExtension = dir + @"\" + "gnollhack." + xLogFileLine.Name + "." + xLogFileLine.StartTime;
                    string plainTextDumpLogPath = filePathWithoutExtension + ".txt";
                    string htmlDumpLogPath = filePathWithoutExtension + ".html";

                    if(System.IO.File.Exists(plainTextDumpLogPath))
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

                    //Write xlogfile entry
                    string line = xLogFileLine.ToString() + "\n";

                    if (!System.IO.Directory.Exists(_logFileDir))
                    {
                        System.IO.Directory.CreateDirectory(_logFileDir);
                    }

                    await System.IO.File.AppendAllTextAsync(_logFilePath, line);

                    _logger.LogInformation("xlogfile entry written for " + xLogFileLine.Name);

                    return StatusCode(200); //OK
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
