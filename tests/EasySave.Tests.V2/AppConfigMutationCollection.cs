namespace EasySave.Tests.V2;

// Tests that mutate AppConfig.Instance via AppConfig.Load(...) must run sequentially:
// xUnit parallelizes test classes within an assembly by default, and AppConfig is a
// process-wide singleton. Without this gate, SmokeTests.EasySave_AppConfig_HasDefaults
// can race with SchedulerServiceLockPropagationTests and observe temp-dir paths.
[CollectionDefinition("AppConfigMutation", DisableParallelization = true)]
public class AppConfigMutationCollection { }
