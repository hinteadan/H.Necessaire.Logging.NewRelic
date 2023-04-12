using H.Necessaire.CLI;
using H.Necessaire.Runtime.CLI;

namespace H.Necessaire.Logging.NewRelic.CLI
{
    internal class Program
    {
        public static void Main()
        {
            new CliApp()
                .WithEverything()
                .WithDefaultRuntimeConfig()
                .With(x => x.Register<NewRelicLoggingDependencyGroup>(() => new NewRelicLoggingDependencyGroup()))
                .Run(askForCommandIfEmpty: true)
                .GetAwaiter()
                .GetResult()
                ;
        }
    }
}