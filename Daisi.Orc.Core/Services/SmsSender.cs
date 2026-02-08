using Azure;
using Azure.Communication.PhoneNumbers;
using Azure.Communication.Sms;

namespace Daisi.Orc.Core.Services
{
    public class SmsSender
    {
        public static Lazy<SmsSender> Instance { get; } = new Lazy<SmsSender>(() => new SmsSender());

        public async Task<Response<SmsSendResult>> SendSmsAsync(string toPhoneNumber, string message)
        {
            // Placeholder for SMS sending logic.
            // In a real implementation, you would integrate with an SMS gateway or service provider.
            Console.WriteLine($"Sending SMS to: {toPhoneNumber}");
            Console.WriteLine($"Message: {message}");

            if(!toPhoneNumber.StartsWith("+"))
            {
                /// assumes the USA country code if no + present
                toPhoneNumber = $"+1{toPhoneNumber}";
            }

            string connectionString = "endpoint=https://daisi-comm-services.unitedstates.communication.azure.com/;accesskey=DQSA9dRXnFyXWvDzYWqlDGlAuAiBYS4ZdnOkb4iMtGeX8uJBOzyOJQQJ99BKACULyCpCZDVOAAAAAZCSauxI";

            var client = new SmsClient(connectionString);
            var result = await client.SendAsync(
                from: "+18333095599",
                to: toPhoneNumber,
                message: message
            );

            return result;

        }
    }
}
