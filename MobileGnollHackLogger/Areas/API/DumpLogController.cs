using Microsoft.AspNetCore.Mvc;
using MobileGnollHackLogger.Data;
using System.Text;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MobileGnollHackLogger.Areas.API
{
    [Route("dumplog")]
    [ApiController]
    public class DumpLogController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly string _dumplogBasePath = "";

        public DumpLogController(IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _configuration = configuration;
            _dbContext = dbContext;
            _dumplogBasePath = _configuration["DumpLogPath"] ?? "";

            if (string.IsNullOrEmpty(_dumplogBasePath))
            {
                throw new Exception("DumpLogPath is null");
            }
        }

        // GET api/<DumpLogController>/5
        [HttpGet("{id:int}")]
        public IActionResult Get(int id)
        {
            return Get(id, "html");
        }

        [HttpGet("{id:int}/{type}")]
        public IActionResult Get(int id, string type)
        {
            var gameLog = _dbContext.GameLog.FirstOrDefault(gl => gl.Id == id);
            
            return GetGameLog(gameLog, type);
        }

        [HttpGet("byname/{name}/{starttimeUTC}")]
        public IActionResult Get(string name, long starttimeUTC)
        {
            return Get(name, starttimeUTC, "html");
        }

        [HttpGet("byname/{name}/{starttimeUTC}/{type}")]
        public IActionResult Get(string name, long starttimeUTC, string type = "html")
        {
            if(string.IsNullOrEmpty(name))
            {
                return StatusCode(400); //Bad Request
            }

            if (starttimeUTC <= 0)
            {
                return StatusCode(400); //Bad Request
            }

            var gameLog = _dbContext.GameLog.FirstOrDefault(gl => gl.Name == name && gl.StartTimeUTC == starttimeUTC);

            return GetGameLog(gameLog, type);
        }

        private IActionResult GetGameLog(GameLog? gameLog, string type = "html")
        {
            if (gameLog == null)
            {
                return StatusCode(404); //Not Found
            }

            if (gameLog.Name == null)
            {
                return StatusCode(500); //Server Error
            }

            string ext = "";
            string mimeType = "";
            Encoding encoding = Encoding.UTF8;

            switch (type)
            {
                case "html":
                    ext = "html";
                    mimeType = "text/html";
                    encoding = Encoding.UTF8;
                    break;
                case "plain":
                    ext = "txt";
                    mimeType = "text/plain";
                    encoding = Encoding.UTF8;
                    break;
                default:
                    ext = "html";
                    mimeType = "text/html";
                    encoding = Encoding.UTF8;
                    break;
            }

            string dumpLogFileName = $"gnollhack.{gameLog.Name}.{gameLog.StartTimeUTC}.{ext}";
            string dumpLogPath = Path.Combine(_dumplogBasePath, gameLog.Name, dumpLogFileName);

            if (!System.IO.File.Exists(dumpLogPath))
            {
                return StatusCode(410); //Gone
            }

            var contents = System.IO.File.ReadAllText(dumpLogPath);

            return Content(contents, mimeType, encoding);
        }
    }
}
