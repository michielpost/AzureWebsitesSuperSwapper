using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AzureWebsitesSuperSwapper.Models
{
	public class SwapCommand
	{
		public string WebSpace { get; set; }
		public string WebsiteName { get; set; }

		public string SourceSlot { get; set; }

		public string TargetSlot { get; set; }

	}
}
