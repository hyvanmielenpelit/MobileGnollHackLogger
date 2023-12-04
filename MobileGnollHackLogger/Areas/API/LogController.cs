using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
        private string _logFilePath = "xlogfile";

        public LogController(SignInManager<IdentityUser> signInManager, ILogger<LogModel> logger)
        {
            _signInManager = signInManager;
            _logger = logger;
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
        public async void Post([FromForm] LogModel model)
        {
            //CancellationToken ct = new CancellationToken();
            //var formCollection = await Request.ReadFormAsync(ct);
            //foreach (var formField in formCollection)
            //{
            //    var key = formField.Key;
            //    var value = formField.Value;

            //}

            //if (model?.PlainTextDumpLog == null || model?.PlainTextDumpLog.Length <= 0)
            //{
            //   return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            //}

            //if (model?.HtmlDumpLog == null || model?.HtmlDumpLog.Length <= 0)
            //{
            //    return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            //}
            //var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, false, lockoutOnFailure: false);

            //var response = new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
            //return response;
        }
    }
}
