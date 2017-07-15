// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Extensions.DurableTask
{
    internal class OrchestrationTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private readonly DurableTaskConfiguration config;
        private readonly ExtensionConfigContext extensionContext;
        private readonly EndToEndTraceHelper traceHelper;

        public OrchestrationTriggerAttributeBindingProvider(
            DurableTaskConfiguration config,
            ExtensionConfigContext extensionContext,
            EndToEndTraceHelper traceHelper)
        {
            this.config = config;
            this.extensionContext = extensionContext;
            this.traceHelper = traceHelper;
        }

        public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ParameterInfo parameter = context.Parameter;
            OrchestrationTriggerAttribute trigger = parameter.GetCustomAttribute<OrchestrationTriggerAttribute>(inherit: false);
            if (trigger == null)
            {
                return Task.FromResult<ITriggerBinding>(null);
            }

            // The orchestration name defaults to the method name.
            string orchestrationName = trigger.Orchestration ?? parameter.Member.Name;

            // TODO: Support for per-function connection string and task hub names
            var binding = new OrchestrationTriggerBinding(
                this.config,
                parameter,
                orchestrationName,
                trigger.Version);
            return Task.FromResult<ITriggerBinding>(binding);
        }

        private class OrchestrationTriggerBinding : ITriggerBinding
        {
            private static readonly IReadOnlyDictionary<string, Type> StaticBindingContract =
                new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
                {
                    // This binding supports return values of any type
                    { "$return", typeof(object) },
                };

            private readonly DurableTaskConfiguration config;
            private readonly ParameterInfo parameterInfo;
            private readonly string orchestrationName;
            private readonly string version;

            public OrchestrationTriggerBinding(
                DurableTaskConfiguration config,
                ParameterInfo parameterInfo,
                string orchestrationName,
                string version)
            {
                this.config = config;
                this.parameterInfo = parameterInfo;
                this.orchestrationName = orchestrationName;
                this.version = version;
            }

            public Type TriggerValueType => typeof(DurableOrchestrationContext);

            public IReadOnlyDictionary<string, Type> BindingDataContract => StaticBindingContract;

            public Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
            {
                // No conversions
                var inputValueProvider = new ObjectValueProvider(value, this.TriggerValueType);
                var returnValueBinder = new OrchestrationTriggerReturnValueBinder(
                    (DurableOrchestrationContext)value,
                    this.TriggerValueType);

                var bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { "$return", returnValueBinder },
                };

                var triggerData = new TriggerData(inputValueProvider, bindingData);
                return Task.FromResult<ITriggerData>(triggerData);
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
            public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException(nameof(context));
                }

                var listener = DurableTaskListener.CreateForOrchestration(
                    this.config,
                    this.orchestrationName,
                    this.version,
                    context.Executor);
                return Task.FromResult<IListener>(listener);
            }

            public ParameterDescriptor ToParameterDescriptor()
            {
                return new ParameterDescriptor { Name = this.parameterInfo.Name };
            }

            private class OrchestrationTriggerReturnValueBinder : IValueBinder
            {
                private readonly DurableOrchestrationContext context;
                private readonly Type valueType;

                public OrchestrationTriggerReturnValueBinder(DurableOrchestrationContext context, Type valueType)
                {
                    this.context = context ?? throw new ArgumentNullException(nameof(context));
                    this.valueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
                }

                public Type Type => this.valueType;

                public Task<object> GetValueAsync()
                {
                    throw new NotImplementedException("This binder should only be used for setting return values!");
                }

                public Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    // We actually expect the output to have already been set by the orchestrator shim at this point.
                    // Adding this check just-in-case it's needed in the future.
                    if (!this.context.IsOutputSet)
                    {
                        this.context.SetOutput(value);
                    }

                    return Task.CompletedTask;
                }

                public string ToInvokeString()
                {
                    return this.context.GetSerializedOutput();
                }
            }
        }
    }
}
