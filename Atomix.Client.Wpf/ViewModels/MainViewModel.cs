﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Atomix.MarketData;
using Atomix.MarketData.Abstract;
using Atomix.Subsystems;
using Atomix.Subsystems.Abstract;
using Atomix.Wallet.Abstract;
using Atomix.Client.Wpf.Common;
using Atomix.Client.Wpf.Controls;
using Atomix.Client.Wpf.ViewModels.Abstract;
using Atomix.Core.Entities;
using Atomix.Updates;
using MahApps.Metro.Controls.Dialogs;
using Serilog;
using System.IO;
using Atomix.Wallet;

namespace Atomix.Client.Wpf.ViewModels
{
    public class MainViewModel : BaseViewModel, IMenuSelector
    {
        public IAtomixApp AtomixApp { get; set; }
        public IDialogViewer DialogViewer { get; set; }
        public IMainView MainView { get; set; }
        public PortfolioViewModel PortfolioViewModel { get; set; }
        public WalletsViewModel WalletsViewModel { get; set; }
        public ConversionViewModel ConversionViewModel { get; set; }
        public ExchangeViewModel ExchangeViewModel { get; set; }
        public SettingsViewModel SettingsViewModel { get; set; }

        private int _selectedMenuIndex;
        public int SelectedMenuIndex
        {
            get => _selectedMenuIndex;
            set { _selectedMenuIndex = value; OnPropertyChanged(nameof(SelectedMenuIndex)); }
        }

        private string _installedVersion;
        public string InstalledVersion
        {
            get => _installedVersion;
            set { _installedVersion = value; OnPropertyChanged(nameof(InstalledVersion)); }
        }

        private bool _updatesReady;
        public bool UpdatesReady
        {
            get => _updatesReady;
            set { _updatesReady = value; OnPropertyChanged(nameof(UpdatesReady)); }
        }

        private bool _hasAccount;
        public bool HasAccount
        {
            get => _hasAccount;
            set { _hasAccount = value; OnPropertyChanged(nameof(HasAccount)); }
        }

        private bool _isLocked;
        public bool IsLocked
        {
            get => _isLocked;
            set { _isLocked = value; OnPropertyChanged(nameof(IsLocked)); }
        }

        private bool _isExchangeConnected;
        public bool IsExchangeConnected
        {
            get => _isExchangeConnected;
            set { _isExchangeConnected = value; OnPropertyChanged(nameof(IsExchangeConnected)); }
        }

        private bool _isMarketDataConnected;
        public bool IsMarketDataConnected
        {
            get => _isMarketDataConnected;
            set { _isMarketDataConnected = value; OnPropertyChanged(nameof(IsMarketDataConnected)); }
        }

        private bool _isQuotesProviderAvailable;
        public bool IsQuotesProviderAvailable
        {
            get => _isQuotesProviderAvailable;
            set { _isQuotesProviderAvailable = value; OnPropertyChanged(nameof(IsQuotesProviderAvailable)); }
        }

        private string _login;
        public string Login
        {
            get => _login;
            set { _login = value; OnPropertyChanged(nameof(Login)); }
        }

        public MainViewModel()
        {
#if DEBUG
            if (Env.IsInDesignerMode())
                DesignerMode();
#endif
        }

        public MainViewModel(IAtomixApp app, IDialogViewer dialogViewer, IMainView mainView = null)
        {
            AtomixApp = app ?? throw new ArgumentNullException(nameof(app));
            DialogViewer = dialogViewer ?? throw new ArgumentNullException(nameof(dialogViewer));

            PortfolioViewModel = new PortfolioViewModel(AtomixApp);
            ConversionViewModel = new ConversionViewModel(AtomixApp, DialogViewer);
            WalletsViewModel = new WalletsViewModel(AtomixApp, DialogViewer, this, ConversionViewModel);
            //ExchangeViewModel = new ExchangeViewModel(AtomixApp);
            SettingsViewModel = new SettingsViewModel(AtomixApp, DialogViewer);

            InstalledVersion = App.Updater.InstalledVersion.ToString();

            SubscribeToServices();
            SubscribeToUpdates(App.Updater);

            if (mainView != null)
            {
                MainView = mainView;
                MainView.MainViewClosing += (sender, args) => Closing(args);
                MainView.Inactivity += InactivityHandler;
            }
        }

        public void SelectMenu(int index)
        {
            SelectedMenuIndex = index;
        }

        private void SubscribeToUpdates(Updater updater)
        {
            updater.UpdatesReady += OnUpdatesReadyEventHandler;
        }

        private void OnUpdatesReadyEventHandler(object sender, ReadyEventArgs e)
        {
            UpdatesReady = true;
        }

        private void SubscribeToServices()
        {
            AtomixApp.AccountChanged += OnAccountChangedEventHandler;

            AtomixApp.Terminal.ServiceConnected += OnTerminalServiceStateChangedEventHandler;
            AtomixApp.Terminal.ServiceDisconnected += OnTerminalServiceStateChangedEventHandler;

            AtomixApp.QuotesProvider.AvailabilityChanged += OnQuotesProviderAvailabilityChangedEventHandler;
        }

        private void OnAccountChangedEventHandler(object sender, AccountChangedEventArgs args)
        {
            if (args.OldAccount != null)
            {
                args.OldAccount.Locked -= OnAccountLockChangedEventHandler;
                args.OldAccount.Unlocked -= OnAccountLockChangedEventHandler;
            }

            var account = args.NewAccount;

            if (account == null) {
                HasAccount = false;
                MainView?.StopInactivityControl();
                return;
            }

            account.Locked += OnAccountLockChangedEventHandler;
            account.Unlocked += OnAccountLockChangedEventHandler;

            IsLocked = account.IsLocked;
            HasAccount = true;

            // auto sign out after timeout
            if (MainView != null && account.UserSettings.AutoSignOut)
                MainView.StartInactivityControl(TimeSpan.FromMinutes(account.UserSettings.PeriodOfInactivityInMin));
        }

        private void OnTerminalServiceStateChangedEventHandler(object sender, TerminalServiceEventArgs args)
        {
            if (!(sender is ITerminal terminal))
                return;       

            IsExchangeConnected = terminal.IsServiceConnected(TerminalService.Exchange);
            IsMarketDataConnected = terminal.IsServiceConnected(TerminalService.MarketData);

            // subscribe to symbols updates
            if (args.Service == TerminalService.MarketData && IsMarketDataConnected)
            {
                terminal.SubscribeToMarketData(SubscriptionType.TopOfBook);
                terminal.SubscribeToMarketData(SubscriptionType.DepthTwenty);
            }
        }

        private void OnQuotesProviderAvailabilityChangedEventHandler(object sender, EventArgs args)
        {
            if (!(sender is ICurrencyQuotesProvider provider))
                return;

            IsQuotesProviderAvailable = provider.IsAvailable;
        }

        private void OnAccountLockChangedEventHandler(object sender, EventArgs args)
        {
            if (!(sender is IAccount account))
                return;

            IsLocked = account.IsLocked;
        }

        private ICommand _updateCommand;
        public ICommand UpdateCommand => _updateCommand ?? (_updateCommand = new Command(OnUpdateClick));

        private void OnUpdateClick()
        {
            Application.Current.Shutdown(101);
        }

        private ICommand _signOutCommand;
        public ICommand SignOutCommand => _signOutCommand ?? (_signOutCommand = new Command(SignOut));

        private async void SignOut()
        {
            try
            {
                if (await WhetherToCancelClosingAsync())
                    return;

                DialogViewer.HideAllDialogs();

                AtomixApp.Account.Lock();

                AtomixApp.UseAccount(
                    account: null,
                    restartTerminal: true);

                DialogViewer.ShowStartDialog(new StartViewModel(AtomixApp, DialogViewer));
            }
            catch (Exception e)
            {
                Log.Error(e, "Sign Out error");
            }
        }

        private bool _forcedClose;

        private async void Closing(CancelEventArgs args)
        {
            if (AtomixApp.Account == null || _forcedClose)
                return;

            args.Cancel = true;

            try
            {
                await Task.Yield();

                var cancel = await WhetherToCancelClosingAsync();

                if (!cancel)
                {
                    _forcedClose = true;
                    MainView.Close();
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Closing error");
            }
        }

        private async Task<bool> HasActiveSwapsAsync()
        {
            var swaps = await AtomixApp.Account
                .GetSwapsAsync();

            return swaps.Any(swap =>
                !swap.IsComplete &&
                !swap.IsRefunded &&
                !swap.IsCanceled &&
                swap.StateFlags.HasFlag(SwapStateFlags.IsPaymentBroadcast));
        }

        private async Task<bool> WhetherToCancelClosingAsync()
        {
            if (!AtomixApp.Account.UserSettings.ShowActiveSwapWarning)
                return false;

            var hasActiveSwaps = await HasActiveSwapsAsync();

            if (!hasActiveSwaps)
                return false;

            var result = await DialogViewer
                .ShowMessageAsync(
                    title: "Warning",
                    message: "You have active swaps. Closing the application or sign out may " +
                             "result in loss of funds as a result of the failore of the " +
                             "refund operation. Are you sure you want to close the application?",
                    style: MessageDialogStyle.AffirmativeAndNegative);

            return result == MessageDialogResult.Negative;
        }

        private void InactivityHandler(object sender, EventArgs args)
        {
            if (AtomixApp?.Account == null)
                return;

            var pathToAccount = AtomixApp.Account.Wallet.PathToWallet;
            var accountDirectory = Path.GetDirectoryName(pathToAccount);

            if (accountDirectory == null)
                return;

            var accountName = new DirectoryInfo(accountDirectory).Name;

            var unlockViewModel = new UnlockViewModel(accountName, password =>
            {
                var _ = Account.LoadFromFile(
                    pathToAccount: pathToAccount,
                    password: password,
                    currenciesProvider: AtomixApp.CurrenciesProvider,
                    symbolsProvider: AtomixApp.SymbolsProvider);
            });

            unlockViewModel.Unlocked += (s, a) =>
            {
                DialogViewer?.HideUnlockDialog();
            };

            DialogViewer?.ShowUnlockDialog(unlockViewModel, (s, a) =>
            {
                SignOut();
            });
        }

        private void DesignerMode()
        {
            HasAccount = true;
        }
    }
}