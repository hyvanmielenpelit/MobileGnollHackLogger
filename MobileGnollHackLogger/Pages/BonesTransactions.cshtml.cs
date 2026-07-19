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

        public List<BonesVersionCompatibilityInfoWithMax> CompatibilityVersions { get; } = new List<BonesVersionCompatibilityInfoWithMax>();
        public int? SelectedCompatibilityVersion { get; set; }

        public uint? MinVersion { get; set; }
        public uint? MaxVersion { get; set; }

        public BonesTransactionsModel(ApplicationDbContext dbContext)
        {
            DbContext = dbContext;
            Title = "Bones";

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
                    CompatibilityVersions.Add(bvciwm);
                }
                SelectedCompatibilityVersion = 0;
            }
        }

        public void OnGet()
        {
            if (BonesHelper.VersionCompatibilityList != null && BonesHelper.VersionCompatibilityList.Count > 0)
            {
                int index = 0;
                if (Request.Query.ContainsKey("compatibility"))
                {
                    var queryVal = Request.Query["compatibility"].ToString();
                    if (!int.TryParse(queryVal, out int parsedIndex) || parsedIndex < 0 || parsedIndex >= CompatibilityVersions.Count)
                    {
                        throw new ArgumentOutOfRangeException("compatibility", "Compatibility version index is out of bounds or invalid.");
                    }
                    index = parsedIndex;
                }

                SelectedCompatibilityVersion = index;
                var selectedVersionInfo = CompatibilityVersions[index];
                MinVersion = selectedVersionInfo.Version;
                MaxVersion = selectedVersionInfo.MaxVersion;
            }
        }
    }
}
