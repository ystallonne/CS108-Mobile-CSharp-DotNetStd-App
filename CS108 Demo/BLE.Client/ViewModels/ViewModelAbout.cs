﻿using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Acr.UserDialogs;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform;

using System.Windows.Input;
using Xamarin.Forms;

using Plugin.BLE.Abstractions.Contracts;

namespace BLE.Client.ViewModels
{
    public class ViewModelAbout : BaseViewModel
    {
        private readonly IUserDialogs _userDialogs;

        public ViewModelAbout (IAdapter adapter, IUserDialogs userDialogs) : base(adapter)
        {
            _userDialogs = userDialogs;
        }
    }
}
