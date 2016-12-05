namespace TraceService
{
    using System;
    using System.Diagnostics;
    using System.Threading;

    using Microsoft.ServiceFabric.Services.Runtime;

    internal static class Program
    {
        /// <summary>
        ///     This is the entry point of the service host process.
        /// </summary>
        private static void Main()
        {
            const string EventListenerId = "ElasticSearchEventListener";
            FabricConfigurationProvider configurationProvider = new FabricConfigurationProvider(EventListenerId);
            ElasticSearchListener listener = null;
            if (configurationProvider.HasConfiguration)
            {
                listener = new ElasticSearchListener(configurationProvider, new FabricHealthReporter(EventListenerId));
            }
            try
            {
                // The ServiceManifest.XML file defines one or more service type names.
                // Registering a service maps a service type name to a .NET type.
                // When Service Fabric creates an instance of this service type,
                // an instance of the class is created in this host process.

                ServiceRuntime.RegisterServiceAsync("TraceServiceType", context => new TraceService(context))
                    .GetAwaiter()
                    .GetResult();

                ServiceEventSource.Current.ServiceTypeRegistered(
                    Process.GetCurrentProcess().Id,
                    typeof(TraceService).Name);

                // Prevents this host process from terminating so services keep running.
                Thread.Sleep(Timeout.Infinite);
                GC.KeepAlive(listener);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceHostInitializationFailed(e.ToString());
                throw;
            }
        }
    }
}