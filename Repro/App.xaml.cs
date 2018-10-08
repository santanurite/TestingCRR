namespace CloudRetailer.Pos
{
    using CloudRetailer.Pos.Contracts.BusinessRules;
    using CloudRetailer.Pos.Contracts.Core;
    using CloudRetailer.Pos.Contracts.Hardware;
    using CloudRetailer.Pos.Contracts.Messaging;
    using CloudRetailer.Pos.Contracts.ViewModels;
    using CloudRetailer.Pos.Contracts.Views;
    using CloudRetailer.Pos.Core;
    using CloudRetailer.Pos.Internal;
    using Common.Data;
    using Contracts;
    using Contracts.Commands;
    using Contracts.Events;
    using Contracts.Services;
    using Microsoft.Win32;
    using Views;
    using NotificationMessage = Contracts.Messaging.NotificationMessage;

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const int DuplicateInstanceShutdownExitCode = -1000;

        private IPosApplicationContainer container;

        private EventMessenger messenger;

        private string[] CommandLineArgs { get; set; }

private sm_1(){

var test ="Hi ";
}
        static App()
        {
			//ABHISHEK RAKSHIT - UPDATE
            //Debugger.Launch();
            //Debugger.Break();

            //Configure AppDomain settings
            AppDomain.CurrentDomain.SetData("DataDirectory",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Cloud Retailer", "Cloud Retailer POS"));
            AppDomain currentDomain = AppDomain.CurrentDomain;
            SetCulture();
            currentDomain.AssemblyResolve += LoadAssembly;
        }

        private static void SetCulture()
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        }

        private static Assembly LoadAssembly(object sender, ResolveEventArgs args)
        {
            if (args.Name.Contains(".resources") || args.Name.Contains(".XmlSerializers"))
            {
                return null;
            }

            var assemblyName = new AssemblyName(args.Name);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName.Name);
            //var path = Assembly.GetEntryAssembly().Location 
            return assembly;
        }

        public App()
        {
            //Required designer call
            this.InitializeComponent();
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            //RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

            //Wire-up additional exception handling
            this.Dispatcher.UnhandledException += ((sender, e) =>
                {
                    if ((e != null) && (e.Exception != null))
                    {
                        //Log the exception
                        MessagingService?.SendLogMessage("Unhandled exception", e.Exception.ToString(), CloudRetailerTraceLevel.Critical);

                        var isCritical = IsCritical(e.Exception);
                        var message = isCritical ? "Unexpected critical exception occurred: " + e.Exception.Message + ". The application will now close." :
                                                   "Unexpected exception occurred: " + e.Exception.Message + ". \n<b>If you experience further problems, please restart the application and contact system administrator.</b>";

                        if (!e.Exception.Message.Contains("DependencySource"))
                        {
                            PromptUser(message);
                        }
                        
                        if (!isCritical)
                        {
                            e.Handled = true;
                        }
                        
                    }
                });
        }

        private bool IsCritical(Exception exception)
        {
            if (exception is ArithmeticException)
            {
                return true;
            }

            if (exception is OutOfMemoryException)
            {
                return true;
            }

            if (exception is SqlException)
            {
                return true;
            }

            return false;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            CommandLineArgs = e.Args;

            //Check to see if there are other instance running, if so exit
            var blnOtherInstancesRunning = this.CheckForOtherApplicationInstances();
            if (blnOtherInstancesRunning)
            {
                return;
            }

            this.SpawnSplashScreen();

            EnsureContainerExists();

            this.MessagingService.SendPosEventMessage(new PosStartupCompletedEventData(PosApplication));
            MessagingService?.SendLogMessage("Pos application started.", CloudRetailerTraceLevel.Info);
            //Create MainView, set it to be the Application's MainWindow, add a event surrogate to close SplashScreen when MainView loads, then show MainView
            this.MessagingService.SendPosStartupMessage("Loading POS View...");
            this.ConfigureAndDisplayMainView();

            this.CustomerDisplayService.StartService();

            SetWebBrowserIEMode();

            if (ServiceSettings.IsDebug())
            {
                this.MessagingService.SendPosStartupMessage("Firing runtime tests...");
                var runtimeTester = new RuntimeTester(PosApplication);
                var invalidTests = runtimeTester.RunAll().Where(t => !t.IsValid).ToArray();
                if (invalidTests.Any())
                {
                    var testText = string.Join("\n", invalidTests.Select(t => $"{t.Test.GetType().Name}: {t.Message}"));
                    PosApplication.MessagingService.SendNotificationMessage("Debug Runtime Test", "The following runtime tests resulted in errors:\n" + testText + "\n\n" + "<b color='#f00000'>Please verify your latest code changes and find out why the runtime tests stopped working.</b>");
                }
            }

            //Call the base class method
            base.OnStartup(e);

            
        }        

        private void SetWebBrowserIEMode()
        {
            var pricipal = new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent());
            if (pricipal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
            {
                RegistryKey registrybrowser = Registry.LocalMachine.OpenSubKey
                    (@"Software\\Microsoft\\Internet Explorer\\Main\\FeatureControl\\FEATURE_BROWSER_EMULATION", true);
                string myProgramName = Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                var currentValue = registrybrowser.GetValue(myProgramName);
                if (currentValue == null || (int)currentValue != 0x00002af9)
                    registrybrowser.SetValue(myProgramName, 0x00002af9, RegistryValueKind.DWord);
            }
        }

        private bool CheckForOtherApplicationInstances()
        {
            // Get Reference to the current Process
            var thisProc = Process.GetCurrentProcess();

            // Check how many total processes have the same name as the current one
            var matchingProcesses = Process.GetProcessesByName(thisProc.ProcessName).Where(p => p.Id != thisProc.Id).ToArray();
            if (matchingProcesses.Any())
            {
                //Find the oldest process and activate it
                var mainProc = GetMainProc(matchingProcesses);
                if (mainProc != null)
                {
                    var foregroundWindowSet = NativeMethods.SetForegroundWindow(mainProc.MainWindowHandle);
                    if (!foregroundWindowSet)
                    {
                        KillProcesses(matchingProcesses);
                        return false;
                    }
                }

                this.Shutdown(DuplicateInstanceShutdownExitCode);
                return true;
            }
            return false;
        }

        private static Process GetMainProc(Process[] matchingProcesses)
        {
            try
            {
                return matchingProcesses.OrderByDescending(p => p.StartTime).FirstOrDefault();
            }
            catch (Exception ex)
            {
                MessageBox.Show("CloudRetailer POS is opened by a different user. There can be only one instance of CloudRetailer running on a given machine.", "CloudRetailer POS", MessageBoxButton.OK, MessageBoxImage.Stop);
                return null;
            }
            
        }

        private void KillProcesses(Process[] processes)
        {
            foreach (var process in processes)
            {
                process.Kill();
            }
        }

        private void SpawnSplashScreen()
        {
            //Create a new Thread to show the SplashScreen on, including its own Dispatcher
            var splashScreenThread = new Thread(() =>
                {
                    this.SplashScreen = new SplashScreenView(this);
                    this.SplashScreen.Show();
                    System.Windows.Threading.Dispatcher.Run();
                });
            splashScreenThread.IsBackground = true;
            splashScreenThread.Name = "CrPosSplashScreenThread";
            splashScreenThread.SetApartmentState(ApartmentState.STA);
            splashScreenThread.Start();
        }

        private IPosApplicationContainer ConfigureApplicationContainer()
        {
            try
            {
                var container = new PosApplicationContainer(PosApplicationContainer.CreateCompositionContainer(Messenger));
                container.Dispatcher = this.Dispatcher;
                container.AddExport<IPosApplicationContainer>(container);
                this.PosApplication = container.RetrieveExportedValue<IPosApplication>();

                this.MessagingService.SendPosStartupMessage("Creating Application Context...");
                this.RegisterMessageListeners();

                PosApplication.PostInit(CommandLineArgs);

                container.RetrieveExportedValue<IPosDeviceManagerService>();
                this.CustomerDisplayService = container.RetrieveExportedValue<ICustomerDisplayService>();
                container.RetrieveExportedValue<IPosViewService>();

                //Log the start of the application
                this.MessagingService.SendPosStartupMessage("Starting Application...");
                SetDynamicResource("ButtonIsFocusable", PosApplication.RetrieveConfiguredValue<bool>(PosConfigurationOptions.FocusableButtons));
                return container;
            }
            catch (CompositionException ex)
            {
                this.PromptUser(GetDetails(ex));
                this.Shutdown();
                return null;
            }
        }

        private string GetDetails(Exception ex)
        {
            var cex = ex as CompositionException;
            if (cex != null)
            {
                return string.Join("\n\n", cex.Errors.Select(e => GetDetails(e.Exception)));
            }

            var cpex = ex as ComposablePartException;
            if (cpex != null)
            {
                return GetDetails(cpex.InnerException) + $"\n(reported by {cpex.Element.DisplayName})";
            }

            var sqlex = ex as SqlException;
            if (sqlex != null)
            {
                return sqlex.Message;
            }

            return ex?.ToString();
        }

        private void SetDynamicResource(string name, object value)
        {
            Application.Current.Resources[name] = value;
        }

        private void RegisterMessageListeners()
        {
            LoggingHelper.RegisterMessaging(PosApplication, this, fileTraceSwitch.Level, dbTraceSwitch.Level);
            
            MessagingService.RegisterListener<ApplicationShutdownMessage>(this, (message) =>
            {
                try
                {
                    MessagingService.SendLogMessage("Pos application closed.", CloudRetailerTraceLevel.Info);
                    this.Shutdown(message.ExitCode);
                }
                catch (Exception)
                {
                }

                try
                {
                    Environment.Exit(message.ExitCode);
                }
                catch (Exception)
                {
                }
            });

            this.MessagingService.RegisterListener<NotificationMessage>(this, (message) =>
            {
                Ensure.NotNull(message, () => message);
                var listener = new ListenerHelper(MessagingService, ViewModelService);
                listener.RegisterNotification(message);
            });

            this.MessagingService.RegisterListener<UserInputMessage>(this, (message) =>
            {
                Ensure.NotNull(message, () => message);
                var listener = new ListenerHelper(MessagingService, ViewModelService);
                listener.RegisterUserInput(message);
            });
        }

        private void ConfigureAndDisplayMainView()
        {
            
            //Cache the ApplicationShell export as the MainWindow
            this.MainWindow = Container.RetrieveExportedValue<Window>("ApplicationShell");
            //Finish configuring MainWindow and show
            this.MainWindow.DataContext = this.PosApplication.ApplicationViewModel;
            ChangeShutdownMode(Current);
            Current.MainWindow = MainWindow;
            //Perform authentication
            


           MainWindow.Loaded += (sender, e) =>
                {
                    if (this.SplashScreen != null)
                    {
                        this.SplashScreen.Close();
                    }
                };

            MainWindow.Show();
            var beforeMainViewLoadMessage = new PosBeforeMainViewLoadEventData(PosApplication, Current);
            MessagingService.SendPosEventMessage(beforeMainViewLoadMessage);
            SystemEvents.DisplaySettingsChanged += FixWin8SizeChanged;
            if (beforeMainViewLoadMessage.PreventSignIn)
            {
                return;
            }

            PosApplication.CommandService.ExecuteCommand(CorePosCommands.ShowSignInViewCommand);
        }

        private void FixWin8SizeChanged(object sender, EventArgs e)
        {
            MainWindow.WindowState = WindowState.Normal;

            // these values are quite weird, height is bigger in one - weight in the other
            var height = Math.Max(SystemParameters.MaximizedPrimaryScreenHeight, SystemParameters.PrimaryScreenHeight);
            var width = Math.Max(SystemParameters.MaximizedPrimaryScreenWidth, SystemParameters.PrimaryScreenWidth);

            MainWindow.Height = height;
            MainWindow.Width = width;
            MainWindow.MaxHeight = height;
            MainWindow.MaxWidth = width;
            MainWindow.MinHeight = height - 50;
            MainWindow.MinWidth = width - 50;

            MainWindow.WindowState = WindowState.Maximized;
        }

        private void ChangeShutdownMode(Application current)
        {
            try
            {
                current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            catch (Exception ex)
            {
                //Intentionally left blank
            }
        }

        private void PromptUser(string message)
        {
            //If MessagingService is available, send a Notification message
            if (this.MessagingService != null)
            {
                this.MessagingService.SendNotificationMessage(this.PosApplication.ApplicationInfo.Name, message, NotificationMessageType.Error);
            }
            else
            {   //Otherwise show a plain MessageBox
                var strTitle = (this.PosApplication != null ? this.PosApplication.ApplicationInfo.Name : "Cloud Retailer POS");
                MessageBox.Show(message, strTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private readonly static CloudRetailerTraceSwitch fileTraceSwitch = new CloudRetailerTraceSwitch("CloudRetailerFileLoggingSwitch", "2");
        private readonly static CloudRetailerTraceSwitch dbTraceSwitch = new CloudRetailerTraceSwitch("CloudRetailerDatabaseLoggingSwitch", "3");

        private IPosApplication PosApplication { get; set; }

        private IPosMessagingService MessagingService => PosApplication?.MessagingService;

        private IPosViewModelService ViewModelService => PosApplication?.ViewModelService;

        private ISplashScreen SplashScreen { get; set; }

        protected ICustomerDisplayService CustomerDisplayService { get; set; }

        public IPosApplicationContainer Container
        {
            get
            {
                EnsureContainerExists();
                return container;
            }
        }

        private void EnsureContainerExists()
        {
            if (container == null)
            {
                container = ConfigureApplicationContainer();
            }
        }

        internal EventMessenger Messenger => messenger ?? (messenger = new EventMessenger());
    }
}