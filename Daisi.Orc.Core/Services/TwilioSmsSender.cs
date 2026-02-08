using System;
using System.Collections.Generic;
using System.Text;

namespace Daisi.Orc.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Threading.Tasks;
    using System.Web;

    namespace TwilioSms
    {
        /// <summary>
        /// Lightweight Twilio SMS sender using only the REST API (no Twilio SDK required)
        /// </summary>
        public class TwilioSmsSender : IDisposable
        {
            private readonly HttpClient _httpClient;
            private readonly string _accountSid;
            private readonly string _authToken;
            private readonly string _fromNumber;

            /// <summary>
            /// Initializes a new instance of the Twilio SMS sender
            /// </summary>
            /// <param name="accountSid">Your Twilio Account SID</param>
            /// <param name="authToken">Your Twilio Auth Token</param>
            /// <param name="fromNumber">Your Twilio phone number (in E.164 format, e.g. +15551234567)</param>
            /// <param name="httpClient">Optional: Provide your own HttpClient for better performance/control</param>
            public TwilioSmsSender(string accountSid, string authToken, string fromNumber, HttpClient? httpClient = null)
            {
                _accountSid = accountSid ?? throw new ArgumentNullException(nameof(accountSid));
                _authToken = authToken ?? throw new ArgumentNullException(nameof(authToken));
                _fromNumber = fromNumber ?? throw new ArgumentNullException(nameof(fromNumber));

                _httpClient = httpClient ?? new HttpClient();

                // Set up Basic Auth header once
                var byteArray = Encoding.ASCII.GetBytes($"{_accountSid}:{_authToken}");
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

                _httpClient.BaseAddress = new Uri($"https://api.twilio.com/2010-04-01/Accounts/{_accountSid}/");
            }

            /// <summary>
            /// Sends an SMS message
            /// </summary>
            /// <param name="to">Recipient phone number in E.164 format (e.g. +15551234567)</param>
            /// <param name="body">Message text (max 1600 characters, 160 GSM characters for single segment)</param>
            /// <returns>Twilio Message SID on success</returns>
            /// <exception cref="TwilioException">Thrown when Twilio returns an error</exception>
            public async Task<string> SendSmsAsync(string to, string body)
            {
                if (string.IsNullOrWhiteSpace(to))
                    throw new ArgumentException("Recipient number is required", nameof(to));

                if (string.IsNullOrWhiteSpace(body))
                    throw new ArgumentException("Message body is required", nameof(body));

                var formData = new Dictionary<string, string>
            {
                { "To", to },
                { "From", _fromNumber },
                { "Body", body }
            };

                var content = new FormUrlEncodedContent(formData);

                var response = await _httpClient.PostAsync("Messages.json", content);

                string responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // Simple parsing of the SID from JSON response
                    // Response looks like: {"sid": "SMxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", ...}
                    const string sidMarker = "\"sid\":\"";
                    var sidStart = responseContent.IndexOf(sidMarker);
                    if (sidStart >= 0)
                    {
                        sidStart += sidMarker.Length;
                        var sidEnd = responseContent.IndexOf('"', sidStart);
                        if (sidEnd > sidStart)
                        {
                            return responseContent.Substring(sidStart, sidEnd - sidStart);
                        }
                    }
                    return "Message sent (SID parsing failed)";
                }
                else
                {
                    // Try to extract Twilio error message
                    var errorMessage = "Unknown error";
                    var messageMarker = "\"message\":\"";
                    var msgStart = responseContent.IndexOf(messageMarker);
                    if (msgStart >= 0)
                    {
                        msgStart += messageMarker.Length;
                        var msgEnd = responseContent.IndexOf('"', msgStart);
                        if (msgEnd > msgStart)
                            errorMessage = responseContent.Substring(msgStart, msgEnd - msgStart);
                    }

                    throw new TwilioException(
                        $"Twilio API error {(int)response.StatusCode}: {errorMessage}",
                        (int)response.StatusCode,
                        responseContent);
                }
            }

            public void Dispose()
            {
                if (_httpClient != null)
                {
                    _httpClient.Dispose();
                }
            }
        }

        /// <summary>
        /// Exception thrown when Twilio returns an error response
        /// </summary>
        public class TwilioException : Exception
        {
            public int StatusCode { get; }
            public string ResponseBody { get; }

            public TwilioException(string message, int statusCode, string responseBody)
                : base(message)
            {
                StatusCode = statusCode;
                ResponseBody = responseBody;
            }
        }
    }
}
