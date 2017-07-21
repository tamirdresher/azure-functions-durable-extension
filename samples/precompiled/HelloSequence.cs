// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.



namespace VSSample
{
    using Microsoft.Azure.WebJobs;
    using Microsoft.Azure.WebJobs.Extensions.Http;
    using Microsoft.Azure.WebJobs.Host;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;

    public static class HelloSequence
    {
        [FunctionName("HelloWorldExecutor")]
        public static async Task<HttpResponseMessage> ExecuteHelloWorld(
            [HttpTrigger(AuthorizationLevel.Anonymous, methods: "post", Route = "HelloWorld")] HttpRequestMessage req,
            [OrchestrationClient] DurableOrchestrationClient client,
            TraceWriter log)
        {
            object functionInput = null;
            string instanceId = await client.StartNewAsync(nameof(HelloWorld), functionInput);

            //return req.CreateResponse(HttpStatusCode.OK, $"New HelloWorld instance created with ID = '{instanceId}'");

            return client.CreateCheckStatusResponse(req, instanceId);
        }


        [FunctionName("HelloWorld")]
        public static async Task<List<string>> HelloWorld(
            [OrchestrationTrigger] DurableOrchestrationContext context,
            TraceWriter log)
        {
            var outputs = new List<string>();

            outputs.Add(await context.CallFunctionAsync<string>("Say", "Hello"));
            outputs.Add(await context.CallFunctionAsync<string>("Say", " "));
            outputs.Add(await context.CallFunctionAsync<string>("Say", "World!"));

            log.Info(string.Concat(outputs));

            // returns ["Hello", "World"]
            return outputs;
        }

        [FunctionName("Say")]
        public static string Say(
            [ActivityTrigger] DurableActivityContext activityContext,
            TraceWriter log)
        {
            string word = activityContext.GetInput<string>();
            log.Info(word);
            return word;
        }
    }
}
