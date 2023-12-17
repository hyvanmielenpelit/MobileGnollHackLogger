using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MobileGnollHackLogger.Pages
{
    public class TestBonesModel : PageModel
    {
        private IConfiguration _configuration;

        public TestBonesModel(IConfiguration configuration)
        {
            _configuration = configuration;
            Input = new Areas.API.BonesModel();
        }

        [BindProperty]
        public MobileGnollHackLogger.Areas.API.BonesModel Input { get; set; }

    }
}
