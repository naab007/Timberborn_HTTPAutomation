using Bindito.Core;
using System.Threading;
using Timberborn.HttpApiSystem;
using Timberborn.SingletonSystem;

namespace HTTPAutomation
{
    [Context("Game")]
    public class Configurator : IConfigurator
    {
        // Atomic claim flag — same pattern as GameServices.LoadClaimed/UnloadClaimed.
        // 0 = available, 1 = claimed by the first container that calls Configure().
        // Two Bindito containers call Configure() per session (sometimes concurrently).
        // Only the winner registers anything; the other returns immediately.
        // Reset to 0 by GameServicesInitializer.Unload() when the game scene tears down,
        // so the next game session registers cleanly.
        internal static int _registered = 0;

        public void Configure(IContainerDefinition containerDefinition)
        {
            // Atomic claim: first caller wins (gets 0 back), all others skip.
            if (Interlocked.CompareExchange(ref _registered, 1, 0) != 0) return;

            containerDefinition
                .Bind<GameServicesInitializer>()
                .AsSingleton();

            containerDefinition
                .MultiBind<ILoadableSingleton>()
                .ToExisting<GameServicesInitializer>();

            // IUnloadableSingleton: Unload() fires when the game scene tears down.
            // GameServicesInitializer uses this to reset all static state so that
            // loading a second game mid-session works correctly.
            containerDefinition
                .MultiBind<IUnloadableSingleton>()
                .ToExisting<GameServicesInitializer>();

            containerDefinition
                .Bind<AutomationUiSection>()
                .AsSingleton();

            containerDefinition
                .MultiBind<IHttpApiPageSection>()
                .ToExisting<AutomationUiSection>();

            containerDefinition
                .Bind<GameStateEndpoint>()
                .AsSingleton();

            containerDefinition
                .MultiBind<IHttpApiEndpoint>()
                .ToExisting<GameStateEndpoint>();

            containerDefinition
                .Bind<PopulationEndpoint>()
                .AsSingleton();

            containerDefinition
                .MultiBind<IHttpApiEndpoint>()
                .ToExisting<PopulationEndpoint>();

            containerDefinition
                .Bind<AutomationJsEndpoint>()
                .AsSingleton();

            containerDefinition
                .MultiBind<IHttpApiEndpoint>()
                .ToExisting<AutomationJsEndpoint>();

            containerDefinition
                .Bind<AutomationStorageEndpoint>()
                .AsSingleton();

            containerDefinition
                .MultiBind<IHttpApiEndpoint>()
                .ToExisting<AutomationStorageEndpoint>();

            containerDefinition
                .Bind<LogEndpoint>()
                .AsSingleton();

            containerDefinition
                .MultiBind<IHttpApiEndpoint>()
                .ToExisting<LogEndpoint>();

            containerDefinition
                .Bind<LeverEndpoint>()
                .AsSingleton();

            containerDefinition
                .MultiBind<IHttpApiEndpoint>()
                .ToExisting<LeverEndpoint>();

            containerDefinition
                .Bind<WelcomeEndpoint>()
                .AsSingleton();

            containerDefinition
                .MultiBind<IHttpApiEndpoint>()
                .ToExisting<WelcomeEndpoint>();

            containerDefinition
                .Bind<SensorEndpoint>()
                .AsSingleton();

            containerDefinition
                .MultiBind<IHttpApiEndpoint>()
                .ToExisting<SensorEndpoint>();
        }
    }
}
