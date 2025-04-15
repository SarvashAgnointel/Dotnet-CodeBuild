using System.Security.Cryptography;
using System.Text;

namespace TravelAd_Api.DataLogic
{
    public class OtpService
    {
        // Hashes the OTP using SHA-256
        public static string HashOtp(int otp)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                // Convert the OTP integer to a string
                string otpString = otp.ToString();

                // Compute hash for the OTP string
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(otpString));

                // Convert byte array to a string
                StringBuilder builder = new StringBuilder();
                foreach (byte b in bytes)
                {
                    builder.Append(b.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
