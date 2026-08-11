using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Overseer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[IgnoreAntiforgeryToken]
public class ChangelogController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public ChangelogController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult GetReleaseNotes()
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "release-notes.json");
        
        Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        int pageSize = _configuration.GetValue<int>("ChangelogPageSize", 10);

        if (!System.IO.File.Exists(path))
        {
            return Ok(new { pageSize = pageSize, notes = new List<object>() });
        }

        string json = System.IO.File.ReadAllText(path);
        string resultJson = $"{{\"pageSize\":{pageSize},\"notes\":{json}}}";
        return Content(resultJson, "application/json");
    }
}
