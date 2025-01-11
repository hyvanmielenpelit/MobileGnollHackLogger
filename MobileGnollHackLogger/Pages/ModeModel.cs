using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MobileGnollHackLogger.Pages
{
    public class ModeModel : PageModel
    {
        public string? Title { get; set; }

        public string? Mode { get; set; }

        public List<string> DisplayModes { get; private set; }

        public ModeModel() : base()
        {
            DisplayModes = new List<string>()
            {
                "normal",
                "modern"
            };

            Mode = null;
        }

    }
}
