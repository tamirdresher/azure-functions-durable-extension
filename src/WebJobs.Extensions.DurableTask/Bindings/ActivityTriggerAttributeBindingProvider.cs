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
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Extensions.DurableTask
{
    internal class ActivityTriggerAttributeBindingProvider : ITriggerBindingProvider
    {
        private readonly DurableTaskConfiguration durableTaskConfig;
        private readonly ExtensionConfigContext extensionContext;
        private readonly EndToEndTraceHelper traceHelper;

        public ActivityTriggerAttributeBindingProvider(
            DurableTaskConfiguration durableTaskConfig,
            ExtensionConfigContext extensionContext,
            EndToEndTraceHelper traceHelper)
        {
            this.durableTaskConfig = durableTaskConfig;
            this.extensionContext = extensionContext;
            this.traceHelper = traceHelper;

            ActivityTriggerBinding.RegisterBindingRules(extensionContext.Config);
        }

        public Task<ITriggerBinding> TryCreateAsync(TriggerBindingProviderContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            ParameterInfo parameter = context.Parameter;
            ActivityTriggerAttribute trigger = parameter.GetCustomAttribute<ActivityTriggerAttribute>(inherit: false);
            if (trigger == null)
            {
                return Task.FromResult<ITriggerBinding>(null);
            }

            // The activity name defaults to the method name.
            string activityName = trigger.Activity ?? parameter.Member.Name;

            var binding = new ActivityTriggerBinding(
                this,
                parameter,
                trigger,
                activityName,
                trigger.Version);
            return Task.FromResult<ITriggerBinding>(binding);
        }

        private class ActivityTriggerBinding : ITriggerBinding
        {
            private static readonly IReadOnlyDictionary<string, Type> StaticBindingContract =
                new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
                {
                    // This binding supports return values of any type
                    { "$return", typeof(object) },
                    { nameof(DurableActivityContext.InstanceId), typeof(string) },
                };

            private readonly ActivityTriggerAttributeBindingProvider parent;
            private readonly ParameterInfo parameterInfo;
            private readonly ActivityTriggerAttribute attribute;
            private readonly string activityName;
            private readonly string activityVersion;

            public ActivityTriggerBinding(
                ActivityTriggerAttributeBindingProvider parent,
                ParameterInfo parameterInfo,
                ActivityTriggerAttribute attribute,
                string activityName,
                string activityVersion)
            {
                this.parent = parent;
                this.parameterInfo = parameterInfo;
                this.attribute = attribute;
                this.activityName = activityName;
                this.activityVersion = activityVersion;
            }

            public Type TriggerValueType => typeof(DurableActivityContext);

            public IReadOnlyDictionary<string, Type> BindingDataContract => StaticBindingContract;

            public Task<ITriggerData> BindAsync(object value, ValueBindingContext context)
            {
                var activityContext = (DurableActivityContext)value;
                Type destinationType = this.parameterInfo.ParameterType;

                object convertedValue;
                if (destinationType == typeof(object))
                {
                    // Straight assignment
                    convertedValue = value;
                }
                else
                {
                    // Try using the converter manager
                    IConverterManager cm = this.parent.extensionContext.Config.ConverterManager;
                    MethodInfo getConverterMethod = cm.GetType().GetMethod(nameof(cm.GetConverter));
                    getConverterMethod = getConverterMethod.MakeGenericMethod(
                        typeof(DurableActivityContext),
                        destinationType,
                        typeof(ActivityTriggerAttribute));

                    Delegate d = (Delegate)getConverterMethod.Invoke(cm, null);
                    if (d != null)
                    {
                        convertedValue = d.DynamicInvoke(value, this.attribute, context);
                    }
                    else if (!destinationType.IsInterface)
                    {
                        MethodInfo getInputMethod = activityContext.GetType()
                            .GetMethod(nameof(activityContext.GetInput))
                            .MakeGenericMethod(destinationType);
                        convertedValue = getInputMethod.Invoke(activityContext, null);
                    }
                    else
                    {
                        throw new ArgumentException(
                            $"Activity triggers cannot be bound to {destinationType}.",
                            this.parameterInfo.Name);
                    }
                }

                var inputValueProvider = new ObjectValueProvider(
                    convertedValue, 
                    this.parameterInfo.ParameterType);

                var returnValueBinder = new ActivityTriggerReturnValueBinder(
                    activityContext,
                    this.parameterInfo.ParameterType);

                var bindingData = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    { nameof(DurableActivityContext.InstanceId), activityContext.InstanceId },
                    { "$return", returnValueBinder },
                };

                var triggerData = new TriggerData(inputValueProvider, bindingData);
                return Task.FromResult<ITriggerData>(triggerData);
            }

            public ParameterDescriptor ToParameterDescriptor()
            {
                return new ParameterDescriptor { Name = this.parameterInfo.Name };
            }

            public Task<IListener> CreateListenerAsync(ListenerFactoryContext context)
            {
                if (context == null)
                {
                    throw new ArgumentNullException(nameof(context));
                }

                var listener = DurableTaskListener.CreateForActivity(
                    this.parent.durableTaskConfig,
                    this.activityName,
                    this.activityVersion,
                    context.Executor);
                return Task.FromResult<IListener>(listener);
            }

            public static void RegisterBindingRules(JobHostConfiguration hostConfig)
            {
                IConverterManager cm = hostConfig.ConverterManager;
                cm.AddConverter<DurableActivityContext, string>(ActivityContextToString);
                cm.AddConverter<DurableActivityContext, JObject>(ActivityContextToJObject);
            }

            private static JObject ActivityContextToJObject(DurableActivityContext arg)
            {
                JToken token = arg.GetInputAsJson();
                if (token == null)
                {
                    return null;
                }

                JObject jObj = token as JObject;
                if (jObj == null)
                {
                    throw new ArgumentException($"Cannot convert '{token}' to a JSON object.");
                }

                return jObj;
            }

            private static string ActivityContextToString(DurableActivityContext arg)
            {
                return arg.GetInput<string>();
            }

            private class ActivityTriggerReturnValueBinder : IValueBinder
            {
                private readonly DurableActivityContext context;
                private readonly Type valueType;

                public ActivityTriggerReturnValueBinder(DurableActivityContext context, Type valueType)
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
                    this.context.SetOutput(value);
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
