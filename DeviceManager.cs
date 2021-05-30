﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Melanchall.DryWetMidi.Devices;

namespace MidiBard
{
	static class DeviceManager
	{
		internal static string ToDeviceString(this InputDevice device)
		{
			if (device is null)
			{
				return "None";
			}
			return device.Name;
		}

		internal static InputDevice CurrentInputDevice { get; set; }

		internal static IEnumerable<InputDevice> Devices
		{
			get
			{
				var alldevice = InputDevice.GetAll();
				return alldevice.Prepend(null);
			}
		}

		internal static void SetDevice(InputDevice device)
		{
			DisposeDevice();
			if (device is null) return;
			CurrentInputDevice = device;
			CurrentInputDevice.EventReceived += InputDevice_EventReceived;
			CurrentInputDevice.StartEventsListening();
		}

		internal static void DisposeDevice()
		{
			try
			{
				CurrentInputDevice.EventReceived -= InputDevice_EventReceived;
				CurrentInputDevice.Reset();
			}
			catch (Exception e)
			{
				PluginLog.Debug($"possible null inputDevice. {e}");
			}
			finally
			{
				CurrentInputDevice?.Dispose();
				CurrentInputDevice = null;
			}
		}

		private static void InputDevice_EventReceived(object sender, MidiEventReceivedEventArgs e)
		{
			Plugin.CurrentOutputDevice.SendEvent(e.Event);
		}
	}
}
