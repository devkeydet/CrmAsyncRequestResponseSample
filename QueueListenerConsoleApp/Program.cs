using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.ServiceBus.Messaging;
using Nito.AsyncEx;
using RestSharp;

namespace QueueListenerConsoleApp
{
    internal static class Program
    {
        private static readonly string _aadInstance = ConfigurationManager.AppSettings["ida:AADInstance"];
        private static readonly string _tenant = ConfigurationManager.AppSettings["ida:Tenant"];
        private static readonly string _clientId = ConfigurationManager.AppSettings["ida:ClientId"];
        private static readonly string _authority = string.Format(CultureInfo.InvariantCulture, _aadInstance, _tenant);
        private static readonly string _connectionString = ConfigurationManager.AppSettings["Microsoft.ServiceBus.ConnectionString"];
        private static readonly string _crmInstanceUrl = ConfigurationManager.AppSettings["crmInstanceUrl"];
        private static readonly object _crmWebApiVersion = ConfigurationManager.AppSettings["crmWebApiVersion"];
        private static readonly Uri _baseUri = new Uri($"{_crmInstanceUrl}/api/data/v{_crmWebApiVersion}");
        // NOTE: If using Dynamics 365 (online) December 2016 update or later, consider using S2S instead of username/password:
        // https://msdn.microsoft.com/en-us/library/mt790168.aspx
        // If not, consider storing retrieving sensitive data using Azure KeyVault
        private static readonly UserPasswordCredential _userCredential = new UserPasswordCredential(
            ConfigurationManager.AppSettings["user"],
            ConfigurationManager.AppSettings["password"]
        );
        private static AuthenticationContext _authContext;

        private static void Main(string[] args)
        {
            _authContext = new AuthenticationContext(_authority, new FileTokenCache());

            AsyncContext.Run(() => MainAsync(args));
        }

        // ReSharper disable once UnusedParameter.Local
        private static async void MainAsync(string[] args)
        {
            var client = QueueClient.CreateFromConnectionString(_connectionString);
            while (true)
            {
                var message = await client.ReceiveAsync();
                if (message != null)
                    ProcessMessageAsync(message);
            }
            // ReSharper disable once FunctionNeverReturns
        }

        private static async void ProcessMessageAsync(BrokeredMessage message)
        {
            var primaryEntityId = await new StreamReader(message.GetBody<Stream>(), Encoding.UTF8).ReadToEndAsync();

            // Pretend more data was passed in the message and there is more processing needed here
            // or perhaps we need to call a web service, etc.
            Thread.Sleep(2000);

            var restClient = new RestClient(_baseUri);
            var authResult = await _authContext.AcquireTokenAsync(_crmInstanceUrl, _clientId, _userCredential);
            restClient.AddDefaultHeader("Authorization", $"Bearer {authResult.AccessToken}");

            var entity = $"dkdt_asyncrequestresponsesamples({primaryEntityId})";

            var request = new RestRequest(entity, Method.PATCH)
            {
                RequestFormat = DataFormat.Json
            };

            restClient.AddDefaultHeader("If-Match", "*"); //Ensure PATCH fails if the entity is already deleted.

            request.AddBody(new
            {
                dkdt_updatefromazurecodecomplete = true,
                dkdt_responsefromazurecode = "WHATEVER DATA YOU WANT TO SEND BACK"
            });

            var response = await restClient.ExecuteTaskAsync(request);

            // If the update was successfulr or the entity has already been deleted, then
            // call CompleteAsync() to delete the message from the queu
            if (response.StatusCode == HttpStatusCode.NoContent || response.StatusCode == HttpStatusCode.NotFound)
            {
                await message.CompleteAsync();
            }
            else
            {
                Console.WriteLine("Something went wrong updating CRM");
                Console.WriteLine($"StatusCode: {response.StatusCode}");
                Console.WriteLine("Content:");
                Console.WriteLine(response.Content);
                Console.WriteLine();
            }
        }
    }
}