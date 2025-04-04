using DBAccess;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Data;
using TravelAd_Api.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using static TravelAd_Api.Models.AdvertiserAccountModel;
using Microsoft.Data.SqlClient;
using static TravelAd_Api.Models.ApiModel;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using Azure.Core;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using log4net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using OfficeOpenXml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using TravelAd_Api.DataLogic;
using Stripe;
using Stripe.Checkout;
using Microsoft.Extensions.Options;
using static TravelAd_Api.Models.AdminModel;
using OfficeOpenXml.FormulaParsing.Excel.Functions.DateTime;



namespace TravelAd_Api.Controllers
{
    
    [Route("[controller]/api/[action]")]
    [ApiController]
    [EnableCors("AllowSpecificOrigin")]
    public class AdvertiserAccountController : Controller
    {

        private readonly IConfiguration _configuration;
        private readonly IDbHandler _dbHandler;
        private readonly ILogger<AdvertiserAccountController> _logger;
        private readonly Stripesettings _stripeSettings;


        public AdvertiserAccountController(IConfiguration configuration, IDbHandler dbHandler, ILogger<AdvertiserAccountController> logger, IOptions<Stripesettings> stripeSettings)
        {
            _configuration = configuration;
            _dbHandler = dbHandler;
            _logger = logger;
            _stripeSettings = stripeSettings.Value;

        }
        //private static readonly ILog Log = LogManager.GetLogger(typeof(AdvertiserAccountController));

        private string DtToJSON(DataTable table)
        {
            string jsonString = JsonConvert.SerializeObject(table);
            return jsonString;
        }


        private WhatsappAccountDetails GetWhatsappAccountDetailsByWId(int workspaceId)
        {
            string procedure = "GetWhatsappAccountDetailsById";

            var parameters = new Dictionary<string, object>
            {
                { "@WorkspaceId", workspaceId }
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
                AccessToken = _configuration["WhatsAppToken"]
            };
        }




        //__________________________________AdvertiserAccount Module----------------------------------------//

        // [Authorize]
        [HttpGet]
        public IActionResult GetChannelList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                _logger.LogInformation("GetChannelList endpoint called.");
                
                string getChannelList = "GetChannelList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getChannelList);

                DataTable ChannelList = dbHandler.ExecuteDataTable(getChannelList);

                if (ChannelList == null || ChannelList.Rows.Count == 0)
                {
                    _logger.LogWarning("No channels found in the database.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Channel found"
                    });
                }

                var ChannelListData = ChannelList.AsEnumerable().Select(row => new
                {
                    channel_id = row.Field<int>("channel_id"),
                    channel_name = row.Field<string>("channel_type"),
                    channel_description = row.Field<string>("channel_description")
                }).ToList();

                _logger.LogInformation("Channel retrieved successfully . Channel Requests :{ChannelListData}", ChannelListData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Channel retrieved successfully",
                    ChannelList = ChannelListData
            });
                
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the cahnnel list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the cahnnel list: {ex.Message}"
                    
                });
            }
        }

        [HttpGet]
        public IActionResult GetContentList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetContentList";
                _logger.LogInformation("Executing stored procedure: {procedure}", procedure);


                DataTable contentList = dbHandler.ExecuteDataTable(procedure);

                if (contentList == null || contentList.Rows.Count == 0)
                {
                    _logger.LogInformation("No content types found in the database.");

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No content type found"
                    });
                }


                var contentListData = contentList.AsEnumerable().Select(row => new
                {
                    content_id = row.Field<int>("content_id"),
                    content_name = row.Field<string>("content_name")
                }).ToList();
                _logger.LogInformation("Successfully retrieved content types from the database. Requests are {contentListData}" , contentListData);
                
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Content types retrieved successfully",
                    ContentList = contentListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the content types list.");

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the content types list: {ex.Message}"
                });
            }
        }



        private static bool? ConvertToNullableBool(object value)
        {
            if (value == DBNull.Value || value == null)
                return null;

            if (value is bool boolValue)
                return boolValue;

            if (bool.TryParse(value.ToString(), out bool parsedBool))
                return parsedBool;

            return null; // Default fallback
        }



        [HttpPost]
        public object CreateCampaign(TravelAd_Api.Models.AdvertiserAccountModel.CreateCampaign request, [FromServices] IDbHandler dbHandler)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred.",
                campaign_id = (object)null
            };

            try
            {
                string insertQuery = "InsertCampaignDetails";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", insertQuery);

                // Ensure proper JSON string format
                string targetCountry = string.IsNullOrWhiteSpace(request.TargetCountry) ? "[]" : request.TargetCountry;
                string roamingCountry = string.IsNullOrWhiteSpace(request.RoamingCountry) ? "[]" : request.RoamingCountry;

                var parameters = new Dictionary<string, object>
 {
     { "@campaign_name", request.CampaignName },
     { "@campaign_budget", request.CampaignBudget ?? (object)DBNull.Value },
     { "@channel_type", request.ChannelType },
     { "@target_country", targetCountry },
     { "@roaming_country", roamingCountry },
     { "@start_date_time", request.StartDateTime ?? (object)DBNull.Value },
     { "@end_date_time", request.EndDateTime ?? (object)DBNull.Value },
     { "@status", request.Status ?? (object)DBNull.Value },
     { "@template_name", request.TemplateName ?? (object)DBNull.Value },
     { "@created_by", request.CreatedBy },
     { "@created_date", request.CreatedDate ?? DateTime.UtcNow },
     { "@updated_by", request.UpdatedBy },
     { "@updated_date", request.UpdatedDate ?? DateTime.UtcNow },
     { "@workspace_id", request.WorkspaceId },
     { "@list_id", request.ListId ?? (object)DBNull.Value },
     { "@device_id", request.DeviceId ?? (object)DBNull.Value },
     { "@delivered", request.Delivered ?? (object)DBNull.Value },
     { "@read_campaign", request.ReadCampaign ?? (object)DBNull.Value },
     { "@c_t_r", request.CTR ?? (object)DBNull.Value },
     { "@delivery_rate", request.DeliveryRate ?? (object)DBNull.Value },
     { "@button_click", request.ButtonClick ?? (object)DBNull.Value },
     { "@age", request.Age ?? (object)DBNull.Value },
     { "@gender", request.Gender ?? (object)DBNull.Value },
     { "@income_level", request.IncomeLevel ?? (object)DBNull.Value },
     { "@location", request.Location ?? (object)DBNull.Value },
     { "@interests", request.Interests ?? (object)DBNull.Value },
     { "@behaviours", request.Behaviours ?? (object)DBNull.Value },
     { "@os_device", request.OSDevice ?? (object)DBNull.Value },
     { "@f_campaign_budget", request.FCampaignBudget ?? (object)DBNull.Value },
     { "@f_start_date_time", request.FStartDateTime ?? (object)DBNull.Value },
     { "@f_end_date_time", request.FEndDateTime ?? (object)DBNull.Value },
     { "@is_admin_approved", request.IsAdminApproved ?? (object)DBNull.Value },
     { "@is_operator_approved", request.IsOperatorApproved ?? (object)DBNull.Value },
     { "@budget_and_schedule", request.BudgetAndSchedule ?? (object)DBNull.Value },
     { "@message_frequency", request.MessageFrequency ?? (object)DBNull.Value },
     { "@sequential_delivery", request.SequentialDelivery ?? (object)DBNull.Value },
     { "@prevent_duplicate_messages", request.PreventDuplicateMessages ?? (object)DBNull.Value },
     { "@daily_recipient_limit", request.DailyRecipientLimit ?? (object)DBNull.Value },
     { "@delivery_start_time", request.DeliveryStartTime ?? (object)DBNull.Value },
     { "@delivery_end_time", request.DeliveryEndTime ?? (object)DBNull.Value },
     { "@sms_number", request.SmsNumber ?? (object)DBNull.Value },
     { "@smpp_id", request.SMPP_id },
     { "@sms_connection_id", request.SMPP_ConnectionID},
     { "@campaign_existing_budget" , request.campaign_existing_budget },
     { "@currency_symbol", "" + request.currency_symbol + "" },
     { "@currency_name" , request.currency_name },
                    {"@country_name" , request.country_name }

 };

                object result = dbHandler.ExecuteScalar(insertQuery, parameters, CommandType.StoredProcedure);

                response = new { Status = "Success", Status_Description = "Campaign inserted", campaign_id = result };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error: {ex.Message}");
            }

            return response;
        }

        [HttpPut]
        public object UpdateCampaign(TravelAd_Api.Models.AdvertiserAccountModel.CreateCampaign request, [FromServices] IDbHandler dbHandler)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred while updating the template."
            };

            try
            {
                DataTable dtmain = new DataTable();
                dtmain.Columns.Add("Status");
                dtmain.Columns.Add("Status_Description");

                string updateQuery = "UpdateCampaignDetails";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", updateQuery);

                var parameters = new Dictionary<string, object>
     {
            {"@CampaignId", request.CampaignId },
            {"@CampaignName", request.CampaignName },
            {"@CampaignBudget", request.CampaignBudget ?? (object)DBNull.Value },
            {"@ChannelType", request.ChannelType },
            {"@TargetCountry", request.TargetCountry ?? (object)DBNull.Value },
            {"@RoamingCountry", request.RoamingCountry ?? (object)DBNull.Value },
            {"@StartDateTime", request.StartDateTime ?? (object)DBNull.Value },
            {"@EndDateTime", request.EndDateTime ?? (object)DBNull.Value },
            {"@Status", request.Status ?? (object)DBNull.Value },
            {"@TemplateName", request.TemplateName ?? (object)DBNull.Value },
            {"@UpdatedBy", request.UpdatedBy },
            {"@UpdateDate", DateTime.UtcNow },
            {"@WorkspaceId", request.WorkspaceId },
            {"@ListId", request.ListId ?? (object)DBNull.Value },
            {"@DeviceId", request.DeviceId ?? (object)DBNull.Value },
            {"@Delivered", request.Delivered ?? (object)DBNull.Value },
            {"@ReadCampaign", request.ReadCampaign ?? (object)DBNull.Value },
            {"@CTR", request.CTR ?? (object)DBNull.Value },
            {"@DeliveryRate", request.DeliveryRate ?? (object)DBNull.Value },
            {"@ButtonClick", request.ButtonClick ?? (object)DBNull.Value },
            {"@Age", request.Age ?? (object)DBNull.Value },
            {"@Gender", request.Gender ?? (object)DBNull.Value },
            {"@IncomeLevel", request.IncomeLevel ?? (object)DBNull.Value },
            {"@Location", request.Location ?? (object)DBNull.Value },
            {"@Interests", request.Interests ?? (object)DBNull.Value },
            {"@Behaviours", request.Behaviours ?? (object)DBNull.Value },
            {"@OsDevice", request.OSDevice ?? (object)DBNull.Value },
            {"@FCampaignBudget", request.FCampaignBudget ?? (object)DBNull.Value },
            {"@FStartDateTime", request.FStartDateTime ?? (object)DBNull.Value },
            {"@FEndDateTime", request.FEndDateTime ?? (object)DBNull.Value },
            {"@IsAdminApproved", request.IsAdminApproved ?? (object)DBNull.Value },
            {"@IsOperatorApproved", request.IsOperatorApproved ?? (object)DBNull.Value },
            {"@BudgetAndSchedule", request.BudgetAndSchedule ?? (object)DBNull.Value },
            {"@MessageFrequency", request.MessageFrequency ?? (object)DBNull.Value },
            {"@SequentialDelivery", request.SequentialDelivery ?? (object)DBNull.Value },
            {"@PreventDuplicateMessages", request.PreventDuplicateMessages ?? (object)DBNull.Value },
            {"@DailyRecipientLimit", request.DailyRecipientLimit ?? (object)DBNull.Value },
            {"@DeliveryStartTime", request.DeliveryStartTime ?? (object)DBNull.Value },
            {"@DeliveryEndTime", request.DeliveryEndTime ?? (object)DBNull.Value },
            {"@SmsNumber", request.SmsNumber ?? (object)DBNull.Value }
        };




                int rowsAffected = dbHandler.ExecuteNonQuery(updateQuery, parameters, CommandType.StoredProcedure);

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Campaign updated successfully.");
                    response = new
                    {
                        Status = "Success",
                        Status_Description = $"Campaign updated successfully."
                    };
                }
                else
                {
                    _logger.LogError("Update failed. No records were updated.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Update failed. No records were updated."
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                _logger.LogError($"An exception occurred while processing the request: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = $"An exception occurred while processing the request: {ex.Message}"
                };
            }

            return response;
        }


        [HttpGet]
        public IActionResult GetTemplateDetails([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetTemplateDetails";
                _logger.LogInformation("Executing stored procedure:", procedure);


                DataTable templateDetails = dbHandler.ExecuteDataTable(procedure);

                if (templateDetails.Rows.Count == 0)
                {
                    _logger.LogWarning("Template Not found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Template Not found"
                    });
                }

                var TemplateData = templateDetails.AsEnumerable().Select(row => new
                {
                    template_id = row.Field<int>("template_id"),
                    template_name = row.Field<string>("template_name"),
                    channel_type = row.Field<string>("channel_type"),
                    status = row.Field<string?>("status"), 
                    last_edited = row.Field<DateTime?>("last_edited"), 
                }).ToList();

                _logger.LogInformation("Templates retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Templates retrieved successfully",
                    CampaignDetails = TemplateData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the template details: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the template details: {ex.Message}"
                });
            }

        }

        
        [HttpDelete]
        public IActionResult DeleteCampaignById([FromServices] IDbHandler dbHandler, int CampaignId)
        {
            try
            {
                string deleteCampaignById = "DeleteCampaignDetail";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", deleteCampaignById);


                var parameters = new Dictionary<string, object>
        {
            { "@CampaignId", CampaignId }  
        };

                int rowsAffected = dbHandler.ExecuteNonQuery(deleteCampaignById, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("Campaign not found or could not be deleted");
                    return Ok(new
                    {
     
                        Status = "Failure",
                        Status_Description = "Campaign not found or could not be deleted"
                    });
                }


                _logger.LogInformation("Campaign deleted successfully");
                return Ok(new

                {
                    Status = "Success",
                    Status_Description = "Campaign deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while deleting the campaign: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while deleting the campaign: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetButtonType([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getButtonType = "GetButtonType";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getButtonType);


                DataTable buttonType = dbHandler.ExecuteDataTable(getButtonType);

                if (buttonType == null || buttonType.Rows.Count == 0)
                {
                    _logger.LogInformation("No Button Types are found.");

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Button Types are found"
                    });
                }

                var buttonTypeData = buttonType.AsEnumerable().Select(row => new
                {
                    button_id = row.Field<int>("button_id"),
                    button_type = row.Field<string>("button_type")
                }).ToList();

                _logger.LogInformation("ButtonType retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "ButtonType retrieved successfully",
                    ButtonType = buttonTypeData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the Button Type list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the Button Type list: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public IActionResult GetTemplatePlatform([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getTemplatePlatform = "GetTemplatePlatform";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getTemplatePlatform);

                DataTable TemplatePlatform = dbHandler.ExecuteDataTable(getTemplatePlatform);


                if (TemplatePlatform == null || TemplatePlatform.Rows.Count == 0)
                {
                    _logger.LogInformation("No Platforms found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Platforms found"
                    });
                }

                var templatePlatformData = TemplatePlatform.AsEnumerable().Select(row => new
                {
                    platform_id = row.Field<int>("platform_id"),
                    platform_name = row.Field<string>("platform_name")
                }).ToList();

                _logger.LogInformation("Template Platforms retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Template Platforms retrieved successfully",
                    TemplatePlatform = templatePlatformData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the Template Form : {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the Template Form : {ex.Message}"
                });
            }
        }

        
        [HttpGet("{workspaceId:int}")]
        public IActionResult GetTemplateList(int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetTemplateDetails";
                _logger.LogInformation($"Executing stored procedure: {procedure} with workspace_id: {workspaceId}");

                // Create parameters dictionary
                var parameters = new Dictionary<string, object>
                {
                    { "@workspace_id", workspaceId }
                };

                // Execute the stored procedure
                DataTable templateDetails = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                // Check if data is returned
                if (templateDetails.Rows.Count == 0)
                {
                    _logger.LogWarning("No templates found for the specified workspace ID");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No templates found for the specified workspace ID"
                    });
                }

                // Map the data to a list of objects
                var templateData = templateDetails.AsEnumerable().Select(row => new
                {
                    template_id = row.Field<int>("template_id"),
                    template_name = row.Field<string>("template_name"),
                    channel_type = row.Field<string>("channel_type"),
                    status = row.Field<string?>("status"),
                    last_edited = row.Field<DateTime?>("last_edited"),
                }).ToList();

                // Return the success response
                _logger.LogInformation("Templates retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Templates retrieved successfully",
                    TemplateDetails = templateData
                });
            }
            catch (Exception ex)
            {
                // Handle and log exceptions
                _logger.LogError($"An error occurred while retrieving the template details: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the template details: {ex.Message}"
                });
            }
        }

        [HttpPut]
        public object UpdateTemplate(TravelAd_Api.Models.AdvertiserAccountModel.TemplateDetailsModel template, [FromServices] IDbHandler dbHandler, int TemplateId)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred while updating the template."
            };

            try
            {
                DataTable dtmain = new DataTable();
                dtmain.Columns.Add("Status");
                dtmain.Columns.Add("Status_Description");

                string updateQuery = "UpdateTemplateDetails";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", updateQuery);

                var parameters = new Dictionary<string, object>
    {
        { "@template_id", TemplateId }, 
        { "@platform_name", template.PlatformName },
  
        { "@template_name", template.TemplateName },
        { "@template_language", template.TemplateLanguage },
        { "@template_header", template.TemplateHeader },
        { "@template_body", template.TemplateBody },
        { "@template_footer", template.TemplateFooter },
        { "@components", template.Components },
        { "@button_type", template.ButtonType },
        { "@button_text", template.ButtonText },
        { "@updated_by", template.UpdatedBy },
        { "@updated_date", DateTime.Now },  
        { "@status", template.Status },
        { "@URLType", template.URLType },
        { "@WebsiteURL", template.WebsiteURL }
    };

                int rowsAffected = dbHandler.ExecuteNonQuery(updateQuery, parameters, CommandType.StoredProcedure);

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Template updated successfully.");
                    response = new
                    {
                        Status = "Success",
                        Status_Description = $"Template updated successfully."
                    };
                }
                else
                {
                    _logger.LogError("Update failed. No records were updated.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Update failed. No records were updated."
                    };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                _logger.LogError($"An exception occurred while processing the request: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = $"An exception occurred while processing the request: {ex.Message}"
                };
            }

            return response;
        }



        [HttpPost]
        public object CreateTemplate(TravelAd_Api.Models.AdvertiserAccountModel.TemplateDetailsModel template, [FromServices] IDbHandler dbHandler)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                DataTable dtmain = new DataTable();
                dtmain.Columns.Add("Status");
                dtmain.Columns.Add("Status_Description");

                string insertQuery = "InsertTemplateDetails";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", insertQuery);

                var parameters = new Dictionary<string, object>
{
                    { "platform_name", template.PlatformName },
                    { "template_name", template.TemplateName },
                    { "template_language", template.TemplateLanguage },
                    { "template_header", template.TemplateHeader },
                    { "template_body", template.TemplateBody },
                    { "template_footer", template.TemplateFooter },
                    { "components", template.Components },
                    { "button_type", template.ButtonType },
                    { "button_text", template.ButtonText },
                    {"created_by", template.CreatedBy },
                    {"created_date", DateTime.Now },
                    {"updated_by",template.UpdatedBy },
                    {"updated_date",DateTime.Now },
                    {"status",template.Status },
                    {"URLType",template.URLType },
                    {"WebsiteURL",template.WebsiteURL },
                    {"workspace_id",template.workspace_id }


};
                object result = dbHandler.ExecuteScalar(insertQuery, parameters, CommandType.StoredProcedure);

                Console.WriteLine(result);
                _logger.LogInformation("Insert Template Details result", result);

                if (result != null)
                {
                    _logger.LogInformation($"Template inserted with ID: {result}");
                    response = new
                    {
                        Status = "Success",
                        Status_Description = $"Template inserted with ID: {result}"
                    };
                }
                else
                {
                    _logger.LogInformation("Insertion failed.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Insertion failed."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the Template list by id: {ex.Message}");
                Console.WriteLine($"Error: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = "An exception occurred while processing the request."
                };
            }

            return response;
        }


        [HttpGet]
        public IActionResult GetTemplateDetailsById([FromServices] IDbHandler dbHandler, int TemplateId)
        {
            try
            {
                string getTemplateDetailsById = "GetTemplateDetailsById";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getTemplateDetailsById);


                var Iparameters = new Dictionary<string, object>
            {
                { "@TemplateId", TemplateId }
            };

                DataTable templateDetailsById = dbHandler.ExecuteDataTable(getTemplateDetailsById, Iparameters, CommandType.StoredProcedure);

                Console.WriteLine(templateDetailsById);

                if (templateDetailsById.Rows.Count == 0)
                {
                    _logger.LogInformation("Template Not found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Template Not found"
                    });
                }

                var TemplateListByIdData = templateDetailsById.AsEnumerable().Select(row => new
                {
                    template_id = row.Field<int>("template_id"),
                    template_name = row.Field<string>("template_name"),
                    platform_name = row.Field<string>("platform_name"),
                    language_name = row.Field<string>("language_name"),

                    template_header = row.Field<string>("template_header"),
                    template_body = row.Field<string>("template_body"),
                    template_footer = row.Field<string>("template_footer"),
                    components = row.Field<string>("components"),
                    button_type = row.Field<string>("button_type"),
                    button_text = row.Field<string>("button_text"),
                    status = row.Field<string>("status"),
                    URLType = row.Field<string>("URLType"),
                    WebsiteURL = row.Field<string>("WebsiteURL"),

                }).ToList();

                _logger.LogInformation("Template retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Template retrieved successfully",
                    TemplateDetails = TemplateListByIdData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the Template list by id: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the Template list by id: {ex.Message}"
                });
            }
        }


        [HttpDelete]
        public IActionResult DeleteTemplateById([FromServices] IDbHandler dbHandler, int TemplateId)
        {
            try
            {
                string deleteTemplateById = "DeleteTemplateDetails";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", deleteTemplateById);


                var parameters = new Dictionary<string, object>
       {
           { "@TemplateId", TemplateId }
       };

                int rowsAffected = dbHandler.ExecuteNonQuery(deleteTemplateById, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("Template not found or could not be deleted");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Template not found or could not be deleted"
                    });
                }

                _logger.LogInformation("Template deleted successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Template deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while deleting the Template: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while deleting the Template: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetMultipleWorkspacesByEmail([FromServices] IDbHandler dbHandler, string EmailId)
        {
            try
            {
                string procedure = "GetMultipleWorkspacesByEmail";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);

                var parameters = new Dictionary<string, object>
    {
        { "@EmailId", EmailId }
    };
                DataTable WorkspaceList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);
                if (WorkspaceList == null || WorkspaceList.Rows.Count == 0)
                {
                    _logger.LogInformation("No workspaces found for mail");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No workspaces found for mail",
                        WorkspaceCount = 0
                    });
                }

                var WorkspaceListData = WorkspaceList.AsEnumerable().Select(row => new
                {
                    workspace_name = row.Field<string>("workspace_name"),
                    workspace_id = row.Field<int>("workspace_id"),
                    workspace_image = row.Field<string>("workspace_image"),
                    billing_country = row.Field<int>("billing_country")
                }).ToList();

                _logger.LogInformation("workspaces retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "workspaces retrieved successfully",
                    WorkspaceCount = WorkspaceList.Rows.Count,
                    WorkspaceList = WorkspaceListData

                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the workspace list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the workspace list: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetLanguageList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getLanguageList = "GetLanguageList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getLanguageList);


                DataTable languageList = dbHandler.ExecuteDataTable(getLanguageList);

                if (languageList == null || languageList.Rows.Count == 0)
                {
                    _logger.LogInformation("No languages found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No languages found"
                    });
                }

                var languageListData = languageList.AsEnumerable().Select(row => new
                {
                    language_id = row.Field<int>("language_id"),
                    language_name = row.Field<string>("language_name"),
                    language_code= row.Field<string>("language_code")
                }).ToList();

                _logger.LogInformation("Languages retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Languages retrieved successfully",
                    LanguageList = languageListData
                });
            }
            catch (Exception ex)
            {_logger.LogError($"An error occurred while retrieving the language list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the language list: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public object CreateNotificationSettings([FromServices] IDbHandler dbHandler, string EmailId)
        {
            try
            {
                string procedure = "InsertNotificationSettings";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);


                var parameters = new Dictionary<string, object>
               {
                   { "@EmailId", EmailId } 
               };

               int rowsAffected = dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);
                if (rowsAffected == 0)
                {
                    _logger.LogInformation("Notification settings for user not created");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Notification settings for user not created"
                    });

                }
                _logger.LogInformation("Notification settings created successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Notification settings created successfully"
                });
            }
            catch (Exception ex)
            {_logger.LogError($"An error occurred while creating notification settings for the user: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while creating notification settings for the user: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetNotificationSettings([FromServices] IDbHandler dbHandler, string EmailId)
        {
            try
            {
                string procedure = "GetNotificationSettings";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);


                var parameters = new Dictionary<string, object>
                    {
                        { "@EmailId", EmailId }
                    };

                DataTable NotificationSettings = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                Console.WriteLine(NotificationSettings);

                if (NotificationSettings.Rows.Count == 0)
                {_logger.LogInformation("Notification settings for user not found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Notification settings for user not found"
                    });
                }

                var NotificationSettingsData = NotificationSettings.AsEnumerable().Select(row => new

                {
                    notification_data = row.Field<string>("notification_data"),
                }).ToList();

                _logger.LogInformation("Notification data retrieved successfully");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Notification data retrieved successfully",
                    NotificationData = NotificationSettingsData
                });
            }
            catch (Exception ex)
            {_logger.LogError($"An error occurred while retrieving the notification data: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the notification data: {ex.Message}"
                });
            }
        }

        [HttpPut("UpdateNotificationSettings")]
        public object UpdateNotificationSettings([FromBody] NotificationUpdateModel model, [FromServices] IDbHandler dbHandler)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                string procedure = "UpdateNotificationSettings";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);


                var parameters = new Dictionary<string, object>
        {
            { "@EmailId", model.EmailId },
            { "@NotificationData", model.NotificationData }
        };

                int result = (int)dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);

                if (result == 1)
                {
                    _logger.LogInformation($"Notification data for email {model.EmailId} was updated successfully.");
                    response = new
                    {
                        Status = "Success",
                        Status_Description = $"Notification data for email {model.EmailId} was updated successfully."
                    };
                }
                else if (result == -1)
                {
                    _logger.LogInformation("User or workspace ID not found. Update failed.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "User or workspace ID not found. Update failed."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the notification data: {ex.Message}");
                Console.WriteLine($"Error: {ex.Message}");

                response = new
                {
                    Status = "Error",
                    Status_Description = "An exception occurred while updating the notification data."
                };
            }

            return response;
        }



        [HttpGet]
        public IActionResult GetAdvRolesList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getRolesList = "GetAdvRoleDetails";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getRolesList);


                DataTable rolesList = dbHandler.ExecuteDataTable(getRolesList);

                if (rolesList == null || rolesList.Rows.Count == 0)
                {
                    _logger.LogInformation("No roles found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No roles found"
                    });
                }

                var rolesListData = rolesList.AsEnumerable().Select(row => new
                {
                    role_id = row.Field<int>("role_id"),
                    role_name = row.Field<string>("role_name")
                }).ToList();

                _logger.LogInformation("Roles retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Roles retrieved successfully",
                    RolesList = rolesListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the notification data: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the roles list: {ex.Message}"
                });
            }
        }


        [HttpPut("updateProfile")]
        public IActionResult UpdateUserProfile([FromBody] UserProfileUpdateModel model)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                string updateQuery = "UpdateUserProfile";
                _logger.LogInformation("Executing stored procedure: {updateQuery}", updateQuery);

                var parameters = new Dictionary<string, object>
               {
                   { "user_email", model.UserEmail },
                   { "first_name", model.FirstName },
                   { "last_name", model.LastName },
               };

                int result = (int)_dbHandler.ExecuteNonQuery(updateQuery, parameters, CommandType.StoredProcedure);

                if (result > 0)
                {
                    _logger.LogInformation($"User profile for account ID: {model.UserEmail} was updated successfully.");

                    response = new
                    {
                        Status = "Success",
                        Status_Description = $"User profile for account ID: {model.UserEmail} was updated successfully."
                    };
                }
                else
                {
                    _logger.LogInformation("Account ID not found. Update failed.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Account ID not found. Update failed."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the notification data: {ex.Message}");
                Console.WriteLine($"Error: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = "An exception occurred while processing the request."
                };
            }

            return Ok(response);
        }

///
        [HttpPut("updateEmailAddress")]
        public IActionResult UpdateUserEmailAddress([FromBody] UserEmailUpdateModel model)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                string updateQuery = "UpdateEmailAddress";
                _logger.LogInformation("Executing stored procedure: {updateQuery}", updateQuery);

                var parameters = new Dictionary<string, object>
        {
            { "existing_email", model.ExistingEmail },
            { "new_email", model.NewEmail },
        };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                int result = (int)_dbHandler.ExecuteNonQuery(updateQuery, parameters, CommandType.StoredProcedure);

                _logger.LogInformation("Stored procedure executed successfully. Result: {Result}", result);

                if (result > 0)
                {  
                    _logger.LogInformation($"User email address for email: {model.ExistingEmail} was updated successfully.");
                    response = new
                    {
                        Status = "Success",
                        Status_Description = $"User email address for email: {model.ExistingEmail} was updated successfully."
                    };
                }
                else
                {
                    _logger.LogWarning("Email not found. Update failed.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Email not found. Update failed."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the notification data: {ex.Message}");
                Console.WriteLine($"Error: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = "An exception occurred while processing the request."
                };
            }
           return Ok(response);
        }

        [HttpPut]
        public IActionResult UpdateWorkspaceAddress([FromBody] WorkspaceAddressUpdateModel model)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                string updateQuery = "UpdateWorkspaceAddress";
                _logger.LogInformation("Executing stored procedure: {updateQuery}", updateQuery);
                var parameters = new Dictionary<string, object>
      {
          { "@workspaceid", model.workspaceid },
          { "@new_streetname", model.StreetName },
          { "@new_streetnumber", model.StreetNumber },
          { "@new_city", model.City },
          { "@new_code", model.PostalCode },
          { "@new_state", model.State },
          { "@new_billing_country", model.BillingCountry }

      };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                int result = (int)_dbHandler.ExecuteNonQuery(updateQuery, parameters, CommandType.StoredProcedure);
                _logger.LogInformation("User company updated successfully. result: {result}", result);

                if (result > 0)
                {
                    _logger.LogError($"User Company Name for account ID: {model.workspaceid} was updated successfully.");
                    response = new
                    {
                        Status = "Success",
                        Status_Description = $"User Company Name for account ID: {model.workspaceid} was updated successfully."
                    };
                }
                else
                {
                    _logger.LogError("Account ID not found. Update failed.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Account ID not found. Update failed."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An exception occurred while processing the request: {ex.Message}");
                Console.WriteLine($"Error: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = "An exception occurred while processing the request."
                };
            }

            return Ok(response);
        }


        [HttpPut]
        public IActionResult UpdateWorkspaceIndustry([FromBody] WorkspaceIndustryModel model)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                string updateQuery = "UpdateWorkSpaceIndustry";
                _logger.LogInformation("Executing stored procedure: {updateQuery}", updateQuery);

                var parameters = new Dictionary<string, object>
       {
           { "@workspaceid", model.workspaceid },
           { "@workspace_industry", model.WorkspaceIndustry }

       };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                int result = (int)_dbHandler.ExecuteNonQuery(updateQuery, parameters, CommandType.StoredProcedure);
                _logger.LogInformation("result received: {result}", result);

                if (result > 0)
                {
                    _logger.LogInformation($"Workspace Industry for User Email: {model.workspaceid} was updated successfully.");
                    response = new
                    {
                        Status = "Success",
                        Status_Description = $"Workspace Industry for User Email: {model.workspaceid} was updated successfully."
                    };
                }
                else
                {
                    _logger.LogWarning("Account ID not found. Update failed.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Account ID not found. Update failed."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An exception occurred while processing the request. {ex.Message}");
                Console.WriteLine($"Error: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = "An exception occurred while processing the request."
                };
            }

            return Ok(response);
        }



        [HttpPost]
        public async Task<IActionResult> InsertWabaDetails([FromServices] IDbHandler dbHandler, [FromBody] OAuthCallbackRequest request)
        {
            try
            {


                var procedure = "InsertWhatsappDetails";
                var parameters = new Dictionary<string, object>
                    {
                        {"@EmailId", request.EmailId },
                        {"@workspaceId", request.workspaceId },
                        {"@wabaId", request.WabaId },
                        {"@phoneId", request.PhoneId },
                        {"@LastUpdated", DateTime.UtcNow },
                        {"@CreatedDate", DateTime.UtcNow }
                    };

                _logger.LogInformation("Inserting waba details into database for email: {EmailId}", request.EmailId ?? "Unknown");

                object? result = dbHandler.ExecuteScalar(procedure, parameters, CommandType.StoredProcedure);

                if (result == null)
                {
                    _logger.LogError("Failed to save waba details for email: {EmailId}", request.EmailId ?? "Unknown");

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Error saving waba details"
                    });
                }

                _logger.LogInformation("waba details saved successfully for email: {EmailId}", request.EmailId ?? "Unknown");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "waba details inserted successfully.",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while inserting waba details for email: {EmailId}", request?.EmailId ?? "Unknown");

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while inserting: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public async Task<IActionResult> GetWhatsappPhoneNumbers(int workspaceId)
        {
            try
            {
                _logger.LogInformation("Received request to get WhatsApp phone numbers for EmailId: {workspaceId}", workspaceId);

                var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);
                Console.WriteLine(whatsappDetails);
                if (whatsappDetails == null)

                {
                    _logger.LogWarning("WhatsApp account details not found for EmailId: {EmailId}", workspaceId);

                    return NotFound(new
                    {
                        Status = "Failure",
                        Status_Description = "WhatsApp account details not found"
                    });
                }
                _logger.LogInformation("Found WhatsApp account details for EmailId: {EmailId}. WabaId: {WabaId}", workspaceId, whatsappDetails.WabaId);

                string url = $"{_configuration["facebookApiUrl"]}{whatsappDetails.WabaId}/phone_numbers?fields=id,cc,country_dial_code,display_phone_number,verified_name,status,quality_rating,search_visibility,platform_type,code_verification_status&access_token={whatsappDetails.AccessToken}";


                Console.WriteLine(url);
                _logger.LogInformation("Constructed Facebook Graph API URL: {Url}", url);

                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to retrieve data from Facebook API for workspaceId: {workspaceId}. StatusCode: {StatusCode}, Reason: {Reason}", workspaceId, response.StatusCode, response.ReasonPhrase);

                    return StatusCode((int)response.StatusCode, new
                    {
                        Status = "Failure",
                        Status_Description = $"Error retrieving data: {response.ReasonPhrase}"
                    });
                }
                _logger.LogInformation("Successfully received response from Facebook API for EmailId: {workspaceId}", workspaceId);


                var responseData = await response.Content.ReadAsStringAsync();
                var parsedData = JsonConvert.DeserializeObject<PhoneNumberResponse>(responseData);

                if (parsedData?.Data == null || !parsedData.Data.Any())
                {
                    _logger.LogWarning("No phone numbers found in the response for EmailId: {workspaceId}", workspaceId);

                    return NotFound(new
                    {
                        Status = "Failure",
                        Status_Description = "No phone numbers found in the response"
                    });
                }
                _logger.LogInformation("Successfully retrieved {PhoneNumberCount} phone numbers for EmailId: {EmailId}", parsedData.Data.Count(), workspaceId);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Phone numbers retrieved successfully",
                    Data = parsedData.Data
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving phone numbers for EmailId: {workspaceId}", workspaceId);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving phone numbers: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult IsWhatsappTokenValid([FromQuery] int workspaceId)
        {
            try
            {
                var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);

                // Check if AccessToken is null
                if (string.IsNullOrEmpty(whatsappDetails?.AccessToken) && string.IsNullOrEmpty(whatsappDetails?.WabaId) && string.IsNullOrEmpty(whatsappDetails?.PhoneId))
                {
                    _logger.LogWarning("Access token is null or empty for email: {workspaceId}", workspaceId);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Access token is null or not found for the given email",
                        IsValid = false
                    });
                }

                string accessToken = whatsappDetails.AccessToken;
                string appId = _configuration["FbAppId"];
                string appSecret = _configuration["FbAppSecret"];

                string url = $"https://graph.facebook.com/debug_token?input_token={accessToken}&access_token={appId}|{appSecret}";
                _logger.LogInformation("Constructed Facebook debug URL for token validation: {Url}", url);

                using var httpClient = new HttpClient();
                var response = httpClient.GetAsync(url).Result;

                _logger.LogInformation("{response}", response);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Error validating token: {response.ReasonPhrase}");

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = $"Error validating token: {response.ReasonPhrase}",
                        IsValid = false
                    });
                }

                var responseData = response.Content.ReadAsStringAsync().Result;
                _logger.LogInformation("Response received from external service. Content: {ResponseContent}", responseData);

                var parsedData = JsonConvert.DeserializeObject<dynamic>(responseData);
                _logger.LogInformation("Response parsed successfully.");

                bool isValid = parsedData?.data?.is_valid == true;
                _logger.LogInformation("Access token validation result: {IsValid}", isValid);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = isValid ? "Access token is valid" : "Access token is invalid",
                    IsValid = isValid
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while validating the WhatsApp token: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while validating the WhatsApp token: {ex.Message}",
                    IsValid = false
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetWhatsappOwnerDetails(int workspaceId)
        {
            try
            {
                var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);
                Console.WriteLine(whatsappDetails);
                _logger.LogInformation(" {whatsappDetails}", whatsappDetails);


                if (whatsappDetails == null)
                {
                    _logger.LogWarning(" WhatsApp account details not found");

                    return NotFound(new
                    {
                        Status = "Failure",
                        Status_Description = "WhatsApp account details not found"
                    });
                }

                string url = $"{_configuration["facebookApiUrl"]}{whatsappDetails.WabaId}?fields=id,name,currency,owner_business_info&access_token={whatsappDetails.AccessToken}";
                _logger.LogInformation("Constructed Facebook API URL: {Url}", url);
                Console.WriteLine(url);


                _logger.LogInformation("Constructed Facebook debug URL for token validation: {Url}", url);

                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Error retrieving data from Facebook API. StatusCode: {StatusCode}, Reason: {ReasonPhrase}", response.StatusCode, response.ReasonPhrase);

                    return StatusCode((int)response.StatusCode, new
                    {
                        Status = "Failure",
                        Status_Description = $"Error retrieving data: {response.ReasonPhrase}"
                    });
                }

                var responseData = await response.Content.ReadAsStringAsync();
                var parsedData = JsonConvert.DeserializeObject<WhatsappOwnerDetailsResponse>(responseData);

                _logger.LogInformation("Response received from external service. Content: {ResponseContent}", responseData);
                _logger.LogInformation("Response parsed successfully. Parsed Data: {ParsedData}", parsedData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Phone numbers retrieved successfully",
                    Data = parsedData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving WhatsApp owner details for EmailId: {workspaceId}", workspaceId);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving whatsapp owner details: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetProfileImage([FromServices] IDbHandler dbHandler, string EmailId, int WorkspaceId)
        {
            try
            {
                string procedure = "GetProfileImage";

                var parameters = new Dictionary<string, object>
        {
            { "@EmailId", EmailId },
            { "@WorkspaceId", WorkspaceId }
        };


                DataTable ImageDetails = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (ImageDetails.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Image Not found"
                    });
                }

                var ImageData = ImageDetails.AsEnumerable().Select(row => new
                {

                    image = row.Field<string>("image"),
                }).ToList();

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Image retrieved successfully",
                    Image = ImageData
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the image: {ex.Message}"
                });
            }

        }

        [HttpGet]
        public IActionResult GetCountryList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getCountryList = "GetCountryList";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", getCountryList);

                DataTable countryList = dbHandler.ExecuteDataTable(getCountryList);

                if (countryList == null || countryList.Rows.Count == 0)
                {
                    _logger.LogInformation("No countries found in the database.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No countries found"
                    });
                }
                var countryListData = countryList.AsEnumerable().Select(row => new
                {
                    country_id = row.Field<int>("country_id"),
                    country_name = row.Field<string>("country_name")
                }).ToList();

                _logger.LogInformation("Countries retrieved successfully .{countryListData}", countryListData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Countries retrieved successfully",
                    CountryList = countryListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the country list.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the country list: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetUserWorkspaceCount([FromQuery] string email)
        {
            try
            {
                _logger.LogInformation("GetUserWorkspaceCount endpoint called for email: {Email}", email);

                // Validate email parameter
                if (string.IsNullOrWhiteSpace(email))
                {
                    _logger.LogWarning("Email parameter is missing.");
                    return BadRequest(new
                    {
                        Status = "Failure",
                        Status_Description = "Email parameter is required."
                    });
                }

                // Stored procedure name
                string storedProcedure = "GetUserWorkspaceCount";

                // Parameters for the stored procedure
                var parameters = new Dictionary<string, object>
      {
          { "@Email", email }
      };

                // Execute the stored procedure and get the result
                DataTable workspaceTable = _dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (workspaceTable == null || workspaceTable.Rows.Count == 0)
                {
                    _logger.LogWarning("No workspaces found for email: {Email}", email);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No workspaces found for the given email."
                    });
                }

                // Extract workspace IDs and count
                var workspaceIds = workspaceTable.AsEnumerable().Select(row => row.Field<int>("workspace_info_id")).ToList();
                int workspaceCount = workspaceIds.Count;

                _logger.LogInformation("Workspaces retrieved successfully. Workspace IDs: {WorkspaceIds}, Count: {Count}", workspaceIds, workspaceCount);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Workspaces retrieved successfully.",
                    WorkspaceCount = workspaceCount,
                    WorkspaceIds = workspaceIds
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while retrieving the workspaces: {Message}", ex.Message);
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the workspaces: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public IActionResult UpdateWabaNPhoneId([FromBody] UpdateWabaNPhoneId request)
        {
            try
            {
                var procedure = "UpdateWabaNPhoneId";
                var parameters = new Dictionary<string, object>
        {
            { "@Id", request.Id },
            { "@WabaId", request.WabaId },
            { "@PhoneId", request.PhoneId },
        };

                _logger.LogInformation("Updating WABA and Phone ID details in the database for Id: {Id}", request.Id);

                int rowsAffected = _dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogError("Failed to update WABA and Phone ID details for Id: {Id}", request.Id);

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Error updating WABA and Phone ID details."
                    });
                }

                _logger.LogInformation("Successfully updated WABA and Phone ID details for Id: {Id}", request.Id);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "WABA and Phone ID details updated successfully."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating WABA and Phone ID details for Id: {Id}", request.Id);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = "An internal server error occurred. Please try again later."
                });
            }
        }


        [HttpGet]
        public IActionResult GetMetaTemplateDetails([FromServices] IDbHandler dbHandler, int workspace_id)
        {
            try
            {
                string procedure = "GetMetaTemplateDetails";

                _logger.LogInformation("Executing stored procedure:", procedure);

                var parameters = new Dictionary<string, object>
                {
                    { "@Workspace_id", workspace_id }
                };

                // Execute the stored procedure
                DataTable templateDetails = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);



                if (templateDetails.Rows.Count == 0)
                {
                    _logger.LogWarning("Template Not found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Template Not found"
                    });
                }

                var TemplateData = templateDetails.AsEnumerable().Select(row => new
                {
                    template_id = row.Field<string>("template_id"),
                    template_name = row.Field<string>("template_name"),
                    channel_type = row.Field<string>("channel_type"),
                    status = row.Field<string>("status"),
                    last_edited = row.Field<DateTime?>("last_updated"),
                    components = row.Field<string>("components"),
                    language = row.Field<string>("language"),
                    category = row.Field<string>("category"),
                    sub_category = row.Field<string>("subcategory")

                }).ToList();

                _logger.LogInformation("Templates retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Templates retrieved successfully",
                    TemplateDetails = TemplateData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the template details: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the template details: {ex.Message}"
                });
            }
        }



        [HttpPost]
        public async Task<IActionResult> CreateMessageTemplate([FromBody] MetaTemplateDetails meta, int workspaceId, int channel_id, [FromServices] IDbHandler dbHandler)
        {
            var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);
            string url = $"{_configuration["facebookApiUrl"]}{whatsappDetails.WabaId}/message_templates";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", whatsappDetails.AccessToken);
                var jsonString = System.Text.Json.JsonSerializer.Serialize(meta.data2);

                string mediaBase64 = meta.mediaBase64;

                try
                {
                    // Send the template creation request
                    var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                    var postResponse = await client.PostAsync(url, content);

                    if (!postResponse.IsSuccessStatusCode)
                    {
                        var errorContent = await postResponse.Content.ReadAsStringAsync();
                        return StatusCode((int)postResponse.StatusCode, new { message = "Template creation failed.", error = errorContent });
                    }

                    // Parse the response for template ID and status
                    var responseBody = await postResponse.Content.ReadAsStringAsync();
                    JsonDocument responseDoc = JsonDocument.Parse(responseBody);
                    string templateId = responseDoc.RootElement.GetProperty("id").GetString();
                    string status = responseDoc.RootElement.GetProperty("status").GetString();


                    //Poll for the status until it's "APPROVED"
                    //var startTime = DateTime.UtcNow;
                    //while (status != "APPROVED" && (DateTime.UtcNow - startTime).TotalSeconds < 10)
                    //{
                    //    await Task.Delay(5000); // Wait for 5 seconds before polling again

                    var getResponse = await client.GetAsync(url);
                    if (!getResponse.IsSuccessStatusCode)
                    {
                        var errorContent = await getResponse.Content.ReadAsStringAsync();
                        return StatusCode((int)getResponse.StatusCode, errorContent);
                    }

                    responseBody = await getResponse.Content.ReadAsStringAsync();
                    responseDoc = JsonDocument.Parse(responseBody);

                    //Iterate over the templates to find the matching ID and check its status
                    JsonElement dataElement = responseDoc.RootElement.GetProperty("data");
                    foreach (JsonElement element in dataElement.EnumerateArray())
                    {
                        if (element.GetProperty("id").GetString() == templateId)
                        {
                            //status = element.GetProperty("status").GetString();
                            //if (status == "APPROVED")
                            //{
                            break;
                            //}
                        }
                    }
                    //}


                    //if (status != "APPROVED")
                    //{
                    //    return BadRequest(new { message = "Template status is not APPROVED within 60 seconds.", apiResponse = responseBody });
                    //}



                    // Retrieve approved template details
                    JsonElement finalTemplateElement = responseDoc.RootElement
                                                                 .GetProperty("data")
                                                                 .EnumerateArray()
                                                                 .FirstOrDefault(e => e.GetProperty("id").GetString() == templateId);

                    if (finalTemplateElement.ValueKind != JsonValueKind.Undefined)
                    {
                        string name = finalTemplateElement.GetProperty("name").GetString();
                        string language = finalTemplateElement.GetProperty("language").GetString();
                        string category = finalTemplateElement.GetProperty("category").GetString();
                        string subCategory = finalTemplateElement.TryGetProperty("sub_category", out JsonElement subCatElem) ? subCatElem.GetString() : null;
                        string componentsElement = finalTemplateElement.GetProperty("components").ToString();

                        // Execute the stored procedure to save the template details in the database
                        var parameters = new Dictionary<string, object>
              {
                  { "TemplateId", templateId },
                  { "TemplateName", name },
                  { "Language", language },
                  { "Status", status },
                  { "Category", category },
                  { "SubCategory", subCategory ?? (object)DBNull.Value },
                  { "Components", componentsElement },
                  { "Channel_type",channel_id },
                  { "Workspace_id",workspaceId },
                  { "Last_updated",DateTime.Now},
                  { "mediaBase64",mediaBase64},
                  { "IsFirstEdit",true}
              };

                        string insertQuery = "InsertMetaTemplateDetails";

                        try
                        {
                            object result = dbHandler.ExecuteScalar(insertQuery, parameters, CommandType.StoredProcedure);

                            return Ok(new
                            {
                                message = "Template Approved and Stored Successfully",
                                template = new
                                {
                                    id = templateId,
                                    name,
                                    language,
                                    status,
                                    category,
                                    subCategory,
                                    components = componentsElement
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to store template details.", error = ex.Message });
                        }
                    }

                    return BadRequest(new { message = "No valid template data found in the response." });
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken == CancellationToken.None)
                {
                    return StatusCode(StatusCodes.Status408RequestTimeout, "The request timed out.");
                }
                catch (Exception ex)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, ex.Message);
                }
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateSMSMessageTemplate([FromBody] SMSTemplateDetails meta, int channel_id, int workspace_id, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string templateId = SMSTemplateIDGenerator.GenerateTemplateId();

                string componentsJson = JsonConvert.SerializeObject(meta.data2.components);
                //string bodyText = ""
                //string componentsJson = $"[{{\"type\":\"BODY\",\"text\":\"{bodyText}\"}}]";

                string insertQuery = "InsertMetaTemplateDetails";
                // Execute the stored procedure to save the template details in the database
                var parameters = new Dictionary<string, object>
                {
                    { "TemplateId", templateId },
                    { "TemplateName", meta.data2.name },
                    { "Language", meta.data2.language },
                    { "Status", "APPROVED" },
                    { "Category", meta.data2.category },
                    { "SubCategory", (object)DBNull.Value },
                    { "Components", componentsJson },
                    { "Channel_type",channel_id },
                    { "Workspace_id",workspace_id },
                    { "Last_updated",DateTime.Now},
                    { "mediaBase64",null},
                    { "IsFirstEdit",true}
                };

                object result = dbHandler.ExecuteScalar(insertQuery, parameters, CommandType.StoredProcedure);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "SMS Template Created Successfully",
                });
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Failed to store template details.", error = ex.Message });
            }

        }
            
        


        [HttpPut]
        public async Task<IActionResult> EditMessageTemplate(object payload, [FromServices] IDbHandler dbHandler)
        {
            Console.WriteLine("Received jsonData: " + payload);

            var data = payload.ToString();

            Console.WriteLine("Template Id : " + payload);

            // Parse the input string into a JObject
            JObject jsonObject = JObject.Parse(data);



            // Access the templateId and jsonData
            string templateId = (string)jsonObject["templateId"];
            string jsonData = (string)jsonObject["components"];
            string mediaBase64 = (string)jsonObject["mediaBase64"];
            int workspaceId = (int)jsonObject["workspaceId"];

            var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);

            Console.WriteLine("TemplateId : " + templateId);
            Console.WriteLine("jsonData : " + jsonData);

            if (string.IsNullOrEmpty(templateId) || string.IsNullOrEmpty(jsonData))
            {
                return BadRequest(new { Status = "Failure", Status_Description = "Invalid payload. templateId, jsonData, and workspaceId are required." });
            }

            var builder = new ConfigurationBuilder();
            builder.SetBasePath(Directory.GetCurrentDirectory())
                   .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            IConfiguration config = builder.Build();

            // The Facebook API URL
            string url = $"{_configuration["facebookApiUrl"]}{templateId}";
            string url2 = $"{_configuration["facebookApiUrl"]}{whatsappDetails.WabaId}/message_templates";

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", whatsappDetails.AccessToken);

                try
                {
                    // Set up the content for the POST request
                    var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                    var postResponse = await client.PostAsync(url, content);

                    if (!postResponse.IsSuccessStatusCode)
                    {
                        var errorContent = await postResponse.Content.ReadAsStringAsync();
                        return StatusCode((int)postResponse.StatusCode, errorContent);
                    }

                    // Check the initial response for the status
                    var responseBody = await postResponse.Content.ReadAsStringAsync();
                    var responseJson = JObject.Parse(responseBody);

                    // Assuming success is indicated by a specific field in the response JSON
                    bool success = responseJson["success"]?.Value<bool>() ?? false;

                    if (success)
                    {
                        // Poll the status until it becomes "APPROVED" or timeout
                        var startTime = DateTime.UtcNow;
                        while (success && (DateTime.UtcNow - startTime).TotalSeconds < 60)
                        {
                            await Task.Delay(5000);
                            var getResponse = await client.GetAsync(url2);
                            Console.WriteLine(getResponse.ToString());
                            if (!getResponse.IsSuccessStatusCode)
                            {
                                var errorContent = await getResponse.Content.ReadAsStringAsync();
                                return StatusCode((int)getResponse.StatusCode, errorContent);
                            }

                            var responseBody2 = await getResponse.Content.ReadAsStringAsync();
                            Console.WriteLine(responseBody2);
                            var responseDoc2 = JsonDocument.Parse(responseBody2);

                            // Iterate over the list of templates to find the one with the matching ID
                            JsonElement dataElement = responseDoc2.RootElement.GetProperty("data");
                            foreach (JsonElement element in dataElement.EnumerateArray())
                            {
                                if (element.GetProperty("id").GetString() == templateId)
                                {
                                    string status = element.GetProperty("status").GetString();
                                    //if (status == "APPROVED")
                                    //{
                                    string name = element.GetProperty("name").GetString();
                                    string language = element.GetProperty("language").GetString();
                                    string category = element.GetProperty("category").GetString();
                                    string subCategory = element.TryGetProperty("sub_category", out JsonElement subCatElem) ? subCatElem.GetString() : null;
                                    string componentsElement = element.GetProperty("components").ToString();

                                    // Execute the stored procedure to save the template details in the database
                                    var parameters = new Dictionary<string, object>
{
    { "templateId", templateId ?? (object)DBNull.Value }, // Avoid null
    { "status", status ?? (object)DBNull.Value },
    { "category", category ?? (object)DBNull.Value },
    { "subcategory", subCategory ?? (object)DBNull.Value },
    { "components", componentsElement ?? (object)DBNull.Value },
    { "mediaBase64", mediaBase64 ?? (object)DBNull.Value },
    { "LastUpdated", DateTime.Now },
    {"IsFirstEdit",false }
};

                                    string updateQuery = "UpdateMetaTemplateDetails";

                                    var result = dbHandler.ExecuteScalar(updateQuery, parameters, CommandType.StoredProcedure);

                                    // Check if the result is null
                                    if (result == null)
                                    {
                                        return Ok(new { Status = "Failure", Status_Description = "Failed to update the template in the database." });
                                    }

                                    // Safely parse the result to an integer
                                    int rowsAffected;
                                    if (!int.TryParse(result.ToString(), out rowsAffected))  // Handle parsing errors
                                    {
                                        return Ok(new { Status = "Failure", Status_Description = "Failed to update the template in the database." });
                                    }

                                    if (rowsAffected > 0)
                                    {
                                        return Ok(new
                                        {
                                            Status = "Success",
                                            Status_Description = "Templates Updated Successfully",

                                        });
                                    }
                                    else
                                    {
                                        return Ok(new { Status = "Failure", Status_Description = "Failed to update the template in the database." });
                                    }
                                }
                            }
                        }
                        //}

                        //return BadRequest(new { message = "Template status is not APPROVED within 60 seconds." });
                    }

                    return Ok(new { status = "Failure", Status_Description = "No valid template data found in the response." });
                }
                catch (TaskCanceledException ex) when (ex.CancellationToken == CancellationToken.None)
                {
                    // Handle timeout
                    return StatusCode(StatusCodes.Status408RequestTimeout, "The request timed out.");
                }
                catch (Exception ex)
                {
                    // Handle other exceptions
                    return BadRequest(new { Status = "Failure", Status_Description = ex.Message });
                }
            }
        }




        [HttpGet]
        public IActionResult GetCountryDetails([FromServices] IDbHandler dbHandler)
        {
            try
            {

                string getCountryDetails = "GetCountryDetails";

                DataTable countryDetails = dbHandler.ExecuteDataTable(getCountryDetails);


                if (countryDetails == null || countryDetails.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No country details found"
                    });
                }


                var countryDetailsData = countryDetails.AsEnumerable().Select(row => new
                {
                    country_id = row.Field<int>("country_id"),
                    country_code = row.Field<int>("country_code"),
                    country_name = row.Field<string>("country_name"),
                    country_shortname = row.Field<string>("country_shortname")
                }).ToList();
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Country details retrieved successfully",
                    CountryDetails = countryDetailsData
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the country details: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult CheckEditPermission(string templateId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetMetaTemplateDetailsById";
                var parameters = new Dictionary<string, object> { { "@TemplateId", templateId } };
                DataTable templateDetails = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (templateDetails.Rows.Count == 0)
                {
                    return Ok(new { CanEdit = false, Message = "Template Not found" });
                }

                var TemplateData = templateDetails.AsEnumerable().Select(row => new
                {
                    last_edited = row.Field<DateTime?>("last_updated"),
                    IsFirstEdit = row.Field<bool>("IsFirstEdit")
                }).FirstOrDefault();

                bool canEdit = TemplateData.IsFirstEdit || (TemplateData.last_edited.HasValue && (DateTime.UtcNow - TemplateData.last_edited.Value).TotalHours >= 24);
                if (!canEdit)
                {
                    double remainingHours = Math.Max(0, 24 - (DateTime.Now - TemplateData.last_edited.Value).TotalHours);

                    if (remainingHours < 1)
                    {
                        double remainingMinutes = Math.Ceiling(remainingHours * 60);
                        return Ok(new { CanEdit = false, Message = $"You can edit this template after {remainingMinutes} minutes." });
                    }
                    else
                    {
                        return Ok(new { CanEdit = false, Message = $"You can edit this template after {Math.Ceiling(remainingHours)} hours." });
                    }
                }

                return Ok(new { CanEdit = true, Message = "Edit allowed." });
            }
            catch (Exception ex)
            {
                return Ok(new { CanEdit = false, Message = ex.Message });
            }
        }


        [HttpDelete]
        public async Task<IActionResult> DeleteMessageTemplate(string id, string template, int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);

            // The Facebook API URL
            string url = $"https://graph.facebook.com/v20.0/{whatsappDetails.WabaId}/message_templates?name={template}";

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", whatsappDetails.AccessToken);

                try
                {
                    // Send the DELETE request
                    HttpResponseMessage response = await client.DeleteAsync(url);

                    // Ensure the response is successful
                    if (response.IsSuccessStatusCode)
                    {
                        // Read and return the response content
                        string responseBody = await response.Content.ReadAsStringAsync();

                        var parameters = new Dictionary<string, object>
                {
                    { "templateId", id }
                };

                        string DeleteQuery = "DeleteMetaTemplate";

                        int rowsAffected = (int)dbHandler.ExecuteScalar(DeleteQuery, parameters, CommandType.StoredProcedure);

                        if (rowsAffected > 0)
                        {
                            return Ok(new { message = "Template deleted successfully", apiResponse = responseBody });
                        }
                        else
                        {
                            return StatusCode(StatusCodes.Status500InternalServerError, "Failed to delete template from the database.");
                        }
                    }
                    else
                    {
                        // Handle non-success status codes
                        string responseBody = await response.Content.ReadAsStringAsync();
                        return StatusCode((int)response.StatusCode, new { message = "Failed to delete template from API", apiResponse = responseBody });
                    }
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"Request error: {e.Message}");
                    return StatusCode(StatusCodes.Status500InternalServerError, e.Message);
                }
            }
        }



        [HttpPost]
        public async Task<string> UploadFileAsync([FromForm] UploadMetaMedia data, [FromForm] IFormFile file)
        {
            try
            {
                string appId = _configuration["FbAppId"];

                var workspaceId = Convert.ToInt32(data.workspace_id); // Convert to int
                var whatsappAccountDetails = GetWhatsappAccountDetailsByWId(workspaceId);

                // Construct the URL for the first API
                string url = $"{_configuration["facebookApiUrl"]}{appId}/uploads" +
                             $"?file_name={Uri.EscapeDataString(data.file_name)}" +
                             $"&file_length={data.file_length}" +
                             $"&file_type={Uri.EscapeDataString(data.file_type)}" +
                             $"&access_token={Uri.EscapeDataString(whatsappAccountDetails.AccessToken)}";

                // Create an empty content body for POST request
                HttpContent content = new StringContent(string.Empty);

                // Make the POST request
                using var httpClient = new HttpClient();
                HttpResponseMessage response = await httpClient.PostAsync(url, content);

                // Ensure success status code
                response.EnsureSuccessStatusCode();

                // Read the response content
                string responseContent = await response.Content.ReadAsStringAsync();
                Console.WriteLine("Full Response: " + responseContent);

                // Extract the "id" field and clean up the value
                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseContent);
                if (jsonResponse != null && jsonResponse.ContainsKey("id"))
                {
                    string rawId = jsonResponse["id"];
                    string uploadSessionId = rawId.Replace("upload:", string.Empty);

                    // Construct the URL for the second API
                    string secondUrl = $"{_configuration["facebookApiUrl"]}upload:{uploadSessionId}";

                    // Set headers for the second API call
                    httpClient.DefaultRequestHeaders.Add("Authorization", $"OAuth {whatsappAccountDetails.AccessToken}");
                    httpClient.DefaultRequestHeaders.Add("file_offset", "0");

                    // Read the file content
                    using var fileStream = file.OpenReadStream();
                    var fileContent = new StreamContent(fileStream);

                    // Make the second POST request
                    HttpResponseMessage secondResponse = await httpClient.PostAsync(secondUrl, fileContent);

                    // Ensure success status code
                    secondResponse.EnsureSuccessStatusCode();

                    // Read and return the response of the second API as a string
                    string secondResponseContent = await secondResponse.Content.ReadAsStringAsync();
                    Console.WriteLine("Second Response: " + secondResponseContent);

                    return secondResponseContent;
                }

                throw new Exception("Id field not found in the response.");
            }
            catch (Exception ex)
            {
                // Handle exceptions
                throw new Exception("Error uploading file to Facebook API", ex);
            }
        }

        [HttpGet]
        public async Task<IActionResult> SyncTemplatesWithMeta(int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Fetch pending templates from the database
                var pendingTemplates = dbHandler.ExecuteDataTable("GetPendingTemplates", null, CommandType.StoredProcedure);

                if (pendingTemplates.Rows.Count == 0)
                {
                    return Ok(new { message = "No pending templates found." });
                }

                // Call Meta API to get all templates
                var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _configuration["WhatsAppToken"]);

                var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);

                string url = $"{_configuration["facebookApiUrl"]}{whatsappDetails.WabaId}/message_templates";
                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode((int)response.StatusCode, await response.Content.ReadAsStringAsync());
                }

                var metaTemplatesJson = await response.Content.ReadAsStringAsync();
                var metaTemplates = JsonConvert.DeserializeObject<JObject>(metaTemplatesJson)?["data"] as JArray;

                if (metaTemplates == null || !metaTemplates.Any())
                {
                    return Ok(new { message = "No templates found in Meta API." });
                }

                // Match and update pending templates
                foreach (DataRow row in pendingTemplates.Rows)
                {
                    string templateId = row["template_id"].ToString();
                    var matchingMetaTemplate = metaTemplates.FirstOrDefault(t => t["id"].ToString() == templateId);

                    if (matchingMetaTemplate != null && matchingMetaTemplate["status"].ToString() == "APPROVED")
                    {
                        // Update the template status in the database
                        var parameters = new Dictionary<string, object>
                    {
                        { "templateId", templateId },
                        { "status", "APPROVED" }
                    };

                        _dbHandler.ExecuteNonQuery("UpdateTemplateStatus", parameters, CommandType.StoredProcedure);
                    }
                }

                return Ok(new { message = "Template sync completed." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet]
        public object GetAdvAudienceListDetailsByWorkspaceId([FromServices] IDbHandler dbHandler, int workspace_id)
        {

            try
            {
                string procedure = "GetAdvAudienceListDetailsByWorkspaceId";
                var parameters = new Dictionary<string, object>
{
    { "@workspace_id", workspace_id }
};
                DataTable audienceList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);
                if (audienceList == null || audienceList.Rows.Count == 0)
                {
                    _logger.LogInformation("No lists found for the given workspace ID: {WorkspaceId}", workspace_id);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No lists found"
                    });
                }

                // Map the DataTable to a list of objects
                var audienceListData = audienceList.AsEnumerable().Select(row => new
                {
                    list_id = row.Field<int>("list_id"),
                    listname = row.Field<string>("listname"),
                    created_date = row.Field<DateTime>("created_date").ToString("yyyy-MM-dd HH:mm:ss"),
                    total_people = row.Field<int>("total_people"),
                    status = row.Field<string>("status")
                }).ToList();

                _logger.LogInformation("Audience lists retrieved successfully: {AudienceListData}", audienceListData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Audience lists retrieved successfully",
                    AudienceList = audienceListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the audience list.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the audience list: {ex.Message}"
                });
            }

        }

        public static Dictionary<int, List<AudienceContact>> AssignContacts(
                    List<AudienceContact> audienceList,
                    int totalContacts,
                    double dailyLimitPercentage,
                    int totalDays,
                    string repeatCondition,
                    int campaignBudget,
                    string budgetType,
                    decimal perMessageCost)
        {
            int repeatInterval = repeatCondition switch
            {
                "Every Day" => 1,
                "Every 2 Days" => 2,
                "Every 3 Days" => 3,
                "Ones a Week" => 7,
                _ => 1
            };

            //int actualDays = totalDays / repeatInterval;
            int actualDays = 0;
            int count = 0;
            if (totalDays - 1 <= 0)
            {
                actualDays = 1;
            }
            else
            {
                int availableDays = totalDays - 1;
                
                while (availableDays >= 0)
                {
                    count+=1;
                    availableDays -=repeatInterval;
                }
                actualDays = count;
            }



            int dailyRecepientLimit = (int)((dailyLimitPercentage / 100) * totalContacts);

            // 🔹 Fix: Ensure the dictionary uses List<AudienceContact> instead of List<object>
            var dayWiseContacts = new Dictionary<int, List<AudienceContact>>();
            int lastDay = 0;
            for (int i = 1; i <= totalDays; i+=repeatInterval)
            {
                dayWiseContacts[i] = new List<AudienceContact>(); // Fix: List<AudienceContact>
                lastDay = i;
            }


            int finalRowCountPerDay;
            if(budgetType== "Daily budget")
            {
                int canSend = (int)Math.Floor((decimal)campaignBudget / perMessageCost);

                if (canSend <= dailyRecepientLimit)
                {
                    finalRowCountPerDay = canSend;
                }
                else
                {
                    finalRowCountPerDay = dailyRecepientLimit;
                }


                int index = 0;
                for (int day = 1; day <= totalDays; day+=repeatInterval)
                {
                    for (int i = 0; i < finalRowCountPerDay; i++)
                    {
                        var assignedContact = audienceList[index % totalContacts];
                        dayWiseContacts[day].Add(assignedContact); // Fix: Add AudienceContact instead of object
                        index++;
                    }
                }


            }

            if(budgetType== "Lifetime budget")
            {
                int canSend = (int)Math.Floor((decimal)campaignBudget / perMessageCost);

                int totalContact = dailyRecepientLimit * actualDays;
                if (canSend <= totalContact)
                {

                    decimal originalValue = (decimal)canSend / dailyRecepientLimit;
                    decimal roundedValue = Math.Round(originalValue, 1);
                   
                    
                    actualDays = (int)(Math.Ceiling(roundedValue));

                    decimal decimalPart = roundedValue - Math.Floor(roundedValue);

                    int remainingPercentage = (int)(decimalPart * (decimal)100);

                    int remainingContacts = (int)(((decimal)remainingPercentage / 100) * (decimal)dailyRecepientLimit);

                    if (actualDays == 1)
                    {
                        remainingContacts = canSend;
                    }

                    int index = 0;
                    for (int day = 1; day <= totalDays; day+=repeatInterval)
                    {
                        if (day == lastDay)
                        {
                            for (int i = 0; i < remainingContacts; i++)
                            {
                                var assignedContact = audienceList[index % totalContacts];
                                dayWiseContacts[day].Add(assignedContact); // Fix: Add AudienceContact instead of object
                                index++;
                            }
                        }
                        else
                        {
                            for (int i = 0; i < dailyRecepientLimit; i++)
                            {
                                var assignedContact = audienceList[index % totalContacts];
                                dayWiseContacts[day].Add(assignedContact); // Fix: Add AudienceContact instead of object
                                index++;
                            }
                        }

                    }

                }
                else
                {
                    int index = 0;
                    for (int day = 1; day <= totalDays; day += repeatInterval)
                    {
                        for (int i = 0; i < dailyRecepientLimit; i++)
                        {
                            var assignedContact = audienceList[index % totalContacts];
                            dayWiseContacts[day].Add(assignedContact); // Fix: Add AudienceContact instead of object
                            index++;
                        }
                    }
                }

            }

            return dayWiseContacts;
        }


        [HttpGet]
        public IActionResult Getcampaigncontacts([FromQuery] string CampaignId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                if (string.IsNullOrEmpty(CampaignId))
                {
                    return BadRequest(new
                    {
                        Status = "Failure",
                        Status_Description = "Campaign id is required."
                    });
                }
                var parameters = new Dictionary<string, object>
                 {
                     { "@CampaignId", CampaignId }
                 };

                string storedProcedure = "GetAudienceDetailsByListId";
                DataTable audienceDetails = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (audienceDetails == null || audienceDetails.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No audience details found for the given campaign."
                    });
                }

                var audienceList = audienceDetails.AsEnumerable().Select(row => new AudienceContact
                {
                    ListId = row.Field<int>("list_id"),
                    FirstName = row.Field<string>("firstname"),
                    LastName = row.Field<string>("lastname"),
                    PhoneNo = row.Field<string>("phoneno"),
                    Location = row.Field<string>("location"),
                    FileName = row.Field<string>("filename1"),
                    ContactId = row.Field<int>("contact_id")
                }).ToList();

                var parameters1 = new Dictionary<string, object>
                 {
                     { "@CampaignId", CampaignId }
                 };


                string storedProcedure2 = "GetCampaignDetailsWithDays";
                DataTable dataTable = _dbHandler.ExecuteDataTable(storedProcedure2, parameters1, CommandType.StoredProcedure);

                _logger.LogInformation("dataTable:", dataTable);
                if (dataTable.Rows.Count == 0)
                {
                    return NotFound("No data found for the given Campaign ID.");
                }
                // Extract values from the first row
                DataRow row = dataTable.Rows[0];

                _logger.LogInformation("DataRow:", row);

                int dailyLimitPercentage = Convert.ToInt32(row["daily_recipient_limit"]);
                int totalDays = Convert.ToInt32(row["days_count"]);
                string repeatCondition = row["message_frequency"].ToString();
                int campaignBudget = Convert.ToInt32(row["campaign_budget"]);
                string budgetType = row["budget_and_schedule"].ToString();
                decimal perMessageCost = (decimal)row["per_message_cost"];


                int totalContacts = audienceList.Count;

                var assignedContacts = AssignContacts(
                audienceList,
                totalContacts,
                dailyLimitPercentage,
                totalDays,
                repeatCondition,
                campaignBudget,
                budgetType,
                perMessageCost);


                // 🔹 Insert assigned contacts into the database
                foreach (var dayContacts in assignedContacts)
                {
                    int dayNumber = dayContacts.Key;
                    foreach (var audience in dayContacts.Value)
                    {
                        var insertParameters = new Dictionary<string, object>
                        {
                            { "@ListId", audience.ListId },
                            { "@FirstName", audience.FirstName },
                            { "@Contact_id", audience.ContactId },
                            { "@LastName", audience.LastName },
                            { "@PhoneNo", audience.PhoneNo },
                            { "@Location", audience.Location },
                            { "@FileName", audience.FileName },
                            { "@CampaignId", CampaignId },
                            { "@Status", "Open" },
                            { "@DayNumber", dayNumber } // Storing assigned day in DB
                        };

                        string storedProcedure1 = "InsertCampaignContacts";
                        dbHandler.ExecuteNonQuery(storedProcedure1, insertParameters, CommandType.StoredProcedure);
                    }


                }


                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Audience details retrieved and inserted successfully.",
                    AudienceDetails = audienceList,
                    AssignedContacts = assignedContacts
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while processing audience details: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public IActionResult GetOperatorsByCountry([FromServices] IDbHandler dbHandler, [FromBody] GetOperatorsByCountryRequest request)
        {
            if (request == null || request.CountryIds == null || request.ToCountryNames == null)
            {
                return BadRequest(new { Status = "Error", Message = "Both CountryIds and ToCountryNames are required." });
            }

            try
            {
                string storedProcedure = "GetOperatorsByCountry";
                _logger.LogInformation($"Executing stored procedure: {storedProcedure}");

                // ✅ Convert CountryIds List<int> to JSON string format (e.g., "[1,2,3,4]")
                string formattedCountryJson = JsonConvert.SerializeObject(request.CountryIds);

                // ✅ Convert ToCountryNames List<string> to JSON string format (e.g., "["India","USA"]")
                string formattedToCountryJson = JsonConvert.SerializeObject(request.ToCountryNames);

                var parameters = new Dictionary<string, object>
    {
        { "@CountryJson", formattedCountryJson },
        { "@ToCountryJson", formattedToCountryJson }
    };

                _logger.LogInformation($"Executing with parameters: {parameters}");

                // Execute the stored procedure and retrieve the result
                DataTable result = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                // Convert DataTable to List of objects
                var operators = result.AsEnumerable().Select(row => new
                {
                    CountryName = row["countryName"].ToString(),
                    Msisdn = row["msisdn"].ToString(),
                    ContactId = Convert.ToInt32(row["contact_id"]),
                    ListId = Convert.ToInt32(row["list_id"]),
                    FileName = row["fileName"].ToString(),
                    WorkspaceId = Convert.ToInt32(row["workspace_id"])
                }).ToList();


                _logger.LogInformation($"Stored Procedure Result Count: {result.Rows.Count}");

                foreach (var op in operators)
                {
                    var insertParameters = new Dictionary<string, object>
{
    { "@ListId", op.ListId },
    { "@FirstName", "UnKnown" },
    { "@Contact_id",op.ContactId},
    { "@LastName", "UnKnown" },
    { "@PhoneNo", op.Msisdn },
    { "@Location", op.CountryName },
    { "@FileName", op.FileName },
    { "@CampaignId", request.CampaignId },
    { "@Status", "Open" }
};
                    string storedProcedure1 = "InsertCampaignContacts";
                    dbHandler.ExecuteNonQuery(storedProcedure1, insertParameters, CommandType.StoredProcedure);
                }
                _logger.LogInformation($"Retrieved {operators.Count} operators.");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Operators retrieved successfully.",
                    Data = operators
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to retrieve operators. Error: {ex.Message}");

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while fetching operators: {ex.Message}"
                });
            }
        }


        [HttpPost, DisableRequestSizeLimit]
        public object contact_upload([FromServices] IDbHandler dbHandler)
        {
            DataTable dtmain = new DataTable();
            dtmain.Columns.Add("Status");
            dtmain.Columns.Add("Status_Description");
            string formattedDateTime = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string path = _configuration.GetValue<string>("filepath");
            string path1 = Directory.CreateDirectory(Path.Combine(path, "uploads")).ToString();
            var file = Request.Form.Files[0];
            string phoneNumber;
            string cleanedValue = "";
            string listname = Request.Form["listName"];
            string workspaceId = Request.Form["workspace_id"];

            string fileName = Path.GetFileName(file.FileName);
            string fileExtension = Path.GetExtension(file.FileName).ToLower();
            string newFileName = $"{formattedDateTime}{fileExtension}";
            string filePath = Path.Combine(path1, newFileName);

            using (FileStream stream = new FileStream(filePath, FileMode.Create))
            {
                file.CopyTo(stream);
            }

            HashSet<string> uniqueNumbers = new HashSet<string>();
            DataTable transData = new DataTable();
            transData.Columns.Add("list_id", typeof(int));
            transData.Columns.Add("firstname", typeof(string));
            transData.Columns.Add("lastname", typeof(string));
            transData.Columns.Add("phoneno", typeof(string));
            transData.Columns.Add("location", typeof(string));
            transData.Columns.Add("filename1", typeof(string));
            transData.Columns.Add("created_date", typeof(DateTime));
            transData.Columns.Add("createdby", typeof(string));
            transData.Columns.Add("workspace_id", typeof(int));
            transData.Columns.Add("listname", typeof(string));


            int listIdCounter = Convert.ToInt32(dbHandler.ExecuteScalar("SELECT ISNULL(MAX(list_id), 0) + 1 FROM ta_adv_audience_trans"));

            try
            {
                if (fileExtension == ".csv" || fileExtension == ".txt")
                {
                    using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    using (var streamReader = new StreamReader(fileStream))
                    {
                        string delimiter = ",";
                        var firstLine = streamReader.ReadLine();
                        if (firstLine.Contains("|"))
                        {
                            delimiter = "|";
                        }

                        while (!streamReader.EndOfStream)
                        {
                            var line = streamReader.ReadLine();
                            var values = line.Split(delimiter);
                            phoneNumber = values[2].Trim();
                            cleanedValue = Regex.Replace(phoneNumber ?? string.Empty, @"[^\d]", "");

                            if (values[0].ToLower() != "firstname")
                            {
                                if (!string.IsNullOrEmpty(cleanedValue) && uniqueNumbers.Add(cleanedValue))
                                {
                                    DataRow row = transData.NewRow();
                                    row["list_id"] = listIdCounter;
                                    row["firstname"] = values[0].Trim();
                                    row["lastname"] = values[1].Trim();
                                    row["phoneno"] = cleanedValue;
                                    row["location"] = values[3].Trim();
                                    row["filename1"] = fileName;
                                    row["createdby"] = workspaceId;
                                    row["created_date"] = DateTime.Now;
                                    row["workspace_id"] = workspaceId;
                                    row["listname"] = listname;

                                    transData.Rows.Add(row);
                                }
                            }
                        }
                    }
                }
                else if (fileExtension == ".xls" || fileExtension == ".xlsx")
                {
                    using (var package = new ExcelPackage(new FileInfo(filePath)))
                    {
                        ExcelWorksheet worksheet = package.Workbook.Worksheets[0];
                        int rowCount = worksheet.Dimension.Rows;

                        for (int row = 2; row <= rowCount; row++) // Start from row 2 to skip header
                        {
                            phoneNumber = worksheet.Cells[row, 3].Text?.Trim();
                            cleanedValue = Regex.Replace(phoneNumber ?? string.Empty, @"[^\d]", "");

                            if (!string.IsNullOrEmpty(cleanedValue) && uniqueNumbers.Add(cleanedValue))
                            {
                                DataRow rowData = transData.NewRow();
                                rowData["list_id"] = listIdCounter;
                                rowData["firstname"] = string.IsNullOrEmpty(worksheet.Cells[row, 1].Text) ? (object)DBNull.Value : worksheet.Cells[row, 1].Text.Trim();
                                rowData["lastname"] = string.IsNullOrEmpty(worksheet.Cells[row, 2].Text) ? (object)DBNull.Value : worksheet.Cells[row, 2].Text.Trim();
                                rowData["phoneno"] = cleanedValue;
                                rowData["location"] = string.IsNullOrEmpty(worksheet.Cells[row, 4].Text) ? (object)DBNull.Value : worksheet.Cells[row, 4].Text.Trim();
                                rowData["filename1"] = fileName;
                                rowData["createdby"] = workspaceId;
                                rowData["created_date"] = DateTime.Now;
                                rowData["workspace_id"] = workspaceId;
                                rowData["listname"] = listname;

                                transData.Rows.Add(rowData);
                            }
                        }
                    }
                }

                else
                {
                    DataRow dr2 = dtmain.NewRow();
                    dr2["Status"] = "Failed";
                    dr2["Status_Description"] = "Unsupported file format.";
                    dtmain.Rows.Add(dr2);
                    return DtToJSON(dtmain);
                }

                using (SqlConnection cnn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection")))
                {
                    cnn.Open();

                    // Insert all records into ta_adv_audience_trans
                    using (SqlBulkCopy bulkCopy = new SqlBulkCopy(cnn))
                    {
                        bulkCopy.DestinationTableName = "ta_adv_audience_trans";
                        foreach (DataColumn col in transData.Columns)
                        {
                            bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                        }
                        bulkCopy.WriteToServer(transData);
                    }

                    // Step 2: Retrieve data from ta_adv_audience_trans for validation
                    DataTable allTransData = new DataTable();
                    using (SqlCommand cmd = new SqlCommand("GetTransDataByListId", cnn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;  // Set it as a stored procedure
                        cmd.Parameters.AddWithValue("@ListId", listIdCounter);

                        using (SqlDataAdapter adapter = new SqlDataAdapter(cmd))
                        {
                            adapter.Fill(allTransData);
                        }
                    }


                    // Step 3: Separate valid and invalid records
                    DataTable validTransData = transData.Clone(); // Clone structure
                    DataTable invalidTransData = transData.Clone();
                    validTransData.Columns.Add("status", typeof(string));
                    invalidTransData.Columns.Add("status", typeof(string));
                    invalidTransData.Columns.Add("error_message", typeof(string));

                    foreach (DataRow row in allTransData.Rows)
                    {
                        if (!ValidateRow(row, out string errorMessage))
                        {
                            DataRow invalidRow = invalidTransData.NewRow();

                            invalidRow["list_id"] = row["list_id"];
                            invalidRow["firstname"] = row["firstname"];
                            invalidRow["lastname"] = row["lastname"];
                            invalidRow["phoneno"] = row["phoneno"];
                            invalidRow["location"] = row["location"];
                            invalidRow["filename1"] = row["filename1"];
                            invalidRow["created_date"] = DateTime.Now; // ✅ Correctly set DateTime
                            invalidRow["createdby"] = row["createdby"];
                            invalidRow["workspace_id"] = row["workspace_id"];
                            invalidRow["listname"] = row["listname"];
                            invalidRow["status"] = "Invalid"; // ✅ Assign "Valid" status
                            invalidRow["error_message"] = errorMessage;
                            invalidTransData.Rows.Add(invalidRow);
                        }
                        else
                        {
                            DataRow validRow = validTransData.NewRow(); // ✅ Create a new row

                            // ✅ Assign each column manually to avoid mismatches
                            validRow["list_id"] = row["list_id"];
                            validRow["firstname"] = row["firstname"];
                            validRow["lastname"] = row["lastname"];
                            validRow["phoneno"] = row["phoneno"];
                            validRow["location"] = row["location"];
                            validRow["filename1"] = row["filename1"];
                            validRow["created_date"] = DateTime.Now; // ✅ Correctly set DateTime
                            validRow["createdby"] = row["createdby"];
                            validRow["workspace_id"] = row["workspace_id"];
                            validRow["listname"] = row["listname"];
                            validRow["status"] = "Syncing..."; // ✅ Assign "Valid" status

                            validTransData.Rows.Add(validRow); // ✅ Add to validTransData
                        }

                    }

                    // Insert valid records into ta_adv_audience
                    if (validTransData.Rows.Count > 0)
                    {
                        using (SqlBulkCopy bulkCopy = new SqlBulkCopy(cnn))
                        {
                            bulkCopy.DestinationTableName = "ta_adv_audience";
                            foreach (DataColumn col in validTransData.Columns)
                            {
                                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                            }
                            bulkCopy.WriteToServer(validTransData);
                        }
                    }

                    // Insert invalid records into ta_adv_audience_failure
                    if (invalidTransData.Rows.Count > 0)
                    {
                        using (SqlBulkCopy bulkCopy = new SqlBulkCopy(cnn))
                        {
                            bulkCopy.DestinationTableName = "ta_adv_audience_failure";
                            foreach (DataColumn col in invalidTransData.Columns)
                            {
                                bulkCopy.ColumnMappings.Add(col.ColumnName, col.ColumnName);
                            }
                            bulkCopy.WriteToServer(invalidTransData);
                        }
                    }
                }

                DataRow dr1 = dtmain.NewRow();
                dr1["Status"] = "Success";
                dr1["Status_Description"] = "File processed successfully.";
                dtmain.Rows.Add(dr1);
            }
            catch (Exception ex)
            {
                DataRow dr1 = dtmain.NewRow();
                dr1["Status"] = "Failed";
                dr1["Status_Description"] = "Error: " + ex.Message;
                dtmain.Rows.Add(dr1);
            }

            return DtToJSON(dtmain);
        }

        private bool ValidateRow(DataRow row, out string errorMessage)
        {
            List<string> errors = new List<string>();

            // Validate firstname: Should only contain alphabets and not be empty
            string firstname = row["firstname"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(firstname) || !Regex.IsMatch(firstname, "^[A-Za-z]+$"))
            {
                errors.Add("Invalid firstname. It should contain only alphabets (a-z, A-Z). No spaces or special characters allowed.");
            }

            // Validate lastname: Should only contain alphabets and not be empty
            string lastname = row["lastname"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(lastname) || !Regex.IsMatch(lastname, "^[A-Za-z]+$"))
            {
                errors.Add("Invalid lastname. It should contain only alphabets (a-z, A-Z). No spaces or special characters allowed.");
            }

            // Validate phoneno: Should contain only digits and length should be between 10 to 15
            string phoneNumber = row["phoneno"].ToString().Trim().Replace(" ", "").Replace("\u200B", "");
            if (!Regex.IsMatch(phoneNumber, "^\\d{10,15}$"))  // Using double backslashes
            {
                errors.Add("Invalid phoneno. It should contain only numbers and be between 10 to 15 digits long.");
            }

            // Validate location: Should only contain alphabets and not be empty
            string location = row["location"].ToString().Trim();
            if (string.IsNullOrWhiteSpace(location) || !Regex.IsMatch(location, "^[A-Za-z ]+$"))
            {
                errors.Add("Invalid location. It should contain only alphabets (a-z, A-Z). No spaces or special characters allowed.");
            }

            // Concatenate all errors properly
            if (errors.Count > 0)
            {
                errorMessage = string.Join(", ", errors);
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }
        [HttpGet]
        public IActionResult GetFile(string templateId, [FromServices] IDbHandler dbHandler)
        {


            var parameters = new Dictionary<string, object>
 {
     { "@TemplateId", templateId }
 };

            string storedProcedure = "GetMediaBase64ByTemplateId";
            object mediaBase64 = dbHandler.ExecuteScalar(storedProcedure, parameters, CommandType.StoredProcedure);

            string base64String = mediaBase64.ToString();
            try
            {
                // Validate input
                if (string.IsNullOrEmpty(base64String))
                {
                    return BadRequest("Base64 string is required.");
                }

                // Check for metadata in the Base64 string
                string mimeType = null;
                if (base64String.StartsWith("data:"))
                {
                    // Extract MIME type from Base64 metadata
                    mimeType = base64String.Split(';')[0].Split(':')[1];
                    // Remove the metadata prefix to get the actual Base64 content
                    base64String = base64String.Substring(base64String.IndexOf(",") + 1);
                }

                // Convert Base64 string to byte array
                byte[] fileBytes = Convert.FromBase64String(base64String);

                // If no MIME type was found in the Base64 metadata, use file signature detection
                if (mimeType == null && fileBytes.Length >= 4)
                {
                    // Check for magic numbers
                    if (fileBytes[0] == 0x89 && fileBytes[1] == 0x50 && fileBytes[2] == 0x4E && fileBytes[3] == 0x47)
                        mimeType = "image/png"; // PNG
                    else if (fileBytes[0] == 0xFF && fileBytes[1] == 0xD8 && fileBytes[2] == 0xFF)
                        mimeType = "image/jpeg"; // JPEG
                    else if (fileBytes[0] == 0x25 && fileBytes[1] == 0x50 && fileBytes[2] == 0x44 && fileBytes[3] == 0x46)
                        mimeType = "application/pdf"; // PDF
                    else if (fileBytes.Length > 8 && fileBytes[4] == 0x66 && fileBytes[5] == 0x74 && fileBytes[6] == 0x79 && fileBytes[7] == 0x70)
                        mimeType = "video/mp4"; // MP4
                    else
                        return BadRequest("Unsupported file type.");
                }

                // Set the file name based on the detected MIME type
                string fileName = mimeType switch
                {
                    "image/png" => "exampleImage.png",
                    "image/jpeg" => "exampleImage.jpg",
                    "application/pdf" => "exampleDocument.pdf",
                    "video/mp4" => "exampleVideo.mp4",
                    _ => "unknownFile"
                };

                // Return the file as a response
                return File(fileBytes, mimeType, fileName);
            }
            catch (FormatException)
            {
                // Handle invalid Base64 string
                return BadRequest("Invalid Base64 string.");
            }
            catch (Exception ex)
            {
                // Handle other errors
                return BadRequest($"Error: {ex.Message}");
            }
        }
        
        [HttpGet]
        public IActionResult GetImage()
        {
            // Sample Base64 string (replace with your Base64 string)
            string base64String = "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";

            try
            {
                // Convert Base64 string to byte array
                byte[] imageBytes = Convert.FromBase64String(base64String);

                // Return the image as a file response
                return File(imageBytes, "image/png", "exampleImage.png");
                // Change "image/png" to "image/jpeg" if the image is in JPEG format
            }
            catch (Exception ex)
            {
                // Handle error (e.g., invalid Base64 string)
                return BadRequest($"Error: {ex.Message}");
            }
        }

        [HttpGet]
        public IActionResult DeleteAccountByEmail([FromServices] IDbHandler dbHandler, string email)
        {
            try
            {
                // Name of the stored procedure
                string storedProcedure = "DeleteAccountByEmail";
                _logger.LogInformation($"Executing stored procedure: {storedProcedure}");

                // Parameters for the stored procedure
                var parameters = new Dictionary<string, object>
  {
      { "@Email", email }
  };
                _logger.LogInformation($"Executing with parameters: {parameters}");

                // Execute the stored procedure and retrieve the result
                DataTable result = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                // Check if the result contains a message
                if (result.Rows.Count > 0)
                {
                    string spMessage = result.Rows[0]["Message"].ToString(); // Assuming the SP returns a column named "Message"
                    _logger.LogInformation($"Stored procedure message: {spMessage}");

                    // Return the SP message in the API response
                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = spMessage
                    });
                }

                // If no message is returned, send a generic success response
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Account deletion process completed."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to delete the account. Error: {ex.Message}");

                // Handle any errors
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while deleting the account: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult deleteworkspce([FromServices] IDbHandler dbHandler, int workspaceid)
        {
            try
            {
                string deleteworkspaces = "DeleteWorkspaceByWorkSpaceId";

                var Iparameters = new Dictionary<string, object>
{
    { "@workspaceinfoid", workspaceid }
};

                DataTable deleteworkspaceids = dbHandler.ExecuteDataTable(deleteworkspaces, Iparameters, CommandType.StoredProcedure);

                Console.WriteLine(deleteworkspaceids);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "workspace deteleted successfully",
                });
            }
            catch (Exception ex)
            {
                return StatusCode(200, new
                {
                    Status = "Error",
                    Status_Description = $" {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetIndusryList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Define the stored procedure name
                string getindustryList = "GetIndustryList";


                // Execute the stored procedure and retrieve the country list as a DataTable
                DataTable industryList = dbHandler.ExecuteDataTable(getindustryList);

                // Check if the result is null or empty
                if (industryList == null || industryList.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No industries found"
                    });
                }

                // Convert the DataTable to a List of objects with country_id and country_name
                var industryListData = industryList.AsEnumerable().Select(row => new
                {
                    industry_id = row.Field<int>("industry_id"),
                    industry_name = row.Field<string>("industry_name")
                }).ToList();

                // Return the country list with success status
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Industries retrieved successfully",
                    IndustryList = industryListData
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during database interaction
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the industry list: {ex.Message}"
                });
            }
        }


        [HttpGet("{workspaceId:int}")]
        public IActionResult GetCampaignListbyDateRange(int workspaceId, DateTime from_date, DateTime to_date, [FromServices] IDbHandler dbHandler)
        {
            try
            {

                string procedure = "GetCampaignListDetailsbyDateRange";
                _logger.LogInformation($"Executing stored procedure: {procedure} with workspace_id: {workspaceId}");

                DateTime fromDate = from_date.Date;
                DateTime toDate = to_date.Date;

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
                {
                    { "@workspace_id", workspaceId },
                    { "@from_date", fromDate },
                    { "@to_date", toDate }
                };

                // Execute the stored procedure
                DataTable campaignList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (campaignList == null || campaignList.Rows.Count == 0)
                {
                    _logger.LogWarning("No campaigns found for the specified workspace ID.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Campaigns found for the specified workspace ID",
                        CampaignCount = 0 // Return zero count if no rows found
                    });
                }

                // Transform the DataTable to a list of objects
                var campaignListData = campaignList.AsEnumerable().Select(row => new
                {
                    campaign_id = row.Field<int>("campaign_id"),
                    campaign_name = row.Field<string>("campaign_name"),
                    channel_type = row.Field<string>("channel_type"),
                    campaign_budget = row.Field<string>("campaign_budget"),
                    start_date_time = row.Field<DateTime>("start_date_time"),
                    end_date_time = row.Field<DateTime>("end_date_time"),
                    status = row.Field<string>("status"),
                    sent = row.Field<int?>("sent")
                }).ToList();

                _logger.LogInformation("Campaigns retrieved successfully. Data: {@CampaignListData}", campaignListData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Campaigns retrieved successfully",
                    CampaignCount = campaignList.Rows.Count, // Add the row count here
                    CampaignList = campaignListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the campaign list.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the campaign list: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetCombinedStatisticsByDateRange([FromServices] IDbHandler dbHandler, int workspaceId, DateTime from_date, DateTime to_date)
        {
            try
            {
                DataTable chartDetails = null;
                DataTable campaignDetails = null;
                DataTable messagesSentDetails = null;
                DataTable recipientCount = null;

                string procedure1 = "GetDashboardChartDetailsByDateRange";
                _logger.LogInformation("Executing stored procedure1: ", procedure1);

                DateTime fromDate = from_date.Date;
                DateTime toDate = to_date.Date;

                var parameters = new Dictionary<string, object>
        {
            { "@WorkspaceId", workspaceId },
            { "@from_date", fromDate },
            { "@to_date", toDate }
        };
                chartDetails = dbHandler.ExecuteDataTable(procedure1, parameters, CommandType.StoredProcedure);

                _logger.LogInformation("Chart details retrieved: {RowCount} rows.", chartDetails?.Rows.Count ?? 0);

                var ChartData = chartDetails != null && chartDetails.Rows.Count > 0
                ? chartDetails.AsEnumerable().Select(row => new Dictionary<string, object>
                {
            { "date", row.Field<DateTime>("date") }, // Keep date unchanged
            { "email", row.Field<int?>("Email") ?? 0 }, // Convert Email → email (lowercase)
            { "Sms", row.Field<int?>("SMS") ?? 0 }, // Convert SMS → sms
            { "pushNotifications", row.Field<int?>("PushNotification") ?? 0 }, // Convert PushNotifications → pushNotifications
            { "rcSmessages", row.Field<int?>("RCSMessages") ?? 0 }, // Convert RCSmessages → rcSmessages
            { "Whatsapp", row.Field<int?>("WhatsApp") ?? 0 } // Convert WhatsApp → whatsApp
                }).ToList()
                : new List<Dictionary<string, object>> {
            new Dictionary<string, object> {
                { "date", DateTime.Now },
                { "email", 0 },  // Fixed Key Casing
                { "Sms", 0 },
                { "pushNotifications", 0 },
                { "rcSmessages", 0 },
                { "Whatsapp", 0 }
            }
                };

                _logger.LogInformation("Parsed chart details successfully. Data count: {Count}", ChartData.Count);

                string procedure2 = "GetDashboardCampaignDetailsByDateRange";
                _logger.LogInformation("Executing stored procedure2: ", procedure2);

                var parameters2 = new Dictionary<string, object>{
            { "@WorkspaceId", workspaceId },
            { "@from_date", fromDate },
            { "@to_date", toDate }
        };
                campaignDetails = dbHandler.ExecuteDataTable(procedure2, parameters2, CommandType.StoredProcedure);

                _logger.LogInformation("Campaign details retrieved: {RowCount} rows.", campaignDetails?.Rows.Count ?? 0);

                var campaignData = campaignDetails.AsEnumerable().Select(row => new
                {
                    totalCampaigns = row.Field<int?>("TotalCampaigns")
                }).ToList();

                string procedure3 = "GetDashboardMessagesSentDetailsByDateRange";
                _logger.LogInformation("Executing stored procedure3: ", procedure3);

                var parameters3 = new Dictionary<string, object>                {
            { "@WorkspaceId", workspaceId },
            { "@from_date", fromDate },
            { "@to_date", toDate }
        };
                messagesSentDetails = dbHandler.ExecuteDataTable(procedure3, parameters3, CommandType.StoredProcedure);

                _logger.LogInformation("Messages sent details retrieved: {RowCount} rows.", messagesSentDetails?.Rows.Count ?? 0);

                var sentData = messagesSentDetails.AsEnumerable().Select(row => new
                {
                    totalSent = row.Field<int?>("TotalSent")
                }).ToList();

                _logger.LogInformation("Parsed messages sent details successfully. Data count: {Count}", sentData.Count);

                string procedure4 = "GetRecipientCountByWorkspaceId";

                var parameters4 = new Dictionary<string, object>                {
            { "@WorkspaceId", workspaceId }
        };

                recipientCount = dbHandler.ExecuteDataTable(procedure4, parameters4, CommandType.StoredProcedure);



                var recipientCountList = recipientCount.AsEnumerable().Select(row => new
                {
                    recipients = row.Field<int?>("Recipients")
                }).ToList();

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Individual statistics retrieved successfully",
                    ChartDetails = ChartData,
                    CampaignDetails = campaignData,
                    MessagesSentDetails = sentData,
                    RecipientCount = recipientCountList
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the data in GetCombinedStatistics API.");

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the data: {ex.Message}"
                });
            }
        }
        //  [Authorize]

        [HttpGet]
        public IActionResult GetCombinedStatistics([FromServices] IDbHandler dbHandler, int workspaceId)
        {
            try
            {
                DataTable chartDetails = null;
                DataTable campaignDetails = null;
                DataTable messagesSentDetails = null;
                DataTable recipientCount = null;

                string procedure1 = "GetDashboardChartDetails";
                _logger.LogInformation("Executing stored procedure1: ", procedure1);


                var parameters = new Dictionary<string, object>
                {
                    { "@WorkspaceId", workspaceId }
                };
                chartDetails = dbHandler.ExecuteDataTable(procedure1, parameters, CommandType.StoredProcedure);

                _logger.LogInformation("Chart details retrieved: {RowCount} rows.", chartDetails?.Rows.Count ?? 0);

                var ChartData = chartDetails.AsEnumerable().Select(row => new
                {
                    date = row.Field<DateTime>("date"),
                    Email = row.Field<int?>("Email"),
                    SMS = row.Field<int?>("SMS"),
                    PushNotifications = row.Field<int?>("PushNotification"),
                    RCSmessages = row.Field<int?>("RCSMessages"),
                    WhatsApp = row.Field<int?>("WhatsApp")
                }).ToList();

                _logger.LogInformation("Parsed chart details successfully. Data count: {Count}", ChartData.Count);

                string procedure2 = "GetDashboardCampaignDetails";
                _logger.LogInformation("Executing stored procedure2: ", procedure2);

                var parameters2 = new Dictionary<string, object>{
                    { "@WorkspaceId", workspaceId }
                }; 
                campaignDetails = dbHandler.ExecuteDataTable(procedure2, parameters2, CommandType.StoredProcedure);

                _logger.LogInformation("Campaign details retrieved: {RowCount} rows.", campaignDetails?.Rows.Count ?? 0);

                var campaignData = campaignDetails.AsEnumerable().Select(row => new
                {
                    totalCampaigns = row.Field<int?>("TotalCampaigns")
                }).ToList();

                string procedure3 = "GetDashboardMessagesSentDetails";
                _logger.LogInformation("Executing stored procedure3: ", procedure3);

                var parameters3 = new Dictionary<string, object>                {
                    { "@WorkspaceId", workspaceId }
                };
                messagesSentDetails = dbHandler.ExecuteDataTable(procedure3, parameters3, CommandType.StoredProcedure);

                _logger.LogInformation("Messages sent details retrieved: {RowCount} rows.", messagesSentDetails?.Rows.Count ?? 0);

                var sentData = messagesSentDetails.AsEnumerable().Select(row => new
                {
                    totalSent = row.Field<int?>("TotalSent")
                }).ToList();

                _logger.LogInformation("Parsed messages sent details successfully. Data count: {Count}", sentData.Count);

                string procedure4 = "GetRecipientCountByWorkspaceId";

                var parameters4 = new Dictionary<string, object>                {
                    { "@WorkspaceId", workspaceId }
                };

                recipientCount = dbHandler.ExecuteDataTable(procedure4, parameters4, CommandType.StoredProcedure);

                

                var recipientCountList = recipientCount.AsEnumerable().Select(row => new
                {
                    recipients = row.Field<int?>("Recipients")
                }).ToList();

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Individual statistics retrieved successfully",
                    ChartDetails = ChartData,
                    CampaignDetails = campaignData,
                    MessagesSentDetails = sentData,
                    RecipientCount = recipientCountList,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the data in GetCombinedStatistics API.");

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the data: {ex.Message}"
                });
            }
        }



        [HttpPost]
        public async Task<IActionResult> SubscribeToWebhook(int workspaceId)
        {
            try
            {
                _logger.LogInformation("Received request to subscribe to WhatsApp webhook for workspaceId: {workspaceId}", workspaceId);

                var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);
                if (whatsappDetails == null)
                {
                    _logger.LogWarning("WhatsApp account details not found for workspaceId: {workspaceId}", workspaceId);
                    return NotFound(new
                    {
                        Status = "Failure",
                        Status_Description = "WhatsApp account details not found"
                    });
                }
                _logger.LogInformation("Found WhatsApp account details for workspaceId: {workspaceId}. WabaId: {WabaId}", workspaceId, whatsappDetails.WabaId);

                string url = $"{_configuration["facebookApiUrl"]}{whatsappDetails.WabaId}/subscribed_apps";

                // Construct the request body, if necessary
                var requestBody = new
                {

                };

                // Serializing the body to JSON
                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogInformation("Sending POST request to Facebook API with URL: {Url}", url);

                using var httpClient = new HttpClient();

                // Adding Authorization header with Bearer token
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", whatsappDetails.AccessToken);

                var response = await httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to subscribe to webhook for workspaceId: {workspaceId}. StatusCode: {StatusCode}, Reason: {Reason}", workspaceId, response.StatusCode, response.ReasonPhrase);
                    return StatusCode((int)response.StatusCode, new
                    {
                        Status = "Failure",
                        Status_Description = $"Error subscribing: {response.ReasonPhrase}"
                    });
                }

                _logger.LogInformation("Successfully subscribed to WhatsApp webhook for workspaceId: {workspaceId}");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Webhook subscription successful",
                    
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while subscribing to WhatsApp webhook for workspaceId: {workspaceId}", workspaceId);
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while subscribing to webhook: {ex.Message}"
                });
            }
        }


        [HttpDelete]
        public async Task<IActionResult> UnsubscribeFromWebhook(int workspaceId)
        {
            try
            {
                _logger.LogInformation("Received request to unsubscribe from WhatsApp webhook for workspaceId: {workspaceId}", workspaceId);

                var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);
                if (whatsappDetails == null)
                {
                    _logger.LogWarning("WhatsApp account details not found for workspaceId: {workspaceId}", workspaceId);
                    return NotFound(new
                    {
                        Status = "Failure",
                        Status_Description = "WhatsApp account details not found"
                    });
                }
                _logger.LogInformation("Found WhatsApp account details for workspaceId: {workspaceId}. WabaId: {WabaId}", workspaceId, whatsappDetails.WabaId);

                string url = $"{_configuration["facebookApiUrl"]}{whatsappDetails.WabaId}/subscribed_apps";

                // Construct the request body (if needed), or if the API doesn't require one for DELETE, you can omit this
                var requestBody = new
                {
                    // If necessary, you can add any specific parameters
                };

                // Serializing the body to JSON (if required)
                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogInformation("Sending DELETE request to Facebook API with URL: {Url}", url);

                using var httpClient = new HttpClient();

                // Adding Authorization header with Bearer token
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", whatsappDetails.AccessToken);

                var response = await httpClient.DeleteAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to unsubscribe from webhook for workspaceId: {workspaceId}. StatusCode: {StatusCode}, Reason: {Reason}", workspaceId, response.StatusCode, response.ReasonPhrase);
                    return StatusCode((int)response.StatusCode, new
                    {
                        Status = "Failure",
                        Status_Description = $"Error unsubscribing: {response.ReasonPhrase}"
                    });
                }

                _logger.LogInformation("Successfully unsubscribed from WhatsApp webhook for workspaceId: {workspaceId}");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Webhook unsubscription successful"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while unsubscribing from WhatsApp webhook for workspaceId: {workspaceId}", workspaceId);
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while unsubscribing from webhook: {ex.Message}"
                });
            }
        }




        [HttpGet]
        public IActionResult GeWorkspacenamesbyworkspaceid([FromServices] IDbHandler dbHandler, int workspaceid)
        {
            try
            {
                string procedure = "[GetWorkspaceNameByWorkspaceInfoId]";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", procedure);

                var parameters = new Dictionary<string, object>
           {
               { "@Workspaceid", workspaceid }
           };


                DataTable workspacedetails = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (workspacedetails.Rows.Count == 0)
                {
                    _logger.LogInformation("workspace not found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "workspace Not found"
                    });
                }

                var workspaces = workspacedetails.AsEnumerable().Select(row => new
                {

                    workspace = row.Field<string>("workspace_name"),
                }).ToList();
                _logger.LogInformation("workspaces retrieved successfully .{workspaces}", workspaces);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "workspaces retrieved successfully",
                    workspacelist = workspaces
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the workspaces.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the workspaces: {ex.Message}"
                });
            }

        }


        [HttpGet]
        public IActionResult GeWorkspacenamesbyid([FromServices] IDbHandler dbHandler, int accountid, int workpaceid)
        {
            try
            {
                string procedure = "GetWorkspaceNameByPersonalInfoId";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", procedure);
                string procedure1 = "Getpersonalid";

                var parameters = new Dictionary<string, object>
          {
              { "@accountid", accountid }
          };
                object pesonalid = dbHandler.ExecuteScalar(procedure1, parameters, CommandType.StoredProcedure);

                var parameters1 = new Dictionary<string, object>();
                parameters1.Add("@PersonalInfoId", Convert.ToInt32(pesonalid));
                parameters1.Add("@workspaceid", workpaceid);

                DataTable workspacedetails = dbHandler.ExecuteDataTable(procedure, parameters1, CommandType.StoredProcedure);

                if (workspacedetails.Rows.Count == 0)
                {
                    _logger.LogInformation("workspace not found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "workspace Not found"
                    });
                }

                var workspaces = workspacedetails.AsEnumerable().Select(row => new
                {

                    workspace = row.Field<string>("workspace_name"),
                    status = row.Field<string>("billing_status"),
                    paireddate = row.Field<string>("paireddate")
                }).ToList();
                _logger.LogInformation("workspaces retrieved successfully .{workspaces}", workspaces);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "workspaces retrieved successfully",
                    workspacelist = workspaces
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the workspaces.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the workspaces: {ex.Message}"
                });
            }

        }

        //[HttpGet]
        //public IActionResult GetbillingDetails([FromServices] IDbHandler dbHandler, int workspaceid)
        //{
        //    try
        //    {
        //        string storedProcedure = "GetBillingDetailsByWorkspaceInfoId";
        //        _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

        //        var parameters = new Dictionary<string, object>
        //        {
        //            { "@workspace_info_id", workspaceid }
        //        };
        //        _logger.LogInformation("stored parameters: ", parameters);

        //        DataTable billingDetails = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

        //        if (billingDetails == null || billingDetails.Rows.Count == 0)
        //        {
        //            _logger.LogWarning("No billing details found");

        //            return Ok(new
        //            {
        //                Status = "Failure",
        //                Status_Description = "No billing details found",
        //                PlanDetails = new List<object>()
        //            });
        //        }




        //        var billingDetailsData = billingDetails.AsEnumerable().Select((row, index) => new
        //        {

        //            billing_name = row.Field<string>("billing_name"),
        //            amount = row.Field<string>("amount"),
        //            features = row.Field<string>("features"),
        //            symbol = row.Field<string>("symbol"),
        //            currency = row.Field<string>("currency_name")
        //        }).ToArray();


        //        _logger.LogInformation("billing details retrieved successfully: ", billingDetailsData);

        //        return Ok(new
        //        {
        //            Status = "Success",
        //            Status_Description = "billing details retrieved successfully",
        //            billingDetails = billingDetailsData
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError("An error occurred while retrieving the billing details:", ex.Message);

        //        return StatusCode(500, new
        //        {
        //            Status = "Error",
        //            Status_Description = $"An error occurred while retrieving the billing details: {ex.Message}"
        //        });
        //    }
        //}

        [HttpGet]
        public IActionResult GetAgeList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getAgeListProcedure = "GetAgeList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getAgeListProcedure);

                DataTable ageList = dbHandler.ExecuteDataTable(getAgeListProcedure);

                if (ageList == null || ageList.Rows.Count == 0)
                {
                    _logger.LogInformation("No age records found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No age records found"
                    });
                }

                var ageListData = ageList.AsEnumerable().Select(row => new
                {
                    id = row.Field<int>("id"),
                    age = row.Field<string>("age")
                }).ToList();

                _logger.LogInformation("Age records retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Age records retrieved successfully",
                    AgeList = ageListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the age list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the age list: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public IActionResult GetGenderList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getGenderListProcedure = "GetGenderList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getGenderListProcedure);

                DataTable genderList = dbHandler.ExecuteDataTable(getGenderListProcedure);

                if (genderList == null || genderList.Rows.Count == 0)
                {
                    _logger.LogInformation("No gender records found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No gender records found"
                    });
                }

                var genderListData = genderList.AsEnumerable().Select(row => new
                {
                    id = row.Field<int>("id"),
                    gender = row.Field<string>("gender")
                }).ToList();

                _logger.LogInformation("Gender records retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Gender records retrieved successfully",
                    GenderList = genderListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the gender list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the gender list: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public IActionResult GetIncomeLevelList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getIncomeLevelListProcedure = "GetIncomeLevelList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getIncomeLevelListProcedure);

                DataTable incomeLevelList = dbHandler.ExecuteDataTable(getIncomeLevelListProcedure);

                if (incomeLevelList == null || incomeLevelList.Rows.Count == 0)
                {
                    _logger.LogInformation("No income level records found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No income level records found"
                    });
                }

                var incomeLevelListData = incomeLevelList.AsEnumerable().Select(row => new
                {
                    id = row.Field<int>("id"),
                    income_level = row.Field<string>("income_level")
                }).ToList();

                _logger.LogInformation("Income level records retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Income level records retrieved successfully",
                    IncomeLevelList = incomeLevelListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the income level list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the income level list: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetLocationList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getLocationListProcedure = "GetLocationList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getLocationListProcedure);

                DataTable locationList = dbHandler.ExecuteDataTable(getLocationListProcedure);

                if (locationList == null || locationList.Rows.Count == 0)
                {
                    _logger.LogInformation("No location records found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No location records found"
                    });
                }

                var locationListData = locationList.AsEnumerable().Select(row => new
                {
                    id = row.Field<int>("id"),
                    location = row.Field<string>("location"),
                    city = row.Field<string>("city")
                }).ToList();

                _logger.LogInformation("Location records retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Location records retrieved successfully",
                    LocationList = locationListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the location list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the location list: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public IActionResult GetInterestList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getInterestListProcedure = "GetInterestList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getInterestListProcedure);

                DataTable interestList = dbHandler.ExecuteDataTable(getInterestListProcedure);

                if (interestList == null || interestList.Rows.Count == 0)
                {
                    _logger.LogInformation("No interest records found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No interest records found"
                    });
                }

                var interestListData = interestList.AsEnumerable().Select(row => new
                {
                    id = row.Field<int>("id"),
                    interest = row.Field<string>("interest")
                }).ToList();

                _logger.LogInformation("Interest records retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Interest records retrieved successfully",
                    InterestList = interestListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the interest list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the interest list: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public IActionResult GetBehaviourList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getBehaviourListProcedure = "GetBehaviourList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getBehaviourListProcedure);

                DataTable behaviourList = dbHandler.ExecuteDataTable(getBehaviourListProcedure);

                if (behaviourList == null || behaviourList.Rows.Count == 0)
                {
                    _logger.LogInformation("No behaviour records found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No behaviour records found"
                    });
                }

                var behaviourListData = behaviourList.AsEnumerable().Select(row => new
                {
                    id = row.Field<int>("id"),
                    behaviour = row.Field<string>("behaviour")
                }).ToList();

                _logger.LogInformation("Behaviour records retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Behaviour records retrieved successfully",
                    BehaviourList = behaviourListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the behaviour list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the behaviour list: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public IActionResult GetDeviceList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getDeviceListProcedure = "GetDeviceList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getDeviceListProcedure);

                DataTable deviceList = dbHandler.ExecuteDataTable(getDeviceListProcedure);

                if (deviceList == null || deviceList.Rows.Count == 0)
                {
                    _logger.LogInformation("No device records found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No device records found"
                    });
                }

                var deviceListData = deviceList.AsEnumerable().Select(row => new
                {
                    id = row.Field<int>("id"),
                    device = row.Field<string>("device")
                }).ToList();

                _logger.LogInformation("Device records retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Device records retrieved successfully",
                    DeviceList = deviceListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the device list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the device list: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public IActionResult GetOSDeviceList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string getOSDeviceListProcedure = "GetOSDeviceList";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getOSDeviceListProcedure);

                DataTable osDeviceList = dbHandler.ExecuteDataTable(getOSDeviceListProcedure);

                if (osDeviceList == null || osDeviceList.Rows.Count == 0)
                {
                    _logger.LogInformation("No OS device records found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No OS device records found"
                    });
                }

                var osDeviceListData = osDeviceList.AsEnumerable().Select(row => new
                {
                    id = row.Field<int>("id"),
                    os_device = row.Field<string>("os_device")
                }).ToList();

                _logger.LogInformation("OS device records retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "OS device records retrieved successfully",
                    OSDeviceList = osDeviceListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the OS device list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the OS device list: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetReachPeopleFromList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Stored procedure name
                string storedProcedure = "GetCampaignFromCountries";

                // Log execution of the stored procedure
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", storedProcedure);

                // Execute the stored procedure and get the result as DataTable
                DataTable reachPeopleFromList = dbHandler.ExecuteDataTable(storedProcedure);

                // Check if the result is empty
                if (reachPeopleFromList == null || reachPeopleFromList.Rows.Count == 0)
                {
                    _logger.LogInformation("No data found in the database for Reach People From.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No data found"
                    });
                }

                // Map the data to a list
                var reachPeopleFromData = reachPeopleFromList.AsEnumerable().Select(row => new
                {
                    country_id = row.Field<int>("country_id"),
                    country_name = row.Field<string>("country_name")
                }).ToList();

                // Log retrieved data
                _logger.LogInformation("Data retrieved successfully: {reachPeopleFromData}", reachPeopleFromData);

                // Return success response
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Data retrieved successfully",
                    ReachPeopleFromList = reachPeopleFromData
                });
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogError(ex, "An error occurred while retrieving data for Reach People From.");

                // Return error response
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving data: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetReachPeopleToList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Stored procedure name
                string storedProcedure = "GetReachPeopleToList";

                // Log execution of the stored procedure
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", storedProcedure);

                // Execute the stored procedure and get the result as DataTable
                DataTable reachPeopleToList = dbHandler.ExecuteDataTable(storedProcedure);

                // Check if the result is empty
                if (reachPeopleToList == null || reachPeopleToList.Rows.Count == 0)
                {
                    _logger.LogInformation("No data found in the database for Reach People To.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No data found"
                    });
                }

                // Map the data to a list
                var reachPeopleToData = reachPeopleToList.AsEnumerable().Select(row => new
                {
                    country_id = row.Field<int>("country_id"),
                    country_name = row.Field<string>("country_name")
                }).ToList();

                // Log retrieved data
                _logger.LogInformation("Countries retrieved successfully: {reachPeopleToData}", reachPeopleToData);

                // Return success response
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Data retrieved successfully",
                    ReachPeopleToList = reachPeopleToData
                });
            }
            catch (Exception ex)
            {
                // Log the error
                _logger.LogError(ex, "An error occurred while retrieving data for Reach People To.");

                // Return error response
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving data: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetMetaTemplateDetailsById([FromServices] IDbHandler dbHandler, string template_id)
        {
            try
            {
                string procedure = "GetMetaTemplateDetailsById";

                _logger.LogInformation("Executing stored procedure:", procedure);

                var parameters = new Dictionary<string, object>
                {
                    { "@TemplateId", template_id }
                };

                // Execute the stored procedure
                DataTable templateDetails = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);



                if (templateDetails.Rows.Count == 0)
                {
                    _logger.LogWarning("Template Not found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Template Not found"
                    });
                }

                var TemplateData = templateDetails.AsEnumerable().Select(row => new
                {
                    template_id = row.Field<string>("template_id"),
                    template_name = row.Field<string>("template_name"),
                    channel_type = row.Field<string>("channel_type"),
                    channel_id = row.Field<int>("channel_id"),
                    status = row.Field<string>("status"),
                    last_edited = row.Field<DateTime?>("last_updated"),
                    components = row.Field<string>("components"),
                    language = row.Field<string>("language"),
                    category = row.Field<string>("category"),
                    sub_category = row.Field<string>("subcategory"),
                    mediaBase64 = row.Field<string>("mediaBase64")

                }).ToList();

                _logger.LogInformation("Templates retrieved successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Templates retrieved successfully",
                    TemplateDetails = TemplateData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the template details: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the template details: {ex.Message}"
                });
            }
        }

        //sarvash
        [HttpPost]
        public async Task<IActionResult> RegisterWhatsappNumber(int workspaceId, string phoneId)
        {
            try
            {
                _logger.LogInformation("Received request to subscribe to WhatsApp webhook for workspaceId: {workspaceId}", workspaceId);

                var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);
                if (whatsappDetails == null)
                {
                    _logger.LogWarning("WhatsApp account details not found for workspaceId: {workspaceId}", workspaceId);
                    return NotFound(new
                    {
                        Status = "Failure",
                        Status_Description = "WhatsApp account details not found"
                    });
                }
                _logger.LogInformation("Found WhatsApp account details for workspaceId: {workspaceId}. WabaId: {WabaId}", workspaceId, whatsappDetails.WabaId);

                string url = $"{_configuration["facebookApiUrl"]}{phoneId}/register";

                // Construct the request body, if necessary
                var requestBody = new
                {
                };

                // Serializing the body to JSON
                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogInformation("Sending POST request to Facebook API with URL: {Url}", url);

                using var httpClient = new HttpClient();

                // Adding Authorization header with Bearer token
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", whatsappDetails.AccessToken);

                var response = await httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to register phone number");
                    return StatusCode((int)response.StatusCode, new
                    {
                        Status = "Failure",
                        Status_Description = $"Error registering phone number"
                    });
                }

                _logger.LogInformation("Successfully registered phone number");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Successfully registered phone number",

                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while registering phone number");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while registering phone number: {ex.Message}"
                });
            }
        }


        //sarvash
        [HttpPost]
        public async Task<IActionResult> DeRegisterWhatsappNumber(int workspaceId, string phoneId)
        {
            try
            {
                _logger.LogInformation("Received request to subscribe to WhatsApp webhook for workspaceId: {workspaceId}", workspaceId);

                var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);
                if (whatsappDetails == null)
                {
                    _logger.LogWarning("WhatsApp account details not found for workspaceId: {workspaceId}", workspaceId);
                    return NotFound(new
                    {
                        Status = "Failure",
                        Status_Description = "WhatsApp account details not found"
                    });
                }
                _logger.LogInformation("Found WhatsApp account details for workspaceId: {workspaceId}. WabaId: {WabaId}", workspaceId, whatsappDetails.WabaId);

                string url = $"{_configuration["facebookApiUrl"]}{phoneId}/deregister";

                // Construct the request body, if necessary
                var requestBody = new
                {
                };

                // Serializing the body to JSON
                var jsonContent = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                _logger.LogInformation("Sending POST request to Facebook API with URL: {Url}", url);

                using var httpClient = new HttpClient();

                // Adding Authorization header with Bearer token
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", whatsappDetails.AccessToken);

                var response = await httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Failed to register phone number");
                    return StatusCode((int)response.StatusCode, new
                    {
                        Status = "Failure",
                        Status_Description = $"Error registering phone number"
                    });
                }

                _logger.LogInformation("Successfully registered phone number");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Successfully registered phone number",

                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while registering phone number");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while registering phone number: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreatePaymentLink([FromServices] IDbHandler dbHandler, [FromBody] AdvertiserAccountModel.PaymentLinkRequest request)
        {
            try
            {
                // Set the Stripe secret key
                StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

                // Create a payment link
                var service = new PaymentLinkService();
                var paymentLinkOptions = new PaymentLinkCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<PaymentLinkLineItemOptions>
        {
            new PaymentLinkLineItemOptions
            {
                Price = request.PriceId,
                Quantity = request.Quantity
            }
        },
                    AutomaticTax = new PaymentLinkAutomaticTaxOptions { Enabled = true },
                    BillingAddressCollection = "required", // Example setting; adjust as needed
                    AfterCompletion = new PaymentLinkAfterCompletionOptions
                    {
                        Type = "redirect",
                        Redirect = new PaymentLinkAfterCompletionRedirectOptions
                        {
                            Url = "https://travelad.agnointel.ai/settings/Billing" // Redirect to success URL
                        }
                    }
                    //                Metadata = new Dictionary<string, string>
                    //{
                    //    { "payment_reference", Guid.NewGuid().ToString() } // Unique identifier
                    //}
                };
                //            var paymentIntentOptions = new PaymentIntentCreateOptions
                //            {

                //                Metadata = new Dictionary<string, string>
                //{
                //    { "payment_reference", Guid.NewGuid().ToString() } // Unique identifier
                //}

                //            };

                //            var paymentIntentService = new PaymentIntentService();
                //            var paymentIntent = await paymentIntentService.CreateAsync(paymentIntentOptions);
                var paymentLink = await service.CreateAsync(paymentLinkOptions);
                Console.WriteLine($"Payment link created: {paymentLink.Url}, Currency: {paymentLink.Currency}");
                //   Console.WriteLine($"Price ID from PaymentLink metadata: {paymentLink.Metadata["payment_reference"]}");
                // Return the full payment link response

                string storedprocedure = "UpdateBillingFeaturesURL";
                var parameter_1 = new Dictionary<string, object>();
                parameter_1.Add("@URL", paymentLink.Url);
                parameter_1.Add("@PriceId", request.PriceId);
                DataTable paymenturl = dbHandler.ExecuteDataTable(storedprocedure, parameter_1, CommandType.StoredProcedure);
                return Ok(paymentLink.Url);
            }
            catch (StripeException ex)
            {
                // Handle Stripe-specific errors
                return BadRequest(new { Error = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetCurrencyById(int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetCurrencyNameByWorkspaceID";
                _logger.LogInformation($"Executing stored procedure: {procedure} with workspace_id: {workspaceId}");

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
{
    { "@WorkspaceInfoID", workspaceId }
};

                // Execute the stored procedure
                DataTable currencyList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (currencyList == null || currencyList.Rows.Count == 0)
                {
                    _logger.LogWarning("No Currency found for the specified workspace ID.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Currency found for the specified workspace ID",
                        //CampaignCount = 0 // Return zero count if no rows found
                    });
                }

                // Transform the DataTable to a list of objects
                var workspaceCurrencyData = currencyList.AsEnumerable().Select(row => new
                {
                    workspace_info_id = row.Field<int>("workspace_info_id"),
                    workspace_name = row.Field<string>("workspace_name"),
                    country_id = row.Field<int?>("country_id"),
                    country_name = row.Field<string>("country_name"),
                    currency_name = row.Field<string>("currency_name"),
                    symbol = row.Field<string>("symbol"),

                }).ToList();

                _logger.LogInformation("currency retrieved successfully. Data: {@workspaceCurrencyData}", workspaceCurrencyData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Data retrieved successfully",
                    CurrencyCount = currencyList.Rows.Count, // Add the row count here
                    CurrencyList = workspaceCurrencyData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the currency.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the currency: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetCampaignColumns(int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetCampaignColumns";
                _logger.LogInformation($"Executing stored procedure: {procedure} with WorkspaceId: {workspaceId}");

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
        {
            { "@WorkspaceId", workspaceId }
        };

                // Execute the stored procedure
                DataTable columnData = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (columnData == null || columnData.Rows.Count == 0)
                {
                    _logger.LogWarning("No columns found for the specified workspace ID.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No columns found for the specified workspace ID"
                    });
                }

                // Parse the Columns (JSON) from the result
                var columnPreferences = columnData.Rows[0].Field<string>("Columns");

                _logger.LogInformation("Column preferences retrieved successfully for WorkspaceId {WorkspaceId}", workspaceId);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Data retrieved successfully",
                    Columns = columnPreferences
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving column preferences.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving column preferences: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public IActionResult UpsertCampaignColumns([FromBody] UpsertCampaignColumnsRequest request, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "UpsertCampaignColumns";
                _logger.LogInformation($"Executing stored procedure: {procedure} with WorkspaceId: {request.WorkspaceId}");

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
        {
            { "@WorkspaceId", request.WorkspaceId },
            { "@Columns", request.Columns }
        };

                // Execute the stored procedure
                dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);

                _logger.LogInformation("Column preferences updated successfully for WorkspaceId {WorkspaceId}", request.WorkspaceId);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Column preferences updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating column preferences.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while updating column preferences: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public object InsertPendingInvitedMembers(
            List<TravelAd_Api.Models.AdvertiserAccountModel.PendingInvitedMembers> members,
            [FromServices] IDbHandler dbHandler)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred.",
                AlreadyInvited = new List<string>(),
                SuccessfullyInvited = new List<string>()
            };

            try
            {
                var alreadyInvitedEmails = new List<string>();
                var successfullyInvitedEmails = new List<string>();

                foreach (var member in members)
                {

                    string getPersonalInfoIdQuery = "GetPersonalIdByEmail";
                    _logger.LogInformation("Executing stored procedure: {getPersonalInfoIdQuery}", getPersonalInfoIdQuery);

                    var personalInfoParams = new Dictionary<string, object>
             {
                 { "@Email", member.InvitedBy }
             };

                    _logger.LogInformation("Stored procedure parameters: {personalInfoParams}", personalInfoParams);

                    var personalInfoIdResult = dbHandler.ExecuteScalar(getPersonalInfoIdQuery, personalInfoParams, CommandType.StoredProcedure);


                    // Updated query
                    string query = "DECLARE @ReturnValue INT; EXEC @ReturnValue = InsertPendingInvitedMember @workspace_id, @email, @role, @invited_at, @status, @is_accepted, @invited_by, @expires_at; SELECT @ReturnValue AS ReturnValue;";

                    var expiresAt = member.InvitedAt?.AddMonths(1)
                        ?? throw new ArgumentNullException("InvitedAt", "InvitedAt cannot be null.");
                    // Updated parameters
                    var parameters = new Dictionary<string, object>
{
    { "@workspace_id", member.WorkspaceId },
    { "@email", member.Email },
    { "@role", member.Role },
    { "@invited_at", member.InvitedAt },
    { "@status", member.Status },
    { "@is_accepted", member.IsAccepted },
    { "@invited_by", personalInfoIdResult },
    { "@expires_at",expiresAt},
};

                    // Execute the query
                    var result = dbHandler.ExecuteScalar(query, parameters, CommandType.Text);

                    // Interpret the result
                    if (result.ToString() == "1")
                    {
                        // Handle already invited case
                        alreadyInvitedEmails.Add(member.Email);
                        _logger.LogInformation("Member already invited: {email}", member.Email);
                    }
                    else if (result.ToString() == "0")
                    {
                        // Handle successfully invited case
                        successfullyInvitedEmails.Add(member.Email);
                        _logger.LogInformation("Member successfully invited: {email}", member.Email);
                    }
                    else
                    {
                        _logger.LogWarning("Unexpected return value: {returnValue}", result);
                    }

                }

                // Update the response with success information
                response = new
                {
                    Status = "Success",
                    Status_Description = "Process completed successfully.",
                    AlreadyInvited = alreadyInvitedEmails,
                    SuccessfullyInvited = successfullyInvitedEmails
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"An exception occurred: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = $"An exception occurred: {ex.Message}",
                    AlreadyInvited = new List<string>(),
                    SuccessfullyInvited = new List<string>()
                };
            }

            return response;
        }

        //Send invitation module

        [HttpGet]
        public IActionResult GetMembersByWorkspaceId([FromServices] IDbHandler dbHandler, [FromQuery] string workspaceId)
        {
            try
            {
                string storedProcedureName = "GetMembersByWorkspaceId";

                _logger.LogInformation("Executing stored procedure: {storedProcedureName}", storedProcedureName);

                var parameters = new Dictionary<string, object>
{
    { "@WorkspaceId", workspaceId }
};
                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                DataTable membersTable = dbHandler.ExecuteDataTable(storedProcedureName, parameters, CommandType.StoredProcedure);

                if (membersTable == null || membersTable.Rows.Count == 0)
                {
                    _logger.LogWarning("No members found for the specified workspace ID");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No members found for the specified workspace ID"
                    });
                }

                var membersList = membersTable.AsEnumerable().Select(row => new
                {
                    member_id = row.Field<int>("member_id"),
                    email = row.Field<string>("email"),
                    role = row.Field<string>("role_name"),
                    invited_at = row.Field<DateTime>("invited_at"),
                    status = row.Field<string>("status"),
                    expires_date = row.Field<DateTime>("expires_at"),
                    image = row.Field<string>("profile_image")
                }).ToList();

                _logger.LogInformation("Members retrieved successfully: {membersList}", membersList);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Members retrieved successfully",
                    members = membersList
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving members: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving members: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetPendingMemberById([FromServices] IDbHandler dbHandler, [FromQuery] string MemberId)
        {
            try
            {
                string storedProcedureName = "GetPendingMemberById";

                _logger.LogInformation("Executing stored procedure: {storedProcedureName}", storedProcedureName);

                var parameters = new Dictionary<string, object>
        {
            { "@MemberId", MemberId }
        };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                DataTable membersTable = dbHandler.ExecuteDataTable(storedProcedureName, parameters, CommandType.StoredProcedure);

                if (membersTable == null || membersTable.Rows.Count == 0)
                {
                    _logger.LogWarning("No member found for the specified ID");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No members found for the specified member ID"
                    });
                }

                var membersList = membersTable.AsEnumerable().Select(row => new
                {
                    member_id = row.Field<int>("member_id"),
                    email = row.Field<string>("email"),
                    role = row.Field<string>("role_name"),
                    invited_at = row.Field<DateTime>("invited_at"),
                    status = row.Field<string>("status"),
                    expires_date = row.Field<DateTime>("expires_at")
                }).ToList();

                _logger.LogInformation("Member retrieved successfully: {membersList}", membersList);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Member retrieved successfully",
                    members = membersList
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving members: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving members: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetUserRoleById([FromServices] IDbHandler dbHandler, [FromQuery] string Email)
        {
            try
            {
                string storedProcedureName = "GetUserRoleById";

                _logger.LogInformation("Executing stored procedure: {storedProcedureName}", storedProcedureName);

                var parameters = new Dictionary<string, object>
        {
            { "@Email", Email }
        };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                DataTable membersTable = dbHandler.ExecuteDataTable(storedProcedureName, parameters, CommandType.StoredProcedure);

                if (membersTable == null || membersTable.Rows.Count == 0)
                {
                    _logger.LogWarning("No user found for the specified ID");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No User found for the specified member ID"
                    });
                }

                var userRoleList = membersTable.AsEnumerable().Select(row => new
                {
                    UWRid = row.Field<int>("id"),
                    role_id = row.Field<int>("role_id"),
                }).ToList();

                _logger.LogInformation("User role retrieved successfully: {membersList}", userRoleList);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "User Role retrieved successfully",
                    userRole = userRoleList
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving members: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving members: {ex.Message}"
                });
            }
        }


        [HttpPut]
        public IActionResult UpdatePendingInvitedMembers(
            int member_id,
            [FromBody] PendingInvitedMembers member,
            [FromServices] IDbHandler dbHandler)
        {
            if (member == null)
            {
                return BadRequest(new { Status = "Error", Status_Description = "The member field is required." });
            }

            try
            {
                var expiresAt = member.InvitedAt?.AddMonths(1)
                    ?? throw new ArgumentNullException("InvitedAt", "InvitedAt cannot be null.");

                string query = "UpdateMemberDetails";
                var parameters = new Dictionary<string, object>
        {
            { "@member_id", member_id },
            { "@role", member.Role },
            { "@invited_at", member.InvitedAt },
            { "@expires_at", expiresAt },
            { "@status", member.Status },
        };

                var result = dbHandler.ExecuteScalar(query, parameters, CommandType.StoredProcedure);

                if (result != null && int.TryParse(result.ToString(), out int rowsAffected) && rowsAffected > 0)
                {
                    return Ok(new { Status = "Success", Status_Description = "Invite Updated Successfully" });
                }

                return Ok(new { Status = "Failure", Status_Description = "Failed to update the Invite." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Status = "Error", Status_Description = ex.Message });
            }
        }

        [HttpDelete]
        public IActionResult DeletePendingMemberById([FromServices] IDbHandler dbHandler, int MemberId)
        {
            try
            {
                string deletePendingMemberById = "DeleteInviteMembersById";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", deletePendingMemberById);


                var parameters = new Dictionary<string, object>
        {
            { "@MemberId", MemberId }
        };

                int rowsAffected = dbHandler.ExecuteNonQuery(deletePendingMemberById, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("Member not found or could not be deleted");
                    return Ok(new
                    {

                        Status = "Failure",
                        Status_Description = "Member not found or could not be deleted"
                    });
                }


                _logger.LogInformation("Member deleted successfully");
                return Ok(new

                {
                    Status = "Success",
                    Status_Description = "Member deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while deleting the Member: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while deleting the member: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetUserDetailsByWorkspace([FromServices] IDbHandler dbHandler, string WorkspaceId)
        {
            try
            {
                string procedure = "GetUserDetailsByWorkspace";
                _logger.LogInformation("Executing stored procedure: {procedure}", procedure);
                var parameters = new Dictionary<string, object>
      {
          { "@workspaceId", WorkspaceId }
      };

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                DataTable UsersWorkspaceList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);
                if (UsersWorkspaceList == null || UsersWorkspaceList.Rows.Count == 0)
                {
                    _logger.LogWarning("No Users found for Workspace");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Users found for Workspace"
                    });
                }

                var UsersWorkspaceListData = UsersWorkspaceList.AsEnumerable().Select(row => new
                {
                    first_name = row.Field<string>("first_name"),
                    last_name = row.Field<string>("last_name"),
                    email = row.Field<string>("email"),
                    joined_at = row.Field<DateTime>("joined_date"),
                    role = row.Field<string>("role_name"),
                    member_id = row.Field<int>("member_id"),
                    image = row.Field<string>("profile_image")
                }).ToList();

                _logger.LogInformation("Users workspace retrieved successfully , Response: {Response}", UsersWorkspaceListData);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Users workspace retrieved successfully",
                    UsersWorkspaceList = UsersWorkspaceListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the users workspace list: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the users workspace list: {ex.Message}"
                });
            }
        }

        [HttpPut]
        public IActionResult UpdateUserRole([FromServices] IDbHandler dbHandler, string WorkspaceId, string Email, int RoleId)
        {
            DataTable dtmain = new DataTable();
            dtmain.Columns.Add("Status");
            dtmain.Columns.Add("Status_Description");
            try
            {
                string getAccountIDQuery = "GetAccountIdByEmail";
                var getAccountIDParams = new Dictionary<string, object>
       {
           { "@Email", Email }
       };

                var userAccountId = dbHandler.ExecuteScalar(getAccountIDQuery, getAccountIDParams, CommandType.StoredProcedure);

                string Query = "UpdateUserRole";
                var QuertParams = new Dictionary<string, object>
               {
                   {"@WorkspaceId",WorkspaceId },
                   {"@UserAccountId",userAccountId },
                   {"@RoleId", RoleId}
               };

                int rowsAffected = dbHandler.ExecuteNonQuery(Query, QuertParams, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("User Role not found or could not be updated");
                    return Ok(new
                    {

                        Status = "Failure",
                        Status_Description = "User Role not found or could not be updated"
                    });
                }
                else
                {
                    _logger.LogInformation("User role updated successfully");
                    return Ok(new

                    {
                        Status = "Success",
                        Status_Description = "User role updated successfully"
                    });
                }


            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while updating user role: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while updating user role: {ex.Message}"
                });
            }



        }

        [HttpPut]
        public IActionResult UpdateWorkSpaceName([FromBody] WorkspaceUpdateModel model)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "Workspace ID not found or update failed."
            };

            try
            {
                string updateQuery = "UpdateWorkSpaceName"; // Stored procedure name
                _logger.LogInformation("Executing stored procedure: {updateQuery}", updateQuery);

                // Parameters for the stored procedure
                var parameters = new Dictionary<string, object>
     {
         { "@workspace_info_id", model.WorkspaceId }, // Workspace ID
         { "@new_workspace_name", model.NewWorkspaceName } // New workspace name
     };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                // Execute the stored procedure
                _dbHandler.ExecuteNonQuery(updateQuery, parameters, CommandType.StoredProcedure);

                _logger.LogInformation($"Workspace name for Workspace ID {model.WorkspaceId} updated successfully.");
                response = new
                {
                    Status = "Success",
                    Status_Description = $"Workspace name for Workspace ID {model.WorkspaceId} updated successfully."
                };
            }
            catch (SqlException ex)
            {
                _logger.LogError($"Database error: {ex.Message}");
                Console.WriteLine($"SQL Error: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = $"Database error: {ex.Message}"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"An exception occurred while processing the request: {ex.Message}");
                Console.WriteLine($"Error: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = "An exception occurred while processing the request."
                };
            }

            return Ok(response);
        }


        [HttpDelete]
        public IActionResult DisconnectWhatsappAccount([FromServices] IDbHandler dbHandler, int WorkspaceId)
        {
            try
            {
                string procedure = "DeleteWhatsappAccountDetails";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);


                var parameters = new Dictionary<string, object>
        {
            { "@workspace_id", WorkspaceId }
        };

                int rowsAffected = dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("whatsapp account not found or could not be deleted");
                    return Ok(new
                    {

                        Status = "Failure",
                        Status_Description = "whatsapp account not found or could not be deleted"
                    });
                }


                _logger.LogInformation("whatsapp account deleted successfully");
                return Ok(new

                {
                    Status = "Success",
                    Status_Description = "Member deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while deleting the whatsapp account: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while deleting the whatsapp account: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetCurrencyNames([FromServices] IDbHandler dbHandler)
        {
            try
            {
                string storedProcedureName = "GetCurrencyNames";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", storedProcedureName);

                DataTable currencyList = dbHandler.ExecuteDataTable(storedProcedureName);
                if (currencyList == null || currencyList.Rows.Count == 0)
                {
                    _logger.LogWarning("No distinct currencies found");

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No distinct currencies found"
                    });
                }

                // Extract the raw data without creating custom objects
                var currencyListData = currencyList.AsEnumerable()
                                                   .Select(row => row.Field<string>("currency_name"))
                                                   .ToList();

                _logger.LogInformation("Currencies retrieved successfully: {CurrencyList}", currencyListData);

                // Return the currency list with success status
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Currencies retrieved successfully",
                    CurrencyList = currencyListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the currency list");

                // Handle any exceptions that occur during database interaction
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the currency list: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetBillingFeatures([FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Define the stored procedure name
                string storedProcedureName = "SelectBillingFeature";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", storedProcedureName);

                // Execute the stored procedure and get the result as a DataTable
                DataTable billingFeatureList = dbHandler.ExecuteDataTable(storedProcedureName);

                // Check if the result is empty
                if (billingFeatureList == null || billingFeatureList.Rows.Count == 0)
                {
                    _logger.LogWarning("No billing features found");

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No billing features found"
                    });
                }

                var billingFeatureData = billingFeatureList.AsEnumerable().Select(row => new
                {
                    BillingId = row.Field<int>("billing_id"),
                    Messages = row.Field<string>("messages"),
                    Price = row.Field<string>("price"),
                    CountrySymbol = row.Field<string>("country_symbol"),
                    Name = row.Field<string>("name"),
                    UpdatedAt = row.Field<DateTime?>("updated_at"),
                    Status = row.Field<string>("status")
                }).ToList();

                _logger.LogInformation("Billing features retrieved successfully: {BillingFeatureList}", billingFeatureData);


                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Billing features retrieved successfully",
                    BillingFeatureList = billingFeatureData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the billing features");

                // Handle exceptions
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the billing features: {ex.Message}"
                });
            }
        }

        [HttpPut]
        public IActionResult UpdateBillingFeature(
          [FromBody] AdvertiserAccountModel.BillingFeatureUpdateRequest request,
          [FromServices] IDbHandler dbHandler)
        {
            if (request == null || request.BillingId <= 0)
            {
                return BadRequest(new
                {
                    Status = "Error",
                    Status_Description = "Invalid request. BillingId must be greater than 0."
                });
            }

            try
            {
                // Define the stored procedure name
                const string storedProcedureName = "UpdateBillingFeature";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName} with BillingId: {BillingId}", storedProcedureName, request.BillingId);

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
      {
          { "@Id", request.BillingId },
          { "@Name", request.Name ?? (object)DBNull.Value },
          { "@Messages", request.Messages ?? (object)DBNull.Value },
          { "@Price", request.Price ?? (object)DBNull.Value },
          { "@PerMessage", request.Permessage ?? (object)DBNull.Value },
          { "@CountrySymbol", request.CountrySymbol ?? (object)DBNull.Value },
      };

                _logger.LogDebug("Parameters: {@Parameters}", parameters);

                // Execute the stored procedure
                int rowsAffected = dbHandler.ExecuteNonQuery(storedProcedureName, parameters, CommandType.StoredProcedure);


                _logger.LogInformation("Billing feature updated successfully. BillingId: {BillingId}", request.BillingId);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Billing feature updated successfully.",
                    BillingId = request.BillingId,
                    RowsAffected = rowsAffected
                });

            }
            catch (SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "Database error occurred while updating the billing feature. BillingId: {BillingId}", request.BillingId);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = "A database error occurred. Please contact support.",
                    ErrorDetails = sqlEx.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unexpected error occurred while updating the billing feature. BillingId: {BillingId}", request.BillingId);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = "An unexpected error occurred. Please try again later.",
                    ErrorDetails = ex.Message
                });
            }
        }



      //  [HttpGet]
      //  public IActionResult GetBillingDetailsById(int BillingId, [FromServices] IDbHandler dbHandler)
      //  {
      //      try
      //      {

      //          string storedProcedureName = "GetBillingDetailsById";
      //          _logger.LogInformation("Executing stored procedure: {StoredProcedureName} with BillingId: {BillingId}", storedProcedureName, BillingId);

      //          var parameters = new Dictionary<string, object>
      //{
      //    { "@BillingId", BillingId }
      //};

      //          // Execute the stored procedure and get the result as a DataTable
      //          DataTable billingDetailsTable = dbHandler.ExecuteDataTable(storedProcedureName, parameters, CommandType.StoredProcedure);

      //          // Check if the result is empty
      //          if (billingDetailsTable == null || billingDetailsTable.Rows.Count == 0)
      //          {
      //              _logger.LogWarning("No billing details found for BillingId: {BillingId}", BillingId);

      //              return Ok(new
      //              {
      //                  Status = "Failure",
      //                  Status_Description = "No billing details found"
      //              });
      //          }

      //          // Map the DataTable result to an object
      //          var billingDetails = billingDetailsTable.AsEnumerable().Select(row => new
      //          {
      //              BillingId = row.Field<int>("billing_id"),
      //              Messages = row.Field<string>("messages"),
      //              Price = row.Field<string>("price"),
      //              CountrySymbol = row.Field<string>("country_symbol"),
      //              Name = row.Field<string>("name"),
      //              Permessage = row.Field<string>("per_message"),
      //              TotalAmount = row.Field<string>("total_amount"),
      //              Url = row.Field<string>("url"),
      //              CreatedAt = row.Field<DateTime?>("created_at"),
      //              UpdatedAt = row.Field<DateTime?>("updated_at")
      //          }).FirstOrDefault();

      //          _logger.LogInformation("Billing details retrieved successfully for BillingId: {BillingId}", BillingId);

      //          return Ok(new
      //          {
      //              Status = "Success",
      //              Status_Description = "Billing details retrieved successfully",
      //              BillingDetails = billingDetails
      //          });
      //      }
      //      catch (Exception ex)
      //      {
      //          _logger.LogError(ex, "An error occurred while retrieving billing details for BillingId: {BillingId}", BillingId);

      //          // Handle exceptions
      //          return StatusCode(500, new
      //          {
      //              Status = "Error",
      //              Status_Description = $"An error occurred while retrieving billing details: {ex.Message}"
      //          });
      //      }
      //  }

        [HttpPost]
        public IActionResult InsertBillingFeature([FromBody] AdvertiserAccountModel.BillingFeatureUpdateRequest request, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Modify the input values
                string modifiedMessages = $" {request.Messages} messages";
                string modifiedPrice = $"{request.Price}";

                // Define the stored procedure name
                string storedProcedureName = "InsertBillingFeature";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", storedProcedureName);


                var parameters = new Dictionary<string, object>
      {
          {"@Name",request.Name },
          { "@Messages", modifiedMessages },
          { "@Price", modifiedPrice },
          { "@CountrySymbol", request.CountrySymbol },
                   { "@PerMessage", request.Permessage }
      };

                int rowsAffected = dbHandler.ExecuteNonQuery(storedProcedureName, parameters, CommandType.StoredProcedure);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Data inserted successfully"
                });

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while inserting billing feature");

                // Handle exceptions
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred: {ex.Message}"
                });
            }
        }

        //[HttpPost]
        //public async Task<IActionResult> SendOtpWhatsappNumber(int workspaceId, string phoneId)
        //{
        //    try
        //    {
        //        _logger.LogInformation("Received request to subscribe to WhatsApp webhook for workspaceId: {workspaceId}", workspaceId);

        //        var whatsappDetails = GetWhatsappAccountDetailsByWId(workspaceId);
        //        if (whatsappDetails == null)
        //        {
        //            _logger.LogWarning("WhatsApp account details not found for workspaceId: {workspaceId}", workspaceId);
        //            return NotFound(new
        //            {
        //                Status = "Failure",
        //                Status_Description = "WhatsApp account details not found"
        //            });
        //        }
        //        _logger.LogInformation("Found WhatsApp account details for workspaceId: {workspaceId}. WabaId: {WabaId}", workspaceId, whatsappDetails.WabaId);

        //        string url = $"{_configuration["facebookApiUrl"]}{phoneId}/request_code";

        //        // Construct the request body, if necessary
        //        var requestBody = new
        //        {

        //        };

        //        // Serializing the body to JSON
        //        var jsonContent = JsonConvert.SerializeObject(requestBody);
        //        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        //        _logger.LogInformation("Sending POST request to Facebook API with URL: {Url}", url);

        //        using var httpClient = new HttpClient();

        //        // Adding Authorization header with Bearer token
        //        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", whatsappDetails.AccessToken);

        //        var response = await httpClient.PostAsync(url, content);

        //        if (!response.IsSuccessStatusCode)
        //        {
        //            _logger.LogError("Failed to register phone number");
        //            return StatusCode((int)response.StatusCode, new
        //            {
        //                Status = "Failure",
        //                Status_Description = $"Error registering phone number"
        //            });
        //        }

        //        _logger.LogInformation("Successfully registered phone number");

        //        return Ok(new
        //        {
        //            Status = "Success",
        //            Status_Description = "Successfully registered phone number",

        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "An error occurred while registering phone number");
        //        return StatusCode(500, new
        //        {
        //            Status = "Error",
        //            Status_Description = $"An error occurred while registering phone number: {ex.Message}"
        //        });
        //    }
        //}

        //[HttpGet]
        //public IActionResult Getuserrole([FromServices] IDbHandler dbHandler, int accountid)
        //{
        //    try
        //    {
        //        string storedProcedure = "GetUserRoles";
        //        _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

        //        var parameters = new Dictionary<string, object>
        //    {
        //        { "@UserAccountId", accountid }
        //    };
        //        _logger.LogInformation("stored parameters: ", parameters);

        //        DataTable userrole = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

        //        if (userrole == null || userrole.Rows.Count == 0)
        //        {
        //            _logger.LogWarning("No user role details found");

        //            return Ok(new
        //            {
        //                Status = "Failure",
        //                Status_Description = "No user role details found",
        //                roleDetails = new List<object>()
        //            });
        //        }

        //        var userroledata = userrole.AsEnumerable().Select((row, index) => new
        //        {
        //            role_name = row.Field<string>("role_name")
        //        }).ToArray();


        //        _logger.LogInformation("user role details retrieved successfully: ", userroledata);

        //        return Ok(new
        //        {
        //            Status = "Success",
        //            Status_Description = "user role details retrieved successfully",
        //            user_role = userroledata
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError("An error occurred while retrieving the user role details:", ex.Message);

        //        return StatusCode(500, new
        //        {
        //            Status = "Error",
        //            Status_Description = $"An error occurred while retrieving the user role details: {ex.Message}"
        //        });
        //    }
        //}


        [HttpGet("{workspaceId}")]
        public IActionResult GetLiveCampaigns(int workspaceId)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@workspaceId", workspaceId } };

                DataTable result = _dbHandler.ExecuteDataTable("GetLiveCampaigns", parameters, CommandType.StoredProcedure);

                if (result.Rows.Count == 0)
                {
                    return Ok(new { message = "No live campaigns found", campaigns = new List<AdvertiserAccountModel.CampaignStatusUpdate>() });
                }

                var campaigns = result.AsEnumerable().Select(row => new AdvertiserAccountModel.CampaignStatusUpdate
                {
                    CampaignId = row.Field<int>("CampaignId"),
                    CampaignName = row.Field<string>("campaign_name"),
                    Status = row.Field<string>("Status"),
                    WorkspaceId = row.Field<int>("WorkspaceId"),
                    update_date = row.Field<DateTime>("update_date")
                }).ToList();

                return Ok(campaigns);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetLiveCampaigns: {ex.Message}");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult UpdateCampaignStatus([FromBody] CampaignStatusUpdateRequest request, [FromServices] IDbHandler dbHandler)
        {
            try
            {

                if (request == null || request.CampaignId <= 0 || string.IsNullOrEmpty(request.Status))
                {
                    _logger.LogWarning("Invalid input: CampaignId or Status is missing or invalid");
                    return BadRequest(new
                    {
                        Status = "Failure",
                        Status_Description = "CampaignId and Status are required"
                    });
                }

                string storedProcedureName = "CampaignStatusUpdate";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName} with CampaignId: {CampaignId}, Status: {Status}", storedProcedureName, request.CampaignId, request.Status);


                var parameters = new Dictionary<string, object>
  {
      { "@CampaignID", request.CampaignId },
      { "@Status", request.Status }
  };

                int rowsAffected = dbHandler.ExecuteNonQuery(storedProcedureName, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogWarning("No campaign found with CampaignId: {CampaignId}", request.CampaignId);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No campaign found with the provided CampaignId"
                    });
                }

                _logger.LogInformation("Campaign status updated successfully for CampaignId: {CampaignId}, Status: {Status}", request.CampaignId, request.Status);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Campaign status updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating the campaign status for CampaignId: {CampaignId}", request?.CampaignId);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while updating the campaign status: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetCampaignListForWbyDateRange(int workspaceId, DateTime from_date, DateTime to_date, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetCampaignListDetailsForWbyDateRange";
                _logger.LogInformation($"Executing stored procedure: {procedure} with workspace_id: {workspaceId}");

                DateTime fromDate = from_date.Date;
                DateTime toDate = to_date.Date;

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
                {
                    { "@workspace_id", workspaceId },
                    { "@from_date", fromDate },
                    { "@to_date", toDate }
                };



                // Execute the stored procedure
                DataTable campaignList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (campaignList == null || campaignList.Rows.Count == 0)
                {
                    _logger.LogWarning("No campaigns found for the specified workspace ID.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Campaigns found for the specified workspace ID",
                        CampaignCount = 0 // Return zero count if no rows found
                    });
                }

                // Transform the DataTable to a list of objects
                var campaignListData = campaignList.AsEnumerable().Select(row => new
                {
                    campaign_id = row.Field<int>("campaign_id"),
                    campaign_name = row.Field<string>("campaign_name"),
                    channel_type = row.Field<string>("channel_type"),
                    campaign_budget = row.Field<string>("campaign_budget"),
                    start_date_time = row.Field<DateTime>("start_date_time"),
                    end_date_time = row.Field<DateTime>("end_date_time"),
                    status = row.Field<string>("status"),
                    sent = row.Field<int?>("sent")
                }).ToList();

                _logger.LogInformation("Campaigns retrieved successfully. Data: {@CampaignListData}", campaignListData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Campaigns retrieved successfully",
                    CampaignCount = campaignList.Rows.Count, // Add the row count here
                    CampaignList = campaignListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the campaign list.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the campaign list: {ex.Message}"
                });
            }
        }

        [HttpGet("{workspaceId:int}")]
        public IActionResult GetCampaignListbyWorkspaceId(int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetCampaignListDetailsbyWorkspaceId";
                _logger.LogInformation($"Executing stored procedure: {procedure} with workspace_id: {workspaceId}");

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
{
    { "@workspace_id", workspaceId }
};

                // Execute the stored procedure
                DataTable campaignList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (campaignList == null || campaignList.Rows.Count == 0)
                {
                    _logger.LogWarning("No campaigns found for the specified workspace ID.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Campaigns found for the specified workspace ID",
                        CampaignCount = 0 // Return zero count if no rows found
                    });
                }

                // Transform the DataTable to a list of objects
                var campaignListData = campaignList.AsEnumerable().Select(row => new
                {
                    campaign_id = row.Field<int>("campaign_id"),
                    campaign_name = row.Field<string>("campaign_name"),
                    channel_type = row.Field<string>("channel_type"),
                    campaign_budget = row.Field<string>("campaign_budget"),
                    start_date_time = row.Field<DateTime>("start_date_time"),
                    end_date_time = row.Field<DateTime>("end_date_time"),
                    status = row.Field<string>("status"),
                    sent = row.Field<int?>("sent")
                }).ToList();

                _logger.LogInformation("Campaigns retrieved successfully. Data: {@CampaignListData}", campaignListData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Campaigns retrieved successfully",
                    CampaignCount = campaignList.Rows.Count, // Add the row count here
                    CampaignList = campaignListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the campaign list.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the campaign list: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public object GetRoleNameByEmailAndWorkspace([FromBody] GetRoleRequest request, [FromServices] IDbHandler dbHandler)
        {
            DataTable dtMain = new DataTable();
            dtMain.Columns.Add("Status");
            dtMain.Columns.Add("Status_Description");
            dtMain.Columns.Add("Role_Name");

            try
            {
                string storedProcName = "dbo.GetRoleNameByEmailAndWorkspace";
                _logger.LogInformation("Executing stored procedure: {storedProcName}", storedProcName);

                var procParams = new Dictionary<string, object>
     {
         { "@Email", request.Email },
         { "@WorkspaceInfoId", request.WorkspaceInfoId }
     };
                _logger.LogInformation("Stored procedure parameters: {procParams}", procParams);

                // Execute the stored procedure
                DataTable resultTable = dbHandler.ExecuteDataTable(storedProcName, procParams, CommandType.StoredProcedure);

                if (resultTable != null && resultTable.Rows.Count > 0)
                {
                    foreach (DataRow row in resultTable.Rows)
                    {
                        // Use the values returned by the stored procedure directly
                        dtMain.Rows.Add(
                            row["Status"].ToString(),
                            row["Status_Description"].ToString(),
                            row["Role_Name"] != DBNull.Value ? row["Role_Name"].ToString() : null
                        );
                    }

                    _logger.LogInformation("Response successfully retrieved for email: {Email} and workspace ID: {WorkspaceInfoId}", request.Email, request.WorkspaceInfoId);
                }
                else
                {
                    // Handle the case where the stored procedure returned no rows
                    _logger.LogWarning("No data returned from stored procedure for email: {Email} and workspace ID: {WorkspaceInfoId}", request.Email, request.WorkspaceInfoId);
                    dtMain.Rows.Add("Error", "No data returned from the stored procedure.", null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the role name for email: {Email} and workspace ID: {WorkspaceInfoId}", request.Email, request.WorkspaceInfoId);

                // Add the exception message to the response
                dtMain.Rows.Add("Error", ex.Message, null);
            }

            _logger.LogInformation("Returning response for GetRoleNameByEmailAndWorkspace API call.");

            return DtToJSON(dtMain); // Convert DataTable to JSON response
        }


        [HttpPost]
        public object GetWorkspaceDetailsByEmail([FromBody] GetWorkspaceDetailsRequest request, [FromServices] IDbHandler dbHandler)
        {
            DataTable dtMain = new DataTable();
            dtMain.Columns.Add("Status");
            dtMain.Columns.Add("Status_Description");
            dtMain.Columns.Add("WorkspaceId");
            dtMain.Columns.Add("Workspace_Name");
            dtMain.Columns.Add("Billing_Country");
            dtMain.Columns.Add("Workspace_Industry");
            dtMain.Columns.Add("Workspace_Type");
            dtMain.Columns.Add("Address");

            try
            {
                string storedProcName = "dbo.GetWorkspaceDetailsByEmail";
                _logger.LogInformation("Executing stored procedure: {storedProcName}", storedProcName);

                var procParams = new Dictionary<string, object>

            {
                { "@Email", request.Email }
            };
                _logger.LogInformation("Stored procedure parameters: {procParams}", procParams);

                DataTable resultTable = dbHandler.ExecuteDataTable(storedProcName, procParams, CommandType.StoredProcedure);

                if (resultTable != null && resultTable.Rows.Count > 0)
                {
                    foreach (DataRow row in resultTable.Rows)
                    {
                        dtMain.Rows.Add(
                            "Success",
                            "Workspace details retrieved successfully.",
                            row["workspace_id"].ToString(),
                            row["workspace_name"].ToString(),
                            row["billing_country"].ToString(),
                            row["workspace_industry"].ToString(),
                            row["workspace_type"].ToString(),
                            row["address"].ToString()
                        );
                    }

                    _logger.LogInformation("Workspace details successfully populated for email: {Email}", request.Email);

                    return DtToJSON(dtMain);
                }
                else
                {
                    _logger.LogWarning("No workspace details found for the given email: {Email}", request.Email);
                    dtMain.Rows.Add("Error", "No workspace details found for the given email.", null, null, null, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving workspace details for email: {Email}", request.Email);

                dtMain.Rows.Add("Error", ex.Message, null, null, null, null);
            }

            _logger.LogInformation("Returning response for GetWorkspaceDetailsByEmail API call.");

            return DtToJSON(dtMain);
        }


//        [HttpPost]
//        public object GetWorkspaceDetailsByWorkspaceID([FromBody] GetWorkspaceDetailsRequestByID request, [FromServices] IDbHandler dbHandler)
//        {
//            DataTable dtMain = new DataTable();
//            dtMain.Columns.Add("Status");
//            dtMain.Columns.Add("Status_Description");
//            dtMain.Columns.Add("Workspace_Id");
//            dtMain.Columns.Add("Workspace_Name");
//            dtMain.Columns.Add("Billing_Country");
//            dtMain.Columns.Add("Workspace_Industry");
//            dtMain.Columns.Add("Workspace_Type");
//            dtMain.Columns.Add("Address");
//            dtMain.Columns.Add("created_date");

//            try
//            {
//                string storedProcName = "dbo.GetWorkspaceDetailsByWorkspaceID";
//                _logger.LogInformation("Executing stored procedure: {storedProcName}", storedProcName);

//                var procParams = new Dictionary<string, object>
//{
//    { "@workspaceid", request.WorkspaceId }
//};
//                _logger.LogInformation("Stored procedure parameters: {procParams}", procParams);

//                // Execute the stored procedure
//                DataTable resultTable = dbHandler.ExecuteDataTable(storedProcName, procParams, CommandType.StoredProcedure);

//                if (resultTable != null && resultTable.Rows.Count > 0)
//                {
//                    foreach (DataRow row in resultTable.Rows)
//                    {
//                        dtMain.Rows.Add(
//                            "Success",
//                            "Workspace details retrieved successfully.",
//                            row["workspace_id"].ToString(),
//                            row["workspace_name"].ToString(),
//                            row["billing_country"].ToString(),
//                            row["workspace_industry"].ToString(),
//                            row["workspace_type"].ToString(),
//                            row["address"].ToString(),
//                            row["created_date"] != DBNull.Value ? Convert.ToDateTime(row["created_date"]).ToString("yyyy-MM-dd HH:mm:ss") : null
//                        );
//                    }

//                    _logger.LogInformation("Workspace details successfully populated for workspace ID: {WorkspaceId}", request.WorkspaceId);

//                    return DtToJSON(dtMain); // Convert DataTable to JSON response
//                }
//                else
//                {
//                    _logger.LogWarning("No workspace details found for the given workspace ID: {WorkspaceId}", request.WorkspaceId);
//                    dtMain.Rows.Add("Error", "No workspace details found for the given workspace ID.", null, null, null, null, null, null, null);
//                }
//            }
//            catch (Exception ex)
//            {
//                _logger.LogError(ex, "An error occurred while retrieving workspace details for workspace ID: {WorkspaceId}", request.WorkspaceId);

//                dtMain.Rows.Add("Error", ex.Message, null, null, null, null, null, null, null);
//            }

//            _logger.LogInformation("Returning response for GetWorkspaceDetailsByWorkspace API call.");

//            return DtToJSON(dtMain);
//        }

        [HttpDelete]
        public IActionResult Delete_Adv_Audience_ById([FromServices] IDbHandler dbHandler, int Id)
        {
            try
            {
                string deleteAudience_ById = "Delete_Adv_Audience_ById";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", deleteAudience_ById);


                var parameters = new Dictionary<string, object>
    {
        { "@Id", Id }
    };

                int rowsAffected = dbHandler.ExecuteNonQuery(deleteAudience_ById, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("Audience not found or could not be deleted");
                    return Ok(new
                    {

                        Status = "Failure",
                        Status_Description = "Audience not found or could not be deleted"
                    });
                }


                _logger.LogInformation("Audience deleted successfully");
                return Ok(new

                {
                    Status = "Success",
                    Status_Description = "Audience deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while deleting the campaign: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while deleting the campaign: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult Get_Adv_Audience_FileDetails_ById([FromServices] IDbHandler dbHandler, int Id)
        {
            try
            {
                string download_Adv_Audience_ById = "Get_Adv_Audience_FileDetails_ById";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", download_Adv_Audience_ById);

                var parameters = new Dictionary<string, object>
    {
        { "@Id", Id }
    };

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                // Execute the stored procedure
                DataTable audienceDetailsById = dbHandler.ExecuteDataTable(download_Adv_Audience_ById, parameters, CommandType.StoredProcedure);

                // Check if the stored procedure returned any rows
                if (audienceDetailsById.Rows.Count == 0)
                {
                    _logger.LogInformation("Audience File not found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Audience File not found"
                    });
                }

                // Map the data to an object list
                var audienceListByIdData = audienceDetailsById.AsEnumerable().Select(row => new
                {
                    firstname = row.Field<string>("firstname"),
                    lastname = row.Field<string>("lastname"),
                    phoneno = row.Field<string>("phoneno"),
                    location = row.Field<string>("location"),
                    filename1 = row.Field<string>("filename1")
                }).ToList();

                _logger.LogInformation("Audience File retrieved successfully. Response: {AudienceListByIdData}", audienceListByIdData);

                // Return success response
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Audience File Details retrieved successfully",
                    AudienceFileDetails = audienceListByIdData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the Audience list by id: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the Audience list by id: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public IActionResult UpdateListNameByListId([FromServices] IDbHandler dbHandler, string list_name, int list_id)
        {
            try
            {
                string procedure = "UpdateListNameByListId";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);

                var parameters = new Dictionary<string, object>
        {
            { "@list_id", list_id },
            { "@listname", list_name }
        };

                // Execute the stored procedure and retrieve the number of affected rows
                int rowsAffected = Convert.ToInt32(dbHandler.ExecuteScalar(procedure, parameters, CommandType.StoredProcedure));

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("List name not updated");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "List name not updated"
                    });
                }

                _logger.LogInformation("List name updated successfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "List name updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while updating list name for the list ID: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while updating list name: {ex.Message}"
                });
            }
        }



        [HttpPost]
        public async Task<IActionResult> Checkpaymenturl([FromServices] IDbHandler dbHandler, [FromBody] AdvertiserAccountModel.ProductRequestModel request)
        {
            string storedprocedure = "CheckBillingNameUrl";
            var parameter_1 = new Dictionary<string, object>();
            parameter_1.Add("@name", request.ProductName);
            parameter_1.Add("@country_symbol", request.Currency);
            DataTable paymenturl = dbHandler.ExecuteDataTable(storedprocedure, parameter_1, CommandType.StoredProcedure);
            if (paymenturl == null || paymenturl.Rows.Count == 0)
            {
                _logger.LogWarning("No paymenturl available");
                return Ok(new
                {
                    Status = "Failure",
                    Status_Description = "No paymenturl available"
                });
            }

            var payment_url = paymenturl.AsEnumerable().Select(row => new
            {
                price_id = row.Field<string>("price_id"),
            }).ToList();

            _logger.LogInformation("paymenturl retrieved successfully: {payment_url}", payment_url);
            return Ok(new
            {
                Status = "Success",
                Status_Description = "paymenturl retrieved successfully",
                payment = payment_url
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateProductWithPrice([FromServices] IDbHandler dbHandler, [FromBody] AdvertiserAccountModel.ProductRequestModel request)
        {
            try
            {
                // Validate the request
                if (request.Amount <= 0 || string.IsNullOrEmpty(request.Currency))
                {
                    return BadRequest(new { Error = "Invalid amount or currency." });
                }

                // Set the Stripe secret key
                StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

                // Create a product
                var productService = new ProductService();
                var productOptions = new ProductCreateOptions
                {
                    Name = request.ProductName ?? "Default Product Name",
                    Description = request.Description ?? "Default Description",
                    TaxCode = "txcd_10000000"
                };

                var product = await productService.CreateAsync(productOptions);
                var fetchedProduct = await productService.GetAsync(product.Id);
                string productName = fetchedProduct.Name;
                // Create a price with tax code
                var priceService = new PriceService();
                var priceOptions = new PriceCreateOptions
                {
                    UnitAmount = (long)(request.Amount * 100), // Convert to cents for Stripe
                    Currency = request.Currency,
                    Product = product.Id,
                    TaxBehavior = "exclusive",  // Optional: Set tax behavior, 'exclusive' or 'inclusive'
                };

                var price = await priceService.CreateAsync(priceOptions);
                decimal amountInCurrency = (decimal)(price.UnitAmount / 100.0m); // Convert cents to dollars (or appropriate currency)
                string currency = price.Currency; // Get the currency code (e.g., "usd")
                                                  // Create a Checkout Session
                var sessionOptions = new Stripe.Checkout.SessionCreateOptions
                {
                    PaymentMethodTypes = new List<string> { "card" },
                    LineItems = new List<SessionLineItemOptions>
        {
            new SessionLineItemOptions
            {
                Price = price.Id,
                Quantity = 1
            }
        },
                    BillingAddressCollection = "required", // Force customer to enter billing address
                    AutomaticTax = new Stripe.Checkout.SessionAutomaticTaxOptions { Enabled = true },
                    Mode = "payment",
                    SuccessUrl = "https://agnointel.ai/",
                    CancelUrl = "https://meet.google.com/landing" // Replace with your cancel URL
                };
                var sessionService = new Stripe.Checkout.SessionService();
                var session = await sessionService.CreateAsync(sessionOptions);
                string storedprocedure = "UpdateBillingFeaturesPriceId";
                var parameter_1 = new Dictionary<string, object>();
                parameter_1.Add("@Name", request.ProductName);
                parameter_1.Add("@Currency", request.Currency);
                parameter_1.Add("@PriceId", price.Id);
                DataTable paymenturl = dbHandler.ExecuteDataTable(storedprocedure, parameter_1, CommandType.StoredProcedure);

                // Return the created product and price details
                return Ok(new
                {
                    session = session,
                    SessionId = session.Id,
                    CheckoutUrl = session.Url, // Redirect the user to this URL for payment
                    ProductId = product.Id,
                    PriceId = price.Id,
                    Amount = amountInCurrency,
                    Currency = currency,
                    ProductName = productName
                });

            }
            catch (StripeException ex)
            {
                // Handle Stripe-specific errors
                return BadRequest(new { Error = ex.Message });
            }
        }


        [HttpPost]
        public object GetWorkspaceDetailsByWorkspaceID([FromBody] GetWorkspaceDetailsRequestByID request, [FromServices] IDbHandler dbHandler)
        {
            DataTable dtMain = new DataTable();
            dtMain.Columns.Add("Status");
            dtMain.Columns.Add("Status_Description");
            dtMain.Columns.Add("Workspace_Id");
            dtMain.Columns.Add("Workspace_Name");
            dtMain.Columns.Add("Billing_Country");
            dtMain.Columns.Add("Workspace_Industry");
            dtMain.Columns.Add("Workspace_Type");
            dtMain.Columns.Add("Address");
            dtMain.Columns.Add("created_date");

            try
            {
                string storedProcName = "dbo.GetWorkspaceDetailsByWorkspaceID";
                _logger.LogInformation("Executing stored procedure: {storedProcName}", storedProcName);

                var procParams = new Dictionary<string, object>
{
    { "@workspaceid", request.WorkspaceId }
};
                _logger.LogInformation("Stored procedure parameters: {procParams}", procParams);

                // Execute the stored procedure
                DataTable resultTable = dbHandler.ExecuteDataTable(storedProcName, procParams, CommandType.StoredProcedure);

                if (resultTable != null && resultTable.Rows.Count > 0)
                {
                    foreach (DataRow row in resultTable.Rows)
                    {
                        dtMain.Rows.Add(
                            "Success",
                            "Workspace details retrieved successfully.",
                            row["workspace_id"].ToString(),
                            row["workspace_name"].ToString(),
                            row["billing_country"].ToString(),
                            row["workspace_industry"].ToString(),
                            row["workspace_type"].ToString(),
                            row["address"].ToString(),
                            row["created_date"] != DBNull.Value ? Convert.ToDateTime(row["created_date"]).ToString("yyyy-MM-dd HH:mm:ss") : null
                        );
                    }

                    _logger.LogInformation("Workspace details successfully populated for workspace ID: {WorkspaceId}", request.WorkspaceId);

                    return DtToJSON(dtMain); // Convert DataTable to JSON response
                }
                else
                {
                    _logger.LogWarning("No workspace details found for the given workspace ID: {WorkspaceId}", request.WorkspaceId);
                    dtMain.Rows.Add("Error", "No workspace details found for the given workspace ID.", null, null, null, null, null, null, null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving workspace details for workspace ID: {WorkspaceId}", request.WorkspaceId);

                dtMain.Rows.Add("Error", ex.Message, null, null, null, null, null, null, null);
            }

            _logger.LogInformation("Returning response for GetWorkspaceDetailsByWorkspace API call.");

            return DtToJSON(dtMain);
        }

        [HttpGet]
        public IActionResult Getuserrole([FromServices] IDbHandler dbHandler, int accountid)
        {
            try
            {
                string storedProcedure = "GetUserRoles";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

                var parameters = new Dictionary<string, object>
            {
                { "@UserAccountId", accountid }
            };
                _logger.LogInformation("stored parameters: ", parameters);

                DataTable userrole = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (userrole == null || userrole.Rows.Count == 0)
                {
                    _logger.LogWarning("No user role details found");

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No user role details found",
                        roleDetails = new List<object>()
                    });
                }

                var userroledata = userrole.AsEnumerable().Select((row, index) => new
                {
                    role_name = row.Field<string>("role_name")
                }).ToArray();


                _logger.LogInformation("user role details retrieved successfully: ", userroledata);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "user role details retrieved successfully",
                    user_role = userroledata
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while retrieving the user role details:", ex.Message);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the user role details: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetuserTransaction([FromServices] IDbHandler dbHandler, int accountid)
        {
            try
            {
                string storedProcedure = "GetuserTransaction";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

                var parameters = new Dictionary<string, object>
            {
                { "@UserAccountId", accountid }
            };
                _logger.LogInformation("stored parameters: ", parameters);

                DataTable usertransac = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (usertransac == null || usertransac.Rows.Count == 0)
                {
                    _logger.LogWarning("No role details found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No role details found",
                        usertransac = new List<object>()
                    });
                }

                var usertransacdetails = usertransac.AsEnumerable().Select((row, index) => new
                {
                    PaymentId = row.Field<string>("PaymentId"),
                    Amount = row.Field<decimal>("Amount"),
                    PaymentDate = row.Field<DateTime>("PaymentDate").ToString("yyyy-MM-dd"),
                    symbol = row.Field<string>("symbol"),
                    receipturl = row.Field<string>("receipturl"),
                    name = row.Field<string>("name"),
                    messages = row.Field<string>("messages"),
                    fundtype = row.Field<string>("fund_type")
                }).ToArray();


                _logger.LogInformation("user role details retrieved successfully: ", usertransacdetails);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "user role details retrieved successfully",
                    user_transaction = usertransacdetails
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while retrieving the user role details:", ex.Message);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the user role details: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult Getdebitdetails([FromServices] IDbHandler dbHandler, string emailid)
        {
            try
            {
                string storedProcedure = "checkpairedstatusinbilling";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

                var parameters = new Dictionary<string, object>
{
    { "@Email", emailid }
};

                DataTable debitdetails = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (debitdetails == null || debitdetails.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No debit details found",
                        usertransac = new List<object>()
                    });
                }

                // Check if this is an error message from the stored procedure
                if (debitdetails.Columns.Contains("Message"))
                {
                    string message = debitdetails.Rows[0]["Message"].ToString();
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = message,
                        usertransac = new List<object>()
                    });
                }

                // If we get here, we should have the normal result set
                try
                {
                    var workspacedebitdetails = debitdetails.AsEnumerable()
                        .Select(row => new
                        {
                            symbol = row.Field<string>("CurrencySymbol") ?? "", // Use CurrencyName instead of symbol
                            Amount = row.Field<decimal>("TotalAmount"),
                            messagecount = row.Field<int>("TotalSentCount"),
                            lastdebited_date = row.IsNull("lastdebiteddate")
    ? string.Empty
    : row.Field<DateTime>("lastdebiteddate").ToString("yyyy-MM-dd")

                        }).ToArray();

                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = "user debit details retrieved successfully",
                        user_transaction = workspacedebitdetails
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing result set. Columns available: {Columns}",
                        string.Join(", ", debitdetails.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the user debit details");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the user debit details: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetBillingDetailsById(int BillingId, [FromServices] IDbHandler dbHandler)
        {
            try
            {

                string storedProcedureName = "GetBillingDetailsById";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName} with BillingId: {BillingId}", storedProcedureName, BillingId);

                var parameters = new Dictionary<string, object>
 {
     { "@BillingId", BillingId }
 };

                // Execute the stored procedure and get the result as a DataTable
                DataTable billingDetailsTable = dbHandler.ExecuteDataTable(storedProcedureName, parameters, CommandType.StoredProcedure);

                // Check if the result is empty
                if (billingDetailsTable == null || billingDetailsTable.Rows.Count == 0)
                {
                    _logger.LogWarning("No billing details found for BillingId: {BillingId}", BillingId);

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No billing details found"
                    });
                }

                // Map the DataTable result to an object
                var billingDetails = billingDetailsTable.AsEnumerable().Select(row => new
                {
                    BillingId = row.Field<int>("billing_id"),
                    Messages = row.Field<string>("messages"),
                    Price = row.Field<string>("price"),
                    CountrySymbol = row.Field<string>("country_symbol"),
                    Name = row.Field<string>("name"),
                    Permessage = row.Field<string>("per_message"),
                    TotalAmount = row.Field<string>("total_amount"),
                    Url = row.Field<string>("url"),
                    CreatedAt = row.Field<DateTime?>("created_at"),
                    UpdatedAt = row.Field<DateTime?>("updated_at")
                }).FirstOrDefault();

                _logger.LogInformation("Billing details retrieved successfully for BillingId: {BillingId}", BillingId);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Billing details retrieved successfully",
                    BillingDetails = billingDetails
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving billing details for BillingId: {BillingId}", BillingId);

                // Handle exceptions
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving billing details: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetbillingDetails([FromServices] IDbHandler dbHandler, int workspaceid)
        {
            try
            {
                string storedProcedure = "GetBillingDetailsByWorkspaceInfoId";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

                var parameters = new Dictionary<string, object>
            {
                { "@workspace_info_id", workspaceid }
            };
                _logger.LogInformation("stored parameters: ", parameters);

                DataTable billingDetails = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (billingDetails == null || billingDetails.Rows.Count == 0)
                {
                    _logger.LogWarning("No billing details found");

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No billing details found",
                        PlanDetails = new List<object>()
                    });
                }




                var billingDetailsData = billingDetails.AsEnumerable().Select((row, index) => new
                {

                    billing_name = row.Field<string>("billing_name"),
                    amount = row.Field<string>("amount"),
                    features = row.Field<string>("features"),
                    symbol = row.Field<string>("symbol"),
                    currency = row.Field<string>("currency_name"),
                    permessage = row.Field<string>("permessage")
                }).ToArray();


                _logger.LogInformation("billing details retrieved successfully: ", billingDetailsData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "billing details retrieved successfully",
                    billingDetails = billingDetailsData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while retrieving the billing details:", ex.Message);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the billing details: {ex.Message}"
                });
            }
        }
        [HttpGet]
        public async Task<IActionResult> DownloadInvoicePdf([FromQuery] string invoiceId)
        {
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;
            if (string.IsNullOrEmpty(invoiceId))
            {
                return BadRequest("Invoice ID is required.");
            }

            try
            {
                // Use Stripe's InvoiceService to get the invoice
                var invoiceService = new InvoiceService();
                var invoice = await invoiceService.GetAsync(invoiceId);

                if (invoice == null)
                {
                    return NotFound("Invoice not found.");
                }

                // Fetch the Invoice PDF URL
                var invoicePdfUrl = invoice.InvoicePdf;

                if (string.IsNullOrEmpty(invoicePdfUrl))
                {
                    return NotFound("Invoice PDF not available.");
                }

                // Optionally download the PDF and send it as a file response
                using (var httpClient = new HttpClient())
                {
                    var pdfBytes = await httpClient.GetByteArrayAsync(invoicePdfUrl);

                    return File(pdfBytes, "application/pdf", $"invoice_{invoiceId}.pdf");
                }
            }
            catch (StripeException ex)
            {
                return StatusCode(500, $"Stripe error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error: {ex.Message}");
            }
        }

        [HttpPost]
        public IActionResult CreateCheckoutSession([FromServices] IDbHandler dbHandler, [FromBody] PaymentLinkRequest request)
        {
            StripeConfiguration.ApiKey = _stripeSettings.SecretKey;

            var options = new SessionCreateOptions
            {
                LineItems = new List<SessionLineItemOptions>
    {
        new SessionLineItemOptions
        {
            Price = request.PriceId, // Price ID passed from frontend
            Quantity = request.Quantity,
        },
    },
                Mode = "payment",
                UiMode = "embedded",
                ReturnUrl = $"{_configuration["FrontendUrl"]}settings/Billing", // Success page
                AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },

                // Enable billing address collection
                BillingAddressCollection = "required",
                InvoiceCreation = new SessionInvoiceCreationOptions
                {
                    Enabled = true, // Enable invoice creation
                },
                // Add metadata with the product name
                Metadata = new Dictionary<string, string>
{
    { "price_id", request.PriceId },
    { "product_name", request.productname },
    { "userid", request.id.ToString() } // Convert int to string
}


            };

            var service = new SessionService();
            var session = service.Create(options);
            string storedprocedure = "UpdateBillingFeaturesURL";
            var parameter_1 = new Dictionary<string, object>();
            parameter_1.Add("@clientsecret", session.ClientSecret);
            parameter_1.Add("@PriceId", request.PriceId);
            DataTable paymenturl = dbHandler.ExecuteDataTable(storedprocedure, parameter_1, CommandType.StoredProcedure);
            return Ok(new { clientSecret = session.ClientSecret });

        }
        [HttpGet]
        public IActionResult GetWalletAmount([FromQuery] int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Validate workspaceId input
                if (workspaceId <= 0)
                {
                    _logger.LogWarning("Workspace ID parameter is missing or invalid");
                    return BadRequest(new
                    {
                        Status = "Failure",
                        Status_Description = "Workspace ID parameter is required and must be greater than zero"
                    });
                }

                string storedProcedureName = "Getwalletamount";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName} with workspaceId: {WorkspaceId}", storedProcedureName, workspaceId);

                // Define parameters for the stored procedure
                var parameters = new Dictionary<string, object>
    {
        { "@workspaceid", workspaceId }
    };

                // Execute the stored procedure and get the result
                object result = dbHandler.ExecuteScalar(storedProcedureName, parameters, CommandType.StoredProcedure);

                if (result == null || result == DBNull.Value)
                {
                    _logger.LogWarning("No wallet amount found for the workspaceId: {WorkspaceId}", workspaceId);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No wallet amount found for the provided workspace ID"
                    });
                }

                // Convert result to decimal
                decimal walletAmount = Convert.ToDecimal(result);

                _logger.LogInformation("Wallet amount retrieved successfully for workspaceId: {WorkspaceId}, Amount: {Amount}", workspaceId, walletAmount);

                // Return the wallet amount with success status
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Wallet amount retrieved successfully",
                    WalletAmount = walletAmount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the wallet amount for workspaceId: {WorkspaceId}", workspaceId);

                // Handle exceptions and return a 500 error
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the wallet amount: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetTotalRecipients([FromServices] IDbHandler dbHandler)
        {
            try
            {
                _logger.LogInformation("GetTotalRecipients endpoint called.");

                string storedProcedure = "GetTotalRecipients";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

                DataTable result = dbHandler.ExecuteDataTable(storedProcedure);

                if (result == null || result.Rows.Count == 0)
                {
                    _logger.LogWarning("No recipients found in the database.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No recipients found"
                    });
                }

                int totalRecipients = result.Rows[0].Field<int>("total_recipients");

                _logger.LogInformation("Total recipients retrieved successfully: {TotalRecipients}", totalRecipients);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Total recipients retrieved successfully",
                    TotalRecipients = totalRecipients
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the total recipients: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the total recipients: {ex.Message}"
                });
            }
        }

        [HttpGet("{countryName}")]
        public IActionResult GetTotalRecipientsByCountry(string countryName, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                _logger.LogInformation("GetTotalRecipientsByCountry endpoint called for country: {CountryName}", countryName);

                string storedProcedure = "GetTotalRecipientsByCountry";
                var parameters = new Dictionary<string, object>
    {
        { "@CountryName", countryName }
    };

                DataTable result = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (result == null || result.Rows.Count == 0)
                {
                    _logger.LogWarning("No recipients found for country: {CountryName}", countryName);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No recipients found for the given country"
                    });
                }

                int totalRecipients = result.Rows[0].Field<int>("total_recipients");

                _logger.LogInformation("Total recipients retrieved successfully for {CountryName}: {TotalRecipients}", countryName, totalRecipients);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Total recipients retrieved successfully",
                    TotalRecipients = totalRecipients
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the total recipients for {countryName}: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the total recipients: {ex.Message}"
                });
            }
        }

        [HttpGet("get-recipients")]
        public IActionResult GetTotalRecipientsByBudget(
            [FromQuery] decimal campaignBudget,
            [FromQuery] int workspaceId,
            [FromQuery] string location,
            [FromServices] IDbHandler dbHandler)
        {
            try
            {
                _logger.LogInformation("GetTotalRecipientsByBudget endpoint called.");

                string storedProcedure = "Getrecipientsbycampaign_budget";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);


                var parameters = new Dictionary<string, object>
    {
        { "@campaign_budget", campaignBudget },
        { "@workspace_id", workspaceId },
        { "@location", location }
    };

                DataTable result = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (result == null || result.Rows.Count == 0)
                {
                    _logger.LogWarning("No recipients found within the given campaign budget.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No recipients found within the given campaign budget."
                    });
                }


                var row = result.Rows[0];
                int totalRecipients = row.Field<int>("total_recipients");
                int maxRecipients = row.Field<int>("max_recipients");
                decimal totalCost = row.Field<decimal>("total_cost");
                decimal remainingAmount = row.Field<decimal>("remaining_amount");

                _logger.LogInformation("Total recipients retrieved successfully: {TotalRecipients}", totalRecipients);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Total recipients retrieved successfully",
                    TotalRecipients = totalRecipients,
                    MaxRecipients = maxRecipients,
                    TotalCost = totalCost,
                    RemainingAmount = remainingAmount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the total recipients: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the total recipients: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetRecipientsByCampaignBudget(
               [FromQuery] decimal campaignBudget,
               [FromQuery] int workspaceId,
               [FromQuery] int totalRecipients,
               [FromServices] IDbHandler dbHandler)
        {
            try
            {
                _logger.LogInformation("GetRecipientsByCampaignBudget called with Budget: {Budget}, Workspace ID: {WorkspaceId}, Total Recipients: {TotalRecipients}",
                    campaignBudget, workspaceId, totalRecipients);

                string storedProcedure = "Getrecipientsbycampaign_budget_totalrecipients";

                var parameters = new Dictionary<string, object>
        {
            { "@campaign_budget", campaignBudget },
            { "@workspace_id", workspaceId },
            { "@total_recipients", totalRecipients } // Passing total recipients
        };

                DataTable result = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (result == null || result.Rows.Count == 0)
                {
                    _logger.LogWarning("No recipients found within budget.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No recipients found within budget"
                    });
                }

                var response = result.AsEnumerable().Select(row => new
                {
                    CampaignBudget = row.Field<decimal>("campaign_budget"),
                    WorkspaceId = row.Field<int>("workspace_id"),
                    TotalRecipients = row.Field<int>("total_recipients"),
                    UserPersonalId = row.Field<int>("user_personal_id"),
                    UserAccountId = row.Field<int>("user_account_id"),
                    Email = row.Field<string>("email"),
                    ProductName = row.Field<string>("product_name"),
                    PerMessage = row.Field<decimal>("per_message"),
                    TotalCost = row.Field<decimal>("total_cost"),
                    RemainingAmount = row.Field<decimal>("remaining_amount"),
                    MaxRecipients = row.Field<int>("max_recipients")
                }).FirstOrDefault();

                _logger.LogInformation("Data retrieved successfully.");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Data retrieved successfully",
                    Data = response
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error occurred: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving data: {ex.Message}"
                });
            }
        }

        [HttpPut("UpdateLogo")]
        public async Task<IActionResult> UpdateLogo_personal_id([FromBody] UpdateLogoPersonalIdModel model)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                // Define parameters to pass to stored procedure
                var parameters = new Dictionary<string, object>
        {
            { "MappingId", model.MappingId }, // Use model.MappingId here
            { "MappingName", "Personal_info" },
            { "Image", model.Image },
            { "CreatedBy", model.CreatedBy },
            { "CreatedDate", model.CreatedDate },
            { "UpdatedBy", model.UpdatedBy },
            { "UpdatedDate", model.UpdatedDate }
        };

                // Execute the stored procedure
                _logger.LogInformation($"Executing stored procedure: UpdateLogo_personalid");
                int result = (int)_dbHandler.ExecuteNonQuery("UpdateLogo_personalid", parameters, CommandType.StoredProcedure);


                _logger.LogInformation($"Logo for mapping ID: {model.MappingId} updated successfully.");
                response = new
                {
                    Status = "Success",
                    Status_Description = $"Logo for mapping ID: {model.MappingId} updated successfully."
                };
                return Ok(response);

            }
            catch (Exception ex)
            {
                _logger.LogError($"An exception occurred: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = "An exception occurred while processing the request."
                };
                return StatusCode(500, response);
            }

  
        }

        [HttpPut]
        public async Task<IActionResult> UpdateLogo_workspace_id([FromBody] UpdateLogoWorkspaceIdModel model)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                // Define parameters to pass to stored procedure
                var parameters = new Dictionary<string, object>
        {
            { "MappingId", model.MappingId }, // Use model.MappingId here
            { "MappingName", "Workspace_info" },
            { "Image", model.Image },
            { "CreatedBy", model.CreatedBy },
            { "CreatedDate", model.CreatedDate },
            { "UpdatedBy", model.UpdatedBy },
            { "UpdatedDate", model.UpdatedDate }
        };

                // Execute the stored procedure
                _logger.LogInformation($"Executing stored procedure: UpdateLogo_workspaceid");
                int result = (int)_dbHandler.ExecuteNonQuery("UpdateLogo_workspaceid", parameters, CommandType.StoredProcedure);


                _logger.LogInformation($"Logo for mapping ID: {model.MappingId} updated successfully.");
                response = new
                {
                    Status = "Success",
                    Status_Description = $"Logo for mapping ID: {model.MappingId} updated successfully."
                };
                return Ok(response);

            }
            catch (Exception ex)
            {
                _logger.LogError($"An exception occurred: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = "An exception occurred while processing the request."
                };
                return StatusCode(500, response);
            }

        }

        [HttpPost]
        public IActionResult DeleteOperatorAccount([FromServices] IDbHandler dbHandler, string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return BadRequest(new { Status = "Error", Message = "Email is required." });
            }

            try
            {
                string storedProcedure = "DeleteOperatorAccountByEmail";
                _logger.LogInformation($"Executing stored procedure: {storedProcedure}");

                // Parameters for the stored procedure
                var parameters = new Dictionary<string, object>
        {
            { "@Email", email }
        };
                _logger.LogInformation($"Executing with parameters: {parameters}");

                // Execute the stored procedure and retrieve the result
                DataTable result = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                // Check if the result contains a message
                if (result.Rows.Count > 0)
                {
                    string spMessage = result.Rows[0]["Message"].ToString(); // Assuming SP returns a "Message" column
                    _logger.LogInformation($"Stored procedure message: {spMessage}");

                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = spMessage
                    });
                }

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Operator account deletion process completed."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to delete operator account. Error: {ex.Message}");

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while deleting the operator account: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetSmsPhoneNumbers([FromServices] IDbHandler dbHandler, int workspace_id)
        {
            try
            {
                

                string procedure = "GetSmsPhoneNumbers";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);

                var parameters = new Dictionary<string, object>
                {
                    { "@workspace_id", workspace_id }
                };

                DataTable PhoneList = dbHandler.ExecuteDataTable(procedure, parameters,CommandType.StoredProcedure);

                if (PhoneList == null || PhoneList.Rows.Count == 0)
                {
                    _logger.LogWarning("No phone numbers found in the database.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Phone Numbers found"
                    });
                }

                var PhoneListData = PhoneList.AsEnumerable().Select(row => new
                {
                    id = row.Field<int>("id"),
                    phone_name = row.Field<string>("phone_name"),
                    phone_number = row.Field<string>("phone_number"),
                    created_date = row.Field<DateTime>("created_date"),
                    last_updated_date = row.Field<DateTime>("last_updated_date")

                }).ToList();


                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Phone Numbers retrieved successfully",
                    PhoneNumberList = PhoneListData
                });

            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the Phone Numbers: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the Phone Numbers: {ex.Message}"

                });
            }
        }

        [HttpPost]
        public IActionResult InsertSMSPhoneNumber([FromServices] IDbHandler dbHandler, [FromBody] InsertSMSPhoneNumber number)
        {
            try
            {
                string procedure = "InsertPhoneNumber";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);


                var parameters = new Dictionary<string, object>
        {
            { "@phone_name", number.PhoneName},
            { "@phone_number", number.PhoneNumber},
                    {"@workspace_id", number.WorkspaceId },
                    {"created_date",DateTime.Now },
                    {"@last_updated",DateTime.Now }

        };

                int rowsAffected = dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("could not insert phone number");
                    return Ok(new
                    {

                        Status = "Failure",
                        Status_Description = "could not insert phone number"
                    });
                }


                _logger.LogInformation("phone number inserted successfully");
                return Ok(new

                {
                    Status = "Success",
                    Status_Description = "Phone number inserted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while inserting phone number: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while inserting phone number: {ex.Message}"
                });
            }
        }


        [HttpPut]
        public IActionResult UpdateSMSPhoneNumber([FromServices] IDbHandler dbHandler, [FromBody] UpdateSMSPhoneNumber number)
        {
            try
            {
                string procedure = "UpdateSMSNumber";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);


                var parameters = new Dictionary<string, object>
        {
                    {"@id", number.Id },
            { "@phone_name", number.PhoneName},
            { "@phone_number", number.PhoneNumber},
                    {"@last_updated",DateTime.Now }

        };

                int rowsAffected = dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("could not update phone number");
                    return Ok(new
                    {

                        Status = "Failure",
                        Status_Description = "could not update phone number"
                    });
                }


                _logger.LogInformation("phone number updated successfully");
                return Ok(new

                {
                    Status = "Success",
                    Status_Description = "Phone number updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while updating phone number: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while updating phone number: {ex.Message}"
                });
            }
        }


        [HttpDelete]
        public IActionResult DeleteSMSPhoneNumber([FromServices] IDbHandler dbHandler, int id)
        {
            try
            {
                string procedure = "DeleteSMSNumber";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", procedure);


                var parameters = new Dictionary<string, object>
        {
                    {"@id", id }

        };

                int rowsAffected = dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogInformation("could not delete phone number");
                    return Ok(new
                    {

                        Status = "Failure",
                        Status_Description = "could not delete phone number"
                    });
                }


                _logger.LogInformation("phone number deleted successfully");
                return Ok(new

                {
                    Status = "Success",
                    Status_Description = "Phone number deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while deleting phone number: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while deleting phone number: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public IActionResult GetPersonalinfoByEmail([FromBody] GetPersonalinfoByEmail request, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string storedProcedure = "GetPersonalinfo";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

                var parameters = new Dictionary<string, object>
{
    { "@user_email", request.UserEmail }
};

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                // Execute the stored procedure
                DataTable resultTable = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (resultTable.Rows.Count == 0)
                {
                    _logger.LogInformation("No personal info found for the provided email: {UserEmail}", request.UserEmail);

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No personal info found for the given email ID."
                    });
                }

                // Map the DataTable rows to a list of strongly-typed objects
                var personalInfoList = resultTable.AsEnumerable().Select(row => new
                {
                    FirstName = row.Field<string>("first_name"),
                    LastName = row.Field<string>("last_name"),
                    Email = row.Field<string>("email"),
                    UserPersonalId = row.Field<int>("user_personal_id"),
                    workspace_type = row.Field<string>("workspace_type"),
                }).ToList();

                _logger.LogInformation("Personal info retrieved successfully: {PersonalInfoList}", personalInfoList);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Personal info retrieved successfully.",
                    PersonalInfoList = personalInfoList
                });
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "An error occurred while retrieving personal info for email: {UserEmail}", request.UserEmail);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving personal info: {ex.Message}"
                });
            }
        }

        [HttpPost]

        public IActionResult ChangeStatusToggle([FromBody] StatusToggleRequest request, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                if (request == null || request.BillingId <= 0 || string.IsNullOrEmpty(request.Status))
                {
                    _logger.LogWarning("Invalid input: BillingId or Status is missing or invalid");
                    return BadRequest(new
                    {
                        Status = "Failure",
                        Status_Description = "BillingId and Status are required"
                    });
                }

                // Validate status values - matching your Switch component values
                if (request.Status != "Active" && request.Status != "Inactive")
                {
                    _logger.LogWarning("Invalid status value: {Status}. Only 'Active' or 'Inactive' are allowed", request.Status);
                    return BadRequest(new
                    {
                        Status = "Failure",
                        Status_Description = "Status must be either 'Active' or 'Inactive'"
                    });
                }

                string storedProcedureName = "UpdateBillingStatusByBillingId";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName} with BillingId: {BillingId}, Status: {Status}",
                    storedProcedureName, request.BillingId, request.Status);

                var parameters = new Dictionary<string, object>
        {
            { "@BillingId", request.BillingId },
            { "@Status", request.Status }
        };

                int rowsAffected = dbHandler.ExecuteNonQuery(storedProcedureName, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    _logger.LogWarning("No matching record found for BillingId: {BillingId}", request.BillingId);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No matching record found"
                    });
                }

                _logger.LogInformation("Status toggled to {Status} for BillingId: {BillingId}", request.Status, request.BillingId);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = $"Status toggled to {request.Status} successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while toggling the status for BillingId: {BillingId}", request?.BillingId);
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public object insertworkspacebillstatus([FromServices] IDbHandler dbHandler, int personalid, string workspacename, string workspaceid)
        {
            try
            {
                string insertbillingstatus = "InsertWorkspaceBillingStatus";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", insertbillingstatus);

                var parameter_1 = new Dictionary<string, object>();
                parameter_1.Add("@workspace_id", workspaceid);
                parameter_1.Add("@workspacename", workspacename);
                parameter_1.Add("@personalid", personalid);
                object result_1 = dbHandler.ExecuteScalar(insertbillingstatus, parameter_1, CommandType.StoredProcedure);

                _logger.LogInformation("workspace billing record Inserted sucessfully");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "workspace billing record Inserted sucessfully"
                });

                return result_1;
            }
            catch(Exception ex)
            {
                Console.WriteLine($"An error occurred while insertng billing status, Error: {ex}");
                object result_1 = null;
                return result_1;
            }


        }



        [HttpGet]
        public IActionResult GetMaxRecipientsByBudget([FromQuery] decimal campaignBudget, [FromQuery] int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                _logger.LogInformation("GetMaxRecipientsByBudget called with Budget: {Budget}, Workspace ID: {WorkspaceId}",
                    campaignBudget, workspaceId);

                string storedProcedure = "GetMaxRecipientsByBudget";

                var parameters = new Dictionary<string, object>
   {
       { "@campaign_budget", campaignBudget },
       { "@workspace_id", workspaceId }
   };

                DataTable result = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (result == null || result.Rows.Count == 0)
                {
                    _logger.LogWarning("No recipients found within budget.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No recipients found within budget"
                    });
                }

                var response = result.AsEnumerable().Select(row => new
                {
                    CampaignBudget = row.Field<decimal>("campaign_budget"),
                    WorkspaceId = row.Field<int>("workspace_id"),
                    TotalRecipients = row.Field<int>("total_recipients"),
                    UserPersonalId = row.Field<int>("user_personal_id"),
                    UserAccountId = row.Field<int>("user_account_id"),
                    Email = row.Field<string>("email"),
                    ProductName = row.Field<string>("product_name"),
                    PerMessage = row.Field<decimal>("per_message"),
                    TotalCost = row.Field<decimal>("total_cost"),
                    RemainingAmount = row.Field<decimal>("remaining_amount"),
                    MaxRecipients = row.Field<int>("max_recipients")
                }).FirstOrDefault();

                _logger.LogInformation("Data retrieved successfully.");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Data retrieved successfully",
                    Data = response
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error occurred: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving data: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult pairworkspaceid([FromServices] IDbHandler dbHandler, int accountid, int workpaceid)
        {
            try
            {
                string procedure = "PairBillingCountry";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", procedure);
                string procedure1 = "Getpersonalid";

                var parameters = new Dictionary<string, object>
          {
              { "@accountid", accountid }
          };
                object pesonalid = dbHandler.ExecuteScalar(procedure1, parameters, CommandType.StoredProcedure);

                var parameters1 = new Dictionary<string, object>();
                parameters1.Add("@personal_info_id", Convert.ToInt32(pesonalid));
                parameters1.Add("@workspace_info_id", workpaceid);

                DataTable workspacedetails = dbHandler.ExecuteDataTable(procedure, parameters1, CommandType.StoredProcedure);

                if (workspacedetails.Rows.Count == 0)
                {
                    _logger.LogInformation("workspace not found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "workspace Not found"
                    });
                }
                _logger.LogInformation("workspaces paired successfully .{workspaces}");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "workspaces paired successfully",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while pairing the workspaces.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while pairing the workspaces: {ex.Message}"
                });
            }

        }
        [HttpGet]
        public IActionResult unpairworkspaceid([FromServices] IDbHandler dbHandler, int workpaceid)
        {
            try
            {
                string procedure = "UnPairBillingCountry";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", procedure);
                var parameters1 = new Dictionary<string, object>
          {
              { "@workspace_info_id", workpaceid }
          };

                DataTable workspacedetails = dbHandler.ExecuteDataTable(procedure, parameters1, CommandType.StoredProcedure);

                if (workspacedetails.Rows.Count == 0)
                {
                    _logger.LogInformation("workspace not found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "workspace Not found"
                    });
                }
                _logger.LogInformation("workspaces unpaired successfully .{workspaces}");
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "workspaces unpaired successfully",
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while unpairing the workspaces.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while unpairing the workspaces: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetWalletAmountByWorkspaceId(int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string storedProcedureName = "Getwalletamount";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName} with WorkspaceId: {WorkspaceId}", storedProcedureName, workspaceId);

                var parameters = new Dictionary<string, object>
{
    { "@workspaceid", workspaceId }
};

                // Execute the stored procedure and get the result as a DataTable
                DataTable walletAmountTable = dbHandler.ExecuteDataTable(storedProcedureName, parameters, CommandType.StoredProcedure);

                // Check if the result is empty
                if (walletAmountTable == null || walletAmountTable.Rows.Count == 0)
                {
                    _logger.LogWarning("No wallet amount found for WorkspaceId: {WorkspaceId}", workspaceId);

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No wallet amount found"
                    });
                }

                // Map the DataTable result to an object
                var walletDetails = walletAmountTable.AsEnumerable().Select(row => new
                {

                    TotalAmount = row.Field<decimal?>("TotalAmount")
                }).FirstOrDefault();

                _logger.LogInformation("Wallet amount retrieved successfully for WorkspaceId: {WorkspaceId}", workspaceId);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Wallet amount retrieved successfully",
                    WalletDetails = walletDetails
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving wallet amount for WorkspaceId: {WorkspaceId}", workspaceId);

                // Handle exceptions
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving wallet amount: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetAdminCampaignColumns(string EmailId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetAdminCampaignColumns";
                _logger.LogInformation($"Executing stored procedure: {procedure} with EmailId: {EmailId}");

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
        {
            { "@EmailId", EmailId }
        };

                // Execute the stored procedure
                DataTable columnData = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (columnData == null || columnData.Rows.Count == 0)
                {
                    _logger.LogWarning("No columns found for the specified workspace ID.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No columns found for the specified workspace ID"
                    });
                }

                // Parse the Columns (JSON) from the result
                var columnPreferences = columnData.Rows[0].Field<string>("Columns");

                _logger.LogInformation("Column preferences retrieved successfully for WorkspaceId {EmailId}", EmailId);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Data retrieved successfully",
                    Columns = columnPreferences
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving column preferences.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving column preferences: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public IActionResult UpdateAdminCampaignColumns([FromBody] UpdateAdminCampaignColumnsRequest request, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "UpdateAdminCampaignColumns";
                _logger.LogInformation($"Executing stored procedure: {procedure} with WorkspaceId: {request.EmailId}");

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
{
    { "@EmailId", request.EmailId },
    { "@Columns", request.Columns }
};

                // Execute the stored procedure
                dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);

                _logger.LogInformation("Column preferences updated successfully for WorkspaceId {WorkspaceId}", request.EmailId);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Column preferences updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while updating column preferences.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while updating column preferences: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetWorkspaceBillingStatus(int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string procedure = "GetWorkspaceBillingStatus";
                _logger.LogInformation($"Executing stored procedure: {procedure} with WorkspaceId: {workspaceId}");

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
        {
            { "@WorkspaceId", workspaceId }
        };

                // Execute the stored procedure
                DataTable columnData = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (columnData == null || columnData.Rows.Count == 0)
                {
                    _logger.LogWarning("No Billing Status found for the specified workspace ID.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Billing Status found for the specified workspace ID"
                    });
                }

                // Parse the Columns (JSON) from the result
                var StatusPreferences = columnData.Rows[0].Field<string>("billing_status");

                _logger.LogInformation("Billing Status preferences retrieved successfully for WorkspaceId {WorkspaceId}", workspaceId);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Data retrieved successfully",
                    BillingStatus = StatusPreferences
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving Status preferences.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving Status preferences: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetBillingCountryandSymbol([FromServices] IDbHandler dbHandler, int accountId)
        {
            try
            {
                string procedure = "GetBillingCountryandSymbol";
                _logger.LogInformation("Executing stored procedure: {procedure}", procedure);
                var parameters = new Dictionary<string, object>
         {
             { "@accountid", accountId }
         };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);
                DataTable billingInfo = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (billingInfo == null || billingInfo.Rows.Count == 0)
                {
                    _logger.LogWarning("No billing information found for account");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No billing information found for account"
                    });
                }

                // Check if there's an error message returned by the stored procedure
                if (billingInfo.Columns.Contains("Message"))
                {
                    string errorMessage = billingInfo.Rows[0].Field<string>("Message");
                    _logger.LogWarning("Stored procedure returned an error: {ErrorMessage}", errorMessage);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = errorMessage
                    });
                }

                var billingData = billingInfo.Rows[0];
                var billingInfoData = new
                {
                    currencyName = billingData.Field<string>("CurrencyName"),
                    currencySymbol = billingData.Field<string>("CurrencySymbol")
                };

                _logger.LogInformation("Billing information retrieved successfully, Response: {Response}", billingInfoData);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Billing information retrieved successfully",
                    BillingInfo = billingInfoData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving billing information: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving billing information: {ex.Message}"
                });
            }
        }


        [HttpGet("{countryName}")]
        public IActionResult GetTotalRecipientsFromCountry(string countryName)
        {
            try
            {
                _logger.LogInformation("GetTotalRecipientsFromCountry endpoint called for country: {CountryName}", countryName);

                string storedProcedure = "GetTotalRecipientsFromCountry";
                var parameters = new Dictionary<string, object>
                {
                    { "@CountryName", countryName }
                };

                DataTable result = _dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (result == null || result.Rows.Count == 0)
                {
                    _logger.LogWarning("No recipients found for country: {CountryName}", countryName);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No recipients found for the given country"
                    });
                }

                int totalRecipients = result.Rows[0].Field<int>("total_recipients");

                _logger.LogInformation("Total recipients retrieved successfully for {CountryName}: {TotalRecipients}", countryName, totalRecipients);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Total recipients retrieved successfully",
                    TotalRecipients = totalRecipients
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the total recipients for {countryName}: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the total recipients: {ex.Message}"
                });
            }
        }

        [HttpGet("{countryName}/{toCountryName}")]
        public IActionResult GetTotalRecipientsToCountry(string countryName, string toCountryName)
        {
            try
            {
                _logger.LogInformation("GetTotalRecipientsToCountry endpoint called for country: {CountryName} to country: {ToCountryName}", countryName, toCountryName);

                string storedProcedure = "GetTotalRecipientsToCountry";
                var parameters = new Dictionary<string, object>
                {
                    { "@CountryName", countryName },
                    { "@ToCountryName", toCountryName }
                };

                DataTable result = _dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (result == null || result.Rows.Count == 0)
                {
                    _logger.LogWarning("No recipients found from {CountryName} to {ToCountryName}", countryName, toCountryName);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No recipients found for the given country pair"
                    });
                }

                int totalRecipients = result.Rows[0].Field<int>("total_recipients");

                _logger.LogInformation("Total recipients retrieved successfully from {CountryName} to {ToCountryName}: {TotalRecipients}", countryName, toCountryName, totalRecipients);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Total recipients retrieved successfully",
                    TotalRecipients = totalRecipients
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the total recipients from {countryName} to {toCountryName}: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the total recipients: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public IActionResult GetAdvertiserShortCodeMapper([FromServices] IDbHandler dbHandler, int workspaceId)
        {
            try
            {
                string procedure = "GetAdvertiserShortCodeMapper";
                _logger.LogInformation("Executing stored procedure: {procedure}", procedure);

                var parameters = new Dictionary<string, object>
        {
            { "@workspace_id", workspaceId }
        };

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);
                DataTable shortCodesList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (shortCodesList == null || shortCodesList.Rows.Count == 0)
                {
                    _logger.LogWarning("No short code information found for workspace ID: {workspaceId}", workspaceId);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No short code information found for workspace"
                    });
                }

                var advertiserShortCodes = shortCodesList.Rows[0].Field<string>("sms_short_codes");

                _logger.LogInformation("Short code information retrieved successfully. Response: {Response}", advertiserShortCodes);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Short code information retrieved successfully",
                    ShortCodes = advertiserShortCodes
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving short code information: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving short code information: {ex.Message}"
                });
            }
        }



        [HttpGet]

        public IActionResult GetCampaignDetailsById([FromServices] IDbHandler dbHandler, int CampaignId)
        {
            try
            {
                string getCampaignDetailsById = "GetCampaignDetailsById";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getCampaignDetailsById);

                var parameters = new Dictionary<string, object>
  {
      { "@CampaignId", CampaignId }
  };

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                // Execute the stored procedure
                DataTable campaignDetailsById = dbHandler.ExecuteDataTable(getCampaignDetailsById, parameters, CommandType.StoredProcedure);

                // Check if the stored procedure returned any rows
                if (campaignDetailsById.Rows.Count == 0)
                {
                    _logger.LogInformation("Campaign not found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Campaign not found"
                    });
                }

                // Map the data to an object list
                var campaignListByIdData = campaignDetailsById.AsEnumerable().Select(row => new
                {
                    campaign_id = row.Field<object>("campaign_id") is int ? row.Field<int>("campaign_id") : 0,
                    campaign_name = row.Field<string>("campaign_name"),
                    campaign_budget = row.Field<object>("campaign_budget")?.ToString(),
                    channel_type = row.Field<string>("channel_type"),
                    start_date_time = row.Field<DateTime?>("start_date_time"),
                    end_date_time = row.Field<DateTime?>("end_date_time"),
                    template_name = row.Field<string>("template_name"),
                    template_id = row.Field<string>("template_id"),
                    status = row.Field<string>("status"),
                    sent = row.Field<object>("sent") is int ? row.Field<int?>("sent") : null,
                    delivered = row.Field<object>("delivered") is int ? row.Field<int?>("delivered") : null,
                    age = row.Field<string>("age"),
                    gender = row.Field<string>("gender"),
                    income_level = row.Field<string>("income_level"),
                    location = row.Field<string>("city"),
                    interests = row.Field<string>("interest"),
                    behaviours = row.Field<string>("behaviour"),
                    os_device = row.Field<string>("os_device"),
                    target_country = row.Field<string>("target_country"),
                    roaming_country = row.Field<string>("roaming_country"),
                    f_campaign_budget = row.Field<string>("f_campaign_budget"),
                    f_start_date_time = row.Field<DateTime?>("f_start_date_time"),
                    f_end_date_time = row.Field<DateTime?>("f_end_date_time"),
                    listname = row.Field<string>("listname"),
                    isAdminApproved = ConvertToNullableBool(row["isAdminApproved"]),
                    isOperatorApproved = ConvertToNullableBool(row["isOperatorApproved"]),
                    budget_and_schedule = row.Field<string>("budget_and_schedule"),
                    message_frequency = row.Field<string>("message_frequency"),
                    sequential_delivery = ConvertToNullableBool(row["sequential_delivery"]),
                    prevent_duplicate_messages = ConvertToNullableBool(row["prevent_duplicate_messages"]),
                    daily_recipient_limit = row.Field<int?>("daily_recipient_limit"),
                    delivery_start_time = row.Field<DateTime?>("delivery_start_time"),
                    delivery_end_time = row.Field<DateTime?>("delivery_end_time"),
                    smpp_id = row.Field<int?>("smpp_id"),
                    sms_connection_id = row.Field<int?>("sms_connection_id"),
                    sms_server_name = row.Field<string>("server_name"),
                    sms_serverConnection_name = row.Field<string>("connection_name"),
                    sms_shortcode = row.Field<string>("short_code")
                }).ToList();

                _logger.LogInformation("Campaign retrieved successfully. Response: {CampaignListByIdData}", campaignListByIdData);

                // Return success response
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Campaign retrieved successfully",
                    CampaignDetails = campaignListByIdData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the campaign list by id: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the campaign list by id: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetFailedAudienceList(int listId, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string storedProcedureName = "GetFailedAudienceList";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName} with ListId: {ListId}", storedProcedureName, listId);

                var parameters = new Dictionary<string, object>
{
    { "@ListId", listId }
};

                // Execute the stored procedure and get the result as a DataTable
                DataTable failedAudienceTable = dbHandler.ExecuteDataTable(storedProcedureName, parameters, CommandType.StoredProcedure);

                // Check if the result is empty
                if (failedAudienceTable == null || failedAudienceTable.Rows.Count == 0)
                {
                    _logger.LogWarning("No failed audience records found for ListId: {ListId}", listId);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No failed audience records found"
                    });
                }

                // Map the DataTable result to a list of objects
                var failedAudienceList = failedAudienceTable.AsEnumerable().Select(row => new
                {
                    FirstName = row.Field<string>("firstname"),
                    LastName = row.Field<string>("lastname"),
                    PhoneNo = row.Field<string>("phoneno"),
                    Location = row.Field<string>("location"),
                    ErrorMessage = row.Field<string>("error_message")
                }).ToList();

                _logger.LogInformation("Failed audience records retrieved successfully for ListId: {ListId}", listId);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Failed audience records retrieved successfully",
                    FailedAudienceList = failedAudienceList,
                    RecordCount = failedAudienceList.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving failed audience records for ListId: {ListId}", listId);
                // Handle exceptions
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving failed audience records: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetBillingWorkspaceDetailsByAccountId([FromServices] IDbHandler dbHandler, int accountid)
        {
            try
            {
                string procedure = "GetBillingWorkspaceDetailsByAccountId";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", procedure);

                var parameters = new Dictionary<string, object>
   {
       { "@Accountid", accountid }
   };


                DataTable workspacedetails = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (workspacedetails.Rows.Count == 0)
                {
                    _logger.LogInformation("workspace not found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "workspace Not found"
                    });
                }

                var workspaces = workspacedetails.AsEnumerable().Select(row => new
                {
                    workspaceid = row.Field<int>("workspace_id"),
                    workspace = row.Field<string>("workspace_name"),
                    billingstatus = row.Field<string>("billing_status"),
                    paireddate = row.Field<DateTime>("paireddate").ToString("yyyy-MM-dd"),
                    workspaceimage = row.Field<string>("workspace_image"),
                }).ToList();
                _logger.LogInformation("workspaces retrieved successfully .{workspaces}", workspaces);
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "workspaces retrieved successfully",
                    workspacelist = workspaces
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the workspaces.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the workspaces:{ex.Message}"
                });
            }

        }

        [HttpGet]
        public IActionResult GetCampaignNotifications(int workspaceId)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "@WorkspaceId", workspaceId } };
                DataTable result = _dbHandler.ExecuteDataTable("GetCampaignNotificationsByWorkspaceId", parameters, CommandType.StoredProcedure);

                if (result.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No unread notifications found",
                        notifications = new List<AdvertiserNotification>()
                    });
                }

                var notifications = result.AsEnumerable().Select(row => new AdvertiserNotification
                {
                    Id = row.Field<int>("Id"),
                    WorkspaceId = row.Field<int>("WorkspaceId"),
                    CampaignId = row.Field<int?>("CampaignId"),
                    CampaignName = row.Field<string>("CampaignName"),
                    StatusMark = row.Field<string>("StatusMark"),
                    NotificationType = row.Field<string>("NotificationType"),
                    NotificationData = !string.IsNullOrEmpty(row.Field<string>("NotificationData"))
                ? JsonConvert.DeserializeObject<Dictionary<string, object>>(row.Field<string>("NotificationData")) // ✅ Deserialize JSON data
                : null,
                    CreatedAt = row.Field<DateTime>("CreatedAt")
                }).Where(n => n.StatusMark == "Unread").ToList();

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Unread notifications retrieved successfully",
                    notifications
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in GetCampaignNotifications: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = "Internal server error",
                    error = ex.Message
                });
            }
        }

        [HttpPost]
        public IActionResult MarkAdvertiserNotificationRead(int id)
        {
            try
            {
                if (id <= 0)
                {
                    _logger.LogWarning($"Invalid CampaignId received: {id}");
                    return BadRequest(new
                    {
                        Status = "Failure",
                        Status_Description = "Invalid CampaignId provided."
                    });
                }

                _logger.LogInformation($"Received CampaignId: {id}");

                var parameters = new Dictionary<string, object> { { "@id", id } };
                object result = _dbHandler.ExecuteScalar("MarkAdvertiserNotificationRead", parameters, CommandType.StoredProcedure);

                int rowsAffected = result != null ? Convert.ToInt32(result) : 0;

                _logger.LogInformation($"Rows affected from DB: {rowsAffected}");

                if (rowsAffected <= 0)  // ✅ Fix incorrect success response
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No unread notifications found to mark as read or invalid CampaignId."
                    });
                }

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "All notifications marked as read."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in MarkCampaignNotificationsRead: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = "Internal server error",
                    error = ex.Message
                });
            }
        }




        [HttpDelete]
        public IActionResult DeleteInvitedAccountByEmailFromMembers([FromServices] IDbHandler dbHandler, string email)
        {
            try
            {
                // Name of the stored procedure
                string storedProcedure = "DeleteInvitedAccountByEmail";
                _logger.LogInformation($"Executing stored procedure: {storedProcedure}");

                // Parameters for the stored procedure
                var parameters = new Dictionary<string, object>
{
    { "@EmailId", email }
};
                _logger.LogInformation($"Executing with parameters: {parameters}");

                // Execute the stored procedure and retrieve the result
                DataTable result = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                // Check if the result contains a message
                if (result.Rows.Count > 0)
                {
                    string spMessage = result.Rows[0]["Message"].ToString(); // Assuming the SP returns a column named "Message"
                    _logger.LogInformation($"Stored procedure message: {spMessage}");

                    // Return the SP message in the API response
                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = spMessage
                    });
                }

                // If no message is returned, send a generic success response
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Account deletion process completed."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to delete the account. Error: {ex.Message}");

                // Handle any errors
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while deleting the account: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public IActionResult GetPersonalSettingsDetailsByEmail([FromBody] GetPersonalSettingsDetailsByEmail request, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                string storedProcedure = "GetPersonalSettingsDetailsByEmail";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

                var parameters = new Dictionary<string, object>
{
    { "@user_email", request.UserEmail }
};

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                // Execute the stored procedure
                DataTable resultTable = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (resultTable.Rows.Count == 0)
                {
                    _logger.LogInformation("No personal info found for the provided email: {UserEmail}", request.UserEmail);

                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No personal info found for the given email ID."
                    });
                }

                // Map the DataTable rows to a list of strongly-typed objects
                var personalInfoList = resultTable.AsEnumerable().Select(row => new
                {
                    FirstName = row.Field<string>("first_name"),
                    LastName = row.Field<string>("last_name"),
                    Email = row.Field<string>("email"),
                    UserPersonalId = row.Field<int>("user_personal_id"),
                }).ToList();

                _logger.LogInformation("Personal info retrieved successfully: {PersonalInfoList}", personalInfoList);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Personal info retrieved successfully.",
                    PersonalInfoList = personalInfoList
                });
            }
            catch (Exception ex)
            {

                _logger.LogError(ex, "An error occurred while retrieving personal info for email: {UserEmail}", request.UserEmail);

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving personal info: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetDebitandCreditMessagesCount([FromServices] IDbHandler dbHandler, int accountId)
        {
            try
            {
                string storedProcedure = "GetDebitandCreditMessagesCount";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", storedProcedure);

                var parameters = new Dictionary<string, object>
      {
          { "@AccountId", accountId }
      };

                DataTable messagesDetails = dbHandler.ExecuteDataTable(storedProcedure, parameters, CommandType.StoredProcedure);

                if (messagesDetails == null || messagesDetails.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No messages details found",
                        messages_details = new List<object>()
                    });
                }

                // Check if this is an error message from the stored procedure
                if (messagesDetails.Columns.Contains("Message"))
                {
                    string message = messagesDetails.Rows[0]["Message"].ToString();
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = message,
                        messages_details = new List<object>()
                    });
                }

                try
                {
                    var messageCountDetails = messagesDetails.AsEnumerable()
                        .Select(row => new
                        {
                            sent_messages = Convert.ToInt64(row["sent_messages"] ?? 0),
                            total_purchased_messages = Convert.ToInt64(row["total_purchased_messages"] ?? 0)
                        }).ToArray();

                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = "Messages count retrieved successfully",
                        messages_details = messageCountDetails
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing result set. Columns available: {Columns}",
                        string.Join(", ", messagesDetails.Columns.Cast<DataColumn>().Select(c => c.ColumnName)));
                    throw;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the messages count details");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the messages count details: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetProfileImageByWorkspaceId([FromServices] IDbHandler dbHandler, int WorkspaceId)
        {
            try
            {
                string procedure = "GetProfileImageByWorkspace";

                var parameters = new Dictionary<string, object>
{
    { "@WorkspaceId", WorkspaceId }
};

                DataTable ImageDetails = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (ImageDetails.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Image Not found"
                    });
                }

                var ImageData = ImageDetails.AsEnumerable().Select(row => new
                {
                    image = row.Field<string>("image"),
                }).ToList(); // Return only one image instead of a list

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Image retrieved successfully",
                    Image = ImageData
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the image: {ex.Message}"
                });
            }
        }


    }



}
