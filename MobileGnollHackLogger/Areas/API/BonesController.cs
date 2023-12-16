using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MobileGnollHackLogger.Data;
using System.Text;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MobileGnollHackLogger.Areas.API
{
    [ApiController]
    public class BonesController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LogModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly string _bonesBasePath = "";

        public BonesController(SignInManager<ApplicationUser> signInManager, ILogger<LogModel> logger,
            IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _logger = logger;
            _configuration = configuration;
            _dbContext = dbContext;

            _bonesBasePath = _configuration["BonesPath"] ?? "";

            if (string.IsNullOrEmpty(_bonesBasePath))
            {
                throw new Exception("BonesPath is null");
            }
        }

        // GET: api/<BonesController>
        [Route("bones")]
        [HttpGet]
        public IActionResult Get()
        {
            return Get(0);
        }

        // GET api/<BonesController>/5
        [Route("bones/{id}")]
        [HttpGet("{id}")]
        public IActionResult Get(int id)
        {
            var bones = _dbContext.Bones.Where(gl => gl.Id == id);
            StringBuilder sb = new StringBuilder();
            foreach (var bone in bones)
            {
                sb.AppendLine(bone.ToString());
            }
            return Content(sb.ToString(), "text/plain", Encoding.ASCII);
        }

        [Route("bones")]
        [HttpPost]
        public async Task<IActionResult> Post([FromForm] BonesModel model)
        {
            try
            {
                if (model == null)
                {
                    return StatusCode(400); //Bad Request
                }
                if (model.UserName == null)
                {
                    return StatusCode(400); //Bad Request
                }
                if (model.Password == null)
                {
                    return StatusCode(400); //Bad Request
                }
                if (model.AntiForgeryToken == null)
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
                    if (!string.IsNullOrEmpty(model.Data) && model.BonesFile != null)
                    {
                        //Sign in succeedeed
                        XLogFileLine xLogFileLine = new XLogFileLine(model.Data);

                        //Change user name to the account name
                        xLogFileLine.Name = model.UserName;

                        // Write Bones Files
                        string dir = Path.Combine(_bonesBasePath, xLogFileLine.Name);
                        if (!System.IO.Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        string filePathWithoutExtension = dir + @"\" + "gnollhack." + xLogFileLine.Name + "." + xLogFileLine.StartTimeUTC;
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

                        using var htmlOutStream = System.IO.File.OpenWrite(htmlDumpLogPath);
                        var t2 = model.BonesFile.CopyToAsync(htmlOutStream);

                        Task.WaitAll(t2);

                        _logger.LogInformation("Bones files written for " + xLogFileLine.Name + " at " + dir);

                        try
                        {
                            GameLog gameLog = new GameLog(xLogFileLine, _dbContext);
                            await _dbContext.GameLog.AddAsync(gameLog);
                            await _dbContext.SaveChangesAsync();
                            long id = gameLog.Id;
                            if (id == 0)
                            {
                                Response.StatusCode = 500;
                                return Content("Inserted Id is 0.");
                            }
                            return Content(id.ToString(), "text/plain", Encoding.UTF8); //OK
                        }
                        catch (InvalidOperationException)
                        {
                            return StatusCode(410); //Gone
                        }
                        catch (Exception ex)
                        {
                            Response.StatusCode = 500; //Server Error
                            return Content(ex.Message.ToString());
                        }
                    }
                    else if (string.IsNullOrEmpty(model.Data) && model.BonesFile == null)
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
