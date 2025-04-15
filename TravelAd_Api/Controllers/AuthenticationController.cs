using DBAccess;
using TravelAd_Api.DataLogic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Data;
using System.Net.Mail;
using System.Net;
using static TravelAd_Api.Models.ApiModel;
using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Azure.Core;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Threading.Channels;
using Microsoft.AspNetCore.Cors;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using TravelAd_Api.Controllers;
using Microsoft.Extensions.Logging;
using log4net;
using static System.Net.WebRequestMethods;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel;
using System.Net.Http.Headers;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Text;

namespace TravelAd_Api.Controllers
{
    [Authorize]
    [Route("[controller]/api/[action]")]
    [ApiController]
    [EnableCors("AllowSpecificOrigin")]
    public class AuthenticationController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IDbHandler _dbHandler;
        private readonly string fromEmail;
        private readonly ILogger<AuthenticationController> _logger;
        private readonly JWT _jwtHelper;
        public AuthenticationController(IConfiguration configuration, IDbHandler dbHandler, ILogger<AuthenticationController> logger)
        {
            _configuration = configuration;
            _dbHandler = dbHandler;
            fromEmail = _configuration.GetValue<string>("Emailid");
            _logger = logger;
            _jwtHelper = new JWT(configuration, dbHandler, logger);
        }


        private string DtToJSON(DataTable table)
        {
            string jsonString = JsonConvert.SerializeObject(table);
            return jsonString;
        }



        //__________________________________Authentication Module----------------------------------------//

        [AllowAnonymous]
        [HttpPost]
        public async Task<object> UserLoginAsync(TravelAd_Api.Models.ApiModel.UserLogin ul, [FromServices] IDbHandler dbHandler, [FromServices] IMemoryCache memoryCache)
        {
            DataTable dtmain = new DataTable();
            dtmain.Columns.Add("Status");
            dtmain.Columns.Add("Status_Description");
            dtmain.Columns.Add("Token");

            try
            {
                string checkEmailQuery = "dbo.userlogincheckemail";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", checkEmailQuery);

                var checkEmailParams = new Dictionary<string, object>
  {
      { "@Email", ul.Email },
  };

                _logger.LogInformation("Stored procedure parameters: {Parameters}", checkEmailParams);

                object emailExists = dbHandler.ExecuteScalar(checkEmailQuery, checkEmailParams, CommandType.StoredProcedure);
                _logger.LogInformation("Email existence check result: {Result}", emailExists);


                if (emailExists != null && Convert.ToInt32(emailExists) > 0)
                {
                    string checkPassword = "dbo.GetPassword";
                    _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", checkPassword);

                    var PasswordParams = new Dictionary<string, object>
     {
          { "@Email", ul.Email },
     };
                    DataTable passwordByMail = dbHandler.ExecuteDataTable(checkPassword, PasswordParams, CommandType.StoredProcedure);
                    _logger.LogInformation("Password retrieval result rows: {RowCount}", passwordByMail.Rows.Count);

                    if (passwordByMail.Rows.Count == 0)
                    {
                        _logger.LogError("No password found for email: {Email}", ul.Email);
                        dtmain.Rows.Add("Error", "User inserted but OTP sending failed.");
                        return DtToJSON(dtmain);
                    }

                    var password = passwordByMail.AsEnumerable().Select(row => new
                    {
                        password = row.Field<string>("password"),
                    }).ToList();

                    if (ul.Password == password[0].password)
                    {
                        _logger.LogInformation("Password verification succeeded for email: {Email}", ul.Email);

                        string otp = GenerateOtp();
                        int otpvalue = int.Parse(otp);
                        string hashedOtp = OtpService.HashOtp(otpvalue);
                        Console.WriteLine("Hashed OTP : " + hashedOtp);
                        _logger.LogInformation("Generated OTP: {Otp}, Hashed OTP: {HashedOtp}", otp, hashedOtp);

                        memoryCache.Set(ul.Email, hashedOtp, TimeSpan.FromMinutes(2));
                        _logger.LogInformation("OTP cached successfully for email: {Email}", ul.Email);


                        bool emailSent = await SendOtpToEmailResend(ul.Email, otp);

                        if (emailSent)
                        {

                            var token = _jwtHelper.GenerateToken();
                            _logger.LogInformation("OTP sent successfully to email: {Email}", ul.Email);
                            dtmain.Rows.Add("Success", "OTP sent to the email.", token);
                        }
                        else
                        {
                            _logger.LogError("Failed to send OTP to email: {Email}", ul.Email);
                            dtmain.Rows.Add("Error", "User inserted but OTP sending failed.");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Password verification failed for email: {Email}", ul.Email);
                        dtmain.Rows.Add("Error", "Password verification failed.");
                    }
                }
                else
                {
                    _logger.LogWarning("Email does not exist: {Email}", ul.Email);
                    dtmain.Rows.Add("Error", "Email does not exist.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing the user login request.");

                dtmain.Rows.Add("Network Error", ex.Message);
            }

            return DtToJSON(dtmain);
        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<object> CheckEmailExistsAsync(TravelAd_Api.Models.ApiModel.UserVerify uv, [FromServices] IDbHandler dbHandler, [FromServices] IMemoryCache memoryCache)
        {
            DataTable dtmain = new DataTable();
            dtmain.Columns.Add("Status");
            dtmain.Columns.Add("Status_Description");

            try
            {
                string checkEmailQuery = "dbo.CheckIfEmailExists";
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", checkEmailQuery);

                var checkEmailParams = new Dictionary<string, object>
{
    { "@Email", uv.Email }
};

                _logger.LogInformation("Stored procedure parameters: {Parameters}", checkEmailParams);


                var emailResult = dbHandler.ExecuteScalar(checkEmailQuery, checkEmailParams, CommandType.StoredProcedure);

                _logger.LogInformation("Email existence check result for email {Email}: {Result}", uv.Email, emailResult);


                string emailStatusQuery = "SELECT Status FROM dbo.ta_user_account_info WHERE Email = @Email";
                var status = dbHandler.ExecuteScalar(emailStatusQuery, checkEmailParams, CommandType.Text);

                if (status != null && status.ToString().ToLower() == "active")
                {
                    _logger.LogWarning("Error", "Email already exists with an active status.");
                    dtmain.Rows.Add("Error", "Email already exists. Please use a different email.");
                    return DtToJSON(dtmain);
                }
                else if (status != null && status.ToString().ToLower() == "inactive" || status == null && Convert.ToInt32(status) == 0)
                {
                    string otp = GenerateOtp();
                    int otpvalue = int.Parse(otp);

                    string hashedOtp = OtpService.HashOtp(otpvalue);
                    Console.WriteLine("Hashed OTP : " + hashedOtp);
                    _logger.LogInformation("Generated OTP: {Otp}, Hashed OTP: {HashedOtp} for email: {Email}", otp, hashedOtp, uv.Email);

                    memoryCache.Set(uv.Email, hashedOtp, TimeSpan.FromMinutes(2));
                    _logger.LogInformation("OTP cached successfully for email: {Email}", uv.Email);

                    bool emailSent = await SendOtpToEmailResend(uv.Email, otp);

                    if (emailSent)
                    {
                        _logger.LogInformation("Success", "OTP sent to the email.");
                        dtmain.Rows.Add("Success", "Email exists but is inactive. OTP sent to the email.");
                    }
                    else
                    {
                        _logger.LogError("Error", "OTP sending failed");
                        dtmain.Rows.Add("Error", "OTP sending failed.");
                    }
                }
                else
                {
                    _logger.LogWarning("Error", "Email status is not valid.");
                    dtmain.Rows.Add("Error", "Email status is not valid.");
                }

            }
            catch (Exception ex)
            {
                _logger.LogError("Error", ex.Message);
                dtmain.Rows.Add("Error", ex.Message);
            }

            return DtToJSON(dtmain);  // Return the response as JSON
        }

        private string GenerateOtp()
        {
            Random random = new Random();
            int otpValue = random.Next(100000, 999999);
            _logger.LogInformation("Generated OTP: {otpValue}", otpValue);
            return otpValue.ToString();

        }

        private bool SendOtpToEmail(string email, string otp)
        {
            try
            {
                _logger.LogInformation("Initializing SMTP client for email: {Email}", email);

                var smtpClient = new SmtpClient("smtp.gmail.com")
                {
                    Port = 587,
                    Credentials = new NetworkCredential(fromEmail, "bmpcnnyysmndsylt"),
                    EnableSsl = true,
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress("vimalrajs9718@gmail.com"),//support@click2go.ai
                    Subject = "Your OTP Code",
                    Body = "Your OTP code is: " + otp,
                    IsBodyHtml = false,
                };


                mailMessage.To.Add(email);//varshini.v@agnointel.ai
                smtpClient.Send(mailMessage);

                Console.WriteLine($"OTP sent to {email}: {otp}");
                _logger.LogInformation($"OTP sent to {email}: {otp}");


                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP to email: {Email}", email);
                return false;
            }
        }


        private async Task<bool> SendOtpToEmailResend(string email, string otp)
        {
            try
            {
                // Log that you’re about to send the email
                _logger.LogInformation("Initializing Resend API call for email: {Email}", email);

                // Prepare the JSON payload
                var requestPayload = new
                {
                    from = "TravelAd@mytravelad.com",  // Update "from" as needed
                    to = new[] { email },
                    subject = "Your OTP Code",
                    html = $"<p>Your OTP code is: {otp}</p>"
                };

                // Convert payload to JSON
                string jsonPayload = JsonConvert.SerializeObject(requestPayload);

                // Create an HttpClient instance
                using (HttpClient client = new HttpClient())
                {
                    // Replace 're_123456789' with your real Resend API key.
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", _configuration["ResendApiKey"]);

                    // Prepare content
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    // Resend endpoint
                    string resendApiUrl = _configuration["ResendApiUrl"];

                    // Send the POST request
                    HttpResponseMessage response = await client.PostAsync(resendApiUrl, content);

                    // Check if successful
                    if (response.IsSuccessStatusCode)
                    {
                        // Optionally, read the response content
                        string successContent = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation($"OTP sent to {email} via Resend. Response: {successContent}");
                        return true;
                    }
                    else
                    {
                        // If failure, log response content to see the error details
                        string errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError("Failed to send OTP to {Email}. Resend error: {Error}", email, errorContent);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception occurred while sending OTP to email: {Email}", email);
                return false;
            }
        }

        [AllowAnonymous]
        [HttpPost]
        public IActionResult VerifyOtp([FromBody] OtpVerificationModel model, [FromServices] IMemoryCache memoryCache, [FromServices] IDbHandler dbHandler)
        {
            try
            {
                _logger.LogInformation("Received OTP verification request for email: {Email}", model.Email);

                // Retrieve the hashed OTP from memory
                if (memoryCache.TryGetValue(model.Email, out string storedHashedOtp))
                {
                    _logger.LogInformation("OTP found in memory for email: {Email}", model.Email);

                    // Compare the OTPs
                    if (storedHashedOtp == model.Otp)
                    {
                        string query = "dbo.UpdateEmailSubscriptionByEmail";
                        _logger.LogInformation("Executing stored procedure: {ProcedureName}", query);

                        var parameters = new Dictionary<string, object>
        {
            { "@Email", model.Email },
            { "@EmailVerified", "yes" }
        };

                        _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                        dbHandler.ExecuteScalar(query, parameters, CommandType.StoredProcedure);

                        _logger.LogInformation("Successfully verified OTP for email: {Email}", model.Email);
                        var token = _jwtHelper.GenerateToken();
                        return Ok(new { Status = "Success", Message = "OTP verified successfully.", Token = token });

                    }
                    else
                    {
                        // Mark email as not verified
                        string query = "dbo.UpdateEmailSubscriptionByEmail";
                        _logger.LogInformation("Executing stored procedure: {ProcedureName}", query);

                        var parameters = new Dictionary<string, object>
        {
            { "@Email", model.Email },
            { "@EmailVerified", "no" }
        };

                        _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                        dbHandler.ExecuteScalar(query, parameters, CommandType.StoredProcedure);

                        _logger.LogWarning("Invalid OTP provided for email: {Email}", model.Email);

                        // Return 200 OK with an error message instead of a 400 status code
                        return Ok(new { Status = "Failed", Message = "Invalid OTP." });
                    }
                }
                else
                {
                    _logger.LogWarning("OTP expired or not found for email: {Email}", model.Email);

                    // Return 200 OK with an error message for expired or missing OTP
                    return Ok(new { Status = "Failed", Message = "OTP expired or not found." });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during OTP verification for email: {Email}", model.Email);

                return StatusCode(500, new { Status = "Error", Message = "An internal server error occurred. Please try again later." });
            }
        }

        [AllowAnonymous]
        [HttpPost("SendOtp")]
        public async Task<IActionResult> SendOtp([FromBody] OtpRequestModel request, [FromServices] IMemoryCache memoryCache)
        {
            string otp = GenerateOtp(); // Generates a new OTP

            int otpvalue = int.Parse(otp);

            string hashedOtp = OtpService.HashOtp(otpvalue);
            Console.WriteLine("Hashed OTP : " + hashedOtp);
            _logger.LogInformation("Hashed OTP : " + hashedOtp);

            // Store the hashed OTP in memory for 5 minutes
            memoryCache.Set(request.Email, hashedOtp, TimeSpan.FromMinutes(2));
            _logger.LogInformation("Stored hashed OTP in memory for email: {Email} with 2-minute expiration", request.Email);


            bool isSent = await SendOtpToEmailResend(request.Email, otp);

            if (isSent)
            {
                _logger.LogInformation("OTP sent successfully to email: {Email}", request.Email);

                return Ok(new { status = "Success", message = "OTP sent successfully" });
            }
            else
            {
                _logger.LogWarning("Failed to send OTP to email: {Email}", request.Email);

                return StatusCode(500, new { status = "Error", message = "Failed to send OTP. Please try again later." });
            }
        }

        [HttpPost]
        public IActionResult InsertUserPersonalInfo(TravelAd_Api.Models.ApiModel.UserPersonalInfoModel personalInfo, [FromServices] IDbHandler dbHandler)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                string getAccountIdQuery = "GetAccountIDByEmail";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getAccountIdQuery);
                var accountIdParams = new Dictionary<string, object>
                   {
                       { "@Email", personalInfo.Email }
                   };

                _logger.LogInformation("Stored procedure parameters: {Parameters}", accountIdParams);
                object accountIdResult = dbHandler.ExecuteScalar(getAccountIdQuery, accountIdParams, CommandType.StoredProcedure);

                if (accountIdResult == null)
                {
                    _logger.LogWarning("Account not found for Email: {Email}", personalInfo.Email);

                    return BadRequest(new { Status = "Error", Status_Description = "Account not found for the provided email." });
                }

                int accountId = Convert.ToInt32(accountIdResult);
                _logger.LogInformation("Fetched Account ID: {AccountId} for Email: {Email}", accountId, personalInfo.Email);



                // Step 2: Fetch the country_id using the provided country name
                string getCountryIdQuery = "GetCountryIdByName";
                var countryParams = new Dictionary<string, object>
{
    { "@CountryName", personalInfo.Country }
};

                object countryIdResult = dbHandler.ExecuteScalar(getCountryIdQuery, countryParams, CommandType.StoredProcedure);

                //if (countryIdResult == null)
                //{
                //    return BadRequest(new { Status = "Error", Status_Description = "Country not found for the provided name." });
                //}

                int countryId = Convert.ToInt32(countryIdResult);
                _logger.LogInformation("Fetched Country ID: {CountryId} for Country: {Country}", countryId, personalInfo.Country);


                // Step 3: Fetch the language_id using the provided language preference
                string getLanguageIdQuery = "GetLanguageIdByName";
                var languageParams = new Dictionary<string, object>
{
    { "@LanguageName", personalInfo.LanguagePreference }
};

                object languageIdResult = dbHandler.ExecuteScalar(getLanguageIdQuery, languageParams, CommandType.StoredProcedure);

                //if (languageIdResult == null)
                //{
                //    return BadRequest(new { Status = "Error", Status_Description = "Language not found for the provided preference." });
                //}

                int languageId = Convert.ToInt32(languageIdResult);
                _logger.LogInformation("Fetched Language ID: {LanguageId} for Language Preference: {Language}", languageId, personalInfo.LanguagePreference);



                // Step 4: Check if a record with the same AccountId and Email already exists
                string checkDuplicationQuery = "dbo.CheckUserPersonalInfoExists";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", checkDuplicationQuery);
                var duplicationParams = new Dictionary<string, object>
     {

 { "@Email", personalInfo.Email }
     };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", duplicationParams);
                object duplicationResult = dbHandler.ExecuteScalar(checkDuplicationQuery, duplicationParams, CommandType.StoredProcedure);

                //if (duplicationResult != null && Convert.ToInt32(duplicationResult) > 0)
                //{
                //    // Duplication found, return validation error
                //    return BadRequest(new { Status = "Error", Status_Description = "Personal information already exists for this account and email." });
                //}



                // Step 2: Insert personal info using the fetched AccountId, CountryId, and LanguageId
                string insertQuery = "dbo.InsertUserPersonalInfo";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", insertQuery);

                var parameters = new Dictionary<string, object>
{
    { "@AccountId", accountId },
    { "@FirstName", personalInfo.FirstName },
    { "@LastName", personalInfo.LastName },
    { "@Email", personalInfo.Email },
    { "@EmailSubscription", personalInfo.EmailSubscription },
    { "@AlternateNumber", personalInfo.AlternateNumber },
    { "@City", personalInfo.City },
    { "@Country", countryId },
    { "@Address", personalInfo.Address },
    { "@LanguagePreference", languageId },
    { "@Gender", personalInfo.Gender },
    { "@DateOfBirth", personalInfo.DateOfBirth },
    { "@Status", personalInfo.Status },
    { "@CreatedBy", personalInfo.CreatedBy },
    { "@CreatedDate", personalInfo.CreatedDate },
    { "@UpdatedBy", personalInfo.UpdatedBy },
    { "@UpdatedDate", personalInfo.UpdatedDate }
};

                _logger.LogInformation("Inserted Personal Info for Email: {Email}", personalInfo.Email);


                object result = dbHandler.ExecuteScalar(insertQuery, parameters, CommandType.StoredProcedure);

                string getPersonalIdQuery = "GetPersonalIdByEmail";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getPersonalIdQuery);

                var personalIdParams = new Dictionary<string, object>
 {
     { "@Email", personalInfo.Email }
 };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", personalIdParams);

                object personalIdResult = dbHandler.ExecuteScalar(getPersonalIdQuery, personalIdParams, CommandType.StoredProcedure);

                if (personalIdResult == null)
                {
                    _logger.LogError("Personal ID not found for the provided email.");

                    return BadRequest(new { Status = "Error", Status_Description = "Personal ID not found for the provided email." });
                }

                int mappingId = Convert.ToInt32(personalIdResult);


                // Step 2: Fetch the account_id (CreatedBy) using the user's email
                string getAccountIdQuery1 = "GetAccountIDByEmail";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", getAccountIdQuery1);
                var accountIdParams1 = new Dictionary<string, object>
 {
     { "@Email", personalInfo.Email }
 };

                //object accountIdResult = dbHandler.ExecuteScalar(getAccountIdQuery1, accountIdParams1, CommandType.StoredProcedure);

                if (accountIdResult == null)
                {
                    _logger.LogError("Account ID not found for the provided email.");
                    return BadRequest(new { Status = "Error", Status_Description = "Account ID not found for the provided email." });
                }

                int createdBy = Convert.ToInt32(accountIdResult);



                // Step 3: Process the image upload if Base64 image data is provided
                if (!string.IsNullOrEmpty(personalInfo.Base64Image))
                {
                    string insertImageQuery = "InsertLogo";
                    var imageParams = new Dictionary<string, object>
           {
               { "@MappingId", mappingId },
               { "@Mappingname", "Personal_info" },
               { "@Image", personalInfo.Base64Image },
               { "@CreatedBy", mappingId },
               { "@CreatedDate", DateTime.Now },
               { "@UpdatedBy", personalInfo.UpdatedBy },
               { "@UpdatedDate", DateTime.Now }
           };


                    dbHandler.ExecuteScalar(insertImageQuery, imageParams, CommandType.StoredProcedure);
                    _logger.LogInformation("Inserted Image for Personal Info ID: {PersonalInfoId}", result);

                }

                if (result != null)
                {
                    _logger.LogInformation($"Personal info inserted successfully with ID: {result}");
                    response = new
                    {
                        Status = "Success",
                        Status_Description = $"Personal info inserted with ID: {result}"
                    };
                }
                else
                {
                    _logger.LogError("Failed to insert Personal Info for Email: {Email}", personalInfo.Email);

                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Insertion failed."
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving the data: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = ex.Message
                };
            }

            return Ok(response);
        }

        [HttpPost]
        public IActionResult InsertWorkspaceInfo(WorkspaceInfoModel workspaceInfo, [FromServices] IDbHandler dbHandler)
        {
            int newWorkspaceId = 0;

            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred.",
                WorkspaceId = newWorkspaceId
            };

            try
            {
                // Step 1: Fetch personal_info_id
                string getPersonalInfoIdQuery = "GetPersonalIdByEmail";
                var personalInfoParams = new Dictionary<string, object> { { "@Email", workspaceInfo.Email } };
                object personalInfoIdResult = dbHandler.ExecuteScalar(getPersonalInfoIdQuery, personalInfoParams, CommandType.StoredProcedure);

                if (personalInfoIdResult == null)
                {
                    _logger.LogError("User not found for the provided email.");
                    return BadRequest(new { Status = "Error", Status_Description = "User not found for the provided email." });
                }

                int personalInfoId = Convert.ToInt32(personalInfoIdResult);

                // Step 2: Insert workspace info
                string insertQuery = "dbo.InsertUserWorkspaceInfo";
                var workspaceParams = new Dictionary<string, object>
        {
            { "@PersonalInfoId", personalInfoId },
            { "@WorkspaceName", workspaceInfo.WorkspaceName },
            { "@BillingCountry", workspaceInfo.BillingCountry },
            { "@WorkspaceIndustry", workspaceInfo.WorkspaceIndustry },
            { "@WorkspaceType", workspaceInfo.WorkspaceType },
            { "@Status", workspaceInfo.Status },
            { "@CreatedBy", personalInfoId },
            { "@CreatedDate", DateTime.Now },
            { "@UpdatedBy", personalInfoId },
            { "@UpdatedDate", DateTime.Now }
        };

                object insertWorkspaceResult = dbHandler.ExecuteScalar(insertQuery, workspaceParams, CommandType.StoredProcedure);
                if (insertWorkspaceResult == null || !int.TryParse(insertWorkspaceResult.ToString(), out newWorkspaceId))
                {
                    throw new Exception("Failed to insert workspace info or retrieve Workspace ID.");
                }

                // Step 3: Insert logo (optional)

                string insertLogoQuery = "InsertLogo";
                var logoParams = new Dictionary<string, object>
                    {
                        { "@MappingId", newWorkspaceId },
                        { "@Mappingname", "Workspace_info" },
                        { "@Image", workspaceInfo.Base64Image },
                        { "@CreatedBy", personalInfoId },
                        { "@CreatedDate", DateTime.Now },
                        { "@UpdatedBy", personalInfoId },
                        { "@UpdatedDate", DateTime.Now }
                    };

                object insertLogoResult = dbHandler.ExecuteScalar(insertLogoQuery, logoParams, CommandType.StoredProcedure);
                if (insertLogoResult == null || Convert.ToInt32(insertLogoResult) != 1)
                {
                    throw new Exception("Failed to insert logo.");
                }
                else
                {
                    _logger.LogInformation("InsertLogo query executed successfully.");
                }




                // Step 4: Insert notification settings
                string insertNotificationsQuery = "InsertNotificationSettings";
                var notificationParams = new Dictionary<string, object> { { "@EmailId", workspaceInfo.Email } };
                object insertNotificationResult = dbHandler.ExecuteScalar(insertNotificationsQuery, notificationParams, CommandType.StoredProcedure);

                if (insertNotificationResult == null || Convert.ToInt32(insertNotificationResult) != 1)
                {
                    throw new Exception("Failed to insert notification settings.");
                }
                else
                {
                    _logger.LogInformation("Notification settings inserted successfully.");
                }


                // Success
                response = new
                {
                    Status = "Success",
                    Status_Description = "Workspace info inserted successfully.",
                    WorkspaceId = newWorkspaceId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = ex.Message,
                    WorkspaceId = newWorkspaceId
                };
            }

            return Ok(response);
        }

        [AllowAnonymous]
        [HttpPost]
        public object GetUserAccountByEmail(TravelAd_Api.Models.ApiModel.OtpRequestModel getAccountByEmail, [FromServices] IDbHandler dbHandler)
        {
            DataTable dtmain = new DataTable();
            dtmain.Columns.Add("Status");
            dtmain.Columns.Add("Status_Description");
            dtmain.Columns.Add("User_Account_Id");

            try
            {
                string getAccountIDQuery = "dbo.GetAccountIDByEmail";
                var getAccountIDParams = new Dictionary<string, object>
        {
            { "@Email", getAccountByEmail.Email }
        };

                var userAccountId = dbHandler.ExecuteScalar(getAccountIDQuery, getAccountIDParams, CommandType.StoredProcedure);

                if (userAccountId != null)
                {
                    dtmain.Rows.Add("Success", "Email found. Returning user account ID.", userAccountId);
                    return DtToJSON(dtmain);
                }
                else
                {

                    dtmain.Rows.Add("Error", "Email does not exist.");
                }
            }
            catch (Exception ex)
            {
                dtmain.Rows.Add("Error", ex.Message);
            }

            return DtToJSON(dtmain);
        }

        [HttpGet]
        public object GetWorkspaceNameByEmail([FromServices] IDbHandler dbHandler, string EmailId)
        {
            
            try
            {
                string procedure = "GetWorkspaceNameByEmail";
                var parameters = new Dictionary<string, object>
        {
            { "@EmailId", EmailId }
        };
                DataTable workspaceNameById = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);
                if (workspaceNameById.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Workspace Name Not found"
                    });
                }

                var WorkspaceNameByIdData = workspaceNameById.AsEnumerable().Select(row => new
                {
                    workspace_name = row.Field<string>("workspace_name"),
                    workspace_type = row.Field<string>("workspace_type"),
                    workspace_id = row.Field<int>("workspace_info_id"),
                }).ToList();

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Workspace Name retrieved successfully",
                    WorkspaceName = WorkspaceNameByIdData[0]
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the workspace name by id: {ex.Message}"
                });
            }

        }




        //Send invitation module

        [HttpPost("send-invite")]
        public async Task<IActionResult> SendInviteAsync([FromBody] List<InviteRequestModel> requests, [FromServices] IDbHandler dbHandler)
        {
            foreach (var request in requests)
            {
                var payloadData = new InviteTokenPayload
                {
                    Email = request.Email,
                    Name = request.Name,
                    WorkspaceId = request.WorkspaceId,
                    RoleId = request.RoleId,
                };

                var token = GenerateInviteToken(payloadData);

                string inviteUrl = $"{_configuration["FrontendUrl"]}signup?token={token}";

                bool emailSent = await SendInviteEmail(request.Email, inviteUrl);

                if (!emailSent)
                {
                    return StatusCode(500, new { Status = "Error", Message = "Failed to send invite to one or more emails. Please try again later." });
                }
            }

            return Ok(new { Status = "Success", Message = "Invite links sent successfully." });
        }

        private string GenerateInviteToken(InviteTokenPayload payloadData)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
    {
        new Claim("Email", payloadData.Email),
        new Claim("Name", payloadData.Name),
        new Claim("WorkspaceId", payloadData.WorkspaceId.ToString()),
        new Claim("RoleId", payloadData.RoleId.ToString())
    };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                expires: DateTime.Now.AddMinutes(10),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task<bool> SendInviteEmail(string email, string inviteUrl)
        {
            try
            {
                // Log the beginning of the API call
                _logger.LogInformation("Initializing Resend API call for invite email to: {Email}", email);

                // Prepare the JSON payload
                var requestPayload = new
                {
                    from = "TravelAd@mytravelad.com",  // Update with your sender email as needed
                    to = new[] { email },
                    subject = "You're Invited!",
                    // The email body includes a clickable link to the invite URL
                    html = $"<p>You have been invited to join. Click the link to join: <a href='{inviteUrl}'>Join Now</a></p>"
                };

                // Convert payload to JSON
                string jsonPayload = JsonConvert.SerializeObject(requestPayload);

                // Create an HttpClient instance
                using (HttpClient client = new HttpClient())
                {
                    // Set up the authorization header with your Resend API key from configuration
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", _configuration["ResendApiKey"]);

                    // Prepare the HTTP content with proper encoding and content type
                    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    // Define the Resend API endpoint
                    string resendApiUrl = _configuration["ResendApiUrl"];

                    // Send the POST request to the Resend API
                    HttpResponseMessage response = await client.PostAsync(resendApiUrl, content);

                    // Check if the request was successful
                    if (response.IsSuccessStatusCode)
                    {
                        string successContent = await response.Content.ReadAsStringAsync();
                        _logger.LogInformation("Invitation email sent successfully to {Email}. Response: {Response}", email, successContent);
                        return true;
                    }
                    else
                    {
                        // Log any errors received from the API
                        string errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError("Failed to send invite email to {Email}. Resend API error: {Error}", email, errorContent);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log any exceptions that occur during the process
                _logger.LogError(ex, "Exception occurred while sending invite email to {Email}", email);
                return false;
            }
        }
        //        [HttpPost]
        //        public object ForgotPassword(TravelAd_Api.Models.ApiModel.ForgotPasswordRequest request, [FromServices] IDbHandler dbHandler, [FromServices] IMemoryCache memoryCache)
        //        {
        //            DataTable dtmain = new DataTable();
        //            dtmain.Columns.Add("Status");
        //            dtmain.Columns.Add("Status_Description");

        //            try
        //            {
        //                string checkEmailQuery = "dbo.userlogincheckemail";
        //                var checkEmailParams = new Dictionary<string, object>
        //{
        //    { "@Email", request.Email },
        //};

        //                object emailExists = dbHandler.ExecuteScalar(checkEmailQuery, checkEmailParams, CommandType.StoredProcedure);

        //                if (emailExists != null && Convert.ToInt32(emailExists) > 0)
        //                {
        //                    // Generate OTP
        //                    string otp = GenerateOtp();
        //                    int otpValue = int.Parse(otp);
        //                    string hashedOtp = OtpService.HashOtp(otpValue);
        //                    Console.WriteLine("Hashed OTP : " + hashedOtp);

        //                    // Store the hashed OTP in memory for 2 minutes
        //                    memoryCache.Set(request.Email, hashedOtp, TimeSpan.FromMinutes(2));

        //                    // Send OTP to the email
        //                    bool emailSent = SendOtpToEmail(request.Email, otp);

        //                    if (emailSent)
        //                    {
        //                        dtmain.Rows.Add("Success", "OTP sent to the email.");
        //                    }
        //                    else
        //                    {
        //                        dtmain.Rows.Add("Error", "OTP sending failed.");
        //                    }
        //                }
        //                else
        //                {
        //                    // If email does not exist, return an error status
        //                    dtmain.Rows.Add("Error", "Email does not exist.");
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                dtmain.Rows.Add("Error", ex.Message);
        //            }

        //            return DtToJSON(dtmain);
        //        }

        [AllowAnonymous]
        [HttpPost]
        public async Task<object> ForgotPasswordAsync(TravelAd_Api.Models.ApiModel.ForgotPasswordRequest request, [FromServices] IDbHandler dbHandler, [FromServices] IMemoryCache memoryCache)
        {
            DataTable dtmain = new DataTable();
            dtmain.Columns.Add("Status");
            dtmain.Columns.Add("Status_Description");

            try
            {
                string checkEmailQuery = "dbo.userlogincheckemail";
                var checkEmailParams = new Dictionary<string, object>
{
    { "@Email", request.Email },
};

                object emailExists = dbHandler.ExecuteScalar(checkEmailQuery, checkEmailParams, CommandType.StoredProcedure);

                if (emailExists != null && Convert.ToInt32(emailExists) > 0)
                {
                    // Generate OTP
                    string otp = GenerateOtp();
                    int otpValue = int.Parse(otp);
                    string hashedOtp = OtpService.HashOtp(otpValue);
                    Console.WriteLine("Hashed OTP : " + hashedOtp);

                    // Store the hashed OTP in memory for 2 minutes
                    memoryCache.Set(request.Email, hashedOtp, TimeSpan.FromMinutes(2));

                    // Send OTP to the email
                    bool emailSent = await SendOtpToEmailResend(request.Email, otp);

                    if (emailSent)
                    {
                        dtmain.Rows.Add("Success", "OTP sent to the email.");
                    }
                    else
                    {
                        dtmain.Rows.Add("Error", "OTP sending failed.");
                    }
                }
                else
                {
                    // If email does not exist, return an error status
                    dtmain.Rows.Add("Error", "Email does not exist.");
                }
            }
            catch (Exception ex)
            {
                dtmain.Rows.Add("Error", ex.Message);
            }

            return DtToJSON(dtmain);
        }


        [HttpPost]
        public object UserRegister(TravelAd_Api.Models.ApiModel.UserLogin ul, [FromServices] IDbHandler dbHandler, [FromServices] IMemoryCache memoryCache)
        {
            DataTable dtmain = new DataTable();
            dtmain.Columns.Add("Status");
            dtmain.Columns.Add("Status_Description");

            try
            {
                string checkEmailQuery = "dbo.CheckIfEmailExists";  // Adjust stored procedure name to check email and status
                _logger.LogInformation("Executing stored procedure: {StoredProcedureName}", checkEmailQuery);

                var checkEmailParams = new Dictionary<string, object>
{
    { "@Email", ul.Email },
};

                object emailExists = dbHandler.ExecuteScalar(checkEmailQuery, checkEmailParams, CommandType.StoredProcedure);
                string emailExistsMessage = emailExists?.ToString()?.Trim();

                if (emailExists != null)
                {
                    if (int.TryParse(emailExists.ToString(), out int emailExistsInt) && emailExistsInt > 0) // Email exists
                    {
                        string checkEmailStatusQuery = "dbo.CheckEmailStatus";  // New stored procedure to check email status
                        var statusParams = new Dictionary<string, object>
        {
            { "@Email", ul.Email },
        };

                        object emailStatus = dbHandler.ExecuteScalar(checkEmailStatusQuery, statusParams, CommandType.StoredProcedure);

                        if (emailStatus != null && emailStatus.ToString() == "inactive")  // Email is inactive
                        {
                            _logger.LogInformation("Email is inactive, proceeding with registration");

                            // Proceed with the registration, for example, re-inserting or updating status
                            string query = "dbo.InsertUserAccountInfo";
                            _logger.LogInformation("Stored procedure parameters: {Parameters}", query);

                            var parameters = new Dictionary<string, object>
            {
                { "@Email", ul.Email },
                { "@password", ul.Password },
            };

                            _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);
                            object result = dbHandler.ExecuteScalar(query, parameters, CommandType.StoredProcedure);

                            if (result != null)
                            {
                                _logger.LogInformation("Success", "User inserted with ID: " + result.ToString());
                                dtmain.Rows.Add("Success", "User inserted with ID: " + result.ToString());
                            }
                            else
                            {
                                _logger.LogWarning("Error", "Insertion failed.");
                                dtmain.Rows.Add("Error", "Insertion failed.");
                            }
                        }
                        else
                        {
                            dtmain.Rows.Add("Error", "Email is active. Please use a different email.");
                            _logger.LogInformation("Error", "Email is active.");
                        }
                    }
                    else if (emailExistsMessage == "Email already exists")
                    {
                        dtmain.Rows.Add("Error", "Email already exists. Please use a different email.");
                        _logger.LogInformation("Error", "Email already exists. Please use a different email.");
                        return DtToJSON(dtmain); // Return and stop further processing
                    }
                    else  // Email does not exist
                    {
                        _logger.LogInformation("Email does not exist, proceeding with registration");

                        string query = "dbo.InsertUserAccountInfo";
                        _logger.LogInformation("Stored procedure parameters: {Parameters}", query);

                        var parameters = new Dictionary<string, object>
        {
            { "@Email", ul.Email },
            { "@password", ul.Password },
            { "@status", "active" }
        };

                        _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);
                        object result = dbHandler.ExecuteScalar(query, parameters, CommandType.StoredProcedure);

                        if (result != null)
                        {
                            _logger.LogInformation("Success", "User inserted with ID: " + result.ToString());
                            dtmain.Rows.Add("Success", "User inserted with ID: " + result.ToString());
                        }
                        else
                        {
                            _logger.LogWarning("Error", "Insertion failed.");
                            dtmain.Rows.Add("Error", "Insertion failed.");
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Error", "Email does not exist.");
                    dtmain.Rows.Add("Error", "Email does not exist.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error", ex.Message);
                dtmain.Rows.Add("Error", ex.Message);
            }

            return DtToJSON(dtmain);
        }

        [HttpPost]
        public object UpdatePassword(TravelAd_Api.Models.ApiModel.UpdatePasswordRequest request, [FromServices] IDbHandler dbHandler)
        {
            DataTable dtmain = new DataTable();
            dtmain.Columns.Add("Status");
            dtmain.Columns.Add("Status_Description");

            try
            {
                string updatePasswordQuery = "dbo.UpdateUserPassword";
                var updatePasswordParams = new Dictionary<string, object>
{
    { "@Email", request.Email },
    { "@Password", request.NewPassword }
};

                // Execute the stored procedure
                dbHandler.ExecuteNonQuery(updatePasswordQuery, updatePasswordParams, CommandType.StoredProcedure);

                // Add success message to the response
                dtmain.Rows.Add("Success", "Password updated successfully.");
            }
            catch (Exception ex)
            {
                // If an error occurs, return the error message
                dtmain.Rows.Add("Error", ex.Message);
            }

            return DtToJSON(dtmain);
        }





        [HttpGet]
        public IActionResult GetEmailDomains([FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Stored procedure name
                string procedure = "GetEmailDomainNames";

                // Execute the stored procedure and retrieve the result into a DataTable
                DataTable domainNames = dbHandler.ExecuteDataTable(procedure);

                // Check if the result set is empty
                if (domainNames.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No data in the table"
                    });
                }

                // Map the DataTable rows to a list of domain names
                var domainList = domainNames.AsEnumerable()
                                            .Select(row => row.Field<string>("domain_name"))
                                            .ToList();

                // Return the list of domain names
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Domain names retrieved successfully",
                    Domains = domainList
                });
            }
            catch (Exception ex)
            {
                // Handle errors and return a server error response
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving domain names: {ex.Message}"
                });
            }
        }

        [HttpGet]
        public IActionResult GetWorkspaceTypesList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Define the stored procedure name
                string getWorkspceTypesList = "GetWorkspaceTypesList";

                // Execute the stored procedure and retrieve the country list as a DataTable
                DataTable workspacetypes = dbHandler.ExecuteDataTable(getWorkspceTypesList);

                // Check if the result is null or empty
                if (workspacetypes == null || workspacetypes.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No countries found"
                    });
                }

                // Convert the DataTable to a List of objects with country_id and country_name
                var workspaceTypesListData = workspacetypes.AsEnumerable().Select(row => new
                {
                    workspace_id = row.Field<int>("workspace_id"),
                    workspace_name = row.Field<string>("workspace_name")
                }).ToList();

                // Return the country list with success status
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Workspace Types Data retrieved successfully",
                    WorkspaceTypes = workspaceTypesListData
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during database interaction
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the workspacetypes list: {ex.Message}"
                });
            }
        }



        [HttpPost]
        public IActionResult InsertUserWorkspaceRole([FromServices] IDbHandler dbHandler, [FromBody] InsertUserWorkspaceRole request)
        {
            try
            {
                string procedure = "InsertUserWorkspaceRole";

                // Validate the mode
                if (string.IsNullOrEmpty(request.Mode) ||
                    (request.Mode != "InsertAdmin" && request.Mode != "InsertFromInvite" && request.Mode != "InsertFromDialogDropdown" && request.Mode != "InsertOperator"))
                {
                    return BadRequest(new
                    {
                        Status = "Failure",
                        Status_Description = "Invalid mode specified. Use 'InsertAdmin' or 'InsertFromInvite' or 'InsertFromDialogDropdown' or 'InsertOperator' "
                    });
                }

                // Prepare parameters based on the mode
                var parameters = new Dictionary<string, object>
                   {
                       { "@Mode", request.Mode },
                       { "@EmailId", request.EmailId }
                   };



                if (request.Mode == "InsertFromInvite")
                {
                    // Validate required parameters for InsertFromInvite mode
                    if (request.WorkspaceId == null || request.RoleId == null)
                    {
                        return BadRequest(new
                        {
                            Status = "Failure",
                            Status_Description = "WorkspaceId and RoleId are required for 'InsertFromInvite' mode."
                        });
                    }

                    parameters.Add("@WorkspaceId", request.WorkspaceId);
                    parameters.Add("@RoleId", request.RoleId);
                }


                if (request.Mode == "InsertFromDialogDropdown")
                {
                    if (request.WorkspaceId == null)
                    {
                        return BadRequest(new
                        {
                            Status = "Failure",
                            Status_Description = "WorkspaceId is required for 'InsertFromDialogDropdown' mode."
                        });
                    }

                    parameters.Add("@WorkspaceId", request.WorkspaceId);
                }

                // Execute the stored procedure
                int rowsAffected = dbHandler.ExecuteNonQuery(procedure, parameters, CommandType.StoredProcedure);

                if (rowsAffected == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Failed to insert user workspace role."
                    });
                }



                // Fetch the WorkspaceId after insertion
                string workspaceIdQuery = string.Empty;

                if (request.Mode == "InsertAdmin")
                {
                    workspaceIdQuery = @"
                                       SELECT wi.workspace_info_id
                                       FROM ta_user_workspace_info wi
                                       JOIN ta_user_personal_info pi ON wi.personal_info_id = pi.user_personal_id
                                       WHERE pi.email = @EmailId";
                }
                else if (request.Mode == "InsertFromInvite")
                {
                    workspaceIdQuery = @"SELECT @WorkspaceId AS workspace_info_id";
                }



                var workspaceIdParams = new Dictionary<string, object>
                   {
                       { "@EmailId", request.EmailId }
                   };



                if (request.Mode == "InsertFromInvite")
                {
                    workspaceIdParams.Add("@WorkspaceId", request.WorkspaceId);
                }

                //// ExecuteScalar to fetch WorkspaceId
                //var result = dbHandler.ExecuteScalar(workspaceIdQuery, workspaceIdParams);

                //// Validate the result and ensure it is not null
                //if (result == null)
                //{
                //    return Ok(new
                //    {
                //        Status = "Failure",
                //        Status_Description = "WorkspaceId could not be fetched."
                //    });
                //}

                //var workspaceId = Convert.ToInt32(result); // Safely convert result to int (modify if it's a string)

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "User workspace role inserted successfully.",
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred: {ex.Message}"
                });
            }
        }


        [HttpPut]
        public object UpdateIsAccepted(TravelAd_Api.Models.ApiModel.isAccepted accepted, [FromServices] IDbHandler dbHandler)
        {
            var response = new
            {
                Status = "Error",
                Status_Description = "An error occurred."
            };

            try
            {
                // Log method entry
                _logger.LogInformation("Entering UpdateIsAccepted API");

                // Define stored procedure name
                string updateQuery = "UpdateIsAccepted";

                // Prepare parameters
                var parameters = new Dictionary<string, object>
        {
            {"Email", accepted.Email },
            {"WorkspaceId", accepted.WorkdspaceId }
        };

                // Execute stored procedure
                int result=(int)dbHandler.ExecuteNonQuery(updateQuery, parameters, CommandType.StoredProcedure);

             

                // Evaluate result
                if (result == 1)
                {
                    _logger.LogInformation("Pending invite was updated successfully.");
                    response = new
                    {
                        Status = "Success",
                        Status_Description = "Pending invite was updated successfully."
                    };
                }
                else if (result == -1)
                {
                    _logger.LogWarning("Pending invites update failed. No matching records found.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Pending invites update failed. No matching records found."
                    };
                }
                else
                {
                    _logger.LogError("Unexpected return value from stored procedure.");
                    response = new
                    {
                        Status = "Error",
                        Status_Description = "Unexpected error occurred during processing."
                    };
                }
            }
            catch (Exception ex)
            {
                // Log exceptions
                _logger.LogError($"Exception: {ex.Message}");
                response = new
                {
                    Status = "Error",
                    Status_Description = $"An exception occurred: {ex.Message}"
                };
            }

            _logger.LogInformation("Exiting UpdateIsAccepted API");
            return response;
        }

        [HttpGet]
        public IActionResult GetCountryList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Define the stored procedure name
                string getCountryList = "GetCountryList";

                // Execute the stored procedure and retrieve the country list as a DataTable
                DataTable countryList = dbHandler.ExecuteDataTable(getCountryList);

                // Check if the result is null or empty
                if (countryList == null || countryList.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No countries found"
                    });
                }

                // Convert the DataTable to a List of objects with country_id and country_name
                var countryListData = countryList.AsEnumerable().Select(row => new
                {
                    country_id = row.Field<int>("country_id"),
                    country_name = row.Field<string>("country_name")
                }).ToList();

                // Return the country list with success status
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Countries retrieved successfully",
                    CountryList = countryListData
                });
            }
            catch (Exception ex)
            {
                // Handle any exceptions that occur during database interaction
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the country list: {ex.Message}"
                });
            }
        }



        [HttpGet]
        public IActionResult GetIndustryList([FromServices] IDbHandler dbHandler)
        {
            try
            {
                // Define the stored procedure name
                string getIndustryList = "GetIndustryList";

                // Execute the stored procedure and retrieve the country list as a DataTable
                DataTable industryList = dbHandler.ExecuteDataTable(getIndustryList);

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
                    Status_Description = "Countries retrieved successfully",
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

        [HttpGet]
        public IActionResult CheckIfAdmin([FromServices] IDbHandler dbHandler, string EmailId)
        {
            try
            {
                // Define the stored procedure name
                string procedure = "CheckIfAdmin";

                var parameters = new Dictionary<string, object>
        {
            { "@EmailId", EmailId }
        };

                // Execute the stored procedure and retrieve the result as a DataTable
                DataTable checkAdminTable = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);

                // Check if the result is null or empty
                if (checkAdminTable == null || checkAdminTable.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Admin not found"
                    });
                }

                // Get the result value from the DataTable
                var result = checkAdminTable.AsEnumerable()
                    .Select(row => row.Field<int>("result"))
                    .FirstOrDefault();

                // Return true or false based on the result value
                if (result == 0)
                {
                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = "User is not admin",
                        IsAdmin = false
                    });
                }
                else
                {
                    return Ok(new
                    {
                        Status = "Success",
                        Status_Description = "Admin found",
                        IsAdmin = true
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = ex.Message
                });
            }
        }



        [HttpGet]
        public object GetWorkspaceNameById([FromServices] IDbHandler dbHandler, int workspace_id)
        {

            try
            {
                string procedure = "GetWorkspaceNamebyID";
                var parameters = new Dictionary<string, object>
 {
     { "@workspace_id", workspace_id }
 };
                DataTable workspaceNameById = dbHandler.ExecuteDataTable(procedure, parameters, CommandType.StoredProcedure);
                if (workspaceNameById.Rows.Count == 0)
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Workspace Name Not found"
                    });
                }

                string workspaceName = workspaceNameById.Rows[0].Field<string>("workspace_name");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Workspace Name retrieved successfully",
                    WorkspaceName = workspaceName
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving the workspace name by id: {ex.Message}"
                });
            }

        }


        private Users AuthenticateUser(Users user)
        {
            Users _user = null;
            if (user.Username == "admin" && user.Password == "1234")
            {
                _user = new Users { Username = "Maha" };
            }
            return _user;

        }

        private string GenerateToken(Users user)
        {
            var securitykey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]));
            var credentials = new SigningCredentials(securitykey, SecurityAlgorithms.HmacSha256);


            var token = new JwtSecurityToken(_configuration["Jwt:Issuer"], _configuration["Jwt:Audience"], null,

                expires: DateTime.Now.AddMinutes(1),
                signingCredentials: credentials
                );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [AllowAnonymous]
        [HttpPost]

        public IActionResult Login(Users user)
        {

            IActionResult response = Unauthorized();
            var user_ = AuthenticateUser(user);

            if (user_ != null)
            {

                var token = GenerateToken(user_);
                response = Ok(new { token = token });
            }
            return response;
        }


        [HttpGet]
        public IActionResult GetPermissionsByRoleId([FromServices] IDbHandler dbHandler, [FromQuery] int RoleID)
        {
            try
            {
                string storedProcedureName = "GetPermissionsByRoleID";

                _logger.LogInformation("Executing stored procedure: {storedProcedureName}", storedProcedureName);

                var parameters = new Dictionary<string, object>
        {
            { "@RoleID", RoleID }
        };

                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                DataTable roleTable = dbHandler.ExecuteDataTable(storedProcedureName, parameters, CommandType.StoredProcedure);

                if (roleTable == null || roleTable.Rows.Count == 0)
                {
                    _logger.LogWarning("No role found for the specified RoleID");
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No role found for the specified RoleID"
                    });
                }

                var roleDetails = roleTable.AsEnumerable().Select(row => new
                {
                    RoleID = row.Field<int>("role_id"),
                    RoleName = row.Field<string>("role_name"),
                    Permissions = row.Field<string>("permissions") // JSON string
                }).FirstOrDefault();

                _logger.LogInformation("Role details retrieved successfully: {RoleDetails}", roleDetails);

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Role details retrieved successfully",
                    RoleDetails = roleDetails
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving role details: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving role details: {ex.Message}"
                });
            }
        }

        [HttpGet("GetUserWorkspaceRoleDetails")]
        public IActionResult GetUserWorkspaceRoleDetails([FromServices] IDbHandler dbHandler, [FromQuery] int accountId, [FromQuery] int workspaceId)
        {
            try
            {
                // Define the stored procedure name
                string storedProcedureName = "GetUserRoleDetailsByAccIDandWorkspaceID";
                _logger.LogInformation("Executing stored procedure: {storedProcedureName}", storedProcedureName);

                // Set up the parameters
                var parameters = new Dictionary<string, object>
        {
            { "@AccountId", accountId },
            { "@WorkspaceId", workspaceId }
        };
                _logger.LogInformation("Stored procedure parameters: {Parameters}", parameters);

                // Execute the stored procedure and retrieve the result
                DataTable roleDetailsTable = dbHandler.ExecuteDataTable(storedProcedureName, parameters, CommandType.StoredProcedure);

                // Check if the result is null or empty
                if (roleDetailsTable == null || roleDetailsTable.Rows.Count == 0)
                {
                    _logger.LogWarning("No role details found for AccountID: {AccountId} and WorkspaceID: {WorkspaceId}", accountId, workspaceId);
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "No user role details found"
                    });
                }

                // Convert the DataTable to a List of user role details
                var roleDetails = roleDetailsTable.AsEnumerable().Select(row => new
                {
                    Id = row.Field<int>("id"),
                    UserAccountId = row.Field<int>("user_account_id"),
                    WorkspaceInfoId = row.Field<int>("workspace_info_id"),
                    RoleId = row.Field<int>("role_id"),
                    Status = row.Field<string>("status")
                }).ToList();

                _logger.LogInformation("User workspace role details retrieved successfully: {RoleDetails}", roleDetails);

                // Return the user workspace role details with success status
                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "User role details retrieved successfully",
                    UserWorkspaceRoleDetails = roleDetails
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while retrieving user workspace role details: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while retrieving user workspace role details: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public IActionResult InsertWorkspaceBillingStatus([FromServices] IDbHandler dbHandler, int workspaceid)
        {
            try
            {
                string insertBillingStatus = "InsertWorkspaceBillingStatus";
                _logger.LogInformation("Executing stored procedure: {ProcedureName}", insertBillingStatus);

                var parameters = new Dictionary<string, object>
        {
            { "@workspace_id", workspaceid }
        };

                var result = dbHandler.ExecuteNonQuery(insertBillingStatus, parameters, CommandType.StoredProcedure);

                _logger.LogInformation("Workspace billing record inserted successfully.");

                return Ok(new
                {
                    Status = "Success",
                    Status_Description = "Workspace billing record inserted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while inserting billing status: {ex.Message}");
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = "An error occurred while inserting the billing status.",
                    ErrorDetails = ex.Message
                });
            }
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult CheckInviteExpirationByEmail([FromServices] IDbHandler dbHandler, string email)
        {
            try
            {
                // Name of the stored procedure
                string storedProcedure = "CheckInviteExpirationByEmail";
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
                    if (spMessage == "Success")
                    {
                        // Return the SP message in the API response
                        return Ok(new
                        {
                            Status = "Success",
                            Status_Description = spMessage
                        });
                    }
                    else
                    {
                        // If no message is returned, send a generic success response
                        return Ok(new
                        {
                            Status = "Invite Expired",
                            Status_Description = "Invite Expired or Access Removed from the Owner"
                        });
                    }

                }
                else
                {
                    return Ok(new
                    {
                        Status = "Failure",
                        Status_Description = "Something went wrong."
                    });

                }

            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to fetch the account. Error: {ex.Message}");

                // Handle any errors
                return StatusCode(500, new
                {
                    Status = "Error",
                    Status_Description = $"An error occurred while fetching the account: {ex.Message}"
                });
            }
        }

    }
}

