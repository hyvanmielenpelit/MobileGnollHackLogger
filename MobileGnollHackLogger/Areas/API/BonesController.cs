using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using MobileGnollHackLogger.Data.Migrations;
using System.Globalization;


//using MobileGnollHackLogger.Data.Migrations;
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
        private readonly DbLogger _dbLogger;
        private readonly string _bonesBasePath = "";

        public BonesController(SignInManager<ApplicationUser> signInManager, ILogger<LogModel> logger,
            IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _logger = logger;
            _configuration = configuration;
            _dbContext = dbContext;
            _dbLogger = new DbLogger(_dbContext);
            _dbLogger.LogType = LogType.Bones;
            _dbLogger.LogSubType = RequestLogSubType.Default;

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
        [HttpGet]
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

                _dbLogger.RequestData = model.GetJson();

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
                if (model.AntiForgeryToken == null)
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
                    _dbLogger.LoginSucceeded = true;

                    if (!string.IsNullOrEmpty(model.Command) && !string.IsNullOrEmpty(model.Data))
                    {
                        //Sign in succeedeed
                        if(model.Command == "SendBonesFile")
                        {
                            _dbLogger.LogSubType = RequestLogSubType.MainFunctionality;
                            //_logger.LogInformation("SendBonesFile request received from user " + model.UserName);
                            await _dbLogger.LogRequestAsync($"SendBonesFile request received from user {model.UserName}.",
                                Data.LogLevel.Info);
                            if (model.BonesFile == null)
                            {
                                //_logger.LogInformation("No bones file was attached to the request");
                                int responseCode = 500;
                                string msg = "Bones file is null when sending a bones file.";
                                await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                Response.StatusCode = responseCode; //Server Error
                                return Content(msg);
                            }

                            const int ServerAllBoneLimit = 512;
                            const int ServerUserBoneLimit = 32;
                            const int ServerAvailableBoneMinLimit = 4;
                            const int ServerAvailableBoneMaxLimit = 128;

                            long id = 0;
                            int i = 0;

                            //Difficulty is in the data field of the SendBonesFile command
                            int difficulty = 0;
                            if (!string.IsNullOrEmpty(model.Data))
                                int.TryParse(model.Data, out difficulty);

                            int userBoneCount = 0;
                            int allBoneCount = 0;
                            ApplicationUser dbUser = (ApplicationUser)_dbContext.Users.First(u => u.UserName == model.UserName);
                            string aspNetUserId = dbUser.Id;

                            try
                            {                                
                                var allBones = _dbContext.Bones.Where(
                                    b => b.DifficultyLevel == difficulty
                                    && (b.VersionNumber == model.VersionNumber
                                        || (b.VersionNumber < model.VersionNumber
                                            ? (b.VersionNumber >= model.VersionCompatibilityNumber)
                                            : (b.VersionCompatibilityNumber <= model.VersionNumber))));

                                var allBoneList = allBones.ToList();

                                /* Return a bones file from the existing bones, if possible */
                                var userBones = _dbContext.Bones.Where(
                                    b => b.AspNetUserId == aspNetUserId
                                    && b.DifficultyLevel == difficulty
                                    && (b.VersionNumber == model.VersionNumber
                                        || (b.VersionNumber < model.VersionNumber
                                            ? (b.VersionNumber >= model.VersionCompatibilityNumber)
                                            : (b.VersionCompatibilityNumber <= model.VersionNumber))));

                                var userBoneList = userBones.ToList();
                                userBoneCount = userBoneList.Count;
                                allBoneCount = allBoneList.Count;
                            }
                            catch
                            {
                                userBoneCount = 0;
                                allBoneCount = 0;
                            }

                            if (userBoneCount < ServerUserBoneLimit && allBoneCount < ServerAllBoneLimit)
                            {
                                // Write Bones Files
                                string dir = Path.Combine(_bonesBasePath, model.UserName);
                                if (!System.IO.Directory.Exists(dir))
                                {
                                    Directory.CreateDirectory(dir);
                                }

                                string baseFilePath = dir + @"\" + model.BonesFile.FileName;
                                string fullFilePath;
                                do
                                {
                                    fullFilePath = baseFilePath + "_" + i;
                                    i++;
                                } while (System.IO.File.Exists(fullFilePath));

                                using var bonesOutStream = System.IO.File.OpenWrite(fullFilePath);
                                await model.BonesFile.CopyToAsync(bonesOutStream);

                                //_logger.LogInformation("Bones file " + fullFilePath + " from user " + model.UserName + " written as " + model.BonesFile.FileName + " at directory " + dir);
                                await _dbLogger.LogRequestAsync($"Bones file {fullFilePath} from user {model.UserName} written as {model.BonesFile.FileName} at directory {dir}.",
                                    Data.LogLevel.Info);

                                try
                                {
                                    Data.Bones bone = new Data.Bones(model.UserName,
                                        model.GetPlatform(),
                                        model.GetPlatformVersion(),
                                        model.GetPort(),
                                        model.GetPortVersion(),
                                        model.GetPortBuild(),
                                        model.VersionNumber,
                                        model.VersionCompatibilityNumber,
                                        difficulty,
                                        fullFilePath,
                                        model.BonesFile.FileName,
                                        dbUser);

                                    await _dbContext.Bones.AddAsync(bone);
                                    await _dbContext.SaveChangesAsync();

                                    Data.BonesTransaction transaction = new Data.BonesTransaction(model.UserName, TransactionType.Upload, bone, dbUser);
                                    await _dbContext.BonesTransactions.AddAsync(transaction);
                                    await _dbContext.SaveChangesAsync();

                                    id = bone.Id;
                                    //_logger.LogInformation("Bones file from user " + model.UserName + " written to database as ID " + id);
                                    await _dbLogger.LogRequestAsync($"Bones file from user {model.UserName} written to database as ID {id}.",
                                        Data.LogLevel.Info);

                                    if (id == 0)
                                    {
                                        int responseCode = 500;
                                        string msg = "Inserted Id is 0";
                                        await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                        Response.StatusCode = responseCode;
                                        return Content(msg);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    int responseCode = 500;
                                    string msg = $"Exception occurred while adding a new bones entry. Message: {ex.Message}";
                                    await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                    Response.StatusCode = responseCode; //Server Error
                                    return Content(msg);
                                }
                            }

                            /* Return a bones file from the existing bones, if possible */
                            try
                            {
                                var availableBones = _dbContext.Bones.Where(
                                    b => b.AspNetUserId != aspNetUserId 
                                    && b.DifficultyLevel == difficulty 
                                    && (b.VersionNumber == model.VersionNumber 
                                        || (b.VersionNumber < model.VersionNumber 
                                            ? (b.VersionNumber >= model.VersionCompatibilityNumber) 
                                            : (b.VersionCompatibilityNumber <= model.VersionNumber))));

                                var availableBoneList = availableBones.ToList();
                                if (availableBoneList != null)
                                {
                                    int availableBoneCount = availableBoneList.Count;
                                    //_logger.LogInformation("Listed " + availableBoneCount + " bones file(s) available to be returned to user " + model.UserName);
                                    await _dbLogger.LogRequestAsync($"Listed {availableBoneCount} bones file(s) available to be returned to user {model.UserName}.",
                                        Data.LogLevel.Info);

                                    if (availableBoneCount < ServerAvailableBoneMinLimit)
                                    {
                                        string msg = id.ToString() + ", too few bones files on server to send a bones file back: " + availableBoneCount + " applicable bones file" + (availableBoneCount == 1 ? "" : "s") + " on server";
                                        await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Info, 200);
                                        return Content(msg, "text/plain", Encoding.UTF8); //OK
                                    }

                                    if (availableBoneCount < ServerAvailableBoneMaxLimit)
                                    {
                                        Random random1 = new Random();
                                        double chance = 1.0d / 2.0d + 1.0d / 2.0d * ((double)(availableBoneCount - ServerAvailableBoneMinLimit) / (double)(ServerAvailableBoneMaxLimit - ServerAvailableBoneMinLimit));
                                        double rndResult = random1.NextDouble();
                                        if (rndResult >= chance) //If fails
                                        {
                                            string msg = id.ToString() + 
                                                ", randomly did not send a bones file back: " + 
                                                availableBoneCount + " applicable bones file" + 
                                                (availableBoneList.Count == 1 ? "" : "s") + 
                                                " on server. " +
                                                "Chance=" + chance.ToPercentageString(4) +
                                                ", result=" + rndResult.ToPercentageString(4);
                                            await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Info, 200);
                                            return Content(msg, "text/plain", Encoding.UTF8); //OK
                                        }
                                    }

                                    /* Send a bones file */
                                    if (availableBoneCount > 0)
                                    {
                                        string? bonespath = null;
                                        long bonesid = 0;
                                        Random random = new Random();
                                        int indx = availableBoneCount == 1 ? 0 : random.Next(availableBoneCount);
                                        bonespath = availableBoneList[indx].BonesFilePath;
                                        bonesid = availableBoneList[indx].Id;
                                        if (availableBoneCount > 1 && (bonespath == null || !System.IO.File.Exists(bonespath)))
                                        {
                                            for (i = 0; i < availableBoneCount; i++)
                                            {
                                                bonespath = availableBoneList[i].BonesFilePath;
                                                bonesid = availableBoneList[i].Id;
                                                if (bonespath != null && System.IO.File.Exists(bonespath))
                                                {
                                                    indx = i;
                                                    break;
                                                }
                                            }
                                        }
                                        if (bonespath != null && System.IO.File.Exists(bonespath))
                                        {
                                            string? originalfilename = availableBoneList[indx].OriginalFileName != null ? availableBoneList[indx].OriginalFileName : "";
                                            try
                                            {
                                                byte[] bytes = await System.IO.File.ReadAllBytesAsync(bonespath);
                                                if (bytes != null && bytes.Length > 0) 
                                                {
                                                    await _dbLogger.LogRequestAsync($"Sending back to user {model.UserName} a bones file with ID {bonesid}, original name of {originalfilename} and server path {bonespath}. File length is {bytes.LongLength}.",
                                                        Data.LogLevel.Info, 200);

                                                    Data.BonesTransaction transaction = new Data.BonesTransaction(model.UserName, TransactionType.Download, null, dbUser)
                                                    {
                                                        BonesId = bonesid,
                                                        DifficultyLevel = difficulty,
                                                        Platform = model.GetPlatform(),
                                                        PlatformVersion = model.GetPlatformVersion(),
                                                        Port = model.GetPort(),
                                                        PortVersion = model.GetPortVersion(),
                                                        PortBuild = model.GetPortBuild(),
                                                        VersionNumber = model.VersionNumber,
                                                        VersionCompatibilityNumber = model.VersionCompatibilityNumber
                                                    };

                                                    await _dbContext.BonesTransactions.AddAsync(transaction);
                                                    await _dbContext.SaveChangesAsync();

                                                    Response?.Headers?.TryAdd("X-GH-OriginalFileName", new Microsoft.Extensions.Primitives.StringValues(originalfilename));
                                                    Response?.Headers?.TryAdd("X-GH-BonesFilePath", new Microsoft.Extensions.Primitives.StringValues(bonespath));
                                                    Response?.Headers?.TryAdd("X-GH-DataBaseTableId", new Microsoft.Extensions.Primitives.StringValues(bonesid.ToString()));
                                                    return File(bytes, "application/octet-stream", originalfilename);
                                                }
                                                else
                                                {
                                                    int responseCode = 500;
                                                    await _dbLogger.LogRequestAsync($"Sending back to user {model.UserName} a bones file with ID {bonesid}, original name of {originalfilename} and server path {bonespath}. However, id {id.ToString()}: read zero bytes.",
                                                        Data.LogLevel.Error, responseCode);
                                                    Response.StatusCode = responseCode;
                                                    return Content(id.ToString() + ", read zero bytes", "text/plain", Encoding.UTF8);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                int responseCode = 500;
                                                string msg = id.ToString() + ", reading all bytes failed: " + ex.Message;
                                                await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                                Response.StatusCode = responseCode;
                                                return Content(id.ToString() + ", reading all bytes failed: " + ex.Message, "text/plain", Encoding.UTF8);
                                            }
                                        }
                                        else
                                        {
                                            int responseCode = 500;
                                            string msg = id.ToString() + ", " + (bonespath == null ? "bones file path is null" : "bones file " + bonespath + " does not exist");
                                            await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                            Response.StatusCode = responseCode;
                                            return Content(msg, "text/plain", Encoding.UTF8);
                                        }
                                    }
                                    else
                                    {
                                        int responseCode = 500;
                                        string msg = id.ToString() + ", couldn't locate a bones file";
                                        await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                        Response.StatusCode = responseCode;
                                        return Content(msg, "text/plain", Encoding.UTF8);
                                    }
                                }
                                else
                                {
                                    int responseCode = 500;
                                    string msg = id.ToString() + ", bones list is null";
                                    await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                    Response.StatusCode = responseCode;
                                    return Content(msg, "text/plain", Encoding.UTF8);
                                }
                            }
                            catch (InvalidOperationException invEx)
                            {
                                int responseCode = 410;
                                await _dbLogger.LogRequestAsync("Invalid Operation: " + invEx.Message, Data.LogLevel.Error, responseCode);
                                return StatusCode(responseCode); //Gone
                            }
                            catch (Exception ex)
                            {
                                int responseCode = 500;
                                string msg = "Error: " + ex.Message.ToString();
                                await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                Response.StatusCode = responseCode; //Server Error
                                return Content(msg);
                            }
                        }
                        else if(model.Command == "ConfirmReceipt")
                        {
                            _dbLogger.LogSubType = RequestLogSubType.MainFunctionality2;
                            await _dbLogger.LogRequestAsync($"Received a bones file confirmation receipt from user {model.UserName} for server bones file path {model.Data}",
                                Data.LogLevel.Info);
                            //_logger.LogInformation("Received a bones file confirmation receipt from user " + model.UserName + " for server bones file path " + model.Data);
                            try
                            {
                                var availableBones = _dbContext.Bones.Where(b => b.BonesFilePath == model.Data);
                                var list = availableBones.ToList();
                                bool didremoveentry = false, diddeletefile = false;
                                ApplicationUser dbUser = (ApplicationUser)_dbContext.Users.First(u => u.UserName == model.UserName);

                                if (list != null && list.Count > 0)
                                {
                                    List<Task> tasks = new List<Task>();
                                    foreach(var bone in list)
                                    {
                                        if(bone != null)
                                        {
                                            long bonesid = bone.Id;
                                            int difficulty = bone.DifficultyLevel;
                                            _dbContext.Bones.Remove(bone);
                                            didremoveentry = true;

                                            tasks.Add(_dbLogger.LogRequestAsync($"Deleted a database bones entry ID {bonesid}.",
                                                Data.LogLevel.Info));

                                            Data.BonesTransaction transaction = new Data.BonesTransaction(model.UserName, TransactionType.Deletion, null, dbUser)
                                            {
                                                BonesId = bonesid,
                                                DifficultyLevel = difficulty,
                                                Platform = model.GetPlatform(),
                                                PlatformVersion = model.GetPlatformVersion(),
                                                Port = model.GetPort(),
                                                PortVersion = model.GetPortVersion(),
                                                PortBuild = model.GetPortBuild(),
                                                VersionNumber = model.VersionNumber,
                                                VersionCompatibilityNumber = model.VersionCompatibilityNumber
                                            };
                                            _dbContext.BonesTransactions.Add(transaction);
                                        }
                                    }
                                    Task.WaitAll(tasks.ToArray());
                                    await _dbContext.SaveChangesAsync();
                                }
                                if (System.IO.File.Exists(model.Data))
                                {
                                    System.IO.File.Delete(model.Data);
                                    diddeletefile = true;
                                    await _dbLogger.LogRequestAsync($"Deleted the server file {model.Data}", Data.LogLevel.Info);
                                }
                                if (diddeletefile)
                                {
                                    int responseCode = 200;
                                    string msg = "File " + model.Data + " was successfully deleted from the server." + (didremoveentry ? " Corresponding entry was also deleted from the database." : "");
                                    await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Info);
                                    Response.StatusCode = responseCode;
                                    return Content(msg, "text/plain", Encoding.UTF8);
                                }
                                else
                                {
                                    int responseCode = 410; 
                                    string msg = "File " + model.Data + " was did not exist on the server and was thus not deleted." + (didremoveentry ? " However, a corresponding entry to the file was deleted from the database." : " A corresponding entry did not exist in the database either.");
                                    await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error);
                                    Response.StatusCode = responseCode; //Gone
                                    return Content(msg, "text/plain", Encoding.UTF8);
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
                        else
                        {
                            int responseCode = 500;
                            string msg = "Unknown bones file command.";
                            await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                            Response.StatusCode = responseCode;
                            return Content(msg);
                        }
                    }
                    else if (string.IsNullOrEmpty(model.Data) && model.BonesFile == null)
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
