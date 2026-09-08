using System;
using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using SP_CopyFromRSN.Models;
using SP_CopyFromRSN.Revit;

namespace SP_CopyFromRSN.App
{
    /// <summary>Точка входа add-in: лента, ExternalEvent. Расписание — таймер в открытом окне.</summary>
    public class CopyFromRsnApp : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                CopyFromRsnRuntime.RevitVersion = application.ControlledApplication.VersionNumber;
                CopyFromRsnRuntime.ReloadActiveProfile();
                CopyFromRsnRuntime.EnsureExternalEvent();

                RegisterRibbon(application);
                CopyFromRsnOpenWarningSuppression.Attach(application);

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("SP CopyFromRSN", "Ошибка запуска: " + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            CopyFromRsnOpenWarningSuppression.Detach(application);
            return Result.Succeeded;
        }

        private static void RegisterRibbon(UIControlledApplication application)
        {
            string assemblyPath = Assembly.GetExecutingAssembly().Location;
            // Отдельная надстройка — на вкладке «Надстройки», не на SP
            RibbonPanel panel = application.CreateRibbonPanel(Tab.AddIns, "Копирование RSN");

            var buttonData = new PushButtonData(
                "SP_CopyFromRSN",
                "Копирование\nс RSN",
                assemblyPath,
                typeof(CopyFromRsnCommand).FullName);

            // LargeImage = 32×32, Image = 16×16 (как в SP_Addin / IfcExportAsClassifier)
            buttonData.LargeImage = LoadEmbeddedPng("SP_CopyFromRSN.Assets.icon32.png", 32);
            buttonData.Image = LoadEmbeddedPng("SP_CopyFromRSN.Assets.icon16.png", 16);
            buttonData.ToolTip = "Копирование моделей с Revit Server Network";

            panel.AddItem(buttonData);
        }

        /// <summary>
        /// Загрузка PNG из embedded resource.
        /// OnLoad + Freeze — иначе после Dispose потока LargeImage «пропадает»
        /// и Revit подставляет увеличенную Image (16×16) — выглядит «размазанно».
        /// DecodePixel* фиксирует размер, чтобы WPF/Revit не масштабировали по DPI.
        /// </summary>
        private static ImageSource? LoadEmbeddedPng(string resourceName, int decodePixels)
        {
            try
            {
                using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null)
                    return null;

                // Копируем в память: исходный stream нельзя держать после выхода из using
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                memory.Position = 0;

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.DecodePixelWidth = decodePixels;
                image.DecodePixelHeight = decodePixels;
                image.StreamSource = memory;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        public static void RaiseCopy(CopyFromRsnProfile profile) =>
            CopyFromRsnRuntime.RaiseCopy(profile);
    }
}
