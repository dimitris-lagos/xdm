using MediaParser.YouTube;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YDLWrapper;

namespace XDM.SystemTests
{

    class JsonTest
    {
        [Test]
        [Ignore("Requires a local media parser fixture")]
        public void ProcessJson()
        {
            var fixture = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "ydl-output.json");
            var res1 = YDLOutputParser.Parse(fixture);
            Console.WriteLine(JsonConvert.SerializeObject(res1));
        }

        [Test]
        [Ignore("Requires a local media parser fixture")]
        public void ProcessYtJson()
        {
            var fixture = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "youtube-formats.json");
            var item = YoutubeDataFormatParser.GetFormats(fixture);
            Console.WriteLine(item.DualVideoItems.Count + " " + item.VideoItems.Count);
            foreach(var a in item.DualVideoItems)
            {
                Console.WriteLine(a.FormatDescription);
            }
        }
    }
}
