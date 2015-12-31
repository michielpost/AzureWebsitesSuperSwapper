using AzureWebsitesSuperSwapper.Models;
using CsvHelper;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Management.WebSites;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AzureWebsitesSuperSwapper
{
	class Program
	{
		private static WebSiteManagementClient _client;

		static void Main(string[] args)
		{
			MainAsync(args).Wait();
			// or, if you want to avoid exceptions being wrapped into AggregateException:
			//  MainAsync().GetAwaiter().GetResult();

			Console.WriteLine("Press ENTER to exit.");
			Console.ReadLine();
		}


		static async Task MainAsync(string[] args)
		{

			//Read SwapCommands
			List<SwapCommand> swapCommands = new List<SwapCommand>();
			using (var sr = new StreamReader(@"SwapCommands.csv"))

			{
				var csv = new CsvReader(sr);
				csv.Configuration.Delimiter = ";";
				swapCommands = csv.GetRecords<SwapCommand>().ToList();
			}

			if (!swapCommands.Any())
			{
				Console.WriteLine("No SwapCommands found.");
				return;
			}
			else
			{
				Console.WriteLine($"Found {swapCommands.Count} SwapCommands. Are you sure?");
				Console.WriteLine("Press ENTER to continue.");
				Console.ReadLine();
			}

			var token = GetAuthorizationHeader();
			var cred = new TokenCloudCredentials(
			  ConfigurationManager.AppSettings["subscriptionId"], token);

			_client = new WebSiteManagementClient(cred);
			Dictionary<string, SwapCommand> swapOperations = new Dictionary<string, SwapCommand>();

			//Execute Swap Commands
			foreach (var command in swapCommands)
			{
				var swapOperation = await _client.WebSites.SwapSlotsAsync(command.AppServicePlan, command.WebsiteName, command.SourceSlot, command.TargetSlot);
				swapOperations.Add(swapOperation.OperationId, command);

				var actionDescription = $"Swapping for {command.AppServicePlan} - {command.WebsiteName}: slot {command.SourceSlot} to {command.TargetSlot}";
				Console.WriteLine(actionDescription);
			}

			//Wait for Swap Commands to finish
			await WaitForSwaps(swapOperations);


		}

		private static async Task WaitForSwaps(Dictionary<string, SwapCommand> swaps)
		{
			List<Task<string>> swapTasks = new List<Task<string>>();

			foreach (var swap in swaps)
			{
				swapTasks.Add(WaitForSwap(swap.Key, swap.Value));
			}

			Console.WriteLine("Waiting for swaps to finish...");

			await Task.WhenAll(swapTasks);
			Console.WriteLine("All swaps FINISHED.");
		}


		private static async Task<string> WaitForSwap(string operationId, SwapCommand command)
		{
			bool finished = false;
			string status = "";

			while (!finished)
			{
				await Task.Delay(TimeSpan.FromSeconds(5));

				var operationStatus = await _client.GetOperationStatusAsync(command.AppServicePlan, command.WebsiteName, operationId);

				if (operationStatus.Status != Microsoft.WindowsAzure.Management.WebSites.Models.WebSiteOperationStatus.Created
				   && operationStatus.Status != Microsoft.WindowsAzure.Management.WebSites.Models.WebSiteOperationStatus.InProgress)
				{
					finished = true;
					status = operationStatus.Status.ToString();
				}
			}

			var statusDescription = $"Swap for {command.AppServicePlan} - {command.WebsiteName}: slot {command.SourceSlot} to {command.TargetSlot} ended with status {status.ToUpper()}";
			Console.WriteLine(statusDescription);

			return status;
		}



		private static string GetAuthorizationHeader()
		{
			AuthenticationResult result = null;

			var context = new AuthenticationContext(string.Format(
			  ConfigurationManager.AppSettings["login"],
			  ConfigurationManager.AppSettings["tenantId"]));

			var thread = new Thread(() =>
			{
				result = context.AcquireToken(
				  ConfigurationManager.AppSettings["apiEndpoint"],
				  ConfigurationManager.AppSettings["clientId"],
				  new Uri(ConfigurationManager.AppSettings["redirectUri"]));
			});

			thread.SetApartmentState(ApartmentState.STA);
			thread.Name = "AquireTokenThread";
			thread.Start();
			thread.Join();

			if (result == null)
			{
				throw new InvalidOperationException("Failed to obtain the JWT token");
			}

			string token = result.AccessToken;
			return token;
		}

	}
}
