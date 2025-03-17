using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Build.Framework;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using MobileGnollHackLogger.Data;
using System.Text;

namespace MobileGnollHackLogger.Areas.API
{
    [Route("api/[controller]")]
    [ApiController]
    public class SaveFileTrackingController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;

        public SaveFileTrackingController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
            _dbContext = dbContext;
        }

        [Route("create")]
        [HttpPost]
        public async Task<IActionResult> Create([FromForm] SaveFileTrackingCreateModel model)
        {
            IActionResult? result = await LogInAsync(model);
            if (result == null)
            {
                try
                {
                    SaveFileTracking sft = new SaveFileTracking(model!.UserName!, _dbContext);
                    sft.CreatedDate = DateTime.UtcNow;
                    sft.TimeStamp = model.TimeStamp;
                    await _dbContext.SaveFileTrackings.AddAsync(sft);
                    await _dbContext.SaveChangesAsync();
                    if (sft.Id == 0)
                    {
                        Response.StatusCode = 500;
                        string msg = "Inserted Id is 0.";
                        return Content(msg);
                    }

                    return Content(sft.Id.ToString(), "text/plain", Encoding.UTF8); //OK
                }
                catch (Exception ex)
                {
                    Response.StatusCode = 500; //Server Error
                    return Content("SaveFileTracking creation to database failed. Message: " + ex.Message);
                }
            }
            else
            {
                return result;
            }
        }

        [Route("sha")]
        [HttpPost]
        public async Task<IActionResult> Sha([FromForm] SaveFileTrackingShaModel model)
        {
            IActionResult? result = await LogInAsync(model);
            if (result == null)
            {
                try
                {
                    var sft = _dbContext.SaveFileTrackings.FirstOrDefault(t => t.Id == model.Id);
                    if (sft == null)
                    {
                        Response.StatusCode = 404;
                        return Content($"SaveFileTracking with ID {model.Id} not found.");
                    }

                    if (sft.TimeStamp != model.TimeStamp)
                    {
                        Response.StatusCode = 403;
                        return Content($"UniqueId {model.TimeStamp} is not correct.");
                    }

                    if (sft.UsedDate != null)
                    {
                        Response.StatusCode = 401;
                        return Content($"SaveFileTracking with ID {model.Id} already used.");
                    }

                    sft.Sha256 = model.Sha256;
                    sft.FileLength = model.FileLength;
                    await _dbContext.SaveChangesAsync();

                    return Ok(); //OK
                }
                catch (Exception ex)
                {
                    Response.StatusCode = 500; //Server Error
                    return Content("Sha256 and file length update to database failed. Message: " + ex.Message);
                }
            }
            else
            {
                return result;
            }
        }

        [Route("use")]
        [HttpPost]
        public async Task<IActionResult> Use([FromForm] SaveFileTrackingUseModel model)
        {
            IActionResult? result = await LogInAsync(model);
            if (result == null)
            {
                try
                {
                    var sft = _dbContext.SaveFileTrackings.FirstOrDefault(t => t.Id == model.Id);
                    if (sft == null)
                    {
                        Response.StatusCode = 404;
                        return Content($"SaveFileTracking with ID {model.Id} not found.");
                    }

                    if(sft.TimeStamp!= model.TimeStamp)
                    {
                        Response.StatusCode = 403;
                        return Content($"UniqueId {model.TimeStamp} is not correct.");
                    }

                    if(sft.UsedDate != null)
                    {
                        Response.StatusCode = 410;
                        return Content($"SaveFileTracking with ID {model.Id} already used.");
                    }

                    if(sft.FileLength == 0)
                    {
                        Response.StatusCode = 403;
                        return Content($"FileLength not set in database.");
                    }

                    if (model.FileLength == 0)
                    {
                        Response.StatusCode = 400;
                        return Content($"Model FileLength is 0.");
                    }

                    if(sft.FileLength != model.FileLength)
                    {
                        Response.StatusCode = 403;
                        return Content($"File lengths do not match.");
                    }

                    if (sft.Sha256 == null)
                    {
                        Response.StatusCode = 403;
                        return Content($"Sha256 not set in database.");
                    }

                    if (string.IsNullOrEmpty(model.Sha256))
                    {
                        Response.StatusCode = 400;
                        return Content($"Sha256 not set in Model.");
                    }

                    if (sft.Sha256 != model.Sha256)
                    {
                        Response.StatusCode = 403;
                        return Content($"Sha256 hashes do not match.");
                    }

                    sft.UsedDate = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    return Ok();
                }
                catch (Exception ex)
                {
                    Response.StatusCode = 500; //Server Error
                    return Content("SaveFileTracking use failed. Message: " + ex.Message);
                }
            }
            else
            {
                return result;
            }
        }

        private async Task<IActionResult?> LogInAsync(LoginInfoModel model)
        {
            try
            {
                if (model == null)
                {
                    Response.StatusCode = 400; //Bad Request
                    return Content("All model data is missing."); //Bad Request
                }

                if (model.UserName == null)
                {
                    Response.StatusCode = 400; //Bad Request
                    return Content("UserName is missing.");
                }

                if (model.Password == null)
                {
                    Response.StatusCode = 400; //Bad Request
                    return Content("Password is missing.");
                }
                if (model.AntiForgeryToken == null)
                {
                    Response.StatusCode = 400; //Bad Request
                    return Content("AntiForgeryToken is missing.");
                }

                var antiForgeryToken = _configuration["AntiForgeryToken"];
                if (antiForgeryToken != model.AntiForgeryToken)
                {
                    Response.StatusCode = 401; //Not Authorized
                    return Content("AntiForgeryToken is wrong.");
                }

                var result = await _signInManager.PasswordSignInAsync(model.UserName, model.Password, false, lockoutOnFailure: false);
                if (result.Succeeded)
                {
                    //Sign in succeedeed
                    //Check next whether the user is not banned
                    try
                    {
                        var user = await _userManager.FindByNameAsync(model.UserName);
                        if (user != null)
                        {
                            if (user.IsBanned == true)
                            {
                                int responseCode = 423;
                                string msg = "User " + model.UserName + " is banned.";
                                Response.StatusCode = responseCode; //Server Error
                                return Content(msg);
                            }
                        }
                        else
                        {
                            int responseCode = 500;
                            string msg = "Server error occurred while verifying user: User is null.";
                            Response.StatusCode = responseCode; //Server Error
                            return Content(msg);
                        }
                    }
                    catch (Exception ex)
                    {
                        int responseCode = 500;
                        string msg = "Server error occurred while verifying user: " + ex.Message;
                        Response.StatusCode = responseCode; //Server Error
                        return Content(msg);
                    }

                    return null;
                }
                if (result.RequiresTwoFactor)
                {
                    int responseCode = 412;
                    return StatusCode(responseCode);
                }
                if (result.IsLockedOut)
                {
                    int responseCode = 423;
                    return StatusCode(responseCode);
                }
                else
                {
                    int responseCode = 403;
                    return StatusCode(responseCode);
                }
            }
            catch (Exception ex)
            {
                Response.StatusCode = 500;
                return Content("Exception: " + ex.Message);
            }

        }
    }
}
