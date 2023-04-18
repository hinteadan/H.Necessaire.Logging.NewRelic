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
                            LoggedBy = "H.Necessaire.Logging.NewRelic",
                        },
                    },
                    Logs
                        = group
                        .Select(log => MapLogEntryToNewRelicLog(log))
                        .ToArray(),
                })
                .ToArray()
                ;
        }

        private static NewRelicLog MapLogEntryToNewRelicLog(LogEntry log)
        {
            LogException[] exceptions = log.Exception?.Flatten()?.Select(MapExceptionToNewRelicLogException)?.ToArrayNullIfEmpty();
            bool hasAddonExceptions = (exceptions?.Length ?? 0) > 1;

            return new NewRelicLog
            {
                TimestampInUnixMilliseconds = log.HappenedAt.ToUnixTimestamp(),
                Message = log.Message,
                Attributes = new
                {
                    log.ID,
                    LogTimestamp = log.HappenedAt.EnsureUtc().ToString("O"),
                    Level = MapLogLevel(log.Level),
                    LevelID = (int)log.Level,
                    log.OperationContext,
                    log.Method,
                    log.StackTrace,
                    log.Component,
                    AppVersionNumber = log.AppVersion?.Number?.ToString(),
                    AppVersionTimestamp = log.AppVersion?.Timestamp.EnsureUtc().ToString("O"),
                    AppVersionBranch = log.AppVersion?.Branch,
                    AppVersionCommit = log.AppVersion?.Commit,
                    Exception = exceptions?.Any() == true ? exceptions.First() : null,
                    ExceptionAddonsMessages = hasAddonExceptions == false ? null : $"{Environment.NewLine}{(string.Join($"{Environment.NewLine}---------------------{Environment.NewLine}", exceptions.Skip(1).Select(x => x.Message.NullIfEmpty()).ToNoNullsArray(nullIfEmpty: false)))}".NullIfEmpty(),
                    ExceptionAddons = hasAddonExceptions == false ? null : exceptions.Skip(1).ToArray(),
                    HasExceptionAddons = hasAddonExceptions ? (true as bool?) : null,
                    log.Payload,
                    log.Notes,
                },
            };
        }

        private static string MapLogLevel(LogEntryLevel level)
        {
            switch(level)
            {
                case LogEntryLevel.Critical: return "Error.Critical";
                default: return level.ToString();
            }
        }

        private static LogException MapExceptionToNewRelicLogException(Exception exception)
        {
            if (exception is null)
                return null;

            List<Note> notes = new List<Note>();
            if (exception.Data?.Keys != null)
            {
                foreach (object key in exception.Data.Keys)
                {
                    string id = key?.ToString();
                    string value = exception.Data[key]?.ToString();
                    if (id.IsEmpty() && value.IsEmpty())
                        continue;
                    notes.Add(new Note(id, value));
                }
            }

            return
                new LogException
                {
                    Message = exception.Message,
                    StackTrace = exception.StackTrace,
                    Source = exception.Source,
                    HResult = exception.HResult,
                    TargetSite = exception.TargetSite?.Name,
                    HelpLink = exception.HelpLink,
                    Notes = notes.ToArrayNullIfEmpty(),
                };
        }

        private class LogException
        {
            public string Message { get; set; }
            public string StackTrace { get; set; }
            public string Source { get; set; }
            public int HResult { get; set; }
            public string TargetSite { get; set; }
            public string HelpLink { get; set; }
            public Note[] Notes { get; set; }
        }
    }
}
