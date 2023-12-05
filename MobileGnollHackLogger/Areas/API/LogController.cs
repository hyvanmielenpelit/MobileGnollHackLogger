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
        private string _dumplogBasePath = "";

        public LogController(SignInManager<IdentityUser> signInManager, ILogger<LogModel> logger, IWebHostEnvironment environment, 
            IConfiguration configuration)
        {
            _signInManager = signInManager;
            _logger = logger;
            _environment = environment;
            _logFilePath = _environment.WebRootPath + @"\logs\xlogfile";
            _dumplogBasePath = _environment.WebRootPath + @"\dumplogs";
            _configuration = configuration;
        }

        // GET: api/<LogController>
        [HttpGet]
        public IActionResult Get()
        {
            if(!System.IO.File.Exists(_logFilePath)) 
            {
                return Ok();
            }
            try
            {
                var text = System.IO.File.ReadAllText(_logFilePath, Encoding.UTF8);
                var text2 = text.Replace("\r", ""); //Ensure Linux line endings
                var bytes = Encoding.UTF8.GetBytes(text2);
                return File(bytes, "text/plain", "xlogfile");
            }
            catch(Exception ex) 
            { 
                return StatusCode(500, ex?.Message ?? "");
            }
        }

        // POST api/<LogController>
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

                    //Do checks on xLogFileLine here
                    //TODO

                    string line = model.XLogEntry + "\n";
                    await System.IO.File.AppendAllTextAsync(_logFilePath, line);

                    _logger.LogInformation("User logged in.");
                    return StatusCode(200); //OK
                }
                if (result.RequiresTwoFactor)
                {
                    return StatusCode(500);
                }
                if (result.IsLockedOut)
                {
                    return StatusCode(403);
                }
                else
                {
                    return StatusCode(400);
                }
            }
            catch (Exception ex) 
            {
                Response.StatusCode = 500;
                return Content(ex.ToString() + ", Message: ", ex.Message);
            }
        }
    }
}
