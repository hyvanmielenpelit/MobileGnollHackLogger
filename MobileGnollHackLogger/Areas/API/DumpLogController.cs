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

        [HttpGet("byname/{name}/{starttime}")]
        public IActionResult Get(string name, long starttime)
        {
            return Get(name, starttime, "html");
        }

        [HttpGet("byname/{name}/{starttime}/{type}")]
        public IActionResult Get(string name, long starttime, string type = "html")
        {
            if(string.IsNullOrEmpty(name))
            {
                return StatusCode(400); //Bad Request
            }

            if (starttime <= 0)
            {
                return StatusCode(400); //Bad Request
            }

            var gameLog = _dbContext.GameLog.FirstOrDefault(gl => gl.Name == name && gl.StartTime == starttime);

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
            string dumpLogDirectory = Path.Combine(_dumplogBasePath, gameLog.Name);
            string dumpLogPath = Path.Combine(dumpLogDirectory, dumpLogFileName);

            // Only files in a player directory strictly inside the dumplog store are served
            if (!BonesFileNames.IsInsideDirectory(dumpLogDirectory, _dumplogBasePath)
                || !BonesFileNames.IsInsideDirectory(dumpLogPath, dumpLogDirectory))
            {
                return StatusCode(404); //Not Found
            }

            if (!System.IO.File.Exists(dumpLogPath))
            {
                return StatusCode(410); //Gone
            }

            var contents = System.IO.File.ReadAllText(dumpLogPath);

            // A dumplog is player-supplied content: it runs sandboxed, in no frame, as the declared type only
            Response.Headers["Content-Security-Policy"] = "sandbox; frame-ancestors 'none'";
            Response.Headers["X-Content-Type-Options"] = "nosniff";

            return Content(contents, mimeType, encoding);
        }
    }
}
