using AzureWebsitesSuperSwapper.Models;
using CsvHelper;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Management.WebSites;
using Microsoft.WindowsAzure.Management.WebSites.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
			string path = Directory.GetCurrentDirectory();
			if (args.Any())
				path = args.First();

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
				Console.WriteLine("List of Swap Commands:");
				foreach(var swap in swapCommands)
				{
					Console.WriteLine($"Found {swap.WebSpace} {swap.WebsiteName}: {swap.SourceSlot} to {swap.TargetSlot}");

				}

				Console.WriteLine($"Found {swapCommands.Count} SwapCommands. Are you sure?");
				Console.WriteLine("Press ENTER to continue.");
				Console.ReadLine();
			}

			//Find .publishprofile file
			var publishProfile = Directory.GetFiles(path, "*.publishsettings").FirstOrDefault();
			if (publishProfile == null)
			{
				Console.WriteLine("No .publishsettings file found. Please download it here http://go.microsoft.com/fwlink/?LinkID=301775 and place it in the current directory.");
				return;
			}

			var pubProfileText = File.ReadAllText(publishProfile);
			PublishSettingsFile file = new PublishSettingsFile(pubProfileText);
			if (file.Subscriptions == null || !file.Subscriptions.Any())
			{
				Console.WriteLine("No subscriptions found in PublishSettingsFile");
				return;
			}
			if (file.Subscriptions.Count() > 1)
				Console.WriteLine("Multiple Subscriptions found, using the first: " + file.Subscriptions.First().Name);

			var cred = file.Subscriptions.First().GetCredentials();


			_client = new WebSiteManagementClient(cred);
			Dictionary<string, SwapCommand> swapOperations = new Dictionary<string, SwapCommand>();

			//List all websites:
			Console.WriteLine();
			Console.WriteLine("List of possible websites to swap:");
			var webspaces = await _client.WebSpaces.ListAsync();
			foreach(var space in webspaces)
			{
				var websites = await _client.WebSpaces.ListWebSitesAsync(space.Name, new WebSiteListParameters
				{
					PropertiesToInclude = new List<string>()
				});

				foreach (var website in websites)
				{
					Console.WriteLine(space.Name + " " + website.Name);
				}
			}
			Console.WriteLine();
			Console.WriteLine();


			//Execute Swap Commands
			foreach (var command in swapCommands)
			{
				try
				{
					var swapOperation = await _client.WebSites.BeginSwappingSlotsAsync(command.WebSpace, command.WebsiteName, command.SourceSlot, command.TargetSlot);
					swapOperations.Add(swapOperation.OperationId, command);

					var actionDescription = $"Swapping for {command.WebSpace} - {command.WebsiteName}: slot {command.SourceSlot} to {command.TargetSlot}";
					Console.WriteLine(actionDescription);
				}
				catch
				{
					var failDescription = $"FAILED for {command.WebSpace} - {command.WebsiteName}: slot {command.SourceSlot} to {command.TargetSlot}";
					Console.WriteLine(failDescription);
				}
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

				var operationStatus = await _client.GetOperationStatusAsync(command.WebSpace, command.WebsiteName, operationId);

				if (operationStatus.Status != Microsoft.WindowsAzure.Management.WebSites.Models.WebSiteOperationStatus.Created
				   && operationStatus.Status != Microsoft.WindowsAzure.Management.WebSites.Models.WebSiteOperationStatus.InProgress)
				{
					finished = true;
					status = operationStatus.Status.ToString();
				}
			}

			var statusDescription = $"Swap for {command.WebSpace} - {command.WebsiteName}: slot {command.SourceSlot} to {command.TargetSlot} ended with status {status.ToUpper()}";
			Console.WriteLine(statusDescription);

			return status;
		}

	}
}
