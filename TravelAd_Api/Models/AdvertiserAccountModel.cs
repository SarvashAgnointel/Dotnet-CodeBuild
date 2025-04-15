using Newtonsoft.Json;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace TravelAd_Api.Models
{
    public class AdvertiserAccountModel
    {

        public class CreateCampaign
        {
            public int CampaignId { get; set; }

            public string CampaignName { get; set; }

            public string CampaignBudget { get; set; }

            public int ChannelType { get; set; }

            public string TargetCountry { get; set; }
            public string RoamingCountry { get; set; }
            public DateTime? StartDateTime { get; set; }
            public DateTime? EndDateTime { get; set; }
            public string Status { get; set; }
            public string TemplateName { get; set; }
            public int CreatedBy { get; set; }
            public DateTime? CreatedDate { get; set; }
            public int UpdatedBy { get; set; }
            public DateTime? UpdatedDate { get; set; }
            public int WorkspaceId { get; set; }
            public int? ListId { get; set; }
            public int? DeviceId { get; set; }

            public string Delivered { get; set; }
            public string ReadCampaign { get; set; }
            public string CTR { get; set; }
            public string DeliveryRate { get; set; }
            public string ButtonClick { get; set; }

            public int? Age { get; set; }
            public int? Gender { get; set; }
            public int? IncomeLevel { get; set; }
            public int? Location { get; set; }
            public int? Interests { get; set; }
            public int? Behaviours { get; set; }
            public int? OSDevice { get; set; }

            public string FCampaignBudget { get; set; }
            public DateTime? FStartDateTime { get; set; }
            public DateTime? FEndDateTime { get; set; }
            public string IsAdminApproved { get; set; }
            public string IsOperatorApproved { get; set; }
            public string BudgetAndSchedule { get; set; }
            public string MessageFrequency { get; set; }
            public string SequentialDelivery { get; set; }
            public string PreventDuplicateMessages { get; set; }
            public int? DailyRecipientLimit { get; set; }
            public DateTime? DeliveryStartTime { get; set; }
            public DateTime? DeliveryEndTime { get; set; }
            public string SmsNumber { get; set; }
            public int SMPP_id { get; set; }
            public int SMPP_ConnectionID { get; set; }

            public decimal campaign_existing_budget { get; set; }

            public string currency_symbol { get; set; }
            public string currency_name { get; set; }

            public string country_name { get; set; }
        }


        public class CampaignStatusUpdate
        {
            public int CampaignId { get; set; }
            public string Status { get; set; }
            public int WorkspaceId { get; set; }
            public string CampaignName { get; set; }
            public DateTime update_date { get; set; }
        }

        public class CampaignStatusUpdateRequest
        {
            public int CampaignId { get; set; }
            public string Status { get; set; }
        }

        public class TemplateDetailsModel
        {
            public int PlatformName { get; set; }
            public string TemplateName { get; set; }
            public int TemplateLanguage { get; set; }
            public string TemplateHeader { get; set; }
            public string TemplateBody { get; set; }
            public string TemplateFooter { get; set; }
            public string Components { get; set; }
            public string ButtonType { get; set; }
            public string ButtonText { get; set; }
            public int CreatedBy { get; set; }

            public DateTime? CreatedDate { get; set; }

            public int UpdatedBy { get; set; }

            public DateTime? UpdatedDate { get; set; }

            public string Status { get; set; }

            public string URLType { get; set; }
            public string WebsiteURL { get; set; }

            public int workspace_id { get; set; }

        }


        public class MetaTemplateDetails
        {
            public Data2 data2 { get; set; }
            public string? mediaBase64 { get; set; }
        }

        public class Data2
        {
            public string name { get; set; }
            public string category { get; set; }
            public bool allow_category_change { get; set; }
            public string language { get; set; }
            public List<Component> components { get; set; } = new List<Component>(); // Corrected type
        }

        public class Component
        {
            public string type { get; set; }
            public string? format { get; set; } // Only for HEADER
            public string? text { get; set; } // For BODY & FOOTER
            public Example? example { get; set; } // For HEADER (image, video, document) & BUTTONS
            public List<Button>? buttons { get; set; } // For BUTTONS component
        }

        public class Example
        {
            public List<string>? header_handle { get; set; } // For media header
            public List<string>? body_text { get; set; } // For example text in BODY
        }

        public class Button
        {
            public string type { get; set; } // URL, PHONE_NUMBER, COPY_CODE
            public string text { get; set; } // Only for URL & PHONE_NUMBER
            public string? url { get; set; } // Only for URL
            public string? phone_number { get; set; } // Only for PHONE_NUMBER
            public string? example { get; set; } // Only for COPY_CODE
        }

        public class SMSTemplateDetails
        {
            public SMSData2 data2 { get; set; }

            public string? mediaBase64 { get; set; }
        }

        public class SMSComponent
        {
            public string type { get; set; } // This represents "BODY"
            public string text { get; set; } // This represents the body text
        }

        public class SMSData2
        {
            public string name { get; set; }
            public string category { get; set; }
            public bool allow_category_change { get; set; }
            public string language { get; set; }
            public List<SMSComponent> components { get; set; }
        }





        public class GetFile
        {
            public string mediaBase64 { get; set; }

        }


        public class UploadMetaMedia
        {
            public string file_name { get; set; }

            public string file_length { get; set; }

            public string file_type { get; set; }

            public string workspace_id { get; set; }


        }

        public class GetMultipleWorkspaceByEmail
        {
            public string Email { get; set; }

        }
        public class NotificationUpdateModel
        {
            public string EmailId { get; set; } // The email used to identify the user
            public string NotificationData { get; set; } // The new notification data in JSON format
        }




        public class GetUserNameByID
        {
            public int UserAccountId { get; set; }
        }
        public class GetPersonalinfoByEmail
        {
            public string UserEmail { get; set; }
        }

        public class GetPersonalSettingsDetailsByEmail
        {
            public string UserEmail { get; set; }
        }

        public class GetWorkspaceDetailsRequest
        {
            public string Email { get; set; }
        }

        public class GetWorkspaceDetailsRequestByID
        {
            public int WorkspaceId { get; set; }
        }

        public class GetRoleRequest
        {
            public string Email { get; set; }
            public int WorkspaceInfoId { get; set; }
        }

        public class UserProfileUpdateModel
        {
            public string UserEmail { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
        }

        public class UserEmailUpdateModel
        {
            public string ExistingEmail { get; set; }
            public string NewEmail { get; set; }    // The new email to be updated
        }


        //public class WorkspaceUpdateModel
        //{
        //    public string UserEmail { get; set; }
        //    public string NewWorkspaceName { get; set; }
        //}
        public class WorkspaceAddressUpdateModel
        {
            public int workspaceid { get; set; }     // User account ID to identify the user
            public string StreetName { get; set; }      // Street name
            public string StreetNumber { get; set; }       // Changed to int: Street number
            public string City { get; set; }            // City
            public string PostalCode { get; set; }         // Changed to int: Postal code
            public string State { get; set; }           // State
            public string BillingCountry { get; set; }  // Country (dropdown selection)
        }


        public class WorkspaceIndustryModel
        {
            public int workspaceid { get; set; }       // Input: ID from ta_user_account_info
            public string WorkspaceIndustry { get; set; }   // Input: New workspace industry
        }

        public class PendingInvitedMembers
        {

            public int WorkspaceId { get; set; }     // Foreign Key
            public string Email { get; set; }        // Email Address
            public int Role { get; set; }            // Role (Foreign Key)
            public DateTime? InvitedAt { get; set; } // Date of Invitation (nullable)
            public string Status { get; set; }       // Member Status
            public string IsAccepted { get; set; }   // Acceptance Status
            public string InvitedBy { get; set; }       // Inviter's ID
        }

        public class OAuthCallbackRequest
        {
            public string WabaId { get; set; }
            public string PhoneId { get; set; }
            public string EmailId { get; set; }            
            public int workspaceId { get; set; }
        }

        public class UpdateWabaNPhoneId
        {
            public int Id { get; set; }
            public string WabaId { get; set; }
            public string PhoneId { get; set; }
        }
            public class GetWhatsappAccountDetails
            {
                public string EmailId { get; set; }
            }
            public class WhatsappAccountDetails
            {
                public string WabaId { get; set; }
                public string PhoneId { get; set; }
                public string AccessToken { get; set; }
            }

            public class AdvancedServerDetails
            {
                public int serverId { get; set; }
            }

            public class PhoneNumber
            {
                public string id { get; set; }
                public string display_phone_number { get; set; }
                public string verified_name { get; set; }
                public string status { get; set; }
                public string quality_rating { get; set; }
                public string search_visibility { get; set; }
                public string platform_type { get; set; }
                public string code_verification_status { get; set; }
            }

            public class PhoneNumberResponse
            {
                public List<PhoneNumber> Data { get; set; }
            }

            public class OwnerBusinessInfo
            {
                [JsonProperty("name")]
                public string Name { get; set; }

                [JsonProperty("id")]
                public string Id { get; set; }
            }

            public class WhatsappOwnerDetailsResponse
            {
                [JsonProperty("id")]
                public string Id { get; set; }

                [JsonProperty("name")]
                public string Name { get; set; }

                [JsonProperty("owner_business_info")]
                public OwnerBusinessInfo OwnerBusinessInfo { get; set; }
            }

        public class ProductRequestModel
        {
            public string ProductName { get; set; }
            public string Description { get; set; }
            public decimal Amount { get; set; }
            public string Currency { get; set; } // e.g., "usd", "eur"
        }
        public class Payment
        {
            public int Id { get; set; }
            public string UserId { get; set; }
            public string PaymentId { get; set; }
            public decimal Amount { get; set; }
            public string Status { get; set; }
            public DateTime PaymentDate { get; set; }
        }
        public class PaymentLinkRequest
        {
            public string PriceId { get; set; } // Stripe Price ID
            public int Quantity { get; set; }   // Quantity
            //public int Amount { get; set; }
            //public string Currency { get; set; } // e.g., "usd", "eur"

            public string productname { get; set; }

            public int id { get; set; }


        }

        public class UpsertCampaignColumnsRequest
        {
            [JsonPropertyName("WorkspaceId")]
            public int WorkspaceId { get; set; }

            [JsonPropertyName("Columns")]
            public string Columns { get; set; }
        }

        public class WorkspaceUpdateModel
        {
            public int WorkspaceId { get; set; }
            public string NewWorkspaceName { get; set; }
        }

        public class BillingFeatureUpdateRequest
        {
            public int BillingId { get; set; }
            public string Messages { get; set; }
            public string Price { get; set; }
            public string CountrySymbol { get; set; }
            public string Permessage { get; set; }

            public string Name { get; set; }
            public DateTime UpdatedAt { get; set; }
        }


        public class UpdateLogoPersonalIdModel
        {
            public int MappingId { get; set; }
            public string Image { get; set; }
            public int CreatedBy { get; set; }
            public DateTime CreatedDate { get; set; }
            public int UpdatedBy { get; set; }
            public DateTime UpdatedDate { get; set; }
        }

        public class UpdateLogoWorkspaceIdModel
        {
            public int MappingId { get; set; }
            public string Image { get; set; }
            public int CreatedBy { get; set; }
            public DateTime CreatedDate { get; set; }
            public int UpdatedBy { get; set; }
            public DateTime UpdatedDate { get; set; }
        }

        public class InsertSMSPhoneNumber
        {
            public string PhoneName { get; set; }
                public string PhoneNumber { get; set; }
            public int WorkspaceId { get; set; }

        }
        public class UpdateSMSPhoneNumber
        {
            public string PhoneName { get; set; }
            public string PhoneNumber { get; set; }
            public int Id { get; set; }

        }

        public class StatusToggleRequest
        {
            public int BillingId { get; set; }
            public string Status { get; set; }
        }

        public class UpdateAdminCampaignColumnsRequest
        {
            [JsonPropertyName("EmailId")]
            public string EmailId { get; set; }

            [JsonPropertyName("Columns")]
            public string Columns { get; set; }
        }

        public class GetOperatorsByCountryRequest
        {
            public List<int> CountryIds { get; set; }  //  (country ID, [1, 2, 3])
            public List<string> ToCountryNames { get; set; } //Country Names (e.g., ["India", "USA"])

            public int CampaignId { get; set; }
        }

        public class AudienceContact
        {
            public int ListId { get; set; }
            public string FirstName { get; set; }
            public string LastName { get; set; }
            public string PhoneNo { get; set; }
            public string Location { get; set; }
            public string FileName { get; set; }
            public int ContactId { get; set; }
        }

        public class AdvertiserNotification
        {
            public int Id { get; set; }
            public int WorkspaceId { get; set; }
            public int? CampaignId { get; set; }
            public string CampaignName { get; set; }
            public string StatusMark { get; set; }
            public string NotificationType { get; set; }
            public Dictionary<string, object> NotificationData { get; set; }
            public DateTime CreatedAt { get; set; }
        }

    }
    }

