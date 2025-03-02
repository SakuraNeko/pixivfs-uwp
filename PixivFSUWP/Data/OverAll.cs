﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.UserActivities;
using Windows.Security.Credentials;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;
using AdaptiveCards;
using Windows.UI.Shell;
using PixivCS;
using Windows.Data.Json;
using System.Linq;
using PixivFSUWP.Data.Collections;

namespace PixivFSUWP.Data
{
    public static class OverAll
    {
        public static Uri AppUri = null;
        public static PixivBaseAPI GlobalBaseAPI = new PixivBaseAPI();
        public const string passwordResource = "PixivFSUWPPassword";
        public const string refreshTokenResource = "PixivFSUWPRefreshToken";
        public static CurrentUser currentUser = null;
        public static RecommendIllustsCollection RecommendList { get; private set; }
        public static BookmarkIllustsCollection BookmarkList { get; private set; }
        public static FollowingIllustsCollection FollowingList { get; private set; }
        public static RankingIllustsCollection RankingList { get; private set; }

        public static void RefreshRecommendList()
        {
            RecommendList?.StopLoading();
            RecommendList = new RecommendIllustsCollection();
        }

        public static void RefreshBookmarkList()
        {
            BookmarkList?.StopLoading();
            BookmarkList = new BookmarkIllustsCollection();
        }

        public static void RefreshFollowingList()
        {
            FollowingList?.StopLoading();
            FollowingList = new FollowingIllustsCollection();
        }

        public static void RefreshRankingList()
        {
            RankingList?.StopLoading();
            RankingList = new RankingIllustsCollection();
        }

        //携带缓存的图像下载
        public static async Task<MemoryStream> DownloadImage(string Uri)
        {
            var tmpFileName = Uri.Split('/').Last();
            var cachedFile = await CacheManager.GetCachedFileAsync(tmpFileName);
            if (cachedFile == null)
            {
                //没有对应的缓存文件
                using (var resStream = await (await new PixivAppAPI(GlobalBaseAPI).RequestCall("GET",
                      Uri, new Dictionary<string, string>() { { "Referer", "https://app-api.pixiv.net/" } })).
                      Content.ReadAsStreamAsync())
                {
                    var memStream = new MemoryStream();
                    await resStream.CopyToAsync(memStream);
                    memStream.Position = 0;
                    var newCachedFile = await CacheManager.CreateCacheFileAsync(tmpFileName);
                    using (var fileStream = await newCachedFile.Value.File.OpenStreamForWriteAsync())
                        await memStream.CopyToAsync(fileStream);
                    await CacheManager.FinishCachedFileAsync(newCachedFile.Value, true);
                    memStream.Position = 0;
                    return memStream;
                }
            }
            else
            {
                //有缓存文件
                using (var fileStream = await cachedFile.OpenStreamForReadAsync())
                {
                    var memStream = new MemoryStream();
                    await fileStream.CopyToAsync(memStream);
                    memStream.Position = 0;
                    return memStream;
                }
            }
        }

        public static async Task<string> GetDataUri(string Uri)
        {
            return string.Format("data:image/png;base64,{0}", Convert.ToBase64String((await DownloadImage(Uri)).ToArray()));
        }

        public static async Task<BitmapImage> LoadImageAsync(string Uri)
        {
            var toret = new BitmapImage();
            using (var memStream = await DownloadImage(Uri))
                await toret.SetSourceAsync(memStream.AsRandomAccessStream());
            return toret;
        }

        public static async Task<WriteableBitmap> LoadImageAsync(string Uri, int Width, int Height)
        {
            var toret = new WriteableBitmap(Width, Height);
            using (var memStream = await DownloadImage(Uri))
                await toret.SetSourceAsync(memStream.AsRandomAccessStream());
            return toret;
        }

        public static async Task<byte[]> ImageToBytes(WriteableBitmap Source)
        {
            byte[] toret;
            using (var stream = Source.PixelBuffer.AsStream())
            {
                toret = new byte[stream.Length];
                await stream.ReadAsync(toret, 0, toret.Length);
            }
            return toret;
        }

        public static async Task<WriteableBitmap> BytesToImage(byte[] Source, int Width, int Height)
        {
            WriteableBitmap toret = new WriteableBitmap(Width, Height);
            using (var stream = toret.PixelBuffer.AsStream())
            {
                await stream.WriteAsync(Source, 0, Source.Length);
            }
            return toret;
        }

        //展示一个新的窗口
        public static async Task ShowNewWindow(Type Page, object Parameter)
        {
            CoreApplicationView newView = CoreApplication.CreateNewView();
            int newViewId = 0;
            await newView.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Frame frame = new Frame();
                frame.Navigate(Page, Parameter);
                Window.Current.Content = frame;
                Window.Current.Activate();
                newViewId = ApplicationView.GetForCurrentView().Id;
            });
            await ApplicationViewSwitcher.TryShowAsStandaloneAsync(newViewId);
        }

        //从Vault中获取身份信息
        //此版本只储存一个，未来可以储存多到20个
        public static PasswordCredential GetCredentialFromLocker(string resourceName)
        {
            PasswordCredential credential = null;
            var vault = new PasswordVault();
            try
            {
                var credentialList = vault.FindAllByResource(resourceName);
                if (credentialList.Count > 0) credential = credentialList[0];
            }
            catch { }
            return credential;
        }

        static UserActivitySession _currentActivity;

        //时间线支持
        public static async Task GenerateActivityAsync(string DisplayText, AdaptiveCard Card, Uri ActivationUri, string ActivityID)
        {
            UserActivityChannel channel = UserActivityChannel.GetDefault();
            UserActivity userActivity = await channel.GetOrCreateUserActivityAsync(ActivityID);
            userActivity.VisualElements.DisplayText = DisplayText;
            userActivity.VisualElements.Content = AdaptiveCardBuilder.CreateAdaptiveCardFromJson(Card.ToJson());
            userActivity.ActivationUri = ActivationUri;
            await userActivity.SaveAsync();
            _currentActivity?.Dispose();
            _currentActivity = userActivity.CreateSession();
        }

        //扩展方法，用于检测值为null的情况
        public static string TryGetString(this IJsonValue source)
        {
            if (source.ValueType == JsonValueType.Null)
                return "";
            return source.GetString();
        }

        public static string GetResourceString(string ID)
        {
            var resourceLoader = Windows.ApplicationModel.Resources.ResourceLoader.GetForCurrentView();
            return resourceLoader.GetString(ID);
        }
    }
}
