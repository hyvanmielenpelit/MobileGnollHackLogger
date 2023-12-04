using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MobileGnollHackLogger.Pages
{
    public class TestModel : PageModel
    {
        [BindProperty]
        public MobileGnollHackLogger.Areas.API.LogModel Input { get; set; }

        public void OnGet()
        {
        }

        public IActionResult OnPost()
        {
            //To Do
            HttpClient httpClient = new HttpClient();
            var baseUrl = "https://" + HttpContext.Request.Headers.Host;
            HttpRequestMessage msg = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/API/log");
            var result = httpClient.Send(msg);
            using var contentStream = result.Content.ReadAsStream();
            using var streamReader = new StreamReader(contentStream);
            var text = streamReader.ReadToEnd();

            return Page();
        }
    }
}
