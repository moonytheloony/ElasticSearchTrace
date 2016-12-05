namespace TraceService
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;

    using Nest;

    public class ElasticSearchListener : BufferingEventListener, IDisposable
    {
        private const string Dash = "-";

        private const string Dot = ".";

        // TODO: make it a (configuration) property of the listener
        private const string EventDocumentTypeName = "event";

        private ElasticSearchConnectionData connectionData;

        // TODO: support for multiple ES nodes/connection pools, for failover and load-balancing        

        public ElasticSearchListener(IConfigurationProvider configurationProvider, IHealthReporter healthReporter)
            : base(configurationProvider, healthReporter)
        {
            if (this.Disabled)
            {
                return;
            }

            Debug.Assert(configurationProvider != null);
            this.CreateConnectionData(configurationProvider);

            this.Sender = new ConcurrentEventSender<EventData>(
                eventBufferSize: 1000,
                maxConcurrency: 2,
                batchSize: 100,
                noEventsDelay: TimeSpan.FromMilliseconds(1000),
                transmitterProc: this.SendEventsAsync,
                healthReporter: healthReporter);
        }

        private void CreateConnectionData(object sender)
        {
            var configurationProvider = (IConfigurationProvider)sender;

            this.connectionData = new ElasticSearchConnectionData();
            this.connectionData.Client = this.CreateElasticClient(configurationProvider);
            this.connectionData.LastIndexName = null;
            var indexNamePrefix = configurationProvider.GetValue("indexNamePrefix");
            this.connectionData.IndexNamePrefix = string.IsNullOrWhiteSpace(indexNamePrefix)
                ? string.Empty
                : indexNamePrefix + Dash;
        }

        private ElasticClient CreateElasticClient(IConfigurationProvider configurationProvider)
        {
            var esServiceUriString = configurationProvider.GetValue("serviceUri");
            Uri esServiceUri;
            var serviceUriIsValid = Uri.TryCreate(esServiceUriString, UriKind.Absolute, out esServiceUri);
            if (!serviceUriIsValid)
            {
                throw new Exception("serviceUri must be a valid, absolute URI");
            }

            var userName = configurationProvider.GetValue("userName");
            var password = configurationProvider.GetValue("password");
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                throw new Exception("Invalid Elastic Search credentials");
            }

            var config = new ConnectionSettings(esServiceUri).BasicAuthentication(userName, password);
            return new ElasticClient(config);
        }

        private async Task EnsureIndexExists(string currentIndexName, ElasticClient esClient)
        {
            var existsResult = await esClient.IndexExistsAsync(currentIndexName);
            if (!existsResult.IsValid)
            {
                this.ReportEsRequestError(existsResult, "Index exists check");
            }

            if (existsResult.Exists)
            {
                return;
            }

            // TODO: allow the consumer to fine-tune index settings
            var indexState = new IndexState();
            indexState.Settings = new IndexSettings();
            indexState.Settings.NumberOfReplicas = 1;
            indexState.Settings.NumberOfShards = 5;
            indexState.Settings.Add("refresh_interval", "15s");

            var createIndexResult =
                await esClient.CreateIndexAsync(currentIndexName, c => c.InitializeUsing(indexState));

            if (!createIndexResult.IsValid)
            {
                if ((createIndexResult.ServerError != null) && (createIndexResult.ServerError.Error != null)
                    && string.Equals(
                        createIndexResult.ServerError.Error.Type,
                        "IndexAlreadyExistsException",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // This is fine, someone just beat us to create a new index.
                    return;
                }

                this.ReportEsRequestError(createIndexResult, "Create index");
            }
        }

        private string GetIndexName(ElasticSearchConnectionData connectionData)
        {
            var now = DateTimeOffset.UtcNow;
            var retval = connectionData.IndexNamePrefix + now.Year + Dot + now.Month + Dot + now.Day;
            return retval;
        }

        private void ReportEsRequestError(IResponse response, string request)
        {
            Debug.Assert(!response.IsValid);

            if (response.ServerError != null)
            {
                this.ReportListenerProblem(
                    string.Format(
                        "ElasticSearch communication attempt resulted in an error: {0} \n ExceptionType: {1} \n Status code: {2}",
                        response.ServerError.Error,
                        response.ServerError.Error.Type,
                        response.ServerError.Status));
            }
            else if (response.DebugInformation != null)
            {
                this.ReportListenerProblem(
                    "ElasticSearch communication attempt resulted in an error. Debug information: "
                    + response.DebugInformation);
            }
            else
            {
                // Hopefully never happens
                this.ReportListenerProblem(
                    "ElasticSearch communication attempt resulted in an error. No further error information is available");
            }
        }

        private async Task SendEventsAsync(
            IEnumerable<EventData> events,
            long transmissionSequenceNumber,
            CancellationToken cancellationToken)
        {
            if (events == null)
            {
                return;
            }

            try
            {
                var currentIndexName = this.GetIndexName(this.connectionData);
                if (!string.Equals(currentIndexName, this.connectionData.LastIndexName, StringComparison.Ordinal))
                {
                    await this.EnsureIndexExists(currentIndexName, this.connectionData.Client);
                    this.connectionData.LastIndexName = currentIndexName;
                }

                var request = new BulkRequest();
                request.Refresh = true;

                var operations = new List<IBulkOperation>();
                foreach (var eventData in events)
                {
                    var operation = new BulkCreateOperation<EventData>(eventData);
                    operation.Index = currentIndexName;
                    operation.Type = EventDocumentTypeName;
                    operations.Add(operation);
                }

                request.Operations = operations;

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                // Note: the NEST client is documented to be thread-safe so it should be OK to just reuse the this.esClient instance
                // between different SendEventsAsync callbacks.
                // Reference: https://www.elastic.co/blog/nest-and-elasticsearch-net-1-3
                var response = await this.connectionData.Client.BulkAsync(request);
                if (!response.IsValid)
                {
                    this.ReportEsRequestError(response, "Bulk upload");
                }

                this.ReportListenerHealthy();
            }
            catch (Exception e)
            {
                this.ReportListenerProblem("Diagnostics data upload has failed." + Environment.NewLine + e);
            }
        }

        private class ElasticSearchConnectionData
        {
            public ElasticClient Client { get; set; }

            public string IndexNamePrefix { get; set; }

            public string LastIndexName { get; set; }
        }
    }
}