using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using System.Globalization;
using System.Text;

namespace MobileGnollHackLogger.Areas.API
{
    [Route("api/replay")]
    [ApiController]
    public class ReplayController : ControllerBase
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LogModel> _logger;
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly DbLogger _dbLogger;
        private readonly string _replayBasePath = "";

        public ReplayController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            ILogger<LogModel> logger, IConfiguration configuration, ApplicationDbContext dbContext)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
            _configuration = configuration;
            _dbContext = dbContext;
            _dbLogger = new DbLogger(_dbContext);
            _dbLogger.LogType = LogType.Bones;
            _dbLogger.LogSubType = RequestLogSubType.Default;
            _replayBasePath = _configuration["ReplayPath"] ?? "";

            if (string.IsNullOrEmpty(_replayBasePath))
            {
                throw new Exception("ReplayPath is null");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromForm] ReplayModel model)
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
                    //Sign in succeeded
                    _dbLogger.LoginSucceeded = true;

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
                                await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                Response.StatusCode = responseCode; //Server Error
                                return Content(msg);
                            }
                            else if (user.IsBonesBanned == true)
                            {
                                int responseCode = 423;
                                string msg = "User " + model.UserName + " is not allowed to send bones.";
                                await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                                Response.StatusCode = responseCode; //Server Error
                                return Content(msg);
                            }
                        }
                        else
                        {
                            int responseCode = 500;
                            string msg = "Server error occurred while verifying user: User is null.";
                            await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                            Response.StatusCode = responseCode; //Server Error
                            return Content(msg);
                        }
                    }
                    catch (Exception ex)
                    {
                        int responseCode = 500;
                        string msg = "Server error occurred while verifying user: " + ex.Message;
                        await _dbLogger.LogRequestAsync(msg, Data.LogLevel.Error, responseCode);
                        Response.StatusCode = responseCode; //Server Error
                        return Content(msg);
                    }

                    //More code goes here


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
