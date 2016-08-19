﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Inversion.Data;
using Inversion.Naiad;
using Inversion.Process;
using Inversion.Process.Behaviour;
using Inversion.Process.Pipeline;
using Inversion.Ultrastructure.Transport;
using log4net;

[assembly: log4net.Config.XmlConfigurator(Watch = true)]

namespace Ultrastructure.Demo2
{
    class Program
    {
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly Dictionary<string, ServiceContainer> _pipelines = new Dictionary<string, ServiceContainer>();
        private static bool _requestToQuit = false;

        static void Lifecycle(IProcessContext context, Func<bool> timeToGo)
        {
            // register to a signal receiver so that we can quit on command
            // sub using adaptor
            // - pass fully registered context

            using (IPubSubClient pubSubClient = context.Services.GetService<IPubSubClient>("pubsub"))
            {
                pubSubClient.Start();

                pubSubClient.Subscribe(context, timeToGo);
            }
        }

        static IProcessContext CreatePipeline(string name)
        {
            IDictionary<string, string> settings = new Dictionary<string, string>();
            foreach (string key in ConfigurationManager.AppSettings.AllKeys)
            {
                settings.Add(key, ConfigurationManager.AppSettings[key]);
            }

            ServiceContainer instance = new ServiceContainer();

            PipelineConfigurationHelper.Configure(instance, settings, new List<string>
            {
                settings["prototype-provider"],
                settings[name],
                settings["storage-provider"]
            });

            _pipelines.Add(name, instance);

            IProcessContext context = new ProcessContext(instance, FileSystemResourceAdapter.Instance);

            context.Register(
                context.Services.GetService<IList<IProcessBehaviour>>("application-behaviours"));

            return context;
        }

        static Task PubSubLifecycle(TaskFactory taskFactory, IProcessContext context)
        {
            return taskFactory.StartNew(() => Lifecycle(context, TimeToGo));
        }

        static bool TimeToGo()
        {
            return _requestToQuit;
        }

        static Task RunListener(string pipeline, TaskFactory taskFactory)
        {
            return PubSubLifecycle(taskFactory, CreatePipeline(String.Format("{0}-listener", pipeline)));
        }

        static Task RunPump(TaskFactory taskFactory)
        {
            return taskFactory.StartNew(() =>
            {
                IProcessContext context = CreatePipeline("pipeline-pump");

                while (!_requestToQuit)
                {
                    context.Fire("process-request");
                    System.Threading.Thread.Sleep(1000);
                }
            });
        }

        static void Main(string[] args)
        {
            TaskFactory taskFactory = new TaskFactory();

            Task requestToQuit = taskFactory.StartNew(() =>
            {
                Console.WriteLine("Press ENTER to quit.");
                Console.ReadLine();
                _requestToQuit = true;
            });

            switch (args[0])
            {
                case "listener1":
                    RunListener("pipeline1", taskFactory).Wait();
                    break;
                case "listener2":
                    RunListener("pipeline2", taskFactory).Wait();
                    break;
                case "pump":
                    RunPump(taskFactory).Wait();
                    break;
            }
        }
    }
}