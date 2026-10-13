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
        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext _dbContext;
        private readonly DbLogger _dbLogger;
        private readonly GameApiAuthenticator _authenticator;

        public ReplayController(SignInManager<ApplicationUser> signInManager, UserManager<ApplicationUser> userManager,
            IConfiguration configuration, ApplicationDbContext dbContext, GameApiAuthenticator authenticator)
        {
            _authenticator = authenticator;
            _signInManager = signInManager;
            _userManager = userManager;
            _configuration = configuration;
            _dbContext = dbContext;
            _dbLogger = new DbLogger(_dbContext);
            _dbLogger.LogType = LogType.Bones;
            _dbLogger.LogSubType = RequestLogSubType.Default;
        }

        [HttpPost]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(GameApiRateLimits.GameApiFlood)]
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

                var auth = await _authenticator.AuthenticateAsync(HttpContext, _dbLogger, model.UserName, model.Password, model.AntiForgeryToken, "replay");
                if (!auth.Succeeded)
                {
                    return auth.Response!;
                }

                {
                    //Sign in succeeded

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

                // Replay upload is not implemented: a correct password ends in the same 403 as a wrong one.
                return GameApiAuthenticator.SignInFailedResponse(HttpContext);
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
