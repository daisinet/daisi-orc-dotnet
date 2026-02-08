using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Services
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;

    namespace SendGridEmail
    {
        /// <summary>
        /// Lightweight SendGrid v3 email sender - no SDK required
        /// </summary>
        public class SendGridEmailSender : IDisposable
        {
            private readonly HttpClient _httpClient;
            private readonly string _apiKey;

            /// <summary>
            /// Creates a new SendGrid email sender
            /// </summary>
            /// <param name="apiKey">Your SendGrid API key (with Mail Send permission)</param>
            /// <param name="httpClient">Optional: Pass your own HttpClient (recommended for reuse)</param>
            public SendGridEmailSender(string apiKey, HttpClient? httpClient = null)
            {
                _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));

                _httpClient = httpClient ?? new HttpClient();
                _httpClient.BaseAddress = new Uri("https://api.sendgrid.net/v3/");
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _apiKey);
                _httpClient.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            }

            /// <summary>
            /// Sends an email via SendGrid
            /// </summary>
            /// <param name="fromEmail">Sender email (must be verified in SendGrid)</param>
            /// <param name="fromName">Optional sender name</param>
            /// <param name="toEmail">Recipient email</param>
            /// <param name="toName">Optional recipient name</param>
            /// <param name="subject">Email subject</param>
            /// <param name="plainTextContent">Plain text version</param>
            /// <param name="htmlContent">HTML version (optional)</param>
            /// <returns>SendGrid message ID if successful</returns>
            public async Task<string> SendEmailAsync(
                string fromEmail,
                string? fromName,
                string toEmail,
                string? toName,
                string subject,
                string? plainTextContent,
                string? htmlContent = null)
            {
                var payload = new SendGridRequest
                {
                    Personalizations = new[]
                    {
                        new Personalization
                        {
                            To = new[]
                            {
                                new EmailAddress { Email = toEmail, Name = toName }
                            }
                        }
                    },
                    From = new EmailAddress { Email = fromEmail, Name = fromName },
                    Subject = subject,
                    Content = new[]
                    {
                        new Content { Type = "text/plain", Value = plainTextContent }
                    }
                };

                // Add HTML content if provided
                if (!string.IsNullOrWhiteSpace(htmlContent))
                {
                    payload.Content = new[]
                    {
                        new Content { Type = "text/plain", Value = plainTextContent },
                        new Content { Type = "text/html", Value = htmlContent }
                    };
                }

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("mail/send", content);

                if (response.IsSuccessStatusCode)
                {
                    // SendGrid returns 202 Accepted with no body on success
                    // You can extract message ID from headers if needed
                    response.Headers.TryGetValues("X-Message-Id", out var messageIds);
                    return messageIds?.FirstOrDefault() ?? "sent (no message ID)";
                }

                string errorBody = await response.Content.ReadAsStringAsync();
                throw new SendGridException(
                    $"SendGrid error {(int)response.StatusCode}: {response.ReasonPhrase}",
                    (int)response.StatusCode,
                    errorBody);
            }

            public void Dispose()
            {
                _httpClient?.Dispose();
            }
        }

        // POCOs for SendGrid v3 API
        internal class SendGridRequest
        {
            [JsonPropertyName("personalizations")]
            public Personalization[] Personalizations { get; set; } = default!;

            [JsonPropertyName("from")]
            public EmailAddress From { get; set; } = default!;

            [JsonPropertyName("subject")]
            public string Subject { get; set; } = default!;

            [JsonPropertyName("content")]
            public Content[] Content { get; set; } = default!;
        }

        internal class Personalization
        {
            [JsonPropertyName("to")]
            public EmailAddress[] To { get; set; } = default!;
        }

        internal class EmailAddress
        {
            [JsonPropertyName("email")]
            public string Email { get; set; } = default!;

            [JsonPropertyName("name")]
            public string? Name { get; set; }
        }

        internal class Content
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = default!;

            [JsonPropertyName("value")]
            public string Value { get; set; } = default!;
        }

        /// <summary>
        /// Exception thrown when SendGrid returns an error
        /// </summary>
        public class SendGridException : Exception
        {
            public int StatusCode { get; }
            public string ResponseBody { get; }

            public SendGridException(string message, int statusCode, string responseBody)
                : base(message)
            {
                StatusCode = statusCode;
                ResponseBody = responseBody;
            }
        }
    }
}
