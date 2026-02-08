using Azure;
using Azure.Communication.Email;


namespace Daisi.Orc.Core.Services
{
    public class EmailSender
    {
        public static Lazy<EmailSender> Instance { get; } = new Lazy<EmailSender>(() => new EmailSender());

        public async Task SendEmailAsync(string toEmailAddress, string subject, string? plainTextContent = default, string? htmlContent = default, string? toName = default)
        {
            // Placeholder for email sending logic.
            // In a real implementation, you would integrate with an SMTP server or an email service provider.
            Console.WriteLine($"Sending Email to: {toEmailAddress}");
            Console.WriteLine($"Subject: {subject}");
            Console.WriteLine($"Plain Text: {plainTextContent}");
            Console.WriteLine($"To Name: {toName}");

            string connectionString = "endpoint=https://daisi-comm-services.unitedstates.communication.azure.com/;accesskey=DQSA9dRXnFyXWvDzYWqlDGlAuAiBYS4ZdnOkb4iMtGeX8uJBOzyOJQQJ99BKACULyCpCZDVOAAAAAZCSauxI";
            var emailClient = new EmailClient(connectionString);


            var emailMessage = new EmailMessage(
                senderAddress: "DoNotReply@2d203cf2-df2e-4999-b1c6-67befb06eeba.azurecomm.net",
                content: new EmailContent(subject)
                {
                    PlainText = plainTextContent,
                    Html = htmlContent
                },
                recipients: new EmailRecipients(new List<EmailAddress>
                {
                    new EmailAddress(toEmailAddress)
                }));


            EmailSendOperation emailSendOperation = emailClient.Send(
                WaitUntil.Completed,
                emailMessage);

        }
    }
}
