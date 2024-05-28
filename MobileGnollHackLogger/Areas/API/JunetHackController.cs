using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;
using System.Text;

namespace MobileGnollHackLogger.Areas.API
{
    [ApiController]
    public class JunetHackController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public JunetHackController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        [Route("junethack/{username}")]
        [HttpGet]
        public async Task<IActionResult> Get(string username)
        {
            var user = await _userManager.FindByNameAsync(username);
            if (user == null)
            {
                return NotFound();
            }
            else
            {
                if(!string.IsNullOrWhiteSpace(user.JunetHackUserName))
                {
                    return Content($"junethack {user.JunetHackUserName}", "text/plain", Encoding.ASCII);
                }
                else
                {
                    return Content("", "text/plain", Encoding.ASCII);
                }
            }
        }
    }
}
