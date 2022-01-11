﻿using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcDotNetNamedPipes;
using Lively.Common;
using Lively.Common.API;
using Lively.Common.Helpers.Storage;
using Lively.Grpc.Common.Proto.Desktop;
using Lively.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lively.Grpc.Client
{
    //TODO: don't catch exceptions, just throw.
    public class WinDesktopCoreClient : IDesktopCoreClient
    {
        public event EventHandler WallpaperChanged;

        private readonly List<WallpaperData> wallpapers = new List<WallpaperData>(2);
        public ReadOnlyCollection<WallpaperData> Wallpapers => wallpapers.AsReadOnly();

        private readonly DesktopService.DesktopServiceClient client;
        private readonly SemaphoreSlim wallpaperChangedLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource cancellationTokeneWallpaperChanged;
        private readonly Task wallpaperChangedTask;
        private bool disposedValue;

        public WinDesktopCoreClient()
        {
            client = new DesktopService.DesktopServiceClient(new NamedPipeChannel(".", Constants.SingleInstance.GrpcPipeServerName));

            Task.Run(async () =>
            {
                wallpapers.AddRange(await GetWallpapers());
            }).Wait();

            cancellationTokeneWallpaperChanged = new CancellationTokenSource();
            wallpaperChangedTask = Task.Run(() => SubscribeWallpaperChangedStream(cancellationTokeneWallpaperChanged.Token));
        }

        public async Task SetWallpaper(string livelyInfoPath, string monitorId)
        {
            var request = new SetWallpaperRequest
            {
                LivelyInfoPath = livelyInfoPath,
                MonitorId = monitorId,
            };
            _ = await client.SetWallpaperAsync(request);
        }

        public async Task SetWallpaper(ILibraryModel item, IDisplayMonitor display) =>
            await SetWallpaper(item.LivelyInfoFolderPath, display.DeviceId);

        private async Task<List<WallpaperData>> GetWallpapers()
        {
            var resp = new List<GetWallpapersResponse>();
            using var call = client.GetWallpapers(new Empty());
            while (await call.ResponseStream.MoveNext())
            {
                var response = call.ResponseStream.Current;
                resp.Add(response);
            }

            var wallpapers = new List<WallpaperData>();
            foreach (var item in resp)
            {
                wallpapers.Add(new WallpaperData()
                {
                    LivelyInfoFolderPath = item.LivelyInfoPath,
                    LivelyPropertyCopyPath = item.PropertyCopyPath,
                    PreviewPath = item.PreviewPath,
                    ThumbnailPath = item.ThumbnailPath,
                    Display = new DisplayMonitor()
                    {
                        DeviceId = item.Screen.DeviceId,
                        DisplayName = item.Screen.DisplayName,
                        DeviceName = item.Screen.DeviceName,
                        HMonitor = new IntPtr(item.Screen.HMonitor),
                        IsPrimary = item.Screen.IsPrimary,
                        Index = item.Screen.Index,
                        Bounds = new System.Drawing.Rectangle(
                        item.Screen.Bounds.X,
                        item.Screen.Bounds.Y,
                        item.Screen.Bounds.Width,
                        item.Screen.Bounds.Height),
                        WorkingArea = new System.Drawing.Rectangle(
                        item.Screen.WorkingArea.X,
                        item.Screen.WorkingArea.Y,
                        item.Screen.WorkingArea.Width,
                        item.Screen.WorkingArea.Height),

                    },
                });
            }
            return wallpapers;
        }

        public async Task CloseAllWallpapers(bool terminate = false)
        {
            await client.CloseAllWallpapersAsync(new CloseAllWallpapersRequest() { Terminate = terminate });
        }

        public async Task CloseWallpaper(WallpaperType type, bool terminate = false)
        {
            await client.CloseWallpaperCategoryAsync(new CloseWallpaperCategoryRequest()
            {
                Category = (WallpaperCategory)((int)type),
                Terminate = terminate 
                
            });
        }

        public async Task CloseWallpaper(ILibraryModel item, bool terminate = false)
        {
            await client.CloseWallpaperLibraryAsync(new CloseWallpaperLibraryRequest()
            {
                LivelyInfoPath = item.LivelyInfoFolderPath,
                Terminate = terminate
            });
        }

        public async Task CloseWallpaper(IDisplayMonitor monitor, bool terminate = false)
        {
            await client.CloseWallpaperMonitorAsync(new CloseWallpaperMonitorRequest()
            {
                MonitorId = monitor.DeviceId,
                Terminate = terminate
            });
        }

        public void SendMessageWallpaper(ILibraryModel obj, IpcMessage msg)
        {
            client.SendMessageWallpaper(new WallpaperMessageRequest()
            {
                MonitorId = string.Empty,
                LivelyInfoPath = obj.LivelyInfoFolderPath,
                Msg = JsonUtil.Serialize(msg),
            });
        }

        public void SendMessageWallpaper(IDisplayMonitor display, ILibraryModel obj, IpcMessage msg)
        {
            client.SendMessageWallpaper(new WallpaperMessageRequest()
            {
                MonitorId = display.DeviceId,
                LivelyInfoPath = obj.LivelyInfoFolderPath,
                Msg = JsonUtil.Serialize(msg),
            });
        }

        private async Task SubscribeWallpaperChangedStream(CancellationToken token)
        {
            try
            {
                using var call = client.SubscribeWallpaperChanged(new Empty());
                while (await call.ResponseStream.MoveNext(token))
                {
                    await wallpaperChangedLock.WaitAsync();
                    try
                    {
                        var response = call.ResponseStream.Current;

                        wallpapers.Clear();
                        wallpapers.AddRange(await GetWallpapers());
                        WallpaperChanged?.Invoke(this, EventArgs.Empty);
                    }
                    finally
                    {
                        wallpaperChangedLock.Release();
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public async Task ShutDown()
        {
            await client.ShutDownAsync(new Empty());
        }

        #region dispose

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    cancellationTokeneWallpaperChanged?.Cancel();
                    wallpaperChangedTask?.Wait(100);
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~DesktopServiceClient()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion //dispose
    }
}