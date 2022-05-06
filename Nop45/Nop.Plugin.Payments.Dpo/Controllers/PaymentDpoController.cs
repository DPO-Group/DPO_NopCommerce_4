using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Nop.Core;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Dpo.Models;
using Nop.Services.Configuration;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Security;
using Nop.Services.Stores;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using System.Threading.Tasks;
using System.Xml.XPath;
using System.IO;

namespace Nop.Plugin.Payments.Dpo.Controllers
{
    public class PaymentDpoController : BasePaymentController
    {
        #region Fields

        private readonly IWorkContext _workContext;
        private readonly IStoreService _storeService;
        private readonly ISettingService _settingService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly ILocalizationService _localizationService;
        private readonly DpoPaymentSettings _dpoPaymentSettings;
        private readonly IPermissionService _permissionService;
        private ILogger _logger;
        private INotificationService _notificationService;
        private IStoreContext _storeContext;

        private readonly String dpoLiveUrl = "https://secure.3gdirectpay.com/API/v6/";
        private readonly String dpoLivePayUrl = "https://secure.3gdirectpay.com/payv2.php";

        #endregion

        #region Ctor

        public PaymentDpoController(
            IWorkContext workContext,
            IStoreService storeService,
            ISettingService settingService,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            ILocalizationService localizationService,
            IPermissionService permissionService,
            ILogger logger,
            DpoPaymentSettings dpoPaymentSettings,
            INotificationService notificationService,
            IStoreContext storeContext
            )
        {
            this._workContext = workContext;
            this._storeService = storeService;
            this._settingService = settingService;
            this._orderService = orderService;
            this._orderProcessingService = orderProcessingService;
            this._localizationService = localizationService;
            this._permissionService = permissionService;
            this._dpoPaymentSettings = dpoPaymentSettings;
            this._logger = logger;
            this._notificationService = notificationService;
            this._storeContext = storeContext;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Handle push payment response from DPO
        /// </summary>
        /// <param name="form"></param>
        public async void DpoNotifyHandler()
        {
            // Notify DPO with OK response
            await Response.WriteAsync("OK");

            var formData = new NameValueCollection();
            using (var reader = new StreamReader(
                Request.Body,
                encoding: Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false
                ))
            {
                var s = await reader.ReadToEndAsync();
                s = s.Replace("\\", "");
                var textReader = new StringReader(s);
                var response = new XPathDocument(textReader);
                var navigator = response.CreateNavigator();


                var reference = navigator.SelectSingleNode("//TransactionRef").Value;
                var trans_status = navigator.SelectSingleNode("//Result").Value;
                var transID = navigator.SelectSingleNode("//TransactionApproval").Value;

                bool isPaid;

                await _logger.InformationAsync("DpoNotifyHandler start. Order no.: " + reference);

                Order order = await _orderService.GetOrderByIdAsync(Int32.Parse(reference));

                int orderId = 0;
                if (order != null)
                {
                    orderId = order.Id;
                }
                await _logger.InformationAsync("DpoNotifyHandler: Order Payment Status: " + order.PaymentStatus);

                isPaid = order.PaymentStatus == PaymentStatus.Paid ? true : false;

                if (isPaid)
                {
                    await _logger.InformationAsync("DpoNotifyHandler: Order no. " + reference + " is already paid");
                }

                PaymentStatus query_status;
                var query_status_desc = "";

                switch (trans_status)
                {
                    case "000":
                        query_status = PaymentStatus.Paid;
                        query_status_desc = "Approved";
                        break;

                    case "901":
                        query_status = PaymentStatus.Voided;
                        query_status_desc = "Declined";
                        break;

                    case "904":
                        query_status = PaymentStatus.Voided;
                        query_status_desc = "Cancelled By Customer with back button on payment page";
                        break;

                    default:
                        query_status = PaymentStatus.Voided;
                        query_status_desc = "Not Done";
                        break;
                }

                var sBuilder = new StringBuilder();

                sBuilder.AppendLine("Dpo Notify Handler");
                sBuilder.AppendLine("Dpo Query Data");
                sBuilder.AppendLine("=======================");
                sBuilder.AppendLine("Dpo Transaction_Id: " + transID);
                sBuilder.AppendLine("Dpo Status Desc: " + query_status_desc);
                sBuilder.AppendLine("");

                //order note
                await _orderService.InsertOrderNoteAsync(new OrderNote
                {
                    OrderId = orderId,
                    Note = sBuilder.ToString(),//sbbustring.Format("Order status has been changed to {0}", PaymentStatus.Paid),
                    DisplayToCustomer = false,
                    CreatedOnUtc = DateTime.UtcNow
                }); ;

                await _orderService.UpdateOrderAsync(order);

                //mark order as paid
                if (query_status == PaymentStatus.Paid)
                {
                    if (_orderProcessingService.CanMarkOrderAsPaid(order))
                    {
                        order.AuthorizationTransactionId = transID;
                        await _orderService.UpdateOrderAsync(order);

                        await _orderProcessingService.MarkOrderAsPaidAsync(order);
                    }
                    await _orderService.UpdateOrderAsync(order);
                    await _logger.InformationAsync("DpoNotifyHandler: Order marked paid");
                }
                else
                {
                    order.AuthorizationTransactionId = transID;
                    OrderNote note = new OrderNote();
                    note.OrderId = orderId;
                    note.CreatedOnUtc = DateTime.Now;
                    note.DisplayToCustomer = true;
                    note.Note = "Payment failed with the following description: " + trans_status;
                    await _logger.ErrorAsync("DpoNotifyHandler: Payment failed with the following description: " + trans_status);
                    if (_orderProcessingService.CanCancelOrder(order))
                    {
                        await _orderProcessingService.CancelOrderAsync(order, false);
                    }
                    await _orderService.InsertOrderNoteAsync(note);
                    await _orderService.UpdateOrderAsync(order);
                }
            }
        }

        /// <summary>
        /// Handle redirect response from DPO
        /// </summary>
        /// <param name="form"></param>
        /// <returns></returns>
        public async Task<IActionResult> DpoReturnHandler(IFormCollection form)
        {
            var reference = Request.Query["pgnopcommerce"];
            var transID = Request.Query["TransID"];
            var transToken = Request.Query["TransactionToken"];
            var client = new System.Net.WebClient();

            var trans_status = "";

            await _logger.InformationAsync("DpoReturnHandler start. Order no.: " + reference);
            var verified = false;
            var companyToken = _dpoPaymentSettings.CompanyToken;
            var serviceType = _dpoPaymentSettings.ServiceType;
            var dpoUrl = dpoLiveUrl;

            var order = await _orderService.GetOrderByIdAsync(Int32.Parse(Request.Query["pgnopcommerce"]));
            int orderId = 0;
            if (order != null)
            {
                orderId = order.Id;
            }


            //First query DPO with POST request
            var verifXML = new StringBuilder();
            verifXML.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            verifXML.Append("<API3G>");
            verifXML.Append($"<CompanyToken>{companyToken }</CompanyToken>");
            verifXML.Append("<Request>verifyToken</Request>");
            verifXML.Append($"<TransactionToken>{transToken}</TransactionToken>");
            verifXML.Append("</API3G>");

            var cnt = 0;

            string payrequestId = "";
            var transactionStatus = "";

            while (!verified && cnt < 5)
            {
                var queryResponse = client.UploadString(dpoUrl, "POST", verifXML.ToString());
                var textReader = new StringReader(queryResponse);
                var response = new XPathDocument(textReader);
                var navigator = response.CreateNavigator();
                trans_status = navigator.SelectSingleNode("//Result").Value;
                payrequestId = navigator.SelectSingleNode("//TransactionApproval").Value;
                transactionStatus = navigator.SelectSingleNode("//ResultExplanation").Value;

                if (trans_status == "000")
                {
                    verified = true;
                }

                cnt++;
            }

            var isPaid = order.PaymentStatus == PaymentStatus.Paid ? true : false;
            await _logger.InformationAsync("DpoReturnHandler: Order Payment Status: " + order.PaymentStatus);

            if (isPaid)
            {
                await _logger.InformationAsync("DpoReturnHandler: Order no. " + reference + " is already paid");
                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }

            PaymentStatus query_status;
            var query_status_desc = "";

            switch (trans_status)
            {
                case "000":
                    query_status = PaymentStatus.Paid;
                    query_status_desc = "Approved";
                    break;

                case "901":
                    query_status = PaymentStatus.Voided;
                    query_status_desc = "Declined";
                    break;

                case "904":
                    query_status = PaymentStatus.Voided;
                    query_status_desc = "Cancelled By Customer with back button on payment page";
                    break;

                default:
                    query_status = PaymentStatus.Voided;
                    query_status_desc = "Not Done";
                    break;
            }

            var sBuilder = new StringBuilder();

            sBuilder.AppendLine("Dpo Return Handler");
            sBuilder.AppendLine("Dpo Query Data");
            sBuilder.AppendLine("=======================");
            sBuilder.AppendLine("Dpo Transaction_Id: " + transID);
            sBuilder.AppendLine("Dpo Status Desc: " + query_status_desc);
            sBuilder.AppendLine("");


            //order note
            await _orderService.InsertOrderNoteAsync(new OrderNote
            {
                OrderId = orderId,
                Note = sBuilder.ToString(),//sbbustring.Format("Order status has been changed to {0}", PaymentStatus.Paid),
                DisplayToCustomer = false,
                CreatedOnUtc = DateTime.UtcNow
            }); ;

            await _orderService.UpdateOrderAsync(order);

            //mark order as paid
            if (query_status == PaymentStatus.Paid)
            {
                if (_orderProcessingService.CanMarkOrderAsPaid(order))
                {
                    order.AuthorizationTransactionId = payrequestId;
                    await _orderService.UpdateOrderAsync(order);

                    await _orderProcessingService.MarkOrderAsPaidAsync(order);
                }
                await _orderService.UpdateOrderAsync(order);
                await _logger.InformationAsync("DpoReturnHandler: Order marked paid");
                return RedirectToRoute("CheckoutCompleted", new { orderId = order.Id });
            }
            else
            {
                order.AuthorizationTransactionId = payrequestId;
                OrderNote note = new OrderNote();
                note.OrderId = orderId;
                note.CreatedOnUtc = DateTime.Now;
                note.DisplayToCustomer = true;
                note.Note = "Payment failed with the following description: " + transactionStatus;
                await _logger.ErrorAsync("DpoReturnHandler: Payment failed with the following description: " + transactionStatus);
                if (_orderProcessingService.CanCancelOrder(order))
                {
                    await _orderProcessingService.CancelOrderAsync(order, false);
                }
                await _orderService.InsertOrderNoteAsync(note);
                await _orderService.UpdateOrderAsync(order);

                return RedirectToRoute("OrderDetails", new { orderId = order.Id.ToString().Trim() });
            }
        }

        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync(); var dpoPaymentSettings = await _settingService.LoadSettingAsync<DpoPaymentSettings>(storeScope);

            var model = new ConfigurationModel();
            model.CompanyToken = dpoPaymentSettings.CompanyToken;
            model.ServiceType = dpoPaymentSettings.ServiceType;
            model.ActiveStoreScopeConfiguration = storeScope;
            model.UseSSL = dpoPaymentSettings.UseSSL;

            if (storeScope > 0)
            {
                model.CompanyToken_OverrideForStore = await _settingService.SettingExistsAsync(dpoPaymentSettings, x => x.CompanyToken, storeScope);
                model.ServiceType_OverrideForStore = await _settingService.SettingExistsAsync(dpoPaymentSettings, x => x.ServiceType, storeScope);
                model.UseSSL_OverrideForStore = await _settingService.SettingExistsAsync(dpoPaymentSettings, x => x.UseSSL, storeScope);
            }

            return View("~/Plugins/Payments.Dpo/Views/Configure.cshtml", model);
        }

        [HttpPost, ActionName("Configure")]
        [AuthorizeAdmin]
        [Area(AreaNames.Admin)]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var dpoPaymentSettings = await _settingService.LoadSettingAsync<DpoPaymentSettings>(storeScope);

            //save settings
            dpoPaymentSettings.CompanyToken = model.CompanyToken;
            dpoPaymentSettings.ServiceType = model.ServiceType;
            dpoPaymentSettings.UseSSL = model.UseSSL;

            /* We do not clear cache after each setting update.
             * This behavior can increase performance because cached settings will not be cleared 
             * and loaded from database after each update */
            await _settingService.SaveSettingOverridablePerStoreAsync
                 (dpoPaymentSettings, x => x.CompanyToken, model.CompanyToken_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync
                 (dpoPaymentSettings, x => x.ServiceType, model.ServiceType_OverrideForStore, storeScope, false);
            await _settingService.SaveSettingOverridablePerStoreAsync
                 (dpoPaymentSettings, x => x.UseSSL, model.UseSSL_OverrideForStore, storeScope, false);

            //now clear settings cache
            await _settingService.ClearCacheAsync();

            _notificationService.SuccessNotification(await _localizationService.GetResourceAsync("Admin.Plugins.Saved"));

            return await Configure();
        }


        #endregion
    }
}