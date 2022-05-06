using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.Dpo
{
    public partial class RouteProvider : IRouteProvider
    {
        /// <summary>
        /// Register routes
        /// </summary>
        /// <param name="routeBuilder">Route builder</param>
        public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
        {
            //DpoReturnHandler
            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.Dpo.DpoReturnHandler",
                "Plugins/PaymentDpo/DpoReturnHandler",
                new { controller = "PaymentDpo", action = "DpoReturnHandler" }
            );
            //DpoNotifyHandler
            endpointRouteBuilder.MapControllerRoute("Plugin.Payments.Dpo.DpoNotifyHandler",
                "Plugins/PaymentDpo/DpoNotifyHandler",
                new { controller = "PaymentDpo", action = "DpoNotifyHandler" }
            );
        }

        /// <summary>
        /// Gets a priority of route provider
        /// </summary>
        public int Priority
        {
            get { return 0; }
        }
    }
}
