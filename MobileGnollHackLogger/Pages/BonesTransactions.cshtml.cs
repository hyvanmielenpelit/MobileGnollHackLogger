using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using MobileGnollHackLogger.Data;

namespace MobileGnollHackLogger.Pages
{
    public class BonesVersionCompatibilityInfoWithMax : BonesVersionCompatibilityInfo
    {
        public uint? MaxVersion { get; set; }

        public BonesVersionCompatibilityInfoWithMax()
        {
            
        }
        public BonesVersionCompatibilityInfoWithMax(uint version, string label, uint? maxVersion) : base (version, label)
        {
            MaxVersion = maxVersion;
        }
    }

    public class BonesTransactionsModel : PageModel
    {
        public ApplicationDbContext DbContext { get; set; }

        public string? Title { get; set; }

        public List<BonesVersionCompatibilityInfoWithMax> CompatibilityVersions { get; }
        public int? SelectedCompatibilityVersion { get; set;  }

        public BonesTransactionsModel(ApplicationDbContext dbContext)
        {
            DbContext = dbContext;
            Title = "Bones Sharing";

            if(BonesHelper.VersionCompatibilityList != null && BonesHelper.VersionCompatibilityList.Count > 0)
            {
                for (int i = BonesHelper.VersionCompatibilityList.Count - 1; i >= 0; i--)
                {
                    var bonesInfo = BonesHelper.VersionCompatibilityList[i];
                    BonesVersionCompatibilityInfoWithMax bvciwm = new BonesVersionCompatibilityInfoWithMax()
                    {
                        Version = bonesInfo.Version,
                        Label = bonesInfo.Label
                    };
                    if(i < BonesHelper.VersionCompatibilityList.Count - 1)
                    {
                        bvciwm.MaxVersion = BonesHelper.VersionCompatibilityList[i + 1].Version - 1;
                    }
                }
                SelectedCompatibilityVersion = 0;
            }
        }

        public void OnGet()
        {

        }
    }
}
