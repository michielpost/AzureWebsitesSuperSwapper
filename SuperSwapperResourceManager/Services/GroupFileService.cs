using Newtonsoft.Json;
using SuperSwapperResourceManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuperSwapperResourceManager.Services
{
	/// <summary>
	/// Responsible for reading and writing (SwapGroup) swap.json files
	/// </summary>
	public class GroupFileService
	{
		/// <summary>
		/// Read a swap.json file
		/// </summary>
		/// <param name="fileName"></param>
		/// <returns></returns>
		public SwapGroup ReadGroupFile(string fileName)
		{
			using (StreamReader r = new StreamReader(fileName))
			{
				string json = r.ReadToEnd();
				SwapGroup result = JsonConvert.DeserializeObject<SwapGroup>(json);
				return result;
			}
		}

		/// <summary>
		/// Save a swap.json file
		/// </summary>
		/// <param name="newGroup"></param>
		public void SaveGroupToFile(SwapGroup newGroup)
		{
			string fileName = string.Format("{0}.swap.json", newGroup.Name);

			using (FileStream fs = File.Open(fileName, FileMode.CreateNew))
			using (StreamWriter sw = new StreamWriter(fs))
			using (JsonWriter jw = new JsonTextWriter(sw))
			{
				jw.Formatting = Formatting.Indented;

				JsonSerializer serializer = new JsonSerializer();
				serializer.Serialize(jw, newGroup);
			}


		}
	}
}
