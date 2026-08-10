using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[IgnoreAntiforgeryToken]
public class ChangelogController : ControllerBase
{
    [HttpGet]
    public IActionResult GetReleaseNotes()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "release-notes.json");
        
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        if (!System.IO.File.Exists(path))
        {
            return Ok(new List<object>());
        }

        var json = System.IO.File.ReadAllText(path);
        return Content(json, "application/json");
    }
}
