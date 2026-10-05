using System.Windows;

namespace Pulsatilla.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeService.Initialize();
        ThemeService.Apply(new ProtectionSettingsStore().Settings.ThemeMode);
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        ThemeService.Shutdown();
        base.OnExit(e);
    }
	public App()
	{
		var migrationWarnings = AppStorage.MigrateLegacyProfile();
		AppLogger.Initialize();
		foreach (var warning in migrationWarnings) AppLogger.WriteEvent("WARN", warning);
		DispatcherUnhandledException += (_, args) =>
			AppLogger.WriteException("FATAL", "Unhandled WPF dispatcher exception", args.Exception);
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
		{
			if (args.ExceptionObject is Exception exception)
				AppLogger.WriteException("FATAL", $"Unhandled application exception; terminating={args.IsTerminating}", exception);
			else
				AppLogger.WriteEvent("FATAL", $"Unhandled non-Exception object; terminating={args.IsTerminating}");
		};
		TaskScheduler.UnobservedTaskException += (_, args) =>
		{
			AppLogger.WriteException("ERROR", "Unobserved background task exception", args.Exception);
			args.SetObserved();
		};
	}
}

