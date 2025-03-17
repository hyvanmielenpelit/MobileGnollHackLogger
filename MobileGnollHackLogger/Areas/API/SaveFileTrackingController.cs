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
        private readonly string _newLine = "\n"; //Use Unix line endings, the same as what Hardfought.org does

        public SaveFileTrackingController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
            _dbContext = dbContext;
            _userManager = userManager;
        }


        [HttpGet]
        public async Task<IActionResult> Get()
        {
            return Ok();
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
                    sft.UniqueId = Guid.NewGuid();
                    await _dbContext.SaveFileTrackings.AddAsync(sft);
                    await _dbContext.SaveChangesAsync();
                    if (sft.Id == 0)
                    {
                        Response.StatusCode = 500;
                        string msg = "Inserted Id is 0.";
                        return Content(msg);
                    }

                    var resposeInfo = new SaveFileTrackingResult(sft.Id, sft.UniqueId);
                    var responseText = System.Text.Json.JsonSerializer.Serialize(resposeInfo);

                    return Content(responseText, "text/plain", Encoding.UTF8); //OK
                }
                catch (InvalidOperationException invEx)
                {
                    Response.StatusCode = 410; //Gone
                    return Content("SaveFileTracking insertion to database failed. Message: " + invEx.Message); 
                }
                catch (Exception ex)
                {
                    Response.StatusCode = 500; //Server Error
                    return Content("GameLog insertion to database failed. Message: " + ex.Message);
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
                    Guid modelGuid;
                    bool ok = Guid.TryParse(model.UniqueId, out modelGuid);
                    if(!ok)
                    {
                        Response.StatusCode = 400;
                        return Content("Unique ID cannot be parsed.");
                    }

                    var sft = _dbContext.SaveFileTrackings.FirstOrDefault(t => t.Id == model.Id);
                    if (sft == null)
                    {
                        Response.StatusCode = 404;
                        return Content($"SaveFileTracking with ID {model.Id} not found.");
                    }

                    if(sft.UniqueId != modelGuid)
                    {
                        Response.StatusCode = 403;
                        return Content($"UniqueId {model.UniqueId} is not correct.");
                    }

                    if(sft.UsedDate != null)
                    {
                        Response.StatusCode = 401;
                        return Content($"SaveFileTracking with ID {model.Id} already used.");
                    }

                    sft.UsedDate = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();

                    var resposeInfo = new SaveFileTrackingResult(sft.Id, sft.UniqueId);
                    var responseText = System.Text.Json.JsonSerializer.Serialize(resposeInfo);

                    return Content(responseText, "text/plain", Encoding.UTF8); //OK
                }
                catch (InvalidOperationException invEx)
                {
                    Response.StatusCode = 410; //Gone
                    return Content("SaveFileTracking insertion to database failed. Message: " + invEx.Message);
                }
                catch (Exception ex)
                {
                    Response.StatusCode = 500; //Server Error
                    return Content("GameLog insertion to database failed. Message: " + ex.Message);
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
