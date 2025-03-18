using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MobileGnollHackLogger.Pages
{
    public class TestSFTUseModel : PageModel
    {
        [BindProperty]
        public Areas.API.SaveFileTrackingUseModel Input { get; set; }

        public TestSFTUseModel()
        {
            Input = new Areas.API.SaveFileTrackingUseModel();
        }

        public void OnGet()
        {
        }
    }
}
