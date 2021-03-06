﻿// TODO : Customer friendly (C) notice required
// BME280 sensor - Temperature, Humidity and Air pressure
// https://github.com/gloveboxes/Windows-IoT-Core-Driver-Library
//
// Need to add a NuGet reference to Units.net V3.34 @ April 2019
//
// Grove BME280 Sensor in I2C1 (3V3)
// https://www.seeedstudio.com/Grove-Temp-Humi-Barometer-Sensor-BME280.html
//
namespace AzureIoTHubClientPropertiesEnd
{
	using System;
	using System.Diagnostics;
	using System.Text;
	using System.Threading;

	using Microsoft.Azure.Devices.Client;
	using Microsoft.Azure.Devices.Shared;
	using Windows.ApplicationModel;
	using Windows.ApplicationModel.Background;
	using Windows.Storage.Streams;
	using Windows.System;
	using Windows.System.Profile;

	using Glovebox.IoT.Devices.Sensors;
	using Newtonsoft.Json;

	public sealed class StartupTask : IBackgroundTask
	{
		private const string AzureIoTHubConnectionString = "HostName=Build2019Test.azure-devices.net;DeviceId=DragonBoard410C;SharedAccessKey=ewbUCMtd6Blau9vaQBqO/J6GlSxgbxPM5aWRgZz6N7c=";
		private readonly TimeSpan timerDue = new TimeSpan(0, 0, 10);
		private readonly TimeSpan timerPeriod = new TimeSpan(0, 0, 30);
		private BackgroundTaskDeferral backgroundTaskDeferral = null;
		private BME280 bme280Sensor;
		private Timer bme280InputPollingTimer;
		private DeviceClient azureIoTHubClient = null;

		public void Run(IBackgroundTaskInstance taskInstance)
		{
			try
			{
				bme280Sensor = new BME280(0x76);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"BME280 Initialisation failed {ex.Message}");

				return;
			}

			try
			{
				azureIoTHubClient = DeviceClient.CreateFromConnectionString(AzureIoTHubConnectionString, TransportType.Mqtt);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"CreateFromConnectionString failed {ex.Message}");
				return;
			}

			try
			{
				TwinCollection reportedProperties;
				reportedProperties = new TwinCollection();

				// This is from the OS 
				reportedProperties["Timezone"] = TimeZoneSettings.CurrentTimeZoneDisplayName;
				reportedProperties["OSVersion"] = Environment.OSVersion.VersionString;
				reportedProperties["MachineName"] = Environment.MachineName;

				// This is from the application manifest 
				Package package = Package.Current;
				PackageId packageId = package.Id;
				PackageVersion version = packageId.Version;
				reportedProperties["ApplicationDisplayName"] = package.DisplayName;
				reportedProperties["ApplicationName"] = packageId.Name;
				reportedProperties["ApplicationVersion"] = string.Format($"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}");

				// Unique identifier from the hardware
				SystemIdentificationInfo systemIdentificationInfo = SystemIdentification.GetSystemIdForPublisher();
				using (DataReader reader = DataReader.FromBuffer(systemIdentificationInfo.Id))
				{
					byte[] bytes = new byte[systemIdentificationInfo.Id.Length];
					reader.ReadBytes(bytes);
					reportedProperties["SystemId"] = BitConverter.ToString(bytes);
				}

				azureIoTHubClient.UpdateReportedPropertiesAsync(reportedProperties).Wait();
			}
			catch (Exception ex)
			{
				Debug.Print($"Azure IoT Hub device twin configuration retrieval failed:{ex.Message}");
				return;
			}

			bme280InputPollingTimer = new Timer(SensorUpdateTimerCallback, null, timerDue, timerPeriod);

			backgroundTaskDeferral = taskInstance.GetDeferral();
		}

		private async void SensorUpdateTimerCallback(object state)
		{
			try
			{
				UnitsNet.Temperature temperature = bme280Sensor.Temperature;
				double humidity = bme280Sensor.Humidity;
				UnitsNet.Pressure airPressure = bme280Sensor.Pressure;

				Debug.WriteLine($"{DateTime.UtcNow.ToLongTimeString()} Timer triggered " +
							$"Temperature: {temperature.DegreesCelsius:0.0}°C {temperature.DegreesFahrenheit:0.0}°F " +
							$"Humidity: {humidity:0.0}% " +
							$"AirPressure: {airPressure.Kilopascals:0.000}KPa ");

				SensorPayloadDto sensorPayload = new SensorPayloadDto()
				{
					Temperature = temperature.DegreesCelsius,
					Humidity = humidity,
					AirPressure = airPressure.Kilopascals,
				};

				string payloadText = JsonConvert.SerializeObject(sensorPayload);

				using (var message = new Message(Encoding.ASCII.GetBytes(payloadText)))
				{
					Debug.WriteLine("AzureIoTHubClient SendEventAsync starting");
					await azureIoTHubClient.SendEventAsync(message);
					Debug.WriteLine("AzureIoTHubClient SendEventAsync finished");
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"Retrieving or sending sensor values failed:{ex.Message}");
			}
		}
	}

	public sealed class SensorPayloadDto
	{
		public double Temperature { get; set; }

		public double Humidity { get; set; }

		public double AirPressure { get; set; }
	}
}
