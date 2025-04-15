using DBAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Data;
using TravelAd_Api.DataLogic;

namespace TravelAd_Api.Controllers
{
    [Authorize]
    [Route("[controller]/api/[action]")]
    [ApiController]
    [EnableCors("AllowSpecificOrigin")]
    public class OperatorController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IDbHandler _dbHandler;
        private readonly ILogger<AdvertiserAccountController> _logger;
        private readonly Stripesettings _stripeSettings;


        public OperatorController(IConfiguration configuration, IDbHandler dbHandler, ILogger<AdvertiserAccountController> logger, IOptions<Stripesettings> stripeSettings)
        {
            _configuration = configuration;
            _dbHandler = dbHandler;
            _logger = logger;

        }
        //private static readonly ILog Log = LogManager.GetLogger(typeof(AdvertiserAccountController));


        private string DtToJSON(DataTable table)
        {
            string jsonString = JsonConvert.SerializeObject(table);
            return jsonString;
        }

        //------------------------------------------------------------------------------------------------------
        //write your controller code here

        [HttpGet]
        public IActionResult GetCampaignListOperator([FromServices] IDbHandler dbHandler, int WorkspaceId)
        {
            try
            {
                string procedure = "GetCampaignsByTargetCountry"; // Stored procedure name


                _logger.LogInformation($"Executing stored procedure: {procedure}");


                var parameters = new Dictionary<string, object>
{
    { "@WorkspaceId", WorkspaceId }
};

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                // Execute the stored procedure without parameters
                DataTable campaignList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);


                if (campaignList == null || campaignList.Rows.Count == 0)
                {
                    _logger.LogWarning("No campaigns found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Campaigns found",
                        CampaignCount = 0 // Return zero count if no rows found
                    });
                }


                var createdDate = campaignList.Rows.Count > 0 ? campaignList.Rows[0].Field<DateTime>("created_date") : (DateTime?)null;


                // Transform the DataTable to a list of objects
                var campaignListData = campaignList.AsEnumerable().Select(row => new
                {
                    workspace_id = row.Field<int>("workspace_id"),
                    workspace_name = row.Field<string>("workspace_name"),
                    campaign_id = row.Field<int>("campaign_id"),
                    campaign_name = row.Field<string>("campaign_name"),
                    channel_type = row.Field<string>("channel_type"),
                    campaign_budget = row.Field<string>("campaign_budget"),
                    start_date_time = row.Field<DateTime>("start_date_time"),
                    end_date_time = row.Field<DateTime>("end_date_time"),
                    status = row.Field<string>("status"),
                    sent = row.Field<int?>("sent"),
                    country_id = row.Field<int?>("country_id"),
                    country_name = row.Field<string>("country_name"),
                    currency_name = row.Field<string>("currency_name"),
                    symbol = row.Field<string>("symbol"),
                }).ToList();

                _logger.LogInformation("Campaigns retrieved successfully. Data: {@CampaignListData}", campaignListData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Campaigns retrieved successfully",
                    AccountCreatedDate = createdDate,
                    CampaignCount = campaignList.Rows.Count, // Add the row count here
                    CampaignList = campaignListData,

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
        public IActionResult GetCampaignListOperatorbyDateRange(DateTime from_date, DateTime to_date, int workspaceId, [FromServices] IDbHandler dbHandler)
        {
            try
            {

                string procedure = "GetCampainListOperatorbyDateRange";
                _logger.LogInformation($"Executing stored procedure: {procedure}");

                DateTime fromDate = from_date.Date;
                DateTime toDate = to_date.Date;

                // Prepare parameters for the stored procedure
                var parameters = new Dictionary<string, object>
       {

           { "@from_date", fromDate },
           { "@to_date", toDate },
           { "@workspaceId",workspaceId}
       };

                // Execute the stored procedure
                DataTable campaignList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (campaignList == null || campaignList.Rows.Count == 0)
                {
                    _logger.LogWarning("No campaigns found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No Campaigns found ",
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
        [HttpPut]
        public IActionResult UpdateCampaignStatus(TravelAd_Api.Models.OperatorModel.UpdateCampaign1 uc, [FromServices] IDbHandler dbHandler)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                string updateQuery = "UpdateCampaignApprovalStatus";
                _logger.LogInformation("Executing stored procedure: {Query}", updateQuery);

                var parameters = new Dictionary<string, object>
        {
            {"@campaignId", uc.campaignId },
            {"@approverType", "Operator" },
            {"@workspaceId", uc.workspaceId },
            {"@approvalStatus", uc.status }
        };

                // Execute the stored procedure
                int result = Convert.ToInt32(dbHandler.ExecuteScalar(updateQuery, parameters, CommandType.StoredProcedure));

                // Handle stored procedure responses
                switch (result)
                {
                    case 1:
                        _logger.LogInformation($"Campaign with ID {uc.campaignId} was updated successfully.");
                        response = new
                        {
                            Status = "Success",
                            Status_Description = "Campaign status updated successfully."
                        };
                        break;

                    case -1:
                        _logger.LogWarning($"Campaign ID {uc.campaignId} approval failed: Possible reason - Already rejected, unauthorized, or duplicate approval.");
                        response = new
                        {
                            Status = "Error",
                            Status_Description = "Campaign approval failed. Possible reasons: already rejected, unauthorized operator, or duplicate approval."
                        };
                        break;

                    default:
                        _logger.LogWarning($"Campaign ID {uc.campaignId} update failed: Unknown error.");
                        response = new
                        {
                            Status = "Error",
                            Status_Description = "Campaign ID not found or update failed."
                        };
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An exception occurred while processing the request. Error: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = $"An exception occurred while processing the request. {ex.Message}"
                };
            }

            return Ok(response);
        }

        [HttpGet]
        public IActionResult GetOperatorApprovalStatus([FromServices] IDbHandler dbHandler, int WorkspaceId, int CampaignId)
        {
            try
            {
                string procedure = "GetCampaignApprovalStatus"; // Stored procedure name

                _logger.LogInformation($"Executing stored procedure: {procedure}");

                var parameters = new Dictionary<string, object>
        {
            { "@WorkspaceId", WorkspaceId },
            { "@CampaignId", CampaignId },
            { "@mode","Operator"}
        };

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                // Execute the stored procedure
                DataTable approvalResult = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                // If no data is returned, assume the operator is not assigned or campaign doesn't exist
                if (approvalResult == null || approvalResult.Rows.Count == 0)
                {
                    _logger.LogWarning("Operator is not assigned to this campaign or no data found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Operator is not assigned to this campaign or no data found.",
                        IsApproved = false
                    });
                }

                // Extract the approval status from the first row
                string approvalStatus = approvalResult.Rows[0]["ApprovalStatus"].ToString();

                // Determine if the campaign is approved
                bool isApproved = approvalStatus == "Approved";

                _logger.LogInformation($"Operator approval status: {approvalStatus}, IsApproved: {isApproved}");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Approval status retrieved successfully.",
                    IsApproved = isApproved // Return true if "Approved", otherwise false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the operator approval status.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the approval status: {ex.Message}",
                    IsApproved = false
                });
            }
        }

        [HttpGet]
        public IActionResult GetCombinedStatisticsOperator([FromServices] IDbHandler dbHandler, int WorkspaceId)
        {
            try
            {
                DataTable chartDetails = null;
                DataTable messagesSentDetails = null;
                string campaignProcedure = "GetCampaignsCountByTargetCountry";
                string contactProcedure = "GetUniqueContactsByWorkspace";

                var parameters = new Dictionary<string, object>
        {
            { "@OperatorId", WorkspaceId }
        };

                string procedure1 = "GetOperatorDashboardChartDetails";
                _logger.LogInformation("Executing stored procedure: {Procedure}", procedure1);
                chartDetails = dbHandler.ExecuteDataTable(procedure1, parameters, CommandType.StoredProcedure);
                _logger.LogInformation("Chart details retrieved: {RowCount} rows.", chartDetails?.Rows.Count ?? 0);

                var chartData = chartDetails.AsEnumerable().Select(row => new
                {
                    date = row.Field<DateTime>("date"),
                    Email = row.Field<int?>("Email"),
                    SMS = row.Field<int?>("SMS"),
                    PushNotifications = row.Field<int?>("PushNotification"),
                    RCSmessages = row.Field<int?>("RCSMessages"),
                    WhatsApp = row.Field<int?>("WhatsApp")
                }).ToList();

                var parameters2 = new Dictionary<string, object>
        {
            { "@WorkspaceId", WorkspaceId }
        };

                _logger.LogInformation("Executing stored procedure: {Procedure}", campaignProcedure);
                DataTable campaignData = dbHandler.ExecuteDataTable(campaignProcedure, parameters2, CommandType.StoredProcedure);
                int campaignCount = campaignData?.Rows.Count > 0 ? Convert.ToInt32(campaignData.Rows[0]["CampaignCount"]) : 0;

                _logger.LogInformation("Executing stored procedure: {Procedure}", contactProcedure);
                object contactCountObj = dbHandler.ExecuteScalar(contactProcedure, parameters2, CommandType.StoredProcedure);
                int contactCount = contactCountObj != DBNull.Value ? Convert.ToInt32(contactCountObj) : 0;

                string procedure3 = "GetOperatorDashboardMessagesSentDetails";
                _logger.LogInformation("Executing stored procedure: {Procedure}", procedure3);
                messagesSentDetails = dbHandler.ExecuteDataTable(procedure3, parameters, CommandType.StoredProcedure);
                _logger.LogInformation("Messages sent details retrieved: {RowCount} rows.", messagesSentDetails?.Rows.Count ?? 0);

                var sentData = messagesSentDetails.AsEnumerable().Select(row => new
                {
                    totalSent = row.Field<int?>("TotalSent")
                }).ToList();

                return Ok(new
                {
                    status = "Success",
                    status_Description = "Individual statistics retrieved successfully",
                    chartDetails = chartData,
                    campaignDetails = new[] { new { totalCampaigns = campaignCount } },
                    messagesSentDetails = sentData,
                    recipientCount = new[] { new { recipients = contactCount } }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the data.");
                return StatusCode(500, new
                {
                    status = "Error",
                    status_Description = $"An error occurred: {ex.Message}"
                });
            }
        }


        [HttpGet]
        public IActionResult GetOperatorCombinedStatisticsByDateRange([FromServices] IDbHandler dbHandler, int workspaceId, DateTime from_date, DateTime to_date)
        {
            try
            {
                DataTable chartDetails = null;
                DataTable campaignDetails = null;
                DataTable messagesSentDetails = null;
                DataTable recipientCount = null;

                string procedure1 = "GetOperatorDashboardChartDetailsByDateRange";
                _logger.LogInformation("Executing stored procedure1: ", procedure1);

                DateTime fromDate = from_date.Date;
                DateTime toDate = to_date.Date;

                var parameters = new Dictionary<string, object>
        {
            { "@OperatorId", workspaceId },
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

                string procedure2 = "GetOperatorDashboardCampaignDetailsByDateRange";
                _logger.LogInformation("Executing stored procedure2: ", procedure2);

                var parameters2 = new Dictionary<string, object>{
            { "@OperatorId", workspaceId },
            { "@from_date", fromDate },
            { "@to_date", toDate }
        };
                campaignDetails = dbHandler.ExecuteDataTable(procedure2, parameters2, CommandType.StoredProcedure);

                _logger.LogInformation("Campaign details retrieved: {RowCount} rows.", campaignDetails?.Rows.Count ?? 0);

                if (campaignDetails == null || campaignDetails.Rows.Count == 0)
                {
                    return NotFound(new { Status = "Error", Status_Description = "No data found." });
                }

                var campaignData = campaignDetails.AsEnumerable().Select(row => new
                {
                    CreatedDateOfWorkspaceId = row.Field<DateTime>("CreatedDateOfWorkspaceId"),
                    TotalCampaigns = row.Field<int?>("TotalCampaigns") ?? 0,
                    FilteredCampaigns = row.Field<int?>("FilteredCampaigns") ?? 0
                }).FirstOrDefault();

                string procedure3 = "GetOperatorDashboardMessagesSentDetailsByDateRange";
                _logger.LogInformation("Executing stored procedure3: ", procedure3);

                var parameters3 = new Dictionary<string, object>                {
            { "@OperatorId", workspaceId },
            { "@from_date", fromDate },
            { "@to_date", toDate }
        };
                messagesSentDetails = dbHandler.ExecuteDataTable(procedure3, parameters3, CommandType.StoredProcedure);

                _logger.LogInformation("Messages sent details retrieved: {RowCount} rows.", messagesSentDetails?.Rows.Count ?? 0);

                var sentData = messagesSentDetails.AsEnumerable().Select(row => new
                {
                    totalSent = row.Field<int?>("TotalSent") ?? 0
                }).ToList();

                _logger.LogInformation("Parsed messages sent details successfully. Data count: {Count}", sentData.Count);

                string procedure4 = "GetOperatorRecipientCountByWorkspaceIdByDateRange";

                var parameters4 = new Dictionary<string, object>                {
            { "@OperatorId", workspaceId },
            { "@from_date", fromDate },
            { "@to_date", toDate }
        };

                recipientCount = dbHandler.ExecuteDataTable(procedure4, parameters4, CommandType.StoredProcedure);



                var recipientCountList = recipientCount.AsEnumerable().Select(row => new
                {
                    recipients = row.Field<int?>("Recipients") ?? 0
                }).ToList();

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Individual statistics retrieved successfully",
                    ChartDetails = ChartData,
                    CampaignDetails = campaignData,
                    MessagesSentDetails = sentData,
                    RecipientCount = recipientCountList,
                    AccountCreatedDate = campaignData.CreatedDateOfWorkspaceId
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


        [HttpGet]
        public IActionResult GetOperatorContactsListByWorkspaceId([FromServices] IDbHandler dbHandler, int WorkspaceId)
        {
            try
            {
                string procedure = "GetOperatorContactsListByWorkspaceId"; // Stored procedure name


                _logger.LogInformation($"Executing stored procedure: {procedure}");


                var parameters = new Dictionary<string, object>
        {
            { "@WorkspaceId", WorkspaceId }
        };

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                // Execute the stored procedure without parameters
                DataTable contactList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                if (contactList == null || contactList.Rows.Count == 0)
                {
                    _logger.LogWarning("No list found.");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No list found",
                        CampaignCount = 0 // Return zero count if no rows found
                    });
                }

                // Transform the DataTable to a list of objects
                var contactListData = contactList.AsEnumerable().Select(row => new
                {

                    file_name = row.Field<string>("fileName"),
                    status = row.Field<string>("status"),
                    update_at = row.Field<DateTime>("updated_date"),
                    recipient = row.Field<int>("ContactCount"),
                    list_id = row.Field<int>("list_id"),
                    type = row.Field<string>("createdby")
                }).ToList();

                _logger.LogInformation("Contact lisr retrieved successfully. Data: {@CampaignListData}", contactListData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Contact list retrieved successfully",
                    contactListCount = contactList.Rows.Count, // Add the row count here
                    ContactList = contactListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while retrieving the contact list.");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the contact list: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetOperatorProfileImage([FromServices] IDbHandler dbHandler, string EmailId)
        {
            try
            {
                string procedure = "GetOperatorProfileImage";

                var parameters = new Dictionary<string, object>
        {
            { "@EmailId", EmailId },
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

    }
}
