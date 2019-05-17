﻿using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Acr.UserDialogs;
using MvvmCross.Core.ViewModels;
using MvvmCross.Platform;

using System.Windows.Input;
using Xamarin.Forms;


using Plugin.BLE.Abstractions.Contracts;

using Plugin.BLE.Abstractions;
using Plugin.BLE.Abstractions.Extensions;

using Prism.Mvvm;

using Plugin.Share;
using Plugin.Share.Abstractions;

namespace BLE.Client.ViewModels
{
    public class ViewModelRFMicroInventory : BaseViewModel
    {
        public class RFMicroTagInfoViewModel : BindableBase
        {
            private string _EPC;
            public string EPC { get { return this._EPC; } set { this.SetProperty(ref this._EPC, value); } }

            private string _NickName;
            public string NickName { get { return this._NickName; } set { this.SetProperty(ref this._NickName, value); } }

            private string _DisplayName;
            public string DisplayName { get { return this._DisplayName; } set { this.SetProperty(ref this._DisplayName, value); } }

            private uint _OCRSSI;
            public uint OCRSSI { get { return this._OCRSSI; } set { this.SetProperty(ref this._OCRSSI, value); } }

            private string _GOODOCRSSI;
            public string GOODOCRSSI { get { return this._GOODOCRSSI; } set { this.SetProperty(ref this._GOODOCRSSI, value); } }

            public double _sensorValueSum;
            private string _sensorAvgValue;
            public string SensorAvgValue { get { return this._sensorAvgValue; } set { this.SetProperty(ref this._sensorAvgValue, value); } }

            private uint _sucessCount;
            public uint SucessCount { get { return this._sucessCount; } set { this.SetProperty(ref this._sucessCount, value); } }

            private string _RSSIColor;
            public string RSSIColor { get { return this._RSSIColor; } set { this.SetProperty(ref this._RSSIColor, value); } }

            private string _valueColor;
            public string valueColor { get { return this._valueColor; } set { this.SetProperty(ref this._valueColor, value); } }

            public RFMicroTagInfoViewModel()
            {
            }
        }

        private readonly IUserDialogs _userDialogs;

        #region -------------- RFID inventory -----------------

        public ICommand OnStartInventoryButtonCommand { protected set; get; }
        public ICommand OnClearButtonCommand { protected set; get; }
        public ICommand OnShareDataCommand { protected set; get; }
        

        private ObservableCollection<RFMicroTagInfoViewModel> _TagInfoList = new ObservableCollection<RFMicroTagInfoViewModel>();
        public ObservableCollection<RFMicroTagInfoViewModel> TagInfoList { get { return _TagInfoList; } set { SetProperty(ref _TagInfoList, value); } }

        public string SensorValueTitle { get {
                switch (BleMvxApplication._rfMicro_SensorUnit)
                {
                    case 0:
                        return "code";
                    case 1:
                        return "ºF";
                    case 2:
                        return "ºC";
                    case 3:
                        return "%";
                    case 4:
                        return "RAW";
                    case 5:
                        return "H";
                }
                return "Value";
            }
        }

        private string _startInventoryButtonText = "Start Inventory";
        public string startInventoryButtonText { get { return _startInventoryButtonText; } }

        bool _tagCount = false;

        private string _tagPerSecondText = "0 tags/s";
        public string tagPerSecondText { get { return _tagPerSecondText; } }
        private string _numberOfTagsText = "0 tags";
        public string numberOfTagsText { get { return _numberOfTagsText; } }
        private string _labelVoltage = "";
        public string labelVoltage { get { return _labelVoltage; } }

        private int _ListViewRowHeight = -1;
        public int ListViewRowHeight { get { return _ListViewRowHeight; } set { _ListViewRowHeight = value; } }

        public bool _startInventory = true;

        public int tagsCount = 0;
        int _tagCountForAlert = 0;
        bool _newTagFound = false;

        DateTime InventoryStartTime;
        private double _InventoryTime = 0;
        public string InventoryTime { get { return ((uint)_InventoryTime).ToString() + "s"; } }

        private string _currentPower;
        public string currentPower { get { return _currentPower; } set { _currentPower = value; } }

        private int _DefaultRowHight;

        bool _cancelVoltageValue = false;

        #endregion

        public ViewModelRFMicroInventory(IAdapter adapter, IUserDialogs userDialogs) : base(adapter)
        {
            _userDialogs = userDialogs;

            RaisePropertyChanged(() => ListViewRowHeight);
            _DefaultRowHight = ListViewRowHeight;

            OnStartInventoryButtonCommand = new Command(StartInventoryClick);
            OnClearButtonCommand = new Command(ClearClick);
            OnShareDataCommand = new Command(ShareDataButtonClick);

            RaisePropertyChanged(() => SensorValueTitle);

            SetPowerString();
        }

        ~ViewModelRFMicroInventory()
        {
        }

        public override void Resume()
        {
            base.Resume();

            // RFID event handler
            BleMvxApplication._reader.rfid.OnAsyncCallback += new EventHandler<CSLibrary.Events.OnAsyncCallbackEventArgs>(TagInventoryEvent);

            // Key Button event handler
            BleMvxApplication._reader.notification.OnKeyEvent += new EventHandler<CSLibrary.Notification.HotKeyEventArgs>(HotKeys_OnKeyEvent);
            BleMvxApplication._reader.notification.OnVoltageEvent += new EventHandler<CSLibrary.Notification.VoltageEventArgs>(VoltageEvent);

            InventorySetting();
        }

        public override void Suspend()
        {
            BleMvxApplication._reader.rfid.CancelAllSelectCriteria();                // Confirm cancel all filter

            BleMvxApplication._reader.rfid.StopOperation();
            ClassBattery.SetBatteryMode(ClassBattery.BATTERYMODE.IDLE);
            BleMvxApplication._reader.barcode.Stop();

            // Cancel RFID event handler
            BleMvxApplication._reader.rfid.OnAsyncCallback -= new EventHandler<CSLibrary.Events.OnAsyncCallbackEventArgs>(TagInventoryEvent);
            BleMvxApplication._reader.rfid.OnStateChanged += new EventHandler<CSLibrary.Events.OnStateChangedEventArgs>(StateChangedEvent);

            // Key Button event handler
            BleMvxApplication._reader.notification.OnKeyEvent -= new EventHandler<CSLibrary.Notification.HotKeyEventArgs>(HotKeys_OnKeyEvent);
            BleMvxApplication._reader.notification.OnVoltageEvent -= new EventHandler<CSLibrary.Notification.VoltageEventArgs>(VoltageEvent);

            base.Suspend();
        }

        protected override void InitFromBundle(IMvxBundle parameters)
        {
            base.InitFromBundle(parameters);
        }

        private void ClearClick()
        {
            InvokeOnMainThread(() =>
            {
                lock (TagInfoList)
                {
                    TagInfoList.Clear();
                    _numberOfTagsText = _TagInfoList.Count.ToString() + " tags";
                    RaisePropertyChanged(() => numberOfTagsText);

                    tagsCount = 0;
                    _tagPerSecondText = tagsCount.ToString() + " tags/s";
                    RaisePropertyChanged(() => tagPerSecondText);
                }
            });
        }

        //private TagInfoViewModel _ItemSelected;
        public RFMicroTagInfoViewModel objItemSelected
        {
            set
            {
                BleMvxApplication._SELECT_EPC = value.EPC;
                ShowViewModel<ViewModelRFMicroReadTemp>(new MvxBundle());
            }
        }

        void InventorySetting()
        {
            switch (BleMvxApplication._config.RFID_FrequenceSwitch)
            {
                case 0:
                    BleMvxApplication._reader.rfid.SetHoppingChannels(BleMvxApplication._config.RFID_Region);
                    break;
                case 1:
                    BleMvxApplication._reader.rfid.SetFixedChannel(BleMvxApplication._config.RFID_Region, BleMvxApplication._config.RFID_FixedChannel);
                    break;
                case 2:
                    BleMvxApplication._reader.rfid.SetAgileChannels(BleMvxApplication._config.RFID_Region);
                    break;
            }

            BleMvxApplication._reader.rfid.Options.TagRanging.flags = CSLibrary.Constants.SelectFlags.ZERO;

            // Setting 1
            BleMvxApplication._reader.rfid.SetInventoryDuration((uint)BleMvxApplication._config.RFID_DWellTime);
            BleMvxApplication._reader.rfid.SetPowerLevel((uint)BleMvxApplication._config.RFID_Power);

            // Setting 3  // MUST SET for RFMicro
            BleMvxApplication._config.RFID_DynamicQParms.retryCount = 5; // for RFMicro special setting
            BleMvxApplication._reader.rfid.SetDynamicQParms(BleMvxApplication._config.RFID_DynamicQParms);
            BleMvxApplication._config.RFID_DynamicQParms.retryCount = 0; // reset to normal

            // Setting 4
            BleMvxApplication._config.RFID_FixedQParms.retryCount = 5; // for RFMicro special setting
            BleMvxApplication._reader.rfid.SetFixedQParms(BleMvxApplication._config.RFID_FixedQParms);
            BleMvxApplication._config.RFID_FixedQParms.retryCount = 0; // reset to normal

            // Setting 2
            BleMvxApplication._reader.rfid.SetOperationMode(BleMvxApplication._config.RFID_OperationMode);
            BleMvxApplication._reader.rfid.SetTagGroup(CSLibrary.Constants.Selected.ASSERTED, CSLibrary.Constants.Session.S1, CSLibrary.Constants.SessionTarget.A);
            BleMvxApplication._reader.rfid.SetCurrentSingulationAlgorithm(BleMvxApplication._config.RFID_Algorithm);
            BleMvxApplication._reader.rfid.SetCurrentLinkProfile(BleMvxApplication._config.RFID_Profile);

            // Select RFMicro filter
            {
                CSLibrary.Structures.SelectCriterion extraSlecetion = new CSLibrary.Structures.SelectCriterion();

                switch (BleMvxApplication._rfMicro_TagType)
                {
                    case 0: // S2
                        extraSlecetion.action = new CSLibrary.Structures.SelectAction(CSLibrary.Constants.Target.SELECTED, CSLibrary.Constants.Action.ASLINVA_DSLINVB, 0);
                        extraSlecetion.mask = new CSLibrary.Structures.SelectMask(CSLibrary.Constants.MemoryBank.TID, 0, 28, new byte[] { 0xe2, 0x82, 0x40, 0x20 });
                        BleMvxApplication._reader.rfid.SetSelectCriteria(0, extraSlecetion);

                        // OC RSSI
                        extraSlecetion.action = new CSLibrary.Structures.SelectAction(CSLibrary.Constants.Target.SELECTED, CSLibrary.Constants.Action.NOTHING_DSLINVB, 0);
                        extraSlecetion.mask = new CSLibrary.Structures.SelectMask(CSLibrary.Constants.MemoryBank.BANK3, 0xa0, 8, new byte[] { 0x20 });
                        BleMvxApplication._reader.rfid.SetSelectCriteria(1, extraSlecetion);

                        break;

                    default: // S3
                        extraSlecetion.action = new CSLibrary.Structures.SelectAction(CSLibrary.Constants.Target.SELECTED, CSLibrary.Constants.Action.ASLINVA_DSLINVB, 0);
                        extraSlecetion.mask = new CSLibrary.Structures.SelectMask(CSLibrary.Constants.MemoryBank.TID, 0, 28, new byte[] { 0xe2, 0x82, 0x40, 0x30 });
                        BleMvxApplication._reader.rfid.SetSelectCriteria(0, extraSlecetion);

                        // OC RSSI
                        extraSlecetion.action = new CSLibrary.Structures.SelectAction(CSLibrary.Constants.Target.SELECTED, CSLibrary.Constants.Action.NOTHING_DSLINVB, 0);
                        extraSlecetion.mask = new CSLibrary.Structures.SelectMask(CSLibrary.Constants.MemoryBank.BANK3, 0xd0, 8, new byte[] { 0x20 });
                        BleMvxApplication._reader.rfid.SetSelectCriteria(1, extraSlecetion);

                        // Temperature and Sensor code
                        extraSlecetion.action = new CSLibrary.Structures.SelectAction(CSLibrary.Constants.Target.SELECTED, CSLibrary.Constants.Action.NOTHING_DSLINVB, 0);
                        extraSlecetion.mask = new CSLibrary.Structures.SelectMask(CSLibrary.Constants.MemoryBank.BANK3, 0xe0, 0, new byte[] { 0x00 });
                        BleMvxApplication._reader.rfid.SetSelectCriteria(2, extraSlecetion);

                        break;
                }


                BleMvxApplication._reader.rfid.Options.TagRanging.flags |= CSLibrary.Constants.SelectFlags.SELECT;
            }

            // Multi bank inventory
            switch (BleMvxApplication._rfMicro_TagType)
            {
                case 0:
                    BleMvxApplication._reader.rfid.Options.TagRanging.multibanks = 2;
                    BleMvxApplication._reader.rfid.Options.TagRanging.bank1 = CSLibrary.Constants.MemoryBank.BANK0;
                    BleMvxApplication._reader.rfid.Options.TagRanging.offset1 = 11; // address B
                    BleMvxApplication._reader.rfid.Options.TagRanging.count1 = 1;
                    BleMvxApplication._reader.rfid.Options.TagRanging.bank2 = CSLibrary.Constants.MemoryBank.BANK0;
                    BleMvxApplication._reader.rfid.Options.TagRanging.offset2 = 13; // address D
                    BleMvxApplication._reader.rfid.Options.TagRanging.count2 = 1;
                    BleMvxApplication._reader.rfid.Options.TagRanging.compactmode = false;
                    break;

                default:
                    BleMvxApplication._reader.rfid.Options.TagRanging.multibanks = 2;
                    BleMvxApplication._reader.rfid.Options.TagRanging.bank1 = CSLibrary.Constants.MemoryBank.BANK0;
                    BleMvxApplication._reader.rfid.Options.TagRanging.offset1 = 12; // address C
                    BleMvxApplication._reader.rfid.Options.TagRanging.count1 = 3;
                    BleMvxApplication._reader.rfid.Options.TagRanging.bank2 = CSLibrary.Constants.MemoryBank.USER;
                    BleMvxApplication._reader.rfid.Options.TagRanging.offset2 = 8;
                    BleMvxApplication._reader.rfid.Options.TagRanging.count2 = 4;
                    BleMvxApplication._reader.rfid.Options.TagRanging.compactmode = false;
                    break;
            }

            BleMvxApplication._reader.rfid.StartOperation(CSLibrary.Constants.Operation.TAG_PRERANGING);
        }

        void SetPowerString ()
        {
            string[] _powerOptions = { "Low (16dBm)", "Mid (23dBm)", "High (30dBm)", "Auto ", "Follow System Setting" };

            if (BleMvxApplication._rfMicro_Power == 3)
                currentPower = "Auto " + _powerOptions[_powerRunning];
            else
                currentPower = _powerOptions[BleMvxApplication._rfMicro_Power];

            RaisePropertyChanged(() => currentPower);
        }

        void SetPower(int index)
        {
            switch (index)
            {
                case 0:
                    BleMvxApplication._reader.rfid.SetPowerLevel(160);
                    break;
                case 1:
                    BleMvxApplication._reader.rfid.SetPowerLevel(230);
                    break;
                case 2:
                    BleMvxApplication._reader.rfid.SetPowerLevel(300);
                    break;
                case 4:
                    BleMvxApplication._reader.rfid.SetPowerLevel((uint)BleMvxApplication._config.RFID_Power);
                    break;
            }
        }

        int _powerRunning = 0;
        void StartInventory()
        {
            if (_startInventory == false)
                return;

            if (BleMvxApplication._rfMicro_Power == 3)
                SetPower(_powerRunning);
            else
                SetPower(BleMvxApplication._rfMicro_Power);

            //TagInfoList.Clear();

            StartTagCount();
            //if (BleMvxApplication._config.RFID_OperationMode == CSLibrary.Constants.RadioOperationMode.CONTINUOUS)
            {
                _startInventory = false;
                _startInventoryButtonText = "Stop Inventory";
            }

            //_ListViewRowHeight = 40 + (int)(BleMvxApplication._reader.rfid.Options.TagRanging.multibanks * 10);
            //RaisePropertyChanged(() => ListViewRowHeight);

            InventoryStartTime = DateTime.Now;
            BleMvxApplication._reader.rfid.StartOperation(CSLibrary.Constants.Operation.TAG_EXERANGING);
            ClassBattery.SetBatteryMode(ClassBattery.BATTERYMODE.INVENTORY);
            _cancelVoltageValue = true;

            RaisePropertyChanged(() => startInventoryButtonText);
        }

        void StopInventory()
        {
            _startInventory = true;
            _startInventoryButtonText = "Start Inventory";

            _tagCount = false;
            BleMvxApplication._reader.rfid.StopOperation();
            RaisePropertyChanged(() => startInventoryButtonText);

            if (_powerRunning >= 2)
                _powerRunning = 0;
            else
                _powerRunning++;
            SetPowerString();

            //BleMvxApplication._reader.rfid.CancelAllSelectCriteria();                // Confirm cancel all filter
        }

        void StartInventoryClick()
        {
            if (_startInventory)
            {
                StartInventory();
            }
            else
            {
                StopInventory();
            }
        }

        void StartTagCount()
        {
            tagsCount = 0;
            _tagCount = true;

            // Create a timer that waits one second, then invokes every second.
            Xamarin.Forms.Device.StartTimer(TimeSpan.FromMilliseconds(1000), () =>
            {
                _InventoryTime = (DateTime.Now - InventoryStartTime).TotalSeconds;
                RaisePropertyChanged(() => InventoryTime);

                _tagCountForAlert = 0;

                _numberOfTagsText = _TagInfoList.Count.ToString() + " tags";
                RaisePropertyChanged(() => numberOfTagsText);

                _tagPerSecondText = tagsCount.ToString() + " tags/s";
                RaisePropertyChanged(() => tagPerSecondText);
                tagsCount = 0;

                if (_tagCount)
                    return true;

                return false;
            });
        }

        void StopInventoryClick()
        {
            BleMvxApplication._reader.rfid.StopOperation();
        }

        void TagInventoryEvent(object sender, CSLibrary.Events.OnAsyncCallbackEventArgs e)
        {
            if (e.type != CSLibrary.Constants.CallbackType.TAG_RANGING)
                return;

            InvokeOnMainThread(() =>
            {
                _tagCountForAlert++;
                if (_tagCountForAlert == 1)
                {
                    if (BleMvxApplication._config.RFID_InventoryAlertSound)
                    {
                        if (_newTagFound)
                            Xamarin.Forms.DependencyService.Get<ISystemSound>().SystemSound(3);
                        else
                            Xamarin.Forms.DependencyService.Get<ISystemSound>().SystemSound(2);
                        _newTagFound = false;
                    }
                }
                else if (_tagCountForAlert >= 5)
                    _tagCountForAlert = 0;

                AddOrUpdateTagData(e.info);
                tagsCount++;
            });
        }

        void StateChangedEvent(object sender, CSLibrary.Events.OnStateChangedEventArgs e)
        {
            //InvokeOnMainThread(() =>
            //{
            switch (e.state)
            {
                case CSLibrary.Constants.RFState.IDLE:
                    ClassBattery.SetBatteryMode(ClassBattery.BATTERYMODE.IDLE);
                    _cancelVoltageValue = true;
                    switch (BleMvxApplication._reader.rfid.LastMacErrorCode)
                    {
                        case 0x00:  // normal end
                            break;

                        case 0x0309:    // 
                            _userDialogs.Alert("Too near to metal, please move CS108 away from metal and start inventory again.");
                            break;

                        default:
                            _userDialogs.Alert("Mac error : 0x" + BleMvxApplication._reader.rfid.LastMacErrorCode.ToString("X4"));
                            break;
                    }
                    break;
            }
            //});
        }

        private void AddOrUpdateTagData(CSLibrary.Structures.TagCallbackInfo info)
        {
            InvokeOnMainThread(() =>
            {
                bool found = false;

                int cnt;

                lock (TagInfoList)
                {
                    UInt16 ocRSSI;
                    UInt16 sensorCode;
                    UInt16 temp;

                    switch (BleMvxApplication._rfMicro_TagType)
                    {
                        case 0:
                            sensorCode = (UInt16)(info.Bank1Data[0] & 0x1f); // address b
                            ocRSSI = info.Bank2Data[0];     // address d
                            temp = 0;                       // no data
                            break;

                        default:
                            sensorCode = (UInt16)(info.Bank1Data[0] & 0x1ff); // address c
                            ocRSSI = info.Bank1Data[1];     // address d
                            temp = info.Bank1Data[2];       // address e
                            break;
                    }

                    for (cnt = 0; cnt < TagInfoList.Count; cnt++)
                    {
                        if (TagInfoList[cnt].EPC == info.epc.ToString())
                        {
                            TagInfoList[cnt].OCRSSI = ocRSSI;
                            TagInfoList[cnt].RSSIColor = "Black";

                            if (ocRSSI >= BleMvxApplication._rfMicro_minOCRSSI && ocRSSI <= BleMvxApplication._rfMicro_maxOCRSSI)
                            {
                                TagInfoList[cnt].GOODOCRSSI = ocRSSI.ToString();

                                //BleMvxApplication._rfMicro_SensorType // 0 = Sensor code, 1 = Temp
                                //BleMvxApplication._rfMicro_SensorUnit // 0=code, 1=f, 2=c, 3=%

                                switch (BleMvxApplication._rfMicro_SensorType)
                                {
                                    case 0:
                                        if ((BleMvxApplication._rfMicro_TagType == 0) || (BleMvxApplication._rfMicro_TagType == 1 && sensorCode >= 5 && sensorCode <= 490))
                                        {
                                            double SensorAvgValue;
                                            TagInfoList[cnt].SucessCount++;

                                            switch (BleMvxApplication._rfMicro_SensorUnit)
                                            {
                                                case 0: // code
                                                    {
                                                        TagInfoList[cnt]._sensorValueSum += sensorCode;
                                                        SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2);
                                                        TagInfoList[cnt].SensorAvgValue = SensorAvgValue.ToString();
                                                    }
                                                    break;

                                                case 3: // %
                                                    {
                                                        if (BleMvxApplication._rfMicro_TagType == 0)
                                                            TagInfoList[cnt]._sensorValueSum += (double)sensorCode / 32 * 100;
                                                        else
                                                            TagInfoList[cnt]._sensorValueSum += (double)sensorCode / 512 * 100;
                                                        SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2);
                                                        TagInfoList[cnt].SensorAvgValue = SensorAvgValue.ToString();
                                                    }
                                                    break;

                                                case 4: // RAW
                                                    {
                                                        SensorAvgValue = sensorCode;
                                                        TagInfoList[cnt].SensorAvgValue = sensorCode.ToString();
                                                    }
                                                    break;

                                                default: // Range Allocation
                                                    {
                                                        SensorAvgValue = sensorCode;

                                                        if (sensorCode >= BleMvxApplication._rfMicro_MinWet && sensorCode <= BleMvxApplication._rfMicro_MaxWet)
                                                        {
                                                            TagInfoList[cnt].SensorAvgValue = "Wet";
                                                        }
                                                        else if (sensorCode >= BleMvxApplication._rfMicro_MinDamp && sensorCode <= BleMvxApplication._rfMicro_MaxDamp)
                                                        {
                                                            TagInfoList[cnt].SensorAvgValue = "Damp";
                                                        }
                                                        else if (sensorCode >= BleMvxApplication._rfMicro_MinDry && sensorCode <= BleMvxApplication._rfMicro_MaxDry)
                                                        {
                                                            TagInfoList[cnt].SensorAvgValue = "Dry";
                                                        }
                                                        else
                                                            TagInfoList[cnt].SensorAvgValue = "";
                                                    }
                                                    break;
                                            }

                                            if (TagInfoList[cnt].SucessCount >= 3)
                                            {
                                                switch (BleMvxApplication._rfMicro_thresholdComparison)
                                                {
                                                    case 0: // >
                                                        if (SensorAvgValue > BleMvxApplication._rfMicro_thresholdValue)
                                                            TagInfoList[cnt].valueColor = BleMvxApplication._rfMicro_thresholdColor;
                                                        else
                                                            TagInfoList[cnt].valueColor = "Green";
                                                        break;
                                                    default: // <
                                                        if (SensorAvgValue < BleMvxApplication._rfMicro_thresholdValue)
                                                            TagInfoList[cnt].valueColor = BleMvxApplication._rfMicro_thresholdColor;
                                                        else
                                                            TagInfoList[cnt].valueColor = "Green";
                                                        break;
                                                }
                                            }
                                        }
                                        break;

                                    default:
                                        if (temp >= 1300 && temp <= 3500)
                                        {
                                            double SensorAvgValue;
                                            TagInfoList[cnt].SucessCount++;
                                            UInt64 caldata = (UInt64)(((UInt64)info.Bank2Data[0] << 48) | ((UInt64)info.Bank2Data[1] << 32) | ((UInt64)info.Bank2Data[2] << 16) | ((UInt64)info.Bank2Data[3]));

                                            switch (BleMvxApplication._rfMicro_SensorUnit)
                                            {
                                                case 0: // code
                                                    TagInfoList[cnt]._sensorValueSum += temp;
                                                    SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2);
                                                    TagInfoList[cnt].SensorAvgValue = SensorAvgValue.ToString();
                                                    break;

                                                case 1: // F
                                                    TagInfoList[cnt]._sensorValueSum += getTempF(temp, caldata);
                                                    SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2);
                                                    TagInfoList[cnt].SensorAvgValue = SensorAvgValue.ToString();
                                                    break;

                                                default: // C
                                                    TagInfoList[cnt]._sensorValueSum += getTempC(temp, caldata);
                                                    SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2);
                                                    TagInfoList[cnt].SensorAvgValue = SensorAvgValue.ToString();
                                                    break;
                                            }

                                            if (TagInfoList[cnt].SucessCount >= 3)
                                            {
                                                switch (BleMvxApplication._rfMicro_thresholdComparison)
                                                {
                                                    case 0: // >
                                                        if (SensorAvgValue > BleMvxApplication._rfMicro_thresholdValue)
                                                            TagInfoList[cnt].valueColor = BleMvxApplication._rfMicro_thresholdColor;
                                                        else
                                                            TagInfoList[cnt].valueColor = "Green";
                                                        break;
                                                    default: // <
                                                        if (SensorAvgValue < BleMvxApplication._rfMicro_thresholdValue)
                                                            TagInfoList[cnt].valueColor = BleMvxApplication._rfMicro_thresholdColor;
                                                        else
                                                            TagInfoList[cnt].valueColor = "Green";
                                                        break;
                                                }
                                            }
                                        }
                                        break;
                                }

                                /*
                                if (BleMvxApplication._rfMicro_SensorUnit == 0)
                                {
                                    if (sensorCode >= 5 && sensorCode <= 490)
                                    {
                                        TagInfoList[cnt].SucessCount++;
                                        TagInfoList[cnt]._sensorValueSum += sensorCode;
                                        TagInfoList[cnt].SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2).ToString();
                                    }
                                }
                                else
                                {
                                    if (temp >= 1300 && temp <= 3500)
                                    {
                                        TagInfoList[cnt].SucessCount++;
                                        UInt64 caldata = (UInt64)(((UInt64)info.Bank2Data[0] << 48) | ((UInt64)info.Bank2Data[1] << 32) | ((UInt64)info.Bank2Data[2] << 16) | ((UInt64)info.Bank2Data[3]));
                                        TagInfoList[cnt]._sensorValueSum += getTemperatue(info.Bank1Data[2], caldata);
                                        TagInfoList[cnt].SensorAvgValue = Math.Round(TagInfoList[cnt]._sensorValueSum / TagInfoList[cnt].SucessCount, 2).ToString();
                                    }
                                }
                                */
                            }
                            else
                            {
                                TagInfoList[cnt].RSSIColor = "Red";
                            }

                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        RFMicroTagInfoViewModel item = new RFMicroTagInfoViewModel();

                        item.EPC = info.epc.ToString();
                        item.NickName = GetNickName(item.EPC);
                        if (item.NickName != "")
                            item.DisplayName = item.NickName;
                        else
                            item.DisplayName = item.EPC;
                        item.OCRSSI = ocRSSI;
                        item.SucessCount = 0;
                        item._sensorValueSum = 0;
                        item.SensorAvgValue = "";
                        item.GOODOCRSSI = "";
                        item.RSSIColor = "Black";
                        item.valueColor = "Black";

                        if (ocRSSI >= BleMvxApplication._rfMicro_minOCRSSI && ocRSSI <= BleMvxApplication._rfMicro_maxOCRSSI)
                        {
                            item.GOODOCRSSI = ocRSSI.ToString();

                            //BleMvxApplication._rfMicro_SensorType // 0 = Sensor code, 1 = Temp
                            //BleMvxApplication._rfMicro_SensorUnit //0=code, 1=f, 2=c, 3=%

                            switch (BleMvxApplication._rfMicro_SensorType)
                            {
                                case 0:
                                    if ((BleMvxApplication._rfMicro_TagType == 0) || (BleMvxApplication._rfMicro_TagType == 1 && sensorCode >= 5 && sensorCode <= 490))
                                    {
                                        item.SucessCount++;

                                        switch (BleMvxApplication._rfMicro_SensorUnit)
                                        {
                                            case 0: // code
                                                item._sensorValueSum = sensorCode;
                                                item.SensorAvgValue = item._sensorValueSum.ToString();
                                                break;

                                            case 3: // %
                                                if (BleMvxApplication._rfMicro_TagType == 0)
                                                    item._sensorValueSum = (double)sensorCode / 32 * 100;
                                                else
                                                    item._sensorValueSum = (double)sensorCode / 512 * 100;
                                                item.SensorAvgValue = item._sensorValueSum.ToString();
                                                break;

                                            case 4:
                                                item.SensorAvgValue = sensorCode.ToString();
                                                break;

                                            default:
                                                if (sensorCode >= BleMvxApplication._rfMicro_MinWet && sensorCode <= BleMvxApplication._rfMicro_MaxWet)
                                                {
                                                    item.SensorAvgValue = "Wet";
                                                }
                                                else if (sensorCode >= BleMvxApplication._rfMicro_MinDamp && sensorCode <= BleMvxApplication._rfMicro_MaxDamp)
                                                {
                                                    item.SensorAvgValue = "Damp";
                                                }
                                                else if (sensorCode >= BleMvxApplication._rfMicro_MinDry && sensorCode <= BleMvxApplication._rfMicro_MaxDry)
                                                {
                                                    item.SensorAvgValue = "Dry";
                                                }
                                                else
                                                    item.SensorAvgValue = "";

                                                break;
                                        }
                                    }
                                    break;

                                default:
                                    if (temp >= 1300 && temp <= 3500)
                                    {
                                        item.SucessCount++;
                                        UInt64 caldata = (UInt64)(((UInt64)info.Bank2Data[0] << 48) | ((UInt64)info.Bank2Data[1] << 32) | ((UInt64)info.Bank2Data[2] << 16) | ((UInt64)info.Bank2Data[3]));

                                        switch (BleMvxApplication._rfMicro_SensorUnit)
                                        {
                                            case 0: // code
                                                item._sensorValueSum = temp;
                                                item.SensorAvgValue = item._sensorValueSum.ToString();
                                                break;

                                            case 1: // F
                                                item._sensorValueSum = getTempF(temp, caldata);
                                                item.SensorAvgValue = Math.Round(item._sensorValueSum, 2).ToString();
                                                break;

                                            default: // C
                                                item._sensorValueSum = getTempC(temp, caldata);
                                                item.SensorAvgValue = Math.Round(item._sensorValueSum, 2).ToString();
                                                break;
                                        }
                                    }
                                    break;
                            }

                            /*
                            if (BleMvxApplication._rfMicro_SensorUnit == 0)
                            {
                                if (sensorCode >= 5 && sensorCode <= 490)
                                {
                                    item.SucessCount++;
                                    item._sensorValueSum = sensorCode;
                                    item.SensorAvgValue = item._sensorValueSum.ToString();
                                }
                            }
                            else
                            {
                                if (temp >= 1300 && temp <= 3500)
                                {
                                    item.SucessCount++;
                                    UInt64 caldata = (UInt64)(((UInt64)info.Bank2Data[0] << 48) | ((UInt64)info.Bank2Data[1] << 32) | ((UInt64)info.Bank2Data[2] << 16) | ((UInt64)info.Bank2Data[3]));
                                    item._sensorValueSum = getTemperatue(info.Bank1Data[2], caldata);
                                    item.SensorAvgValue = Math.Round(item._sensorValueSum, 2).ToString();
                                }
                            }*/

                        }
                        else
                        {
                            item.RSSIColor = "Red";
                        }

                        TagInfoList.Insert(0, item);

                        _newTagFound = true;

                        Trace.Message("EPC Data = {0}", item.EPC);
                    }
                }
            });
        }

        string GetNickName(string EPC)
        {
            for (int index = 0; index < ViewModelRFMicroNickname._TagNicknameList.Count; index++)
                if (ViewModelRFMicroNickname._TagNicknameList[index].EPC == EPC)
                    return ViewModelRFMicroNickname._TagNicknameList[index].Nickname;

            return "";
        }

        double getTempF(UInt16 temp, UInt64 CalCode)
        {
            return (getTemperatue(temp, CalCode) * 1.8 + 32.0);
        }

        double getTempC(UInt16 temp, UInt64 CalCode)
        {
            return getTemperatue(temp, CalCode);
        }

        double getTemperatue(UInt16 temp, UInt64 CalCode)
        {
            int crc = (int)(CalCode >> 48) & 0xffff;
            int calCode1 = (int)(CalCode >> 36) & 0x0fff;
            int calTemp1 = (int)(CalCode >> 25) & 0x07ff;
            int calCode2 = (int)(CalCode >> 13) & 0x0fff;
            int calTemp2 = (int)(CalCode >> 2) & 0x7FF;
            int calVer = (int)(CalCode & 0x03);

            double fTemperature = temp;
            fTemperature = ((double)calTemp2 - (double)calTemp1) * (fTemperature - (double)calCode1);
            fTemperature /= ((double)(calCode2) - (double)calCode1);
            fTemperature += (double)calTemp1;
            fTemperature -= 800;
            fTemperature /= 10;
            //textViewTemperatureCode.setText(accessResult.substring(0, 4) + (calVer != -1 ? ("(" + String.format("%.1f", fTemperature) + (char)0x00B0 + "C" + ")") : ""));

            return fTemperature;
        }

        void VoltageEvent(object sender, CSLibrary.Notification.VoltageEventArgs e)
		{
            if (e.Voltage == 0xffff)
            {
                _labelVoltage = "CS108 Bat. ERROR"; //			3.98v
            }
            else
            {
                // to fix CS108 voltage bug
                if (_cancelVoltageValue)
                {
                    _cancelVoltageValue = false;
                    return;
                }

                switch (BleMvxApplication._config.BatteryLevelIndicatorFormat)
                {
                    case 0:
                        _labelVoltage = "CS108 Bat. " + ((double)e.Voltage / 1000).ToString("0.000") + "v"; //			v
                        break;

                    default:
                        _labelVoltage = "CS108 Bat. " + ClassBattery.Voltage2Percent((double)e.Voltage / 1000).ToString("0") + "%"; //			%
                        break;
                }
            }

			RaisePropertyChanged(() => labelVoltage);
		}

        private void ShareDataButtonClick()
        {
            InvokeOnMainThread(() =>
            {
                string dataBase = "";

                lock (TagInfoList)
                {
                    for (int index = 0; index < TagInfoList.Count; index++)
                    {
                        dataBase += "\"" + TagInfoList[index].EPC + "\"," +
                                    "\"" + TagInfoList[index].NickName + "\"," +
                                    "\"" + ((BleMvxApplication._rfMicro_SensorType == 0) ? "Sensor code" : "Temperature") + "\"," +
                                    TagInfoList[index].SensorAvgValue + "," +
                                    "\"";
                        switch (BleMvxApplication._rfMicro_SensorUnit)
                        {
                            case 0:
                                dataBase += "code";
                                break;
                            case 1:
                                dataBase += "F";
                                break;
                            case 2:
                                dataBase += "C";
                                break;
                            case 3:
                                dataBase += "%";
                                break;
                        }
                        dataBase += "\"";
                        ;
                    }
                }

                var r = CrossShare.Current.Share(new Plugin.Share.Abstractions.ShareMessage
                {
                    Text = dataBase,
                    Title = "Axzon tags list"
                });

                CSLibrary.Debug.WriteLine("BackupData : {0}", r.ToString());
            });
        }

        #region Key_event

        void HotKeys_OnKeyEvent(object sender, CSLibrary.Notification.HotKeyEventArgs e)
        {
            if (e.KeyCode == CSLibrary.Notification.Key.BUTTON)
            {
                if (e.KeyDown)
                {
                    StartInventory();
                }
                else
                {
                    StopInventory();
                }
            }
        }
        #endregion

        async void ShowDialog(string Msg)
        {
            var config = new ProgressDialogConfig()
            {
                Title = Msg,
                IsDeterministic = true,
                MaskType = MaskType.Gradient,
            };

            using (var progress = _userDialogs.Progress(config))
            {
                progress.Show();
                await System.Threading.Tasks.Task.Delay(1000);
            }
        }
    }
}
