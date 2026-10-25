using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.CSharp.RuntimeBinder;

namespace GlassDesk.Platform;

public static class FanSpeedReader
{
	private sealed record FanHelperSnapshot(int Version, DateTimeOffset TimestampUtc, int? Rpm);

	private const int MinimumPlausibleRpm = 100;

	private const int MaximumPlausibleRpm = 30000;

	private static readonly TimeSpan HelperFreshness = TimeSpan.FromSeconds(20L);

	private static readonly JsonSerializerOptions HelperJsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true
	};

	private static readonly string[] HardwareMonitorNamespaces = new string[3] { "root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor", "root\\Hardware" };

	public static int? ReadRpm()
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}
		int? result = ReadHelperRpm(GetHelperStatusPath(), DateTimeOffset.UtcNow);
		if (result.HasValue)
		{
			return result;
		}
		string[] hardwareMonitorNamespaces = HardwareMonitorNamespaces;
		for (int i = 0; i < hardwareMonitorNamespaces.Length; i++)
		{
			int? result2 = ReadHardwareMonitorRpm(hardwareMonitorNamespaces[i]);
			if (result2.HasValue)
			{
				return result2;
			}
		}
		return null;
	}

	public static bool IsPlausibleRpm(double value)
	{
		if (double.IsFinite(value))
		{
			if (value >= 100.0)
			{
				return value <= 30000.0;
			}
			return false;
		}
		return false;
	}

	public static int? ReadHelperRpm(string path, DateTimeOffset utcNow)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
			{
				return null;
			}
			using FileStream utf8Json = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			FanHelperSnapshot fanHelperSnapshot = JsonSerializer.Deserialize<FanHelperSnapshot>(utf8Json, HelperJsonOptions);
			if ((object)fanHelperSnapshot != null && fanHelperSnapshot.Version == 1)
			{
				int? rpm = fanHelperSnapshot.Rpm;
				if (rpm.HasValue)
				{
					int valueOrDefault = rpm.GetValueOrDefault();
					TimeSpan timeSpan = utcNow - fanHelperSnapshot.TimestampUtc;
					if (timeSpan < TimeSpan.FromSeconds(-5L) || timeSpan > HelperFreshness)
					{
						return null;
					}
					return IsPlausibleRpm(valueOrDefault) ? new int?(valueOrDefault) : ((int?)null);
				}
			}
			return null;
		}
		catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is NotSupportedException) ? true : false)
		{
			return null;
		}
	}

	private static string GetHelperStatusPath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GlassDesk", "FanSensor", "fan-status.json");
	}

	private static int? ReadHardwareMonitorRpm(string wmiNamespace)
	{
		object obj = null;
		object obj2 = null;
		object obj3 = null;
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
			if ((object)typeFromProgID == null)
			{
				return null;
			}
			obj = Activator.CreateInstance(typeFromProgID);
			if (obj == null)
			{
				return null;
			}
			dynamic val = obj;
			obj2 = val.ConnectServer(".", wmiNamespace);
			dynamic val2 = obj2;
			obj3 = val2.ExecQuery("SELECT Name, SensorType, Value, Identifier FROM Sensor WHERE SensorType = 'Fan'", "WQL", 48);
			int num = 0;
			if (!(obj3 is IEnumerable enumerable))
			{
				return null;
			}
			foreach (object item in enumerable)
			{
				try
				{
					dynamic val3 = item;
					dynamic val4 = Convert.ToDouble(val3.Value, CultureInfo.InvariantCulture);
					if (!((!FanSpeedReader.IsPlausibleRpm(val4)) ? true : false))
					{
						num = Math.Max(num, (int)Math.Round(val4));
					}
				}
				catch (Exception ex) when ((ex is COMException || ex is InvalidCastException || ex is FormatException || ex is OverflowException || ex is RuntimeBinderException) ? true : false)
				{
				}
				finally
				{
					ReleaseCom(item);
				}
			}
			return (num > 0) ? new int?(num) : ((int?)null);
		}
		catch (Exception ex2) when ((ex2 is COMException || ex2 is UnauthorizedAccessException || ex2 is InvalidCastException || ex2 is RuntimeBinderException) ? true : false)
		{
			return null;
		}
		finally
		{
			ReleaseCom(obj3);
			ReleaseCom(obj2);
			ReleaseCom(obj);
		}
	}

	private static void ReleaseCom(object? value)
	{
		if (value != null && Marshal.IsComObject(value))
		{
			try
			{
				Marshal.FinalReleaseComObject(value);
			}
			catch (InvalidComObjectException)
			{
			}
		}
	}
}
