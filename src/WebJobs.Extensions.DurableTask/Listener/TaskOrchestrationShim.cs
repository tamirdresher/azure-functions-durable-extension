﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using DurableTask.Core;

namespace Microsoft.Azure.WebJobs.Extensions.DurableTask
{
    /// <summary>
    /// Task orchestration implementation which delegates the orchestration implementation to a function.
    /// </summary>
    internal class TaskOrchestrationShim : TaskOrchestration
    {
        private readonly DurableTaskConfiguration config;
        private readonly DurableOrchestrationContext context;

        private Func<Task<object>> functionInvocationCallback;

        public TaskOrchestrationShim(
            DurableTaskConfiguration config,
            DurableOrchestrationContext context)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        internal DurableOrchestrationContext Context => this.context;

        public void SetFunctionInvocationCallback(Func<Task<object>> callback)
        {
            if (this.functionInvocationCallback != null)
            {
                throw new InvalidOperationException($"{nameof(SetFunctionInvocationCallback)} must be called only once.");
            }

            this.functionInvocationCallback = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        public override async Task<string> Execute(OrchestrationContext innerContext, string serializedInput)
        {
            if (this.functionInvocationCallback == null)
            {
                throw new InvalidOperationException($"The {nameof(functionInvocationCallback)} has not been assigned!");
            }

            this.context.SetInput(innerContext, serializedInput);

            this.config.TraceHelper.FunctionStarting(
                this.context.HubName,
                this.context.Name,
                this.context.Version,
                this.context.InstanceId,
                this.config.GetIntputOutputTrace(serializedInput),
                true /* isOrchestrator */,
                this.context.IsReplaying);

            object returnValue;
            try
            {
                returnValue = await this.functionInvocationCallback();
            }
            catch (Exception e)
            {
                this.config.TraceHelper.FunctionFailed(
                    this.context.HubName,
                    this.context.Name,
                    this.context.Version,
                    this.context.InstanceId,
                    e.ToString(),
                    true /* isOrchestrator */,
                    this.context.IsReplaying);
                throw;
            }
            finally
            {
                this.context.IsCompleted = true;
            }

            if (returnValue != null)
            {
                this.context.SetOutput(returnValue);
            }

            string serializedOutput = this.context.GetSerializedOutput();

            this.config.TraceHelper.FunctionCompleted(
                this.context.HubName,
                this.context.Name,
                this.context.Version,
                this.context.InstanceId,
                this.config.GetIntputOutputTrace(serializedOutput),
                this.context.ContinuedAsNew,
                true /* isOrchestrator */,
                this.context.IsReplaying);

            return serializedOutput;
        }

        public override string GetStatus()
        {
            // TODO: Implement a means for orchestrator functions to set status
            return null;
        }

        public override void RaiseEvent(OrchestrationContext unused, string eventName, string serializedEventData)
        {
            this.config.TraceHelper.ExternalEventRaised(
                this.context.HubName,
                this.context.Name,
                this.context.Version,
                this.context.InstanceId,
                eventName,
                this.config.GetIntputOutputTrace(serializedEventData),
                this.context.IsReplaying);

            this.context.RaiseEvent(eventName, serializedEventData);
        }
    }
}
