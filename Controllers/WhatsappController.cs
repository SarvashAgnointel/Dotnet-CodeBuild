using DBAccess;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Data;
using TravelAd_Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static TravelAd_Api.Models.WhatsappModel;
using Microsoft.Extensions.Logging;
using log4net;
using System.Net.Http.Headers;
using static TravelAd_Api.Models.AdvertiserAccountModel;

namespace TravelAd_Api.Controllers
{
    [Route("[controller]/api/[action]")]
    [ApiController]
    [EnableCors("AllowSpecificOrigin")]
    public class WhatsappController : Controller
    {

        private readonly IConfiguration _configuration;
        private readonly IDbHandler _dbHandler;
        private readonly ILogger<WhatsappController> _logger;
        //private static readonly ILog Log = LogManager.GetLogger(typeof(AdvertiserAccountController));



        public WhatsappController(IConfiguration configuration, IDbHandler dbHandler, ILogger<WhatsappController> logger)
        {
            _configuration = configuration;
            _dbHandler = dbHandler;
            _logger = logger;
        }

            private string DtToJSON(DataTable table)
        {
            string jsonString = JsonConvert.SerializeObject(table);
            return jsonString;
        }

        private WhatsappAccountDetails GetWhatsappAccountDetails(string emailId)
        {
            string procedure = "GetWhatsappAccountDetails";

            var parameters = new Dictionary<string, object>
    {
        { "@EmailId", emailId }
    };
            DataTable campaignDetailsById = _dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

            if (campaignDetailsById.Rows.Count == 0)
            {
                return null;
            }

            return new WhatsappAccountDetails
            {
                WabaId = campaignDetailsById.Rows[0]["wabaId"].ToString(),
                PhoneId = campaignDetailsById.Rows[0]["phoneId"].ToString(),
                AccessToken = campaignDetailsById.Rows[0]["accessToken"].ToString()
            };
        }


        //Webhook connection will be verified here
        [HttpGet]
        public IActionResult Webhook([FromQuery(Name = "hub.mode")] string hub_mode, [FromQuery(Name = "hub.challenge")] int hub_challenge, [FromQuery(Name = "hub.verify_token")] string hub_verify_token)
        {
            string VERIFY_TOKEN = _configuration["WebHookVerifyToken"]; // Replace with your token
            if (hub_mode == "subscribe" && hub_verify_token == VERIFY_TOKEN)
            {
                return Ok(hub_challenge); // Echo back the challenge token
            }
            return Forbid();
        }



        //Webhook events will be received here below
        [HttpPost]
        public IActionResult Webhook([FromBody] JsonElement payload)
        {
            if (payload.TryGetProperty("object", out var objectProperty) &&
                objectProperty.GetString() == "whatsapp_business_account")
            {
                var entry = payload.GetProperty("entry")[0];
                var changes = entry.GetProperty("changes");

                foreach (var change in changes.EnumerateArray())
                {
                    var field = change.GetProperty("field").GetString();

                    switch (field)
                    {
                        case "messages":
                            HandleMessages(change.GetProperty("value"));
                            break;

                        case "message_template_status_update":
                            HandleStatuses(change.GetProperty("value"));
                            break;

                        default:
                            Console.WriteLine($"Unhandled field: {field}");
                            break;
                    }
                }
            }

            return Ok();
        }





        //Handle all webhook events here below

        private void HandleMessages(JsonElement value)
        {
            if (value.TryGetProperty("messages", out var messages))
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var from = message.GetProperty("from").GetString();
                    var text = message.GetProperty("text").GetProperty("body").GetString();
                    Console.WriteLine($"Received message from {from}: {text}");
                    _logger.LogInformation($"Received message from {from}: {text}");
                }
            }
        }

        private void HandleStatuses(JsonElement value)
        {
            if (value.TryGetProperty("message_template_status_update", out var statuses))
            {
                foreach (var status in statuses.EnumerateArray())
                {
                    var id = status.GetProperty("id").GetString();
                    var statusValue = status.GetProperty("status").GetString();
                    Console.WriteLine($"Status update for message {id}: {statusValue}");
                    _logger.LogInformation($"Status update for message {id}: {statusValue}");
                }
            }
        }

        



    }
}



