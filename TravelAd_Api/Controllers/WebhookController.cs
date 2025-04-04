
using DBAccess;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stripe;
using Stripe.Checkout;
using System;
using System.Data;
using TravelAd_Api.DataLogic;
using static TravelAd_Api.Models.AdvertiserAccountModel;

namespace TravelAd_Api.Controllers
{
    [ApiController]
    [Route("api/webhook")]
    public class WebhookController : ControllerBase
    {
        private readonly IDbHandler _dbHandler;
        private readonly IConfiguration _configuration;
        private readonly Stripesettings _stripeSettings;
        private readonly string StripeSecret = "whsec_qhn2siA5Pv5Zrq8zi55tnb543TJT7KQR";
        public WebhookController(IConfiguration configuration, IDbHandler dbHandler, IOptions<Stripesettings> stripeSettings)
        {
            _configuration = configuration;
            _dbHandler = dbHandler;
            _stripeSettings = stripeSettings.Value;
        }


        [HttpPost("stripe")]
        public async Task<IActionResult> HandleWebhook([FromServices] IDbHandler dbHandler)
        {
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
            // Read the webhook payload
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            Console.WriteLine($"Received webhook payload: {json}");
            var paymentDetails = new PaymentDetails();

            try
            {
                // Validate the webhook signature
                var stripeSignature = Request.Headers["Stripe-Signature"];
                Console.WriteLine($"Stripe-Signature: {stripeSignature}");
                var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, StripeSecret);

                // Initialize a container to consolidate data

                switch (stripeEvent.Type)
                {
                    case "payment_intent.succeeded":
                        await ProcessPaymentIntentSucceeded(stripeEvent, paymentDetails);
                        break;

                    case "checkout.session.completed":
                        await ProcessCheckoutSessionCompleted(dbHandler, stripeEvent, paymentDetails);
                        break;

                    default:
                        Console.WriteLine($"Unhandled event type: {stripeEvent.Type}");
                        break;
                }
                // Insert consolidated data into the database


                // Acknowledge receipt of the webhook

            }
            catch (StripeException ex)
            {
                Console.WriteLine($"Stripe webhook error: {ex.Message}");
                return BadRequest(new { error = "Invalid Stripe webhook signature." });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"General error handling webhook: {ex.Message}");
                return BadRequest(new { error = "An error occurred while processing the webhook." });
            }

            return Ok();
        }

        private async Task ProcessPaymentIntentSucceeded(Event stripeEvent, PaymentDetails paymentDetails)
        {
            var paymentIntent = stripeEvent.Data.Object as PaymentIntent;

            if (paymentIntent != null)
            {
                paymentDetails.PaymentId = paymentIntent.Id;
                paymentDetails.Amount = paymentIntent.Amount / 100.0m;
                paymentDetails.Status = "Succeeded";
                var chargeId = paymentIntent.LatestChargeId;

                var stripeClient = new Stripe.StripeClient(StripeConfiguration.ApiKey);
                var chargeService = new ChargeService(stripeClient);
                var charge = await chargeService.GetAsync(chargeId);
                var cardDetails = charge.PaymentMethodDetails?.Card;

                var currency = charge.Currency;
                var receiptUrl = charge.ReceiptUrl;
                var email = charge.BillingDetails.Email;
                paymentDetails.FundingType = cardDetails.Funding;

                paymentDetails.ReceiptUrl = receiptUrl;
                paymentDetails.Email = email;
                paymentDetails.Currency = currency;
            }
        }

        private async Task ProcessCheckoutSessionCompleted(IDbHandler dbHandler, Event stripeEvent, PaymentDetails paymentDetails)
        {
            var session = stripeEvent.Data.Object as Session;
            Console.WriteLine($"Session Details: {JsonConvert.SerializeObject(session)}");
            Console.WriteLine(JsonConvert.SerializeObject(session.Invoice));
            string paymentIntentId = null;
            string invoice = null;
            string invoicePdfUrl = null;
            if (session != null)
            {
                // Extract required details
                var invoiceId = session.Invoice;
                var priceId = session.Metadata?["price_id"];
                var plan = session.Metadata?["product_name"];
                var userid = session.Metadata?["userid"];
                var amountTotal = session.AmountTotal.HasValue ? (decimal)session.AmountTotal.Value / 100 : 0; // Stripe uses smallest currency unit (e.g., cents)
                var currency = session.Currency;
                var email = session.CustomerDetails?.Email;
                var fundType = session.Mode; // Assuming 'Mode' is used for 'fund type'

                // Attempt to retrieve PaymentIntentId from the session
                paymentIntentId = session.PaymentIntentId;

                Console.WriteLine(invoiceId);
                // If PaymentIntentId is null, try extracting from raw JSON
                if (string.IsNullOrEmpty(paymentIntentId) || string.IsNullOrEmpty(invoice))
                {
                    try
                    {
                        // Serialize the entire Stripe event to JSON
                        var rawEventJson = JsonConvert.SerializeObject(stripeEvent);

                        // Deserialize the raw JSON into a dictionary
                        var stripeEventData = JsonConvert.DeserializeObject<Dictionary<string, object>>(rawEventJson);

                        if (stripeEventData.ContainsKey("data") &&
                            stripeEventData["data"] is JObject dataObject &&
                            dataObject["object"] is JObject objectData &&
                            objectData.ContainsKey("payment_intent") && objectData.ContainsKey("invoice"))
                        {
                            paymentIntentId = objectData["payment_intent"]?.ToString();
                            invoice = objectData["invoice"]?.ToString();
                            //  invoicePdfUrl = invoice.InvoicePdf;

                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error deserializing raw Stripe event data: {ex.Message}");
                    }
                }
                var invoiceService = new InvoiceService();

                Console.WriteLine($"Price ID: {priceId}");
                Console.WriteLine($"Amount: {amountTotal} {currency}");
                Console.WriteLine($"Email: {email}");
                Console.WriteLine($"Fund Type: {fundType}");
                Console.WriteLine($"Payment Intent ID: {paymentIntentId}");

                // Populate paymentDetails with extracted values
                if (!string.IsNullOrEmpty(priceId))
                {
                    paymentDetails.ProductName = priceId;
                }

                paymentDetails.PaymentId = paymentIntentId;
                paymentDetails.Amount = amountTotal;
                paymentDetails.Currency = currency;
                paymentDetails.Email = email;
                paymentDetails.FundingType = fundType;
                paymentDetails.Status = "Succedded";
                paymentDetails.ReceiptUrl = invoice;
                paymentDetails.plan = plan;
                paymentDetails.userid = userid;
                if (!string.IsNullOrEmpty(paymentDetails.PaymentId))
                {
                    await SaveConsolidatedPaymentDetails(dbHandler, paymentDetails);
                }
            }
            else
            {
                Console.WriteLine("Session is null.");
            }
        }



        private async Task SaveConsolidatedPaymentDetails(IDbHandler dbHandler, PaymentDetails paymentDetails)
        {
            try
            {
                string procedureName = "InsertPaymentDetails";
                var parameters = new Dictionary<string, object>
        {
            { "@PaymentId", paymentDetails.PaymentId },
            { "@Amount", paymentDetails.Amount },
            { "@Status", paymentDetails.Status },
            { "@ReceiptUrl", paymentDetails.ReceiptUrl },
            { "@Email", paymentDetails.Email },
            { "@Currency", paymentDetails.Currency },
            { "@productname", paymentDetails.ProductName },
            { "@fund_type", paymentDetails.FundingType },
            {"@plan_name",paymentDetails.plan },
            {"@userid",paymentDetails.userid }

        };

                await Task.Run(() => dbHandler.ExecuteNonQuery(procedureName, parameters, CommandType.StoredProcedure));
                Console.WriteLine("Payment details saved successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving payment details: {ex.Message}");
            }
        }



        private class PaymentDetails
        {
            public string PaymentId { get; set; }
            public decimal Amount { get; set; }
            public string Status { get; set; }
            public string ReceiptUrl { get; set; }
            public string Email { get; set; }
            public string Currency { get; set; }
            public string ProductName { get; set; }
            public DateTime paymentdate { get; set; }
            public string FundingType { get; set; }
            public string plan { get; set; }
            public string userid { get; set; }
        }
        public class ProductDetails
        {
            public string PriceId { get; set; }
            public string ProductName { get; set; }
            //public string Description { get; set; }
            //public decimal Price { get; set; }
        }
    }

}