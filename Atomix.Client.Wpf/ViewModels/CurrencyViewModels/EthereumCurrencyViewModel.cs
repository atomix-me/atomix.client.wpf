﻿using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Atomix.Client.Wpf.Properties;
using Atomix.Client.Wpf.ViewModels.Abstract;
using Atomix.Core.Entities;

namespace Atomix.Client.Wpf.ViewModels.CurrencyViewModels
{
    public class EthereumCurrencyViewModel : CurrencyViewModel
    {
        public EthereumCurrencyViewModel(Currency currency)
            : base(currency)
        {
            Header = Currency.Description;
            IconBrush = new ImageBrush(new BitmapImage(new Uri(PathToImage("ethereum.png"))));
            IconMaskBrush = new ImageBrush(new BitmapImage(new Uri(PathToImage("ethereum_mask.png"))));
            AccentColor = Color.FromRgb(r: 73, g: 114, b: 143);
            AmountColor = Color.FromRgb(r: 183, g: 208, b: 225);
            UnselectedIconBrush = Brushes.White;
            IconPath = PathToImage("ethereum.png");
            LargeIconPath = PathToImage("ethereum_90x90.png");
            FeeName = Resources.SvGasLimit;
        }
    }
}