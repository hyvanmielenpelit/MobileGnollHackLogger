﻿using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
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
                            _logger.LogInformation("SendBonesFile request received from user " + model.UserName);
                            if (model.BonesFile == null)
                            {
                                _logger.LogInformation("No bones file was attached to the request");
                                Response.StatusCode = 500;
                                return Content("Bones file is null when sending a bones file.");
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

                            string aspNetUserId = "";
                            int userBoneCount = 0;
                            int allBoneCount = 0;

                            try
                            {
                                var dbUser = _dbContext.Users.First(u => u.UserName == model.UserName);
                                aspNetUserId = dbUser.Id;

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

                                _logger.LogInformation("Bones file " + fullFilePath + " from user " + model.UserName + " written as " + model.BonesFile.FileName + " at directory " + dir);

                                try
                                {
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
                                    id = bone.Id;
                                    _logger.LogInformation("Bones file from user " + model.UserName + " written to database as ID " + id);
                                    if (id == 0)
                                    {
                                        Response.StatusCode = 500;
                                        return Content("Inserted Id is 0.");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Response.StatusCode = 500;
                                    return Content("Exception occurred while adding a new bones entry: " + ex.Message);
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
                                    _logger.LogInformation("Listed " + availableBoneCount + " bones file(s) available to be returned to user " + model.UserName);

                                    if (availableBoneCount < ServerAvailableBoneMinLimit)
                                        return Content(id.ToString() + ", too few bones files on server to send a bones file back: " + availableBoneCount + " applicable bones file" + (availableBoneCount == 1 ? "" : "s") + " on server", "text/plain", Encoding.UTF8); //OK

                                    if (availableBoneCount < ServerAvailableBoneMaxLimit)
                                    {
                                        Random random1 = new Random();
                                        double chance = 1.0 / 3.0 + 2.0 / 3.0 * ((double)(availableBoneCount - ServerAvailableBoneMinLimit) / (ServerAvailableBoneMaxLimit - ServerAvailableBoneMinLimit));
                                        if (!(random1.NextDouble() < chance))
                                            return Content(id.ToString() + ", randomly did not send a bones file back: " + availableBoneCount + " applicable bones file" + (availableBoneList.Count == 1 ? "" : "s") + " on server", "text/plain", Encoding.UTF8); //OK
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
                                            _logger.LogInformation("Sending back to user " + model.UserName + " a  bones file with ID " + bonesid + ", original name of " + originalfilename + " and server path " + bonespath);
                                            try
                                            {
                                                byte[] bytes = await System.IO.File.ReadAllBytesAsync(bonespath);
                                                if (bytes != null && bytes.Length > 0) 
                                                {
                                                    Response?.Headers?.TryAdd("X-GH-OriginalFileName", new Microsoft.Extensions.Primitives.StringValues(originalfilename));
                                                    Response?.Headers?.TryAdd("X-GH-BonesFilePath", new Microsoft.Extensions.Primitives.StringValues(bonespath));
                                                    Response?.Headers?.TryAdd("X-GH-DataBaseTableId", new Microsoft.Extensions.Primitives.StringValues(bonesid.ToString()));
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
                            _logger.LogInformation("Received a bones file confirmation receipt from user " + model.UserName + " for server bones file path " + model.Data);
                            try
                            {
                                var availableBones = _dbContext.Bones.Where(b => b.BonesFilePath == model.Data);
                                var list = availableBones.ToList();
                                bool didremoveentry = false, diddeletefile = false;
                                if(list != null && list.Count > 0)
                                {
                                    foreach(var bone in list)
                                    {
                                        if(bone != null)
                                        {
                                            long bonesid = bone.Id;
                                            _dbContext.Bones.Remove(bone);
                                            didremoveentry = true;
                                            _logger.LogInformation("Deleted a database bones entry ID " + bonesid);
                                        }
                                    }
                                    await _dbContext.SaveChangesAsync();
                                }
                                if (System.IO.File.Exists(model.Data))
                                {
                                    System.IO.File.Delete(model.Data);
                                    diddeletefile = true;
                                    _logger.LogInformation("Deleted the server file " + model.Data);
                                }
                                if (diddeletefile)
                                    return Content("File " + model.Data + " was successfully deleted from the server."  + (didremoveentry ? " Corresponding entry was also deleted from the database." : ""), "text/plain", Encoding.UTF8);
                                else
                                    return Content("File " + model.Data + " was did not exist on the server and was thus not deleted." + (didremoveentry ? " However, a corresponding entry to the file was deleted from the database." : " A corresponding entry did not exist in the database either."), "text/plain", Encoding.UTF8);
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
