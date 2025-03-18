using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
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
        private ApplicationUser? _user = null;

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
                    var existingSft = _dbContext.SaveFileTrackings.FirstOrDefault(t => t.TimeStamp == model.TimeStamp && t.AspNetUserId == _user!.Id);
                    if(existingSft != null)
                    {
                        Response.StatusCode = 409; //Conflict
                        return Content($"User '{model.UserName}' already has Save File Tracking entry with Time Stamp {model.TimeStamp}.");
                    }

                    SaveFileTracking sft = new SaveFileTracking(model!.UserName!, _dbContext);
                    sft.CreatedDate = DateTime.UtcNow;
                    sft.TimeStamp = model.TimeStamp;
                    await _dbContext.SaveFileTrackings.AddAsync(sft);
                    await _dbContext.SaveChangesAsync();
                    if (sft.Id == 0)
                    {
                        Response.StatusCode = 500;
                        return Content("Inserted Id is 0.");
                    }

                    return Content(sft.Id.ToString()); //OK
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
                    if(model.Id <= 0)
                    {
                        Response.StatusCode = 400;
                        return Content($"Model Id is not greater than 0.");
                    }

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

                    if(string.IsNullOrEmpty(model.Sha256))
                    {
                        Response.StatusCode = 400;
                        return Content($"Sha256 is empty.");
                    }

                    try
                    {
                        var bytes = Convert.FromBase64String(model.Sha256);
                        if(bytes.Length != 32)
                        {
                            Response.StatusCode = 400;
                            return Content($"Sha256 is not 32 bytes long.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Response.StatusCode = 400;
                        return Content($"Error parsing Sha256 from Base64 string. Message: " + ex.Message);
                    }

                    if(model.FileLength <= 0)
                    {
                        Response.StatusCode = 400;
                        return Content($"File Length {model.FileLength} must be positive.");
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
                    if (model.Id <= 0)
                    {
                        Response.StatusCode = 400;
                        return Content($"Model Id is not greater than 0.");
                    }

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

                    if (model.FileLength <= 0)
                    {
                        Response.StatusCode = 400;
                        return Content($"Model FileLength {model.FileLength} is not positive.");
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

                    if(sft.AspNetUserId == null)
                    {
                        Response.StatusCode = 500;
                        return Content($"AspNetUserId is null.");
                    }

                    var aspNetUser = _dbContext.Users.FirstOrDefault(u => u.Id == sft.AspNetUserId);
                    if (aspNetUser == null)
                    {
                        Response.StatusCode = 500;
                        return Content($"AspNetUser not found.");
                    }

                    if (aspNetUser.UserName != model.UserName)
                    {
                        Response.StatusCode = 403;
                        return Content($"User names do not match.");
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
                        _user = await _userManager.FindByNameAsync(model.UserName);
                        if (_user != null)
                        {
                            if (_user.IsBanned == true)
                            {
                                Response.StatusCode = 423; //Server Error
                                return Content("User " + model.UserName + " is banned.");
                            }
                        }
                        else
                        {
                            Response.StatusCode = 500; //Server Error
                            return Content("Server error occurred while verifying user: User is null.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Response.StatusCode = 500; //Server Error
                        return Content("Server error occurred while verifying user: " + ex.Message);
                    }

                    //Everything OK
                    return null;
                }
                if (result.RequiresTwoFactor)
                {
                    Response.StatusCode = 412;
                    return Content($"User '{model.UserName}' requires two factor authentication.");
                }
                if (result.IsLockedOut)
                {
                    Response.StatusCode = 423;
                    return Content($"User '{model.UserName}' requires is locked out.");
                }
                else
                {
                    Response.StatusCode = 403;
                    return Content($"Login failed for user '{model.UserName}'.");
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
