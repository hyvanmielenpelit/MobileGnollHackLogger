using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MobileGnollHackLogger.Pages
{
    public class TestModel : PageModel
    {
        private IConfiguration _configuration;

        public TestModel(IConfiguration configuration)
        {
            _configuration = configuration;
            Input = new Areas.API.LogModel();
        }

        [BindProperty]
        public MobileGnollHackLogger.Areas.API.LogModel Input { get; set; }

    }
}
