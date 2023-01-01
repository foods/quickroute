using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using QuickRoute.BusinessEntities.Importers;
using QuickRoute.BusinessEntities.Importers.FIT;
using QuickRoute.BusinessEntities.Importers.FRWD;
using QuickRoute.BusinessEntities.Importers.Navilock;
using QuickRoute.BusinessEntities.Importers.Polar.ProTrainer;
using QuickRoute.BusinessEntities.Importers.GPX;
using QuickRoute.BusinessEntities.Importers.QuickRoute;
using QuickRoute.BusinessEntities.Importers.TCX;
using QuickRoute.Resources;
using Wintellect.PowerCollections;

namespace QuickRoute.BusinessEntities.Importers
{
  public delegate void RefreshProgressDelegate(string message, int percent);
  public delegate void RefreshCompletedDelegate();

  public static class SupportedImportFormatManager
  {
    private static Thread _thread;
    public static event RefreshProgressDelegate RefreshProgressChanged;
    public static event RefreshCompletedDelegate RefreshCompleted;

    public static List<GPSDevice> GetSupportedGPSDevices()
    {
      var supportedGPSDevices = new List<GPSDevice>();
      return supportedGPSDevices;
    }

    public static List<RouteFileFormat> GetSupportedRouteFileFormats()
    {
      var supportedRouteFileFormats = new List<RouteFileFormat>();
      supportedRouteFileFormats.Add(
        new RouteFileFormat(Strings.FileFilter_GpxFiles, new GPXImporter()));

      supportedRouteFileFormats.Add(
        new RouteFileFormat(Strings.FileFilter_AllQuickRouteFiles, new QuickRouteImporter()));

      supportedRouteFileFormats.Add(
        new RouteFileFormat(Strings.FileFilter_FitFiles, new FITImporter()));

      supportedRouteFileFormats.Add(
        new RouteFileFormat(Strings.FileFilter_TcxFiles, new TCXImporter()));

      supportedRouteFileFormats.Add(
        new RouteFileFormat(Strings.FileFilter_FrwdTxtFiles, new FRWDImporter()));

      supportedRouteFileFormats.Add(
        new RouteFileFormat(Strings.FileFilter_NavilockActFiles, new NavilockImporter()));

      return supportedRouteFileFormats;
    }

    public static List<FileFormat> GetSupportedMapFileFormats()
    {
      var supportedMapImageFileFormats = new List<FileFormat>();
      supportedMapImageFileFormats.Add(
        new FileFormat(Strings.FileFilter_ImageFiles));
      supportedMapImageFileFormats.Add(
        new FileFormat(Strings.FileFilter_QuickRouteFiles));
      supportedMapImageFileFormats.Add(
        new FileFormat(Strings.FileFilter_KmzFiles));
      return supportedMapImageFileFormats;
    }

    public static List<GPSDevice> SearchForGPSDevices()
    {
      return SearchForGPSDevices(GetSupportedGPSDevices());
    }

    public static List<GPSDevice> SearchForGPSDevices(List<GPSDevice> devicesToSearchFor)
    {
      var foundDevices = new List<GPSDevice>();
      foreach (var device in devicesToSearchFor)
      {
        if (device.Importer.IsConnected || device.Importer.CachedDataExists)
        {
          foundDevices.Add(device);
        }
      }
      return foundDevices;
    }

    public static void StopRefreshThread()
    {
      try
      {
        if (_thread != null && _thread.IsAlive)
        {
          _thread.Abort();
        }
      }
      catch (ThreadAbortException)
      {
        if (_thread != null)
        {
          while (_thread.ThreadState == ThreadState.Running)
          {
          }
        }
        _thread = null;
      }
    }

    private static void StartReadThread(ThreadStart threadStartMethod)
    {
      StopRefreshThread();
      var myThread = new ThreadStart(threadStartMethod);
      _thread = new Thread(myThread);
      _thread.Start();
    }

    public static void StartRefreshGPSDevices()
    {
      StartReadThread(RefreshGPSDevices);
    }

    private static void RefreshGPSDevices()
    {
      List<GPSDevice> devices = GetSupportedGPSDevices();
      for (int i = 0; i < devices.Count; i++)
      {
        var device = devices[i];
        var percent = ((i / (Single)devices.Count) * 100);
        if (percent > 100)
        {
          percent = 100;
        }
        if (RefreshProgressChanged != null)
        {
          RefreshProgressChanged(String.Format("Looking for supported devices... ({0}/{1})", i, devices.Count), (int)percent);
        }
        device.Importer.Refresh();
      }
      if (RefreshCompleted != null)
      {
        RefreshCompleted();
      }
    }

    private static string GetProgramFilesPath()
    {
      if (IntPtr.Size == 8 || (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"))))
      {
        return Environment.GetEnvironmentVariable("ProgramFiles(x86)");
      }
      return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    }

  }
}
