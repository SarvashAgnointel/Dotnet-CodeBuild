namespace TravelAd_Api.Models
{
    public class ApiModel
    {
        public class UserRegister
        {
            public int AccountId { get; set; }
            public string Email { get; set; }
            public string PhoneNumber { get; set; }
            public string EmailVerified { get; set; }
            public string PhoneVerified { get; set; }
            public string Password { get; set; }

            public string Status { get; set; }


        }

        public class UserLogin
        {
            public int AccountId { get; set; }
            public string Email { get; set; }
            public string PhoneNumber { get; set; }
            public string EmailVerified { get; set; }
            public string PhoneVerified { get; set; }
            public string Password { get; set; }


        }
        public class UserVerify
        {
            public string Email { get; set; }

        }

        public class OtpVerificationModel
{
    public string Email { get; set; }
    public string Otp { get; set; }
}

        public class UserPersonalInfoModel
        {
            // Personal Information fields
            public int AccountId { get; set; }
            public string FirstName { get; set; }
            public string Email { get; set; }
            public string LastName { get; set; }
            public string EmailSubscription { get; set; }
            public int? AlternateNumber { get; set; }
            public string City { get; set; }
            public int? Country { get; set; }
            public string Address { get; set; }
            public int? LanguagePreference { get; set; }
            public string Gender { get; set; }
            public DateTime? DateOfBirth { get; set; }
            public string Status { get; set; }
            public int? CreatedBy { get; set; }
            public DateTime? CreatedDate { get; set; }
            public int? UpdatedBy { get; set; }
            public DateTime? UpdatedDate { get; set; }

            // Image Upload fields
            public int MappingId { get; set; }
            public string Base64Image { get; set; }

        }

        public class ImageUploadModel
        {
            public int mapping_id { get; set; }
            public string Email { get; set; }
            public string Base64Image { get; set; } 
            public int CreatedBy { get; set; }
        }


        public class WorkspaceInfoModel
        {
            public string Email { get; set; } 
            public string WorkspaceName { get; set; } 
            public string BillingCountry { get; set; } 
            public string WorkspaceIndustry { get; set; } 
            public string WorkspaceType { get; set; } 
            public string Status { get; set; } 
            public int CreatedBy { get; set; } 
            public DateTime CreatedDate { get; set; } 
            public int UpdatedBy { get; set; } 
            public DateTime UpdatedDate { get; set; }
            // Image Upload fields
            public int MappingId { get; set; }
            public string Base64Image { get; set; }
        }

        public class isAccepted
        {
            public string Email { get; set; }

            public int WorkdspaceId { get; set; }
        }

        public class EmailSubscriptionModel
        {
            public string Email { get; set; }
            public string EmailVerified { get; set; }  
        }

        public class OtpRequestModel
        {
            public string Email { get; set; }
        }

        public class InviteRequestModel
        {
            public string Email { get; set; }
            public string Name { get; set; }

            public int WorkspaceId { get; set; }

            public int RoleId { get; set; }
        }

        public class InviteTokenPayload
        {
            public string Email { get; set; }
            public string Name { get; set; }

            public int WorkspaceId { get; set; }

            public int RoleId { get; set; }
        }

        public class ForgotPasswordRequest
        {
            public string Email { get; set; }
        }


        public class UpdatePasswordRequest
        {
            public string Email { get; set; }
            public string NewPassword { get; set; }
        }

        public class InsertUserWorkspaceRole
        {
            public string Mode { get; set; }
            public string EmailId { get; set; }
            public int? WorkspaceId { get; set; }

            public int? RoleId { get; set; }


        }

        public class Users
        {
            public string Username { get; set; }

            public string Password { get; set; }

        }

    }
}



