using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Pages
{
    public class BonesTransactionsModel : PageModel
    {
        public ApplicationDbContext DbContext { get; set; }

        public string? Title { get; set; }

        public BonesTransactionsModel(ApplicationDbContext dbContext)
        {
            DbContext = dbContext;
            Title = "Bones Transactions";
        }

        public void OnGet()
        {

        }
    }
}
