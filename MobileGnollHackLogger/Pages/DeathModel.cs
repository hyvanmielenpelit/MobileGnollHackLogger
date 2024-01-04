using System.Text;

namespace MobileGnollHackLogger.Pages
{
    public class DeathModel : ModeModel
    {
        public string? PageName {  get; set; }
        public string? Death { get; set; }

        public DeathModel(string pageName) : base()
        {
            Death = null;
            PageName = pageName;
        }

        public string GetUrl(string? mode, string? death)
        {
            int n = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append("/").Append(PageName);
            if(!string.IsNullOrEmpty(mode) || !string.IsNullOrEmpty(death))
            {
                sb.Append("?");
            }
            if(!string.IsNullOrEmpty(mode))
            {
                sb.Append("mode=").Append(mode);
                n++;
            }
            if(!string.IsNullOrEmpty(death)) 
            { 
                if(n > 0)
                {
                    sb.Append("&");
                }
                sb.Append("death=").Append(death);
            }
            return sb.ToString();
        }
    }
}
