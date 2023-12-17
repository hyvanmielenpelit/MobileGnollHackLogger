using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
                    if (!string.IsNullOrEmpty(model.Command) && !string.IsNullOrEmpty(model.Data))
                    {
                        //Sign in succeedeed
                        if(model.Command == "SendBonesFile")
                        {
                            if (model.BonesFile == null)
                            {
                                Response.StatusCode = 500;
                                return Content("Bones file is null when sending a bones file.");
                            }

                            // Write Bones Files
                            string dir = Path.Combine(_bonesBasePath, model.UserName);
                            if (!System.IO.Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            string baseFilePath = dir + @"\" + model.BonesFile.FileName;
                            string fullFilePath;
                            int i = 0;
                            do
                            {
                                fullFilePath = baseFilePath + "_" + i;
                                i++;
                            } while (System.IO.File.Exists(fullFilePath));

                            using var bonesOutStream = System.IO.File.OpenWrite(fullFilePath);
                            await model.BonesFile.CopyToAsync(bonesOutStream);

                            _logger.LogInformation("Bones files written for " + model.BonesFile.FileName + " at " + dir);

                            try
                            {
                                //Difficulty is in the data field of the SendBonesFile command
                                int difficulty = 0;
                                if(!string.IsNullOrEmpty(model.Data))
                                    int.TryParse(model.Data, out difficulty);

                                Bones bone = new Bones(model.UserName, 
                                    model.Platform == null ? "Unknown" : model.Platform,
                                    model.PlatformVersion == null ? "" : model.PlatformVersion,
                                    model.Port == null ? "" : model.Port,
                                    model.PortVersion == null ? "" : model.PortVersion,
                                    model.PortBuild == null ? "" : model.PortBuild,
                                    model.VersionNumber, 
                                    model.VersionCompatibilityNumber,
                                    difficulty, 
                                    fullFilePath, 
                                    model.BonesFile.FileName, 
                                    _dbContext);

                                await _dbContext.Bones.AddAsync(bone);
                                await _dbContext.SaveChangesAsync();
                                long id = bone.Id;
                                if (id == 0)
                                {
                                    Response.StatusCode = 500;
                                    return Content("Inserted Id is 0.");
                                }

                                /* Return a bones file from the existing bones, if possible */
                                var availableBones = _dbContext.Bones.Where(
                                    b => b.AspNetUserId != bone.AspNetUserId 
                                    && b.DifficultyLevel == bone.DifficultyLevel 
                                    && (b.VersionNumber == bone.VersionNumber 
                                        || (b.VersionNumber < bone.VersionNumber 
                                            ? (b.VersionNumber >= bone.VersionCompatibilityNumber) 
                                            : (b.VersionCompatibilityNumber <= bone.VersionNumber))));

                                var list = availableBones.ToList();
                                if (list != null)
                                {
                                    if(list.Count < 5)
                                        return Content(id.ToString() + ", too few bones files on server: " + list.Count + " applicable bones file" + (list.Count == 1 ? "" : "s") + " on server", "text/plain", Encoding.UTF8); //OK

                                    if (list.Count < 200)
                                    {
                                        Random random1 = new Random();
                                        double chance = 1.0 / 3.0 + 2.0 / 3.0 * ((double)(list.Count - 5) / 195);
                                        if (!(random1.NextDouble() < chance))
                                            return Content(id.ToString() + ", randomly did not send a bones file: " + list.Count + " applicable bones file" + (list.Count == 1 ? "" : "s") + " on server", "text/plain", Encoding.UTF8); //OK
                                    }

                                    /* Send a bones file */
                                    if (list.Count > 0)
                                    {
                                        string? bonespath = null;
                                        Random random = new Random();
                                        int indx = list.Count == 1 ? 0 : random.Next(list.Count);
                                        bonespath = list[indx].BonesFilePath;
                                        if (list.Count > 1 && (bonespath == null || !System.IO.File.Exists(bonespath)))
                                        {
                                            for (i = 0; i < list.Count; i++)
                                            {
                                                bonespath = list[i].BonesFilePath;
                                                if (bonespath != null && System.IO.File.Exists(bonespath))
                                                {
                                                    indx = i;
                                                    break;
                                                }
                                            }
                                        }
                                        if (bonespath != null && System.IO.File.Exists(bonespath))
                                        {
                                            string? originalfilename = list[indx].OriginalFileName != null ? list[indx].OriginalFileName : "";
                                            try
                                            {
                                                byte[] bytes = await System.IO.File.ReadAllBytesAsync(bonespath);
                                                if (bytes != null && bytes.Length > 0) 
                                                {
                                                    Response?.Headers?.TryAdd("X-GH-OriginalFileName", new Microsoft.Extensions.Primitives.StringValues(originalfilename));
                                                    Response?.Headers?.TryAdd("X-GH-BonesFilePath", new Microsoft.Extensions.Primitives.StringValues(bonespath));
                                                    return File(bytes, "application/octet-stream", originalfilename);
                                                }
                                                else
                                                    return Content(id.ToString() + ", read zero bytes", "text/plain", Encoding.UTF8); //OK
                                            }
                                            catch (Exception ex)
                                            {
                                                return Content(id.ToString() + ", reading all bytes failed: " + ex.Message, "text/plain", Encoding.UTF8); //OK
                                            }
                                        }
                                        else
                                            return Content(id.ToString() + ", " + (bonespath == null ? "bones file path is null" : "bones file " + bonespath + " does not exist"), "text/plain", Encoding.UTF8); //OK
                                    }
                                    else
                                        return Content(id.ToString() + ", couldn't locate a bones file", "text/plain", Encoding.UTF8); //OK
                                }
                                else
                                    return Content(id.ToString() + ", bones list is null", "text/plain", Encoding.UTF8); //OK
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
                        else if(model.Command == "ConfirmReceipt")
                        {
                            try
                            {
                                var availableBones = _dbContext.Bones.Where(b => b.BonesFilePath == model.Data);
                                var list = availableBones.ToList();
                                if(list != null && list.Count > 0)
                                {
                                    foreach(var bone in list)
                                    {
                                        if(bone != null)
                                        {
                                            _dbContext.Bones.Remove(bone);
                                        }
                                    }
                                    await _dbContext.SaveChangesAsync();
                                }
                                if (System.IO.File.Exists(model.Data))
                                {
                                    System.IO.File.Delete(model.Data);
                                }
                            }
                            catch (Exception ex)
                            {
                                Response.StatusCode = 500; //Server Error
                                return Content(ex.Message.ToString());
                            }
                        }
                        else
                        {
                            Response.StatusCode = 500;
                            return Content("Unknown bones file command.");
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
