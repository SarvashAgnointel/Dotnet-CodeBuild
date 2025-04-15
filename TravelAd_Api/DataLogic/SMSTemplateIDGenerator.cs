namespace TravelAd_Api.DataLogic
{
    public class SMSTemplateIDGenerator
    {
        private static readonly Random _random = new Random();

        // Method to generate a 16-digit random number ID
        public static string GenerateTemplateId()
        {
            // Generate a 16-digit random number
            long id = GenerateRandomLong();
            return id.ToString();
        }

        // Helper method to generate a 16-digit number
        private static long GenerateRandomLong()
        {
            // Generate a random 16-digit number by creating a large number
            // You can adjust the max value here depending on your requirements.
            return (long)(_random.NextDouble() * (long)Math.Pow(10, 16));
        }
    }
}
