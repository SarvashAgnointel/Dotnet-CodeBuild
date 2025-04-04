using DBAccess;
using System.Data;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using System;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using static TravelAd_Api.Models.AdvertiserAccountModel;
using static System.Net.WebRequestMethods;
using Azure;
using System.Collections.Concurrent;
using System.Threading.Channels;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Information;
using TravelAd_Api.Controllers;
using static TravelAd_Api.Models.SmsModel;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Xml;
using Inetlab.SMPP.Common;
using Org.BouncyCastle.Crypto.Tls;
using System.Security.Cryptography;
public class Dialler
{
    private readonly IDbHandler _dbHandler;
    private readonly SmsController _sms;
    private readonly IConfiguration _configuration;

    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(10); // Limit concurrency to 10 campaigns
    private readonly HashSet<int> _runningCampaigns = new HashSet<int>();
    private readonly object _lock = new object();
    public Dialler(IDbHandler dbHandler, SmsController sms, IConfiguration configuration)
    {
        _dbHandler = dbHandler;
        _sms = sms;
        _configuration = configuration;
    }
    private WhatsappAccountDetails GetWhatsappAccountDetailsByWId(int workspaceId)
    {
        var builder = new ConfigurationBuilder();
        IConfiguration config = builder.Build();
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

    public class MessageCounter
    {
        private readonly ConcurrentDictionary<string, int> _campaignMessageCounts = new ConcurrentDictionary<string, int>();

        public void Increment(string campaignId)
        {
            _campaignMessageCounts.AddOrUpdate(campaignId, 1, (key, currentValue) => currentValue + 1);
            Console.WriteLine($"[DEBUG] Incremented Counter for Campaign {campaignId}. Current Count: {_campaignMessageCounts[campaignId]}");
        }

        public int GetCount(string campaignId)
        {
            _campaignMessageCounts.TryGetValue(campaignId, out var count);
            return count;
        }

        public IDictionary<string, int> GetAllCounts()
        {
            Console.WriteLine($"[DEBUG] GetAllCounts Called. Entries: {_campaignMessageCounts.Count}");
            return new Dictionary<string, int>(_campaignMessageCounts);
        }
    }



    public static string GetServerUrl(int ServerId, IDbHandler dbHandler)
    {
        string procedure = "GetServerUrl";
        var parameters = new Dictionary<string, object>
                {
                    {"@ServerId",ServerId },
                };

        DataTable UrlList = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

        if (UrlList == null || UrlList.Rows.Count == 0)
        {
            return "";
        }

        var UrlListData = UrlList.AsEnumerable().Select(row => new
        {
            url = row.Field<string>("server_url"),
        }).ToList();

        return UrlListData[0].url;

    }


    // Method to process campaigns asynchronously
    public async Task ProcessCampaignsAsync()
    {
        Console.WriteLine("Starting campaign processing...");

        while (true)
        {
            try
            {
                var dt = _dbHandler.ExecuteDataTable("EXEC GetDistinctOpenCampaignDetails");

                if (dt.Rows.Count > 0)
                {
                    foreach (DataRow campaign in dt.Rows)
                    {
                        int campaignId = Convert.ToInt32(campaign["campaign_id"]);

                        lock (_lock)
                        {
                            if (_runningCampaigns.Contains(campaignId))
                                continue; // Skip if already running

                            _runningCampaigns.Add(campaignId);
                        }

                        // Start processing campaign with concurrency control
                        _ = this.ProcessCampaignAsync(campaignId);
                    }
                }
                else
                {
                    Console.WriteLine("No campaigns with status 'Open' found.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ProcessCampaignsAsync method: {ex.Message}");
            }

            await Task.Delay(5000); // Wait before checking for new campaigns
        }
    }

    // Ensure this method is inside the Dialler class
    private async Task ProcessCampaignAsync(int campaignId)
    {
        await _semaphore.WaitAsync(); // Ensure max 10 concurrent tasks

        try
        {

            var dt = _dbHandler.ExecuteDataTable($"EXEC GetOpenCampaignDetails {campaignId}");
            if (dt.Rows.Count > 0)
            {
                  // Shared Counter

                DateTime campaignStartDate = DateTime.Parse(dt.Rows[0]["start_date_time"].ToString());
                DateTime campaignEndDate = DateTime.Parse(dt.Rows[0]["end_date_time"].ToString());
                DateTime currentDateTime = DateTime.Now;
                TimeSpan currentTime = currentDateTime.TimeOfDay;

                TimeSpan deliveryStartTime = TimeSpan.Zero;
                TimeSpan deliveryEndTime = TimeSpan.Zero;

                if (!string.IsNullOrEmpty(dt.Rows[0]["delivery_start_time"].ToString()))
                    deliveryStartTime = TimeSpan.Parse(dt.Rows[0]["delivery_start_time"].ToString());

                if (!string.IsNullOrEmpty(dt.Rows[0]["delivery_end_time"].ToString()))
                    deliveryEndTime = TimeSpan.Parse(dt.Rows[0]["delivery_end_time"].ToString());

                if (currentDateTime >= campaignStartDate && currentDateTime <= campaignEndDate)
                {
                    var billingDetailsDt = _dbHandler.ExecuteDataTable($"EXEC Getwalletamount {dt.Rows[0]["workspace_id"].ToString()}");

                    if (billingDetailsDt.Rows.Count > 0)
                    {
                        decimal perMessageCost = Convert.ToDecimal(billingDetailsDt.Rows[0]["permessage"]);
                        if (Convert.ToInt32(dt.Rows[0]["campaign_budget"]) < perMessageCost)
                        {
                            Console.WriteLine($"Campaign {dt.Rows[0]["campaign_id"]} has insufficient budget. Required: {perMessageCost}, Available: {dt.Rows[0]["campaign_budget"]}");
                            return;  // Stop processing if budget is insufficient
                        }
                    }
                    else
                    {
                        Console.WriteLine($"No billing details found for workspace {dt.Rows[0]["workspace_id"]}. Skipping campaign {dt.Rows[0]["campaign_id"]}.");
                        return;
                    }

                    // **Run campaign based on frequency**
                    if (dt.Rows[0]["message_frequency"].ToString() == "Every Day" && currentTime >= deliveryStartTime && currentTime <= deliveryEndTime)
                    {
                        
                        int campaignDayCycle = (currentDateTime.Date - campaignStartDate.Date).Days + 1;
                        Console.WriteLine($"Current campaign cycle day: {campaignDayCycle}");

                        await RunCampaign(campaignId, campaignDayCycle, (decimal)billingDetailsDt.Rows[0]["permessage"]);
                    }
                    else if (dt.Rows[0]["message_frequency"].ToString() == "Every 2 Days")
                    {
                        int daysSinceStart = (currentDateTime.Date - campaignStartDate.Date).Days;

                        if (daysSinceStart % 2 == 0) // Runs every second day
                        {
                            if (currentTime >= deliveryStartTime && currentTime <= deliveryEndTime)
                            {
                                int campaignDayCycle = (currentDateTime.Date - campaignStartDate.Date).Days + 1;
                               
                                await RunCampaign(campaignId, campaignDayCycle, (decimal)billingDetailsDt.Rows[0]["permessage"]);
                            }
                        }
                    }
                    else if (dt.Rows[0]["message_frequency"].ToString() == "Every 3 Days")
                    {
                        int daysSinceStart = (currentDateTime.Date - campaignStartDate.Date).Days;

                        if (daysSinceStart % 3 == 0) // Runs every third day
                        {
                            if (currentTime >= deliveryStartTime && currentTime <= deliveryEndTime)
                            {
                                int campaignDayCycle = (currentDateTime.Date - campaignStartDate.Date).Days + 1;

                                await RunCampaign(campaignId, campaignDayCycle, (decimal)billingDetailsDt.Rows[0]["permessage"]);
                            }
                        }
                    }

                    else if (dt.Rows[0]["message_frequency"].ToString() == "Once a week")
                    {

                        int daysSinceStart = (currentDateTime.Date - campaignStartDate.Date).Days;

                        // Calculate the week number (0-based)
                        int weekNumber = daysSinceStart / 7;

                        // Calculate the day within the current week (0-6)
                        int dayInWeek = daysSinceStart % 7;

                        // Run campaign only on the first day of each week
                        if (dayInWeek == 0)
                        {
                            if (currentTime >= deliveryStartTime && currentTime <= deliveryEndTime)
                            {
                                int campaignDayCycle = (currentDateTime.Date - campaignStartDate.Date).Days + 1;

                                await RunCampaign(campaignId, campaignDayCycle, (decimal)billingDetailsDt.Rows[0]["permessage"]);                             //Console.WriteLine($"Running campaign {campaign.campaignId} - Cycle {cycleNumber + 1}, Day {dayInCycle + 1} of active period");
                            }
                            else
                            {
                                Console.WriteLine($"Current time is outside the delivery window for campaign {campaignId}.");
                            }
                        }

                    }
                    else
                    {
                        Console.WriteLine($"No Delivery Rate is Found");
                        return;
                    }

                }

                // **Update Message Count in DB**
               
            }
            else
            {
                Console.WriteLine("No campaigns with status 'Open' found.");
            }

            Console.WriteLine($"Campaign {campaignId} processed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing campaign {campaignId}: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _runningCampaigns.Remove(campaignId);
            }
            _semaphore.Release(); // Free up a slot
        }
    }


    private async Task RunCampaign(int campaignId, int campaignDayCycle,decimal perMessage)
    {
        // Limit concurrent contact processing to 50
        var contactSemaphore = new SemaphoreSlim(50);
        var messageCounter = new MessageCounter();



        // List to store campaign data
        var campaignData = new List<(string campaignId, string listId, string firstName, string lastName, string phoneNo, string campaignName, string templateName, string channelType, string startDate, string endDate, string messageFrequency, string deliveryStartTime, string deliveryEndTime, string workspaceInfoId, int serverId, int connectionId, string smsNumber, int campaignBudget, string location, string contact_id, int daynumber)>();

        var parameters = new Dictionary<string, object>
    {
        { "@campaign_id", campaignId },
        { "@campaignDayCycle", campaignDayCycle } // Adjust percentage as needed
    };

        // Fetch campaign contacts
        var dtPercentage = _dbHandler.ExecuteDataTable("EXEC GetPercentageOfCampaignContacts @campaign_id, @campaignDayCycle", parameters);
        
        // Check if result set is null or empty
        if (dtPercentage == null || dtPercentage.Rows.Count == 0)
        {
            Console.WriteLine($"No data returned for campaign {campaignId}.");
            return;
        }





        
        _dbHandler.ExecuteDataTable($"EXEC updateCampaignProcessing {campaignId}");

        // Populate campaign data from result
        foreach (DataRow row in dtPercentage.Rows)
        {
            campaignData.Add((
                campaignId: row["campaign_id"].ToString(),
                listId: row["list_id"].ToString(),
                firstName: row["firstname"].ToString(),
                lastName: row["lastname"].ToString(),
                phoneNo: row["phoneno"].ToString(),
                campaignName: row["campaign_name"].ToString(),
                templateName: row["template_full_name"].ToString(),
                channelType: row["channel_full_type"].ToString(),
                startDate: row["start_date_time"].ToString(),
                endDate: row["end_date_time"].ToString(),
                messageFrequency: row["message_frequency"].ToString(),
                deliveryStartTime: row["delivery_start_time"].ToString(),
                deliveryEndTime: row["delivery_end_time"].ToString(),
                workspaceInfoId: row["workspace_id"].ToString(),
                serverId: Convert.ToInt32(row["smpp_id"]),
                connectionId: Convert.ToInt32(row["sms_connection_id"]),
                smsNumber: row["sms_number"].ToString(),
                campaignBudget: Convert.ToInt32(row["campaign_budget"]),
                location: row["location"].ToString(),
                contactId: row["contact_id"].ToString(),
                daynumber: Convert.ToInt32(row["DayNumber"])
            ));
        }



        DataTable dtmain1 = _dbHandler.ExecuteDataTable($"SELECT * FROM dbo.ta_meta_templates WHERE template_name='{dtPercentage.Rows[0]["template_full_name"].ToString()}'");
        if (dtmain1.Rows.Count == 0)
        {
            Console.WriteLine($"No template found with name {dtPercentage.Rows[0]["template_full_name"].ToString()}");
            return;
        }

        string template_language = dtmain1.Rows[0]["language"].ToString();
        string components = dtmain1.Rows[0]["components"].ToString();
        string templateId = dtmain1.Rows[0]["template_id"].ToString();

        var advancedSMSData = new AdvancedSMSData { };

        if (dtPercentage.Rows[0]["channel_full_type"].ToString() == "SMS")
        {
            DataTable AdvancedSMSSettings = _dbHandler.ExecuteDataTable($"EXEC getAdvanceSMSConnection {Convert.ToInt32(dtPercentage.Rows[0]["sms_connection_id"])}");

            advancedSMSData = new AdvancedSMSData
            {
                senderTON = Convert.ToInt32(AdvancedSMSSettings?.Rows[0]["sender_ton"]),
                senderNPI = Convert.ToInt32(AdvancedSMSSettings?.Rows[0]["sender_ton"]),
                receiverTON = Convert.ToInt32(AdvancedSMSSettings?.Rows[0]["destination_ton"]),
                receiverNPI = Convert.ToInt32(AdvancedSMSSettings?.Rows[0]["destination_npi"]),
                serviceType = AdvancedSMSSettings?.Rows[0]["service_type"].ToString(),
                dataEncoding = Convert.ToInt32(AdvancedSMSSettings?.Rows[0]["data_coding"]),
                characterEncoding = AdvancedSMSSettings?.Rows[0]["character_encoding"].ToString()
        };
        
            
        }


        var parames = new Dictionary<string, object>
    {
        { "@workspaceId", Convert.ToInt32(campaignData[0].workspaceInfoId) },
        { "@totalContacts", campaignData.Count }, // Adjust percentage as needed
        {"@campaign_id",  Convert.ToInt32(campaignData[0].campaignId) }
    };

        // Fetch campaign contacts
        var possibleSendContactsCount = _dbHandler.ExecuteScalar("EXEC ThreadCampaignWalletProcessing @workspaceId, @totalContacts, @campaign_id", parames);

        var failureCase = new failureCase
        {
            count = 0
        };






        // Create a list of async tasks to process each contact in parallel (max 50 at a time)
        List<Task> contactProcessingTasks = new List<Task>();

        foreach (var campaign in campaignData)
        {

            contactProcessingTasks.Add(Task.Run(async () =>
            {
                await contactSemaphore.WaitAsync(); // Ensure max 50 concurrent contacts
                try
                {
                    await ProcessCampaign((campaign.campaignId, campaign.listId, campaign.firstName, campaign.lastName, campaign.phoneNo, campaign.campaignName, campaign.templateName, campaign.channelType, campaign.startDate, campaign.serverId, campaign.connectionId, campaign.smsNumber, campaign.endDate , campaign.location , campaign.contact_id , campaign.daynumber, campaign.workspaceInfoId),template_language,components,templateId, messageCounter,failureCase, advancedSMSData);
                }
                finally
                {
                    contactSemaphore.Release(); // Free up a slot
                }
            }
            )
           );
        }


        // Wait for all contact processing tasks to complete
        await Task.WhenAll(contactProcessingTasks);


        try
        {
            foreach (var countEntry in messageCounter.GetAllCounts())
            {
                Console.WriteLine($"Campaign ID: {countEntry.Key}, Messages Sent: {countEntry.Value}");
                var channel = _dbHandler.ExecuteDataTable($"EXEC GetChannelTypeByCampaignId {countEntry.Key}");

                if (channel != null && channel.Rows.Count > 0)
                {
                    string channelType = channel.Rows[0]["channel_name"]?.ToString();
                    string procedure = "InsertCampaignSent";
                    string sentDate = DateTime.Now.ToString("yyyy-MM-dd");

                    if (!string.IsNullOrEmpty(channelType))
                    {
                        var paras = new Dictionary<string, object>
                            {
                                { "@CampaignId", countEntry.Key },
                                { "@SentDate", sentDate }
                            };

                        if (channelType == "WhatsApp")
                        {
                            paras.Add("@WhatsApp", countEntry.Value);
                        }
                        else if (channelType == "SMS")
                        {
                            paras.Add("@Sms", countEntry.Value);
                        }

                        if (paras.ContainsKey("@WhatsApp") || paras.ContainsKey("@Sms"))
                        {
                            int rowsAffected = _dbHandler.ExecuteNonQuery(procedure, paras, CommandType.StoredProcedure);

                            if (rowsAffected > 0)
                            {
                                Console.WriteLine($"✅ Successfully stored {countEntry.Value} messages for campaign {countEntry.Key}.");
                            }
                            else
                            {
                                Console.WriteLine($"❌ No rows affected. Check if campaign {countEntry.Key} exists.");
                            }

                        }
                    }
                }
                else
                {
                    Console.WriteLine($"Error: Channel information for campaign {countEntry.Key} not found.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating message count for campaign {campaignId}: {ex.Message}");
        }




        //wallet amount handling for failure cases
        int Result = _dbHandler.ExecuteNonQuery("UpdateWalletAmountandExistingBudget",
                    new Dictionary<string, object>
                    {
                                { "@campaignId",Convert.ToInt32(campaignData[0].campaignId)},
                                { "@workspaceId",Convert.ToInt32(campaignData[0].workspaceInfoId) },
                                {"@count",failureCase.count },
                    }, CommandType.StoredProcedure);

        if (Result > 0)
        {
            Console.WriteLine($"Updated Wallet Amount");
        }
        else
        {
            Console.WriteLine($"Failed to Update Wallet Amount");
        }

        //campaign status updation based on day_number
        int Result2 = _dbHandler.ExecuteNonQuery("CheckCampaignStatus",
                    new Dictionary<string, object>
                    {
                                { "@campaign_id",Convert.ToInt32(campaignData[0].campaignId)},
                                {"@day_number",Convert.ToInt32(campaignData[0].daynumber) }
                    }, CommandType.StoredProcedure);

        if (Result2 > 0)
        {
            Console.WriteLine($"Updated Campaign Status");
        }
        else
        {
            Console.WriteLine($"Failed to Update Campaign Status");
        }

    }

    private async Task ProcessCampaign((string campaignId, string listId, string firstName, string lastName, string phoneNo, string campaignName, string templateName, string channelType, string startDate, int serverId, int connectionId, string smsNumber, string endDate, string location , string contact_id, int daynumber, string workspaceId) campaign, string template_language, string components, string templateId, MessageCounter messageCounter,failureCase failureCase, AdvancedSMSData advancedSMSData)
    {
        try
        {
            Console.WriteLine($"Campaign {campaign.campaignId} Processing Started.");
            Console.WriteLine($"Processing contact {campaign.firstName} {campaign.lastName} with phone number: {campaign.phoneNo}");

            if (campaign.channelType == "WhatsApp")
            {
                Console.WriteLine($"Sending WhatsApp message to {campaign.phoneNo}");
                Console.WriteLine($"Contact_Id {campaign.contact_id}");
                Console.WriteLine($"Location {campaign.location}");
                Console.WriteLine($"DayNumber {campaign.daynumber}");


                // Call WhatsApp_contacts_dialAsync
                await WhatsApp_contacts_dialAsync(
                    _dbHandler,
                    _sms,
                    _configuration,
                    campaign.listId,
                    campaign.campaignId,
                    campaign.campaignName,
                    campaign.channelType,
                    campaign.phoneNo,
                    campaign.startDate,
                    campaign.endDate,
                    campaign.templateName,
                    campaign.firstName,
                    campaign.lastName,
                    campaign.location,
                    campaign.contact_id,
                    campaign.daynumber,
                    campaign.smsNumber,
                    campaign.workspaceId,
                    template_language,
                    components,
                    templateId,
                    messageCounter,
                    failureCase
                );
            }
            if (campaign.channelType == "SMS")
            {
                Console.WriteLine($"Sending SMS message to {campaign.phoneNo}");

                // Call WhatsApp_contacts_dialAsync
                await SMS_contacts_dialAsync(
                    _dbHandler,
                    _sms,
                    _configuration,
                    campaign.listId,
                    campaign.campaignId,
                    campaign.campaignName,
                    campaign.channelType,
                    campaign.phoneNo,
                    campaign.startDate,
                    campaign.endDate,
                    campaign.templateName,
                    campaign.firstName,
                    campaign.lastName,
                    campaign.location,
                    campaign.contact_id,
                    campaign.daynumber,
                    campaign.serverId,
                    campaign.connectionId,
                    campaign.smsNumber,
                    campaign.workspaceId,
                    template_language,
                    components,
                    templateId,
                    messageCounter,
                    failureCase,
                    advancedSMSData
                );
            }
            else
            {
                Console.WriteLine("Unknown channel type.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing campaign {campaign.campaignId}: {ex.Message}");
        }
    }

    public static string ExtractBodyComponent(string inputJson, string templateId)
    {
        var builder = new ConfigurationBuilder();
        IConfiguration config = builder.Build();
        JArray originalJson = JArray.Parse(inputJson);
        var transformedJson = new JArray();

        foreach (var item in originalJson)
        {
            string type = item.Value<string>("type")?.ToLower();

            if (type == "header")
            {
                string format = item.Value<string>("format").ToLower();
                Console.WriteLine("Format : " + format);

                if (format == "text")
                {
                    // Skip adding headerObj if the format is "text"
                    continue;
                }

                JObject parameterObj = null;

                if (format == "document")
                {
                    parameterObj = new JObject
                    {
                        ["type"] = "document",
                        ["document"] = new JObject
                        {
                            ["link"] = "" // Add your document link here
                        }
                    };
                }
                else if (format == "image")
                {
                    parameterObj = new JObject
                    {
                        ["type"] = "image",
                        ["image"] = new JObject
                        {
                            ["link"] = $"{config["BackendUrl"]}AdvertiserAccount/api/GetFile?templateId={templateId}" // Provide the image link
                        }
                    };
                }
                else if (format == "video")
                {
                    parameterObj = new JObject
                    {
                        ["type"] = "video",
                        ["video"] = new JObject
                        {
                            ["link"] = "" // Provide the video link
                        }
                    };
                }
                else
                {
                    throw new ArgumentException("Unsupported format: " + format);
                }

                var headerObj = new JObject
                {
                    ["type"] = "header",
                    ["parameters"] = new JArray
        {
            parameterObj
        }
                };

                transformedJson.Add(headerObj);
            }


            if (type == "body")
            {
                string text = item.Value<string>("text");

                // Check for placeholders like {{1}} or {{2}}
                if (!string.IsNullOrEmpty(text) && Regex.IsMatch(text, @"\{\{\d+\}\}"))
                {
                    // Extract placeholders such as {{1}}, {{2}}, etc.
                    var matches = Regex.Matches(text, @"\{\{\d+\}\}");

                    var parameters = new JArray();
                    foreach (Match match in matches)
                    {
                        parameters.Add(new JObject
                        {
                            ["type"] = "text",
                            ["text"] = match.Value
                        });
                    }

                    var bodyObj = new JObject
                    {
                        ["type"] = "body",
                        ["parameters"] = parameters
                    };

                    transformedJson.Add(bodyObj);
                }
            }
        }

        string jsonOutput = JsonConvert.SerializeObject(transformedJson);
        //string finalOutput = jsonOutput.Replace("\"", "\\\"");
        // Console.WriteLine(jsonOutput);
        // Console.WriteLine(finalOutput);
        return jsonOutput;
    }

    public static string ConvertJson(string inputJson, string name, string imageLink, string pdfLink, string templateId)
    {
        JArray originalJson = JArray.Parse(inputJson);
        var transformedJson = new JArray();

        foreach (var item in originalJson)
        {
            string type = item.Value<string>("type")?.ToLower();

            if (type == "header")
            {
                string format = item.Value<string>("format").ToLower();
                Console.WriteLine("Format : " + format);

                if (format == "text")
                {
                    // Skip adding headerObj if the format is "text"
                    continue;
                }

                JObject parameterObj = null;

                if (format == "document")
                {
                    parameterObj = new JObject
                    {
                        ["type"] = "document",
                        ["document"] = new JObject
                        {
                            ["link"] = $"https://travelad.agnointel.ai/AdvertiserAccount/api/GetFile?templateId={templateId}" // Add your document link here
                        }
                    };
                }
                else if (format == "image")
                {
                    parameterObj = new JObject
                    {
                        ["type"] = "image",
                        ["image"] = new JObject
                        {
                            ["link"] = $"https://travelad.agnointel.ai/AdvertiserAccount/api/GetFile?templateId={templateId}" // Provide the image link
                        }
                    };
                }
                else if (format == "video")
                {
                    parameterObj = new JObject
                    {
                        ["type"] = "video",
                        ["video"] = new JObject
                        {
                            ["link"] = $"https://travelad.agnointel.ai/AdvertiserAccount/api/GetFile?templateId={templateId}" // Provide the video link
                        }
                    };
                }
                else
                {
                    throw new ArgumentException("Unsupported format: " + format);
                }

                var headerObj = new JObject
                {
                    ["type"] = "header",
                    ["parameters"] = new JArray
        {
            parameterObj
        }
                };

                transformedJson.Add(headerObj);
            }
            if (type == "body")
            {
                // Ensure the text property is properly accessed
                string text = item.SelectToken("parameters[0].text")?.ToString() ?? item.Value<string>("text");

                if (!string.IsNullOrEmpty(text))
                {
                    // Check for placeholders like {{1}}, {{2}}, etc.
                    var matches = Regex.Matches(text, @"\{\{\d+\}\}");

                    if (matches.Count > 0)
                    {
                        var parameters = new JArray();

                        // Extract placeholders such as {{1}}, {{2}}, etc.
                        foreach (Match match in matches)
                        {
                            parameters.Add(new JObject
                            {
                                ["type"] = "text",
                                ["text"] = match.Value
                            });
                        }

                        // Create the transformed JSON object
                        var bodyObj = new JObject
                        {
                            ["type"] = "body",
                            ["parameters"] = parameters
                        };

                        // Parse and transform the original JSON
                        JArray originalJson2 = new JArray { bodyObj }; // Assuming bodyObj is the JSON object you want to process

                        foreach (var originalItem in originalJson2)
                        {
                            string itemType = originalItem.Value<string>("type")?.ToLower();

                            if (itemType == "body")
                            {
                                // Access the text property of the first parameter
                                string innerText = originalItem.SelectToken("parameters[0].text")?.ToString();

                                if (!string.IsNullOrEmpty(innerText))
                                {
                                    // Replace placeholders
                                    innerText = ReplacePlaceholders(innerText, name, imageLink, pdfLink);
                                }

                                var transformedBodyObj = new JObject
                                {
                                    ["type"] = "body",
                                    ["parameters"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "text",
                                ["text"] = innerText ?? "" // Assign an empty string if text is null
                            }
                        }
                                };

                                transformedJson.Add(transformedBodyObj);
                            }
                        }
                    }
                }
            }


            else if (type == "buttons")
            {
                var buttonType = item["buttons"][0]["type"].ToString(); // Get button type
                JObject buttonObj = null; // Initialize as null

                if (buttonType == "COPY_CODE") // Handle COPY_CODE
                {
                    buttonObj = new JObject
                    {
                        ["type"] = "button",
                        ["sub_type"] = "copy_code",
                        ["index"] = "0",
                        ["parameters"] = new JArray
            {
                new JObject
                {
                    ["type"] = "coupon_code",
                    ["coupon_code"] = item["buttons"][0]["example"][0].ToString()
                }
            }
                    };
                }
                else if (buttonType == "URL") // Handle URL
                {
                    buttonObj = new JObject
                    {
                        ["type"] = "button",
                        ["sub_type"] = "URL",
                        ["index"] = "1",
                        ["parameters"] = new JArray
            {
                new JObject
                {
                    ["type"] = "text",
                    ["text"] = item["buttons"][0]["url"].ToString()
                }
            }
                    };
                }

                // Only add to transformedJson if buttonObj is not null
                if (buttonObj != null)
                {
                    transformedJson.Add(buttonObj);
                }
            }




        }
        return JsonConvert.SerializeObject(transformedJson, Newtonsoft.Json.Formatting.None);
    }

    private static string ReplacePlaceholders(string text, string name, string imageLink, string pdfLink)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return text
            .Replace("{{1}}", name ?? "")
            .Replace("{{2}}", imageLink ?? "")
            .Replace("{{3}}", pdfLink ?? "");
    }

    private static async Task WhatsApp_contacts_dialAsync(
        [FromServices] IDbHandler dbHandler,
        [FromServices] SmsController sms,
        IConfiguration configuration,
        string listid,
        string campaignid,
        string campaign_name,
        string channelType,
        string phoneNo,
        string start_time,
        string end_time,
        string templateName,
        string firstname,
        string lastname,
        string location,
        string contact_id,
        int daynumber,
        string whatsappNumber,
        string workspaceId,
        string template_language,
        string components,
        string templateId,
        MessageCounter messageCounter,
        failureCase failureCase)
    {
        int msg_count = 0; // Track messages sent for the campaign
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        Console.WriteLine($"Workspace ID: {workspaceId}");

        Dialler dialler = new Dialler(dbHandler, sms, configuration);
        var whatsappDetails = dialler.GetWhatsappAccountDetailsByWId(Convert.ToInt32(workspaceId));
        if (whatsappDetails == null)
        {
            Console.WriteLine($"No WhatsApp account details found for workspace ID: {workspaceId}");
            return;
        }


            string callernumber = phoneNo;

            string extractedBodyComponent = ExtractBodyComponent(components, templateId);
            string data1 = ConvertJson(components, firstname, lastname, location, templateId);

            string messagePayload = $"{{ \"messaging_product\": \"whatsapp\", \"recipient_type\": \"individual\", \"to\": \"{callernumber}\", \"type\": \"template\", \"template\": {{ \"name\": \"{templateName}\", \"language\": {{ \"code\": \"{template_language}\" }}, \"components\": {data1} }} }}";
            byte[] msgText = Encoding.ASCII.GetBytes(messagePayload);

            var request = (HttpWebRequest)WebRequest.Create($"{config["facebookApiUrl"].TrimEnd('/')}/{whatsappNumber}/messages");
            request.Method = "POST";
            request.Headers.Add("Authorization", "Bearer " + config["WhatsAppToken"]);
            request.ContentType = "application/json";
            request.ContentLength = msgText.Length;

            try
            {
                await using (var stream = request.GetRequestStream())
                {
                    stream.Write(msgText, 0, msgText.Length);
                }

                using (var response = (HttpWebResponse)await request.GetResponseAsync())
                using (var responseStream = response.GetResponseStream())
                using (var reader = new StreamReader(responseStream))
                {
                    string responseText = await reader.ReadToEndAsync();
                    Console.WriteLine("API Response: " + responseText);

                    JObject jsonResponse = JObject.Parse(responseText);
                    string messageId = jsonResponse["messages"]?[0]?["id"]?.ToString();

                    if (!string.IsNullOrEmpty(messageId))
                    {
                        Console.WriteLine($"Message ID: {messageId} sent to {callernumber}");
                        messageCounter.Increment(campaignid);
                        msg_count++;
                    // Update database after sending
                        var updateResult = dbHandler.ExecuteScalar("EXEC UpdateCampaignContactAndPaymentDetails @campaign_id, @contact_id, @phoneno, @DayNumber",
                            new Dictionary<string, object>
                            {
                                { "@campaign_id", campaignid },
                                { "@contact_id", contact_id },
                                { "@phoneno", callernumber },
                                { "@DayNumber", daynumber },
                              
                            });

                        if (updateResult != null)
                        {
                            Console.WriteLine($"Updated campaign contact {contact_id}");
                        }
                        else
                        {
                            Console.WriteLine($"Failed to update campaign contact {contact_id}");
                        }
                }
                    else
                    {
                        failureCase.count++;
                        Console.WriteLine($"Message sending failed for {callernumber}");
                    var updateResult = dbHandler.ExecuteScalar("EXEC UpdateCampaignContactAndPaymentDetails @campaign_id, @contact_id, @phoneno, @DayNumber, @Status",
                    new Dictionary<string, object>
                    {
                                                    { "@campaign_id", campaignid },
                                                    { "@contact_id", contact_id },
                                                    { "@phoneno", callernumber },
                                                    { "@DayNumber", daynumber },
                                                    {"@Status", "Failed" }

                    });

                    if (updateResult != null)
                    {
                        Console.WriteLine($"Updated campaign contact {contact_id}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to update campaign contact {contact_id}");
                    }
                }
                }

         
            }
            catch (Exception ex)
            {
                failureCase.count++;
                Console.WriteLine($"Error sending message to {callernumber}: {ex.Message}");
            var updateResult = dbHandler.ExecuteScalar("EXEC UpdateCampaignContactAndPaymentDetails @campaign_id, @contact_id, @phoneno, @DayNumber, @Status",
                new Dictionary<string, object>
                {
                                                    { "@campaign_id", campaignid },
                                                    { "@contact_id", contact_id },
                                                    { "@phoneno", callernumber },
                                                    { "@DayNumber", daynumber },
                                                    {"@Status", "Failed" }

                });
            if (updateResult != null)
            {
                Console.WriteLine($"Updated campaign contact {contact_id}");
            }
            else
            {
                Console.WriteLine($"Failed to update campaign contact {contact_id}");
            }

        }
        

        // Check if any messages were sent and log it
        if (msg_count > 0)
        {
            Console.WriteLine($"Total {msg_count} messages sent for campaign {campaignid}");
        }
        else
        {
            Console.WriteLine($"No messages were successfully sent for campaign {campaignid}");
        }
    }


    private static async Task SMS_contacts_dialAsync([FromServices] IDbHandler dbHandler, [FromServices] SmsController sms, IConfiguration configuration,
            string listid,
            string campaignid,
            string campaign_name,
            string channelType,
            string phoneNo,
            string start_time,
            string end_time,
            string templateName,
            string firstname,
            string lastname,
            string location,
            string contact_id,
            int daynumber,
            int serverId,
            int connectionId,
            string smsNumber,
             string workspaceId,
            string template_language,
            string components,
            string templateId,
            MessageCounter messageCounter,
            failureCase failureCase,
            AdvancedSMSData advancedSMSData)
    {
        int msg_count = 0;
        var builder = new ConfigurationBuilder();
        builder.SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        IConfiguration config = builder.Build();

        List<SMSComponent> componentList = JsonConvert.DeserializeObject<List<SMSComponent>>(components);

        // Access the 'text' field of the first component
        if (componentList != null && componentList.Count > 0)
        {
            string text = componentList[0].text;
            Console.WriteLine($"Extracted text: {text}");
        }
        else
        {
            Console.WriteLine("No components found in the JSON.");
        }

        if (advancedSMSData == null)
        {
            Console.WriteLine("Advanced parameters for SMS is not processed.");
        }

        
        Console.WriteLine("Workspace ID: " + workspaceId);

        Dialler dialler = new Dialler(dbHandler, sms, configuration);

        // Ensure serverId is not null
        if (serverId == 0 || connectionId == 0)
        {
            Console.WriteLine("No SMS channel details found for workspace ID: " + workspaceId);
            return;
        }

        string callernumber = phoneNo;

        // Construct the URL

        var otherProjectBaseUrl = GetServerUrl(serverId, dbHandler);
        var url = $"{otherProjectBaseUrl}/Message/api/send?serverId={serverId}&ConnectionId={connectionId}";

        try
        {
            // Prepare the message payload for sending the request
            var messagePayload = new SendSmsRequest
            {
                Sender = smsNumber,  // Replace with the actual sender ID
                Receiver = callernumber,
                Message = componentList[0].text,  // Extracted text from the components JSON
                ChannelId = connectionId,
                SenderTON = (byte)advancedSMSData.senderTON,
                SenderNPI = (byte)advancedSMSData.senderNPI,
                DestinationTON = (byte)advancedSMSData.receiverTON,
                DestinationNPI = (byte)advancedSMSData.receiverNPI,
                ServiceType = advancedSMSData.serviceType,
                DataCoding = (DataCodings)advancedSMSData.dataEncoding,
                CharacterEncoding = advancedSMSData.characterEncoding
            };

            var smsResponse = await sms.SendSmsAsync(messagePayload, serverId, connectionId);


            if (smsResponse is ObjectResult objectResult)
            {
                var response = objectResult.Value as dynamic; // Convert to dynamic or specific type

                if (response != null && response.DeliveryStatus.Equals("Delivered", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("✅ The SMS was successfully delivered.");
                    messageCounter.Increment(campaignid);
                    msg_count++;
                    var updateResult = dbHandler.ExecuteScalar("EXEC UpdateCampaignContactAndPaymentDetails @campaign_id, @contact_id, @phoneno, @DayNumber",
                    new Dictionary<string, object>
                    {
                        { "@campaign_id", campaignid },
                        { "@contact_id", contact_id },
                        { "@phoneno", callernumber },
                        { "@DayNumber", daynumber },

                    });

                    if (updateResult != null)
                    {
                        Console.WriteLine($"Updated campaign contact {contact_id}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to update campaign contact {contact_id}");
                    }
                }
                else
                {
                    Console.WriteLine($"⚠️ SMS delivery status: {response?.DeliveryStatus}");
                    failureCase.count++;
                    var updateResult = dbHandler.ExecuteScalar("EXEC UpdateCampaignContactAndPaymentDetails @campaign_id, @contact_id, @phoneno, @DayNumber, @Status",
                    new Dictionary<string, object>
                    {
                                                                                { "@campaign_id", campaignid },
                                                                                { "@contact_id", contact_id },
                                                                                { "@phoneno", callernumber },
                                                                                { "@DayNumber", daynumber },
                                                                                {"@Status", "Failed" }

                    });
                    if (updateResult != null)
                    {
                        Console.WriteLine($"Updated campaign contact {contact_id}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to update campaign contact {contact_id}");
                    }

                }
            }
            else
            {
                Console.WriteLine("❌ Failed to retrieve SMS delivery status.");
                failureCase.count++;
                var updateResult = dbHandler.ExecuteScalar("EXEC UpdateCampaignContactAndPaymentDetails @campaign_id, @contact_id, @phoneno, @DayNumber, @Status",
                new Dictionary<string, object>
                {
                                                            { "@campaign_id", campaignid },
                                                            { "@contact_id", contact_id },
                                                            { "@phoneno", callernumber },
                                                            { "@DayNumber", daynumber },
                                                            {"@Status", "Failed" }

                });
            if (updateResult != null)
                {
                    Console.WriteLine($"Updated campaign contact {contact_id}");
                }
                else
                {
                    Console.WriteLine($"Failed to update campaign contact {contact_id}");
                }

            }



            Console.WriteLine($"Response Content: {smsResponse}");
            // Increment message counter and log the result
            // Call the stored procedure to update contact status and payment details
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            Console.WriteLine(ex.StackTrace);
            failureCase.count++;
            var updateResult = dbHandler.ExecuteScalar("EXEC UpdateCampaignContactAndPaymentDetails @campaign_id, @contact_id, @phoneno, @DayNumber, @Status",
                            new Dictionary<string, object>
                            {
                                                                            { "@campaign_id", campaignid },
                                                                            { "@contact_id", contact_id },
                                                                            { "@phoneno", callernumber },
                                                                            { "@DayNumber", daynumber },
                                                                            {"@Status", "Failed" }

                            });
            if (updateResult != null)
            {
                Console.WriteLine($"Updated campaign contact {contact_id}");
            }
            else
            {
                Console.WriteLine($"Failed to update campaign contact {contact_id}");
            }

        }


    }

}

