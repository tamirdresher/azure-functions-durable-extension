using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VSSample
{

    public static class RunAuction
    {
        [FunctionName("AddAsk")]
        public static async Task<List<BidInfo>> Run(
            [OrchestrationTrigger] DurableOrchestrationContext context,
            TraceWriter log)
        {
            var product = context.GetInput<string>();
            var outputs = new List<BidInfo>();
            log.Info("calling SaveAsk");
            var askInfo = await context.CallFunctionAsync<AskInfo>("SaveAsk", product);
            log.Info($"AskInfo - Id: {askInfo.AskId}");

            using (var timeoutCts = new CancellationTokenSource())
            {
                DateTime expiration = context.CurrentUtcDateTime.AddSeconds(10);
                await context.CreateTimer(expiration, timeoutCts.Token);
                log.Info("delay done");

                var highestBid = await context.CallFunctionAsync<BidInfo>("GetHighestBid", askInfo.AskId);
                log.Info($"highestBid - Id: {highestBid.BidId} AskInfo.Id: {askInfo?.AskId}");

                outputs.Add(highestBid);

                // returns ["Hello Tokyo!", "Hello Seattle!", "Hello London!"]
                return outputs;
            }
        }

        [FunctionName("SaveAsk")]
        public static AskInfo SaveAsk(
            [ActivityTrigger] DurableActivityContext helloContext)
        {
            string productname;
            try
            {
                productname = helloContext.GetInput<string>();

            }
            catch (Exception)
            {

                productname = "problem with GetInput";
            }
            //return $"Saving {name}!";

            return new AskInfo
            {
                AskId = Guid.NewGuid(),
                ProductName = productname
            };
        }

        [FunctionName("GetHighestBid")]
        public static BidInfo GetHighestBid(
           [ActivityTrigger] DurableActivityContext helloContext)
        {
            Guid askId = helloContext.GetInput<Guid>();
            //return $"Saving {name}!";

            return new BidInfo
            {
                BidId = Guid.NewGuid(),
                AskId = askId,

                UserName = "Tamir",
                UserEmail = "Tamir@Dresher.com",
                Price = 100
            };
        }
    }


    public class AskInfo
    {
        public AskInfo()
        {
            Console.WriteLine("AskInfo Created");
        }
        public Guid AskId { get; set; }
        public string ProductName { get; set; }
    }

    public class BidInfo
    {
        public Guid BidId { get; set; }
        public string UserName { get; set; }
        public string UserEmail { get; set; }

        public Guid AskId { get; set; }
        public decimal Price { get; set; }
    }
}
