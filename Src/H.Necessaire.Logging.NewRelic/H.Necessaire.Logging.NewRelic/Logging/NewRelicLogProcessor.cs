using H.Necessaire.Logging.NewRelic.Logging.DataContracts;
using H.Necessaire.Serialization;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace H.Necessaire.Logging.NewRelic.Logging
{
    internal class NewRelicLogProcessor : LogProcessorBase, ImADependency
    {
        #region Construct
        const int processingBatchMaxSize = 100;
        static readonly TimeSpan logProcessingInterval = TimeSpan.FromSeconds(2.5);
        static readonly LogConfig defaultConfig = new LogConfig { EnabledLevels = LogConfig.LevelsHigherOrEqualTo(LogEntryLevel.Trace, includeNone: false) };
        readonly ConcurrentQueue<LogEntry> logQueue = new ConcurrentQueue<LogEntry>();
        public NewRelicLogProcessor()
        {
            logConfig = defaultConfig;
        }
        ImAPeriodicAction logQueueProcessor;
        string apiKey;
        string apiBaseUrl;
        public void ReferDependencies(ImADependencyProvider dependencyProvider)
        {
            apiBaseUrl = dependencyProvider?.GetRuntimeConfig()?.Get("NewRelic")?.Get("Logging")?.Get("ApiBaseUrl")?.ToString();
            apiKey = dependencyProvider?.GetRuntimeConfig()?.Get("NewRelic")?.Get("Logging")?.Get("ApiKey")?.ToString();
            if (apiKey.IsEmpty() || apiBaseUrl.IsEmpty())
                OperationResult.Fail("NewRelic ApiKey and/or ApiBaseUrl is missing. Must be specified via Runtime Config: <CfgRoot>:NewRelic:Logging:ApiKey  and  <CfgRoot>:NewRelic:Logging:ApiBaseUrl").ThrowOnFail();

            logQueueProcessor = logQueueProcessor ?? dependencyProvider.Get<ImAPeriodicAction>();
            logQueueProcessor.Stop();
            logQueueProcessor.StartDelayed(logProcessingInterval, logProcessingInterval, ProcessLogQueue);
        }
        #endregion

        public override LoggerPriority GetPriority() => LoggerPriority.Immediate;

        public override Task<OperationResult<LogEntry>> Process(LogEntry logEntry)
        {
            logQueue.Enqueue(logEntry);
            return OperationResult.Win().WithPayload(logEntry).AsTask();
        }

        private async Task ProcessLogQueue()
        {
            if (logQueue.IsEmpty)
                return;

            await
                new Func<Task>(async () =>
                {
                    Stack<LogEntry> logStack = new Stack<LogEntry>();

                    while (logQueue.TryDequeue(out LogEntry logEntry))
                    {
                        logStack.Push(logEntry);
                        if (logStack.Count == processingBatchMaxSize)
                            break;
                    }

                    NewRelicLogGroup[] newRelicLogRequest = MapLogEntriesToNewRelicLogRequest(logStack);

                    (await DoLogAppendPostHttpRequest(newRelicLogRequest)).ThrowOnFail();

                })
                .TryOrFailWithGrace(
                    numberOfTimes: 5,
                    onFail: ex => { }
                );
        }

        private async Task<OperationResult> DoLogAppendPostHttpRequest(NewRelicLogGroup[] newRelicLogRequest)
        {
            OperationResult result = OperationResult.Fail("Not yet started");

            await
                new Func<Task>(async () =>
                {
                    using (HttpClient http = BuildNewHttpClient())
                    using (HttpContent jsonContent = new StringContent(newRelicLogRequest.ToJsonArray(), Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage response = await http.PostAsync("", jsonContent))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            result = OperationResult.Win();
                            return;
                        }

                        string responseBody = await response.Content.ReadAsStringAsync();

                        result = OperationResult.Fail($"Error occurred while trying to do HTTP Log Request to New Relic. Response: {(int)response.StatusCode} - {response.StatusCode}", $"{(int)response.StatusCode} - {response.StatusCode}", responseBody);
                    }
                })
                .TryOrFailWithGrace(
                    onFail: ex =>
                    {
                        result = OperationResult.Fail(ex, $"Error occurred while trying to do HTTP Log Request to New Relic. Error: {ex.Message}");
                    }
                );

            return result;
        }

        private HttpClient BuildNewHttpClient()
        {
            return
                new HttpClient
                (
                    handler: BuildNewStandardSocketsHttpHandler(),
                    disposeHandler: true
                )
                {
                    BaseAddress = new Uri(apiBaseUrl),
                }.And(x =>
                {
                    x.DefaultRequestHeaders.Add("Api-Key", apiKey);
                });
        }

        private static StandardSocketsHttpHandler BuildNewStandardSocketsHttpHandler()
        {
            return
                new StandardSocketsHttpHandler()
                {
                    // The maximum idle time for a connection in the pool. When there is no request in
                    // the provided delay, the connection is released.
                    // Default value in .NET 6: 1 minute
                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),

                    // This property defines maximal connection lifetime in the pool regardless
                    // of whether the connection is idle or active. The connection is reestablished
                    // periodically to reflect the DNS or other network changes.
                    // ⚠️ Default value in .NET 6: never
                    //    Set a timeout to reflect the DNS or other network changes
                    PooledConnectionLifetime = TimeSpan.FromHours(.5),
                };
        }

        private static NewRelicLogGroup[] MapLogEntriesToNewRelicLogRequest(IEnumerable<LogEntry> logEntries)
        {
            return
                logEntries
                .GroupBy(x => new
                {
                    x.Application,
                    x.ScopeID,
                })
                .Select(group => new NewRelicLogGroup
                {
                    Common = new NewRelicLogGroupCommon
                    {
                        TimestampInUnixMilliseconds = null,
                        Attributes = new
                        {
                            group.Key.Application,
                            ScopeID = group.Key.ScopeID.ToString(),
                        },
                    },
                    Logs
                        = group
                        .Select(log => new NewRelicLog
                        {
                            TimestampInUnixMilliseconds = log.HappenedAt.ToUnixTimestamp(),
                            Message = log.Message,
                            Attributes = new
                            {
                                log.ID,
                                LogTimestamp = log.HappenedAt.EnsureUtc().ToString("O"),
                                Level = log.Level.ToString(),
                                LevelID = (int)log.Level,
                                log.OperationContext,
                                log.Method,
                                log.StackTrace,
                                log.Component,
                                AppVersionNumber = log.AppVersion?.Number?.ToString(),
                                AppVersionTimestamp = log.AppVersion?.Timestamp.EnsureUtc().ToString("O"),
                                AppVersionBranch = log.AppVersion?.Branch,
                                AppVersionCommit = log.AppVersion?.Commit,
                                log.Exception,
                                log.Payload,
                                log.Notes,
                            },
                        })
                        .ToArray(),
                })
                .ToArray()
                ;
        }
    }
}
