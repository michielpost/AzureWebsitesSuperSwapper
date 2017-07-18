using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperSwapperResourceManager.Models
{
	public class SwapGroup
	{
		public SwapGroup()
		{
			SwapSlots = new List<SlotConfig>();
			CdnPurgeConfigs = new List<CdnPurgeConfig>();
		}

		public string Name { get; set; }

		public List<SlotConfig> SwapSlots { get; set; }

		public List<CdnPurgeConfig> CdnPurgeConfigs { get; set; }


	}
}
