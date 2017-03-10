using ConsoleMenu;
using Microsoft.Azure.Management.ResourceManager;
using Microsoft.Azure.Management.ResourceManager.Models;
using Microsoft.Azure.Management.WebSites;
using Microsoft.Azure.Management.WebSites.Models;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using Microsoft.Rest;
using Microsoft.Rest.Azure;
using SuperSwapperResourceManager.Models;
using SuperSwapperResourceManager.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperSwapperResourceManager
{
	class Program
	{
		private static string _aadTenantDomain = "";
		private static string subscriptionId = "";
		private static GroupFileService _groupFileService = new GroupFileService();
		private static TokenCredentials credentials;


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
			//Get AppVeyor API key from first argument
			if (args.Any())
				_aadTenantDomain = args.First();

			//Or ask for AppVeyor API key
			if (string.IsNullOrWhiteSpace(_aadTenantDomain))
			{
				Console.WriteLine("Enter Active Directory Tenant domain (yourname.onmicrosoft.com):");
				_aadTenantDomain = Console.ReadLine();

				if (string.IsNullOrEmpty(_aadTenantDomain))
				{
					Console.WriteLine("No domain, exiting");
					return;
				}
			}

			var login = await GetAccessTokenAsync();
			credentials = new TokenCredentials(login.AccessToken);

			var subClient = new SubscriptionClient(credentials);
			var subs = subClient.Subscriptions.List();

			if (subs.Count() == 1)
				subscriptionId = subs.First().SubscriptionId;
			else
			{
				var subMenu = new TypedMenu<Subscription>(subs.ToList(), "Choose a subscription", x => x.DisplayName);
				var sub = subMenu.Display();

				subscriptionId = sub.SubscriptionId;
			}


			var choices = new List<Func<Task>>
			{
				CreateSwapGroup,
				NewSwap
			};
			//Present menu of choices
			var menu = new TypedMenu<Func<Task>>(choices, "Choose a number", x => x.Method.Name);

			var picked = menu.Display();
			picked().Wait();


		}


		/// <summary>
		/// Flow to create a new SwapGroup
		/// </summary>
		/// <returns></returns>
		private static async Task CreateSwapGroup()
		{
			Console.WriteLine("Creating new Swap Group");

			//Get GroupName
			Console.WriteLine("Enter new group name:");
			var groupName = Console.ReadLine();

			SwapGroup newGroup = new SwapGroup();
			newGroup.Name = groupName;

			var client = new WebSiteManagementClient(credentials);
			client.SubscriptionId = subscriptionId;

			var resourceClient = new ResourceManagementClient(credentials);
			resourceClient.SubscriptionId = subscriptionId;

			var resourceGroups = resourceClient.ResourceGroups.List();
			var resourceGroepMenu = new TypedMenu<ResourceGroup>(resourceGroups.ToList(), "Choose a resource group", x => x.Name);
			var resourceGroup = resourceGroepMenu.Display();

			var allWebsites = client.WebApps.ListByResourceGroup(resourceGroup.Name).ToList();

			AskForSlot(newGroup, resourceGroup, client, allWebsites);

			//Save group
			//Write JSON to file
			_groupFileService.SaveGroupToFile(newGroup);
			Console.WriteLine("Saved SwapSlots Group {0} with {1} SwapSlots", newGroup.Name, newGroup.SwapSlots.Count);

		}


		/// <summary>
		/// Ask for an slot to add to a group
		/// </summary>
		/// <param name="newGroup"></param>
		/// <param name="allEnv"></param>
		private static void AskForSlot(SwapGroup newGroup, ResourceGroup resourceGroup, WebSiteManagementClient client, List<Site> allWebsites)
		{
			Console.WriteLine("Pick a website:");

			
			var websiteMenu = new TypedMenu<Site>(allWebsites.ToList(), "Choose a website", x => x.Name);
			var website = websiteMenu.Display();

			var websiteSlots = client.WebApps.ListSlots(resourceGroup.Name, website.Name);
			if (!websiteSlots.Any())
				Console.WriteLine("No slots found");
			else if (websiteSlots.Count() == 1)
			{
				var slot = websiteSlots.First();
				Console.WriteLine("Found 1 slot, using: " + slot.Name);
				var name = slot.Name.Replace(slot.RepositorySiteName + "/", string.Empty);
				newGroup.SwapSlots.Add(new SlotConfig() { Name = name, ResourceGroup = resourceGroup.Name, Website = website.Name });

			}
			else
			{
				var slotMenu = new TypedMenu<Site>(websiteSlots.ToList(), "Choose a slot", x => x.Name);
				var slot = slotMenu.Display();
				var name = slot.Name.Replace(slot.RepositorySiteName + "/", string.Empty);
				newGroup.SwapSlots.Add(new SlotConfig() { Name = name, ResourceGroup = resourceGroup.Name, Website = website.Name });
			}



			Console.WriteLine("Add another slot? (Y/N) (default Y)");
			var answer = Console.ReadLine();

			if (answer.Equals("N", StringComparison.InvariantCultureIgnoreCase))
				return;
			else
				AskForSlot(newGroup, resourceGroup, client, allWebsites);

		}


		/// <summary>
		/// Flow to trigger a new deploy
		/// </summary>
		/// <returns></returns>
		private static async Task NewSwap()
		{
			Console.WriteLine("Which Swap Group do you want to swap?");

			//Get all groups from disk
			string path = Directory.GetCurrentDirectory();
			var allFiles = Directory.GetFiles(path, "*.swap.json");

			if (!allFiles.Any())
			{
				Console.WriteLine("No group.json files found! Please create a SwapGroup first.");
				return;
			}

			List<SwapGroup> groupList = allFiles.Select(_groupFileService.ReadGroupFile).ToList();

			var menu = new TypedMenu<SwapGroup>(groupList, "Choose a number", x => x.Name);
			var picked = menu.Display();
			Console.WriteLine("-------------------------------------------------------");

			Console.WriteLine();
			Console.WriteLine($"You want to swap {picked.Name}");

			foreach(var swapSlot in picked.SwapSlots)
			{
				Console.WriteLine(swapSlot.Website + " - " + swapSlot.Name);
			}

			Console.WriteLine("Are you sure? (Y/N) (default N)");
			var answer = Console.ReadLine();

			Console.WriteLine();

			var client = new WebSiteManagementClient(credentials);
			client.SubscriptionId = subscriptionId;

			if (answer.Equals("Y", StringComparison.InvariantCultureIgnoreCase))
			{
				List<Task> swapTasks = new List<Task>();
				foreach(var slot in picked.SwapSlots)
				{
					Console.WriteLine($"Begin swapping: {slot.Website} - {slot.Name}");
                    swapTasks.Add(WaitForSwap(slot, client.WebApps.SwapSlotWithProductionWithHttpMessagesAsync(slot.ResourceGroup, slot.Website, new Microsoft.Azure.Management.WebSites.Models.CsmSlotEntity() { TargetSlot = slot.Name })));
				}

                Console.WriteLine("Waiting for swaps to finish...");
                if (swapTasks.Any())
                {
                    await Task.WhenAll(swapTasks);
                }

                Console.WriteLine("Finished all swap operations!");
			}
		}

        private static async Task WaitForSwap(SlotConfig slot, Task<AzureOperationResponse> swapAction)
        {
            while (!swapAction.IsCompleted)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            if (swapAction.Result.Response.IsSuccessStatusCode)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($" - Swap {slot.Website} - {slot.Name} finished with status: success");
                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" - Swap FAILED {slot.Website} - {slot.Name} finished with status: {swapAction.Result.Response.StatusCode}");
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        private static async Task<AuthenticationResult> GetAccessTokenAsync()
		{
			var context = new AuthenticationContext($"https://login.windows.net/{_aadTenantDomain}");
			var token = await context.AcquireTokenAsync("https://management.core.windows.net/",
				"1950a258-227b-4e31-a9cf-717495945fc2", new Uri("urn:ietf:wg:oauth:2.0:oob"), new PlatformParameters(PromptBehavior.Auto));

			if (token == null)
			{
				throw new InvalidOperationException("Could not get the token");
			}
			return token;
		}
	}
}
