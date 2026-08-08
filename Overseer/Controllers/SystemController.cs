using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class SystemController : ControllerBase
{
    [HttpGet("version")]
    public IActionResult GetVersion()
    {
        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "unknown";
            
        var cleanVersion = version.Split('+')[0];
            
        return Ok(new { version = cleanVersion });
    }
}
