using Amazon.Runtime.Internal.Transform;
using Azure;
using DBAccess;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Numeric;
using Org.BouncyCastle.Crypto.Tls;
using System;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using TravelAd_Api.Models;
using static TravelAd_Api.Models.SmsModel;

namespace TravelAd_Api.Controllers
{
    
    [Route("[controller]/api")]
    [ApiController]
    [EnableCors("AllowSpecificOrigin")]
    public class SmsController : ControllerBase
    {
        //private readonly SmppClientService _smppClientService;
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly IDbHandler _dbHandler;
        private readonly ILogger<AdvertiserAccountController> _logger;
        public SmsController(HttpClient httpClient, IConfiguration configuration, IDbHandler dbHandler, ILogger<AdvertiserAccountController> logger)
        {
            // _smppClientService = smppClientService ?? throw new ArgumentNullException(nameof(smppClientService));
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _dbHandler = dbHandler;
            _logger = logger;
        }

        private string GetServerUrl(int ServerId)
        {
            string procedure = "GetServerUrl";
            var parameters = new Dictionary<string, object>
                {
                    {"@ServerId",ServerId },
                };

            DataTable UrlList = _dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

            if (UrlList == null || UrlList.Rows.Count == 0)
            {
                _logger.LogInformation("No connections found");
                return "";
            }

            var UrlListData = UrlList.AsEnumerable().Select(row => new
            {
                url = row.Field<string>("server_url"),
            }).ToList();

            return UrlListData[0].url;

        }





        [HttpPost("connect")]
        public async Task<IActionResult> ConnectAsync([FromBody] SmppConnection request, int ServerId)
        {
            if (request == null)
                return BadRequest(new { Status = "Error", Status_Description = "❌ Invalid request. Please provide connection details." });

            try
            {
                var otherProjectBaseUrl = GetServerUrl(ServerId);
                var url = $"{otherProjectBaseUrl}/Message/connect";

                var jsonContent = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, jsonContent);

                return await HandleApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Failed to connect to SMPP server.");
            }
        }


        [HttpPost("send")]
        public async Task<IActionResult> SendSmsAsync([FromBody] SendSmsRequest request, int serverId, int ConnectionId)
        {
            if (request == null)
                return BadRequest(new { Status = "Error", Status_Description = "❌ Invalid request. Please provide SMS details." });

            try
            {
                var otherProjectBaseUrl = GetServerUrl(serverId);
                var url = $"{otherProjectBaseUrl}/Message/send";

                var jsonContent = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, jsonContent);

                return await HandleApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error sending SMS.");
            }
        }
        
        
        /// 📌 Send Bulk SMS (Calls MessageController in another project)
        [HttpPost("sendBulk")]
        public async Task<IActionResult> SendBulkSmsAsync([FromBody] SendBulkSmsRequest request, int serverId, int ConnectionId)
        {
            if (request == null || request.ChannelId == 0 || request.Recipients == null || request.Recipients.Count == 0)
            {
                return BadRequest(new
                {
                    Status = "Error",
                    Status_Description = "❌ Invalid request. Please provide a valid ChannelId and list of recipients.",
                });
            }

            try
            {
                //var otherProjectBaseUrl = _configuration["OtherProject:BaseUrl"];
                var otherProjectBaseUrl = GetServerUrl(serverId);
                var url = $"{otherProjectBaseUrl}/Message/sendBulk";

                var jsonContent = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, jsonContent);

                return await HandleApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error sending bulk SMS.");
            }
        }


        /// 📌 Disconnect from SMPP Server (Calls MessageController in another project)
        [HttpPost("disconnect")]
        public async Task<IActionResult> DisconnectAsync(int serverId, int ConnectionId)
        {
            try
            {
                //    var otherProjectBaseUrl = _configuration["OtherProject:BaseUrl"];
                var otherProjectBaseUrl = GetServerUrl(serverId);
                var url = $"{otherProjectBaseUrl}/Message/disconnect?channelId={ConnectionId}";
                var response = await _httpClient.PostAsync(url, null);
                return await HandleApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Failed to disconnect from SMPP server.");
            }
        }

        /// 📌 Check if SMPP is Connected (Calls MessageController in another project)
        [HttpGet("isAlive")]
        public async Task<IActionResult> IsSMPPConnectedAsync(int serverId, int ConnectionId)
        {
            try
            {
                //var otherProjectBaseUrl = _configuration["OtherProject:BaseUrl"];
                var otherProjectBaseUrl = GetServerUrl(serverId);
                var url = $"{otherProjectBaseUrl}/Message/isAlive?channelId={ConnectionId}";

                var response = await _httpClient.GetAsync(url);
                return await HandleApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error checking SMPP service.");
            }
        }

        /// 🔹 Common Method to Handle API Responses
        private async Task<IActionResult> HandleApiResponse(HttpResponseMessage response)
        {
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
                return Ok(JsonConvert.DeserializeObject<StatusBody>(responseContent));

            return StatusCode((int)response.StatusCode, JsonConvert.DeserializeObject<StatusBody>(responseContent));
        }

        /// 🔹 Common Method to Handle Exceptions
        private IActionResult HandleException(Exception ex, string errorMessage)
        {
            return StatusCode(500, new { Status = "Error", Status_Description = $"❌ {errorMessage}: {ex.Message}" });
        }


        [HttpGet("isServerAlive")]
        public async Task<IActionResult> IsServerAlive(int ServerId)
        {
            try
            {
                //var otherProjectBaseUrl = _configuration["OtherProject:BaseUrl"];
                var otherProjectBaseUrl = GetServerUrl(ServerId);
                var url = $"{otherProjectBaseUrl}/Message/isserveralive";

                var response = await _httpClient.GetAsync(url);
                return await HandleApiResponse(response);
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error checking SMPP service.");
            }
        }





        //code for handling servers and connections are listed here


        [HttpPost("createserver")]
        public object CreateServer([FromBody] CreateSMSServer server)
        {


            try
            {

                string insertQuery = "InsertSMSServer";

                var parameters = new Dictionary<string, object>
                {
                    {"serverName", server.ServerName},
                    {"serverUrl", server.ServerUrl },
                    {"serverType", server.ServerType }
                };

                int rowsAffected = _dbHandler.ExecuteNonQuery(insertQuery, parameters, CommandType.StoredProcedure);

                if (rowsAffected > 0)
                {
                    _logger.LogInformation("Server created successfully.");
                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = "Server created successfully"
                    });
                }
                else
                {
                    _logger.LogError("Server creation failed.");
                    return Ok(new
                    {
                        Status = "Error",
                        Status_Description = "Server creation failed."
                    });
                }
            }
            catch (Exception ex)
            {

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An exception occurred while creating server: {ex.Message}",
                });
            }

        }


        [HttpGet("getservers")]
        public IActionResult GetServerList()
        {
            try
            {
                string getServerList = "GetSMSServers";

                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getServerList);

                DataTable serverList = _dbHandler.ExecuteDataTable(getServerList);

                if (serverList == null || serverList.Rows.Count == 0)
                {
                    _logger.LogInformation("No servers found");
                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = "No servers found"
                    });
                }

                var serverListData = serverList.AsEnumerable().Select(row => new
                {

                    serverId = row.Field<int>("server_id"),
                    ServerName = row.Field<string>("server_name"),
                    ServerUrl = row.Field<string>("server_url")

                }).ToList();


                _logger.LogInformation("Servers retrieved successfully");
                _logger.LogInformation("Requests: {@channelListData}", serverListData);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Servers retrieved successfully",
                    ServerList = serverListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the server list: {ex.Message}");

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the server list: {ex.Message}"
                });
            }
        }


        [HttpGet("getconnections")]
        public IActionResult GetConnectionList(int ServerId)
        {
            try
            {
                string getConnectionList = "GetSmsConnections";

                var parameters = new Dictionary<string, object>
        {
            {"@ServerId", ServerId },
            {"@ConnectionId", null },
            {"@Mode", null }
        };

                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getConnectionList);

                DataTable connectionList = _dbHandler.ExecuteDataTable(getConnectionList, parameters, CommandType.StoredProcedure);

                if (connectionList == null || connectionList.Rows.Count == 0)
                {
                    _logger.LogInformation("No connections found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No connections found"
                    });
                }

                var connectionListData = connectionList.AsEnumerable().Select(row => new
                {
                    Id = row.Field<int>("connection_id"),
                    ChannelName = row.Field<string>("channel_name"),
                    Type = row.Field<string>("type"),
                    Host = row.Field<string>("host"),
                    Port = row.Field<int>("port"),
                    SystemId = row.Field<string>("system_id"),
                    Password = row.Field<string>("password"),
                    Created_date = row.Field<DateTime>("created_date"),
                    BindingTON = row.Field<int?>("binding_ton") ?? 0,
                    BindingNPI = row.Field<int?>("binding_npi") ?? 0,
                    SenderTON = row.Field<int?>("sender_ton") ?? 5,
                    SenderNPI = row.Field<int?>("sender_npi") ?? 0,
                    DestinationTON = row.Field<int?>("destination_ton") ?? 1,
                    DestinationNPI = row.Field<int?>("destination_npi") ?? 1,
                    BindMode= row.Field<int?>("bind_mode") ,
                    SystemType= row.Field<string>("system_type"),
                    AddressRange= row.Field<string>("address_range"),
                    TransportProtocol = row.Field<string>("transport_protocol"),
                }).ToList();

                _logger.LogInformation("Connections retrieved successfully");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Connections retrieved successfully",
                    connectionList = connectionListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the channel list: {ex.Message}");

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the account list: {ex.Message}"
                });
            }
        }

        [HttpGet("getconnectionsById")]
        public IActionResult GetConnectionListById(int ServerId, int ConnectionId)
        {
            try
            {
                string getConnectionList = "GetSmsConnections";

                var parameters = new Dictionary<string, object>
        {
            {"server_id", ServerId },
            {"@ConnectionId", ConnectionId },
            {"@Mode", "ById" }
        };

                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getConnectionList);

                DataTable connectionList = _dbHandler.ExecuteDataTable(getConnectionList, parameters, CommandType.StoredProcedure);

                if (connectionList == null || connectionList.Rows.Count == 0)
                {
                    _logger.LogInformation("No connections found");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No connections found"
                    });
                }

                var connectionListData = connectionList.AsEnumerable().Select(row => new
                {
                    Id = row.Field<int>("connection_id"),
                    ChannelName = row.Field<string>("channel_name"),
                    Type = row.Field<string>("type"),
                    Host = row.Field<string>("host"),
                    Port = row.Field<int>("port"),
                    SystemId = row.Field<string>("system_id"),
                    Password = row.Field<string>("password"),
                    Created_date = row.Field<DateTime>("created_date"),
                    BindingTON = row.Field<int?>("binding_ton") ?? 0,
                    BindingNPI = row.Field<int?>("binding_npi") ?? 0,
                    SenderTON = row.Field<int?>("sender_ton") ?? 5,
                    SenderNPI = row.Field<int?>("sender_npi") ?? 0,
                    DestinationTON = row.Field<int?>("destination_ton") ?? 1,
                    DestinationNPI = row.Field<int?>("destination_npi") ?? 1
                }).ToList();

                _logger.LogInformation("Connections retrieved successfully");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Connections retrieved successfully",
                    connectionList = connectionListData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the channel list: {ex.Message}");

                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the account list: {ex.Message}"
                });
            }
        }

        [HttpPost("createconnection")]
        public object CreateConnection([FromBody] SmppConnectionRequest cc)
        {
            try
            {
                string insertQuery = "InsertSmsConnection";

                var parameters = new Dictionary<string, object>
        {
            {"ChannelName", cc.ChannelName },
            {"Type", cc.Type },
            {"Host", cc.Host },
            {"Port", cc.Port },
            {"SystemId", cc.SystemId },
            {"Password", cc.Password },
            {"CreatedDate", DateTime.Now },
            {"ServerId", cc.ServerId },
            {"BindingTON", cc.BindingTON },
            {"BindingNPI", cc.BindingNPI },
            {"SenderTON", cc.SenderTON },
            {"SenderNPI", cc.SenderNPI },
            {"DestinationTON", cc.DestinationTON },
            {"DestinationNPI", cc.DestinationNPI },
            {"ShortCodes", cc.ShortCodes },
            {"TransportProtocol",cc.TransportProtocol },
            {"BindMode",cc.BindMode },
            {"AddressRange",cc.AddressRange },
            {"SystemType",cc.SystemType },
            {"DataCodingScheme",cc.DataCodingScheme },
            {"CharacterEncoding",cc.CharacterEncoding },
            {"ServiceType",cc.ServiceType }
        };

                object resultObj = _dbHandler.ExecuteScalar(insertQuery, parameters, CommandType.StoredProcedure);
                int channelId = resultObj != null ? Convert.ToInt32(resultObj) : 0;

                if (channelId > 0)
                {
                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = $"Connection inserted successfully with ID: {channelId}",
                        channel_id = channelId
                    });
                }
                else
                {
                    return BadRequest(new
                    {
                        Status = "Error",
                        Status_Description = "Insertion failed.",
                        channel_id = (object)null
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An exception occurred while processing the request: {ex.Message}",
                    channel_id = (object)null
                });
            }
        }
    }
}
