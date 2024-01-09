using Nop.Core.Configuration;

namespace Nop.Plugin.Payments.Dpo
{
    public class DpoPaymentSettings : ISettings
    {
        public string CompanyToken { get; set; }
        public string ServiceType { get; set; }
        public bool UseSSL { get; set; }

    }
}