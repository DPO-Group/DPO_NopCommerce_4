using Microsoft.AspNetCore.Mvc;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.Dpo.Components
{
    [ViewComponent(Name = "PaymentDpo")]
    public class PaymentDpoViewComponent : NopViewComponent
    {
        public IViewComponentResult Invoke()
        {
            return View("~/Plugins/Payments.Dpo/Views/PaymentInfo.cshtml");
        }
    }
}
