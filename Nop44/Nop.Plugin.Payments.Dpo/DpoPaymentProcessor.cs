using System;
using System.Globalization;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nop.Core;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Services.Plugins;
using Nop.Plugin.Payments.Dpo.Controllers;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Payments;
using Nop.Web.Framework;
using Nop.Services.Logging;
using Nop.Core.Domain.Common;
using Nop.Services.Customers;
using Microsoft.AspNetCore.Http.Features;
using System.Xml.XPath;

namespace Nop.Plugin.Payments.Dpo
{
    /// <summary>
    /// Dpo payment processor
    /// </summary>
    public class DpoPaymentProcessor : BasePlugin, IPaymentMethod
    {
        #region Fields

        private readonly DpoPaymentSettings _dpoPaymentSettings;
        private readonly ISettingService _settingService;
        private readonly ICurrencyService _currencyService;
        private readonly CurrencySettings _currencySettings;
        private readonly ICustomerService _customerService;
        private readonly ICountryService _countryService;
        private readonly IWebHelper _webHelper;
        private readonly ILocalizationService _localizationService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private Dictionary<string, string> dictionaryResponse;
        private ILogger defaultLogger;

        private readonly String dpoLiveUrl = "https://secure.3gdirectpay.com/API/v6/";
        private readonly String dpoLivePayUrl = "https://secure.3gdirectpay.com/payv2.php";
        #endregion

        #region Ctor

        public DpoPaymentProcessor(DpoPaymentSettings dpoPaymentSettings,
            ISettingService settingService, ICurrencyService currencyService,
            CurrencySettings currencySettings, ICustomerService customerService, ICountryService countryService,
            IWebHelper webHelper,
            ILocalizationService localizationService,
            IHttpContextAccessor httpContextAccessor,
            ILogger logger
            )
        {
            this._dpoPaymentSettings = dpoPaymentSettings;
            this._settingService = settingService;
            this._currencyService = currencyService;
            this._countryService = countryService;
            this._customerService = customerService;
            this._currencySettings = currencySettings;
            this._webHelper = webHelper;
            this._localizationService = localizationService;
            this._httpContextAccessor = httpContextAccessor;
            this.defaultLogger = logger;
        }

        #endregion

        #region Utilities

        #endregion

        #region Methods

        /// <summary>
        /// Process a payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.NewPaymentStatus = PaymentStatus.Pending;
            return Task.FromResult(result);
        }

        //public void PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        //{
        //    this.defaultLogger.Information("Calling async");
        //    await PostProcessPaymentAsync(postProcessPaymentRequest);
        //    this.defaultLogger.Information("After async");
        //}

        /// <summary>
        /// Post process payment (used by payment gateways that require redirecting to a third-party URL)
        /// </summary>
        /// <param name="postProcessPaymentRequest">Payment info required for an order processing</param>
        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var orderTotal = Math.Round(postProcessPaymentRequest.Order.OrderTotal, 2);
            var initiated = false;

            using (var client = new WebClient())
            {
                var initiateData = new NameValueCollection();
                initiateData["COMPANY_TOKEN"] = _dpoPaymentSettings.CompanyToken;
                initiateData["SERVICE_TYPE"] = _dpoPaymentSettings.ServiceType;
                var dpoUrl = dpoLiveUrl;
                var dpoPayUrl = dpoLivePayUrl;
                initiateData["REFERENCE"] = postProcessPaymentRequest.Order.Id.ToString();
                initiateData["AMOUNT"] = (Convert.ToDouble(orderTotal)).ToString();
                initiateData["CURRENCY"] = (await _currencyService.GetCurrencyByIdAsync(_currencySettings.PrimaryStoreCurrencyId)).CurrencyCode;

                var storeLocation = _webHelper.GetStoreLocation(false);
                if (_dpoPaymentSettings.UseSSL)
                {
                    storeLocation = storeLocation.Replace("http://", "https://");
                }
                initiateData["RETURN_URL"] = storeLocation + "Plugins/PaymentDpo/DpoReturnHandler?pgnopcommerce=" + postProcessPaymentRequest.Order.Id.ToString();
                initiateData["SERVICE_DATE"] = String.Format("{0:yyyy/MM/dd HH:mm}", DateTime.Now).ToString();
                initiateData["LOCALE"] = "en-za";

                var threeLetterIsoCode = "";

                var customer = await _customerService.GetCustomerByIdAsync(postProcessPaymentRequest.Order.CustomerId);
                var billingEmail = "";
                var customerFirstName = "";
                var customerLastName = "";
                var customerAddress = "";
                var customerCity = "";
                var customerPhone = "";
                var customerDialCode = "";
                var customerZip = "1234";
                if (customer != null)
                {
                    billingEmail = customer.Email;
                    var billingAddress = await _customerService.GetCustomerBillingAddressAsync(customer);

                    if (billingAddress != null)
                    {
                        billingEmail = billingAddress.Email;
                        customerAddress = billingAddress.Address1;
                        customerCity = billingAddress.City;
                        customerFirstName = billingAddress.FirstName;
                        customerLastName = billingAddress.LastName;
                        customerPhone = billingAddress.PhoneNumber;
                        customerZip = billingAddress.ZipPostalCode;

                        var country = await _countryService.GetCountryByAddressAsync(billingAddress);
                        if (country != null && !string.IsNullOrWhiteSpace(country.TwoLetterIsoCode))
                        {
                            threeLetterIsoCode = country.TwoLetterIsoCode;
                            customerDialCode = country.TwoLetterIsoCode;
                        }
                    }
                }

                initiateData["COUNTRY"] = threeLetterIsoCode;
                initiateData["EMAIL"] = billingEmail;
                initiateData["customerFirstName"] = customerFirstName;
                initiateData["customerLastName"] = customerLastName;
                initiateData["customerAddress"] = customerAddress;
                initiateData["customerCity"] = customerCity;
                initiateData["customerDialCode"] = customerDialCode;
                initiateData["customerPhone"] = customerPhone;
                initiateData["customerZip"] = customerZip;
                initiateData["USER1"] = postProcessPaymentRequest.Order.Id.ToString();
                initiateData["USER3"] = "nopcommerce-v4.4.0";

                var createXML = new StringBuilder();
                createXML.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
                createXML.Append("<API3G>");
                createXML.Append($"<CompanyToken>{initiateData["COMPANY_TOKEN"]}</CompanyToken>");
                createXML.Append("<Request>createToken</Request>");
                createXML.Append("<Transaction>");
                createXML.Append($"<PaymentAmount>{initiateData["AMOUNT"]}</PaymentAmount>");
                createXML.Append($"<PaymentCurrency>{initiateData["CURRENCY"]}</PaymentCurrency>");
                createXML.Append($"<CompanyRef>{initiateData["REFERENCE"]}</CompanyRef>");
                createXML.Append($"<RedirectURL>{initiateData["RETURN_URL"]}</RedirectURL>");
                createXML.Append($"<BackURL>{initiateData["RETURN_URL"]}</BackURL>");
                createXML.Append($"<customerEmail>{initiateData["EMAIL"]}</customerEmail>");
                createXML.Append($"<customerFirstName>{initiateData["customerFirstName"]}</customerFirstName>");
                createXML.Append($"<customerLastName>{initiateData["customerLastName"]}</customerLastName>");
                createXML.Append($"<customerAddress>{initiateData["customerAddress"]}</customerAddress>");
                createXML.Append($"<customerCity>{initiateData["customerCity"]}</customerCity>");
                createXML.Append($"<customerCountry>{initiateData["COUNTRY"]}</customerCountry>");
                createXML.Append($"<customerDialCode>{initiateData["customerDialCode"]}</customerDialCode>");
                createXML.Append($"<customerPhone>{initiateData["customerPhone"]}</customerPhone>");
                createXML.Append($"<customerZip>{initiateData["customerZip"]}</customerZip>");
                createXML.Append("</Transaction>");
                createXML.Append("<Services><Service>");
                createXML.Append($"<ServiceType>{initiateData["SERVICE_TYPE"]}</ServiceType>");
                createXML.Append($"<ServiceDescription>{initiateData["REFERENCE"]}</ServiceDescription>");
                createXML.Append($"<ServiceDate>{initiateData["SERVICE_DATE"]}</ServiceDate>");
                createXML.Append("</Service></Services></API3G>");

                string result = "";
                string transToken = "";

                var cnt = 0;
                while (!initiated && cnt < 5)
                {
                    var initiateResponse = client.UploadString(dpoUrl, "POST", createXML.ToString());
                    await defaultLogger.InformationAsync("Initiate response: " + initiateResponse + " cnt=" + cnt);
                    var textReader = new StringReader(initiateResponse);
                    var response = new XPathDocument(textReader);

                    var navigator = response.CreateNavigator();
                    result = navigator.SelectSingleNode("//Result").Value;

                    if (result == "000")
                    {
                        transToken = navigator.SelectSingleNode("//TransToken").Value;
                        initiated = true;
                    }


                    cnt++;
                }

                // Redirect to payment portal
                if (initiated)
                {
                    _webHelper.IsPostBeingDone = false;
                    try
                    {
                        await defaultLogger.InformationAsync("Is initiated");
                        var Url = dpoPayUrl + $"?ID={transToken}";

                        // Synchronous operations disabled by default in DotnetCore >= 3.0
                        var feat = _httpContextAccessor.HttpContext.Features.Get<IHttpBodyControlFeature>();
                        if (feat != null)
                        {
                            feat.AllowSynchronousIO = true;
                        }

                        var response = _httpContextAccessor.HttpContext.Response;
                        response.ContentType = "text/html; charset=utf-8";
                        response.Body.Flush();
                        response.Redirect(Url);
                    }
                    catch (Exception e)
                    {
                        await defaultLogger.ErrorAsync("Failed to POST: " + e.Message);
                    }
                }
                else
                {
                    await defaultLogger.ErrorAsync("Failed to get valid initiate response after 5 attempts");
                }
            }
        }

        /// <summary>
        /// Returns a value indicating whether payment method should be hidden during checkout
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>true - hide; false - display.</returns>
        public Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            //you can put any logic here
            //for example, hide this payment method if all products in the cart are downloadable
            //or hide this payment method if current customer is from certain country
            return Task.FromResult(false);
        }

        /// <summary>
        /// Gets additional handling fee
        /// </summary>
        /// <param name="cart">Shoping cart</param>
        /// <returns>Additional handling fee</returns>
        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return 0.00M;
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>Capture payment result</returns>
        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            var result = new CapturePaymentResult();
            result.AddError("Capture method not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            var result = new RefundPaymentResult();
            result.AddError("Refund method not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            var result = new VoidPaymentResult();
            result.AddError("Void method not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>Process payment result</returns>
        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            var result = new ProcessPaymentResult();
            result.AddError("Recurring payment not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>Result</returns>
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            var result = new CancelRecurringPaymentResult();
            result.AddError("Recurring payment not supported");
            return Task.FromResult(result);
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>Result</returns>
        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            if (order == null)
                throw new ArgumentNullException("order");

            //let's ensure that at least 5 seconds passed after order is placed
            //P.S. there's no any particular reason for that. we just do it
            if ((DateTime.UtcNow - order.CreatedOnUtc).TotalSeconds < 5)
                return Task.FromResult(false);

            return Task.FromResult(true);
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>List of validating errors</returns>
        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>Payment info holder</returns>
        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        /// <summary>
        /// Gets a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <param name="viewComponentName">View component name</param>
        public void GetPublicViewComponent(out string viewComponentName)
        {
            viewComponentName = "PaymentDpo";
        }

        /// <summary>
        /// Gets a name of a view component for displaying plugin in public store ("payment info" checkout step)
        /// </summary>
        /// <returns>View component name</returns>
        public string GetPublicViewComponentName()
        {
            return "PaymentDpo";
        }

        /// <summary>
        /// Gets a route for provider configuration
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetConfigurationRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "Configure";
            controllerName = "PaymentDpo";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.Dpo.Controllers" }, { "area", null } };
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentDpo/Configure";
        }

        /// <summary>
        /// Gets a route for payment info
        /// </summary>
        /// <param name="actionName">Action name</param>
        /// <param name="controllerName">Controller name</param>
        /// <param name="routeValues">Route values</param>
        public void GetPaymentInfoRoute(out string actionName, out string controllerName, out RouteValueDictionary routeValues)
        {
            actionName = "PaymentInfo";
            controllerName = "PaymentDpo";
            routeValues = new RouteValueDictionary { { "Namespaces", "Nop.Plugin.Payments.Dpo.Controllers" }, { "area", null } };
        }

        public Type GetControllerType()
        {
            return typeof(PaymentDpoController);
        }

        public override async Task InstallAsync()
        {
            //settings
            var settings = new DpoPaymentSettings
            {
                CompanyToken = "9F416C11-127B-4DE2-AC7F-D5710E4C5E0A",
                ServiceType = "3854",
                UseSSL = false,
            };
            await _settingService.SaveSettingAsync(settings);

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Dpo.Fields.RedirectionTip", "You will be redirected to the DPO Group to complete the order.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Dpo.Fields.CompanyToken", "Company Token");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Dpo.Fields.CompanyToken.Hint", "Enter your Company Token.");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Dpo.Fields.ServiceType", "Service Type");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Dpo.Fields.ServiceType.Hint", "Enter Service Type");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Dpo.PaymentMethodDescription", "Pay by Credit/Debit Card");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Dpo.Fields.UseSSL", "Use SSL for Store");
            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Dpo.Fields.UseSSL.Hint", "Enforce use of SSL for Store.");

            await _localizationService.AddOrUpdateLocaleResourceAsync("Plugins.Payments.Dpo.Instructions", @"<p>

<b>Open an Account:</b>
                <br />
                <br />
                1.	You need an account with DPO to accept online payments. Register a new
                account with DPO by completing the online registration form at
                <a href=""https://dpogroup.com/"">https://dpogroup.com/</a>
                <br />
                <br />
                2.  One of our sales agents will contact you to complete the registration process.
                <br />
                3.  You will be provided with your DPO Credentials required to set up your Store.
                <br /></p>");


            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<DpoPaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Dpo.Fields.RedirectionTip");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Dpo.Fields.CompanyToken");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Dpo.Fields.CompanyToken.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Dpo.Fields.ServiceType");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Dpo.Fields.ServiceType.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Dpo.Fields.UseSSL");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Dpo.Fields.UseSSL.Hint");
            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Dpo.Fields.UseSSL.Hint2");

            await _localizationService.DeleteLocaleResourceAsync("Plugins.Payments.Dpo.Instructions");

            await base.UninstallAsync();
        }

        /// <summary>
        /// Gets a payment method description that will be displayed on checkout pages in the public store
        /// </summary>
        /// <returns>A task that represents the asynchronous operation</returns>
        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            //return description of this payment method to be display on "payment method" checkout step. good practice is to make it localizable
            //for example, for a redirection payment method, description may be like this: "You will be redirected to the DPO Group to complete the payment"
            return await _localizationService.GetResourceAsync("Plugins.Payments.Dpo.PaymentMethodDescription");
        }

        #endregion

        #region Properties
        /// <summary>
        /// Gets a value indicating whether capture is supported
        /// </summary>
        public bool SupportCapture
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether partial refund is supported
        /// </summary>
        public bool SupportPartiallyRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether refund is supported
        /// </summary>
        public bool SupportRefund
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether void is supported
        /// </summary>
        public bool SupportVoid
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a recurring payment type of payment method
        /// </summary>
        public RecurringPaymentType RecurringPaymentType
        {
            get
            {
                return RecurringPaymentType.NotSupported;
            }
        }

        /// <summary>
        /// Gets a payment method type
        /// </summary>
        public PaymentMethodType PaymentMethodType
        {
            get
            {
                return PaymentMethodType.Redirection;
            }
        }

        /// <summary>
        /// Gets a value indicating whether we should display a payment information page for this plugin
        /// </summary>
        public bool SkipPaymentInfo
        {
            get
            {
                return false;
            }
        }

        public string X2 { get; private set; }

        #endregion
    }
}
