namespace H.Necessaire.Logging.NewRelic
{
    public class NewRelicLoggingDependencyGroup : ImADependencyGroup
    {
        public void RegisterDependencies(ImADependencyRegistry dependencyRegistry)
        {
            dependencyRegistry
                .Register<Logging.DependencyGroup>(() => new Logging.DependencyGroup())
                ;
        }
    }
}
