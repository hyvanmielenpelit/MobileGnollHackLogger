using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MobileGnollHackLogger.Pages
{
    public class TestSFTCreateModel : PageModel
    {
        [BindProperty]
        public Areas.API.SaveFileTrackingCreateModel Input { get; set; }

        public TestSFTCreateModel()
        {
            Input = new Areas.API.SaveFileTrackingCreateModel();
        }

        public void OnGet()
        {
        }
    }
}
