using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;

namespace Nop.Plugin.Payments.Dpo.Models
{
    public record ConfigurationModel : BaseNopModel
    {
        public int ActiveStoreScopeConfiguration { get; set; }      

        [NopResourceDisplayName("Plugins.Payments.Dpo.Fields.CompanyToken")]
        public string CompanyToken { get; set; }
        public bool CompanyToken_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Dpo.Fields.ServiceType")]
        public string ServiceType { get; set; }
        public bool ServiceType_OverrideForStore { get; set; }

        [NopResourceDisplayName("Plugins.Payments.Dpo.Fields.UseSSL")]
        public bool UseSSL { get; set; }
        public bool UseSSL_OverrideForStore { get; set; }
    }
}