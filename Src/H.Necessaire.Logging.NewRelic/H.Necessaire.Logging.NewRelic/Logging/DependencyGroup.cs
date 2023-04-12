namespace H.Necessaire.Logging.NewRelic.Logging
{
    internal class DependencyGroup : ImADependencyGroup
    {
        public void RegisterDependencies(ImADependencyRegistry dependencyRegistry)
        {
            dependencyRegistry
                .Register<NewRelicLogProcessor>(() => new NewRelicLogProcessor())
                ;
        }
    }
}
