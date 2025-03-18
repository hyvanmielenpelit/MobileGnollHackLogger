using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MobileGnollHackLogger.Pages
{
    public class TestSFTShaModel : PageModel
    {
        [BindProperty]
        public Areas.API.SaveFileTrackingShaModel Input { get; set; }

        public TestSFTShaModel()
        {
            Input = new Areas.API.SaveFileTrackingShaModel();
        }

        public void OnGet()
        {
        }
    }
}
